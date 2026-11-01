# Mozc-UT の辞書を使う

[English version: MOZC-UT.en.md](MOZC-UT.en.md)

Meltype の変換エンジン「Mozc」に、[Mozc-UT](https://github.com/utuhiro78/merge-ut-dictionaries) の辞書 (`mozcdic-ut.txt`) を足せます。
人名・地名・ウィキペディアの語などが変換の候補に出るようになります。ソースからビルドするときの任意の機能です。

**既定では統合しません。** 何も指定しなければ、これまでどおり Mozc だけのヘルパーを作ります。
Mozc-UT の辞書を指定したときだけ、Mozc の辞書に足してビルドします。

- Windows と Linux で同じ環境変数を使います: `MELTYPE_UT_DICTIONARY=/path/to/mozcdic-ut.txt`
- Meltype の変換エンジン本体 (C#) は変わりません。変換ヘルパー (`meltype_mozc_helper`) を作り直すだけです。
- 学習データの扱いは変わりません。
- Mac 版は azooKey で変換するため、この機能の対象外です。

## 先に読んでください: ライセンスと再配布

Mozc-UT 辞書は任意の追加データで、**Meltype には同梱していません**。Meltype は、`MELTYPE_UT_DICTIONARY` で指定されたファイルを手元の Mozc ビルドにマージするだけで、UT 辞書をダウンロードも再配布もしません。

UT 辞書はライセンスが混在しています。日本郵便の郵便番号データ (著作権を主張していない)、Apache-2.0 (SudachiDict・NEologd・人名辞書)、CC BY-SA (ウィキペディア由来の辞書・EDICT2)、GPL-2.0-or-later (SKK-JISYO)、GPL-2.0 (alt-cannadic) です。
UT を有効にしたバイナリをビルドして配布する場合、各ライセンスを守る責任は Meltype ではなく、**配布するあなた**にあります。特にウィキペディア由来の部分は CC BY-SA です。出典の表示、改変したことの明示、ライセンス正文またはリンクの同梱、継承 (ShareAlike) の条件を満たしてください。
UT 辞書は CC BY-SA のバージョン (3.0 か 4.0 か) を明記しておらず、CC BY-SA 3.0 のデータは GPLv3 と組み合わせられません。**alt-cannadic は GPL-2.0 のみで、Meltype とは組み合わせられません。**

UT 辞書を入れたバイナリはあなた自身のビルドであり、Meltype の公式成果物でも、サポートや動作保証の対象でもありません。
辞書ごとの詳しい条件は [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) の「Mozc-UT 辞書」にあります。

## UT 辞書を用意する

### merge-ut-dictionaries で作る (推奨)

`mozcdic-ut.txt` は、[merge-ut-dictionaries](https://github.com/utuhiro78/merge-ut-dictionaries) で作ります。
既定の構成では、**巨大なウィキペディアのダンプ (4.2 GB) は取りに行きません**。各辞書のリポジトリにある、あらかじめ作られた `.txt.bz2` を取得して連結します。

```bash
git clone --depth 1 https://github.com/utuhiro78/merge-ut-dictionaries.git
cd merge-ut-dictionaries/src/merge
```

`make.sh` で使う辞書を選びます。既定では `jawiki` も有効になっています。ウィキペディア由来の語を含めるならそのままで、含めないなら `jawiki="true"` を `#jawiki="true"` にします (Meltype の動作確認では、`personal_names`・`place_names`・`sudachidict` の 3 つで試しました)。

```sh
sh make.sh
ls -lh mozcdic-ut.txt
```

補足:

- `make.sh` は `python` を呼びます。`python3` しか無い環境では、`python3` に通してください。
- `merge_dictionaries.py` は、コストの調整のために**現在の Mozc のソース (zip)** と**ウィキペディアの multistream index (約 31 MB)** を取得します。これは記事本文のダンプ (4.2 GB) とは別の小さなファイルです。
- `generate_latest="true"` にすると、各辞書を最新のデータから作り直します。このときは 4.2 GB のダンプを取得するので、通常のビルドの必須条件にはしないでください。

### 作った辞書を記録する

同じ辞書で同じ結果になるように、作ったときの版とハッシュを残してください。Meltype は、統合した内容を `native/mozc/bin/MOZC-UT.json` に書きます (辞書の SHA-256・Mozc の commit・`id.def` の SHA-256・品詞 ID の対応・除外した件数)。

動作確認に使った組み合わせ (再現性のための記録):

| もの | 版 |
| --- | --- |
| merge-ut-dictionaries | `640844881690e877d0e54dff024edb8ec7b77290` |
| 人名辞書 | `b557dd4c1548cc7c2164d5f8b14170c40cbb6f81` |
| 地名辞書 | `98cf8e9d7731695a52e5bc72ed633933c71d1913` |
| SudachiDict 辞書 | `13129ddebe1cb8eabb63feccbe157adec4e1de01` |
| 作った mozcdic-ut.txt | 1,142,950 行、SHA-256 `0c16fa79ca6c4958bb5c4cc7378dadd17d382e987c586b90d82a50e452030c15` |
| Mozc | `a069a88d4cb5c011de0f9aebb6c149a1c808d904` (`native/mozc/MOZC_COMMIT`) |

## Linux でビルドする

必要なものは、これまでのビルドと同じです (`native/mozc/README.md`)。環境変数を付けるだけです。
Mozc-UT の辞書を扱うときは Python 3.10 以降を使います (`PYTHON` で場所を指定できます)。

```bash
MELTYPE_UT_DICTIONARY=~/mozcdic-ut.txt native/mozc/build-mozc-helper.sh
```

`native/mozc/bin/linux/meltype_mozc_helper` が、Mozc-UT の辞書入りになります。

## Windows でビルドする

PowerShell で環境変数を置いてから、いつものスクリプトを実行します。

```powershell
$env:MELTYPE_UT_DICTIONARY = 'C:\path\to\mozcdic-ut.txt'
.\native\mozc\Build-MozcHelper.ps1 -Python python -Bazelisk C:\tools\bazelisk.exe
```

`-UtDictionary` でも指定できます (`-UtDictionary 'C:\path\to\mozcdic-ut.txt'`)。環境変数と引数の両方を指定した場合は引数が優先です。
`native\mozc\bin\` のヘルパーが、Mozc-UT の辞書入りになります。`Build-Package.ps1` / `Install-Meltype.ps1` はこれまでどおり `bin\` を同梱します。

GitHub Actions で作る場合は、`.github/workflows/build.yml` の `mozc` ジョブに環境変数を足します。

```yaml
      - name: Build Mozc helper
        env:
          MELTYPE_UT_DICTIONARY: C:\ut\mozcdic-ut.txt
        run: ...
```

## 有効になったか確かめる

1. `native/mozc/bin/MOZC-UT.json` (Linux は `native/mozc/bin/linux/MOZC-UT.json`) がある。中身の `dictionary.entries_used` が 0 より大きい。
2. その辞書にしか無い語を打って、候補に出る。Linux なら、ヘルパーに直接聞けます。

```bash
printf 'C\t\tいちじょうりゅうのすけ\nQ\n' | native/mozc/bin/linux/meltype_mozc_helper
```

`READY` の次の行に、候補が `<US>` (0x1F) 区切りで出ます。Mozc-UT の辞書を入れたビルドなら `一条龍之介` が候補にあります。標準の Mozc だけのビルドでは出ません。
Windows では Meltype から打って、同じ語が候補に出るかで確かめられます。

## 標準の Mozc に戻す

環境変数を指定せずに、もう一度ビルドします。

```bash
native/mozc/build-mozc-helper.sh
```

```powershell
Remove-Item Env:\MELTYPE_UT_DICTIONARY
.\native\mozc\Build-MozcHelper.ps1 -Python python -Bazelisk C:\tools\bazelisk.exe
```

環境変数を指定しないビルドでは、Mozc-UT の語を入れるファイル (`dictionary_ut.txt`) を空にしてからビルドするので、前回の語は残りません (v1.3.1-pre.1 でビルドしたときは、次の「v1.3.1-pre.1 から更新したとき」)。

## v1.3.1-pre.1 から更新したとき

v1.3.1-pre.1 で `MELTYPE_UT_DICTIONARY` を指定してビルドした場合は、Mozc の `dictionary00.txt` に Mozc-UT の語が残っています。
今のビルドスクリプトは `dictionary00.txt` を書き換えないので、残った語は、環境変数を指定しないビルドにも入ります。UT 辞書を新しくしても、古い辞書の語とコストが残ります。
更新したら一度、次を実行して戻してください。戻したあと環境変数を指定してビルドすれば、Mozc-UT の語はまた入ります。

```bash
git -C ~/mozc checkout HEAD -- src/data/dictionary_oss/dictionary00.txt
```

```powershell
git -C $env:USERPROFILE\mozc checkout HEAD -- src/data/dictionary_oss/dictionary00.txt
```

Mozc を別の場所に置いている場合は、`~/mozc` (Windows は `$env:USERPROFILE\mozc`) をその場所に読み替えてください。

## 性能とサイズ (実測)

Linux x86_64 (6 コア、clang 21.1.8、Bazel 9.0.2、Mozc `a069a88d`) で、`personal_names`・`place_names`・`sudachidict` の 3 つ (1,142,950 行) を統合して測りました。
数値はこの環境のもので、CPU やディスクで変わります。

| | 標準の Mozc | Mozc-UT 入り |
| --- | --- | --- |
| ヘルパーのサイズ | 24,625,632 バイト (23.5 MiB) | 41,351,072 バイト (39.4 MiB) (+68%) |
| 起動 (READY まで、9 回の中央値) | 9.96 ms | 11.5 ms |
| 変換 1 回 (200 回の中央値) | 0.209 ms | 0.213 ms |
| メモリ (RSS、起動直後) | 21.5 MB | 27.8 MB |
| 辞書のテキスト (`dictionary00.txt` + `dictionary_ut.txt`) | 7.2 MB | 76.7 MB |

- 変換の速さはほとんど変わりません (中央値で +2%)。メモリとバイナリのサイズは増えます。
- 統合する辞書を増やすほど、ビルド時間・サイズ・メモリは増えます。
- ビルドのキャッシュ: Bazel は入力の中身でキャッシュを分けるので、標準のビルドと Mozc-UT のビルドを取り違えません。実際に、Mozc-UT でビルドしたあとに標準でビルドし直すと、**最初に作った標準のバイナリと同じバイト列** (SHA-256 `84929176b0f5265c41e64690775398312ff9ed7cd8e41ca43937d70ea9e71c82`) に戻ることを確かめました。

## 仕組み

1. ビルドスクリプトが、Mozc の `src/data/dictionary_oss/BUILD.bazel` の辞書の一覧に `dictionary_ut.txt` を足し、`dictionary_ut.txt` を空にします。Mozc の辞書 (`dictionary00.txt`〜`dictionary09.txt`) は書き換えません。
2. 環境変数を指定したときは、`native/mozc/merge-ut-dictionary.py` が指定された `mozcdic-ut.txt` を読み、Mozc の辞書に足せるか確かめます。
   - UTF-8 か (BOM・NUL バイトは拒否)、5 列か、空の列が無いか
   - 品詞 ID (lid/rid) が `id.def` にあるか、コストが 0〜65535 か
   - 読みがひらがなか (警告。`--strict-readings` でエラー)
3. 品詞 ID を、**ビルドする Mozc の `id.def`** に合わせます。UT 辞書は作ったときの Mozc の ID で書かれているので、版が違えば ID の意味が変わります。`merge-ut-dictionaries` の出力はすべて 1 つの品詞 (既定は「名詞,一般」) なので、その ID が今の Mozc の「名詞,一般」と違えば置き換え、`MOZC-UT.json` に記録します (複数の品詞を使う自前の辞書は、使える ID をそのまま使います)。
4. Mozc の辞書と重複する語 (読みと表記が同じ) と、辞書の中の重複を取り除きます。Mozc の辞書を手元で書き換えていれば、書き換えた中身と比べます。
5. 決まった順に並べて `dictionary_ut.txt` に書きます (前回の中身は残しません)。Mozc は辞書の行の順番を気にしません。
6. Bazel が Mozc の辞書を組み込み直し、ヘルパーが作られます。

Mozc-UT のツール (`merge-ut-dictionaries`) のコードは使っていません。統合の処理は Meltype のために書き下ろしたものです。

## うまくいかないとき

| 出るメッセージ | 意味と対処 |
| --- | --- |
| `辞書ファイルを UTF-8 として読めません` | UTF-8 ではありません。`mozcdic-ut.txt` か確かめてください |
| `辞書ファイルの先頭に BOM があります` | BOM を外してください |
| `列が N 個あります (5 列ではありません)` | `mozcdic-ut.txt` ではありません (読み<Tab>lid<Tab>rid<Tab>コスト<Tab>表記 の 5 列) |
| `品詞 ID が、ビルドする Mozc の id.def にありません` | この Mozc の版に無い ID です。`--keep-pos-ids` を外すか、辞書を作り直してください |
| `Mozc の id.def が見つかりません` | Mozc の取得が途中で終わっています。Mozc のフォルダーを消してやり直してください |
| `BUILD.bazel に dictionary_ut.txt がありません` | `merge-ut-dictionary.py` を直接実行しています。ビルドスクリプトから実行してください |

統合できるかだけ確かめることもできます (`dictionary_ut.txt` を書きません)。
このときも UT 辞書 (100 万行を超えることがあります) と Mozc の辞書を読み込むので、1 GB 前後のメモリを使います。

```bash
python3 native/mozc/merge-ut-dictionary.py --mozc-src ~/mozc/src \
    --dictionary ~/mozcdic-ut.txt --check
```

## 既知の制約

- **Mozc の変換エンジンだけ**が対象です (Windows・Linux の `meltype_mozc_helper`)。Mac 版は azooKey なので対象外です。
- UT 辞書はビルド時に Mozc の辞書へ足す方式です。辞書を切り替えるたびにヘルパーのビルド (辞書の組み込み直し) が要ります。ビルドのキャッシュが効くので、2 回目以降は数分です。
- ビルドスクリプトは Mozc の `src/data/dictionary_oss/BUILD.bazel` に `dictionary_ut.txt` を足したままにします。`dictionary_ut.txt` を消すと、その Mozc のフォルダーで `bazel build` を直接実行したときに失敗します。ビルドはビルドスクリプトから実行してください。
- UT 辞書の品質 (候補の順位・表記) は UT 側のものです。Meltype は並べ替えも選別もしません。
- 辞書のライセンスは辞書ごとに違います。とくに **alt-cannadic (GPL-2.0 のみ) を含む辞書は使わないでください**。
