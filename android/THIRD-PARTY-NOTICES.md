# サードパーティの表示

## Mozc

- 名前: Mozc（かな漢字変換エンジンと辞書）
- 入手元: https://github.com/google/mozc （版は `mozc/MOZC_COMMIT` に書いてある）
- 使っているもの: `libmozc.so`（変換エンジン）、`mozc.data`（辞書）、`mozc/src/main/proto/protocol/*.proto`（命令定義の写し）
- ライセンス: BSD 3-Clause。辞書のデータ（`src/data/dictionary*`）には、NAIST（IPAdic）と ICOT の条件が加わる。Mozc に組み込まれているほかのソフトウェア（Abseil など）の表示は Mozc のクレジット（`credits_en.html`）にある。
- 著作権表示:
  - Copyright 2010-2018, Google Inc. All rights reserved.
  - Copyright 2000, 2001, 2002, 2003 Nara Institute of Science and Technology. All Rights Reserved.
  - ICOT（財団法人 新世代コンピュータ技術開発機構）の辞書に由来する部分を含む。
- 全文: アプリに入れている `MOZC_NOTICES.txt`（Mozc の `LICENSE` と `credits_en.html` をつなげたもの。`tools/build-mozc.sh` が作る）にある。

## Protocol Buffers

- 名前: Protocol Buffers（Java lite ランタイム `com.google.protobuf:protobuf-javalite`。`libmozc.so` の中にも C++ 版が入っている）
- 入手元: https://github.com/protocolbuffers/protobuf
- ライセンス: BSD 3-Clause
- 著作権表示: Copyright 2008, Google Inc. All rights reserved.
- 全文: `MOZC_NOTICES.txt` の中の Protocol Buffers の節にある。

## Kotlin

- 名前: Kotlin 標準ライブラリ（`org.jetbrains.kotlin:kotlin-stdlib`）と、それが依存する JetBrains Annotations（`org.jetbrains:annotations`）
- 入手元: https://github.com/JetBrains/kotlin 、https://github.com/JetBrains/java-annotations
- ライセンス: Apache License 2.0（https://www.apache.org/licenses/LICENSE-2.0）
- 著作権表示: Copyright JetBrains s.r.o. and Kotlin Programming Language contributors

## Unicode の絵文字の一覧

- 名前: Unicode Emoji 18.0 の `emoji-test.txt`（Emoji Keyboard/Display Test Data for UTS #51、Date: 2026-04-30）
- 入手元: https://unicode.org/Public/18.0.0/emoji/emoji-test.txt
- 使っているもの: `app/src/main/assets/emoji.txt`（上のファイルから fully-qualified の絵文字とグループ名だけを取り出し、形を変えたもの。`tools/make-emoji-asset.py` が作る）。絵文字の画面の一覧と、種類への振り分けに使う。
- ライセンス: Unicode License v3（https://www.unicode.org/license.txt）
- 著作権表示と許諾の全文:

```
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.
```
