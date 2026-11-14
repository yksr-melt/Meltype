#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
"""全OS・測定値・本文画像を検証して、リリースの下書きを作る。"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tomllib

REPO = "appipinopi/Meltype-Flash"
MEDIA_BRANCH = "release-media"
COMBINED_IMAGES = ("speed-32-64.png", "speed-128-256.png", "speed-short-edit.png", "cpu.png", "ram.png")
RUST_IMAGES = tuple("rust-" + name for name in COMBINED_IMAGES)
IMAGES = RUST_IMAGES + COMBINED_IMAGES
PACKAGES = (
    "Meltype-flash-Windows-x64.zip", "Meltype-flash-Linux-x64.tar.gz",
    "Meltype-flash-macOS-arm64.zip", "Meltype-flash-macOS-x64.zip",
    "Meltype-flash-Android-debug.apk", "Meltype-flash-ChromeOS.zip",
    "Meltype-flash-iOS-unsigned.ipa", "Meltype-flash-iOS-Simulator-arm64.zip",
    "Meltype-flash-iOS-source.zip",
    "Meltype-flash-Linux-x64.deb", "Meltype-flash-Linux-x64.rpm",
    "Meltype-flash-Windows-x64-setup.exe",
    "Meltype-flash-Shunti-Linux-x64.tar.gz",
    "Meltype-flash-Shunti-Windows-x64.zip",
    "Meltype-flash-Shunti-macOS-arm64.zip",
    "Meltype-flash-Shunti-macOS-x64.zip",
    "Meltype-flash-ChromeOS.crx",
    "updates.xml",
)
DOCUMENTS = ("LICENSE", "THIRD-PARTY-NOTICES.md", "SHA256SUMS.txt")


def gh(*args, payload=None):
    command = ["gh", *args]
    if payload is not None:
        command += ["--input", "-"]
    result = subprocess.run(command, input=json.dumps(payload) if payload is not None else None,
                            text=True, encoding="utf-8", capture_output=True, check=True)
    return result.stdout


def api(path, payload=None, method=None):
    args = ["api", f"repos/{REPO}/{path}"]
    if method:
        args += ["--method", method]
    return json.loads(gh(*args, payload=payload))


def find_release(tag):
    # タグ未作成の下書きは /releases/tags/{tag} では取得できない。
    # 認証済みの一覧を全ページ検索し、既存の下書きも重複作成しない。
    pages = json.loads(gh("api", f"repos/{REPO}/releases?per_page=100", "--paginate", "--slurp"))
    return next((release for page in pages for release in page if release["tag_name"] == tag), None)


def normalize_markdown(text):
    text = text.replace("\r", "")
    text = re.sub(r"(?m)^(\|[^\n]*\|)\n+(?=\|)", r"\1\n", text)
    return re.sub(r"\n{3,}", "\n\n", text).strip() + "\n"


def collapse_details(body):
    """変更一覧と測定結果を閉じ、配布先と確認範囲を先に表示する。"""
    for heading, following, summary in (
        ("## 変更内容", "## ビルドと確認範囲", "▶ 詳細な変更内容"),
        ("## ベンチマーク", None, "▶ ベンチマーク"),
    ):
        before, content = body.split(heading + "\n", 1)
        if following:
            content, after = content.split(following + "\n", 1)
            after = following + "\n" + after
        else:
            after = ""
        if not content.lstrip().startswith("<details>"):
            content = f"\n<details>\n<summary>{summary}</summary>\n\n{content.strip()}\n\n</details>\n\n"
        body = before + heading + "\n" + content + after
    return normalize_markdown(body)


def validate_assets(directory):
    expected = set(PACKAGES + DOCUMENTS)
    if {p.name for p in directory.iterdir()} != expected:
        raise ValueError("配布ファイルが不足、または不要な添付が混入しています")
    if any(not (directory / name).is_file() or not (directory / name).stat().st_size for name in expected):
        raise ValueError("空の配布ファイルがあります")
    sums = dict(line.split(None, 1)[::-1] for line in (directory / "SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines())
    if not set(PACKAGES) <= sums.keys():
        raise ValueError("全OSのチェックサムが必要です")
    for name in PACKAGES:
        if hashlib.sha256((directory / name).read_bytes()).hexdigest() != sums[name]:
            raise ValueError(f"配布ファイルのSHA256不一致: {name}")


def validate_benchmark(directory, sha):
    report = json.loads((directory / "report.json").read_text(encoding="utf-8-sig"))
    if report.get("comparison_kind") != "rust-versions" or report.get("new_commit") != sha:
        raise ValueError("ビルド元とベンチマークのコミットが一致しません")
    if not all(report.get(key) is True for key in ("binaries_unchanged", "sources_unchanged", "accepted_performance_baseline")):
        raise ValueError("測定前後の整合性検査が必要です")
    if not report.get("results") or not report.get("process_resources"):
        raise ValueError("ベンチマークの測定値がありません")
    if not re.fullmatch(r"[0-9a-f]{40}", report.get("old_commit", "")):
        raise ValueError("以前の版のコミットSHAが不正です")
    summary = json.loads((directory / "charts/summary.json").read_text(encoding="utf-8-sig"))
    if summary.get("full_report_sha256") != hashlib.sha256((directory / "report.json").read_bytes()).hexdigest():
        raise ValueError("箱ひげ図と測定レポートが一致しません")
    for name in IMAGES:
        if not (directory / "charts" / name).read_bytes().startswith(b"\x89PNG\r\n\x1a\n"):
            raise ValueError(f"箱ひげ図がありません: {name}")
    notes = (directory / "charts/README.md").read_text(encoding="utf-8-sig")
    if "## 全入力の中央値" not in notes or "## CPU・RAMのプロセス中央値" not in notes or "## 全画像" not in notes:
        raise ValueError("測定結果の本文がありません")
    return report, normalize_markdown(notes.split("## 全画像")[0])


def parse_tag(tag):
    match = re.fullmatch(r"flash-v(\d+\.\d+\.\d+)(?:-preview\.(\d+))?", tag)
    if not match:
        raise ValueError("Flashの正式版またはプレビュー版のタグが必要です")
    return match[1], match[2]


def notes_heading(tag):
    """リリースノートの見出し。タグはゼロ埋め (flash-v0001.0001.0019)、ノートは 1.1.19 と書く。"""
    version, preview = parse_tag(tag)
    normalized = ".".join(str(int(part)) for part in version.split("."))
    suffix = f" Pre{preview}" if preview is not None else ""
    return f"# Meltype-FLASH v{normalized}{suffix}" + "\n"


def compose_body(tag, sha, benchmark, media_sha, changes):
    version, preview = parse_tag(tag)
    title = f"Meltype-FLASH v{version}" + (f" Pre{preview}" if preview is not None else "")
    description = "共通Rustコアを使う開発プレビューです。" if preview is not None else "共通Rustコアを使う正式版です。"
    rows = [f"# {title}", "", description + "ビルド成功と各OSの実機確認は別です。", "",
            "## 変更内容", ""]
    # 同じ版の説明があれば、利用者向けの変更点をコミット一覧より先に載せる。
    notes_path = Path(__file__).resolve().parents[1] / "rust/RELEASE-NOTES.md"
    notes = normalize_markdown(notes_path.read_text(encoding="utf-8"))
    heading = notes_heading(tag)
    if notes.startswith(heading):
        section = notes[len(heading):].split("\n---\n", 1)[0]
        rows += [line for line in section.splitlines() if line.startswith("- ")]
        rows += ["", "### ソースの変更", ""]
    # コミット件名は命令として実行せず、コード表記のテキストとして載せる。
    rows += ["- `" + line.replace("`", "'").replace("<", "&lt;").replace(">", "&gt;") + "`" for line in changes.splitlines() if line]
    rows += ["", "## ビルドと確認範囲", "",
             "同じコミットの全OS配布ビルドが成功した後に、この下書きを作成しています。", "",
             "|OS|配布ビルド|実機でのIME入力|", "|---|---|---|"]
    for os_name in ("Windows", "macOS", "Linux", "Android", "ChromeOS", "iOS"):
        rows.append(f"|{os_name}|成功|未実施|")
    rows += ["", "Windows・macOSはCI専用試験、Linuxは隔離IBus、Androidはエミュレーター、ChromeOSは実WASM・模擬IME API、iOSはiOS Simulatorで検証しています。通常アプリ・物理端末でのIME入力確認とは別です。",
             "", "## 配布と導入", "", "|OS|配布ファイル|使い方|", "|---|---|---|"]
    platforms = [
        ("Windows x64", PACKAGES[0:1] + PACKAGES[11:12], "rust/WINDOWS-PACKAGE.md"),
        ("Linux x64", PACKAGES[1:2] + PACKAGES[9:11], "linux/README.md"),
        ("macOS Apple Silicon / Intel", PACKAGES[2:4], "mac/README.md"),
        ("Android", PACKAGES[4:5], "android/README.md"),
        ("ChromeOS", PACKAGES[5:6] + PACKAGES[16:17], "chromeos/README.md"),
        ("iOS", PACKAGES[6:9], "ios/README.md"),
    ]
    platforms.append(("Linux x64 任意導入 shunti", PACKAGES[12:13], "linux/README.md"))
    platforms.append(("Windows x64 任意導入 shunti", PACKAGES[13:14], "rust/WINDOWS-PACKAGE.md"))
    platforms.append(("macOS 任意導入 shunti", PACKAGES[14:16], "rust/AI-MODELS.md"))
    for os_name, files, guide in platforms:
        links = "<br>".join(f"[{name}](https://github.com/{REPO}/releases/download/{tag}/{name})" for name in files)
        rows.append(f"|{os_name}|{links}|[導入手順](https://github.com/{REPO}/blob/{sha}/{guide})|")
    rows += ["", "iOSのIPAは未署名です。実機へ入れるには自分のApple IDでの署名が必要です。Simulator ZIPとソースZIPは開発用です。",
             "Androidはdebug署名、macOSはアドホック署名です。ChromeOSは通常のデスクトップChrome用IMEではありません。",
             "LinuxのdebはUbuntu 24.04以降向け、rpmはglibc 2.39以降のRPM系環境向けです。apt・dnfでの導入手順はLinuxの案内を参照してください。",
             "各配布ファイルのSHA256は添付のSHA256SUMS.txtで確認できます。", "", "## ベンチマーク", ""]
    if benchmark is None:
        rows += ["今回は依頼者の指定によりベンチマークを実施していません。", ""]
    else:
        rows += [re.sub(r"(?m)^(#+) ", r"\1# ", benchmark),
                 "GitHub共有Linuxランナー上の共通Coreの測定です。全OSの入力遅延や常駐IME全体の性能を示すものではありません。", "",
                 "### 箱ひげ図", ""]
        for heading, names in [("Rust前版・現在版（2者比較）", RUST_IMAGES),
                               ("C#・Rust前版・現在版（3者比較）", COMBINED_IMAGES)]:
            rows += ["#### " + heading, ""]
            for name in names:
                rows += [f"![{name}](https://raw.githubusercontent.com/{REPO}/{media_sha}/{tag}/{name})", ""]
        rows += ["測定レポート・画像・生データのダウンロード用アセットは添付していません。", ""]
    body = collapse_details(normalize_markdown("\n".join(rows)))
    if re.search(r"\]\((?!https://)[^)]+\)", body):
        raise ValueError("本文にローカルファイルへのリンクがあります")
    return title, body


def validate_rendered(body, expected_images=len(IMAGES)):
    html = gh("api", "markdown", payload={"text": body, "mode": "gfm", "context": REPO})
    minimum_tables = 3 if expected_images else 2
    if len(re.findall(r"<table\b", html)) < minimum_tables or len(re.findall(r"<img\b", html)) != expected_images:
        raise ValueError("GitHubで表または箱ひげ図を描画できません")


def publish_images(tag, charts):
    # CIからGitデータAPIで画像専用ブランチへ保存する。
    # コミットSHAのURLを本文へ埋め込み、過去リリースの画像を変更しない。
    refs = api(f"git/matching-refs/heads/{MEDIA_BRANCH}")
    ref = next((r for r in refs if r["ref"] == f"refs/heads/{MEDIA_BRANCH}"), None)
    parent = ref["object"]["sha"] if ref else None
    payload = {"tree": []}
    if parent:
        payload["base_tree"] = api(f"git/commits/{parent}")["tree"]["sha"]
    for name in IMAGES:
        blob = api("git/blobs", {"content": base64.b64encode((charts / name).read_bytes()).decode(), "encoding": "base64"})
        payload["tree"].append({"path": f"{tag}/{name}", "mode": "100644", "type": "blob", "sha": blob["sha"]})
    tree = api("git/trees", payload)
    commit = api("git/commits", {"message": f"docs: {tag}の本文用箱ひげ図", "tree": tree["sha"], "parents": [parent] if parent else []})
    if parent:
        api(f"git/refs/heads/{MEDIA_BRANCH}", {"sha": commit["sha"], "force": False}, "PATCH")
    else:
        api("git/refs", {"ref": f"refs/heads/{MEDIA_BRANCH}", "sha": commit["sha"]})
    return commit["sha"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--assets", type=Path, required=True)
    measurement = parser.add_mutually_exclusive_group(required=True)
    measurement.add_argument("--benchmark", type=Path)
    measurement.add_argument("--skip-benchmark", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--publish", action="store_true", help="検証後、画像と下書きをGitHubへ保存する")
    args = parser.parse_args()
    _, preview = parse_tag(args.tag)
    prerelease = preview is not None
    if not re.fullmatch(r"[0-9a-f]{40}", args.sha):
        parser.error("完全なコミットSHAが必要です")
    if args.publish:
        head = subprocess.check_output(["git", "rev-parse", "HEAD"], encoding="utf-8").strip()
        version = tomllib.loads(Path("Cargo.toml").read_text(encoding="utf-8"))["workspace"]["package"]["version"]
        tag_version = args.tag.removeprefix("flash-v")
        core = tag_version.split("-")[0]
        normalized = ".".join(str(int(part)) for part in core.split(".")) + tag_version[len(core):].replace("-preview.", "-pre.")
        if head != args.sha or normalized != version:
            raise ValueError("タグ・ソースの版・実行元コミットが一致しません")
    validate_assets(args.assets)
    benchmark = None
    if args.skip_benchmark:
        baseline = subprocess.check_output(["git", "describe", "--tags", "--match", "flash-v*", "--abbrev=0", args.sha + "^"], encoding="utf-8").strip()
    else:
        report, benchmark = validate_benchmark(args.benchmark, args.sha)
        baseline = report['old_commit']
    changes = subprocess.check_output(["git", "log", "--format=%s", f"{baseline}..{args.sha}"], encoding="utf-8")
    title, body = compose_body(args.tag, args.sha, benchmark, args.sha, changes)
    expected_images = 0 if args.skip_benchmark else len(IMAGES)
    validate_rendered(body, expected_images=expected_images)
    if args.publish:
        # 再実行で公開済みリリースや配布ファイルを上書きしない。
        if find_release(args.tag) is not None:
            raise ValueError("同じタグのリリースが既にあります。上書きしません")
        if not args.skip_benchmark:
            media_sha = publish_images(args.tag, args.benchmark / "charts")
            title, body = compose_body(args.tag, args.sha, benchmark, media_sha, changes)
            validate_rendered(body, expected_images=expected_images)
    args.output.write_text(body, encoding="utf-8", newline="\n")
    # アプリだけでなくライセンスのハッシュも最終一覧へ含める。
    hashes = "".join(f"{hashlib.sha256((args.assets / name).read_bytes()).hexdigest()}  {name}\n"
                     for name in sorted(PACKAGES + DOCUMENTS[:2]))
    (args.assets / "SHA256SUMS.txt").write_text(hashes, encoding="utf-8", newline="\n")
    if args.publish:
        names = [str(args.assets / name) for name in PACKAGES + DOCUMENTS]
        gh("release", "create", args.tag, *names, "--repo", REPO, "--target", args.sha,
           "--title", title, "--notes-file", str(args.output), f"--prerelease={str(prerelease).lower()}", "--draft")
        release = find_release(args.tag)
        if release is None:
            raise ValueError("作成した下書きを取得できません")
        if release.get("draft") is not True or release.get("prerelease") is not prerelease:
            raise ValueError("GitHub上の下書き・プレリリース設定が期待した内容と一致しません")
        uploaded = {asset["name"]: asset["digest"] for asset in release["assets"]}
        expected = {name: "sha256:" + hashlib.sha256((args.assets / name).read_bytes()).hexdigest() for name in PACKAGES + DOCUMENTS}
        if uploaded != expected or normalize_markdown(release["body"]) != body:
            raise ValueError("GitHub上の添付または本文が期待した内容と一致しません")
        print(release["html_url"])


if __name__ == "__main__":
    main()
