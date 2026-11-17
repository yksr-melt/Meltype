#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 ninjin0802
"""Unicode の emoji-test.txt から、アプリに入れる絵文字の一覧（app/src/main/assets/emoji.txt）を作る。

使い方:
    curl -LO https://unicode.org/Public/18.0.0/emoji/emoji-test.txt
    python3 tools/make-emoji-asset.py emoji-test.txt app/src/main/assets/emoji.txt

形を変えるだけで、選別はしない。fully-qualified の行を、元の並びのまま、グループごとに書き出す。
どのグループをどの種類に入れるか、どれを一覧から外すか（肌の色の違いなど）は、アプリの側
（app/src/main/kotlin/…/emoji/EmojiData.kt）が決める。

書き出す形（UTF-8、行末は LF）:
    # で始まる行  説明。読み飛ばす
    @グループ名   ここから先の絵文字が入る、Unicode のグループ
    そのほかの行  絵文字を空白で区切って並べたもの（元のサブグループごとに1行）
"""
import re
import sys

SOURCE_URL = "https://unicode.org/Public/{version}.0/emoji/emoji-test.txt"


def main(source: str, target: str) -> None:
    version = date = None
    out: list[str] = []
    row: list[str] = []

    def flush() -> None:
        if row:
            out.append(" ".join(row))
            row.clear()

    with open(source, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if m := re.match(r"# Version: (\S+)", line):
                version = m.group(1)
            elif m := re.match(r"# Date: (.+)", line):
                date = m.group(1)
            elif m := re.match(r"# group: (.+)", line):
                flush()
                out.append("@" + m.group(1))
            elif line.startswith("# subgroup:"):
                flush()
            elif m := re.match(r"([0-9A-F ]+?)\s*; fully-qualified\s", line):
                row.append("".join(chr(int(cp, 16)) for cp in m.group(1).split()))
    flush()
    if not version or not date:
        sys.exit("emoji-test.txt の Version または Date の行が見つからない")

    header = [
        "# Unicode Emoji " + version + " の emoji-test.txt から、tools/make-emoji-asset.py で作った。手で直さない。",
        "# 元のファイル: " + SOURCE_URL.format(version=version) + " (Date: " + date + ")",
        "# © Unicode, Inc. Unicode License v3 (https://www.unicode.org/license.txt)。表記は THIRD-PARTY-NOTICES.md。",
        "# 形: 「@」で始まる行は Unicode のグループ名。そのほかの行は、fully-qualified の絵文字を空白で区切ったもの。",
    ]
    with open(target, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(header + out) + "\n")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
