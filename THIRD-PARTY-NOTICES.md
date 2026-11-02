# サードパーティー通知

Meltype のソースコード (このリポジトリ) には、他者の著作物は含まれていません。

## 配布用パッケージに同梱しているもの

| 部品 | ライセンス | 同梱先 |
| --- | --- | --- |
| [Sparkle](https://sparkle-project.org/) 2.10.0 | MIT License | Mac版の更新通知・署名検証・更新。Sparkle.frameworkとライセンス表示を同梱 |
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

### カタカナで書く語の読みの一覧 (dictionaries/katakana.txt)

日英の区切りで、よく使う語の読みに無いカタカナ語 (ホスティング) を日本語の語として読むのに使います。JMdict のカタカナだけの読みを `tools/make-katakana.mjs` で取り出して作りました。著作権・ライセンスは英訳の候補と同じ (EDRDG、CC BY-SA 4.0) で、この派生データも CC BY-SA 4.0 です。

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
