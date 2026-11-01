#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 souta
"""Mozc-UT の辞書 (mozcdic-ut.txt) を、ビルドする Mozc の辞書に統合する。

Mozc-UT の辞書は、そのままでは Mozc の辞書と同じ形 (読み<Tab>lid<Tab>rid<Tab>コスト<Tab>表記) だが、
作られたときの Mozc の品詞 ID で書かれている。ビルドする Mozc の版が違うと ID の意味が変わるので、
ここで品詞 ID を確かめ、必要なら今の Mozc の ID に置き換える。

出力は Mozc の data/dictionary_oss/dictionary_ut.txt で、実行するたびに丸ごと書き直す。
Mozc の辞書 (dictionary00.txt〜dictionary09.txt) は読むだけで書き換えないので、手元で書き換えた辞書も残る。
dictionary_ut.txt を Mozc のビルドに含める BUILD.bazel の変更と、UT を使わないときに空にするのは
ビルドスクリプト (build-mozc-helper.sh / Build-MozcHelper.ps1) が行う (UT を使わないときは Python が要らないため)。

使い方:
  python3 native/mozc/merge-ut-dictionary.py --mozc-src ~/mozc/src --dictionary mozcdic-ut.txt
  python3 native/mozc/merge-ut-dictionary.py --mozc-src ~/mozc/src --dictionary mozcdic-ut.txt --check

終了コード: 0 = 成功、1 = 使い方・環境の誤り、2 = 辞書の中身が不正。
"""

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

# 出力先。ビルドスクリプトが BUILD.bazel の base_dictionary_data に足す名前と揃える。
OUTPUT_RELATIVE = Path("data/dictionary_oss/dictionary_ut.txt")
BUILD_RELATIVE = Path("data/dictionary_oss/BUILD.bazel")
# Mozc の辞書のファイル (重複を調べるのに読む)。
DICTIONARY_FILES = [f"dictionary{i:02d}.txt" for i in range(10)]
ID_DEF_RELATIVE = Path("data/dictionary_oss/id.def")
# Mozc-UT の辞書の品詞 (merge-ut-dictionaries は、すべての語をこれで書き出す)。
DEFAULT_POS = "名詞,一般,*,*,*,*,*"
# 読みに使ってよい文字 (ひらがな・長音・繰り返し記号)。Mozc の辞書の読みはひらがな。
HIRAGANA = re.compile(r"^[\u3041-\u3096\u309d\u309e\u30fc]+$")
# 統合した辞書を配るときの注意 (Mozc-UT の辞書は Meltype に同梱しない。出所ごとにライセンスが違う)。
LICENSE_NOTICE = (
    "Mozc-UT の辞書は Meltype に同梱しません。この辞書を入れたバイナリを配る場合は、"
    "辞書ごとのライセンスに従ってください (CC BY-SA の表示・継承、Apache-2.0 の表示など。"
    "THIRD-PARTY-NOTICES.md)。alt-cannadic (GPL-2.0 のみ) を含む辞書は使えません。"
)
LICENSES = {
    "place_names": "Public Domain (日本郵便は著作権を主張していません)",
    "personal_names": "Apache-2.0 (出所の記載なし)",
    "sudachidict": "Apache-2.0 (UniDic BSD-3-Clause・NEologd 由来を含む)",
    "jawiki": "CC BY-SA (バージョンの記載なし)",
    "neologd": "Apache-2.0 (第三者のデータを含む)",
    "edict2": "CC BY-SA 4.0 (EDRDG)",
    "skk_jisyo": "GPL-2.0-or-later",
    "alt_cannadic": "GPL-2.0 only (Meltype とは組み合わせられません)",
}

EXIT_OK = 0
EXIT_ERROR = 1
EXIT_INVALID = 2
# 辞書の誤りのうち、画面に出す件数 (残りは件数だけ出す)。巨大な壊れた辞書でもメモリを使わないため。
MAX_ERRORS = 20

# 辞書の 1 行が長すぎる場合 (読みと表記を合わせて) は、読み込み違いの可能性が高い。
MAX_LINE_CHARS = 512


class MergeError(Exception):
    """使い方・環境の誤り (終了コード 1)。"""


class DictionaryError(Exception):
    """辞書の中身が不正 (終了コード 2)。"""


class Errors:
    """辞書の誤り。件数は全部数え、覚えておくのは MAX_ERRORS 件まで。"""

    def __init__(self) -> None:
        self.items: list[str] = []
        self.total = 0

    def add(self, message: str) -> None:
        self.total += 1
        if len(self.items) < MAX_ERRORS:
            self.items.append(message)

    def __bool__(self) -> bool:
        return self.total > 0

    def __len__(self) -> int:
        return self.total


@dataclass
class IdDefinition:
    """Mozc の id.def (品詞 ID と品詞の対応)。"""

    by_id: dict[int, str]
    by_pos: dict[str, int]
    sha256: str

    def pos_of(self, pos_id: int) -> str:
        return self.by_id.get(pos_id, "")


@dataclass
class Entry:
    """辞書の 1 行。"""

    reading: str
    lid: int
    rid: int
    cost: int
    word: str
    line_number: int

    def line(self) -> str:
        return f"{self.reading}\t{self.lid}\t{self.rid}\t{self.cost}\t{self.word}\n"


@dataclass
class ParseResult:
    entries: list[Entry] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    pos_ids_before: dict[int, int] = field(default_factory=dict)
    pos_remap: dict[int, int] = field(default_factory=dict)
    pos_drift: bool = False


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run_git(root: Path, *args: str) -> subprocess.CompletedProcess:
    try:
        return subprocess.run(
            ["git", "-C", str(root), *args],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except FileNotFoundError as error:  # pragma: no cover - git が無い環境
        raise MergeError("git が見つかりません。Mozc の取得に使うので入れてください。") from error


def load_id_def(mozc_src: Path) -> IdDefinition:
    """Mozc の id.def を読む。1 行は「ID 品詞」の形 (例: 1851 名詞,一般,*,*,*,*,*)。"""
    path = mozc_src / ID_DEF_RELATIVE
    if not path.is_file():
        raise MergeError(
            f"Mozc の id.def が見つかりません: {path}\n"
            "Mozc の取得に失敗している可能性があります。"
        )
    by_id: dict[int, str] = {}
    by_pos: dict[str, int] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split(" ", 1)
        if len(parts) != 2:
            continue
        try:
            pos_id = int(parts[0])
        except ValueError:
            continue
        pos = parts[1].strip()
        by_id[pos_id] = pos
        by_pos.setdefault(pos, pos_id)
    if not by_id:
        raise MergeError(f"id.def を読めませんでした (1 行も取れません): {path}")
    return IdDefinition(by_id=by_id, by_pos=by_pos, sha256=sha256_of(path))


def parse_dictionary(path: Path, id_def: IdDefinition, pos: str,
                     keep_pos_ids: bool, strict_readings: bool) -> ParseResult:
    """Mozc-UT の辞書を読み、Mozc の辞書に足せる形に整える。"""
    if not path.is_file():
        raise MergeError(f"辞書ファイルが見つかりません: {path}")
    if path.stat().st_size == 0:
        raise DictionaryError(f"辞書ファイルが空です: {path}")

    try:
        text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError as error:
        raise DictionaryError(
            f"辞書ファイルを UTF-8 として読めません: {path}\n"
            f"  {error.reason} (バイト位置 {error.start})"
        ) from error

    if text.startswith("\ufeff"):
        raise DictionaryError(
            f"辞書ファイルの先頭に BOM があります: {path}\n"
            "BOM があると最初の読みが崩れるので、BOM を外してから指定してください。"
        )
    if "\x00" in text:
        raise DictionaryError(f"辞書ファイルに NUL バイトがあります: {path}")

    result = ParseResult()
    errors = Errors()
    pos_counts: dict[int, int] = {}

    for index, raw in enumerate(text.splitlines(), start=1):
        if not raw.strip():
            continue
        if len(raw) > MAX_LINE_CHARS:
            errors.add(
                f"{index} 行目: 行が長すぎます ({len(raw)} 文字)。読みと表記を確かめてください"
            )
            continue

        columns = raw.split("\t")
        # Mozc の辞書は 5 列 (読み・lid・rid・コスト・表記)。
        # 6 列目以降は Mozc の特別な印 (SPELLING_CORRECTION など) なので、受け取らない。
        if len(columns) != 5:
            errors.add(
                f"{index} 行目: 列が {len(columns)} 個あります (5 列ではありません): "
                f"{raw[:80]!r}"
            )
            continue
        reading, lid_text, rid_text, cost_text, word = columns
        empty = [
            name for name, value in
            (("読み", reading), ("lid", lid_text), ("rid", rid_text),
             ("コスト", cost_text), ("表記", word))
            if value == ""
        ]
        if empty:
            # 空の列があると、Mozc の読み込みが列をずらして壊れる。
            errors.add(f"{index} 行目: 空の列があります: {', '.join(empty)}")
            continue

        lid = _parse_int(lid_text, "lid", index, errors)
        rid = _parse_int(rid_text, "rid", index, errors)
        cost = _parse_int(cost_text, "コスト", index, errors)
        if lid is None or rid is None or cost is None:
            continue
        if not 0 <= lid <= 0xFFFF or not 0 <= rid <= 0xFFFF:
            errors.add(f"{index} 行目: 品詞 ID が範囲外です (0〜65535): {lid} {rid}")
            continue
        if not 0 <= cost <= 0xFFFF:
            errors.add(f"{index} 行目: コストが範囲外です (0〜65535): {cost}")
            continue

        if not HIRAGANA.match(reading):
            message = (
                f"{index} 行目: 読みがひらがなではありません: {reading!r}"
                " (Mozc の辞書の読みはひらがなです)"
            )
            if strict_readings:
                errors.add(message)
                continue
            result.warnings.append(message)

        if word.strip() != word:
            result.warnings.append(
                f"{index} 行目: 表記の前後に空白があります: {word!r} (そのまま登録します)"
            )

        result.entries.append(
            Entry(reading=reading, lid=lid, rid=rid, cost=cost, word=word, line_number=index)
        )
        pos_counts[lid] = pos_counts.get(lid, 0) + 1

    if errors:
        more = "" if errors.total <= len(errors.items) else \
            f"\n  ...ほか {errors.total - len(errors.items)} 件"
        raise DictionaryError(
            f"辞書ファイルの中身が不正です: {path}\n  " + "\n  ".join(errors.items) + more
        )
    if not result.entries:
        raise DictionaryError(f"辞書ファイルに使える行がありません: {path}")

    result.pos_ids_before = dict(sorted(pos_counts.items()))
    _remap_pos_ids(result, id_def, pos, keep_pos_ids)
    return result


def _parse_int(text: str, name: str, line_number: int, errors: Errors) -> int | None:
    try:
        return int(text)
    except ValueError:
        errors.add(f"{line_number} 行目: {name} が整数ではありません: {text!r}")
        return None


def _remap_pos_ids(result: ParseResult, id_def: IdDefinition, pos: str,
                   keep_pos_ids: bool) -> None:
    """品詞 ID を、今の Mozc の id.def に合わせる。

    merge-ut-dictionaries は、すべての語を 1 つの品詞 (既定は「名詞,一般」) で書き出す。
    辞書を作ったときの Mozc と、ビルドする Mozc で ID の意味が変わっていれば、ここで置き換える。
    複数の品詞を使っている辞書 (自前で作ったもの) は、そのまま使える ID を尊重し、
    id.def に無い ID だけを置き換える。
    """
    if keep_pos_ids:
        invalid = sorted({e.lid for e in result.entries if e.lid not in id_def.by_id} |
                         {e.rid for e in result.entries if e.rid not in id_def.by_id})
        if invalid:
            raise DictionaryError(
                "辞書の品詞 ID が、ビルドする Mozc の id.def にありません: "
                f"{invalid}\n(この Mozc の版に合わせてください)"
            )
        return

    target_id = id_def.by_pos.get(pos)
    if target_id is None:
        raise MergeError(
            f"ビルドする Mozc の id.def に「{pos}」がありません。--pos で品詞を指定してください。"
        )

    source_ids = sorted({e.lid for e in result.entries} | {e.rid for e in result.entries})

    if len(source_ids) == 1 and source_ids[0] != target_id:
        # merge-ut-dictionaries の出力。作ったときの Mozc の品詞 ID が、今の Mozc と違う。
        result.pos_drift = True
        result.pos_remap[source_ids[0]] = target_id
    else:
        for pos_id in source_ids:
            if pos_id not in id_def.by_id:
                result.pos_remap[pos_id] = target_id

    for entry in result.entries:
        if entry.lid in result.pos_remap:
            entry.lid = result.pos_remap[entry.lid]
        if entry.rid in result.pos_remap:
            entry.rid = result.pos_remap[entry.rid]

    invalid = sorted({e.lid for e in result.entries if e.lid not in id_def.by_id} |
                     {e.rid for e in result.entries if e.rid not in id_def.by_id})
    if invalid:
        raise DictionaryError(
            f"辞書の品詞 ID が、ビルドする Mozc の id.def にありません: {invalid}"
        )


def read_mozc_entries(mozc_src: Path) -> set[tuple[str, str]]:
    """ビルドする Mozc の辞書にある語 (読み, 表記)。手元で書き換えた辞書なら、その中身で比べる。"""
    entries: set[tuple[str, str]] = set()
    for name in DICTIONARY_FILES:
        path = mozc_src / OUTPUT_RELATIVE.parent / name
        if not path.is_file():
            continue
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            columns = line.split("\t")
            if len(columns) >= 5:
                entries.add((columns[0], columns[4]))
    return entries


def remove_duplicates(entries: list[Entry],
                      existing: set[tuple[str, str]]) -> tuple[list[Entry], int, int]:
    """重複を取り除く。UT の中の重複と、Mozc の辞書にすでにある語の 2 種類。"""
    # 同じ (読み, 表記) は、いちばん低いコスト (＝いちばん出やすい) だけを残す。
    best: dict[tuple[str, str], Entry] = {}
    for entry in entries:
        key = (entry.reading, entry.word)
        current = best.get(key)
        if current is None or (entry.cost, entry.line_number) < (current.cost, current.line_number):
            best[key] = entry
    in_ut = len(entries) - len(best)

    kept = [entry for key, entry in best.items() if key not in existing]
    in_mozc = len(best) - len(kept)
    return kept, in_ut, in_mozc


def ensure_registered(mozc_src: Path) -> None:
    """出力先が Mozc のビルドに含まれるか確かめる。含まれないと、書いた語が黙って捨てられる。

    出力先の行は、ビルドスクリプト (build-mozc-helper.sh の sed と Build-MozcHelper.ps1 の正規表現) が
    BUILD.bazel の "dictionary09.txt", の行を目印に足す。Mozc の BUILD.bazel の形が変わったら 3 か所を直す。
    """
    build = mozc_src / BUILD_RELATIVE
    text = build.read_text(encoding="utf-8") if build.is_file() else ""
    if f'"{OUTPUT_RELATIVE.name}"' not in text:
        raise MergeError(
            f"{build} に {OUTPUT_RELATIVE.name} がありません (このままでは UT の語がビルドに入りません)。\n"
            "build-mozc-helper.sh / Build-MozcHelper.ps1 から実行してください。"
        )


def write_entries(output: Path, entries: list[Entry]) -> None:
    """出力先を丸ごと書き直す。Mozc の辞書は行の順番を気にしない (どの行も独立に読まれる)。"""
    with open(output, "w", encoding="utf-8", newline="\n") as stream:
        for entry in entries:
            stream.write(entry.line())


def count_lines(path: Path) -> int:
    with open(path, "rb") as stream:
        return sum(chunk.count(b"\n") for chunk in iter(lambda: stream.read(1024 * 1024), b""))


def mozc_commit(mozc_src: Path) -> str:
    root = mozc_src.parent
    result = run_git(root, "rev-parse", "HEAD")
    return result.stdout.strip() if result.returncode == 0 else ""


def iso_now() -> str:
    epoch = os.environ.get("SOURCE_DATE_EPOCH")
    if epoch and epoch.isdigit():
        moment = datetime.fromtimestamp(int(epoch), tz=timezone.utc)
    else:
        moment = datetime.now(timezone.utc)
    return moment.replace(microsecond=0).isoformat()


def summary(result: ParseResult, dictionary: Path, output: Path,
            in_ut: int, in_mozc: int, id_def: IdDefinition, pos: str) -> str:
    lines = [
        f"Mozc-UT の辞書を統合しました: {dictionary}",
        f"  辞書の SHA-256: {sha256_of(dictionary)}",
        f"  読み込み: {len(result.entries) + in_ut + in_mozc} 件 / 統合: {len(result.entries)} 件",
        f"  除外: Mozc の辞書と重複 {in_mozc} 件 / UT の中の重複 {in_ut} 件",
    ]
    target_id = id_def.by_pos.get(pos, -1)
    if result.pos_drift or result.pos_remap:
        changes = ", ".join(
            f"{old} ({id_def.pos_of(old) or 'id.def に無い ID'}) → {new}"
            for old, new in sorted(result.pos_remap.items())
        )
        lines.append(f"  品詞 ID を今の Mozc に合わせました: {changes}")
    else:
        lines.append(f"  品詞 ID: {target_id} ({pos}) — そのまま使えます")
    if result.warnings:
        lines.append(f"  注意: {len(result.warnings)} 件 (--check で一覧を出せます)")
    lines.append(f"  出力先: {output} ({len(result.entries)} 行)")
    lines.append(f"  注意: {LICENSE_NOTICE}")
    return "\n".join(lines)


def write_report(path: Path, report: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    with open(path, "w", encoding="utf-8", newline="\n") as stream:
        stream.write(text)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Mozc-UT の辞書 (mozcdic-ut.txt) を、ビルドする Mozc の辞書に統合する。",
    )
    parser.add_argument(
        "--mozc-src", required=True, type=Path,
        help="Mozc の src ディレクトリ (例: ~/mozc/src)",
    )
    parser.add_argument(
        "--dictionary", type=Path,
        help="統合する Mozc-UT の辞書 (mozcdic-ut.txt)",
    )
    parser.add_argument(
        "--check", action="store_true",
        help=f"辞書を確かめるだけで、{OUTPUT_RELATIVE.name} は書かない",
    )
    parser.add_argument(
        "--report", type=Path,
        help="統合した内容 (辞書と Mozc のハッシュなど) を JSON で書き出す",
    )
    parser.add_argument(
        "--pos", default=DEFAULT_POS,
        help=f"UT の語に付ける品詞 (既定: {DEFAULT_POS})",
    )
    parser.add_argument(
        "--keep-pos-ids", action="store_true",
        help="品詞 ID を置き換えず、辞書の ID をそのまま使う (自前の辞書向け)",
    )
    parser.add_argument(
        "--strict-readings", action="store_true",
        help="読みがひらがなでない行を警告ではなくエラーにする",
    )
    parser.add_argument("--quiet", action="store_true", help="--check の一覧を出さない")
    args = parser.parse_args(argv)

    mozc_src = args.mozc_src.expanduser().resolve()
    output = mozc_src / OUTPUT_RELATIVE
    if not output.parent.is_dir():
        raise MergeError(
            f"Mozc の data/dictionary_oss が見つかりません: {mozc_src}\n"
            "--mozc-src には Mozc の src ディレクトリを指定してください。"
        )

    # argparse の required は終了コード 2 (辞書の中身が不正) になるので、ここで確かめる。
    if not args.dictionary:
        raise MergeError("--dictionary を指定してください。")
    dictionary = args.dictionary.expanduser().resolve()
    if dictionary == output:
        raise MergeError(f"--dictionary に出力先 ({OUTPUT_RELATIVE.name}) を指定できません。")
    if not args.check:
        ensure_registered(mozc_src)

    id_def = load_id_def(mozc_src)
    result = parse_dictionary(
        dictionary, id_def, args.pos, args.keep_pos_ids, args.strict_readings
    )

    existing = read_mozc_entries(mozc_src)
    kept, in_ut, in_mozc = remove_duplicates(result.entries, existing)
    kept.sort(key=lambda entry: (entry.reading, entry.word, entry.cost))
    result.entries = kept

    report = {
        "schema": 2,
        "tool": "native/mozc/merge-ut-dictionary.py",
        "generated_at": iso_now(),
        "mode": "check" if args.check else "merge",
        "mozc": {
            "commit": mozc_commit(mozc_src),
            "id_def": str(ID_DEF_RELATIVE),
            "id_def_sha256": id_def.sha256,
            "id_def_pos_count": len(id_def.by_id),
            "pos": args.pos,
            "pos_id": id_def.by_pos.get(args.pos, -1),
            "output": OUTPUT_RELATIVE.as_posix(),
        },
        "dictionary": {
            "path": str(dictionary),
            "sha256": sha256_of(dictionary),
            "bytes": dictionary.stat().st_size,
            "lines_read": count_lines(dictionary),
            "entries_used": len(result.entries),
            "pos_ids_before": {str(key): value for key, value in result.pos_ids_before.items()},
            "pos_remap": {str(key): value for key, value in sorted(result.pos_remap.items())},
            "pos_drift": result.pos_drift,
        },
        "removed": {
            "duplicate_in_ut": in_ut,
            "already_in_mozc": in_mozc,
        },
        "result": {
            "lines_added": len(result.entries),
        },
        "warnings": result.warnings[:100],
        "license": {
            "bundled_with_meltype": False,
            "notice": LICENSE_NOTICE,
            "sources": LICENSES,
        },
    }

    if args.check:
        if not args.quiet:
            print(summary(result, dictionary, output, in_ut, in_mozc, id_def, args.pos))
            for warning in result.warnings[:20]:
                print(f"  注意: {warning}", file=sys.stderr)
            if len(result.warnings) > 20:
                print(f"  注意: ほか {len(result.warnings) - 20} 件", file=sys.stderr)
        if args.report:
            write_report(args.report, report)
        print(f"辞書を確かめました ({OUTPUT_RELATIVE.name} は書いていません)。")
        return EXIT_OK

    write_entries(output, result.entries)
    report["result"]["output_sha256"] = sha256_of(output)

    if args.report:
        write_report(args.report, report)
    print(summary(result, dictionary, output, in_ut, in_mozc, id_def, args.pos))
    return EXIT_OK


def configure_streams() -> None:
    """コンソールの文字コードで表せない文字があっても落ちないようにする。

    Windows のコンソールが UTF-8 でない (cp932・cp1252 など) とき、そのままだと print が
    UnicodeEncodeError で止まる。表せない文字だけを \\uXXXX の形にして、処理は続ける
    (日本語の Windows なら cp932 でそのまま出る)。
    """
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors="backslashreplace")
        except (AttributeError, ValueError, OSError):
            pass


def entry_point() -> int:
    configure_streams()
    if sys.version_info < (3, 10):
        print("エラー: Python 3.10 以降が必要です (今の版: "
              + sys.version.split()[0] + ")。", file=sys.stderr)
        return EXIT_ERROR
    try:
        return main(sys.argv[1:])
    except MergeError as error:
        print(f"エラー: {error}", file=sys.stderr)
        return EXIT_ERROR
    except DictionaryError as error:
        print(f"辞書のエラー: {error}", file=sys.stderr)
        return EXIT_INVALID
    except OSError as error:
        print(f"エラー: {error}", file=sys.stderr)
        return EXIT_ERROR


if __name__ == "__main__":
    sys.exit(entry_point())
