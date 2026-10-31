# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
"""関連 Issue 検知の試作 (issue #1382)。本番の bot とは別で、Issue には何も書かない。

  python3 tools/related-issues/poc.py fetch    作業フォルダー   # 誤判定の Issue と timeline を gh api で取る
  python3 tools/related-issues/poc.py extract  作業フォルダー   # 本文から 打ったもの・出たもの・期待 を取り出す
  python3 tools/related-issues/poc.py sign     作業フォルダー   # 判定エンジンで判定理由を出す (dotnet が要る)
  python3 tools/related-issues/poc.py eval     作業フォルダー   # 方式ごとの Precision / Recall / 誤検出

正解: 同じ PR (直した Issue が MAX_GROUP 件以下のもの) で閉じた Issue の組を「関連」、
どちらも PR で閉じていて共通の PR が無い組を「無関係」とする。大きな PR だけを共有する組は数えない。
labels.tsv (番号 TAB 番号 TAB 1|0) があれば、人が付けた正解で上書きする。
"""
import collections, concurrent.futures, json, math, os, random, re, subprocess, sys

REPO = "yksr-melt/Meltype"
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MAX_GROUP = 5


def gh(path, jq, **params):
    fields = [a for k, v in params.items() for a in ("-f", f"{k}={v}")]
    return subprocess.run(["gh", "api", "-X", "GET", path, *fields, "--jq", jq], capture_output=True, text=True, check=True).stdout


def fetch(work):
    with open(f"{work}/issues.jsonl", "w") as out:
        for page in range(1, 50):
            text = gh(f"repos/{REPO}/issues", ".[] | select(.pull_request == null) | {number,title,body,state,labels:[.labels[].name]}",
                      labels="誤判定", state="all", per_page=100, page=page)
            if not text.strip():
                break
            out.write(text)
    issues = [json.loads(l) for l in open(f"{work}/issues.jsonl")]

    def timeline(n):
        return n, json.loads(gh(f"repos/{REPO}/issues/{n}/timeline?per_page=100",
                                "[.[]|{e:.event,c:.commit_id,src:(.source.issue.number//null),pr:(.source.issue.pull_request!=null),merged:(.source.issue.pull_request.merged_at//null)}]"))
    with concurrent.futures.ThreadPoolExecutor(8) as ex:
        json.dump(dict(ex.map(timeline, [i["number"] for i in issues])), open(f"{work}/timeline.json", "w"))


def section(body, name):
    m = re.search(r"###\s*" + re.escape(name) + r"[^\n]*\n+(.*?)(?=\n###|\Z)", body or "", re.S)
    if not m:
        return None
    value = m.group(1).strip().strip("`").strip()
    return None if value in ("", "_No response_") else value


def extract(work):
    rows = []
    for issue in map(json.loads, open(f"{work}/issues.jsonl")):
        body = issue["body"] or ""
        typed = section(body, "打ったもの")
        rows.append({
            "number": issue["number"], "title": issue["title"], "body": body,
            "typed": typed.splitlines()[0] if typed else None,
            "actual": section(body, "出たもの"), "expected": section(body, "期待した結果"),
            "kind": section(body, "種類"), "version": section(body, "Meltype の版"),
        })
    json.dump(rows, open(f"{work}/cases.json", "w"), ensure_ascii=False)
    with open(f"{work}/typed.tsv", "w") as out:
        for r in rows:
            if r["typed"]:
                out.write(f"{r['number']}\t{r['typed']}\n")
    print(f"{len(rows)} 件中 {sum(1 for r in rows if r['typed'])} 件で打ったものが取れた")


def sign(work, project=f"{ROOT}/src/Meltype.Core.Tests"):
    subprocess.run(["dotnet", "run", "-c", "Release", "--project", project, "--", "--related-signature",
                    f"{work}/typed.tsv", f"{work}/signature.jsonl"], check=True)


# ---- 正解 ----

def fixes(work):
    timeline = json.load(open(f"{work}/timeline.json"))
    cache = {}

    def pr_of(commit):
        if commit not in cache:
            s = subprocess.run(["git", "-C", ROOT, "log", "-1", "--format=%s", commit], capture_output=True, text=True).stdout
            m = re.search(r"\(#(\d+)\)\s*$", s)
            cache[commit] = int(m.group(1)) if m else (f"commit:{commit[:8]}" if s else None)
        return cache[commit]
    out = {}
    for n, events in timeline.items():
        prs = {pr_of(e["c"]) for e in events if e["e"] == "closed" and e["c"]} - {None}
        if not prs:
            prs = {e["src"] for e in events if e["e"] == "cross-referenced" and e["pr"] and e["merged"]}
        out[int(n)] = prs
    return out


def truth(work, numbers):
    fix = fixes(work)
    groups = collections.defaultdict(set)
    for n, prs in fix.items():
        for p in prs:
            groups[p].add(n)
    labels = {}
    for a in numbers:
        for b in numbers:
            if a >= b or not fix.get(a) or not fix.get(b):
                continue
            shared = fix[a] & fix[b]
            if any(len(groups[p]) <= MAX_GROUP for p in shared):
                labels[(a, b)] = 1
            elif not shared:
                labels[(a, b)] = 0
    path = f"{work}/labels.tsv"
    if os.path.exists(path):
        for line in open(path):
            if line.strip() and not line.startswith("#"):
                a, b, y = line.split("\t")[:3]
                a, b = sorted((int(a), int(b)))
                labels[(a, b)] = int(y)
    return labels


# ---- 方式 ----

def jaccard(x, y):
    return len(x & y) / len(x | y) if x and y else 0.0


def reason_key(reason):
    return re.sub(r"「[^」]*」", "「…」", reason)


def latin(s):
    return sum(c.isascii() and c.isalpha() for c in s or "")


def direction(case):
    """報告の向き: 英字にしたかったのにかな (en)、かなにしたかったのに英字 (ja)、どちらも同じ字種で変換の違い (kanji)。"""
    a, e = latin(case["actual"]), latin(case["expected"])
    return "en" if e > a else "ja" if a > e else "kanji"


def features(case, sig):
    words = sig["words"] if sig else []
    contributions = [c for w in words for c in w["contributions"]]
    chosen = [l for l in (sig["lattice"] if sig else []) if l.lstrip().startswith("*")]
    edges = [re.sub(r"[:=].*", "", t) for l in chosen for t in l.split(":", 1)[1].split()] if chosen else []
    return {
        "source": {c["source"] for c in contributions},
        "reason": {(c["source"], reason_key(c["reason"])) for c in contributions},
        "verdict": {(w["verdict"], tuple(sorted({c["source"] for c in w["contributions"]}))) for w in words},
        "lattice": {" ".join(edges[i:i + 2]) for i in range(len(edges))} | {f"dir:{direction(case)}"},
    }


def ngrams(s, n=3):
    s = re.sub(r"\s+", " ", (s or "").lower())
    return {s[i:i + n] for i in range(len(s) - n + 1)}


class Bm25:
    def __init__(self, docs):
        self.docs = docs
        self.avg = sum(map(len, docs.values())) / len(docs)
        df = collections.Counter(t for d in docs.values() for t in set(d))
        self.idf = {t: math.log(1 + (len(docs) - f + 0.5) / (f + 0.5)) for t, f in df.items()}

    def score(self, a, b, k=1.2, bb=0.75):
        tf = collections.Counter(self.docs[b])
        norm = k * (1 - bb + bb * len(self.docs[b]) / self.avg)
        return sum(self.idf[t] * tf[t] * (k + 1) / (tf[t] + norm) for t in set(self.docs[a]) if t in tf)


def bigrams(s):
    s = re.sub(r"###[^\n]*|_No response_|\s+", " ", (s or "").lower())
    return [s[i:i + 2] for i in range(len(s) - 1) if s[i:i + 2].strip()]


def evaluate(work):
    cases = {c["number"]: c for c in json.load(open(f"{work}/cases.json")) if c["typed"]}
    sigs = {int(s["id"]): s for s in map(json.loads, open(f"{work}/signature.jsonl"))}
    feats = {n: features(c, sigs.get(n)) for n, c in cases.items()}
    labels = truth(work, sorted(cases))
    bm = Bm25({n: bigrams(c["title"] + " " + c["body"]) for n, c in cases.items()})
    title = {n: ngrams(c["title"]) for n, c in cases.items()}

    methods = {
        "Source の一致 (Jaccard)": lambda a, b: jaccard(feats[a]["source"], feats[b]["source"]),
        "Source+Reason の一致": lambda a, b: jaccard(feats[a]["reason"], feats[b]["reason"]),
        "語の判定+Source の一致": lambda a, b: jaccard(feats[a]["verdict"], feats[b]["verdict"]),
        "ラティスの道+向き": lambda a, b: jaccard(feats[a]["lattice"], feats[b]["lattice"]),
        "文字列 (題の 3-gram)": lambda a, b: jaccard(title[a], title[b]),
        "BM25 (本文の 2-gram)": lambda a, b: (bm.score(a, b) + bm.score(b, a)) / 2,
    }
    pos = sum(labels.values())
    print(f"打ったものが取れた Issue {len(cases)} 件、判定理由 {len(sigs)} 件、正解の組 {len(labels)} (関連 {pos} / 無関係 {len(labels) - pos})")
    report = {"cases": len(cases), "pairs": len(labels), "positives": pos, "methods": {}}
    for name, f in methods.items():
        values = [(f(a, b), y) for (a, b), y in labels.items()]
        top = max(v for v, _ in values)
        ties = sum(1 for v, _ in values if v == top)
        # 同点は順番に意味が無いので、ランダムに並べた 20 回の平均をとる
        runs = []
        for seed in range(20):
            shuffled = values[:]
            random.Random(seed).shuffle(shuffled)
            runs.append(sorted(shuffled, key=lambda x: -x[0]))
        res = {f"P@{k}": sum(sum(y for _, y in r[:k]) for r in runs) / (k * len(runs)) for k in (10, 25, 50)}
        res["ap"] = sum(map(ap, runs)) / len(runs)
        best = max(((t, *pr(values, t)) for t in sorted({v for v, _ in values}) if t > 0), key=lambda x: x[3], default=(0, 0, 0, 0, 0))
        res.update(threshold=best[0], precision=best[1], recall=best[2], f1=best[3], fpr=best[4], ties_at_top=ties,
                   hit1=hit_at_1(f, labels, cases))
        report["methods"][name] = res
        print(f"{name:20} AP={res['ap']:.3f} P@10={res['P@10']:.2f} P@25={res['P@25']:.2f} P@50={res['P@50']:.2f} 1位が関連={res['hit1']:.2f} "
              f"最良F1={best[3]:.3f} (しきい値 {best[0]:.2f}: P={best[1]:.3f} R={best[2]:.3f} 誤検出率={best[4]:.4f}) 最高点の同点 {ties} 組")
    json.dump(report, open(f"{work}/report.json", "w"), ensure_ascii=False, indent=1)
    return feats, labels, methods


def hit_at_1(f, labels, cases):
    """関連の Issue が先にある Issue について、先の Issue (正解の付いたもの) の中で 1 位の候補が関連か (同点はランダムの期待値)。"""
    by = collections.defaultdict(list)
    for (a, b), y in labels.items():
        by[b].append((f(a, b), y))
    hits = []
    for b, cands in by.items():
        if not any(y for _, y in cands):
            continue
        top = max(v for v, _ in cands)
        best = [y for v, y in cands if v == top]
        hits.append(sum(best) / len(best))
    return sum(hits) / max(len(hits), 1)


def pr(scored, t):
    tp = sum(y for s, y in scored if s >= t)
    fp = sum(1 - y for s, y in scored if s >= t)
    pos = sum(y for _, y in scored)
    neg = len(scored) - pos
    p, r = tp / max(tp + fp, 1), tp / max(pos, 1)
    return p, r, 2 * p * r / max(p + r, 1e-9), fp / max(neg, 1)


def ap(scored):
    hits = total = 0
    for i, (_, y) in enumerate(scored, 1):
        if y:
            hits += 1
            total += hits / i
    return total / max(hits, 1)


if __name__ == "__main__":
    step, work = sys.argv[1], sys.argv[2]
    os.makedirs(work, exist_ok=True)
    {"fetch": fetch, "extract": extract, "sign": sign, "eval": evaluate}[step](work)
