# 設定の組合せと候補源の互換試験

<!-- meltype-flash-document-banner:start -->
<p align="center"><img src="../../../assets/branding/readme/documentation.svg" alt="Meltype-FLASH — 稲妻のように、言葉の壁を駆け抜ける日本語入力。" width="1000"></p>
<!-- meltype-flash-document-banner:end -->

元版 `45e14f86558aa6ea34f2c2ecd038ba95ca8c3c46` の `src/Meltype.Core` を変更せず、Windows CompositionService と同じ CompositionOptions を組み立てる。外部変換器は固定応答を返す。利用者の実データには触れない。

- 68設定の値域を定義し、全2項目間の値の組合せ12,595通りを42構成で網羅。生成時に網羅数を検査する。
- 翻訳、意味表示、予測、Tab変換、ライブ変換、SeparateKeyRoles、ContinueAfterConversion は128構成を全列挙。
- 4判定レベル、スコア区切り、ローマ字/JISかな、学習の32構成も検査。計648入力シナリオ。
- 追加候補源は外部エンジンの成功/空応答、一般語・外来語・ブランド・語句、ユーザー辞書、plist、文脈、絵文字、変換/翻訳/予測学習、独自書き間違い、複数文節、数式/時刻/全角数字、英語/日本語/固有名詞追加辞書を検査する。
- 比較は各打鍵のキー消費、確定編集、候補全文と順序、文節、選択位置、意味・注釈、予測、入力キー。非変換中の選択番号とhint文言は比較しない。
- アプリ別設定は別ホスト settings-apps で384通り。実行名の大文字小文字、拡張子、適用プロファイル、独自種類、有効/無効、ゲーム判定を含む。

```powershell
dotnet build tools/parity/settings-candidates/SettingsCandidates.csproj
python tools/parity/settings-candidates/matrix.py dist/candidates-matrix
python tools/parity/settings-candidates/check.py --csharp tools/parity/settings-candidates/bin/Debug/net10.0/Meltype.Core.Tests.dll --dll target/release/meltype_native.dll --cases dist/candidates-matrix/cases.json --output dist/candidates-check
python tools/parity/settings-candidates/sources.py dist/candidate-sources.json
python tools/parity/settings-candidates/check.py --csharp tools/parity/settings-candidates/bin/Debug/net10.0/Meltype.Core.Tests.dll --dll target/release/meltype_native.dll --cases dist/candidate-sources.json --output dist/sources-check
python tools/parity/settings-candidates/sources.py --dictionaries dist/candidate-dictionaries.json
python tools/parity/settings-candidates/check.py --csharp tools/parity/settings-candidates/bin/Debug/net10.0/Meltype.Core.Tests.dll --dll target/release/meltype_native.dll --cases dist/candidate-dictionaries.json --output dist/dictionaries-check
python tools/parity/settings-candidates/replay.py target/release/meltype_native.dll
cargo test -p meltype-core --test upstream_settings_apps
```

`check.py --reference-only` はC#のみを採取する。再照合の `--reference <採取ディレクトリー>` はケースとC# DLLのハッシュが一致する場合だけ使用できる。`freeze.py --output <固定先> <成功記録...>` はRust比較が全成功したC#出力のみを期待値として固定する。

全68項目の直積、OSスペルサービスの全応答、実System/Mozc辞書の全語彙、全UI操作や全OS実動の一致を証明するものではない。固定変換器でエンジン結果の扱いを検査し、実変換器/ワーカーは既存の別試験で確認する。設定UI・更新・OS起動設定などはCore入力トレースだけでは実動を確認できない。


`exhaustive.py dist/batch --offset 0 --limit 128` は定義済み値域の直積から128構成（2入力ずつ）を生成する。`coverage.json` のnext_offsetを次回のoffsetに渡し、domains_sha256/設定順/strideが一致する同一の値域で再開する。組合せ数は2,067,895,430,987,964,852,731,904で、offsetを進めたこと自体は過去の全範囲の照合成功を証明しない。追加256入力のC#由来期待値は `upstream-settings-exhaustive-batch.json` に固定し、通常の候補replay.pyへ --fixture で渡せる。任意の数値・文字列や入力履歴の完全網羅ではない。


`exhaustive.py --scenes all` は各構成を既存の20入力例で検査するためのケースを生成する。分割比較・証跡のハッシュ検査・失敗を保持した再開は [BATCHES.md](../BATCHES.md) を参照。追加比較で見つかった予測順・残留英字の幅候補・かな記号の回帰期待値は `upstream-settings-expanded-regressions.json` に元のC#応答から固定した。

## 2026-10-21: 1.3.0向け固定候補の更新

主fixture `upstream-settings-candidates.json` の677ケースは、未変更C# v1.3.0（6310c44b728d5ad88284853f9ef37ea224993b18）から再採取した。Coreと辞書の70ファイルをGitオブジェクトと照合し、WindowsチェックアウトのCRLFだけを正規化して確認した。全677ケースが実Rust DLLと一致した後に固定している。従来の45e14f8基準から応答が変わったのはnumber-widthだけで、1.3.0の漢数字・大字候補を含む。期待値をRustから生成したものではない。

再固定は `freeze.py <全比較成功の証跡フォルダー> --baseline 6310c44b728d5ad88284853f9ef37ea224993b18 --output rust/core/tests/data/upstream-settings-candidates.json`。採取ホストのビルドではCoreProjectをその基準の未変更Coreに指定する。追加のexhaustive-batchとexpanded-regressionsは、それぞれJSON内に記録された従来の基準を維持する。この更新で全設定直積の検証を再開したわけではない。

`both-input-formal-composition` は `InputStyle=Both` の設定確認に絞り、`konnichiwa` の共通入力で比較する。後から追加された `byi` ローマ字拡張を含む入力を旧C# 1.3.0の固定期待値と比較しないための更新で、ケースの期待値は未変更C# 1.3.0から再採取し、Rust DLLとの一致を検証している。
