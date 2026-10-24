# Mozc の変換ヘルパー

Meltype の変換エンジン「Mozc」(設定の「変換エンジン」が 両方 / Mozc のとき) は、[Mozc](https://github.com/google/mozc)
(Google 日本語入力のオープンソース版、BSD-3-Clause) の変換エンジンを使う小さなプログラム `meltype_mozc_helper.exe` を別のプロセスで動かして使います。

| ファイル | 中身 |
| --- | --- |
| `meltype_mozc_helper.cc` | ヘルパーのソース。標準入出力で 変換 (`C`)・学習 (`L`)・保存 (`S`) を受け付ける (やり取りの形式はファイルの先頭に説明) |
| `BUILD.fragment` | Mozc の `src/converter/BUILD.bazel` に足す Bazel の定義 |
| `MOZC_COMMIT` | ビルドする Mozc の commit (動作を確かめた版に固定)。版を上げるときは、`src/data/dictionary_oss/BUILD.bazel` に `"dictionary09.txt",` の行が 1 か所だけあることを確かめる (`dictionary_ut.txt` を足す処理が目印にしている。直す場所は `build-mozc-helper.sh` のコメントに書いてある) |
| `Build-MozcHelper.ps1` | Mozc を取得してヘルパーをビルドし、`bin\` にライセンスと一緒に置く |
| `build-mozc-helper.sh` | 同じことを Linux で行う (`bin/linux/` に置く) |
| `merge-ut-dictionary.py` | Mozc-UT の辞書 (`mozcdic-ut.txt`) を Mozc の辞書に統合する (下の「Mozc-UT の辞書を使う」) |
| `tests/` | `merge-ut-dictionary.py` のテスト。本物の Mozc も UT の辞書も要らない |
| `bin\` | ビルドしたヘルパー (Git には入れない)。`Build-Package.ps1` / `Install-Meltype.ps1` が `app\mozc\` に同梱する |

## ビルド

必要なもの: Visual Studio 2022 (C++ によるデスクトップ開発、Windows 11 SDK、C++ ATL)、Python 3.12 以降、[Bazelisk](https://github.com/bazelbuild/bazelisk)、Git。

```powershell
.\native\mozc\Build-MozcHelper.ps1 -Python python -Bazelisk C:\tools\bazelisk.exe
```

初回は Mozc の依存関係 (LLVM など約 1 GB) の取得とビルドで 30〜40 分ほどかかります。
GitHub Actions では `.github/workflows/build.yml` の `mozc` ジョブが同じスクリプトでビルドし、`package` ジョブが同梱します。

学習データ (Mozc が覚えた変換) は `%LOCALAPPDATA%\Meltype\mozc\` に保存されます。

## Mozc-UT の辞書を使う (任意)

[Mozc-UT](https://github.com/utuhiro78/merge-ut-dictionaries) の辞書 (`mozcdic-ut.txt`) を、変換の候補に足せます。
名前・地名・Wikipedia の語などが候補に出るようになります。**既定では統合しません**。何も指定しなければ、これまでどおり Mozc だけのヘルパーを作ります。

統合するときは、環境変数 `MELTYPE_UT_DICTIONARY` に辞書のファイルを指定して、いつものビルドを実行します。
Windows と Linux で同じ環境変数です。

```bash
# Linux
MELTYPE_UT_DICTIONARY=~/mozcdic-ut.txt native/mozc/build-mozc-helper.sh
```

```powershell
# Windows
$env:MELTYPE_UT_DICTIONARY = 'C:\path\to\mozcdic-ut.txt'
.\native\mozc\Build-MozcHelper.ps1 -Python python -Bazelisk C:\tools\bazelisk.exe
```

`build.yml` の `mozc` ジョブも同じスクリプトなので、CI で作るときも `env:` に `MELTYPE_UT_DICTIONARY` を置けば同じように統合できます。

- **辞書の入手方法・ライセンス・性能への影響・うまくいかないとき**: [../../docs/MOZC-UT.md](../../docs/MOZC-UT.md)
  (English: [../../docs/MOZC-UT.en.md](../../docs/MOZC-UT.en.md))
- 何を統合したかは `bin/MOZC-UT.json` に残ります (辞書と Mozc のハッシュ・品詞 ID の対応・除外した件数)。
- **標準の Mozc に戻すとき**は、`MELTYPE_UT_DICTIONARY` を指定せずにビルドし直します。Mozc-UT の語は Mozc の辞書とは別の `dictionary_ut.txt` に入れ、指定しないビルドではこのファイルを空にするので、語は残りません。Mozc の辞書 (`dictionary00.txt` など) は書き換えません。
- 統合するのは Mozc の辞書のテキストだけで、Meltype の変換エンジン (C#) は変えていません。ヘルパーを差し替えるだけで候補が変わります。
