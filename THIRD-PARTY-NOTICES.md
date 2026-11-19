# サードパーティー通知

## Rust/WASM ChromeOS試験版

ChromeOS配布には同梱辞書とRust依存ライブラリを組み込む。`chromeos/build.py`がCargoの解決グラフから対象ライブラリのライセンス本文を収集し、`RUST-DEPENDENCY-NOTICES.txt`として同梱する。wasm-bindgen / js-sys / web-timeはMIT OR Apache-2.0。同梱辞書の由来・表示条件は本書の各辞書の節を参照する。

ChromeOSの漢字変換辞書は、`native/mozc/MOZC_COMMIT` に固定した [Mozc](https://github.com/google/mozc) の `src/data/dictionary_oss/dictionary00.txt` ～ `dictionary09.txt` と `connection_single_column.txt` を別形式へ変換したもの。Mozc本体の変換エンジンは含まない。出典・固定commit・SHA256と、GoogleのBSD-3-Clause、NAIST・ICOT・沖縄辞書を含む元のライセンス全文を `pkg/MOZC-DICTIONARY-NOTICES.txt` としてChromeOS配布へ同梱する。

Meltype のソースコード (このリポジトリ) には、他者の著作物は含まれていません。

## 配布用パッケージに同梱しているもの

| 部品 | ライセンス | 同梱先 |
| --- | --- | --- |
| .NET ランタイム (Microsoft.NETCore.App, Microsoft.WindowsDesktop.App) | MIT License | `app\dotnet\` (ライセンス: `app\dotnet\LICENSE.txt`、同梱部品の通知: `app\dotnet\ThirdPartyNotices.txt`) |
| Microsoft Visual C++ のランタイム (`msvcp140.dll`・`vcruntime140.dll` など、Mozc の変換ヘルパーが使うもの。一覧は `app\mozc\VC-RUNTIME.txt`) | Microsoft Visual Studio ソフトウェア ライセンス条項の「再頒布可能コード」(Visual Studio の VC\Redist にあり、REDIST の一覧に載っているファイル。変えずにアプリと一緒に配ることが認められている) | `app\mozc\` |
| [Mozc](https://github.com/google/mozc) の変換エンジンと辞書 (`meltype_mozc_helper.exe`。Meltype 用の小さな入出力部分 `native/mozc/meltype_mozc_helper.cc` を足してビルドしたもの) | Mozc: BSD-3-Clause (Copyright Google Inc.)。辞書: IPAdic (NAIST)・ICOT・沖縄辞書 のライセンス。組み込みのライブラリ: Abseil (Apache-2.0)・Protocol Buffers (BSD-3-Clause)・Japanese Usage Dictionary など | `app\mozc\` (ライセンス: `app\mozc\MOZC-LICENSE.txt`、辞書とライブラリの全文: `app\mozc\MOZC-CREDITS.html`。Qt は使っていない) |

ポータブル版も通常版と同じフォルダー構成で配布します。アプリと .NET ランタイムに関するライセンス・第三者通知、および Mozc をビルドに含める場合のライセンス・辞書クレジットは、各配布物に同梱します。Mozc の VC ランタイム DLL は元のバイナリを変更せずに実行し、対象ファイルの一覧を `VC-RUNTIME.txt` に記載します。

## Mac 版 (mac/) がビルド時に取り込むもの

| 部品 | ライセンス | 使い方 |
| --- | --- | --- |
| [AzooKeyKanaKanjiConverter](https://github.com/azooKey/AzooKeyKanaKanjiConverter) (azooKey の変換エンジンと辞書) | MIT License | Swift Package として取り込み、Meltype.app に組み込む (漢字変換)。Meltype.app を配布するときは、azooKey のライセンス表示も同梱する |
| .NET ランタイム (NativeAOT) | MIT License | libMeltypeNative.dylib に組み込まれる |
| [swift-collections](https://github.com/apple/swift-collections)・[swift-algorithms](https://github.com/apple/swift-algorithms)・[swift-numerics](https://github.com/apple/swift-numerics) (Apple) | Apache License 2.0 | AzooKeyKanaKanjiConverter が使う部品として、一緒に Meltype.app に組み込まれる (`mac/Package.resolved` で版を固定) |
| [Jinja](https://github.com/johnmai-dev/Jinja)・[swift-tokenizers](https://github.com/ensan-hcl/swift-tokenizers) | Apache License 2.0 | AzooKeyKanaKanjiConverter が依存する部品 (`mac/Package.resolved` で版を固定)。組み込まれる場合は、ライセンス表示を同梱する |
| [aptabase-swift](https://github.com/aptabase/aptabase-swift) | MIT License (Copyright Sumbit Labs Ltd.) | Swift Package として取り込み、Meltype.app に組み込む (利用状況の集計)。初回起動の同意後に送信 |

## iOS 版 (ios/) がビルド時に取り込むもの

| 部品 | ライセンス | 使い方 |
| --- | --- | --- |
| [aptabase-swift](https://github.com/aptabase/aptabase-swift) | MIT License (Copyright Sumbit Labs Ltd.) | Swift Package として取り込み、アプリ本体に組み込む (利用状況の集計)。キーボード拡張には入れない |

## Android 版 (android/) がビルド時に取り込むもの

| 部品 | ライセンス | 使い方 |
| --- | --- | --- |
| [aptabase-kotlin](https://github.com/aptabase/aptabase-kotlin) | MIT License (Copyright Sumbit Labs Ltd.) | JitPack から取り込み、APK に組み込む (利用状況の集計)。初回起動の同意後に送信 |

## Linux・ChromeOS 版が送信に使うもの

| 部品 | ライセンス | 使い方 |
| --- | --- | --- |
| [Aptabase](https://aptabase.com/) HTTPS API | サービス利用条件は Aptabase に従う | Linux の設定画面と ChromeOS 拡張が、初回同意後に利用状況を直接送信する (ライブラリは同梱しない) |

## 実行時に使う Windows の機能 (同梱しない)

次は Windows に標準で入っているものを実行時に呼び出すだけで、Meltype には含まれません。

- Microsoft IME の変換エンジン (IFELanguage)
- Windows の変換候補 API (Windows.Data.Text.TextConversionGenerator)
- Windows のスペルチェッカー (ISpellChecker)
- UI Automation、IMM32、Text Services Framework

## 辞書

### 英訳の候補 (dictionaries/translations.txt)

[JMdict](https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project) (Japanese-Multilingual Dictionary) の英語版から、よく使う語 (ichi1・news1・spec1・spec2・gai1 の印が付いた語) の書き方・品詞・英訳の一部を `tools/make-translations.mjs` で取り出して作りました。

- 著作権: Electronic Dictionary Research and Development Group (EDRDG)
- ライセンス: [Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)](https://creativecommons.org/licenses/by-sa/4.0/)。EDRDG のライセンスの説明: https://www.edrdg.org/edrdg/licence.html
- この派生データ (dictionaries/translations.txt) も CC BY-SA 4.0 です。Meltype のプログラム本体は GPL-3.0-or-later です。

### 読みの一覧 (dictionaries/readings.txt)

ローマ字の打ち間違いを直すときに、直した読みがよく使う語になるかを調べるのに使います。JMdict のよく使う語 (同じ印) の読みと活用の種類を `tools/make-readings.mjs` で取り出して作りました。著作権・ライセンスは英訳の候補と同じ (EDRDG、CC BY-SA 4.0) で、この派生データも CC BY-SA 4.0 です。

### カタカナで書く語の読み (dictionaries/katakana.txt)

カタカナ語を日本語の語として読むために使います。JMdict のカタカナだけの読みを基にしています。

- 著作権: Electronic Dictionary Research and Development Group (EDRDG)
- ライセンス: [Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)](https://creativecommons.org/licenses/by-sa/4.0/)。EDRDG のライセンスの説明: https://www.edrdg.org/edrdg/licence.html
- この派生データ (dictionaries/katakana.txt) も CC BY-SA 4.0 です。

### 補足の読み (dictionaries/readings-extra.txt)

`readings.txt` に無い話し言葉・略した言い方の読みを補います。

- 著作権: Meltype の contributors
- ライセンス: GPL-3.0-or-later（リポジトリ本体と同じ）

### 英字で書く語の候補 (dictionaries/loanwords.txt)

リナックス → Linux のように、カタカナで打った語の候補に英字の書き方を出すのに使います。JMdict の英字で書く語 (Ｌｉｎｕｘ、ＧＨＱ) の書き方とカタカナの読みを `tools/make-loanwords.mjs` で取り出して作りました。著作権・ライセンスは英訳の候補と同じ (EDRDG、CC BY-SA 4.0) で、この派生データも CC BY-SA 4.0 です。

### 候補の意味 (dictionaries/meanings.txt)

変換中に候補で止まったとき、その候補の日本語の意味を出すのに使います。[ウィクショナリー日本語版](https://ja.wiktionary.org/) の記事から [kaikki.org](https://kaikki.org/jawiktionary/) (wiktextract、Tatu Ylonen) が取り出したデータを元に、英訳の候補と同じよく使う語の意味の最初の文を `tools/make-meanings.mjs` で取り出して作りました (長い文は途中で切っています)。

- 著作権: ウィクショナリーの執筆者 (各記事の履歴を参照)
- ライセンス: [Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)](https://creativecommons.org/licenses/by-sa/4.0/)
- この派生データ (dictionaries/meanings.txt) も CC BY-SA 4.0 です。

### よく使う英単語 (dictionaries/english-words.txt)

Windows のスペルチェッカーが使えない環境 (Mac 版・Linux 版・テスト) で英単語を知るのに使います。[SCOWL](http://wordlist.aspell.net/) (Spell Checker Oriented Word Lists、2020.12.07) の大きさ 35 までの english / american の語のうち、小文字の英字だけの語を `tools/make-english-words.mjs` で取り出して作りました。SCOWL の Copyright ファイルの全文:

```
The collective work is Copyright 2000-2018 by Kevin Atkinson as well
as any of the copyrights mentioned below:

  Copyright 2000-2018 by Kevin Atkinson

  Permission to use, copy, modify, distribute and sell these word
  lists, the associated scripts, the output created from the scripts,
  and its documentation for any purpose is hereby granted without fee,
  provided that the above copyright notice appears in all copies and
  that both that copyright notice and this permission notice appear in
  supporting documentation. Kevin Atkinson makes no representations
  about the suitability of this array for any purpose. It is provided
  "as is" without express or implied warranty.

Alan Beale <biljir@pobox.com> also deserves special credit as he has,
in addition to providing the 12Dicts package and being a major
contributor to the ENABLE word list, given me an incredible amount of
feedback and created a number of special lists (those found in the
Supplement) in order to help improve the overall quality of SCOWL.

The 10 level includes the 1000 most common English words (according to
the Moby (TM) Words II [MWords] package), a subset of the 1000 most
common words on the Internet (again, according to Moby Words II), and
frequently class 16 from Brian Kelk's "UK English Wordlist
with Frequency Classification".

The MWords package was explicitly placed in the public domain:

    The Moby lexicon project is complete and has
    been place into the public domain. Use, sell,
    rework, excerpt and use in any way on any platform.

    Placing this material on internal or public servers is
    also encouraged. The compiler is not aware of any
    export restrictions so freely distribute world-wide.

    You can verify the public domain status by contacting

    Grady Ward
    3449 Martha Ct.
    Arcata, CA  95521-4884

    grady@netcom.com
    grady@northcoast.com

The "UK English Wordlist With Frequency Classification" is also in the
Public Domain:

  Date: Sat, 08 Jul 2000 20:27:21 +0100
  From: Brian Kelk <Brian.Kelk@cl.cam.ac.uk>

  > I was wondering what the copyright status of your "UK English
  > Wordlist With Frequency Classification" word list as it seems to
  > be lacking any copyright notice.

  There were many many sources in total, but any text marked
  "copyright" was avoided. Locally-written documentation was one
  source. An earlier version of the list resided in a filespace called
  PUBLIC on the University mainframe, because it was considered public
  domain.

  Date: Tue, 11 Jul 2000 19:31:34 +0100

  > So are you saying your word list is also in the public domain?

  That is the intention.

The 20 level includes frequency classes 7-15 from Brian's word list.

The 35 level includes frequency classes 2-6 and words appearing in at
least 11 of 12 dictionaries as indicated in the 12Dicts package.  All
words from the 12Dicts package have had likely inflections added via
my inflection database.

The 12Dicts package and Supplement is in the Public Domain.

The WordNet database, which was used in the creation of the
Inflections database, is under the following copyright:

  This software and database is being provided to you, the LICENSEE,
  by Princeton University under the following license.  By obtaining,
  using and/or copying this software and database, you agree that you
  have read, understood, and will comply with these terms and
  conditions.:

  Permission to use, copy, modify and distribute this software and
  database and its documentation for any purpose and without fee or
  royalty is hereby granted, provided that you agree to comply with
  the following copyright notice and statements, including the
  disclaimer, and that the same appear on ALL copies of the software,
  database and documentation, including modifications that you make
  for internal use or for distribution.

  WordNet 1.6 Copyright 1997 by Princeton University.  All rights
  reserved.

  THIS SOFTWARE AND DATABASE IS PROVIDED "AS IS" AND PRINCETON
  UNIVERSITY MAKES NO REPRESENTATIONS OR WARRANTIES, EXPRESS OR
  IMPLIED.  BY WAY OF EXAMPLE, BUT NOT LIMITATION, PRINCETON
  UNIVERSITY MAKES NO REPRESENTATIONS OR WARRANTIES OF MERCHANT-
  ABILITY OR FITNESS FOR ANY PARTICULAR PURPOSE OR THAT THE USE OF THE
  LICENSED SOFTWARE, DATABASE OR DOCUMENTATION WILL NOT INFRINGE ANY
  THIRD PARTY PATENTS, COPYRIGHTS, TRADEMARKS OR OTHER RIGHTS.

  The name of Princeton University or Princeton may not be used in
  advertising or publicity pertaining to distribution of the software
  and/or database.  Title to copyright in this software, database and
  any associated documentation shall at all times remain with
  Princeton University and LICENSEE agrees to preserve same.

The 40 level includes words from Alan's 3esl list found in version 4.0
of his 12dicts package.  Like his other stuff the 3esl list is also in the
public domain.

The 50 level includes Brian's frequency class 1, words appearing
in at least 5 of 12 of the dictionaries as indicated in the 12Dicts
package, and uppercase words in at least 4 of the previous 12
dictionaries.  A decent number of proper names is also included: The
top 1000 male, female, and Last names from the 1990 Census report; a
list of names sent to me by Alan Beale; and a few names that I added
myself.  Finally a small list of abbreviations not commonly found in
other word lists is included.

The name files form the Census report is a government document which I
don't think can be copyrighted.

The file special-jargon.50 uses common.lst and word.lst from the
"Unofficial Jargon File Word Lists" which is derived from "The Jargon
File".  All of which is in the Public Domain.  This file also contain
a few extra UNIX terms which are found in the file "unix-terms" in the
special/ directory.

The 55 level includes words from Alan's 2of4brif list found in version
4.0 of his 12dicts package.  Like his other stuff the 2of4brif is also
in the public domain.

The 60 level includes all words appearing in at least 2 of the 12
dictionaries as indicated by the 12Dicts package.

The 70 level includes Brian's frequency class 0 and the 74,550 common
dictionary words from the MWords package.  The common dictionary words,
like those from the 12Dicts package, have had all likely inflections
added.  The 70 level also included the 5desk list from version 4.0 of
the 12Dics package which is in the public domain.

The 80 level includes the ENABLE word list, all the lists in the
ENABLE supplement package (except for ABLE), the "UK Advanced Cryptics
Dictionary" (UKACD), the list of signature words from the YAWL package,
and the 10,196 places list from the MWords package.

The ENABLE package, mainted by M\Cooper <thegrendel@theriver.com>,
is in the Public Domain:

  The ENABLE master word list, WORD.LST, is herewith formally released
  into the Public Domain. Anyone is free to use it or distribute it in
  any manner they see fit. No fee or registration is required for its
  use nor are "contributions" solicited (if you feel you absolutely
  must contribute something for your own peace of mind, the authors of
  the ENABLE list ask that you make a donation on their behalf to your
  favorite charity). This word list is our gift to the Scrabble
  community, as an alternate to "official" word lists. Game designers
  may feel free to incorporate the WORD.LST into their games. Please
  mention the source and credit us as originators of the list. Note
  that if you, as a game designer, use the WORD.LST in your product,
  you may still copyright and protect your product, but you may *not*
  legally copyright or in any way restrict redistribution of the
  WORD.LST portion of your product. This *may* under law restrict your
  rights to restrict your users' rights, but that is only fair.

UKACD, by J Ross Beresford <ross@bryson.demon.co.uk>, is under the
following copyright:

  Copyright (c) J Ross Beresford 1993-1999. All Rights Reserved.

  The following restriction is placed on the use of this publication:
  if The UK Advanced Cryptics Dictionary is used in a software package
  or redistributed in any form, the copyright notice must be
  prominently displayed and the text of this document must be included
  verbatim.

  There are no other restrictions: I would like to see the list
  distributed as widely as possible.

The 95 level includes the 354,984 single words, 256,772 compound
words, 4,946 female names and the 3,897 male names, and 21,986 names
from the MWords package, ABLE.LST from the ENABLE Supplement, and some
additional words found in my part-of-speech database that were not
found anywhere else.

Accent information was taken from UKACD.

The VarCon package was used to create the American, British, Canadian,
and Australian word list.  It is under the following copyright:

  Copyright 2000-2016 by Kevin Atkinson

  Permission to use, copy, modify, distribute and sell this array, the
  associated software, and its documentation for any purpose is hereby
  granted without fee, provided that the above copyright notice appears
  in all copies and that both that copyright notice and this permission
  notice appear in supporting documentation. Kevin Atkinson makes no
  representations about the suitability of this array for any
  purpose. It is provided "as is" without express or implied warranty.

  Copyright 2016 by Benjamin Titze

  Permission to use, copy, modify, distribute and sell this array, the
  associated software, and its documentation for any purpose is hereby
  granted without fee, provided that the above copyright notice appears
  in all copies and that both that copyright notice and this permission
  notice appear in supporting documentation. Benjamin Titze makes no
  representations about the suitability of this array for any
  purpose. It is provided "as is" without express or implied warranty.

  Since the original words lists come from the Ispell distribution:

  Copyright 1993, Geoff Kuenning, Granada Hills, CA
  All rights reserved.

  Redistribution and use in source and binary forms, with or without
  modification, are permitted provided that the following conditions
  are met:

  1. Redistributions of source code must retain the above copyright
     notice, this list of conditions and the following disclaimer.
  2. Redistributions in binary form must reproduce the above copyright
     notice, this list of conditions and the following disclaimer in the
     documentation and/or other materials provided with the distribution.
  3. All modifications to the source code must be clearly marked as
     such.  Binary redistributions based on modified source code
     must be clearly marked as modified versions in the documentation
     and/or other materials provided with the distribution.
  (clause 4 removed with permission from Geoff Kuenning)
  5. The name of Geoff Kuenning may not be used to endorse or promote
     products derived from this software without specific prior
     written permission.

  THIS SOFTWARE IS PROVIDED BY GEOFF KUENNING AND CONTRIBUTORS ``AS IS'' AND
  ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
  IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
  ARE DISCLAIMED.  IN NO EVENT SHALL GEOFF KUENNING OR CONTRIBUTORS BE LIABLE
  FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
  DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
  OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
  LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY
  OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF
  SUCH DAMAGE.
```

### 絵文字の辞書 (dictionaries/emoji-cldr.txt)

Unicode CLDR の絵文字の日本語の名前・キーワード (common/annotations/ja.xml, common/annotationsDerived/ja.xml) から、読みを付けて作りました。Unicode License v3 (SPDX: Unicode-3.0) です。

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

SPDX-License-Identifier: Unicode-3.0
```

### 国名の語 (dictionaries/countries.txt)

かなに割れる国名を英語にするのに使います。ローマ字として最後まで読める語・2文字の略・大文字だけの略・日本語の語のローマ字の途中と重なる語は、Core が読むときに外します（docs/UPSTREAM-1.6.0-AUDIT.md）。[Unicode CLDR](https://github.com/unicode-org/cldr) の `common/main/en.xml` の国・地域名を `tools/make-countries.mjs` で語に分けて作りました。著作権・ライセンスは絵文字の辞書と同じ (Unicode, Inc.、Unicode License v3) です。

### 分野の辞書 (dictionaries/domain-*.txt)

変換の候補に専門用語・固有名詞を足すのに使います。`tools/make-domain-dictionaries.py` で、元の辞書から読みと表記を取り出し、読みで 1 行にまとめ、かなだけで書く語とよく使う語の読みを除いて作りました。

| ファイル | 元データ | 著作権 | ライセンス |
| --- | --- | --- | --- |
| `domain-medical.txt` | [JMED-DICT-mini](https://sociocom.naist.jp/jmed-dict/) ver 1.1.2 (病名・医薬品・部位の各データの上位 10,000 行) | 奈良先端科学技術大学院大学 ソーシャル・コンピューティング研究室 | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| `domain-it.txt` | [IDiME](https://github.com/ssvp003/IDiME) (情報処理学会 情報入試委員会「情報科全教科書用語リスト 第2版」をもとにしたもの) | ssvp003、情報処理学会 情報入試委員会 | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)。この派生データも CC BY-SA 4.0 |
| `domain-stations.txt` | [SKK 辞書](https://github.com/skk-dev/dict) SKK-JISYO.station | MITA Yuusuke、SKK Development Team | GPL-2.0-or-later (Meltype では GPL-3.0-or-later で使う) |
| `domain-propernouns.txt` | SKK 辞書 SKK-JISYO.propernoun | MITA Yuusuke、SKK Development Team | GPL-2.0-or-later (同上) |
| `domain-names.txt` | SKK 辞書 SKK-JISYO.jinmei・SKK-JISYO.fullname | Wnn Consortium、SKK Development Team、Akie Kaidoh | GPL-2.0-or-later (同上) |
| `domain-propernouns-sudachi.txt`・`domain-words-sudachi.txt` | [SudachiDict](https://github.com/WorksApplications/SudachiDict) core (20261015) の固有名詞 (一般)・普通名詞 (正規化表記と同じ表記だけ) | Works Applications Co., Ltd.。一部は UniDic (Copyright (c) 2011-2013, The UniDic Consortium) | Apache License 2.0。UniDic 由来の部分は BSD-3-Clause (下の全文) |
| `domain-laws.txt` | [e-Gov 法令検索](https://laws.e-gov.go.jp/) の法令 API の、今ある法律の題名と読み | デジタル庁 | [公共データ利用規約 (第1.0版)](https://www.digital.go.jp/resources/open_data/public_data_license_v1.0) (CC BY 4.0 と互換)。出典: e-Gov 法令検索。題名と読みを取り出して加工した |

どの辞書も、Mozc が読みから出せる語を除いています (Mozc の変換ヘルパーに読みを変換させて比べた)。SudachiDict は漢字を含む表記だけを入れ、人名 (固有名詞,人名。まれな字の組み合わせが多く、名前として確かめられない) と地名 (固有名詞,地名,一般。町名・字名がほとんど) は入れていません。

SudachiDict に含まれる UniDic のライセンス (SudachiDict の LEGAL):

```
Copyright (c) 2011-2013, The UniDic Consortium
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

 * Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.

 * Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the
   distribution.

 * Neither the name of the UniDic Consortium nor the names of its
   contributors may be used to endorse or promote products derived
   from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


- src/main/text/core_lex.csv and src/main/text/notcore_lex.csv contain
a part of NEologd (https://github.com/neologd/mecab-unidic-neologd).

Copyright (C) 2015-2019 Toshinori Sato (@overlast)

      https://github.com/neologd/mecab-unidic-neologd

    i. 本データは、株式会社はてなが提供するはてなキーワード一覧ファイル
       中の表記、及び、読み仮名の大半を使用している。

       はてなキーワード一覧ファイルの著作権は、株式会社はてなにある。

       はてなキーワード一覧ファイルの使用条件に基づき、また、
       データ使用の許可を頂いたことに対する感謝の意を込めて、
       以下に株式会社はてなおよびはてなキーワードへの参照をURLで示す。

       株式会社はてな : http://hatenacorp.jp/information/outline

       はてなキーワード :
       http://developer.hatena.ne.jp/ja/documents/keyword/misc/catalog

   ii. 本データは、日本郵便株式会社が提供する郵便番号データ中の表記、
       及び、読み仮名を使用している。

       日本郵便株式会社は、郵便番号データに限っては著作権を主張しないと
       述べている。

       日本郵便株式会社の郵便番号データに対する感謝の意を込めて、
       以下に日本郵便株式会社および郵便番号データへの参照をURLで示す。

       日本郵便株式会社 :
         http://www.post.japanpost.jp/about/profile.html

       郵便番号データ :
         http://www.post.japanpost.jp/zipcode/dl/readme.html

  iii. 本データは、スナフキん氏が提供する日本全国駅名一覧中の表記、及び
       読み仮名を使用している。

       日本全国駅名一覧の著作権は、スナフキん氏にある。

       スナフキん氏は 「このデータを利用されるのは自由ですが、その際に
       不利益を被ったりした場合でも、スナフキんは一切責任は負えません
       ことをご承知おき下さい」と述べている。

       スナフキん氏に対する感謝の意を込めて、
       以下に日本全国駅名一覧のコーナーへの参照をURLで示す。

       日本全国駅名一覧のコーナー :
         http://www5a.biglobe.ne.jp/~harako/data/station.htm

   iv. 本データは、工藤拓氏が提供する人名(姓/名)エントリデータ中の、
       漢字表記の姓・名とそれに対応する読み仮名を使用している。

       人名(姓/名)エントリデータは被災者・安否不明者の人名の
       表記揺れ対策として、Mozcの人名辞書を活用できるという
       工藤氏の考えによって提供されている。

       工藤氏に対する感謝の意を込めて、
       以下にデータ本体と経緯が分かる情報への参照をURLで示す。

       人名(姓/名)エントリデータ :
         http://chasen.org/~taku/software/misc/personal_name.zip

       上記データが提供されることになった経緯
         http://togetter.com/li/111529

    v. 本データは、Web上からクロールした大量の文書データから抽出した
       表記とそれに対応する読み仮名のデータを含んでいる。

       抽出した表記とそれに対応する読み仮名の組は、上記の i. から iv.
       の言語資源の組み合わせによって得られる組のみを採録した。

       Web 上に文書データを公開して下さっている皆様に感謝いたします。

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

### 漢字の構成要素 (dictionaries/kanji-parts.txt)

部首の呼び名から漢字を探すのに使います。[KRADFILE](https://www.edrdg.org/krad/kradinf.html) (漢字の構成要素) と [KANJIDIC2](https://www.edrdg.org/wiki/index.php/KANJIDIC_Project) (使われる頻度・画数) から `tools/make-kanji-parts.mjs` で作りました。著作権・ライセンスは英訳の候補と同じ (EDRDG、CC BY-SA 4.0) で、この派生データも CC BY-SA 4.0 です。KRADFILE の著作権: Copyright 2001/2007 Michael Raine, James Breen and the Electronic Dictionary Research & Development Group。

### そのほかの辞書

`dictionaries/` の辞書 (日本語・英語の単語、固有名詞、同音異義語の候補、文脈の手がかり) は Meltype のために作成したもので、
Meltype 本体と同じライセンス (GPL-3.0-or-later) です。
固有名詞の辞書に含まれる製品名・会社名は各社の商標です。

### Mozc-UT 辞書 (同梱しない。ユーザーが任意で用意するもの)

Meltype は **Mozc-UT 辞書を同梱していません**。Mozc-UT 辞書 (mozcdic-ut.txt など) は、[merge-ut-dictionaries](https://github.com/utuhiro78/merge-ut-dictionaries) などを使ってユーザー自身が用意する任意のデータです。Meltype は、環境変数 `MELTYPE_UT_DICTIONARY` で指定されたファイルを、そのユーザーの手元の Mozc ビルドにマージするだけです。Meltype の配布物には UT 辞書のデータは含まれず、Meltype が UT 辞書をダウンロードすることもありません。

| 部品 | ライセンス | 同梱先 |
| --- | --- | --- |
| Mozc-UT 辞書 (mozcdic-ut.txt など。複数の辞書を連結したもの) | 辞書ごとに異なります (下の表)。CC BY-SA・Apache-2.0・パブリックドメイン・GPL-2.0-only などが混在しています | 同梱しません。`MELTYPE_UT_DICTIONARY` でユーザーが指定したファイルだけを、そのユーザーの環境でビルドする Mozc にマージします |

#### UT 辞書を構成する辞書

| 辞書 | ライセンス | 入手元 |
| --- | --- | --- |
| 人名辞書 (personal names) | Apache-2.0 (リポジトリの表示) | https://github.com/utuhiro78/mozcdic-ut-personal-names |
| 地名辞書 (place names) | パブリックドメイン相当 (日本郵便が郵便番号データについて著作権を主張していない) | https://github.com/utuhiro78/mozcdic-ut-place-names / 日本郵便「郵便番号データの説明」 https://www.post.japanpost.jp/zipcode/dl/readme.html |
| SudachiDict | Apache-2.0 (UniDic の BSD-3-Clause のデータと、NEologd 由来のデータを含みます) | https://github.com/utuhiro78/mozcdic-ut-sudachidict / https://github.com/WorksApplications/SudachiDict/blob/develop/LEGAL |
| jawiki (日本語版ウィキペディア由来) | CC BY-SA (**バージョンの記載がありません**) | https://github.com/utuhiro78/mozcdic-ut-jawiki / https://ja.wikipedia.org/wiki/Wikipedia:ウィキペディアを二次利用する |
| NEologd | Apache-2.0 (はてなキーワード・駅名一覧・人名データなど、第三者のデータを含みます) | https://github.com/utuhiro78/mozcdic-ut-neologd / https://github.com/neologd/mecab-ipadic-neologd/blob/master/COPYING |
| EDICT2 | CC BY-SA 4.0 (Electronic Dictionary Research and Development Group) | https://github.com/utuhiro78/mozcdic-ut-edict2 / https://www.edrdg.org/edrdg/licence.html |
| SKK-JISYO | GPL-2.0-or-later | https://github.com/utuhiro78/mozcdic-ut-skk-jisyo / https://github.com/skk-dev/dict |
| alt-cannadic | GPL-2.0 (only。**Meltype では使用できません**) | https://github.com/utuhiro78/mozcdic-ut-alt-cannadic |

#### UT 辞書を有効にしたビルドを配布する場合

Meltype は UT 辞書入りのビルドを配布しません。配布するのは、そのビルドを作ったあなた自身です。

- **CC BY-SA の部分 (jawiki・EDICT2)**: 出典 (ウィキペディアの記事へのリンク、EDRDG への謝辞) を表示し、改変したこと (コスト・品詞 ID の書き換え、重複の削除、語の取捨選択) を明示し、ライセンス正文または URI を同梱し、二次的著作物も同じ条件で許諾してください (ShareAlike)。EDRDG の辞書は「最新版に更新する手続きを用意すること」「EDRDG の著作権を自分のものと主張しないこと」も求めています。jawiki 辞書は CC BY-SA のバージョンを明記しておらず、CC BY-SA 3.0 のデータは GPLv3 と組み合わせることができません。
- **Apache-2.0 の部分 (SudachiDict・NEologd・人名辞書・UT のコード)**: 著作権表示・ライセンス正文・NOTICE を保持してください。SudachiDict と NEologd には、UniDic (BSD-3-Clause) と、はてなキーワード・駅名一覧・人名データなど第三者のデータが含まれます。上流の `LEGAL` と `COPYING` の帰属表示もあわせて同梱してください。
- **GPL-2.0-or-later の部分 (SKK-JISYO)**: GPL の条件 (ソースの提供など) に従ってください。
- **GPL-2.0 (only) の部分 (alt-cannadic)**: GPLv2 は GPLv3 と互換ではないため、**Meltype (GPL-3.0-or-later) と組み合わせて配布することはできません**。alt-cannadic を含む UT 辞書は使用しないでください。
- **パブリックドメイン相当の部分 (地名辞書)**: 日本郵便は郵便番号データについて著作権を主張していませんが、日本郵便への謝意を明記することをおすすめします。
- **GPLv3 (Meltype 本体)**: UT 辞書を入れたバイナリを配布する場合、Meltype 本体の GPL-3.0-or-later の条件 (対応するソースの提供など) も満たす必要があります。

#### Mozc 本体の辞書

Mozc の変換エンジンに同梱される Mozc 本体の辞書 (IPAdic (NAIST)・ICOT・沖縄辞書) のライセンスは、UT 辞書を追加しても変わりません。`app\mozc\MOZC-CREDITS.html` の記載を引き続き同梱してください。

#### 公式の成果物ではありません

UT 辞書を有効にしてビルドしたバイナリは、**あなた自身のビルド**です。Meltype の公式な配布物ではなく、Meltype のサポートや動作保証の対象でもありません。

### README の画像

- `docs/meltype.jpg`・`docs/images/logo.png` (Meltype のロゴ) は [@Crysta1221](https://github.com/Crysta1221) さんの作品です。
- `docs/images/headings/` の画像 (README と docs/ の見出し) と `docs/images/demo.svg`・`docs/images/features.svg` の文字は、M PLUS Rounded 1c (Copyright 2016 The M+ Project Authors、SIL Open Font License 1.1) の字形を図形にしたものです。

## Rust辞書ストレージの追加部品

システム辞書FSTにはfst、ユーザー辞書にはrusqliteとbundled SQLiteを静的に組み込みます。SQLite本体はpublic domainです。辞書データのライセンスは変わりません。以下は追加したRustライブラリの通知です。

### fst-0.4.7

```text
The MIT License (MIT)

Copyright (c) 2015 Andrew Gallant

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

### rusqlite-0.37.0

```text
Copyright (c) 2014 The rusqlite developers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

### libsqlite3-sys-0.35.0

```text
Copyright (c) 2014 The rusqlite developers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

### fallible-iterator-0.3.0

```text
Copyright (c) 2015 The rust-openssl-verify Developers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### fallible-streaming-iterator-0.1.9

```text
Copyright (c) 2016 The fallible-streaming-iterator Developers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### hashlink-0.10.0

```text
This work is derived in part from the `linked-hash-map` crate, Copyright (c)
2015 The Rust Project Developers

Permission is hereby granted, free of charge, to any
person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the
Software without restriction, including without
limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software
is furnished to do so, subject to the following
conditions:

The above copyright notice and this permission notice
shall be included in all copies or substantial portions
of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

## shunti local conversion engine

Source: https://github.com/shuntilettuce/Japanese-IME-for-HarmonyOS-next/tree/14801bffb90c9ea5f2dd0f594943a97ea35c8a1a

Android・iOS・ChromeOSに組み込む変換エンジンです。Windows・macOS・Linuxの追加パッケージにも使います。モデル・辞書は本体に同梱しません。任意導入データの出典・条件はソースの `rust/ai/vendor/shunti/THIRD_PARTY_NOTICES.md` と `DATA_SOURCES.md`、設定の導入確認を参照してください。

```text
MIT License

Copyright (c) 2026 shuntilettuce

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

This license covers the application code (.ets/.ts files and build
configuration) authored for this project. Bundled dictionary and
connection-cost data are derived from third-party sources under their own
licenses (public domain, CC BY-SA 4.0, BSD-3-Clause, Apache License 2.0) --
see THIRD_PARTY_NOTICES.md and DATA_SOURCES.md for the full breakdown and
attribution.

```

### shunti data attribution (data downloaded separately)

# Third-Party Notices

This project ships derived data generated from the mozc project's
open-source dictionary and connection-cost data, augmented with
kanji-spelling vocabulary from JMdict and SudachiDict (see
`tools/mozc_data/build_mozc_engine.py`, `tools/mozc_data/
build_jmdict_augment.py`, and `tools/mozc_data/build_sudachi_augment.py`),
used to build the optional "統計データ" (mozc-derived) conversion engine,
which the user can enable alongside the app's own hand-built dictionary
via a settings toggle. The generated files
(`entry/src/main/resources/rawfile/mozc_dict.json`, `mozc_costs.json`,
`mozc_matrix.json`) are derived works of the data below and are covered by
the same license terms.

(An earlier round also fetched and merged in real mecab-ipadic's own
connection-cost matrix directly from https://github.com/taku910/mecab --
that merge was later superseded by using mozc's own matrix unreduced,
which measured better; see `tools/mozc_data/build_mozc_engine.py`'s module
docstring and git history. mozc's own bundled dictionary text is still
itself IPAdic-derived per mozc's own documentation, which is what the
"IPAdic-derived dictionary entries" section below covers -- that one is
unrelated to the now-removed direct fetch and still applies.)

The AI conversion engine's dictionary **shuntorge** (`kkc_lex.bin`, distributed
via GitHub Releases and fetched by `tools/fetch_ai_assets.py`) also contains
mozc's dictionary entries, connection costs, and symbol / emoji / emoticon
tables, plus readings from SudachiDict; the mozc, IPAdic, Okinawa Dictionary
and SudachiDict terms below apply to those parts as well. The AI conversion
model **shuntelligence** (`kkc_model.bin`) is licensed under CC BY 4.0
(attribution: "shuntelligence (shuntilettuce)"); the inference code in
`entry/src/main/cpp/` is MIT.

The English-word candidates in shuntorge (e.g. いんじぇくしょん → injection) were
checked for coverage against **The CEFR-J Wordlist Version 1.5**, compiled by
Yukio Tono, Tokyo University of Foreign Studies (copyright Tono Laboratory, TUFS;
free for research and commercial use with citation; retrieved via
https://github.com/openlanguageprofiles/olp-en-cefrj). The katakana readings
are this project's own.

shuntorge also contains:

- **Proper nouns from Japanese Wikipedia** (people, organizations, works,
  surnames / given names, and the original English titles of katakana
  article titles), extracted from the article leads ("題名（よみ）") of the
  jawiki dump of 2026-10-01 (https://dumps.wikimedia.org/jawiki/). Wikipedia
  text is licensed under **CC BY-SA 4.0**
  (https://creativecommons.org/licenses/by-sa/4.0/); attribution:
  "Wikipedia contributors, Japanese Wikipedia". Because of this, the
  dictionary file `kkc_lex.bin` as a whole is distributed under CC BY-SA 4.0
  as well (the same share-alike terms already apply through JMdict).
  This project's own parts of shuntorge are also available under MIT on
  their own.
- **Common misreadings and reading variants from JMdict/EDICT** (e.g.
  げっきょく → 月極 from JMdict's "irregular reading" marks, and alternative
  readings such as ばえる → 映える), under **CC BY-SA 4.0**; see the
  "JMdict/EDICT" section below for the attribution and EDRDG policy. The
  JMdict copy is refreshed whenever shuntorge is rebuilt.
- **Readings of Latin-script words** (Win, GitHub, Excel …) from
  **mecab-ipadic-NEologd** (https://github.com/neologd/mecab-ipadic-neologd,
  `seed/mecab-user-dict-seed.20200910.csv.xz`), Copyright (C) 2015-2019
  Toshinori Sato (@overlast), under the **Apache License 2.0**. NEologd uses the
  spellings and readings of the Hatena Keyword list (株式会社はてな,
  http://developer.hatena.ne.jp/ja/documents/keyword/misc/catalog) and other
  sources listed in its COPYING file. Only Latin-script nouns that appear 30+
  times in FineWeb-2 Japanese were taken; whether the Latin spelling ranks first
  was decided from word counts in this project's training text.
- **Postal codes → addresses** from Japan Post's postal code data
  (`utf_ken_all.csv`, https://www.post.japanpost.jp/zipcode/). Japan Post
  states: "郵便番号データに限っては日本郵便株式会社は著作権を主張しません。
  自由に配布していただいて結構です。"
- **Emoji names** from the Unicode CLDR (cldr-json 48, Japanese
  `annotations` / `annotationsDerived`) and the order in Unicode's
  `emoji-test.txt` (18.0), under the **Unicode License v3**
  (https://www.unicode.org/license.txt). Copyright © 1991-2026 Unicode, Inc.
  Emoji were ranked by how often they appear in the FineWeb-2 Japanese test
  split (ODC-By 1.0); no FineWeb text is distributed.

Sources:
- https://github.com/google/mozc (branch: master, `src/data/dictionary_oss/`)
- https://www.edrdg.org/ (JMdict, via http://ftp.edrdg.org/pub/Nihongo/JMdict.gz)
  — used only to append extra kanji-spelling candidates to readings mozc's
  own dictionary already recognises (`tools/mozc_data/build_jmdict_augment.py`).
  See the "JMdict (CC BY-SA 4.0)" section below for the license text and
  the attribution/share-alike/update-mechanism terms this data carries.
- https://github.com/WorksApplications/SudachiDict (lexicon CSVs fetched
  from the project's own distribution bucket, see
  `tools/mozc_data/fetch_sudachi.py`) — used the same way as JMdict, only
  to append extra kanji-spelling candidates to readings mozc's own
  dictionary already recognises
  (`tools/mozc_data/build_sudachi_augment.py`). See the "SudachiDict
  (Apache License 2.0)" section below.

---

## mozc (connection cost data, POS definitions, and Google-authored
## dictionary additions) — BSD-3-Clause

```
Copyright 2010-2018, Google Inc.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.
  * Redistributions in binary form must reproduce the above
    copyright notice, this list of conditions and the following disclaimer
    in the documentation and/or other materials provided with the
    distribution.
  * Neither the name of Google Inc. nor the names of its
    contributors may be used to endorse or promote products derived from
    this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## IPAdic-derived dictionary entries (files: `dictionary*.txt`) — NAIST /
## ICOT license (permissive)

```
Copyright 2000, 2001, 2002, 2003 Nara Institute of Science
and Technology.  All Rights Reserved.

Use, reproduction, and distribution of this software is permitted.
Any copy of this software, whether in its original form or modified,
must include both the above copyright notice and the following
paragraphs.

Nara Institute of Science and Technology (NAIST),
the copyright holders, disclaims all warranties with regard to this
software, including all implied warranties of merchantability and
fitness, in no event shall NAIST be liable for
any special, indirect or consequential damages or any damages
whatsoever resulting from loss of use, data or profits, whether in an
action of contract, negligence or other tortuous action, arising out
of or in connection with the use or performance of this software.

A large portion of the dictionary entries
originate from ICOT Free Software.  The following conditions for ICOT
Free Software applies to the current dictionary as well.

Each User may also freely distribute the Program, whether in its
original form or modified, to any third party or parties, PROVIDED
that the provisions of Section 3 ("NO WARRANTY") will ALWAYS appear
on, or be attached to, the Program, which is distributed substantially
in the same form as set out herein and that such intended
distribution, if actually made, will neither violate or otherwise
contravene any of the laws and regulations of the countries having
jurisdiction over the User or the intended distribution itself.

NO WARRANTY

The program was produced on an experimental basis in the course of the
research and development conducted during the project and is provided
to users as so produced on an experimental basis.  Accordingly, the
program is provided without any warranty whatsoever, whether express,
implied, statutory or otherwise.  The term "warranty" used herein
includes, but is not limited to, any warranty of the quality,
performance, merchantability and fitness for a particular purpose of
the program and the nonexistence of any infringement or violation of
any right of any third party.

Each user of the program will agree and understand, and be deemed to
have agreed and understood, that there is no warranty whatsoever for
the program and, accordingly, the entire risk arising from or
otherwise connected with the program is assumed by the user.

Therefore, neither ICOT, the copyright holder, or any other
organization that participated in or was otherwise related to the
development of the program and their respective officials, directors,
officers and other employees shall be held liable for any and all
damages, including, without limitation, general, special, incidental
and consequential damages, arising out of or otherwise in connection
with the use or inability to use the program or any product, material
or result produced or otherwise obtained by using the program,
regardless of whether they have been advised of, or otherwise had
knowledge of, the possibility of such damages at any time during the
project or thereafter.  Each user will be deemed to have agreed to the
foregoing by his or her commencement of use of the program.  The term
"use" as used herein includes, but is not limited to, the use,
modification, copying and distribution of the program and the
production of secondary products from the program.

In the case where the program, whether in its original form or
modified, was distributed or delivered to or received by a user from
any person, organization or entity other than ICOT, unless it makes or
grants independently of ICOT any specific warranty to the user in
writing, such person, organization or entity, will also be exempted
from and not be held liable to the user for any such damages as noted
above as far as the program is concerned.
```

## JMdict/EDICT (kanji-spelling candidate augmentation for the "統計データ"
## engine only, via `tools/mozc_data/build_jmdict_augment.py`) —
## CC BY-SA 4.0

Source: https://www.edrdg.org/ (The Electronic Dictionary Research and
Development Group), file fetched: http://ftp.edrdg.org/pub/Nihongo/JMdict.gz

License: Creative Commons Attribution-ShareAlike 4.0 International
(https://creativecommons.org/licenses/by-sa/4.0/). Full terms and the
EDRDG's own usage policy: https://www.edrdg.org/edrdg/licence.html

This project's use, and how it satisfies the license's conditions:
- **Attribution**: this notice, the in-app "ⓘ ライセンス" screen
  (`SymbolView.ets`), and `DATA_SOURCES.md` each credit JMdict/EDICT by
  name with a link to the EDRDG's project page, as required by both CC
  BY-SA 4.0 and the EDRDG's own stated policy for software/apps using
  these files.
- **ShareAlike**: only the derived, redistributed data
  (`entry/src/main/resources/rawfile/mozc_dict.json`'s appended candidate
  surfaces) is a JMdict-derived work; it is covered by this same CC BY-SA
  4.0 license, consistent with every other file in this repository being
  under a permissive/compatible license already (see the app code's own
  MIT license, noted in the in-app license screen).
- **"Regular updating" requirement**: the EDRDG's policy asks that
  software using JMdict data implement "a procedure for regular updating
  of the data from the most recent versions available." This project does
  not fetch JMdict at runtime (it is baked into a static shipped asset at
  build time, like every other data source here); the update procedure is
  to re-run `tools/mozc_data/fetch_jmdict.py` (which always re-downloads if
  the local cache is cleared) followed by
  `tools/mozc_data/build_jmdict_augment.py` before each release that
  touches track B's data, so the shipped snapshot doesn't go stale
  indefinitely.

Note: JMdict/EDICT was already used, separately, as one of several sources
for the hand-built `dict.json`/`global_dict.json` vocabulary (track A) --
see `DATA_SOURCES.md` and the in-app license screen's existing "変換辞書"
section. The usage documented here is a second, independent, later use of
the same source: appending extra kanji-spelling candidates to track B's
`mozc_dict.json` (see `tools/mozc_data/README.md`'s "JMdict vocabulary
augmentation" section for the mechanism and scope).

## SudachiDict (kanji-spelling candidate augmentation for the "統計データ"
## engine only, via `tools/mozc_data/build_sudachi_augment.py`) —
## Apache License 2.0

Source: https://github.com/WorksApplications/SudachiDict, copyright
2017-2023 Works Applications Co., Ltd. Lexicon CSVs fetched from the
project's own distribution host (see `tools/mozc_data/fetch_sudachi.py`
for the exact URLs, discovered via that host's public bucket listing).

License: Apache License, Version 2.0
(http://www.apache.org/licenses/LICENSE-2.0). SudachiDict's own README
states the project "incorporates UniDic and a part of NEologd" and
distributes the resulting lexicon as a whole under Apache-2.0, without
carving out separate terms for those incorporated components.

This project's use: only the "small" and "core" lexicon tiers (not the
much larger "notcore"/full tier of proper nouns) are scanned for entries
whose reading already exists as a key in `mozc_dict.json`; their kanji
surface is appended as an extra candidate for that reading, exactly like
the JMdict augmentation above (same "never adds a new reading key" safe
mode — see `build_sudachi_augment.py` and `build_jmdict_augment.py`'s
module docstrings, and `tools/mozc_data/README.md`, for why). Apache-2.0
is a permissive license (attribution required, no share-alike/copyleft
obligation on derivative data) — satisfied by this notice, the in-app "ⓘ
ライセンス" screen (`SymbolView.ets`), and `DATA_SOURCES.md`.

## Okinawa Dictionary entries (also within files: `dictionary*.txt`) —
## Public Domain

```
Public Domain Dataです。使用・変更・配布に関しては一切の制限をつけません。
商品などに組み込むことも自由に行なってください。すでにいくつかの辞書には沖縄辞書が採用されています。
勝手ながら、沖縄辞書に寄贈された辞書も in the Public Domain' 扱いとさせていただきます。
```

Source: http://sourceforge.jp/projects/o-dic/

## BIZ UDPGothic (bundled font: `entry/src/main/resources/rawfile/font/
## BIZUDPGothic-Regular.ttf`, also in the Android APK) — SIL Open Font License 1.1

The keyboard and candidate text use BIZ UDPGothic, a universal-design
Japanese font. The bundled file is a derived work: subset to the
characters the app can display, by `tools/build_ud_font.py`. Outlines are
unmodified.

```
Copyright 2022 The BIZ UDGothic Project Authors (https://github.com/googlefonts/morisawa-biz-ud-gothic)

This Font Software is licensed under the SIL Open Font License, Version 1.1.
(The license text is the same as in the Noto Sans JP section below.)
```

Source: https://github.com/google/fonts —
`ofl/bizudpgothic/BIZUDPGothic-Regular.ttf`

## Noto Sans JP (bundled font: `entry/src/main/resources/rawfile/font/
## NotoSansJP-Fallback.ttf`, also in the Android APK) — SIL Open Font License 1.1

The app bundles Japanese fonts because HarmonyOS's own system font ships
only Simplified/Traditional Chinese regional variants — without them,
Han-unified kanji render with Chinese glyph shapes on a China-region
device (see `entry/src/main/ets/components/JpFont.ets`).

This file supplies only the characters BIZ UDPGothic lacks (mostly rarer
kanji). It is a derived work of Noto Sans JP: instanced from the variable
font at `wght=400` and subset to those characters, by
`tools/build_ud_font.py`. Outlines are unmodified.

```
Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name
'Source'. Copyright 2012 Google Inc. All Rights Reserved.

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
https://openfontlicense.org

Permission is hereby granted, free of charge, to any person obtaining a
copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components, in
Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or in
the appropriate machine-readable metadata fields within text or binary
files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any Modified
Version, except to acknowledge the contribution(s) of the Copyright
Holder(s) and the Author(s) or with their explicit written permission.

5) The Font Software, modified or unmodified, in part or in whole, must be
distributed entirely under this license, and must not be distributed under
any other license. The requirement for fonts to remain under this license
does not apply to any document created using the Font Software.
```

Source: https://github.com/google/fonts —
`ofl/notosansjp/NotoSansJP[wght].ttf`

## 紅皿 (Benizara) の配列定義ファイル (files: `desktop/layouts/*.tsv` のうち、見出しに「紅皿 (Benizara) の配列定義ファイル … から書き起こした」とあるもの) — MIT 相当

キーと字の対応を https://github.com/k-ayaki/benizara の .bnz から書き起こした。各配列の考案者は各ファイルの見出し (#! name / #! url) を参照。

```
紅皿のLicense
Copyright (c) 2018 綾木健一郎

以下に定める条件に従い、IME.ahkおよび PATH.ahkを除く本ソフトウェアおよび関連文書のファイル（以下「ソフトウェア」）の複製を取得するすべての人に対し、ソフトウェアを無制限に扱うことを無償で許可します。これには、ソフトウェアの複製を使用、複写、変更、結合、掲載、頒布、サブライセンス、および/または販売する権利、およびソフトウェアを提供する相手に同じことを許可する権利も無制限に含まれます。

上記の著作権表示および本許諾表示を、ソフトウェアのすべての複製または重要な部分に記載するものとします。

ソフトウェアは「現状のまま」で、明示であるか暗黙であるかを問わず、何らの保証もなく提供されます。ここでいう保証とは、商品性、特定の目的への適合性、および権利非侵害についての保証も含みますが、それに限定されるものではありません。 作者または著作権者は、契約行為、不法行為、またはそれ以外であろうと、ソフトウェアに起因または関連し、あるいはソフトウェアの使用またはその他の扱いによって生じる一切の請求、損害、その他の義務について何らの責任も負わないものとします。
```

# 辞書データの出典一覧

`entry/src/main/resources/rawfile/dict.json` および `global_dict.json` の語彙は、
本プロジェクトの開発過程（複数セッションにわたる）で以下の外部データソースから
収集・編纂されました。個々のエントリに出典タグは付与されていないため
（28万件超の規模でエントリ単位のタグ付けは非現実的）、本ファイルが
出典追跡の唯一の記録です。git のコミット履歴と合わせて参照してください。

**免責事項**: 本ファイルは開発時点での把握内容をまとめたものであり、法的助言では
ありません。公開配布前には、特に JMdict（下記）のシェアアライク条項について
確認することを推奨します。

**関連ファイル**: `entry/src/main/resources/rawfile/mozc_dict.json` /
`mozc_costs.json` / `mozc_matrix.json`（設定でオプション有効化する第二変換
エンジン「統計データ」用、track Aとは完全に別データ）の出典は
`THIRD_PARTY_NOTICES.md` および `tools/mozc_data/README.md` を参照。JMdict
のみ両トラックで（別々の目的に）利用されているため、下記3.に両方の利用内容を
記載しています。

---

## 1. Wikipedia日本語版 (ja.wikipedia.org)

- **ライセンス**: CC BY-SA 4.0（一部旧版は3.0）
- **利用内容**: 記事本文を MeCab で形態素解析し、単語の「読み」を抽出。記事の
  文章そのものは保存・再配布していない（読み↔漢字の対応関係のみを抽出）。
  駅名・地名・専門用語カテゴリの網羅的収集に使用。
- **関連コミット**: `c38514d`〜`f1cb947`（本セッション、バッチ11〜16、駅・
  市区町村・廃駅・難語）、`db49fab`〜`021c4f1`（バッチ3〜10）、`edd5ab9`
  （Wikipedia corpus deep expansion）等
- **帰属表示**: アプリ内ライセンス画面（SymbolView.ets）・プライバシー
  ポリシー画面（PrivacyPolicy.ets）に記載済み

## 2. Wiktionary日本語版 (ja.wiktionary.org)

- **ライセンス**: CC BY-SA 4.0 + GFDL（デュアルライセンス）
- **利用内容**: 見出し語・読みの抽出（初期の dict.json 構築に使用）
- **関連コミット**: `4ff3f6e`, `48c59ba`, `a6cf6b0`, `2b45b58`, `a2cf1df`
- **帰属表示**: 記載済み — アプリ内ライセンス画面（`SymbolView.ets`）

## 3. JMdict/EDICT（電子化辞書研究開発グループ, EDRDG）

- **ライセンス**: Creative Commons Attribution-ShareAlike 4.0
- **利用内容 (track A, dict.json/global_dict.json)**: 「196K canonical
  dictionary, priority entries cross-referenced」として語彙収集に利用
- **利用内容 (track B, mozc_dict.json — 別セッション、後日追加)**:
  `tools/mozc_data/build_jmdict_augment.py` が、mozc/実ipadic統計データ
  エンジン（独自辞書とは完全に別データの、設定でオプション有効化する
  第二変換エンジン）向けに、既存の読みへの追加の候補表記（漢字表記の
  バリエーション）としてのみ利用。新しい読みキーの追加は一切行わない
  （DPのセグメンテーション挙動に影響を与えないための制約、詳細は
  `tools/mozc_data/build_jmdict_augment.py` のモジュールdocstring参照）。
- **関連コミット**: `1996469`（corpus blitz + JMdict expansion, track A）
- **帰属表示**: 記載済み — アプリ内ライセンス画面（`SymbolView.ets`
  「ⓘ ライセンス」、変換辞書セクションとtrack Bの統計データセクション
  両方）、`THIRD_PARTY_NOTICES.md`（track B分の全文条件を記載）、
  本ファイル。EDRDG公式要件（ソフトウェア／アプリでファイルを使用する
  場合、ドキュメント・宣伝資料・ウェブサイト等で使用と出典を明記する
  必要がある）を満たす。推奨URL:
  https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project
- **シェアアライク条項**: JMdict由来のデータを含む派生物（dict.json/
  global_dict.json の該当エントリ、および mozc_dict.json への追加候補）
  は CC BY-SA 4.0 の下で配布される（このリポジトリの他のデータ・コードは
  元々MIT/BSD等の互換ライセンスであり、抵触しない）。
- **定期更新要件**: EDRDG方針は「入手可能な最新版からのデータの定期的な
  更新手順」の実装を求めている。本プロジェクトは実行時取得ではなく
  ビルド時に静的アセットへ焼き込む方式のため、`tools/mozc_data/
  fetch_jmdict.py` を毎リリース前に再実行（キャッシュを消してから）
  して最新スナップショットを取り込む運用とする。

## 4. Aozora Bunko（青空文庫）

- **ライセンス**: 収録作品ごとに異なる（著作権保護期間満了＝パブリック
  ドメイン、または著作権者の許諾に基づく無料公開）
- **利用内容**: 古典文学作品から文語・難読語彙を収集
- **関連コミット**: `5172741`（Aozora+Wikipedia）, `1996469`（同上に言及）

## 5. 日本郵便 郵便番号データ (KEN_ALL.CSV)

- **ライセンス**: 日本郵便が無償公開する公共データ、自由利用可
- **利用内容**: 全国の町域名・読みの網羅的収集（区より細かい地名）。
  加えて、`tools/build_place_packs.py` がこのデータと照合して
  「どの読みが町域名か」と「どの都道府県のものか」を判定し、
  地名を8地方の語彙パック (`tools/vocab_packs/place_*.pack`) に分割している。
  接尾辞だけの判定では 拡張漢字・英数字・企業城下町 のような一般語を
  巻き込むため、表記と読みの両方が郵便データに存在するものだけを地名と
  確定している。
- **取得元**: 都道府県別ファイル
  <https://www.post.japanpost.jp/service/search/zipcode/download/kogaki-zip.html>
  （全国一括 `ken_all.zip` は 2026-08 時点で 404 だったため県別を使用。
  都道府県はファイル名ではなくレコード内の都道府県欄から取る）
- **関連コミット**: `46e6ae1`（初回収集）
- **帰属表示**: アプリ内ライセンス画面に記載済み

## 5b. Wikidata

- **ライセンス**: CC0 1.0（パブリックドメイン提供）
- **利用内容**: `tools/build_place_core.py` が2つの一覧を取得し、
  「地方パックを切っても引けるべき地名」の判定にのみ使用する。
  1つは**現行の市区町村**（廃止日 P576 を持たないもの／1,727件）、
  もう1つは**日本の鉄道駅とそのかな表記**（P1814／7,218件）。
  駅は表記から「駅」・読みから「えき」を落とした**基底の地名**だけを
  取り、駅名そのもの（「米原駅」）は登録しない。地名さえあれば
  「米原」＋「駅」で組めるうえ、駅名を単位で持つと重複が増えるため
  （以前この形の重複を7,486件削除している）。
- **取得方法**: SPARQL エンドポイント <https://query.wikidata.org/sparql>。
  問い合わせ文は `tools/build_place_core.py` の `QUERY_MUNI` /
  `QUERY_STATION` にそのまま置いてある。取得結果は
  `tools/.cache/wikidata/`（git 管理外）に残り、2回目以降は通信しない。
- **帰属表示**: CC0 のため法的な義務は無いが、出典として本ファイルに記載。

## 6. 常用漢字表（文部科学省告示）

- **ライセンス**: パブリックドメイン（政府告示）
- **帰属表示**: アプリ内ライセンス画面に記載済み

## 7. Unicode Unihan データベース

- **ライセンス**: Unicode, Inc. のライセンス（データ利用は一般的に許可、
  Unicode社の利用条件表示が求められる場合がある）
- **帰属表示**: アプリ内ライセンス画面に記載済み

## 8. SudachiDict（Works Applications Co., Ltd.）

- **ライセンス**: Apache License 2.0
- **利用内容 (track B, mozc_dict.json のみ)**: `tools/mozc_data/
  build_sudachi_augment.py` が、統計データエンジン向けに、small/core
  レキシコンCSVから既存の読みへの追加の候補表記（漢字表記のバリエー
  ション）としてのみ利用。JMdictと同じ「新しい読みキーは追加しない」
  安全モード。track Aのdict.json/global_dict.jsonでは未使用。
- **帰属表示**: 記載済み — アプリ内ライセンス画面（`SymbolView.ets`
  「ⓘ ライセンス」track Bセクション）、`THIRD_PARTY_NOTICES.md`
  （全文条件を記載）、本ファイル。
- **注記**: Apache-2.0は許諾的ライセンスで、JMdictのCC BY-SAと異なり
  シェアアライク（同一ライセンスでの再配布）義務はない。帰属表示のみ
  required。

---

## 9. AI変換（shuntelligence / shuntorge）

AI変換（設定 → 変換エンジン → AI変換）のモデル **shuntelligence**（`kkc_model.bin`）と辞書 **shuntorge**
（`kkc_lex.bin`）は GitHub Releases に置き、`tools/fetch_ai_assets.py` で取得する（`tools/ai_assets.json` に SHA-256）。
学習のコードと教材は公開していない。モデルの重みと推論のコード（`entry/src/main/cpp/`）だけを公開する。

### モデル shuntelligence — CC BY 4.0

約630万パラメータの Transformer。学習に使った文章（モデルの重みに文章そのものは含まれない。日本の著作権法第30条の4に基づく情報解析）:

| 出典 | 条件 |
|---|---|
| おーぷん2ちゃんねる（open2ch-dialogue-corpus） | サイトが「投稿は著作権を放棄しパブリックドメイン」と明記。URL・顔文字・コピペ等を掃除し、差別語を含む投稿は除外、罵詈雑言は 1/10 に間引いた |
| GitHub（codeparrot/github-code-clean） | MIT・Apache-2.0・BSD・ISC・Unlicense・CC0 のファイルのみ |
| 青空文庫（globis-university/aozorabunko-clean） | 著作権の切れた作品のうち新字新仮名のもの |
| Wikipedia日本語版 | CC BY-SA 4.0。本文の段落のみ |
| FineWeb-2 日本語（HuggingFaceFW/fineweb-2、jpn_Jpan） | ODC-By 1.0（元の Common Crawl の利用規約にも従う）。本文のみ |

- 読み付けは SudachiPy + SudachiDict-core（Apache License 2.0）。
- 書き分けの強化の参考: 文化庁「「異字同訓」の漢字の使い分け例（報告）」（平成26年、文化審議会国語分科会。
  出典: 文化庁ホームページ。用例から語の組み合わせを取り出して加工し、同じ組み合わせを含む上記の文章を探す手掛かりに使用。
  用例の文そのものは学習に入れていない）、Wikipedia「助数詞」の一覧（CC BY-SA 4.0、名詞と助数詞の組を同様に手掛かりとして使用）、
  Wikidata（CC0、生き物の和名と分類）。
- 評価だけに使ったもの（学習には入れていない）: AJIMEE-Bench（CC BY-SA 3.0）、開発者本人の入力ログ（集計値のみ）。

### 辞書 shuntorge — 辞書ファイル全体は CC BY-SA 4.0（独自部分は単独では MIT）

下の表の Wikipedia日本語版と JMdict/EDICT の部分が CC BY-SA 4.0 のため、`kkc_lex.bin` 全体を CC BY-SA 4.0 で配布している。

| 部分 | 出どころ | ライセンス |
|---|---|---|
| 語・品詞のつながりのコスト | mozc OSS 辞書（`src/data/dictionary_oss`） | BSD-3-Clause / IPAdic（NAIST）ライセンス / 沖縄辞書（パブリックドメイン） |
| 記号・絵文字・顔文字 | mozc `symbol.tsv`・`emoji_data.tsv`・`emoticon.tsv` | BSD-3-Clause |
| 手入れ済みの語 | shunti IME 独自辞書のネット語・バス行き先等の節（SudachiDict の読みと一致したもの） | MIT |
| 地名 | shunti IME 地名パック（日本郵便 郵便番号データの町域名と照合） | MIT |
| ネットの読み・技術用語・【速報】等の定型 | 本プロジェクトで作成（定型の読みは SudachiDict） | MIT / SudachiDict（Apache 2.0） |
| 文章から拾った名詞 | 上記の教材に30回以上出て mozc に無い名詞（読みは SudachiDict） | MIT / SudachiDict（Apache 2.0） |
| 英字の語の読み（Win・GitHub・Excel など） | mecab-ipadic-NEologd（`mecab-user-dict-seed.20200910`。はてなキーワード一覧ファイルほかの表記と読み）。FineWeb-2 に30回以上出る英字の名詞のみ。英字を先に出すかは教材の回数で決定 | Apache License 2.0 |
| 英単語の一覧（each・other・hello など、ローマ字で英字のまま打ったときの英字の語） | SCOWL（en-wl/wordlist rel-2026.02.25、Copyright 2000-2026 Kevin Atkinson）の大きさ 35 の語 | MIT に近い許諾（著作権表示と許諾の表示を残す） |
| 人名・組織・作品などの固有名詞と読み | Wikipedia日本語版（2026-10-01 のダンプ、記事の冒頭の「題名（よみ）」）。表示: "Wikipedia contributors, Japanese Wikipedia" | CC BY-SA 4.0 |
| よくある誤読（げっきょく → 月極 など）・読みの揺れ（ばえる → 映える など） | JMdict/EDICT（電子化辞書研究開発グループ、https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project）。誤読は教材の書き込みからも | CC BY-SA 4.0 |

差別語は辞書と候補から除いている。

## 未対応の推奨アクション

1. ~~Wiktionary・JMdictの帰属表示をライセンス画面に追加~~ — 完了
   （両方ともアプリ内ライセンス画面に既に記載されていたことを本セッションで
   確認・修正。track B分のJMdict利用も新規に追記済み）
2. 可能であれば、今後の語彙拡充では出典を都度このファイルに追記する運用にする
