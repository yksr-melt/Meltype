# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
"""関連 Issue 検知の試作 (issue #1382) の 2 つ目: 「同じ直し方で直る Issue」を、PR の前後で挙動が変わるかで探す。

  python3 tools/related-issues/impact.py build 作業フォルダー   # 誤判定を直した PR ごとに、マージの前と後で全 Issue の入力を打つ
  python3 tools/related-issues/impact.py eval  作業フォルダー   # PR 用 (その PR で変わる Issue) とトリアージ用 (新しい Issue に近い Issue) の精度
  python3 tools/related-issues/impact.py triage 作業フォルダー [番号 …]  # 報告より前の PR で、その入力が期待どおりになったもの (再発の候補)

poc.py の fetch / extract を先に済ませておく。PR の情報は gh api、古い版のビルドは git worktree と dotnet を使う。
古い版にも ImpactBatch.cs.txt を入れて、--repro を 1 回の起動でまとめて流す。
"""
import collections, concurrent.futures, json, os, random, re, shutil, subprocess, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import poc  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


def last_key(body):
    m = re.search(r"###\s*最後に押したキー\s*\n+([^\n]*)", body or "")
    return "space" if m and "Space" in m.group(1) else "enter"


def git(*args):
    return subprocess.run(["git", "-C", poc.ROOT, *args], capture_output=True, text=True, check=True).stdout.strip()


def pr_commits(work):
    path = f"{work}/prinfo.json"
    if not os.path.exists(path):
        prs = sorted({p for v in poc.fixes(work).values() for p in v if isinstance(p, int)})

        def get(n):
            return json.loads(poc.gh(f"repos/{poc.REPO}/pulls/{n}", "{n:.number,merged:.merged_at,sha:.merge_commit_sha,base:.base.ref}"))
        with concurrent.futures.ThreadPoolExecutor(8) as ex:
            json.dump(list(ex.map(get, prs)), open(path, "w"))
    out = {}
    for pr in json.load(open(path)):
        if pr.get("merged") and pr.get("base") == "main":
            try:
                out[pr["n"]] = (git("rev-parse", pr["sha"] + "^1"), pr["sha"])
            except subprocess.CalledProcessError:
                pass  # main に無い (履歴が浅い・書き換えられた)
    return out


def run_commit(work, sha, inputs):
    out = f"{work}/outputs/{sha}.tsv"
    if os.path.exists(out):
        return out
    tree = f"{work}/trees/{sha[:12]}"
    shutil.rmtree(tree, ignore_errors=True)
    git("worktree", "add", "-f", "--detach", tree, sha)
    try:
        tests = f"{tree}/src/Meltype.Core.Tests"
        shutil.copy(f"{HERE}/ImpactBatch.cs.txt", f"{tests}/ImpactBatch.cs")
        program = open(f"{tests}/Program.cs").read()
        program = program.replace('case "--repro":', 'case "--repro-batch":\n                ImpactBatch.Run(args[1], args[2]);\n                return 0;\n            case "--repro":', 1)
        open(f"{tests}/Program.cs", "w").write(program)
        build = subprocess.run(["dotnet", "build", tests, "-c", "Release", "-v", "q", "-nologo"], capture_output=True, text=True)
        if build.returncode != 0:
            open(out + ".error", "w").write(build.stdout[-4000:])
            return None
        dll = f"{tests}/bin/Release/net10.0/Meltype.Core.Tests.dll"
        subprocess.run(["dotnet", dll, "--repro-batch", inputs, out + ".part"], capture_output=True, text=True, timeout=1800,
                       env={**os.environ, "MELTYPE_UTF8": "1"})
        os.replace(out + ".part", out)
        return out
    finally:
        git("worktree", "remove", "--force", tree)


def build(work):
    os.makedirs(f"{work}/outputs", exist_ok=True)
    cases = [c for c in json.load(open(f"{work}/cases.json")) if c["typed"]]
    inputs = f"{work}/repro.tsv"
    with open(inputs, "w") as f:
        for c in cases:
            f.write(f"{c['number']}\t{c['typed']}\t{last_key(c['body'])}\n")
    commits = pr_commits(work)
    shas = sorted({s for pair in commits.values() for s in pair})
    print(f"PR {len(commits)} 件、ビルドする commit {len(shas)} 件")
    for i, sha in enumerate(shas, 1):
        ok = run_commit(work, sha, inputs)
        print(f"[{i}/{len(shas)}] {sha[:10]} {'ok' if ok else 'ビルドできない'}", flush=True)


def load(path):
    out = {}
    for line in open(path, encoding="utf-8"):
        n, _, j = line.rstrip("\n").partition("\t")
        try:
            r = json.loads(j)
            out[int(n)] = None if "error" in r else (r.get("showing"), r.get("committed"))
        except json.JSONDecodeError:
            out[int(n)] = None
    return out


def matrix(work):
    """PR ごとに、マージの前後で結果が変わった Issue の集まり。"""
    changed = {}
    for pr, (before, after) in pr_commits(work).items():
        a, b = f"{work}/outputs/{before}.tsv", f"{work}/outputs/{after}.tsv"
        if not (os.path.exists(a) and os.path.exists(b)):
            continue
        x, y = load(a), load(b)
        changed[pr] = {n for n in x if x[n] is not None and y.get(n) is not None and x[n] != y[n]}
    return changed


def evaluate(work):
    changed = matrix(work)
    fix = poc.fixes(work)
    cases = {c["number"]: c for c in json.load(open(f"{work}/cases.json")) if c["typed"]}
    labels = poc.truth(work, sorted(cases))
    print(f"前後を比べられた PR {len(changed)} 件")

    # PR 用: その PR で結果が変わった Issue のうち、その PR が閉じた Issue / 閉じていない Issue
    closed_hit = closed_all = extra = 0
    sizes = []
    for pr, ns in changed.items():
        closes = {n for n, p in fix.items() if pr in p and n in cases}
        closed_all += len(closes)
        closed_hit += len(closes & ns)
        extra += len(ns - closes)
        sizes.append(len(ns))
    sizes.sort()
    print(f"[PR 用] PR が閉じた Issue のうち、前後で結果が変わったもの {closed_hit}/{closed_all} ({closed_hit / max(closed_all, 1):.0%})")
    print(f"        閉じていないのに変わった Issue (一緒に直る・壊れる候補) 計 {extra} 件、PR 1 件あたり 中央値 {sizes[len(sizes) // 2] if sizes else 0} 件・最大 {max(sizes, default=0)} 件")

    # トリアージ用: 2 つの Issue の入力を同時に変えた PR を、2 つの Issue を直した PR 以外で数える (自分を直した PR を使うのはずる)
    def score(a, b):
        own = fix.get(a, set()) | fix.get(b, set())
        va = {p for p, ns in changed.items() if a in ns and p not in own}
        vb = {p for p, ns in changed.items() if b in ns and p not in own}
        return poc.jaccard(va, vb)
    values = [(score(a, b), y) for (a, b), y in labels.items()]
    runs = []
    for seed in range(20):
        shuffled = values[:]
        random.Random(seed).shuffle(shuffled)
        runs.append(sorted(shuffled, key=lambda x: -x[0]))
    p10 = sum(sum(y for _, y in r[:10]) for r in runs) / 200
    apv = sum(map(poc.ap, runs)) / len(runs)
    best = max(((t, *poc.pr(values, t)) for t in sorted({v for v, _ in values}) if t > 0), key=lambda x: x[3], default=(0, 0, 0, 0, 0))
    print(f"[トリアージ用] 自分を直した PR を除いて、同じ PR で一緒に変わった度合い: AP={apv:.3f} P@10={p10:.2f} 1位が関連={poc.hit_at_1(score, labels, cases):.2f} "
          f"最良F1={best[3]:.3f} (しきい値 {best[0]:.2f}: P={best[1]:.3f} R={best[2]:.3f} 誤検出率={best[4]:.4f})")
    json.dump({str(k): sorted(v) for k, v in changed.items()}, open(f"{work}/impact.json", "w"))


def created(work):
    path = f"{work}/created.json"
    if not os.path.exists(path):
        out = {}
        for page in range(1, 50):
            text = poc.gh(f"repos/{poc.REPO}/issues", '.[] | "\\(.number) \\(.created_at)"', labels="誤判定", state="all", per_page=100, page=page)
            if not text.strip():
                break
            out.update(dict(line.split() for line in text.splitlines()))
        json.dump(out, open(path, "w"))
    return {int(k): v for k, v in json.load(open(path)).items()}


def triage(work, numbers=()):
    """報告より前にマージされた PR (直した Issue が MAX_GROUP 件以下) のうち、その入力をマージの前後で期待どおりに変えたもの。
    その PR で閉じた Issue を関連 (再発) の候補にする。期待した結果が漢字のものは Mozc が無いと比べられない。"""
    cases = {c["number"]: c for c in json.load(open(f"{work}/cases.json")) if c["typed"]}
    fix, when = poc.fixes(work), created(work)
    merged = {p["n"]: p.get("merged") for p in json.load(open(f"{work}/prinfo.json"))}
    closes = collections.defaultdict(set)
    for n, prs in fix.items():
        for p in prs:
            closes[p].add(n)
    found = 0
    for n in (map(int, numbers) if numbers else sorted(cases)):
        c = cases[n]
        hits = []
        for pr, (before, after) in pr_commits(work).items():
            if pr in fix.get(n, set()) or not merged.get(pr) or merged[pr] >= when.get(n, "") or len(closes[pr]) > poc.MAX_GROUP:
                continue
            try:
                x, y = load(f"{work}/outputs/{before}.tsv").get(n), load(f"{work}/outputs/{after}.tsv").get(n)
            except FileNotFoundError:
                continue
            if x and y and x != y and y[1] == c["expected"]:
                hits.append((pr, sorted(closes[pr] - {n})))
        if hits:
            found += 1
            print(f"#{n} {c['typed']} → {c['actual']} (期待 {c['expected']})")
            for pr, ns in hits:
                print(f"    PR #{pr} で期待どおりになった。その PR で閉じた Issue: " + ", ".join(f"#{m} {cases[m]['typed'] if m in cases else ''}" for m in ns))
    print(f"候補が出た Issue {found} 件")


if __name__ == "__main__":
    step, work = sys.argv[1], sys.argv[2]
    if step == "triage":
        triage(work, sys.argv[3:])
    else:
        {"build": build, "eval": evaluate}[step](work)
