# Meltype ファイル解説・構成・整理記録

この1ファイルは、Rust移植を含むこのフォークを読むための案内です。**2026-10-21時点で存在する全追跡ファイル**を末尾の一覧に載せ、役割、主要な関数/型/データ、残す理由を示します。`.git`、外部ツールチェーン、生成キャッシュ、利用者データは製品ソースの一覧に混ぜず、整理の節で扱います。

## 1. 最初に読む場所

|知りたいこと|入口|
|---|---|
|何が移行済みで何が未確認か|[rust/MIGRATION.md](rust/MIGRATION.md)、[rust/UPGRADE-1.3.0.md](rust/UPGRADE-1.3.0.md)|
|Windowsで導入・更新・解除・登録なし入力|[rust/WINDOWS-PACKAGE.md](rust/WINDOWS-PACKAGE.md)|
|キーを受けて候補・確定ができる流れ|[Session](rust/core/src/session.rs)、[設定](rust/core/src/settings.rs)|
|保存データ・辞書・バックアップ|[Store](rust/core/src/storage.rs)、[辞書](rust/core/src/dictionary.rs)|
|設定とトレイの画面|[設定アプリ](rust/settings-app/src/main.rs)|
|最新の速度・CPU・RAMと全画像|[2026-10-21の測定](tools/performance/windows/results/20261021-boxplots/README.md)|

## 2. 実行時のつながり

### Windows TSF

WindowsのTSF → 登録された`MeltypeTip.dll`（`rust/native/src/tsf`）→ 別プロセスの`meltype-tip-worker` → `meltype-core::Session` → 候補・文節・確定編集の応答 → SDKの編集範囲へ適用、という経路です。C ABIとJSONの形をOSホストと共有し、workerの失敗・フォーカス・範囲の不一致を検査します。x64/x86の**実登録・プロファイル・COM起動・元版復元は確認済み**ですが、外部アプリの実TSF入力の全受入は未完了です。

### 登録なしの入力と画面

`Start-Portable.ps1` → 設定アプリの`--portable-keyboard` → 低レベルフックのキュー → UI Automationによる編集先の検査 → Core → Unicodeの確定配送です。Ctrl+Alt+Jで切り替えます。パスワード、読取専用、判別できない入力欄では取り込みません。OS辞書、ライブ変換、確定後訂正、学習保存はこのホストに未接続です。設定画面・候補の表示が元版と全て一致したという意味ではありません。

### 辞書・設定・保存

`dictionaries/*.txt`は共同編集する原本です。`rust/core/build.rs`がFST検索データと文字列データへ変換して組み込みます。個人のユーザー辞書はSQLite、設定・プロファイル・学習等はStoreの互換処理を経由します。バックアップは作成/検査/復元を分け、未知のファイルを一括削除しません。保存先は`Store::default_directory`と`MELTYPE_RUST_DATA_DIR`で決まり、ポータブルでは配布内`data`を使います。実データをGitへ追加しないでください。

### そのほかのOS・元版

macOS、IBus/fcitx5、AndroidにはOS SDKとの薄い接続と比較用の旧実装が残ります。Windowsと共通Coreを優先しています。`src/`のC#、元版のC++/Swift等は、既存CI・パッケージ・独立した互換比較で参照されるため、拡張子だけを理由に削除していません。SDKの型幅、所有権、OS登録の確認に使うホストも必要です。

### ChromeOSの経路（2026-10-21追加）

ChromeOSの`chrome.input.ime` → MV3 Service Worker → WASMの`BrowserSession` → 共通Session/FST/判定/学習 → ChromeOSの未確定文字列・候補・確定APIです。OSのfilesystemとSQLiteはWASMでは使わず、共有した学習型を拡張機能のlocal storageへ保存します。デスクトップでは従来のfilesystem featureが標準ONです。

実WASM・模擬Chrome APIの12試験、Windows workspaceの239試験（手動2件ignore）、WASM/デスクトップのClippyが成功しました。**ChromeOS実機で導入・入力に成功したという意味ではありません。** OS辞書・全画面一致・確定後の削除/Undo・括弧内移動等は未対応/未確認です。[作成・導入・APIの根拠](chromeos/README.md)。

## 3. ビルド・検査・配布

```powershell
cargo test --workspace --locked
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo build -p meltype-settings -p meltype-cli -p meltype-tip-worker --release --locked
# TSF DLLの両ABIビルド
./native/tip/build.ps1 -RustCore
# 検査対象は展開した配布フォルダー
./rust/Manage-Windows.ps1 -Action Inspect -Package <配布フォルダー>
```

ZIPの組み立ては`rust/package-windows.py --help`、C#との測定は`tools/benchmark-windows.py --help`を参照します。`Manage-Windows.ps1`のInstall/Update/Uninstallと実登録試験は管理者権限が必要です。実登録試験は元版の両ABI DLLが戻せる状態で行い、finallyで復元します。バイナリのコピーだけでIME登録済みと判断しません。

最新Core比較は各入力30試行・各版10プロセス、21/21の比較対象応答が一致し、全入力でRustの中央値が短い結果です。TSF・描画・OS辞書を含むIME全体の速度ではありません。箱ひげ図は外れ値を除外せず、元のms値から四分位を計算し、公開JSONから同じPNGを再作成できます。

## 4. 今回の整理と保持方針

参照されない文書画像はありませんでした。元版のC#はSDKの既定Compile対象と比較ホスト/CIに参照があるため保持しました。旧ベンチマーク報告5ファイルは最新結果への集約に伴い削除し、必要な過去の記述は削除前のGitコミットへのリンクへ変更しました。旧Rustの悪化ケースが存在したという記録を、最新C#比較によって解決済みとは扱いません。

### Gitから整理した旧報告

|旧ファイル|理由・参照先|
|---|---|
|`tools/performance/windows/results/rust-f3ede6a-vs-198ecdd-summary.json`|旧版・部分測定。最新結果と重ねず、[削除前のGit履歴](https://github.com/appipinopi/Meltype/blob/fa5ee4e43060b69ec8d4e6f1c985b46a1aac7b40/tools/performance/windows/results/rust-f3ede6a-vs-198ecdd-summary.json)で参照する。|
|`tools/performance/windows/results/rust-f3ede6a-vs-198ecdd.md`|旧版・部分測定。最新結果と重ねず、[削除前のGit履歴](https://github.com/appipinopi/Meltype/blob/fa5ee4e43060b69ec8d4e6f1c985b46a1aac7b40/tools/performance/windows/results/rust-f3ede6a-vs-198ecdd.md)で参照する。|
|`tools/performance/windows/results/v130-initial-summary.json`|旧版・部分測定。最新結果と重ねず、[削除前のGit履歴](https://github.com/appipinopi/Meltype/blob/fa5ee4e43060b69ec8d4e6f1c985b46a1aac7b40/tools/performance/windows/results/v130-initial-summary.json)で参照する。|
|`tools/performance/windows/results/v130-initial.md`|旧版・部分測定。最新結果と重ねず、[削除前のGit履歴](https://github.com/appipinopi/Meltype/blob/fa5ee4e43060b69ec8d4e6f1c985b46a1aac7b40/tools/performance/windows/results/v130-initial.md)で参照する。|
|`tools/performance/windows/results/v130-prefix-component.json`|旧版・部分測定。最新結果と重ねず、[削除前のGit履歴](https://github.com/appipinopi/Meltype/blob/fa5ee4e43060b69ec8d4e6f1c985b46a1aac7b40/tools/performance/windows/results/v130-prefix-component.json)で参照する。|

### ローカル生成物の整理結果

検証後に削除できたのは **3,766ファイル、1,627,225,017バイト（1.515 GiB）** です。Git追跡中の旧報告5ファイルはこの件数に含めません。

|生成領域|削除ファイル数|削除バイト|削除理由|
|---|---:|---:|---|
|`dist/benchmark-chart-draft-20261021`|10|1,778,393|途中停止した試走または採用していないグラフの下書き。最新の集計・全画像を保持。|
|`dist/benchmark-chart-reproduction-20261021`|10|1,787,047|旧測定または再生成確認用の重複生成物。最新測定とGit履歴を保持。|
|`dist/rust-source-comparison-20261020`|10|40,545,283|旧測定または再生成確認用の重複生成物。最新測定とGit履歴を保持。|
|`dist/windows-benchmark-20261021-boxplots`|323|187,583,516|途中停止した試走または採用していないグラフの下書き。最新の集計・全画像を保持。|
|`dist/windows-preview-20261020`|14|62,709,787|最新配布に置き換えられた、マニフェストで検証済みの旧配布ファイル。|
|`dist/windows-preview-20261020-final`|14|62,693,403|最新配布に置き換えられた、マニフェストで検証済みの旧配布ファイル。|
|`dist/windows-preview-20261021-input-update`|18|63,945,013|最新配布に置き換えられた、マニフェストで検証済みの旧配布ファイル。|
|`dist/windows-preview-20261021-install-check`|17|63,915,016|最新配布に置き換えられた、マニフェストで検証済みの旧配布ファイル。|
|`dist/windows-preview-20261021-portable-final`|18|63,944,285|最新配布に置き換えられた、マニフェストで検証済みの旧配布ファイル。|
|`dist/windows-preview-20261021-windows`|18|63,944,005|最新配布に置き換えられた、マニフェストで検証済みの旧配布ファイル。|
|`dist/windows-resource-benchmark-v130-initial`|328|216,423,428|旧測定または再生成確認用の重複生成物。最新測定とGit履歴を保持。|
|`dist/旧Windows配布ZIP`|6|157,629,254|内容を旧配布マニフェストと照合した、最新配布に置き換えられたZIP。|
|`dist/旧ログ・デバッグ出力`|1,374|30,096,477|旧試行のログ、シンボル、再採取可能な画面バッファ。原版UIの参照バッファは保持。|
|`src/Meltype.Core/bin`|6|8,586,592|.NETプロジェクトの再生成可能なbin/obj。|
|`src/Meltype.Core/obj`|31|9,350,298|.NETプロジェクトの再生成可能なbin/obj。|
|`target`|1,012|404,315,757|再生成可能なワークスペース内のCargo出力。現在のビルド先はワークスペース外。|
|`tools/parity/clauses/bin`|7|4,737,736|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/clauses/obj`|22|292,865|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/core-data/bin`|7|4,531,665|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/core-data/obj`|22|289,789|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/direct/bin`|7|4,661,481|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/direct/obj`|22|283,005|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/hook/bin`|7|4,672,251|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/hook/obj`|22|294,432|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/misspellings/bin`|7|4,516,407|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/misspellings/obj`|22|270,518|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/mozc-lifecycle/bin`|7|4,581,718|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/mozc-lifecycle/obj`|22|357,413|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/reconversion/bin`|7|4,739,955|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/reconversion/obj`|22|296,382|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/settings-apps/bin`|7|4,514,375|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/settings-apps/obj`|22|268,515|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/settings-candidates/bin`|14|9,269,926|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/settings-candidates/obj`|39|494,481|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/v130/obj`|21|297,331|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/windows-converter/bin`|7|4,544,520|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/parity/windows-converter/obj`|22|314,835|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/performance/windows/bin`|195|110,116,998|.NETプロジェクトの再生成可能なbin/obj。|
|`tools/performance/windows/obj`|27|23,630,865|.NETプロジェクトの再生成可能なbin/obj。|

変更・ロック・安全検査等により保持した対象: 0件。

### 保持したもの

- 最新のWindows配布`dist/windows-preview-20261021-ime-check`と同名ZIP、最新測定の生データ`dist/windows-benchmark-20261021-final`、実IME復元・採取の現在の証跡`dist/v130-reference`。
- 未変更C# 1.3.0から採取した677候補シナリオの生応答とプロファイル、実OS辞書の採取結果、個人データの可能性がある学習・バックアップ・プロファイル。
- 全ソース、回帰fixture、現行CI、ライセンスと第三者表示、ユーザーが追加したmacOS登録ツールの説明。
- ワークスペース外のツールチェーンと現在使用しているCargoビルド先。`.git`、OneDrive全体、インストール済みの元版は整理対象にしない。

旧配布はSHA256SUMSと照合できる配布ファイルだけを対象にし、一覧にない`data`等を削除しません。ファイルは作業フォルダー内であること、リンク/リパースポイントでないこと、計画時からハッシュが変わっていないことを検査してから個別に削除します。空ではないフォルダーを再帰的に削除する方式ではありません。

## 5. 検証の現在地と残るもの

677候補シナリオを未変更C# v1.3.0から再採取し、Rust DLLの全応答と照合しました。Core/辞書70ファイルをGitオブジェクトと照合し、WindowsのCRLFだけを正規化しています。旧fixtureとの差はnumber-widthの1ケースで、1.3.0の漢数字・大字候補です。追加256ケースと3回帰ケースのネイティブ再生も通過しました。Windows CIで発生したDドライブのos error 433はランナーのデバイスエラーとして分け、ローカル成功を全OSのCI成功とは書きません。

Windowsで cargo test --workspace --locked は239件成功、手動試験2件はignoreです。ネイティブABIの旧入力試験は、旧仕様175列とBoth→Romaji移行7列に分けて成功しました。旧APIそのものの182列はCoreの回帰試験として保持します。

全設定の無制限な直積・全OS/全学習状態の総当たりは完了条件へ戻しません。全画面の一致、外部アプリへの実TSF入力、実公開ZIPによる自動更新、旧Rustとの原因別性能分析には未完了項目があります。mainへ取り込むことと全機能の受入完了は別です。

## 6. 全追跡ファイルの解説

一覧の「主要要素」はそのファイルに存在する宣言・見出し・データの要点です。私用データや生成キャッシュはこの一覧へ含めません。C#とSDKホストは比較/互換経路のため保持しており、Rust以外のファイルがあるだけで不要とは判断しません。

対象ファイル数: **1,115**。ファイル名はクリックすると実体を開きます。

### ルート（15件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[.gitignore](.gitignore)|生成物、個人設定、ローカルの機械情報をGit追跡から除外する規則。|.gitignore|実装・ビルドに必要|
|[Build-Package.ps1](Build-Package.ps1)|元版C#のWindowsパッケージを生成する。Rust配布はrust/package-windows.pyを使う。|param、if、else、foreach|元版・比較に必要|
|[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)|「CODE_OF_CONDUCT」の手順・仕様・確認範囲を説明する。|CODE_OF_CONDUCT.md|説明・参照に必要|
|[CONTRIBUTING.md](CONTRIBUTING.md)|「CONTRIBUTING」の手順・仕様・確認範囲を説明する。|CONTRIBUTING.md|説明・参照に必要|
|[Cargo.lock](Cargo.lock)|Rust依存ライブラリの解決版とチェックサムを固定し、再現可能なビルドに使う。|Cargo.lock|実装・ビルドに必要|
|[Cargo.toml](Cargo.toml)|Rustワークスペースのcrate一覧、共通の版・ライセンス、Release最適化を定義する。|"0.1.0"|実装・ビルドに必要|
|[FILE_GUIDE.md](FILE_GUIDE.md)|本ファイル。構成、処理の流れ、全追跡ファイルの役割、削除記録を一か所にまとめる。|FILE_GUIDE.md|説明・参照に必要|
|[Install-Meltype.ps1](Install-Meltype.ps1)|元版のWindows導入入口。Rust版の版別管理とは別の互換経路。|param、if、foreach|元版・比較に必要|
|[LICENSE](LICENSE)|元版と派生物に適用するGPLライセンス本文。配布物にも同梱する。|LICENSE|実装・ビルドに必要|
|[Meltype.sln](Meltype.sln)|C#元版のプロジェクトをまとめるソリューション。既存CIと比較用ビルドで使用。|Meltype.sln|元版・比較に必要|
|[README.md](README.md)|フォークの入口と元版の機能・導入案内。Rustの現在地と本解説へ案内する。|README.md|説明・参照に必要|
|[SECURITY.md](SECURITY.md)|「SECURITY」の手順・仕様・確認範囲を説明する。|SECURITY.md|説明・参照に必要|
|[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)|辞書、コード、外部ライブラリ等の第三者ライセンスと帰属表示。|サードパーティー通知、配布用パッケージに同梱しているもの、Mac 版 (mac/) がビルド時に取り込むもの|説明・参照に必要|
|[Uninstall-Meltype.ps1](Uninstall-Meltype.ps1)|元版のWindows削除入口。既存配布の保守用に保持。|param、foreach、if|元版・比較に必要|
|[nuget.config](nuget.config)|C#比較ホストと元版のNuGetパッケージ取得元を設定する。|nuget.config|実装・ビルドに必要|

### GitHub運用とCI（26件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[.github/FUNDING.yml](.github/FUNDING.yml)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|FUNDING.yml|CI・貢献運用に必要|
|[.github/ISSUE_TEMPLATE/1-bug.yml](.github/ISSUE_TEMPLATE/1-bug.yml)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|1-bug.yml|CI・貢献運用に必要|
|[.github/ISSUE_TEMPLATE/2-misdetection.yml](.github/ISSUE_TEMPLATE/2-misdetection.yml)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|2-misdetection.yml|CI・貢献運用に必要|
|[.github/ISSUE_TEMPLATE/3-dictionary.yml](.github/ISSUE_TEMPLATE/3-dictionary.yml)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|3-dictionary.yml|CI・貢献運用に必要|
|[.github/ISSUE_TEMPLATE/4-idea.yml](.github/ISSUE_TEMPLATE/4-idea.yml)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|4-idea.yml|CI・貢献運用に必要|
|[.github/ISSUE_TEMPLATE/5-feature-request.yml](.github/ISSUE_TEMPLATE/5-feature-request.yml)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|5-feature-request.yml|CI・貢献運用に必要|
|[.github/ISSUE_TEMPLATE/config.yml](.github/ISSUE_TEMPLATE/config.yml)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|config.yml|CI・貢献運用に必要|
|[.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|変更の内容、確かめること、確認したこと|説明・参照に必要|
|[.github/bot/cla.mjs](.github/bot/cla.mjs)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|cla.mjs|CI・貢献運用に必要|
|[.github/bot/meltype-bot.mjs](.github/bot/meltype-bot.mjs)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|meltype-bot.mjs|CI・貢献運用に必要|
|[.github/bot/notices.mjs](.github/bot/notices.mjs)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|notices.mjs|CI・貢献運用に必要|
|[.github/bot/pr-checks.mjs](.github/bot/pr-checks.mjs)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|pr-checks.mjs|CI・貢献運用に必要|
|[.github/bot/sponsors.mjs](.github/bot/sponsors.mjs)|GitHubでの貢献受付・報告・自動チェックに使う設定またはテンプレート。|sponsors.mjs|CI・貢献運用に必要|
|[.github/workflows/bot.yml](.github/workflows/bot.yml)|GitHub Actionsで「bot」を実行するCI・配布の定義。|Mozc helper from latest release、Run、Test、Package|CI・貢献運用に必要|
|[.github/workflows/build.yml](.github/workflows/build.yml)|GitHub Actionsで「build」を実行するCI・配布の定義。|Build、Build Meltype IME、Test package scan failure handling、Test|CI・貢献運用に必要|
|[.github/workflows/cla.yml](.github/workflows/cla.yml)|GitHub Actionsで「cla」を実行するCI・配布の定義。|cla.yml|CI・貢献運用に必要|
|[.github/workflows/dotnet-update.yml](.github/workflows/dotnet-update.yml)|GitHub Actionsで「dotnet-update」を実行するCI・配布の定義。|Compare bundled .NET with the latest patch|CI・貢献運用に必要|
|[.github/workflows/fcitx-rust.yml](.github/workflows/fcitx-rust.yml)|GitHub Actionsで「Rust fcitx ABI」を実行するCI・配布の定義。|Install fcitx5 development SDK、Build Rust and legacy SDK adapters、Compare production adapters with reference|CI・貢献運用に必要|
|[.github/workflows/linux.yml](.github/workflows/linux.yml)|GitHub Actionsで「linux」を実行するCI・配布の定義。|Check Python、Packages、Bazelisk、Build Mozc helper|CI・貢献運用に必要|
|[.github/workflows/mac.yml](.github/workflows/mac.yml)|GitHub Actionsで「mac」を実行するCI・配布の定義。|Tools、Signing certificate、Build、NativeAOT and Swift boundary tests|CI・貢献運用に必要|
|[.github/workflows/macos-rust.yml](.github/workflows/macos-rust.yml)|GitHub Actionsで「Rust macOS SDK」を実行するCI・配布の定義。|Compare actual Swift bridge and Rust ABI w、Controlled IMK and Unicode boundary integr、Pinned azooKey engine through actu|CI・貢献運用に必要|
|[.github/workflows/notices.yml](.github/workflows/notices.yml)|GitHub Actionsで「notices」を実行するCI・配布の定義。|notices.yml|CI・貢献運用に必要|
|[.github/workflows/pr-checks.yml](.github/workflows/pr-checks.yml)|GitHub Actionsで「pr-checks」を実行するCI・配布の定義。|品質テスト (Pull Request)、品質テスト (main)、比べる|CI・貢献運用に必要|
|[.github/workflows/rust.yml](.github/workflows/rust.yml)|GitHub Actionsで「Rust preview」を実行するCI・配布の定義。|Linux IBus development and isolated-test r、Build fcitx5 adapter、Windows converter batch ABI bounds and cle、fcitx5 Rust J|CI・貢献運用に必要|
|[.github/workflows/sponsors.yml](.github/workflows/sponsors.yml)|GitHub Actionsで「sponsors」を実行するCI・配布の定義。|Update README from GitHub Sponsors、Open or update the pull request|CI・貢献運用に必要|
|[.github/workflows/winget.yml](.github/workflows/winget.yml)|GitHub Actionsで「winget」を実行するCI・配布の定義。|Check token|CI・貢献運用に必要|

### Rust実装・型/ABI・回帰試験（419件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[rust/ACCEPTANCE.md](rust/ACCEPTANCE.md)|「元版互換の完了条件」の手順・仕様・確認範囲を説明する。|元版互換の完了条件、やめること（2026-10-20決定）、2026-10-20 FST・SQLiteと追加検証|説明・参照に必要|
|[rust/MIGRATION.md](rust/MIGRATION.md)|移植の現在地、比較済み範囲、既知の不足、完了条件から外した無制限検証を記録する。|全機能移植の作業表、2026-10-21: 実IME導入確認・自動更新・ポータブル入力、現状（2026-10-20）|説明・参照に必要|
|[rust/Manage-Windows.ps1](rust/Manage-Windows.ps1)|配布ハッシュ検査、版別導入・更新、両ABIの登録解除、検証済みファイルのCleanupを行う。|param、Get-Server、Get-InstallRoot、Assert-Administrator|実装・ビルドに必要|
|[rust/README.md](rust/README.md)|Rust部分のビルド入口と各移植の経緯。現在の判定はMIGRATION/UPGRADEも参照する。|Meltype Rust 移植プレビュー、ビルド・確認、きょうはgoogleでけんさく|説明・参照に必要|
|[rust/STORAGE.md](rust/STORAGE.md)|「辞書の保存と共同編集」の手順・仕様・確認範囲を説明する。|辞書の保存と共同編集、SQLiteへの移行、確認範囲|説明・参照に必要|
|[rust/Start-Portable.ps1](rust/Start-Portable.ps1)|配布内dataを保存先にして登録なし入力を起動する。Ctrl+Alt+JでON/OFF、SettingsOnlyは画面のみ。|param、if、try|実装・ビルドに必要|
|[rust/Test-InstalledIme.ps1](rust/Test-InstalledIme.ps1)|一時的に実IMEを導入し、両ABIの登録先・TSFプロファイル・COM起動を検査して元版へ復元する。UACが必要。|param、if、Server、foreach|実装・ビルドに必要|
|[rust/Test-Manage-Windows.ps1](rust/Test-Manage-Windows.ps1)|OSウィンドウと表示の回帰・境界・失敗時の動作を検査する。|foreach、Expect-Rejection|実装・ビルドに必要|
|[rust/Test-StartupCli.ps1](rust/Test-StartupCli.ps1)|自動起動と保守コマンドの回帰・境界・失敗時の動作を検査する。|param、Run-Probe、try|実装・ビルドに必要|
|[rust/Test-TrayStop.ps1](rust/Test-TrayStop.ps1)|トレイ表示と操作の回帰・境界・失敗時の動作を検査する。|param、try|実装・ビルドに必要|
|[rust/Test-UpdateWindows.ps1](rust/Test-UpdateWindows.ps1)|OSウィンドウと表示の回帰・境界・失敗時の動作を検査する。|if、Get-Releases、Download-Asset、foreach|実装・ビルドに必要|
|[rust/Test-WindowsLifecycle.ps1](rust/Test-WindowsLifecycle.ps1)|起動・終了・保存の寿命の回帰・境界・失敗時の動作を検査する。|Assert-That、Package、Get-InstallRoot、Assert-Administrator|実装・ビルドに必要|
|[rust/UPGRADE-1.3.0.md](rust/UPGRADE-1.3.0.md)|1.3.0までの機能別チェック表。全機能完了と限定条件での確認を区別する。|1.3.0への対応作業、機能、性能と整理|説明・参照に必要|
|[rust/UPSTREAM-1.3.1-pre1-GAPS.md](rust/UPSTREAM-1.3.1-pre1-GAPS.md)|「Meltype 1.2.1〜1.3.1-pre.1のRust版未対応・未確認一覧」の手順・仕様・確認範囲を説明する。|Meltype 1.2.1〜1.3.1-pre.1のRust版未対応・未確認一覧、対象の5リリースと版ごとの不足、未対応の追加設定（31項目）|説明・参照に必要|
|[rust/Update-Windows.ps1](rust/Update-Windows.ps1)|公開Rust Windows ZIPを取得し、取得元・サイズ・SHA-256・展開先を検査して管理更新へ渡す。|param、Installed-Root、if、Get-Releases|実装・ビルドに必要|
|[rust/WINDOWS-PACKAGE.md](rust/WINDOWS-PACKAGE.md)|Windows配布の構成、導入・更新・解除・ポータブルと各操作の制限を説明する。|Rust Windows版・配布前確認用、内容、制限|説明・参照に必要|
|[rust/build-desktop.sh](rust/build-desktop.sh)|`build-desktop` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|build-desktop.sh|実装・ビルドに必要|
|[rust/build-linux.sh](rust/build-linux.sh)|`build-linux` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|build-linux.sh|実装・ビルドに必要|
|[rust/build-windows.ps1](rust/build-windows.ps1)|OSウィンドウと表示をRust側のOSホスト・配布経路へ接続する。|param、try|実装・ビルドに必要|
|[rust/cli/Cargo.toml](rust/cli/Cargo.toml)|cli crateの依存、OS別機能、実行形式を定義する。|"meltype-cli"、"meltype-rs"|実装・ビルドに必要|
|[rust/cli/src/main.rs](rust/cli/src/main.rs)|`main` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|Request、main|実装・ビルドに必要|
|[rust/core/Cargo.toml](rust/core/Cargo.toml)|core crateの依存、OS別機能、実行形式を定義する。|"meltype-core"|実装・ビルドに必要|
|[rust/core/build.rs](rust/core/build.rs)|共同編集用TXT辞書をビルド時にFSTとUTF-8データへ変換してCoreへ組み込む。|words、main|実装・ビルドに必要|
|[rust/core/data/function-words.txt](rust/core/data/function-words.txt)|`function-words` にまとめた処理をOS共通Core内で処理する。|133有効行|実装・ビルドに必要|
|[rust/core/data/settings-defaults.json](rust/core/data/settings-defaults.json)|設定と候補の実効動作の設定・固定ケース・採取証跡を構造化して保存する。|Enabled、JapaneseKeyboardOnly、AllowInjectedInput、Mode|実装・ビルドに必要|
|[rust/core/data/upstream-detection.json](rust/core/data/upstream-detection.json)|日英・入力対象の判定の設定・固定ケース・採取証跡を構造化して保存する。|128項目|実装・ビルドに必要|
|[rust/core/data/upstream-typing.json](rust/core/data/upstream-typing.json)|`upstream-typing` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|216項目|実装・ビルドに必要|
|[rust/core/examples/benchmark.rs](rust/core/examples/benchmark.rs)|`benchmark` にまとめた処理をOS共通Core内で処理する。|Case、main|実装・ビルドに必要|
|[rust/core/examples/core_data.rs](rust/core/examples/core_data.rs)|`core_data` にまとめた処理をOS共通Core内で処理する。|main|実装・ビルドに必要|
|[rust/core/examples/direct_trace.rs](rust/core/examples/direct_trace.rs)|`direct_trace` にまとめた処理をOS共通Core内で処理する。|Case、Step、main|実装・ビルドに必要|
|[rust/core/examples/hook_trace.rs](rust/core/examples/hook_trace.rs)|キーボードフックとキー所有をOS共通Core内で処理する。|Case、main|実装・ビルドに必要|
|[rust/core/examples/input_latency.rs](rust/core/examples/input_latency.rs)|入力イベント・入力先をOS共通Core内で処理する。|main|実装・ビルドに必要|
|[rust/core/examples/mozc_lifecycle.rs](rust/core/examples/mozc_lifecycle.rs)|起動・終了・保存の寿命をOS共通Core内で処理する。|main|実装・ビルドに必要|
|[rust/core/examples/profile_composition.rs](rust/core/examples/profile_composition.rs)|設定プロファイルの適用をOS共通Core内で処理する。|Case、main|実装・ビルドに必要|
|[rust/core/examples/quality.rs](rust/core/examples/quality.rs)|入力判定の品質評価をOS共通Core内で処理する。|apply、run、main|実装・ビルドに必要|
|[rust/core/src/calculator.rs](rust/core/src/calculator.rs)|式の解析と計算候補をOS共通Core内で処理する。|evaluate、Parser、sum、product|実装・ビルドに必要|
|[rust/core/src/candidate_sources.rs](rust/core/src/candidate_sources.rs)|候補源の統合・候補の優先順位をOS共通Core内で処理する。|Sources、add、lookup、bundled|実装・ビルドに必要|
|[rust/core/src/commit_correction.rs](rust/core/src/commit_correction.rs)|確定後の安全な訂正と取り消しをOS共通Core内で処理する。|Record、replacement|実装・ビルドに必要|
|[rust/core/src/composition_units.rs](rust/core/src/composition_units.rs)|字種変換用の入力単位をOS共通Core内で処理する。|english_offset、Units、from_tokens、replace_reading|実装・ビルドに必要|
|[rust/core/src/context.rs](rust/core/src/context.rs)|周辺文・文脈の判定をOS共通Core内で処理する。|LineKind、classify、prose_title、terminal|実装・ビルドに必要|
|[rust/core/src/control.rs](rust/core/src/control.rs)|管理コマンドの実行をOS共通Core内で処理する。|field、snippet_name_valid、snippet_text、execute|実装・ビルドに必要|
|[rust/core/src/detection.rs](rust/core/src/detection.rs)|日英・入力対象の判定をOS共通Core内で処理する。|Verdict、Contribution、Result、Lexicon|実装・ビルドに必要|
|[rust/core/src/dictionary.rs](rust/core/src/dictionary.rs)|システムFST辞書と追加辞書を読み、単語・候補の検索を行う。|Dictionary、PrefixIndex、default、new|実装・ビルドに必要|
|[rust/core/src/dictionary/composition.rs](rust/core/src/dictionary/composition.rs)|未確定入力と変換状態をOS共通Core内で処理する。|Evidence、composition_kana、side_character、english_sentence|実装・ビルドに必要|
|[rust/core/src/dictionary/scored.rs](rust/core/src/dictionary/scored.rs)|スコア付きの区切り判定をOS共通Core内で処理する。|Path、Weights、get、offer|実装・ビルドに必要|
|[rust/core/src/github_titles.rs](rust/core/src/github_titles.rs)|GitHub由来の候補・情報をOS共通Core内で処理する。|normalize、valid、ResultState、Entry|実装・ビルドに必要|
|[rust/core/src/input_session.rs](rust/core/src/input_session.rs)|入力セッションの状態遷移をOS共通Core内で処理する。|KeyEvent、State、Outcome、Summary|実装・ビルドに必要|
|[rust/core/src/kana.rs](rust/core/src/kana.rs)|かな・JISかなの変換をOS共通Core内で処理する。|key、combine、halfwidth、fullwidth|実装・ビルドに必要|
|[rust/core/src/language_memory.rs](rust/core/src/language_memory.rs)|日英の判定結果と文脈学習をOS共通Core内で処理する。|get、active、relevant、Scope|実装・ビルドに必要|
|[rust/core/src/lib.rs](rust/core/src/lib.rs)|共通Coreのモジュールと公開型を公開するcrate入口。OSの登録処理は持たない。|lib.rs|実装・ビルドに必要|
|[rust/core/src/linguistics.rs](rust/core/src/linguistics.rs)|`linguistics` にまとめた処理をOS共通Core内で処理する。|Linguistics、Misspelling、kanji、kana|実装・ビルドに必要|
|[rust/core/src/linguistics/normalization_marks.rs](rust/core/src/linguistics/normalization_marks.rs)|`normalization_marks` にまとめた処理をOS共通Core内で処理する。|is_non_spacing_mark|実装・ビルドに必要|
|[rust/core/src/math_candidates.rs](rust/core/src/math_candidates.rs)|候補一覧と選択をOS共通Core内で処理する。|digits、shape、candidate、augment|実装・ビルドに必要|
|[rust/core/src/mozc.rs](rust/core/src/mozc.rs)|`mozc` にまとめた処理をOS共通Core内で処理する。|Mozc、clean、LearningClause、Segment|実装・ビルドに必要|
|[rust/core/src/number_candidates.rs](rust/core/src/number_candidates.rs)|数字・漢数字・単位の候補をOS共通Core内で処理する。|half_digits、to_era、candidates、render|実装・ビルドに必要|
|[rust/core/src/phrase_spacing.rs](rust/core/src/phrase_spacing.rs)|`phrase_spacing` にまとめた処理をOS共通Core内で処理する。|boundaries、token、is_phrase、format|実装・ビルドに必要|
|[rust/core/src/prefix_learning.rs](rust/core/src/prefix_learning.rs)|学習の保存・反映をOS共通Core内で処理する。|Stats、Model、default、key|実装・ビルドに必要|
|[rust/core/src/protection.rs](rust/core/src/protection.rs)|`protection` にまとめた処理をOS共通Core内で処理する。|Span、path、element、mention|実装・ビルドに必要|
|[rust/core/src/romaji-strict.tsv](rust/core/src/romaji-strict.tsv)|ローマ字表とかな変換をOS共通Core内で処理する。|romaji-strict.tsv|実装・ビルドに必要|
|[rust/core/src/romaji.rs](rust/core/src/romaji.rs)|ローマ字表とかな変換をOS共通Core内で処理する。|table、prefixes、Token、Analysis|実装・ビルドに必要|
|[rust/core/src/romaji.tsv](rust/core/src/romaji.tsv)|ローマ字表とかな変換をOS共通Core内で処理する。|romaji.tsv|実装・ビルドに必要|
|[rust/core/src/romaji/custom.rs](rust/core/src/romaji/custom.rs)|`custom` にまとめた処理をOS共通Core内で処理する。|CustomTable、entries、parse、load|実装・ビルドに必要|
|[rust/core/src/romaji/probes.rs](rust/core/src/romaji/probes.rs)|`probes` にまとめた処理をOS共通Core内で処理する。|Probe、Entries、Rendered、Cache|実装・ビルドに必要|
|[rust/core/src/session.rs](rust/core/src/session.rs)|キー入力を受け、未確定文字・候補・文節・確定編集の状態を進める中心処理。|is_zero、TextEdit、View、SessionResult|実装・ビルドに必要|
|[rust/core/src/session/clause_metadata.rs](rust/core/src/session/clause_metadata.rs)|文節の分割・選択・伸縮をOS共通Core内で処理する。|single_conversion_clause、clause_history_change、clause_history_changes、clause_language_change|実装・ビルドに必要|
|[rust/core/src/session/direct.rs](rust/core/src/session/direct.rs)|`direct` にまとめた処理をOS共通Core内で処理する。|set_direct、forget_direct_input、direct_timeout、detect_direct|実装・ビルドに必要|
|[rust/core/src/session/engine_learning.rs](rust/core/src/session/engine_learning.rs)|学習の保存・反映をOS共通Core内で処理する。|Batch、collect_engine_learning、take_engine_learning、record_engine_learning|実装・ビルドに必要|
|[rust/core/src/session/misspelling.rs](rust/core/src/session/misspelling.rs)|スペル検査・修正候補をOS共通Core内で処理する。|Edit、composition_misspelling、misspelling_suggestion、repair_misspelling|実装・ビルドに必要|
|[rust/core/src/session/reconversion.rs](rust/core/src/session/reconversion.rs)|選択文字列の再変換をOS共通Core内で処理する。|ReconversionPart、romaji_reconversion_parts、begin_romaji_reconversion|実装・ビルドに必要|
|[rust/core/src/session/v130.rs](rust/core/src/session/v130.rs)|`v130` にまとめた処理をOS共通Core内で処理する。|State、github_title_request、set_github_title、wants_clipboard_candidate|実装・ビルドに必要|
|[rust/core/src/settings.rs](rust/core/src/settings.rs)|既定値、型、移行、プロファイル、アプリ別の実効設定を定義する。|Settings、same_type、migrate_input_style、default|実装・ビルドに必要|
|[rust/core/src/shortcuts.rs](rust/core/src/shortcuts.rs)|`shortcuts` にまとめた処理をOS共通Core内で処理する。|Shortcut、key_name、parse、parse_list|実装・ビルドに必要|
|[rust/core/src/sigil.rs](rust/core/src/sigil.rs)|`sigil` にまとめた処理をOS共通Core内で処理する。|Sigil、default、name、context|実装・ビルドに必要|
|[rust/core/src/spelling.rs](rust/core/src/spelling.rs)|スペル検査・修正候補をOS共通Core内で処理する。|Evidence、Scope、disabled、new|実装・ビルドに必要|
|[rust/core/src/storage.rs](rust/core/src/storage.rs)|OSファイルロック・原子的保存・SQLite辞書・元版データの取込み・バックアップを実装し、state.rsの学習型を再公開する。filesystem featureの標準実装。|Store, atomic_write, settings, load, update, record, backup, restore|デスクトップの保存とデータ互換に必要|
|[rust/core/src/storage/sqlite_dictionary.rs](rust/core/src/storage/sqlite_dictionary.rs)|辞書の検索・更新をOS共通Core内で処理する。|error、version、read、Write|実装・ビルドに必要|
|[rust/core/src/storage/upstream.rs](rust/core/src/storage/upstream.rs)|`upstream` にまとめた処理をOS共通Core内で処理する。|timestamp、upstream_files、upstream_json|実装・ビルドに必要|
|[rust/core/src/system_data.rs](rust/core/src/system_data.rs)|`system_data` にまとめた処理をOS共通Core内で処理する。|archive、value、text、archive_preserves_every_txt_byte_and_line_|実装・ビルドに必要|
|[rust/core/src/time_candidates.rs](rust/core/src/time_candidates.rs)|時刻とタイミングをOS共通Core内で処理する。|number、shape、typed、candidates|実装・ビルドに必要|
|[rust/core/src/typo.rs](rust/core/src/typo.rs)|`typo` にまとめた処理をOS共通Core内で処理する。|Fix、Corrector、bundled、new|実装・ビルドに必要|
|[rust/core/src/user_dictionary.rs](rust/core/src/user_dictionary.rs)|辞書の検索・更新をOS共通Core内で処理する。|Import、hiragana、valid、parse|実装・ビルドに必要|
|[rust/core/src/windows_http.rs](rust/core/src/windows_http.rs)|OSウィンドウと表示をOS共通Core内で処理する。|WinHttpOpen、WinHttpConnect、WinHttpOpenRequest、WinHttpSetTimeouts|実装・ビルドに必要|
|[rust/core/tests/candidates.rs](rust/core/tests/candidates.rs)|候補一覧と選択の回帰・境界・失敗時の動作を検査する。|upstream_date_and_clock_formats_validate_c、date_segments_join_and_dynamic_choices_do_、emoji_and_ascii_supplementary_word|回帰・比較に必要|
|[rust/core/tests/clause_refresh.rs](rust/core/tests/clause_refresh.rs)|文節の分割・選択・伸縮の回帰・境界・失敗時の動作を検査する。|session、converter_refresh_uses_new_readings_and_re、absent_converter_reply_keeps_the_local_rea|回帰・比較に必要|
|[rust/core/tests/commit_correction.rs](rust/core/tests/commit_correction.rs)|確定後の安全な訂正と取り消しの回帰・境界・失敗時の動作を検査する。|apply、type_text、following_english_repairs_ambiguous_commit、correction_requires_capability_setting_mat|回帰・比較に必要|
|[rust/core/tests/composition_levels.rs](rust/core/tests/composition_levels.rs)|未確定入力と変換状態の回帰・境界・失敗時の動作を検査する。|session、upstream_feedback_level_and_manual_example、conservative_requires_two_sides_or_an_engl、manual_tab_is_an_explicit_|回帰・比較に必要|
|[rust/core/tests/custom_romaji.rs](rust/core/tests/custom_romaji.rs)|ローマ字表とかな変換の回帰・境界・失敗時の動作を検査する。|session、type_text、table_parser_matches_original_validation_a、custom_spellings_override_builtin_sokuon_n|回帰・比較に必要|
|[rust/core/tests/data/upstream-backspace.json](rust/core/tests/data/upstream-backspace.json)|`upstream-backspace` にまとめた処理の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:20、baseline、cases|回帰・比較に必要|
|[rust/core/tests/data/upstream-balanced-keys.json](rust/core/tests/data/upstream-balanced-keys.json)|キーと修飾キーの扱いの固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:75、baseline、builtin_spelling|回帰・比較に必要|
|[rust/core/tests/data/upstream-case-keys.json](rust/core/tests/data/upstream-case-keys.json)|キーと修飾キーの扱いの固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:96、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-clause-history.json](rust/core/tests/data/upstream-clause-history.json)|文節の分割・選択・伸縮の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:25、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-clause-language.json](rust/core/tests/data/upstream-clause-language.json)|文節の分割・選択・伸縮の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:16、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-clause-raw.json](rust/core/tests/data/upstream-clause-raw.json)|文節の分割・選択・伸縮の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:36、baseline、cases|回帰・比較に必要|
|[rust/core/tests/data/upstream-clause-resize.json](rust/core/tests/data/upstream-clause-resize.json)|文節の分割・選択・伸縮の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:20、baseline、host_sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-conversion-keys.json](rust/core/tests/data/upstream-conversion-keys.json)|キーと修飾キーの扱いの固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:84、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-core-data.json](rust/core/tests/data/upstream-core-data.json)|`upstream-core-data` にまとめた処理の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:371、baseline、source_sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-custom-romaji.json](rust/core/tests/data/upstream-custom-romaji.json)|ローマ字表とかな変換の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:12、baseline、builtin_spelling|回帰・比較に必要|
|[rust/core/tests/data/upstream-direct-time.json](rust/core/tests/data/upstream-direct-time.json)|時刻とタイミングの固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:32、baseline、reference_assembly_sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-direct-typos.json](rust/core/tests/data/upstream-direct-typos.json)|`upstream-direct-typos` にまとめた処理の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:20、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-engine-keys.json](rust/core/tests/data/upstream-engine-keys.json)|キーと修飾キーの扱いの固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:50、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-engine-learning.json](rust/core/tests/data/upstream-engine-learning.json)|学習の保存・反映の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:22、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-hook-session.json](rust/core/tests/data/upstream-hook-session.json)|入力セッションの状態遷移の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:182、baseline、scope|回帰・比較に必要|
|[rust/core/tests/data/upstream-long-keys.json](rust/core/tests/data/upstream-long-keys.json)|キーと修飾キーの扱いの固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:63、baseline、builtin_spelling|回帰・比較に必要|
|[rust/core/tests/data/upstream-misspelling-keys.json](rust/core/tests/data/upstream-misspelling-keys.json)|スペル検査・修正候補の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:104、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-misspellings.json](rust/core/tests/data/upstream-misspellings.json)|スペル検査・修正候補の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:2281、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-number-keys.json](rust/core/tests/data/upstream-number-keys.json)|数字・漢数字・単位の候補の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:39、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-prediction-keys.json](rust/core/tests/data/upstream-prediction-keys.json)|予測候補と選択の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:110、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-public-number-keys.json](rust/core/tests/data/upstream-public-number-keys.json)|数字・漢数字・単位の候補の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:48、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-reconversion.json](rust/core/tests/data/upstream-reconversion.json)|選択文字列の再変換の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:28、baseline、host_sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-scored-keys.json](rust/core/tests/data/upstream-scored-keys.json)|キーと修飾キーの扱いの固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:87、baseline、builtin_spelling|回帰・比較に必要|
|[rust/core/tests/data/upstream-settings-apps.json](rust/core/tests/data/upstream-settings-apps.json)|設定と候補の実効動作の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|384項目|回帰・比較に必要|
|[rust/core/tests/data/upstream-settings-candidates.json](rust/core/tests/data/upstream-settings-candidates.json)|設定と候補の実効動作の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:6310c44b72、cases:677、baseline、captures|回帰・比較に必要|
|[rust/core/tests/data/upstream-settings-exhaustive-batch.json](rust/core/tests/data/upstream-settings-exhaustive-batch.json)|設定と候補の実効動作の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:256、baseline、captures|回帰・比較に必要|
|[rust/core/tests/data/upstream-settings-expanded-regressions.json](rust/core/tests/data/upstream-settings-expanded-regressions.json)|設定と候補の実効動作の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:3、baseline、source|回帰・比較に必要|
|[rust/core/tests/data/upstream-single-choice.json](rust/core/tests/data/upstream-single-choice.json)|`upstream-single-choice` にまとめた処理の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:16、baseline、sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-sound-boundaries.json](rust/core/tests/data/upstream-sound-boundaries.json)|`upstream-sound-boundaries` にまとめた処理の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:9、baseline、builtin_spelling|回帰・比較に必要|
|[rust/core/tests/data/upstream-unicode-marks.json](rust/core/tests/data/upstream-unicode-marks.json)|Unicode/UTF-16の境界の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、baseline、sha256、categories|回帰・比較に必要|
|[rust/core/tests/data/upstream-unknown-words.json](rust/core/tests/data/upstream-unknown-words.json)|`upstream-unknown-words` にまとめた処理の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:9、baseline、reference_executable_sha256|回帰・比較に必要|
|[rust/core/tests/data/upstream-v130-defaults.json](rust/core/tests/data/upstream-v130-defaults.json)|`upstream-v130-defaults` にまとめた処理の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|upstream、defaults|回帰・比較に必要|
|[rust/core/tests/data/upstream-v130-input-edges.json](rust/core/tests/data/upstream-v130-input-edges.json)|入力イベント・入力先の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|cases:65、upstreamCommit、cases|回帰・比較に必要|
|[rust/core/tests/data/upstream-v130-sessions.json](rust/core/tests/data/upstream-v130-sessions.json)|入力セッションの状態遷移の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|cases:79、upstreamCommit、cases|回帰・比較に必要|
|[rust/core/tests/data/upstream-v130-settings.json](rust/core/tests/data/upstream-v130-settings.json)|設定と候補の実効動作の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|upstreamCommit、settings、baseSettings|回帰・比較に必要|
|[rust/core/tests/data/upstream-v130-values.json](rust/core/tests/data/upstream-v130-values.json)|`upstream-v130-values` にまとめた処理の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|cases:472、upstream、commit、source_sha256|回帰・比較に必要|
|[rust/core/tests/data_interop.rs](rust/core/tests/data_interop.rs)|`data_interop` にまとめた処理の回帰・境界・失敗時の動作を検査する。|backup、upstream_backup_replaces_stale_state_and_p、backup_exports_updated_upstream_files_and_、restore_preflights_all_targ|回帰・比較に必要|
|[rust/core/tests/detection.rs](rust/core/tests/detection.rs)|日英・入力対象の判定の回帰・境界・失敗時の動作を検査する。|classify、upstream_detection_verdicts_and_early_deci、detection_settings_control_kana_threshold_、romaji_analysis_distingui|回帰・比較に必要|
|[rust/core/tests/features.rs](rust/core/tests/features.rs)|`features` にまとめた処理の回帰・境界・失敗時の動作を検査する。|type_text、settings_profiles_preserve_shared_and_futu、app_rules_last_override_and_custom_kind、durable_learning_and_concur|回帰・比較に必要|
|[rust/core/tests/learning_management.rs](rust/core/tests/learning_management.rs)|学習の保存・反映の回帰・境界・失敗時の動作を検査する。|forgetting_individual_learning_keeps_other、translation_counts_keep_short_readings_and|回帰・比較に必要|
|[rust/core/tests/macos_product.rs](rust/core/tests/macos_product.rs)|macOSのOS連携の回帰・境界・失敗時の動作を検査する。|mac、type_text、external_mixed_reading_keeps_english_and_a、mac_phrase_spacing_preserves_case_and_expl|回帰・比較に必要|
|[rust/core/tests/mixed_clauses.rs](rust/core/tests/mixed_clauses.rs)|文節の分割・選択・伸縮の回帰・境界・失敗時の動作を検査する。|typed、mixed、grows_into_english_using_its_kana_and_pres、shrinking_english_moves_its_kana_into_the_|回帰・比較に必要|
|[rust/core/tests/prefix_learning.rs](rust/core/tests/prefix_learning.rs)|学習の保存・反映の回帰・境界・失敗時の動作を検査する。|persisted_false_positive_feedback_reaches_、imports_original_model_once_and_clear_does、invalid_legacy_model_preserves_byt|回帰・比較に必要|
|[rust/core/tests/private_learning.rs](rust/core/tests/private_learning.rs)|学習の保存・反映の回帰・境界・失敗時の動作を検査する。|private_scope_converts_without_learning_an|回帰・比較に必要|
|[rust/core/tests/reconversion.rs](rust/core/tests/reconversion.rs)|選択文字列の再変換の回帰・境界・失敗時の動作を検査する。|romaji_plan_matches_original_conversion_re、explicit_reconversion_works_in_direct_mode、cancel_restores_original_and_reset|回帰・比較に必要|
|[rust/core/tests/session.rs](rust/core/tests/session.rs)|入力セッションの状態遷移の回帰・境界・失敗時の動作を検査する。|type_text、mixed_language_and_protected_spans、conversion_candidate_and_learning、shortcuts_commit_once_and_pass_through|回帰・比較に必要|
|[rust/core/tests/settings_edits.rs](rust/core/tests/settings_edits.rs)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|settings、unchanged_window_preserves_current_setting、window_edits_merge_into_original_profile_a、edited_profile_switch_and|回帰・比較に必要|
|[rust/core/tests/sigil_passthrough.rs](rust/core/tests/sigil_passthrough.rs)|`sigil_passthrough` にまとめた処理の回帰・境界・失敗時の動作を検査する。|key、passes、upstream_sigil_words_pass_each_key_until_s、host_context_ends_a_sigil_when_space_or_ne|回帰・比較に必要|
|[rust/core/tests/snippet_files.rs](rust/core/tests/snippet_files.rs)|`snippet_files` にまとめた処理の回帰・境界・失敗時の動作を検査する。|upstream_snippet_files_merge_replace_norma、malformed_snippet_files_are_rejected_befor|回帰・比較に必要|
|[rust/core/tests/sqlite_dictionary.rs](rust/core/tests/sqlite_dictionary.rs)|辞書の検索・更新の回帰・境界・失敗時の動作を検査する。|add、legacy_dictionary_migrates_preserving_prio、empty_dictionary_does_not_resurrect_legacy、corrupt_and_future_databases_f|回帰・比較に必要|
|[rust/core/tests/upstream_backspace.rs](rust/core/tests/upstream_backspace.rs)|`upstream_backspace` にまとめた処理の回帰・境界・失敗時の動作を検査する。|upstream_backspace_units_and_pending_lette、retained_units_survive_function_keys_and_n|回帰・比較に必要|
|[rust/core/tests/upstream_balanced_keys.rs](rust/core/tests/upstream_balanced_keys.rs)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|original_balanced_words_edits_and_capitali|回帰・比較に必要|
|[rust/core/tests/upstream_case_keys.rs](rust/core/tests/upstream_case_keys.rs)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|original_f9_f10_case_cycles_match|回帰・比較に必要|
|[rust/core/tests/upstream_core_data.rs](rust/core/tests/upstream_core_data.rs)|`upstream_core_data` にまとめた処理の回帰・境界・失敗時の動作を検査する。|fixture、original_levels_context_learning_custom_ta、known_settings、v130_known_settings_normalization_and_prof|回帰・比較に必要|
|[rust/core/tests/upstream_direct_time.rs](rust/core/tests/upstream_direct_time.rs)|時刻とタイミングの回帰・境界・失敗時の動作を検査する。|session、original_timed_direct_input_matches_each_e、timeout_never_deletes_unverified_text_and_|回帰・比較に必要|
|[rust/core/tests/upstream_hook_session.rs](rust/core/tests/upstream_hook_session.rs)|入力セッションの状態遷移の回帰・境界・失敗時の動作を検査する。|project、strip、original_hook_and_learning_match_all_182_e、inputs|回帰・比較に必要|
|[rust/core/tests/upstream_long_keys.rs](rust/core/tests/upstream_long_keys.rs)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|long_inputs_and_trailing_english_boundarie|回帰・比較に必要|
|[rust/core/tests/upstream_misspelling_keys.rs](rust/core/tests/upstream_misspelling_keys.rs)|スペル検査・修正候補の回帰・境界・失敗時の動作を検査する。|projection、original_nonconverting_misspelling_edits_p|回帰・比較に必要|
|[rust/core/tests/upstream_misspellings.rs](rust/core/tests/upstream_misspellings.rs)|スペル検査・修正候補の回帰・境界・失敗時の動作を検査する。|original_misspelling_dictionary_results_ma|回帰・比較に必要|
|[rust/core/tests/upstream_prediction_keys.rs](rust/core/tests/upstream_prediction_keys.rs)|予測候補と選択の回帰・境界・失敗時の動作を検査する。|projection、original_prediction_display_navigation_and|回帰・比較に必要|
|[rust/core/tests/upstream_scored_keys.rs](rust/core/tests/upstream_scored_keys.rs)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|scored_words_and_edits_match_the_original_|回帰・比較に必要|
|[rust/core/tests/upstream_settings_apps.rs](rust/core/tests/upstream_settings_apps.rs)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|original_app_rules_profiles_game_detection|回帰・比較に必要|
|[rust/core/tests/upstream_settings_expanded.rs](rust/core/tests/upstream_settings_expanded.rs)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|original_prediction_order_residual_width_a|回帰・比較に必要|
|[rust/core/tests/upstream_sound_boundaries.rs](rust/core/tests/upstream_sound_boundaries.rs)|`upstream_sound_boundaries` にまとめた処理の回帰・境界・失敗時の動作を検査する。|english_spans_respect_sound_units_and_part、english_suffix_y_protection_requires_a_who|回帰・比較に必要|
|[rust/core/tests/upstream_unknown_words.rs](rust/core/tests/upstream_unknown_words.rs)|`upstream_unknown_words` にまとめた処理の回帰・境界・失敗時の動作を検査する。|unknown_words_particles_small_kana_and_dou|回帰・比較に必要|
|[rust/core/tests/upstream_v130_sessions.rs](rust/core/tests/upstream_v130_sessions.rs)|入力セッションの状態遷移の回帰・境界・失敗時の動作を検査する。|v130_key_operations_match_original|回帰・比較に必要|
|[rust/core/tests/upstream_v130_values.rs](rust/core/tests/upstream_v130_values.rs)|`upstream_v130_values` にまとめた処理の回帰・境界・失敗時の動作を検査する。|values_match_unmodified_v130_core|回帰・比較に必要|
|[rust/core/tests/v130_learning.rs](rust/core/tests/v130_learning.rs)|学習の保存・反映の回帰・境界・失敗時の動作を検査する。|type_text、contextual_choices_survive_reload_without_、delete_prediction_forgets_all_phrase_readi、intervals_are_opt_in_bou|回帰・比較に必要|
|[rust/core/tests/v130_settings.rs](rust/core/tests/v130_settings.rs)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|additions、all_28_defaults_types_profiles_and_import_、removed_choices_migrate_at_top_level_profi、migration_is_persisted_o|回帰・比較に必要|
|[rust/ibus/Cargo.toml](rust/ibus/Cargo.toml)|ibus crateの依存、OS別機能、実行形式を定義する。|"meltype-ibus"|実装・ビルドに必要|
|[rust/ibus/install.sh](rust/ibus/install.sh)|`install` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|install.sh|実装・ビルドに必要|
|[rust/ibus/src/component.rs](rust/ibus/src/component.rs)|`component` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|escape、xml|実装・ビルドに必要|
|[rust/ibus/src/keys.rs](rust/ibus/src/keys.rs)|キーと修飾キーの扱いをRust側のOSホスト・配布経路へ接続する。|translate、modifiers、maps_ibus_shortcuts_unicode_and_punctuatio|実装・ビルドに必要|
|[rust/ibus/src/main.rs](rust/ibus/src/main.rs)|`main` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|main|実装・ビルドに必要|
|[rust/ibus/src/platform/ffi.rs](rust/ibus/src/platform/ffi.rs)|`ffi` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|GTypeQuery、g_type_query、g_type_register_static_simple、g_signal_connect_data|実装・ビルドに必要|
|[rust/ibus/src/platform/mod.rs](rust/ibus/src/platform/mod.rs)|`mod` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|State、drop、with、result|実装・ビルドに必要|
|[rust/ibus/tests/installation.py](rust/ibus/tests/installation.py)|`installation` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Installation、setUp、run_script、test_install_component_command_round_trips|回帰・比較に必要|
|[rust/ibus/tests/integration.py](rust/ibus/tests/integration.py)|`integration` にまとめた処理の回帰・境界・失敗時の動作を検査する。|pump、wait、EngineTests、setUpClass|回帰・比較に必要|
|[rust/mac-register/Cargo.toml](rust/mac-register/Cargo.toml)|mac-register crateの依存、OS別機能、実行形式を定義する。|"meltype-mac-register"、"meltype-register-input-source"、"meltype-select-input-source"|実装・ビルドに必要|
|[rust/mac-register/README.md](rust/mac-register/README.md)|「macOS installer tools in Rust」の手順・仕様・確認範囲を説明する。|macOS installer tools in Rust|説明・参照に必要|
|[rust/mac-register/src/lib.rs](rust/mac-register/src/lib.rs)|`lib` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|System、default_app_path、exists、register|実装・ビルドに必要|
|[rust/mac-register/src/main.rs](rust/mac-register/src/main.rs)|`main` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|main|実装・ビルドに必要|
|[rust/mac-register/src/native.rs](rust/mac-register/src/native.rs)|ネイティブ境界と型幅をRust側のOSホスト・配布経路へ接続する。|TISRegisterInputSource、TISCreateInputSourceList、TISGetInputSourceProperty、TISSelectInputSource|実装・ビルドに必要|
|[rust/mac-register/src/select.rs](rust/mac-register/src/select.rs)|`select` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|main|実装・ビルドに必要|
|[rust/mac-register/src/selection.rs](rust/mac-register/src/selection.rs)|`selection` にまとめた処理をRust側のOSホスト・配布経路へ接続する。|SelectionSystem、list、count、id|実装・ビルドに必要|
|[rust/native/Cargo.toml](rust/native/Cargo.toml)|native crateの依存、OS別機能、実行形式を定義する。|"meltype-native"、"meltype_native"|実装・ビルドに必要|
|[rust/native/include/meltype.h](rust/native/include/meltype.h)|`meltype` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|factory、MeltypeTsfEditCallbacks、MeltypeTsfKeySnapshot、MeltypeTsfKeyCallbacks|実装・ビルドに必要|
|[rust/native/src/android.rs](rust/native/src/android.rs)|Androidの入力ホストをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Java_moe_meltype_rust_Native_startInput、Java_moe_meltype_rust_Native_isModifierKey、Java_moe_meltype_rust_Native_androidK|実装・ビルドに必要|
|[rust/native/src/android/actions.rs](rust/native/src/android/actions.rs)|`actions` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|native、render、core_reply、apply|実装・ビルドに必要|
|[rust/native/src/android/activity_view.rs](rust/native/src/android/activity_view.rs)|`activity_view` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|widget、add、build、Java_moe_meltype_rust_Native_createSetting|実装・ビルドに必要|
|[rust/native/src/android/dialogs.rs](rust/native/src/android/dialogs.rs)|`dialogs` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Action、read、build、show|実装・ビルドに必要|
|[rust/native/src/android/documents.rs](rust/native/src/android/documents.rs)|`documents` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|State、states、create、specification|実装・ビルドに必要|
|[rust/native/src/android/editor_jni.rs](rust/native/src/android/editor_jni.rs)|`editor_jni` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Ending、enter、drop、reset_session|実装・ビルドに必要|
|[rust/native/src/android/input_view.rs](rust/native/src/android/input_view.rs)|入力イベント・入力先をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Render、Choices、widget、add|実装・ビルドに必要|
|[rust/native/src/android/service_events.rs](rust/native/src/android/service_events.rs)|イベント配送と寿命をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|connection、pending、refresh、start|実装・ビルドに必要|
|[rust/native/src/android/service_state.rs](rust/native/src/android/service_state.rs)|状態と再入・終了条件をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|State、new、states、create|実装・ビルドに必要|
|[rust/native/src/android/settings.rs](rust/native/src/android/settings.rs)|設定と候補の実効動作をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|request、label、field、button|実装・ビルドに必要|
|[rust/native/src/android/settings_ui.rs](rust/native/src/android/settings_ui.rs)|設定と候補の実効動作をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|read、copy、form、status|実装・ビルドに必要|
|[rust/native/src/android/settings_view.rs](rust/native/src/android/settings_view.rs)|設定と候補の実効動作をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Page、Node、local、invoke|実装・ビルドに必要|
|[rust/native/src/android_editor.rs](rust/native/src/android_editor.rs)|Androidの入力ホストをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|EditorError、Editor、selected、before|実装・ビルドに必要|
|[rust/native/src/android_input.rs](rust/native/src/android_input.rs)|入力イベント・入力先をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|field_flags、modifier_key、virtual_key、modifiers|実装・ビルドに必要|
|[rust/native/src/fcitx.rs](rust/native/src/fcitx.rs)|`fcitx` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Frontend、frontends、attach、forget|実装・ビルドに必要|
|[rust/native/src/fcitx_dispatch.rs](rust/native/src/fcitx_dispatch.rs)|`fcitx_dispatch` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Surrounding、reply、context_string、sync|実装・ビルドに必要|
|[rust/native/src/fcitx_view.rs](rust/native/src/fcitx_view.rs)|`fcitx_view` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Event、string、strings、integer|実装・ビルドに必要|
|[rust/native/src/json_bridge.rs](rust/native/src/json_bridge.rs)|JSON形式の要求・応答をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Value、decode_utf8、Parser、skip|実装・ビルドに必要|
|[rust/native/src/lib.rs](rust/native/src/lib.rs)|各OSの薄いホストへ公開するC ABI、セッション寿命、応答メモリ解放の入口。|Callbacks、sessions、run、allocate|実装・ビルドに必要|
|[rust/native/src/macos_application.rs](rust/native/src/macos_application.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_macos_application_run、meltype_macos_services_register、NSRegisterServicesProvider、run|実装・ビルドに必要|
|[rust/native/src/macos_apply.rs](rust/native/src/macos_apply.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Edit、Response、meltype_macos_apply_utf8|実装・ビルドに必要|
|[rust/native/src/macos_candidates.rs](rust/native/src/macos_candidates.rs)|候補一覧と選択をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Update、meltype_macos_candidates_utf8|実装・ビルドに必要|
|[rust/native/src/macos_dictionary.rs](rust/native/src/macos_dictionary.rs)|辞書の検索・更新をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Merge、newline、canonical、merge|実装・ビルドに必要|
|[rust/native/src/macos_dictionary_dialog.rs](rust/native/src/macos_dictionary_dialog.rs)|辞書の検索・更新をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_macos_dictionary_dialog、run|実装・ビルドに必要|
|[rust/native/src/macos_editor.rs](rust/native/src/macos_editor.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|context、replacement、utf16、meltype_macos_context_range|実装・ビルドに必要|
|[rust/native/src/macos_indicator.rs](rust/native/src/macos_indicator.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_macos_indicator_content、meltype_macos_indicator_frame、meltype_macos_indicator_create、meltype_macos_indicator_upd|実装・ビルドに必要|
|[rust/native/src/macos_key.rs](rust/native/src/macos_key.rs)|キーと修飾キーの扱いをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_macos_key、meltype_macos_modifiers、scalar_boundaries_and_carbon_priority|実装・ビルドに必要|
|[rust/native/src/macos_meaning.rs](rust/native/src/macos_meaning.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|states、attach、forget、key|実装・ビルドに必要|
|[rust/native/src/macos_menu.rs](rust/native/src/macos_menu.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_macos_menu、create|実装・ビルドに必要|
|[rust/native/src/macos_preferences.rs](rust/native/src/macos_preferences.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_macos_mode_after、meltype_macos_indicator_preference、meltype_macos_mode_selection_create、meltype_macos_mode_selec|実装・ビルドに必要|
|[rust/native/src/macos_registration.rs](rust/native/src/macos_registration.rs)|OSの入力ソース登録をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|owned_identifier、InputSources、register、list|実装・ビルドに必要|
|[rust/native/src/macos_services.rs](rust/native/src/macos_services.rs)|OS入力サービスの連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|message、meltype_macos_services_error、meltype_macos_mode_services_create、meltype_macos_mode_services_target|実装・ビルドに必要|
|[rust/native/src/macos_suggestion.rs](rust/native/src/macos_suggestion.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_macos_suggestion、CGRectIntersectsRect、CGRectGetMinX、CGRectGetMinY|実装・ビルドに必要|
|[rust/native/src/macos_view.rs](rust/native/src/macos_view.rs)|macOSのOS連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|View、plan、meltype_macos_composition_utf8、marks_use_utf16_and_ignore_outside_or_empt|実装・ビルドに必要|
|[rust/native/src/macos_window.rs](rust/native/src/macos_window.rs)|OSウィンドウと表示をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_macos_candidate_window、meltype_macos_server_create、meltype_macos_candidates_create、string|実装・ビルドに必要|
|[rust/native/src/tip.rs](rust/native/src/tip.rs)|`tip` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|connections、meltype_tip_create、meltype_tip_destroy、meltype_tip_request|実装・ビルドに必要|
|[rust/native/src/tip_transport.rs](rust/native/src/tip_transport.rs)|プロセス間通信とフレームをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|retire_threads、threads_idle、spawn_thread、Job|実装・ビルドに必要|
|[rust/native/src/tsf.rs](rust/native/src/tsf.rs)|`tsf` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Spec、Object、ModuleLifetime、new|実装・ビルドに必要|
|[rust/native/src/tsf/abandon_control.rs](rust/native/src/tsf/abandon_control.rs)|管理コマンドの実行をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、replaced、meltype_tsf_abandon_composition|実装・ビルドに必要|
|[rust/native/src/tsf/abandoned.rs](rust/native/src/tsf/abandoned.rs)|`abandoned` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Item、State、state、same|実装・ビルドに必要|
|[rust/native/src/tsf/apply_reply.rs](rust/native/src/tsf/apply_reply.rs)|`apply_reply` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、meltype_tsf_apply_reply|実装・ビルドに必要|
|[rust/native/src/tsf/candidates.rs](rust/native/src/tsf/candidates.rs)|候補一覧と選択をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|View、State、state、with|実装・ビルドに必要|
|[rust/native/src/tsf/candidates/graphics.rs](rust/native/src/tsf/candidates/graphics.rs)|`graphics` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Graphics、new、format、layout|実装・ビルドに必要|
|[rust/native/src/tsf/candidates/paint.rs](rust/native/src/tsf/candidates/paint.rs)|`paint` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|paint、meaning_size、show_meaning、place_meaning|実装・ビルドに必要|
|[rust/native/src/tsf/candidates/presentation.rs](rust/native/src/tsf/candidates/presentation.rs)|`presentation` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|nullable_text、nullable_notes、ReplyView、project|実装・ビルドに必要|
|[rust/native/src/tsf/commit_request.rs](rust/native/src/tsf/commit_request.rs)|`commit_request` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、Work、matches、run|実装・ビルドに必要|
|[rust/native/src/tsf/commits.rs](rust/native/src/tsf/commits.rs)|`commits` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Edit、meltype_tsf_apply_commits|実装・ビルドに必要|
|[rust/native/src/tsf/composition.rs](rust/native/src/tsf/composition.rs)|未確定入力と変換状態をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|guard、attribute、meltype_tsf_start_composition、View|実装・ビルドに必要|
|[rust/native/src/tsf/composition_owner.rs](rust/native/src/tsf/composition_owner.rs)|未確定入力と変換状態をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Slots、ensure、finish、slots|実装・ビルドに必要|
|[rust/native/src/tsf/core_link.rs](rust/native/src/tsf/core_link.rs)|`core_link` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|StateCallbacks、snapshot、now、meltype_tsf_core_ready|実装・ビルドに必要|
|[rust/native/src/tsf/edit_events.rs](rust/native/src/tsf/edit_events.rs)|イベント配送と寿命をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、meltype_tsf_edit_notification|実装・ビルドに必要|
|[rust/native/src/tsf/edit_session.rs](rust/native/src/tsf/edit_session.rs)|入力セッションの状態遷移をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Session、session、query、add_ref|実装・ビルドに必要|
|[rust/native/src/tsf/factory.rs](rust/native/src/tsf/factory.rs)|`factory` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Factory、factory、query、add_ref|実装・ビルドに必要|
|[rust/native/src/tsf/fields.rs](rust/native/src/tsf/fields.rs)|`fields` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|ScopeList、drop、query、property|実装・ビルドに必要|
|[rust/native/src/tsf/focus.rs](rust/native/src/tsf/focus.rs)|`focus` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、current、meltype_tsf_document_focus、meltype_tsf_foreground_focus|実装・ビルドに必要|
|[rust/native/src/tsf/key_edit.rs](rust/native/src/tsf/key_edit.rs)|キーと修飾キーの扱いをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、Outcome、Work、surrounding|実装・ビルドに必要|
|[rust/native/src/tsf/key_state.rs](rust/native/src/tsf/key_state.rs)|キーと修飾キーの扱いをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Pending、Queue、observe、prepare|実装・ビルドに必要|
|[rust/native/src/tsf/keys.rs](rust/native/src/tsf/keys.rs)|キーと修飾キーの扱いをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_tsf_key_flags、meltype_tsf_key_down、modifiers、meltype_tsf_key_modifiers|実装・ビルドに必要|
|[rust/native/src/tsf/langbar.rs](rust/native/src/tsf/langbar.rs)|`langbar` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Client、State、Bar、bar|実装・ビルドに必要|
|[rust/native/src/tsf/mode.rs](rust/native/src/tsf/mode.rs)|`mode` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、ReadResult、Read、receive_before|実装・ビルドに必要|
|[rust/native/src/tsf/open_change.rs](rust/native/src/tsf/open_change.rs)|`open_change` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、snapshot、meltype_tsf_open_changed|実装・ビルドに必要|
|[rust/native/src/tsf/process.rs](rust/native/src/tsf/process.rs)|`process` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|filename、exe_suffix、display_name、module_path|実装・ビルドに必要|
|[rust/native/src/tsf/ranges.rs](rust/native/src/tsf/ranges.rs)|`ranges` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|composition_range、attribute_property、selection、caret|実装・ビルドに必要|
|[rust/native/src/tsf/recovery.rs](rust/native/src/tsf/recovery.rs)|`recovery` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Snapshot、take、meltype_tsf_capture_resume、meltype_tsf_resume_matches|実装・ビルドに必要|
|[rust/native/src/tsf/recovery_control.rs](rust/native/src/tsf/recovery_control.rs)|管理コマンドの実行をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、Pending、clear、drop|実装・ビルドに必要|
|[rust/native/src/tsf/registration.rs](rust/native/src/tsf/registration.rs)|OSの入力ソース登録をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Backend、server、profile、categories|実装・ビルドに必要|
|[rust/native/src/tsf/server.rs](rust/native/src/tsf/server.rs)|`server` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|retain_module、release_module、lock、module_path|実装・ビルドに必要|
|[rust/native/src/tsf/service_com.rs](rust/native/src/tsf/service_com.rs)|OS入力サービスの連携をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Call、Slot、Service、owner|実装・ビルドに必要|
|[rust/native/src/tsf/service_state.rs](rust/native/src/tsf/service_state.rs)|状態と再入・終了条件をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|ServiceState、default、drop、meltype_tsf_service_mode|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/activate.rs](rust/native/src/tsf/service_state/activate.rs)|`activate` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、initialize、core_notice、category|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/apply.rs](rust/native/src/tsf/service_state/apply.rs)|`apply` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、current、write、valid|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/commit.rs](rust/native/src/tsf/service_state/commit.rs)|`commit` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、Reply、current、update|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/core.rs](rust/native/src/tsf/service_state/core.rs)|`core` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、owner、read、update|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/deactivate.rs](rust/native/src/tsf/service_state/deactivate.rs)|`deactivate` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、read、action、request|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/dispatch.rs](rust/native/src/tsf/service_state/dispatch.rs)|`dispatch` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、commit、handle、notice|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/edited.rs](rust/native/src/tsf/service_state/edited.rs)|`edited` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、take_own、current、query|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/exchange.rs](rust/native/src/tsf/service_state/exchange.rs)|`exchange` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、read、transact、parse|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/focus.rs](rust/native/src/tsf/service_state/focus.rs)|`focus` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、read、commit、action|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/input.rs](rust/native/src/tsf/service_state/input.rs)|入力イベント・入力先をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_tsf_service_input_scope|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/key_input.rs](rust/native/src/tsf/service_state/key_input.rs)|キーと修飾キーの扱いをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、relay、Read、read_scope|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/key_work.rs](rust/native/src/tsf/service_state/key_work.rs)|キーと修飾キーの扱いをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、log、scope、mode_changed|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/mode_action.rs](rust/native/src/tsf/service_state/mode_action.rs)|`mode_action` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、relay、read、ready|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/notifications.rs](rust/native/src/tsf/service_state/notifications.rs)|`notifications` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、relay、failure、focus_commit|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/object.rs](rust/native/src/tsf/service_state/object.rs)|`object` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Object、drop、dispatch、destroy|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/opened.rs](rust/native/src/tsf/service_state/opened.rs)|`opened` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、current、update、meltype_tsf_service_opened|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/owned.rs](rust/native/src/tsf/service_state/owned.rs)|`owned` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|frame、meltype_tsf_service_apply_composition、meltype_tsf_service_write_commit、meltype_tsf_service_end_composition|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/recovered.rs](rust/native/src/tsf/service_state/recovered.rs)|`recovered` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Owner、metadata、valid、failure|実装・ビルドに必要|
|[rust/native/src/tsf/service_state/route.rs](rust/native/src/tsf/service_state/route.rs)|`route` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|get、Owner、invalid、meltype_tsf_service_route|実装・ビルドに必要|
|[rust/native/src/tsf/shutdown.rs](rust/native/src/tsf/shutdown.rs)|`shutdown` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、current、Work、run|実装・ビルドに必要|
|[rust/native/src/tsf/startup.rs](rust/native/src/tsf/startup.rs)|自動起動と保守コマンドをOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、current、meltype_tsf_startup|実装・ビルドに必要|
|[rust/native/src/tsf/subscriptions.rs](rust/native/src/tsf/subscriptions.rs)|`subscriptions` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Subscription、drop、source、top|実装・ビルドに必要|
|[rust/native/src/tsf/termination.rs](rust/native/src/tsf/termination.rs)|`termination` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Callbacks、same、meltype_tsf_composition_terminated|実装・ビルドに必要|
|[rust/native/src/windows_clipboard.rs](rust/native/src/windows_clipboard.rs)|クリップボード候補の取得・保護をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|OpenClipboard、CloseClipboard、RegisterClipboardFormatW、IsClipboardFormatAvailable|実装・ビルドに必要|
|[rust/native/src/windows_converter.rs](rust/native/src/windows_converter.rs)|OSウィンドウと表示をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|meltype_windows_converter_probe|実装・ビルドに必要|
|[rust/native/src/windows_converter/candidates.rs](rust/native/src/windows_converter/candidates.rs)|候補一覧と選択をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Candidates、get|実装・ビルドに必要|
|[rust/native/src/windows_converter/system.rs](rust/native/src/windows_converter/system.rs)|`system` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Apartment、enter、drop、Segment|実装・ビルドに必要|
|[rust/native/src/windows_converter/worker.rs](rust/native/src/windows_converter/worker.rs)|別プロセスworkerの要求・応答をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|Mode、parse、Backend、available|実装・ビルドに必要|
|[rust/native/src/windows_github.rs](rust/native/src/windows_github.rs)|GitHub由来の候補・情報をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|lookup、fetch|実装・ビルドに必要|
|[rust/native/src/word_checker.rs](rust/native/src/word_checker.rs)|`word_checker` にまとめた処理をOS SDK/C ABIへ接続し、型幅・寿命・応答の所有を管理する。|State、state、configure、meltype_word_check_count|実装・ビルドに必要|
|[rust/native/tests/abi_smoke.py](rust/native/tests/abi_smoke.py)|外部ABIの型・所有権の回帰・境界・失敗時の動作を検査する。|main、allocate、clauses、candidates|回帰・比較に必要|
|[rust/native/tests/android/ActivityViewHost.java](rust/native/tests/android/ActivityViewHost.java)|`ActivityViewHost` にまとめた処理の回帰・境界・失敗時の動作を検査する。|ActivityViewHost|回帰・比較に必要|
|[rust/native/tests/android/ContextHost.java](rust/native/tests/android/ContextHost.java)|周辺文・文脈の判定の回帰・境界・失敗時の動作を検査する。|ContextHost|回帰・比較に必要|
|[rust/native/tests/android/DialogHost.java](rust/native/tests/android/DialogHost.java)|`DialogHost` にまとめた処理の回帰・境界・失敗時の動作を検査する。|DialogHost|回帰・比較に必要|
|[rust/native/tests/android/DispatchHost.java](rust/native/tests/android/DispatchHost.java)|`DispatchHost` にまとめた処理の回帰・境界・失敗時の動作を検査する。|DispatchHost、Manager|回帰・比較に必要|
|[rust/native/tests/android/DocumentHost.java](rust/native/tests/android/DocumentHost.java)|`DocumentHost` にまとめた処理の回帰・境界・失敗時の動作を検査する。|DocumentHost、Input、Output|回帰・比較に必要|
|[rust/native/tests/android/EditorHost.java](rust/native/tests/android/EditorHost.java)|`EditorHost` にまとめた処理の回帰・境界・失敗時の動作を検査する。|EditorHost、Editor|回帰・比較に必要|
|[rust/native/tests/android/InputHost.java](rust/native/tests/android/InputHost.java)|入力イベント・入力先の回帰・境界・失敗時の動作を検査する。|InputHost|回帰・比較に必要|
|[rust/native/tests/android/KeyEvent.java](rust/native/tests/android/KeyEvent.java)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|KeyEvent|回帰・比較に必要|
|[rust/native/tests/android/LifecycleHost.java](rust/native/tests/android/LifecycleHost.java)|起動・終了・保存の寿命の回帰・境界・失敗時の動作を検査する。|LifecycleHost|回帰・比較に必要|
|[rust/native/tests/android/ServiceHost.java](rust/native/tests/android/ServiceHost.java)|OS入力サービスの連携の回帰・境界・失敗時の動作を検査する。|ServiceHost|回帰・比較に必要|
|[rust/native/tests/android/SettingsActivity.java](rust/native/tests/android/SettingsActivity.java)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|token、SettingsActivity|回帰・比較に必要|
|[rust/native/tests/android/SettingsHost.java](rust/native/tests/android/SettingsHost.java)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|SettingsHost|回帰・比較に必要|
|[rust/native/tests/android/SettingsPage.java](rust/native/tests/android/SettingsPage.java)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|SettingsPage|回帰・比較に必要|
|[rust/native/tests/android/SettingsUiHost.java](rust/native/tests/android/SettingsUiHost.java)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|SettingsUiHost|回帰・比較に必要|
|[rust/native/tests/android/SettingsViewHost.java](rust/native/tests/android/SettingsViewHost.java)|設定と候補の実効動作の回帰・境界・失敗時の動作を検査する。|SettingsViewHost|回帰・比較に必要|
|[rust/native/tests/android/StateHost.java](rust/native/tests/android/StateHost.java)|状態と再入・終了条件の回帰・境界・失敗時の動作を検査する。|StateHost|回帰・比較に必要|
|[rust/native/tests/android/WidgetHost.java](rust/native/tests/android/WidgetHost.java)|`WidgetHost` にまとめた処理の回帰・境界・失敗時の動作を検査する。|WidgetHost|回帰・比較に必要|
|[rust/native/tests/android/widgets/AdapterView.java](rust/native/tests/android/widgets/AdapterView.java)|`AdapterView` にまとめた処理の回帰・境界・失敗時の動作を検査する。|AdapterView、OnItemSelectedListener|回帰・比較に必要|
|[rust/native/tests/android/widgets/AlertDialog.java](rust/native/tests/android/widgets/AlertDialog.java)|`AlertDialog` にまとめた処理の回帰・境界・失敗時の動作を検査する。|AlertDialog、Builder|回帰・比較に必要|
|[rust/native/tests/android/widgets/ArrayAdapter.java](rust/native/tests/android/widgets/ArrayAdapter.java)|`ArrayAdapter` にまとめた処理の回帰・境界・失敗時の動作を検査する。|ArrayAdapter|回帰・比較に必要|
|[rust/native/tests/android/widgets/Build.java](rust/native/tests/android/widgets/Build.java)|`Build` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Build、VERSION|回帰・比較に必要|
|[rust/native/tests/android/widgets/Button.java](rust/native/tests/android/widgets/Button.java)|`Button` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Button|回帰・比較に必要|
|[rust/native/tests/android/widgets/CompoundButton.java](rust/native/tests/android/widgets/CompoundButton.java)|`CompoundButton` にまとめた処理の回帰・境界・失敗時の動作を検査する。|CompoundButton、OnCheckedChangeListener|回帰・比較に必要|
|[rust/native/tests/android/widgets/ContentResolver.java](rust/native/tests/android/widgets/ContentResolver.java)|`ContentResolver` にまとめた処理の回帰・境界・失敗時の動作を検査する。|ContentResolver|回帰・比較に必要|
|[rust/native/tests/android/widgets/Context.java](rust/native/tests/android/widgets/Context.java)|周辺文・文脈の判定の回帰・境界・失敗時の動作を検査する。|Context|回帰・比較に必要|
|[rust/native/tests/android/widgets/DialogInterface.java](rust/native/tests/android/widgets/DialogInterface.java)|`DialogInterface` にまとめた処理の回帰・境界・失敗時の動作を検査する。|DialogInterface、OnClickListener、OnCancelListener|回帰・比較に必要|
|[rust/native/tests/android/widgets/DisplayMetrics.java](rust/native/tests/android/widgets/DisplayMetrics.java)|表示情報・候補の描画の回帰・境界・失敗時の動作を検査する。|DisplayMetrics|回帰・比較に必要|
|[rust/native/tests/android/widgets/EditText.java](rust/native/tests/android/widgets/EditText.java)|文字列と編集範囲の回帰・境界・失敗時の動作を検査する。|EditText|回帰・比較に必要|
|[rust/native/tests/android/widgets/Editable.java](rust/native/tests/android/widgets/Editable.java)|`Editable` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Editable|回帰・比較に必要|
|[rust/native/tests/android/widgets/EditorInfo.java](rust/native/tests/android/widgets/EditorInfo.java)|`EditorInfo` にまとめた処理の回帰・境界・失敗時の動作を検査する。|EditorInfo|回帰・比較に必要|
|[rust/native/tests/android/widgets/HorizontalScrollView.java](rust/native/tests/android/widgets/HorizontalScrollView.java)|`HorizontalScrollView` にまとめた処理の回帰・境界・失敗時の動作を検査する。|HorizontalScrollView|回帰・比較に必要|
|[rust/native/tests/android/widgets/InputConnection.java](rust/native/tests/android/widgets/InputConnection.java)|入力イベント・入力先の回帰・境界・失敗時の動作を検査する。|InputConnection|回帰・比較に必要|
|[rust/native/tests/android/widgets/Intent.java](rust/native/tests/android/widgets/Intent.java)|`Intent` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Intent|回帰・比較に必要|
|[rust/native/tests/android/widgets/LinearLayout.java](rust/native/tests/android/widgets/LinearLayout.java)|`LinearLayout` にまとめた処理の回帰・境界・失敗時の動作を検査する。|LinearLayout、LayoutParams|回帰・比較に必要|
|[rust/native/tests/android/widgets/R.java](rust/native/tests/android/widgets/R.java)|`R` にまとめた処理の回帰・境界・失敗時の動作を検査する。|R、layout|回帰・比較に必要|
|[rust/native/tests/android/widgets/Resources.java](rust/native/tests/android/widgets/Resources.java)|`Resources` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Resources|回帰・比較に必要|
|[rust/native/tests/android/widgets/ScrollView.java](rust/native/tests/android/widgets/ScrollView.java)|`ScrollView` にまとめた処理の回帰・境界・失敗時の動作を検査する。|ScrollView|回帰・比較に必要|
|[rust/native/tests/android/widgets/Spinner.java](rust/native/tests/android/widgets/Spinner.java)|`Spinner` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Spinner|回帰・比較に必要|
|[rust/native/tests/android/widgets/SpinnerAdapter.java](rust/native/tests/android/widgets/SpinnerAdapter.java)|`SpinnerAdapter` にまとめた処理の回帰・境界・失敗時の動作を検査する。|SpinnerAdapter|回帰・比較に必要|
|[rust/native/tests/android/widgets/Switch.java](rust/native/tests/android/widgets/Switch.java)|`Switch` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Switch|回帰・比較に必要|
|[rust/native/tests/android/widgets/TextView.java](rust/native/tests/android/widgets/TextView.java)|文字列と編集範囲の回帰・境界・失敗時の動作を検査する。|TextView|回帰・比較に必要|
|[rust/native/tests/android/widgets/Uri.java](rust/native/tests/android/widgets/Uri.java)|`Uri` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Uri|回帰・比較に必要|
|[rust/native/tests/android/widgets/View.java](rust/native/tests/android/widgets/View.java)|`View` にまとめた処理の回帰・境界・失敗時の動作を検査する。|View、OnClickListener|回帰・比較に必要|
|[rust/native/tests/android/widgets/ViewGroup.java](rust/native/tests/android/widgets/ViewGroup.java)|`ViewGroup` にまとめた処理の回帰・境界・失敗時の動作を検査する。|ViewGroup、LayoutParams|回帰・比較に必要|
|[rust/native/tests/android_editor_smoke.py](rust/native/tests/android_editor_smoke.py)|Androidの入力ホストの回帰・境界・失敗時の動作を検査する。|android_editor_smoke.py|回帰・比較に必要|
|[rust/native/tests/apply_reply_host.cpp](rust/native/tests/apply_reply_host.cpp)|`apply_reply_host` にまとめた処理の回帰・境界・失敗時の動作を検査する。|RustApplyReplyCallbacks、Context、State|回帰・比較に必要|
|[rust/native/tests/build-edit-session-host.ps1](rust/native/tests/build-edit-session-host.ps1)|入力セッションの状態遷移の回帰・境界・失敗時の動作を検査する。|param、if、foreach|回帰・比較に必要|
|[rust/native/tests/build-reference-candidates.ps1](rust/native/tests/build-reference-candidates.ps1)|候補一覧と選択の回帰・境界・失敗時の動作を検査する。|param、if、foreach|回帰・比較に必要|
|[rust/native/tests/build-reference-keys.ps1](rust/native/tests/build-reference-keys.ps1)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|param、if、foreach|回帰・比較に必要|
|[rust/native/tests/build-transport-fixture.ps1](rust/native/tests/build-transport-fixture.ps1)|プロセス間通信とフレームの回帰・境界・失敗時の動作を検査する。|foreach|回帰・比較に必要|
|[rust/native/tests/candidate_view_host.cpp](rust/native/tests/candidate_view_host.cpp)|候補一覧と選択の回帰・境界・失敗時の動作を検査する。|Selection|回帰・比較に必要|
|[rust/native/tests/case_keys_smoke.py](rust/native/tests/case_keys_smoke.py)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|result、key|回帰・比較に必要|
|[rust/native/tests/clause_resize_smoke.py](rust/native/tests/clause_resize_smoke.py)|文節の分割・選択・伸縮の回帰・境界・失敗時の動作を検査する。|allocate、clauses、convert、candidates|回帰・比較に必要|
|[rust/native/tests/commit_batch_host.cpp](rust/native/tests/commit_batch_host.cpp)|`commit_batch_host` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Document、Range、Context|回帰・比較に必要|
|[rust/native/tests/core_data_smoke.py](rust/native/tests/core_data_smoke.py)|`core_data_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|core_data_smoke.py|回帰・比較に必要|
|[rust/native/tests/custom_romaji_smoke.py](rust/native/tests/custom_romaji_smoke.py)|ローマ字表とかな変換の回帰・境界・失敗時の動作を検査する。|read|回帰・比較に必要|
|[rust/native/tests/data/upstream-windows-converter.json](rust/native/tests/data/upstream-windows-converter.json)|OSウィンドウと表示の固定入力・期待応答。テストから読み、Rustの出力を期待値として上書きしない。|基準:45e14f8655、cases:27、baseline、host_sha256|回帰・比較に必要|
|[rust/native/tests/direct_time_smoke.py](rust/native/tests/direct_time_smoke.py)|時刻とタイミングの回帰・境界・失敗時の動作を検査する。|key|回帰・比較に必要|
|[rust/native/tests/edit_events_host.cpp](rust/native/tests/edit_events_host.cpp)|イベント配送と寿命の回帰・境界・失敗時の動作を検査する。|Context、Record、State|回帰・比較に必要|
|[rust/native/tests/edit_session_host.cpp](rust/native/tests/edit_session_host.cpp)|入力セッションの状態遷移の回帰・境界・失敗時の動作を検査する。|Context、ContextVtable、Guard|回帰・比較に必要|
|[rust/native/tests/fields_host.cpp](rust/native/tests/fields_host.cpp)|`fields_host` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Compartment、EventSink、Source、Manager|回帰・比較に必要|
|[rust/native/tests/fixtures/windows_mozc.rs](rust/native/tests/fixtures/windows_mozc.rs)|OSウィンドウと表示の回帰・境界・失敗時の動作を検査する。|main、GetErrorMode|回帰・比較に必要|
|[rust/native/tests/hook_smoke.py](rust/native/tests/hook_smoke.py)|キーボードフックとキー所有の回帰・境界・失敗時の動作を検査する。|request、project|回帰・比較に必要|
|[rust/native/tests/json_host.cpp](rust/native/tests/json_host.cpp)|JSON形式の要求・応答の回帰・境界・失敗時の動作を検査する。|json_host.cpp|回帰・比較に必要|
|[rust/native/tests/key_edit_host.cpp](rust/native/tests/key_edit_host.cpp)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|factory、FixtureService、T、PrivateProfile|回帰・比較に必要|
|[rust/native/tests/keys_host.cpp](rust/native/tests/keys_host.cpp)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|RustKeySnapshot、RustKeyCallbacks、Context、Keyboard|回帰・比較に必要|
|[rust/native/tests/macos_apply_smoke.py](rust/native/tests/macos_apply_smoke.py)|macOSのOS連携の回帰・境界・失敗時の動作を検査する。|run、callback|回帰・比較に必要|
|[rust/native/tests/macos_boundary_smoke.py](rust/native/tests/macos_boundary_smoke.py)|入力・文字列の境界の回帰・境界・失敗時の動作を検査する。|result、clauses、candidates、owned|回帰・比較に必要|
|[rust/native/tests/macos_candidates_smoke.py](rust/native/tests/macos_candidates_smoke.py)|候補一覧と選択の回帰・境界・失敗時の動作を検査する。|run、callback|回帰・比較に必要|
|[rust/native/tests/macos_dictionary_smoke.py](rust/native/tests/macos_dictionary_smoke.py)|辞書の検索・更新の回帰・境界・失敗時の動作を検査する。|unavailable_directory、merge|回帰・比較に必要|
|[rust/native/tests/macos_meaning_smoke.py](rust/native/tests/macos_meaning_smoke.py)|macOSのOS連携の回帰・境界・失敗時の動作を検査する。|units、update、matches、key|回帰・比較に必要|
|[rust/native/tests/macos_product_smoke.py](rust/native/tests/macos_product_smoke.py)|macOSのOS連携の回帰・境界・失敗時の動作を検査する。|allocate、clauses、candidates、word|回帰・比較に必要|
|[rust/native/tests/macos_registration_smoke.py](rust/native/tests/macos_registration_smoke.py)|OSの入力ソース登録の回帰・境界・失敗時の動作を検査する。|macos_registration_smoke.py|回帰・比較に必要|
|[rust/native/tests/macos_view_smoke.py](rust/native/tests/macos_view_smoke.py)|macOSのOS連携の回帰・境界・失敗時の動作を検査する。|run、callback|回帰・比較に必要|
|[rust/native/tests/misspelling_marks_smoke.py](rust/native/tests/misspelling_marks_smoke.py)|スペル検査・修正候補の回帰・境界・失敗時の動作を検査する。|main、no_os_word、result|回帰・比較に必要|
|[rust/native/tests/misspelling_reference.py](rust/native/tests/misspelling_reference.py)|スペル検査・修正候補の回帰・境界・失敗時の動作を検査する。|misspelling_reference.py|回帰・比較に必要|
|[rust/native/tests/mode_host.cpp](rust/native/tests/mode_host.cpp)|`mode_host` にまとめた処理の回帰・境界・失敗時の動作を検査する。|RustModeCallbacks、RustModeQueryCallbacks、RustModeApplyCallbacks、F|回帰・比較に必要|
|[rust/native/tests/mozc_learning_reference.py](rust/native/tests/mozc_learning_reference.py)|学習の保存・反映の回帰・境界・失敗時の動作を検査する。|mozc_learning_reference.py|回帰・比較に必要|
|[rust/native/tests/mozc_lifecycle_smoke.py](rust/native/tests/mozc_lifecycle_smoke.py)|起動・終了・保存の寿命の回帰・境界・失敗時の動作を検査する。|mozc_lifecycle_smoke.py|回帰・比較に必要|
|[rust/native/tests/native_key_owner_host.cpp](rust/native/tests/native_key_owner_host.cpp)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|native_key_owner_host.cpp|回帰・比較に必要|
|[rust/native/tests/number_keys_smoke.py](rust/native/tests/number_keys_smoke.py)|数字・漢数字・単位の候補の回帰・境界・失敗時の動作を検査する。|allocate、candidate、clauses、candidates|回帰・比較に必要|
|[rust/native/tests/prediction_keys_smoke.py](rust/native/tests/prediction_keys_smoke.py)|予測候補と選択の回帰・境界・失敗時の動作を検査する。|allocate、candidate、clauses、candidates|回帰・比較に必要|
|[rust/native/tests/prediction_worker_smoke.py](rust/native/tests/prediction_worker_smoke.py)|予測候補と選択の回帰・境界・失敗時の動作を検査する。|call、projection|回帰・比較に必要|
|[rust/native/tests/prepare-reference-json.ps1](rust/native/tests/prepare-reference-json.ps1)|JSON形式の要求・応答の回帰・境界・失敗時の動作を検査する。|if|回帰・比較に必要|
|[rust/native/tests/reconversion_smoke.py](rust/native/tests/reconversion_smoke.py)|選択文字列の再変換の回帰・境界・失敗時の動作を検査する。|allocate、convert、clauses、result|回帰・比較に必要|
|[rust/native/tests/reference_candidates.cpp](rust/native/tests/reference_candidates.cpp)|候補一覧と選択の回帰・境界・失敗時の動作を検査する。|reference_candidates.cpp|回帰・比較に必要|
|[rust/native/tests/scored_smoke.py](rust/native/tests/scored_smoke.py)|スコア付きの区切り判定の回帰・境界・失敗時の動作を検査する。|scored_smoke.py|回帰・比較に必要|
|[rust/native/tests/selection_host.cpp](rust/native/tests/selection_host.cpp)|`selection_host` にまとめた処理の回帰・境界・失敗時の動作を検査する。|RustCommitCallbacks、Context、State|回帰・比較に必要|
|[rust/native/tests/service_state_host.cpp](rust/native/tests/service_state_host.cpp)|状態と再入・終了条件の回帰・境界・失敗時の動作を検査する。|service_state_host.cpp|回帰・比較に必要|
|[rust/native/tests/shift_space_smoke.py](rust/native/tests/shift_space_smoke.py)|`shift_space_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|allocate、clauses、candidates、result|回帰・比較に必要|
|[rust/native/tests/support/tip_transport_fixture.rs](rust/native/tests/support/tip_transport_fixture.rs)|プロセス間通信とフレームの回帰・境界・失敗時の動作を検査する。|main|回帰・比較に必要|
|[rust/native/tests/tip_transport_smoke.py](rust/native/tests/tip_transport_smoke.py)|プロセス間通信とフレームの回帰・境界・失敗時の動作を検査する。|TransportTests、setUpClass、setUp、tearDown|回帰・比較に必要|
|[rust/native/tests/transport_host.cpp](rust/native/tests/transport_host.cpp)|プロセス間通信とフレームの回帰・境界・失敗時の動作を検査する。|CoreState|回帰・比較に必要|
|[rust/native/tests/tsf_abandon_control_smoke.py](rust/native/tests/tsf_abandon_control_smoke.py)|管理コマンドの実行の回帰・境界・失敗時の動作を検査する。|Callbacks、AbandonControlTests、setUpClass、setUp|回帰・比較に必要|
|[rust/native/tests/tsf_abandoned_smoke.py](rust/native/tests/tsf_abandoned_smoke.py)|`tsf_abandoned_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|DeferredHost、__init__、query_composition、get_context|回帰・比較に必要|
|[rust/native/tests/tsf_apply_reply_smoke.py](rust/native/tests/tsf_apply_reply_smoke.py)|`tsf_apply_reply_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Callbacks、ReplyTests、setUpClass、setUp|回帰・比較に必要|
|[rust/native/tests/tsf_candidate_smoke.py](rust/native/tests/tsf_candidate_smoke.py)|候補一覧と選択の回帰・境界・失敗時の動作を検査する。|Point、Rect、Message、pump|回帰・比較に必要|
|[rust/native/tests/tsf_candidate_view_smoke.py](rust/native/tests/tsf_candidate_view_smoke.py)|候補一覧と選択の回帰・境界・失敗時の動作を検査する。|GeometryHost、__init__、view_ref、get_view|回帰・比較に必要|
|[rust/native/tests/tsf_com_smoke.py](rust/native/tests/tsf_com_smoke.py)|`tsf_com_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Guid、parse、Color、Attribute|回帰・比較に必要|
|[rust/native/tests/tsf_commit_request_smoke.py](rust/native/tests/tsf_commit_request_smoke.py)|`tsf_commit_request_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Callbacks、CommitTests、setUpClass、setUp|回帰・比較に必要|
|[rust/native/tests/tsf_commit_smoke.py](rust/native/tests/tsf_commit_smoke.py)|`tsf_commit_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|BatchHost、get_selection、clear、CommitTests|回帰・比較に必要|
|[rust/native/tests/tsf_composition_owner_smoke.py](rust/native/tests/tsf_composition_owner_smoke.py)|未確定入力と変換状態の回帰・境界・失敗時の動作を検査する。|Slots、OwnerHost、__init__、get_range|回帰・比較に必要|
|[rust/native/tests/tsf_composition_smoke.py](rust/native/tests/tsf_composition_smoke.py)|未確定入力と変換状態の回帰・境界・失敗時の動作を検査する。|CompositionHost、__init__、query_context、manager_ref|回帰・比較に必要|
|[rust/native/tests/tsf_core_link_smoke.py](rust/native/tests/tsf_core_link_smoke.py)|`tsf_core_link_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|State、Exchange、CoreLinkTests、setUpClass|回帰・比較に必要|
|[rust/native/tests/tsf_edit_events_smoke.py](rust/native/tests/tsf_edit_events_smoke.py)|イベント配送と寿命の回帰・境界・失敗時の動作を検査する。|Callbacks、EventHost、__init__、record_ref|回帰・比較に必要|
|[rust/native/tests/tsf_edit_session_smoke.py](rust/native/tests/tsf_edit_session_smoke.py)|入力セッションの状態遷移の回帰・境界・失敗時の動作を検査する。|EditTests、setUpClass、setUp、tearDown|回帰・比較に必要|
|[rust/native/tests/tsf_fields_smoke.py](rust/native/tests/tsf_fields_smoke.py)|`tsf_fields_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Variant、ScopeValue、__init__、ref|回帰・比較に必要|
|[rust/native/tests/tsf_focus_smoke.py](rust/native/tests/tsf_focus_smoke.py)|`tsf_focus_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Callbacks、FocusTests、setUpClass、setUp|回帰・比較に必要|
|[rust/native/tests/tsf_key_edit_smoke.py](rust/native/tests/tsf_key_edit_smoke.py)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|Callbacks、KeyHost、__init__、context_ref|回帰・比較に必要|
|[rust/native/tests/tsf_key_entry_smoke.py](rust/native/tests/tsf_key_entry_smoke.py)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|Entry、Passed、Stale、EntryTests|回帰・比較に必要|
|[rust/native/tests/tsf_key_state_smoke.py](rust/native/tests/tsf_key_state_smoke.py)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|Keyboard、Mouse、Hardware、Payload|回帰・比較に必要|
|[rust/native/tests/tsf_keys_smoke.py](rust/native/tests/tsf_keys_smoke.py)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|Snapshot、Callbacks、KeyboardTests、setUpClass|回帰・比較に必要|
|[rust/native/tests/tsf_mode_query_smoke.py](rust/native/tests/tsf_mode_query_smoke.py)|`tsf_mode_query_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|QueryCallbacks、ApplyCallbacks、QueryTests、setUpClass|回帰・比較に必要|
|[rust/native/tests/tsf_mode_smoke.py](rust/native/tests/tsf_mode_smoke.py)|`tsf_mode_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Callbacks、ModeHost、__init__、focus_ref|回帰・比較に必要|
|[rust/native/tests/tsf_open_change_smoke.py](rust/native/tests/tsf_open_change_smoke.py)|`tsf_open_change_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Callbacks、Host、__init__、read_mode|回帰・比較に必要|
|[rust/native/tests/tsf_range_smoke.py](rust/native/tests/tsf_range_smoke.py)|`tsf_range_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|units、buffer、Selection、Host|回帰・比較に必要|
|[rust/native/tests/tsf_recovery_control_smoke.py](rust/native/tests/tsf_recovery_control_smoke.py)|管理コマンドの実行の回帰・境界・失敗時の動作を検査する。|Callbacks、RecoveryControlTests、setUpClass、setUp|回帰・比較に必要|
|[rust/native/tests/tsf_recovery_smoke.py](rust/native/tests/tsf_recovery_smoke.py)|`tsf_recovery_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|RecoveryHost、__init__、compare、get_selection|回帰・比較に必要|
|[rust/native/tests/tsf_selection_smoke.py](rust/native/tests/tsf_selection_smoke.py)|`tsf_selection_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|SelectionTests、setUpClass、setUp、current|回帰・比較に必要|
|[rust/native/tests/tsf_server_smoke.py](rust/native/tests/tsf_server_smoke.py)|`tsf_server_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|ServerTests、setUpClass、tearDownClass、setUp|回帰・比較に必要|
|[rust/native/tests/tsf_service_com_smoke.py](rust/native/tests/tsf_service_com_smoke.py)|OS入力サービスの連携の回帰・境界・失敗時の動作を検査する。|Call、ServiceComTests、setUpClass、setUp|回帰・比較に必要|
|[rust/native/tests/tsf_service_state_smoke.py](rust/native/tests/tsf_service_state_smoke.py)|状態と再入・終了条件の回帰・境界・失敗時の動作を検査する。|Point、Stale、State、StateTests|回帰・比較に必要|
|[rust/native/tests/tsf_shutdown_smoke.py](rust/native/tests/tsf_shutdown_smoke.py)|`tsf_shutdown_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Callbacks、ShutdownTests、setUpClass、setUp|回帰・比較に必要|
|[rust/native/tests/tsf_startup_smoke.py](rust/native/tests/tsf_startup_smoke.py)|自動起動と保守コマンドの回帰・境界・失敗時の動作を検査する。|Callbacks、StartupHost、__init__、query|回帰・比較に必要|
|[rust/native/tests/tsf_subscriptions_smoke.py](rust/native/tests/tsf_subscriptions_smoke.py)|`tsf_subscriptions_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Source、__init__、ref、advise|回帰・比較に必要|
|[rust/native/tests/tsf_termination_smoke.py](rust/native/tests/tsf_termination_smoke.py)|`tsf_termination_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Callbacks、Host、__init__、clear|回帰・比較に必要|
|[rust/native/tests/windows_converter_batch_smoke.py](rust/native/tests/windows_converter_batch_smoke.py)|OSウィンドウと表示の回帰・境界・失敗時の動作を検査する。|probe|回帰・比較に必要|
|[rust/native/tests/windows_converter_smoke.py](rust/native/tests/windows_converter_smoke.py)|OSウィンドウと表示の回帰・境界・失敗時の動作を検査する。|main、probe|回帰・比較に必要|
|[rust/native/tests/windows_worker_converter_smoke.py](rust/native/tests/windows_worker_converter_smoke.py)|別プロセスworkerの要求・応答の回帰・境界・失敗時の動作を検査する。|configure、call、reconvert|回帰・比較に必要|
|[rust/native/tests/word_checker_smoke.py](rust/native/tests/word_checker_smoke.py)|`word_checker_smoke` にまとめた処理の回帰・境界・失敗時の動作を検査する。|word、read、handle、type_word|回帰・比較に必要|
|[rust/package-windows.py](rust/package-windows.py)|RustのEXE、両ABI DLL/worker、CRT、ライセンス、管理スクリプトを検査してZIP化する。|main|実装・ビルドに必要|
|[rust/settings-app/Cargo.toml](rust/settings-app/Cargo.toml)|settings-app crateの依存、OS別機能、実行形式を定義する。|"meltype-settings"|実装・ビルドに必要|
|[rust/settings-app/settings-ui.json](rust/settings-app/settings-ui.json)|元版から採取した設定のラベル・説明・型・配置情報。画面のメタデータとして使う。|upstream、fields、enums|実装・ビルドに必要|
|[rust/settings-app/src/lifecycle.rs](rust/settings-app/src/lifecycle.rs)|初回案内、報告、公開Rust版の更新確認と更新スクリプト起動を扱う。|Lifecycle、rust_release、new、check_updates|実装・ビルドに必要|
|[rust/settings-app/src/main.rs](rust/settings-app/src/main.rs)|eguiの設定・辞書・学習・バックアップ・診断画面と、トレイ常駐の入口。|main、App、BTreeEditor、new|実装・ビルドに必要|
|[rust/settings-app/src/metadata.rs](rust/settings-app/src/metadata.rs)|`metadata` にまとめた処理を設定画面・トレイ・Windowsサービスへ接続する。|data、fields、field、label|実装・ビルドに必要|
|[rust/settings-app/src/portable_keyboard.rs](rust/settings-app/src/portable_keyboard.rs)|登録なし入力。低レベルフックでキーをキューへ送り、UI Automationで入力先を検査し、確定をUnicode配送する。|Key、Event、Shared、modifiers|実装・ビルドに必要|
|[rust/settings-app/src/rules.rs](rust/settings-app/src/rules.rs)|`rules` にまとめた処理を設定画面・トレイ・Windowsサービスへ接続する。|editor、nullable_choice|実装・ビルドに必要|
|[rust/settings-app/src/startup.rs](rust/settings-app/src/startup.rs)|現在ユーザーの起動設定と、画面を開かないquery/rebind/disable/stop-trayコマンド。|Spec、RunEntry、current、new|実装・ビルドに必要|
|[rust/settings-app/src/tray.rs](rust/settings-app/src/tray.rs)|トレイ表示と操作を設定画面・トレイ・Windowsサービスへ接続する。|Action、Event、Tray、new|実装・ビルドに必要|
|[rust/settings-app/src/tray_control.rs](rust/settings-app/src/tray_control.rs)|データ保存先ごとの終了イベントとロックで、登録更新前の協調終了を確認する。|Handle、drop、name、StopEvent|実装・ビルドに必要|
|[rust/settings-app/src/widgets.rs](rust/settings-app/src/widgets.rs)|`widgets` にまとめた処理を設定画面・トレイ・Windowsサービスへ接続する。|wheel_step、toggle|実装・ビルドに必要|
|[rust/settings-app/src/windows_services.rs](rust/settings-app/src/windows_services.rs)|OSウィンドウと表示を設定画面・トレイ・Windowsサービスへ接続する。|tray_guid、Sink、Hotkey、choose|実装・ビルドに必要|
|[rust/tip-worker/Cargo.toml](rust/tip-worker/Cargo.toml)|tip-worker crateの依存、OS別機能、実行形式を定義する。|"meltype-tip-worker"|実装・ビルドに必要|
|[rust/tip-worker/src/main.rs](rust/tip-worker/src/main.rs)|Windows TSF用の別プロセスworker入口。フレーム付き通信でCoreセッションを処理する。|serve、main|実装・ビルドに必要|

### 共同編集用システム辞書（27件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[dictionaries/README.md](dictionaries/README.md)|READMEの共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|共同編集する辞書|説明・参照に必要|
|[dictionaries/brands.txt](dictionaries/brands.txt)|ブランド名の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|225有効行|共同編集辞書の原本|
|[dictionaries/candidates.txt](dictionaries/candidates.txt)|読み別の追加候補の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|340有効行|共同編集辞書の原本|
|[dictionaries/contexts.txt](dictionaries/contexts.txt)|同音語と文脈規則の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|147有効行|共同編集辞書の原本|
|[dictionaries/countries.txt](dictionaries/countries.txt)|国名の語の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|278有効行|共同編集辞書の原本|
|[dictionaries/domain-it.txt](dictionaries/domain-it.txt)|IT の分野の辞書の共同編集用テキスト。上流 v1.6.0 の `domain-*.txt` をそのまま同梱する。|364有効行|共同編集辞書の原本|
|[dictionaries/domain-laws.txt](dictionaries/domain-laws.txt)|法令の題名の分野の辞書の共同編集用テキスト。上流 v1.6.0 の `domain-*.txt` をそのまま同梱する。|779有効行|共同編集辞書の原本|
|[dictionaries/domain-medical.txt](dictionaries/domain-medical.txt)|医療の分野の辞書の共同編集用テキスト。上流 v1.6.0 の `domain-*.txt` をそのまま同梱する。|12886有効行|共同編集辞書の原本|
|[dictionaries/domain-names.txt](dictionaries/domain-names.txt)|人名の分野の辞書の共同編集用テキスト。上流 v1.6.0 の `domain-*.txt` をそのまま同梱する。|24737有効行|共同編集辞書の原本|
|[dictionaries/domain-propernouns-sudachi.txt](dictionaries/domain-propernouns-sudachi.txt)|SudachiDict の固有名詞の分野の辞書の共同編集用テキスト。上流 v1.6.0 の `domain-*.txt` をそのまま同梱する。|7011有効行|共同編集辞書の原本|
|[dictionaries/domain-propernouns.txt](dictionaries/domain-propernouns.txt)|固有名詞の分野の辞書の共同編集用テキスト。上流 v1.6.0 の `domain-*.txt` をそのまま同梱する。|4280有効行|共同編集辞書の原本|
|[dictionaries/domain-stations.txt](dictionaries/domain-stations.txt)|駅名の分野の辞書の共同編集用テキスト。上流 v1.6.0 の `domain-*.txt` をそのまま同梱する。|1421有効行|共同編集辞書の原本|
|[dictionaries/domain-words-sudachi.txt](dictionaries/domain-words-sudachi.txt)|SudachiDict の普通名詞の分野の辞書の共同編集用テキスト。上流 v1.6.0 の `domain-*.txt` をそのまま同梱する。|7558有効行|共同編集辞書の原本|
|[dictionaries/emoji-cldr.txt](dictionaries/emoji-cldr.txt)|CLDRの絵文字の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|5166有効行|共同編集辞書の原本|
|[dictionaries/emoji.txt](dictionaries/emoji.txt)|顔文字・絵文字の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|125有効行|共同編集辞書の原本|
|[dictionaries/english-phrases.txt](dictionaries/english-phrases.txt)|英語の定型句の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|16有効行|共同編集辞書の原本|
|[dictionaries/english-readable.txt](dictionaries/english-readable.txt)|読める英字の候補の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|20有効行|共同編集辞書の原本|
|[dictionaries/english-words.txt](dictionaries/english-words.txt)|英語の単語の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|39095有効行|共同編集辞書の原本|
|[dictionaries/english.txt](dictionaries/english.txt)|英語判定用語の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|211有効行|共同編集辞書の原本|
|[dictionaries/japanese.txt](dictionaries/japanese.txt)|日本語として扱う語の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|96有効行|共同編集辞書の原本|
|[dictionaries/loanwords.txt](dictionaries/loanwords.txt)|loanwordsの共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|1314有効行|共同編集辞書の原本|
|[dictionaries/meanings.txt](dictionaries/meanings.txt)|meaningsの共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|29238有効行|共同編集辞書の原本|
|[dictionaries/misspellings.txt](dictionaries/misspellings.txt)|書き間違いの対応の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|169有効行|共同編集辞書の原本|
|[dictionaries/phrases.txt](dictionaries/phrases.txt)|語句と定型文の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|42有効行|共同編集辞書の原本|
|[dictionaries/propernouns.txt](dictionaries/propernouns.txt)|固有名詞の共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|58有効行|共同編集辞書の原本|
|[dictionaries/readings.txt](dictionaries/readings.txt)|readingsの共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|20181有効行|共同編集辞書の原本|
|[dictionaries/translations.txt](dictionaries/translations.txt)|translationsの共同編集用テキスト。システム辞書の材料であり、個人用SQLite辞書とは別。|23074有効行|共同編集辞書の原本|

### 比較・診断・ベンチマークの道具（163件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[tools/Scan-WindowsPackage.ps1](tools/Scan-WindowsPackage.ps1)|OSウィンドウと表示の開発・比較・配布を補助する。|param、Invoke-PackageScan、if、foreach|実装・ビルドに必要|
|[tools/Test-SetupIme.ps1](tools/Test-SetupIme.ps1)|`Test-SetupIme` にまとめた処理の回帰・境界・失敗時の動作を検査する。|try、Test-MeltypeImeRegistered、Install-MeltypeIme、Set-MeltypeImeDeclined|実装・ビルドに必要|
|[tools/Test-WindowsPackageScan.ps1](tools/Test-WindowsPackageScan.ps1)|OSウィンドウと表示の回帰・境界・失敗時の動作を検査する。|try、finally|実装・ビルドに必要|
|[tools/benchmark-composition.py](tools/benchmark-composition.py)|未確定入力と変換状態の開発・比較・配布を補助する。|digest、main|実装・ビルドに必要|
|[tools/benchmark-windows.py](tools/benchmark-windows.py)|未変更C# v1.3.0 NativeAOTとRust Releaseを、同じ辞書・入力・設定で順序を交互に測定する。|command、sha、fixtures、keys|実装・ビルドに必要|
|[tools/check-core-parity.py](tools/check-core-parity.py)|`check-core-parity` にまとめた処理の開発・比較・配布を補助する。|fixtures、keys、main|実装・ビルドに必要|
|[tools/check-dictionaries.mjs](tools/check-dictionaries.mjs)|辞書の検索・更新の開発・比較・配布を補助する。|check-dictionaries.mjs|実装・ビルドに必要|
|[tools/check-direct-parity.py](tools/check-direct-parity.py)|`check-direct-parity` にまとめた処理の開発・比較・配布を補助する。|project、main|実装・ビルドに必要|
|[tools/check-hook-parity.py](tools/check-hook-parity.py)|キーボードフックとキー所有の開発・比較・配布を補助する。|project、main|実装・ビルドに必要|
|[tools/check-rust-windows-package.py](tools/check-rust-windows-package.py)|OSウィンドウと表示の開発・比較・配布を補助する。|pe、unpack、rva、string|実装・ビルドに必要|
|[tools/compare-rust-benchmarks.py](tools/compare-rust-benchmarks.py)|`compare-rust-benchmarks` にまとめた処理の開発・比較・配布を補助する。|sha、main|実装・ビルドに必要|
|[tools/extract-rust-settings-ui.py](tools/extract-rust-settings-ui.py)|設定と候補の実効動作の開発・比較・配布を補助する。|string、attr|実装・ビルドに必要|
|[tools/make-english-words.mjs](tools/make-english-words.mjs)|英語の判定・候補の開発・比較・配布を補助する。|make-english-words.mjs|実装・ビルドに必要|
|[tools/make-headings.py](tools/make-headings.py)|`make-headings` にまとめた処理の開発・比較・配布を補助する。|text_path、snowflake、heading、title|実装・ビルドに必要|
|[tools/make-loanwords.mjs](tools/make-loanwords.mjs)|`make-loanwords` にまとめた処理の開発・比較・配布を補助する。|make-loanwords.mjs|実装・ビルドに必要|
|[tools/make-manifests.mjs](tools/make-manifests.mjs)|`make-manifests` にまとめた処理の開発・比較・配布を補助する。|make-manifests.mjs|実装・ビルドに必要|
|[tools/make-meanings.mjs](tools/make-meanings.mjs)|`make-meanings` にまとめた処理の開発・比較・配布を補助する。|make-meanings.mjs|実装・ビルドに必要|
|[tools/make-readings.mjs](tools/make-readings.mjs)|`make-readings` にまとめた処理の開発・比較・配布を補助する。|make-readings.mjs|実装・ビルドに必要|
|[tools/make-readme-art.py](tools/make-readme-art.py)|`make-readme-art` にまとめた処理の開発・比較・配布を補助する。|Glyphs、__init__、glyph_id、width|実装・ビルドに必要|
|[tools/make-translations.mjs](tools/make-translations.mjs)|`make-translations` にまとめた処理の開発・比較・配布を補助する。|make-translations.mjs|実装・ビルドに必要|
|[tools/mozc-compare-cases.tsv](tools/mozc-compare-cases.tsv)|`mozc-compare-cases` にまとめた処理の開発・比較・配布を補助する。|mozc-compare-cases.tsv|実装・ビルドに必要|
|[tools/parity/BATCHES.md](tools/parity/BATCHES.md)|「有限コーパスの分割比較と再開」の手順・仕様・確認範囲を説明する。|有限コーパスの分割比較と再開、設定の入力例を増やす、同梱読みを全件含める|回帰・比較に必要|
|[tools/parity/batch_compare.py](tools/parity/batch_compare.py)|`batch_compare` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|digest、write_json、main、command|回帰・比較に必要|
|[tools/parity/clauses/Clauses.csproj](tools/parity/clauses/Clauses.csproj)|ClausesのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|回帰・比較に必要|
|[tools/parity/clauses/Program.cs](tools/parity/clauses/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Case、Converter|回帰・比較に必要|
|[tools/parity/clauses/README.md](tools/parity/clauses/README.md)|「文節伸縮と変換器候補の元版比較」の手順・仕様・確認範囲を説明する。|文節伸縮と変換器候補の元版比較、ABI|回帰・比較に必要|
|[tools/parity/clauses/cases.json](tools/parity/clauses/cases.json)|`cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|20項目|回帰・比較に必要|
|[tools/parity/core-data/CoreData.csproj](tools/parity/core-data/CoreData.csproj)|CoreDataのC#参照・テストホストのビルド条件と依存を定義する。|net10.0、Meltype.Core.Tests|回帰・比較に必要|
|[tools/parity/core-data/Program.cs](tools/parity/core-data/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|NullConverter|回帰・比較に必要|
|[tools/parity/core-data/README.md](tools/parity/core-data/README.md)|「共通Core・データ互換の照合」の手順・仕様・確認範囲を説明する。|共通Core・データ互換の照合|回帰・比較に必要|
|[tools/parity/core-data/check.py](tools/parity/core-data/check.py)|`check` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|cases、keys、add、main|回帰・比較に必要|
|[tools/parity/core-data/freeze.py](tools/parity/core-data/freeze.py)|`freeze` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|read|回帰・比較に必要|
|[tools/parity/direct/Direct.csproj](tools/parity/direct/Direct.csproj)|DirectのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|回帰・比較に必要|
|[tools/parity/direct/Program.cs](tools/parity/direct/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Case、Step、NoConverter|回帰・比較に必要|
|[tools/parity/direct/README.md](tools/parity/direct/README.md)|「英数入力中の時刻付き比較」の手順・仕様・確認範囲を説明する。|英数入力中の時刻付き比較|回帰・比較に必要|
|[tools/parity/hook/Hook.csproj](tools/parity/hook/Hook.csproj)|HookのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|回帰・比較に必要|
|[tools/parity/hook/Program.cs](tools/parity/hook/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Case、Learn、Environment|回帰・比較に必要|
|[tools/parity/hook/README.md](tools/parity/hook/README.md)|「旧入力方式の状態・再入力・学習の比較」の手順・仕様・確認範囲を説明する。|旧入力方式の状態・再入力・学習の比較、固定試験と再比較、RustとネイティブABI|回帰・比較に必要|
|[tools/parity/hook/cases.py](tools/parity/hook/cases.py)|`cases` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|typed、press、drain、fixtures|回帰・比較に必要|
|[tools/parity/long/README.md](tools/parity/long/README.md)|「長文・英語境界の全打鍵比較」の手順・仕様・確認範囲を説明する。|長文・英語境界の全打鍵比較|回帰・比較に必要|
|[tools/parity/misspellings/Misspellings.csproj](tools/parity/misspellings/Misspellings.csproj)|MisspellingsのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|回帰・比較に必要|
|[tools/parity/misspellings/Program.cs](tools/parity/misspellings/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Program.cs|回帰・比較に必要|
|[tools/parity/misspellings/README.md](tools/parity/misspellings/README.md)|「元版の書き間違い候補との比較」の手順・仕様・確認範囲を説明する。|元版の書き間違い候補との比較、元版のUnicode分類|回帰・比較に必要|
|[tools/parity/misspellings/cases.json](tools/parity/misspellings/cases.json)|`cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|2281項目|回帰・比較に必要|
|[tools/parity/misspellings/generate-marks.py](tools/parity/misspellings/generate-marks.py)|`generate-marks` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|generate-marks.py|回帰・比較に必要|
|[tools/parity/misspellings/shape-cases.json](tools/parity/misspellings/shape-cases.json)|`shape-cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|112項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/CaseKeys.cs](tools/parity/mozc-lifecycle/CaseKeys.cs)|キーと修飾キーの扱いについて、C#元版の採取・Rustとの照合・証跡の固定を補助する。|CaseKeys、Converter|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/Choice.cs](tools/parity/mozc-lifecycle/Choice.cs)|`Choice` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Choice、Converter|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/ConversionKeys.cs](tools/parity/mozc-lifecycle/ConversionKeys.cs)|キーと修飾キーの扱いについて、C#元版の採取・Rustとの照合・証跡の固定を補助する。|ConversionKeys、Converter|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/DirectTypos.cs](tools/parity/mozc-lifecycle/DirectTypos.cs)|`DirectTypos` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|DirectTypos|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/EngineKeys.cs](tools/parity/mozc-lifecycle/EngineKeys.cs)|キーと修飾キーの扱いについて、C#元版の採取・Rustとの照合・証跡の固定を補助する。|EngineKeys、Learner|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/Filter.cs](tools/parity/mozc-lifecycle/Filter.cs)|`Filter` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Filter、Learner、Host|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/History.cs](tools/parity/mozc-lifecycle/History.cs)|`History` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|History、Converter、Host|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/Language.cs](tools/parity/mozc-lifecycle/Language.cs)|`Language` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Language、Converter、Host|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/MisspellingKeys.cs](tools/parity/mozc-lifecycle/MisspellingKeys.cs)|スペル検査・修正候補について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|MisspellingKeys、Converter|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/MozcLifecycle.csproj](tools/parity/mozc-lifecycle/MozcLifecycle.csproj)|MozcLifecycleのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/NumberKeys.cs](tools/parity/mozc-lifecycle/NumberKeys.cs)|数字・漢数字・単位の候補について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|NumberKeys、Converter|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/PredictionKeys.cs](tools/parity/mozc-lifecycle/PredictionKeys.cs)|予測候補と選択について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|PredictionKeys、Converter|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/Program.cs](tools/parity/mozc-lifecycle/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Program.cs|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/PublicNumberKeys.cs](tools/parity/mozc-lifecycle/PublicNumberKeys.cs)|数字・漢数字・単位の候補について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|PublicNumberKeys、Converter|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/README.md](tools/parity/mozc-lifecycle/README.md)|「Mozcの学習通知・保存・終了の比較」の手順・仕様・確認範囲を説明する。|Mozcの学習通知・保存・終了の比較、通信・保存、学習対象の選別|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/Raw.cs](tools/parity/mozc-lifecycle/Raw.cs)|`Raw` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Raw|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/case-key-cases.json](tools/parity/mozc-lifecycle/case-key-cases.json)|キーと修飾キーの扱いの設定・固定ケース・採取証跡を構造化して保存する。|96項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/cases.json](tools/parity/mozc-lifecycle/cases.json)|`cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|13項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/choice-cases.json](tools/parity/mozc-lifecycle/choice-cases.json)|`choice-cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|16項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/conversion-key-cases.json](tools/parity/mozc-lifecycle/conversion-key-cases.json)|キーと修飾キーの扱いの設定・固定ケース・採取証跡を構造化して保存する。|84項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/direct-typo-cases.json](tools/parity/mozc-lifecycle/direct-typo-cases.json)|`direct-typo-cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|20項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/engine-key-cases.json](tools/parity/mozc-lifecycle/engine-key-cases.json)|キーと修飾キーの扱いの設定・固定ケース・採取証跡を構造化して保存する。|50項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/filter-cases.json](tools/parity/mozc-lifecycle/filter-cases.json)|`filter-cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|22項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/history-cases.json](tools/parity/mozc-lifecycle/history-cases.json)|`history-cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|25項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/language-cases.json](tools/parity/mozc-lifecycle/language-cases.json)|`language-cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|16項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/misspelling-key-cases.json](tools/parity/mozc-lifecycle/misspelling-key-cases.json)|スペル検査・修正候補の設定・固定ケース・採取証跡を構造化して保存する。|104項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/number-key-cases.json](tools/parity/mozc-lifecycle/number-key-cases.json)|数字・漢数字・単位の候補の設定・固定ケース・採取証跡を構造化して保存する。|39項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/prediction-key-cases.json](tools/parity/mozc-lifecycle/prediction-key-cases.json)|予測候補と選択の設定・固定ケース・採取証跡を構造化して保存する。|110項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/public-number-key-cases.json](tools/parity/mozc-lifecycle/public-number-key-cases.json)|数字・漢数字・単位の候補の設定・固定ケース・採取証跡を構造化して保存する。|48項目|回帰・比較に必要|
|[tools/parity/mozc-lifecycle/raw-cases.json](tools/parity/mozc-lifecycle/raw-cases.json)|`raw-cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|36項目|回帰・比較に必要|
|[tools/parity/partition.py](tools/parity/partition.py)|`partition` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|partition.py|回帰・比較に必要|
|[tools/parity/pre1/inventory.json](tools/parity/pre1/inventory.json)|`inventory` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|upstream_tag、upstream_commit、upstream_release、parity_baseline|回帰・比較に必要|
|[tools/parity/reconversion/Program.cs](tools/parity/reconversion/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Case、Converter、Host|回帰・比較に必要|
|[tools/parity/reconversion/README.md](tools/parity/reconversion/README.md)|「選択範囲の再変換と元版の比較」の手順・仕様・確認範囲を説明する。|選択範囲の再変換と元版の比較、Windows TSFへの接続|回帰・比較に必要|
|[tools/parity/reconversion/Reconversion.csproj](tools/parity/reconversion/Reconversion.csproj)|ReconversionのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|回帰・比較に必要|
|[tools/parity/reconversion/cases.json](tools/parity/reconversion/cases.json)|`cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|28項目|回帰・比較に必要|
|[tools/parity/scored/README.md](tools/parity/scored/README.md)|「スコア区切りの元版比較」の手順・仕様・確認範囲を説明する。|スコア区切りの元版比較、固定試験、元版を再ビルドして照合|回帰・比較に必要|
|[tools/parity/settings-apps/Program.cs](tools/parity/settings-apps/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Program.cs|回帰・比較に必要|
|[tools/parity/settings-apps/README.md](tools/parity/settings-apps/README.md)|「アプリ別設定のC#比較」の手順・仕様・確認範囲を説明する。|アプリ別設定のC#比較|回帰・比較に必要|
|[tools/parity/settings-apps/SettingsApps.csproj](tools/parity/settings-apps/SettingsApps.csproj)|SettingsAppsのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|回帰・比較に必要|
|[tools/parity/settings-apps/check.py](tools/parity/settings-apps/check.py)|`check` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|check.py|回帰・比較に必要|
|[tools/parity/settings-candidates/Program.cs](tools/parity/settings-candidates/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Converter|回帰・比較に必要|
|[tools/parity/settings-candidates/README.md](tools/parity/settings-candidates/README.md)|「設定の組合せと候補源の互換試験」の手順・仕様・確認範囲を説明する。|設定の組合せと候補源の互換試験、2026-10-21: 1.3.0向け固定候補の更新|回帰・比較に必要|
|[tools/parity/settings-candidates/SettingsCandidates.csproj](tools/parity/settings-candidates/SettingsCandidates.csproj)|SettingsCandidatesのC#参照・テストホストのビルド条件と依存を定義する。|net10.0、Meltype.Core.Tests|回帰・比較に必要|
|[tools/parity/settings-candidates/check.py](tools/parity/settings-candidates/check.py)|`check` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|keys、cases、add、projection|回帰・比較に必要|
|[tools/parity/settings-candidates/coverage.json](tools/parity/settings-candidates/coverage.json)|`coverage` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|coverage.json|回帰・比較に必要|
|[tools/parity/settings-candidates/exhaustive-coverage.json](tools/parity/settings-candidates/exhaustive-coverage.json)|`exhaustive-coverage` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|settings、declared_cartesian_configurations、offset、next_offset|回帰・比較に必要|
|[tools/parity/settings-candidates/exhaustive.py](tools/parity/settings-candidates/exhaustive.py)|`exhaustive` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|exhaustive.py|回帰・比較に必要|
|[tools/parity/settings-candidates/expanded-coverage.json](tools/parity/settings-candidates/expanded-coverage.json)|`expanded-coverage` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|基準:45e14f8655、domains_sha256、settings_order、settings|回帰・比較に必要|
|[tools/parity/settings-candidates/freeze.py](tools/parity/settings-candidates/freeze.py)|`freeze` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|freeze.py|回帰・比較に必要|
|[tools/parity/settings-candidates/matrix.py](tools/parity/settings-candidates/matrix.py)|`matrix` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|domains、covering_rows、generate|回帰・比較に必要|
|[tools/parity/settings-candidates/replay.py](tools/parity/settings-candidates/replay.py)|`replay` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|replay.py|回帰・比較に必要|
|[tools/parity/settings-candidates/sources.py](tools/parity/settings-candidates/sources.py)|`sources` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|generate、add、dictionary_cases|回帰・比較に必要|
|[tools/parity/settings-candidates/verification.json](tools/parity/settings-candidates/verification.json)|`verification` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|基準:45e14f8655、date、platform、rust|回帰・比較に必要|
|[tools/parity/storage/verification.json](tools/parity/storage/verification.json)|`verification` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|基準:45e14f8655、date、baseline、rust|回帰・比較に必要|
|[tools/parity/summarize_shards.py](tools/parity/summarize_shards.py)|`summarize_shards` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|digest、summarize|回帰・比較に必要|
|[tools/parity/test_batch_compare.py](tools/parity/test_batch_compare.py)|`test_batch_compare` にまとめた処理の回帰・境界・失敗時の動作を検査する。|BatchEvidence、run_case、subprocess、invoke|回帰・比較に必要|
|[tools/parity/ui/README.md](tools/parity/ui/README.md)|「画面の検証範囲」の手順・仕様・確認範囲を説明する。|画面の検証範囲、小さな設定表示差の修正（2026-10-20）|回帰・比較に必要|
|[tools/parity/ui/capture_settings.py](tools/parity/ui/capture_settings.py)|設定と候補の実効動作について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|chunk|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-fst-sqlite/backup.png](tools/parity/ui/evidence/windows-fst-sqlite/backup.png)|OSパッケージ・画面で使う `backup` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-fst-sqlite/dictionary.png](tools/parity/ui/evidence/windows-fst-sqlite/dictionary.png)|OSパッケージ・画面で使う `dictionary` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-fst-sqlite/input-test.png](tools/parity/ui/evidence/windows-fst-sqlite/input-test.png)|OSパッケージ・画面で使う `input-test` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-fst-sqlite/learning.png](tools/parity/ui/evidence/windows-fst-sqlite/learning.png)|OSパッケージ・画面で使う `learning` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-fst-sqlite/report.json](tools/parity/ui/evidence/windows-fst-sqlite/report.json)|`report` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|binary_sha256、pages、all_screens_equivalent|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-fst-sqlite/settings.png](tools/parity/ui/evidence/windows-fst-sqlite/settings.png)|OSパッケージ・画面で使う `settings` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-fst-sqlite/snippets.png](tools/parity/ui/evidence/windows-fst-sqlite/snippets.png)|OSパッケージ・画面で使う `snippets` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-small-fixes-20261020/backup.png](tools/parity/ui/evidence/windows-small-fixes-20261020/backup.png)|OSパッケージ・画面で使う `backup` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-small-fixes-20261020/dictionary.png](tools/parity/ui/evidence/windows-small-fixes-20261020/dictionary.png)|OSパッケージ・画面で使う `dictionary` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-small-fixes-20261020/input-test.png](tools/parity/ui/evidence/windows-small-fixes-20261020/input-test.png)|OSパッケージ・画面で使う `input-test` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-small-fixes-20261020/learning.png](tools/parity/ui/evidence/windows-small-fixes-20261020/learning.png)|OSパッケージ・画面で使う `learning` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-small-fixes-20261020/long-help.png](tools/parity/ui/evidence/windows-small-fixes-20261020/long-help.png)|OSパッケージ・画面で使う `long-help` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-small-fixes-20261020/report.json](tools/parity/ui/evidence/windows-small-fixes-20261020/report.json)|`report` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|binary_sha256、pages、all_screens_equivalent、source_sha256|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-small-fixes-20261020/settings.png](tools/parity/ui/evidence/windows-small-fixes-20261020/settings.png)|OSパッケージ・画面で使う `settings` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/ui/evidence/windows-small-fixes-20261020/snippets.png](tools/parity/ui/evidence/windows-small-fixes-20261020/snippets.png)|OSパッケージ・画面で使う `snippets` の画像リソース。|画像リソース|回帰・比較に必要|
|[tools/parity/v130/Program.cs](tools/parity/v130/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Program.cs|回帰・比較に必要|
|[tools/parity/v130/README.md](tools/parity/v130/README.md)|「Meltype 1.3.0の参照比較」の手順・仕様・確認範囲を説明する。|Meltype 1.3.0の参照比較|回帰・比較に必要|
|[tools/parity/v130/Reference.csproj](tools/parity/v130/Reference.csproj)|ReferenceのC#参照・テストホストのビルド条件と依存を定義する。|net10.0、Meltype.Core.Tests|回帰・比較に必要|
|[tools/parity/v130/SessionReference.cs](tools/parity/v130/SessionReference.cs)|入力セッションの状態遷移について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|SessionReference、Converter|回帰・比較に必要|
|[tools/parity/v130/known-gaps.json](tools/parity/v130/known-gaps.json)|`known-gaps` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|cases:2、description、cases|回帰・比較に必要|
|[tools/parity/v130/regenerate.py](tools/parity/v130/regenerate.py)|`regenerate` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|run、read、write、main|回帰・比較に必要|
|[tools/parity/windows-converter/Program.cs](tools/parity/windows-converter/Program.cs)|`Program` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Program、Case|回帰・比較に必要|
|[tools/parity/windows-converter/README.md](tools/parity/windows-converter/README.md)|「Windows変換器のRust移植と元版の比較」の手順・仕様・確認範囲を説明する。|Windows変換器のRust移植と元版の比較、workerへの接続、このPCでの検証（2026-10-19）|回帰・比較に必要|
|[tools/parity/windows-converter/Synthetic.cs](tools/parity/windows-converter/Synthetic.cs)|`Synthetic` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|Synthetic|回帰・比較に必要|
|[tools/parity/windows-converter/WindowsConverter.csproj](tools/parity/windows-converter/WindowsConverter.csproj)|WindowsConverterのC#参照・テストホストのビルド条件と依存を定義する。|net10.0-windows|回帰・比較に必要|
|[tools/parity/windows-converter/cases.json](tools/parity/windows-converter/cases.json)|`cases` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|26項目|回帰・比較に必要|
|[tools/parity/windows-converter/corpus.py](tools/parity/windows-converter/corpus.py)|`corpus` にまとめた処理について、C#元版の採取・Rustとの照合・証跡の固定を補助する。|corpus.py|回帰・比較に必要|
|[tools/parity/windows-converter/full-corpus-verification.json](tools/parity/windows-converter/full-corpus-verification.json)|`full-corpus-verification` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|schema_version、date、rust_source_commit、upstream_baseline|回帰・比較に必要|
|[tools/parity/windows-converter/synthetic.json](tools/parity/windows-converter/synthetic.json)|`synthetic` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|27項目|回帰・比較に必要|
|[tools/parity/windows-package/verification.json](tools/parity/windows-package/verification.json)|`verification` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|passed、errors、binaries、system_data|回帰・比較に必要|
|[tools/performance/Benchmark.csproj](tools/performance/Benchmark.csproj)|BenchmarkのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|実装・ビルドに必要|
|[tools/performance/Program.cs](tools/performance/Program.cs)|`Program` にまとめた処理の開発・比較・配布を補助する。|Bench、Converter|実装・ビルドに必要|
|[tools/performance/README.md](tools/performance/README.md)|「README」の手順・仕様・確認範囲を説明する。|README.md|説明・参照に必要|
|[tools/performance/windows/Compare.csproj](tools/performance/windows/Compare.csproj)|CompareのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|実装・ビルドに必要|
|[tools/performance/windows/Program.cs](tools/performance/windows/Program.cs)|`Program` にまとめた処理の開発・比較・配布を補助する。|Case、Row、Bench、NullConverter|実装・ビルドに必要|
|[tools/performance/windows/README.md](tools/performance/windows/README.md)|「Windows・共通コアの C# / Rust 比較」の手順・仕様・確認範囲を説明する。|Windows・共通コアの C# / Rust 比較、CPU・RAMの測定、速度を測らず各キーの差を調べる|説明・参照に必要|
|[tools/performance/windows/cleanup-20261020.json](tools/performance/windows/cleanup-20261020.json)|`cleanup-20261020` にまとめた処理の設定・固定ケース・採取証跡を構造化して保存する。|generated_directories_deleted、generated_files_deleted、generated_bytes_deleted、tracked_old_reports_deleted|実装・ビルドに必要|
|[tools/performance/windows/resources.py](tools/performance/windows/resources.py)|Windows APIで測定プロセスのCPU時間とWorking Set/Private Bytesを約50ms間隔で観測する。|MemoryStatus、ProcessMemory、measure_process、system_memory|実装・ビルドに必要|
|[tools/performance/windows/results/20261021-boxplots/README.md](tools/performance/windows/results/20261021-boxplots/README.md)|「Windows共通Coreの再測定（2026-10-21）」の手順・仕様・確認範囲を説明する。|Windows共通Coreの再測定（2026-10-21）、条件と箱ひげ図の読み方、全入力の中央値|説明・参照に必要|
|[tools/performance/windows/results/20261021-boxplots/boxplot-statistics.json](tools/performance/windows/results/20261021-boxplots/boxplot-statistics.json)|中央値・四分位・ひげ・全外れ値と単位。観測値を捨てず保存。|speed、resources、resource_units|最新測定の証跡|
|[tools/performance/windows/results/20261021-boxplots/cpu.png](tools/performance/windows/results/20261021-boxplots/cpu.png)|CPU時間・経過時間・CPU使用率の箱ひげ図（各版10観測）。|画像リソース|最新測定の証跡|
|[tools/performance/windows/results/20261021-boxplots/ram.png](tools/performance/windows/results/20261021-boxplots/ram.png)|常駐/専用メモリ・物理RAM比の箱ひげ図（各版10観測）。|画像リソース|最新測定の証跡|
|[tools/performance/windows/results/20261021-boxplots/resources.csv](tools/performance/windows/results/20261021-boxplots/resources.csv)|本測定の全観測を行単位で保存する表。図から数値を復元せず、この値を使用する。|version,round,wall_seconds,cpu_seconds,cpu|最新測定の証跡|
|[tools/performance/windows/results/20261021-boxplots/speed-128-256.png](tools/performance/windows/results/20261021-boxplots/speed-128-256.png)|128/256文字8入力の速度箱ひげ図（各版各入力30試行）。|画像リソース|最新測定の証跡|
|[tools/performance/windows/results/20261021-boxplots/speed-32-64.png](tools/performance/windows/results/20261021-boxplots/speed-32-64.png)|32/64文字8入力の速度箱ひげ図（各版各入力30試行）。|画像リソース|最新測定の証跡|
|[tools/performance/windows/results/20261021-boxplots/speed-short-edit.png](tools/performance/windows/results/20261021-boxplots/speed-short-edit.png)|短文・編集5入力の速度箱ひげ図（各版各入力30試行）。|画像リソース|最新測定の証跡|
|[tools/performance/windows/results/20261021-boxplots/summary.json](tools/performance/windows/results/20261021-boxplots/summary.json)|測定条件・ソース/バイナリのハッシュと全試行。図の再生成入力。|基準:6310c44b72、measured_at、machine、cpu|最新測定の証跡|
|[tools/performance/windows/results/20261021-boxplots/timings.csv](tools/performance/windows/results/20261021-boxplots/timings.csv)|本測定の全観測を行単位で保存する表。図から数値を復元せず、この値を使用する。|case,version,round,sample,total_ms|最新測定の証跡|
|[tools/plot-windows-benchmark.py](tools/plot-windows-benchmark.py)|全試行から中央値・IQR・全外れ値付き箱ひげ図とCSV/集計を作る。速度・CPU・RAMの計5図。|distribution、boxes、finish、main|実装・ビルドに必要|
|[tools/report-form/Code.gs](tools/report-form/Code.gs)|`Code` にまとめた処理の開発・比較・配布を補助する。|Code.gs|実装・ビルドに必要|
|[tools/report-form/README.md](tools/report-form/README.md)|「不具合報告フォーム (Google フォーム → GitHub の Issue)」の手順・仕様・確認範囲を説明する。|不具合報告フォーム (Google フォーム → GitHub の Issue)、1. フォームを作る、2. 回答をスプレッドシートにつなぐ|説明・参照に必要|
|[tools/test-mac-boundaries.py](tools/test-mac-boundaries.py)|`test-mac-boundaries` にまとめた処理の開発・比較・配布を補助する。|main|実装・ビルドに必要|
|[tools/test-mac-boundaries.swift](tools/test-mac-boundaries.swift)|`test-mac-boundaries` にまとめた処理の開発・比較・配布を補助する。|MeltypeConverter、clauses、candidates、BoundaryTests|実装・ビルドに必要|
|[tools/test-mac-events.py](tools/test-mac-events.py)|イベント配送と寿命の開発・比較・配布を補助する。|main|実装・ビルドに必要|
|[tools/test-mac-events.swift](tools/test-mac-events.swift)|イベント配送と寿命の開発・比較・配布を補助する。|EventClient、text、insertText、setMarkedText|実装・ビルドに必要|
|[tools/test-native-boundaries.py](tools/test-native-boundaries.py)|ネイティブ境界と型幅の開発・比較・配布を補助する。|NativeBoundaryTests、setUpClass、setUp、key|実装・ビルドに必要|
|[tools/test-portable-input.py](tools/test-portable-input.py)|入力イベント・入力先の開発・比較・配布を補助する。|main、WindowClass、Keyboard、Payload|実装・ビルドに必要|
|[tools/test_rust_windows_package.py](tools/test_rust_windows_package.py)|OSウィンドウと表示の回帰・境界・失敗時の動作を検査する。|image、put、string、PackageTests|実装・ビルドに必要|

### 元版ネイティブホストと変換器（27件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[native/mozc/BUILD.fragment](native/mozc/BUILD.fragment)|nativeのビルド・導入・OS登録に使う `BUILD.fragment` の宣言データ。|BUILD.fragment|OS連携・互換経路|
|[native/mozc/Build-MozcHelper.ps1](native/mozc/Build-MozcHelper.ps1)|`Build-MozcHelper` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|param、Invoke-Native、if、try|OS連携・互換経路|
|[native/mozc/MOZC_COMMIT](native/mozc/MOZC_COMMIT)|`MOZC_COMMIT` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|MOZC_COMMIT|OS連携・互換経路|
|[native/mozc/README.md](native/mozc/README.md)|「Mozc の変換ヘルパー」の手順・仕様・確認範囲を説明する。|Mozc の変換ヘルパー、ビルド|説明・参照に必要|
|[native/mozc/build-mozc-helper.sh](native/mozc/build-mozc-helper.sh)|`build-mozc-helper` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|build-mozc-helper.sh|OS連携・互換経路|
|[native/mozc/meltype_mozc_helper.cc](native/mozc/meltype_mozc_helper.cc)|`meltype_mozc_helper` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|Helper|OS連携・互換経路|
|[native/tip/CandidateWindow.cpp](native/tip/CandidateWindow.cpp)|OSウィンドウと表示のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|Palette、CandidateWindow|OS連携・互換経路|
|[native/tip/CandidateWindow.h](native/tip/CandidateWindow.h)|OSウィンドウと表示のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|CandidateView、CandidateWindow、Impl|OS連携・互換経路|
|[native/tip/DisplayAttributes.cpp](native/tip/DisplayAttributes.cpp)|表示情報・候補の描画のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|AttributeSpec、DisplayAttributeInfo、DisplayAttributeEnum|OS連携・互換経路|
|[native/tip/DisplayAttributes.h](native/tip/DisplayAttributes.h)|表示情報・候補の描画のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|DisplayAttributes.h|OS連携・互換経路|
|[native/tip/Dll.cpp](native/tip/Dll.cpp)|`Dll` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|ClassFactory|OS連携・互換経路|
|[native/tip/Globals.h](native/tip/Globals.h)|`Globals` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|JsonValue、RustKeyStale、RustKeyPassed、RustCoreStateCallbacks|OS連携・互換経路|
|[native/tip/Json.cpp](native/tip/Json.cpp)|JSON形式の要求・応答のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|JsonBuilder、Frame、Parser|OS連携・互換経路|
|[native/tip/Json.h](native/tip/Json.h)|JSON形式の要求・応答のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|JsonValue、class|OS連携・互換経路|
|[native/tip/LangBar.cpp](native/tip/LangBar.cpp)|`LangBar` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|LangBar.cpp|OS連携・互換経路|
|[native/tip/LangBar.h](native/tip/LangBar.h)|`LangBar` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|TextService、LangBarButton|OS連携・互換経路|
|[native/tip/MeltypeTip.def](native/tip/MeltypeTip.def)|nativeのビルド・導入・OS登録に使う `MeltypeTip.def` の宣言データ。|MeltypeTip.def|OS連携・互換経路|
|[native/tip/MeltypeTip.rc](native/tip/MeltypeTip.rc)|nativeのビルド・導入・OS登録に使う `MeltypeTip.rc` の宣言データ。|MeltypeTip.rc|OS連携・互換経路|
|[native/tip/Pipe.cpp](native/tip/Pipe.cpp)|`Pipe` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|Pipe.cpp|OS連携・互換経路|
|[native/tip/Pipe.h](native/tip/Pipe.h)|`Pipe` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|PipeClient|OS連携・互換経路|
|[native/tip/Register-Tip.ps1](native/tip/Register-Tip.ps1)|OSの入力ソース登録のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|param、if、Write-Log、Invoke-Regsvr|OS連携・互換経路|
|[native/tip/RustPipe.cpp](native/tip/RustPipe.cpp)|`RustPipe` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|RustApi、EditAction、SurroundingReceiver|OS連携・互換経路|
|[native/tip/RustServiceState.h](native/tip/RustServiceState.h)|状態と再入・終了条件のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|RustServiceState、RustServiceStateStorage|OS連携・互換経路|
|[native/tip/TextService.cpp](native/tip/TextService.cpp)|文字列と編集範囲のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|EditSession|OS連携・互換経路|
|[native/tip/TextService.h](native/tip/TextService.h)|文字列と編集範囲のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|LangBarButton、TextService、T、Reinjected|OS連携・互換経路|
|[native/tip/build.ps1](native/tip/build.ps1)|`build` にまとめた処理のネイティブホスト・変換器連携。元版と薄いSDK試験の経路で使用。|param、if、Get-Sha256、foreach|OS連携・互換経路|
|[native/tip/meltype.ico](native/tip/meltype.ico)|OSパッケージ・画面で使う `meltype` の画像リソース。|画像リソース|OS連携・互換経路|

### 元版C#の製品・共通Core・比較対象（137件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[src/Meltype.Core.Tests/AltTapTests.cs](src/Meltype.Core.Tests/AltTapTests.cs)|`AltTapTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|AltTapTests|元版・比較に必要|
|[src/Meltype.Core.Tests/BackupTests.cs](src/Meltype.Core.Tests/BackupTests.cs)|バックアップの検査・復元の元版C#実装。既存の製品経路と独立した比較基準として残す。|BackupTests|元版・比較に必要|
|[src/Meltype.Core.Tests/CliTests.cs](src/Meltype.Core.Tests/CliTests.cs)|`CliTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|CliTests|元版・比較に必要|
|[src/Meltype.Core.Tests/CompositionTests.cs](src/Meltype.Core.Tests/CompositionTests.cs)|未確定入力と変換状態の元版C#実装。既存の製品経路と独立した比較基準として残す。|CompositionTests、FakeConverter、FakeHost、Keyboard|元版・比較に必要|
|[src/Meltype.Core.Tests/ContextBoundaryTests.cs](src/Meltype.Core.Tests/ContextBoundaryTests.cs)|周辺文・文脈の判定の元版C#実装。既存の製品経路と独立した比較基準として残す。|ContextBoundaryTests|元版・比較に必要|
|[src/Meltype.Core.Tests/CorrectionTests.cs](src/Meltype.Core.Tests/CorrectionTests.cs)|`CorrectionTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|CorrectionTests|元版・比較に必要|
|[src/Meltype.Core.Tests/DetectionTests.cs](src/Meltype.Core.Tests/DetectionTests.cs)|日英・入力対象の判定の元版C#実装。既存の製品経路と独立した比較基準として残す。|DetectionTests|元版・比較に必要|
|[src/Meltype.Core.Tests/FeedbackTests.cs](src/Meltype.Core.Tests/FeedbackTests.cs)|`FeedbackTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|FeedbackTests、FewWordsChecker、KanaInputTests、MisspellingTests|元版・比較に必要|
|[src/Meltype.Core.Tests/InputCoverageTests.cs](src/Meltype.Core.Tests/InputCoverageTests.cs)|入力イベント・入力先の元版C#実装。既存の製品経路と独立した比較基準として残す。|InputCoverageTests|元版・比較に必要|
|[src/Meltype.Core.Tests/InputSuspensionTests.cs](src/Meltype.Core.Tests/InputSuspensionTests.cs)|入力イベント・入力先の元版C#実装。既存の製品経路と独立した比較基準として残す。|InputSuspensionTests|元版・比較に必要|
|[src/Meltype.Core.Tests/Jht.cs](src/Meltype.Core.Tests/Jht.cs)|`Jht` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|Jht、Sound|元版・比較に必要|
|[src/Meltype.Core.Tests/KanaQualityTests.cs](src/Meltype.Core.Tests/KanaQualityTests.cs)|かな・JISかなの変換の元版C#実装。既存の製品経路と独立した比較基準として残す。|KanaQualityTests|元版・比較に必要|
|[src/Meltype.Core.Tests/Meltype.Core.Tests.csproj](src/Meltype.Core.Tests/Meltype.Core.Tests.csproj)|Meltype.Core.TestsのC#参照・テストホストのビルド条件と依存を定義する。|net10.0|元版・比較に必要|
|[src/Meltype.Core.Tests/MemoryPersistenceTests.cs](src/Meltype.Core.Tests/MemoryPersistenceTests.cs)|`MemoryPersistenceTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|MemoryPersistenceTests|元版・比較に必要|
|[src/Meltype.Core.Tests/MozcConverterTests.cs](src/Meltype.Core.Tests/MozcConverterTests.cs)|`MozcConverterTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|MozcConverterTests|元版・比較に必要|
|[src/Meltype.Core.Tests/NativeBoundaryTests.cs](src/Meltype.Core.Tests/NativeBoundaryTests.cs)|ネイティブ境界と型幅の元版C#実装。既存の製品経路と独立した比較基準として残す。|NativeBoundaryTests、NativeSession|元版・比較に必要|
|[src/Meltype.Core.Tests/PastePolicyTests.cs](src/Meltype.Core.Tests/PastePolicyTests.cs)|`PastePolicyTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|PastePolicyTests|元版・比較に必要|
|[src/Meltype.Core.Tests/Performance.cs](src/Meltype.Core.Tests/Performance.cs)|`Performance` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|Performance、Case、Run、Distribution|元版・比較に必要|
|[src/Meltype.Core.Tests/ProfileTests.cs](src/Meltype.Core.Tests/ProfileTests.cs)|設定プロファイルの適用の元版C#実装。既存の製品経路と独立した比較基準として残す。|ProfileTests|元版・比較に必要|
|[src/Meltype.Core.Tests/Program.cs](src/Meltype.Core.Tests/Program.cs)|`Program` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|Program|元版・比較に必要|
|[src/Meltype.Core.Tests/ProtectionPropertyTests.cs](src/Meltype.Core.Tests/ProtectionPropertyTests.cs)|`ProtectionPropertyTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ProtectionPropertyTests|元版・比較に必要|
|[src/Meltype.Core.Tests/ProtectionTests.cs](src/Meltype.Core.Tests/ProtectionTests.cs)|`ProtectionTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ProtectionTests|元版・比較に必要|
|[src/Meltype.Core.Tests/QualityTests.cs](src/Meltype.Core.Tests/QualityTests.cs)|入力判定の品質評価の元版C#実装。既存の製品経路と独立した比較基準として残す。|Quality、TypingCase、Case、Result|元版・比較に必要|
|[src/Meltype.Core.Tests/Repro.cs](src/Meltype.Core.Tests/Repro.cs)|`Repro` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|Repro、Checks、Henkan、WithoutLearning|元版・比較に必要|
|[src/Meltype.Core.Tests/ScoredSegmentationTests.cs](src/Meltype.Core.Tests/ScoredSegmentationTests.cs)|スコア付きの区切り判定の元版C#実装。既存の製品経路と独立した比較基準として残す。|ScoredSegmentationTests|元版・比較に必要|
|[src/Meltype.Core.Tests/SessionFacadeTests.cs](src/Meltype.Core.Tests/SessionFacadeTests.cs)|入力セッションの状態遷移の元版C#実装。既存の製品経路と独立した比較基準として残す。|SessionFacadeTests|元版・比較に必要|
|[src/Meltype.Core.Tests/SessionTests.cs](src/Meltype.Core.Tests/SessionTests.cs)|入力セッションの状態遷移の元版C#実装。既存の製品経路と独立した比較基準として残す。|SessionTests|元版・比較に必要|
|[src/Meltype.Core.Tests/SettingsJsonTests.cs](src/Meltype.Core.Tests/SettingsJsonTests.cs)|設定と候補の実効動作の元版C#実装。既存の製品経路と独立した比較基準として残す。|SettingsJsonTests|元版・比較に必要|
|[src/Meltype.Core.Tests/SnippetTests.cs](src/Meltype.Core.Tests/SnippetTests.cs)|`SnippetTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|SnippetTests|元版・比較に必要|
|[src/Meltype.Core.Tests/TestFramework.cs](src/Meltype.Core.Tests/TestFramework.cs)|`TestFramework` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|TestAttribute、AssertionException、Assert、TestHost|元版・比較に必要|
|[src/Meltype.Core.Tests/TestSupport.cs](src/Meltype.Core.Tests/TestSupport.cs)|`TestSupport` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|TestSupport、FakeEnvironment、Typist|元版・比較に必要|
|[src/Meltype.Core.Tests/UserDictionaryFileTests.cs](src/Meltype.Core.Tests/UserDictionaryFileTests.cs)|辞書の検索・更新の元版C#実装。既存の製品経路と独立した比較基準として残す。|UserDictionaryFileTests|元版・比較に必要|
|[src/Meltype.Core.Tests/UserModelTests.cs](src/Meltype.Core.Tests/UserModelTests.cs)|`UserModelTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|UserModelTests|元版・比較に必要|
|[src/Meltype.Core.Tests/WordJudgeTests.cs](src/Meltype.Core.Tests/WordJudgeTests.cs)|`WordJudgeTests` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|WordJudgeTests|元版・比較に必要|
|[src/Meltype.Core/Composition/CandidateDictionary.cs](src/Meltype.Core/Composition/CandidateDictionary.cs)|辞書の検索・更新の元版C#実装。既存の製品経路と独立した比較基準として残す。|CandidateDictionary|元版・比較に必要|
|[src/Meltype.Core/Composition/CaptureGate.cs](src/Meltype.Core/Composition/CaptureGate.cs)|`CaptureGate` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|struct、CaptureGate|元版・比較に必要|
|[src/Meltype.Core/Composition/CompositionController.cs](src/Meltype.Core/Composition/CompositionController.cs)|未確定入力と変換状態の元版C#実装。既存の製品経路と独立した比較基準として残す。|CompositionView、ICompositionHost、ReconversionSelection、CompositionOptions|元版・比較に必要|
|[src/Meltype.Core/Composition/CompositionDetector.cs](src/Meltype.Core/Composition/CompositionDetector.cs)|未確定入力と変換状態の元版C#実装。既存の製品経路と独立した比較基準として残す。|CompositionDetector|元版・比較に必要|
|[src/Meltype.Core/Composition/CompositionText.cs](src/Meltype.Core/Composition/CompositionText.cs)|未確定入力と変換状態の元版C#実装。既存の製品経路と独立した比較基準として残す。|DisplayMode、LetterCase、struct、CompositionText|元版・比較に必要|
|[src/Meltype.Core/Composition/ConversionPreferences.cs](src/Meltype.Core/Composition/ConversionPreferences.cs)|`ConversionPreferences` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ContextRules、ConversionHistory、Entry|元版・比較に必要|
|[src/Meltype.Core/Composition/Corrections.cs](src/Meltype.Core/Composition/Corrections.cs)|`Corrections` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|Correction、CorrectionLog|元版・比較に必要|
|[src/Meltype.Core/Composition/EnglishPhraseSpacing.cs](src/Meltype.Core/Composition/EnglishPhraseSpacing.cs)|英語の判定・候補の元版C#実装。既存の製品経路と独立した比較基準として残す。|EnglishPhraseSpacing|元版・比較に必要|
|[src/Meltype.Core/Composition/HybridConverter.cs](src/Meltype.Core/Composition/HybridConverter.cs)|`HybridConverter` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|HybridConverter|元版・比較に必要|
|[src/Meltype.Core/Composition/KanjiConversion.cs](src/Meltype.Core/Composition/KanjiConversion.cs)|`KanjiConversion` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ConversionClause、IKanjiConverter、ILearningConverter|元版・比較に必要|
|[src/Meltype.Core/Composition/LanguageMemory.cs](src/Meltype.Core/Composition/LanguageMemory.cs)|`LanguageMemory` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|LanguageMemory、Entry、Learned|元版・比較に必要|
|[src/Meltype.Core/Composition/MeaningDictionary.cs](src/Meltype.Core/Composition/MeaningDictionary.cs)|辞書の検索・更新の元版C#実装。既存の製品経路と独立した比較基準として残す。|MeaningDictionary|元版・比較に必要|
|[src/Meltype.Core/Composition/MeltypeSession.cs](src/Meltype.Core/Composition/MeltypeSession.cs)|入力セッションの状態遷移の元版C#実装。既存の製品経路と独立した比較基準として残す。|struct、SessionResult、MeltypeSession、Host|元版・比較に必要|
|[src/Meltype.Core/Composition/MisspellingDictionary.cs](src/Meltype.Core/Composition/MisspellingDictionary.cs)|スペル検査・修正候補の元版C#実装。既存の製品経路と独立した比較基準として残す。|struct、MisspellingDictionary|元版・比較に必要|
|[src/Meltype.Core/Composition/MozcConverter.cs](src/Meltype.Core/Composition/MozcConverter.cs)|`MozcConverter` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|MozcConverter、in、struct|元版・比較に必要|
|[src/Meltype.Core/Composition/Predictor.cs](src/Meltype.Core/Composition/Predictor.cs)|予測候補と選択の元版C#実装。既存の製品経路と独立した比較基準として残す。|PhraseHistory、Predictor|元版・比較に必要|
|[src/Meltype.Core/Composition/ProtectedSpan.cs](src/Meltype.Core/Composition/ProtectedSpan.cs)|`ProtectedSpan` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ProtectedKind、struct、ProtectedSpanScanner|元版・比較に必要|
|[src/Meltype.Core/Composition/RomajiTypoCorrector.cs](src/Meltype.Core/Composition/RomajiTypoCorrector.cs)|ローマ字表とかな変換の元版C#実装。既存の製品経路と独立した比較基準として残す。|struct、RomajiTypoCorrector|元版・比較に必要|
|[src/Meltype.Core/Composition/ScoredSegmentation.cs](src/Meltype.Core/Composition/ScoredSegmentation.cs)|スコア付きの区切り判定の元版C#実装。既存の製品経路と独立した比較基準として残す。|CompositionDetector、ScoredPath|元版・比較に必要|
|[src/Meltype.Core/Composition/SigilWord.cs](src/Meltype.Core/Composition/SigilWord.cs)|`SigilWord` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|SigilWord|元版・比較に必要|
|[src/Meltype.Core/Composition/Snippets.cs](src/Meltype.Core/Composition/Snippets.cs)|`Snippets` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|SnippetStore、SnippetTrigger|元版・比較に必要|
|[src/Meltype.Core/Composition/TranslationDictionary.cs](src/Meltype.Core/Composition/TranslationDictionary.cs)|辞書の検索・更新の元版C#実装。既存の製品経路と独立した比較基準として残す。|TranslationDictionary、TranslationHistory|元版・比較に必要|
|[src/Meltype.Core/Composition/UserDictionary.cs](src/Meltype.Core/Composition/UserDictionary.cs)|辞書の検索・更新の元版C#実装。既存の製品経路と独立した比較基準として残す。|UserWord、UserDictionary|元版・比較に必要|
|[src/Meltype.Core/Composition/UserDictionaryFile.cs](src/Meltype.Core/Composition/UserDictionaryFile.cs)|辞書の検索・更新の元版C#実装。既存の製品経路と独立した比較基準として残す。|UserDictionaryFile、ImportResult|元版・比較に必要|
|[src/Meltype.Core/Composition/WordJudge.cs](src/Meltype.Core/Composition/WordJudge.cs)|`WordJudge` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|WordJudge、WordChange|元版・比較に必要|
|[src/Meltype.Core/Config/AppPaths.cs](src/Meltype.Core/Config/AppPaths.cs)|`AppPaths` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|AppPaths|元版・比較に必要|
|[src/Meltype.Core/Config/Backup.cs](src/Meltype.Core/Config/Backup.cs)|バックアップの検査・復元の元版C#実装。既存の製品経路と独立した比較基準として残す。|Backup|元版・比較に必要|
|[src/Meltype.Core/Config/CoreJsonContext.cs](src/Meltype.Core/Config/CoreJsonContext.cs)|周辺文・文脈の判定の元版C#実装。既存の製品経路と独立した比較基準として残す。|SettingsJsonContext、LearningJsonContext、ConversionJsonContext、IndentedLearningJsonContext|元版・比較に必要|
|[src/Meltype.Core/Config/PastePolicy.cs](src/Meltype.Core/Config/PastePolicy.cs)|`PastePolicy` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|PastePolicy|元版・比較に必要|
|[src/Meltype.Core/Config/ProjectInfo.cs](src/Meltype.Core/Config/ProjectInfo.cs)|`ProjectInfo` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ProjectInfo|元版・比較に必要|
|[src/Meltype.Core/Config/Settings.cs](src/Meltype.Core/Config/Settings.cs)|設定と候補の実効動作の元版C#実装。既存の製品経路と独立した比較基準として残す。|InputStyle、DetectionLevel、CompositionSize、CompositionPlacement|元版・比較に必要|
|[src/Meltype.Core/Detection/BuiltInWordChecker.cs](src/Meltype.Core/Detection/BuiltInWordChecker.cs)|`BuiltInWordChecker` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|BuiltInWordChecker|元版・比較に必要|
|[src/Meltype.Core/Detection/DetectionTypes.cs](src/Meltype.Core/Detection/DetectionTypes.cs)|日英・入力対象の判定の元版C#実装。既存の製品経路と独立した比較基準として残す。|Verdict、DetectionInput、struct、DetectionResult|元版・比較に必要|
|[src/Meltype.Core/Detection/DictionaryDetector.cs](src/Meltype.Core/Detection/DictionaryDetector.cs)|辞書の検索・更新の元版C#実装。既存の製品経路と独立した比較基準として残す。|DictionaryDetector|元版・比較に必要|
|[src/Meltype.Core/Detection/EnglishDetector.cs](src/Meltype.Core/Detection/EnglishDetector.cs)|英語の判定・候補の元版C#実装。既存の製品経路と独立した比較基準として残す。|EnglishDetector|元版・比較に必要|
|[src/Meltype.Core/Detection/IWordChecker.cs](src/Meltype.Core/Detection/IWordChecker.cs)|`IWordChecker` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|IWordChecker|元版・比較に必要|
|[src/Meltype.Core/Detection/KanaDetector.cs](src/Meltype.Core/Detection/KanaDetector.cs)|かな・JISかなの変換の元版C#実装。既存の製品経路と独立した比較基準として残す。|KanaDetector|元版・比較に必要|
|[src/Meltype.Core/Detection/ProperNouns.cs](src/Meltype.Core/Detection/ProperNouns.cs)|`ProperNouns` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ProperNouns|元版・比較に必要|
|[src/Meltype.Core/Detection/RomajiDetector.cs](src/Meltype.Core/Detection/RomajiDetector.cs)|ローマ字表とかな変換の元版C#実装。既存の製品経路と独立した比較基準として残す。|struct、RomajiAnalysis、RomajiDetector|元版・比較に必要|
|[src/Meltype.Core/Detection/ScoreEngine.cs](src/Meltype.Core/Detection/ScoreEngine.cs)|`ScoreEngine` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ScoreEngine|元版・比較に必要|
|[src/Meltype.Core/Detection/TypoDetector.cs](src/Meltype.Core/Detection/TypoDetector.cs)|`TypoDetector` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|TypoDetector|元版・比較に必要|
|[src/Meltype.Core/Detection/WordList.cs](src/Meltype.Core/Detection/WordList.cs)|`WordList` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|WordList、DictionarySource|元版・比較に必要|
|[src/Meltype.Core/Diagnostics/Log.cs](src/Meltype.Core/Diagnostics/Log.cs)|`Log` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|LogLevel、struct、Log|元版・比較に必要|
|[src/Meltype.Core/Input/AltTapDetector.cs](src/Meltype.Core/Input/AltTapDetector.cs)|`AltTapDetector` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|AltTap、AltTapDetector|元版・比較に必要|
|[src/Meltype.Core/Input/InputSession.cs](src/Meltype.Core/Input/InputSession.cs)|入力セッションの状態遷移の元版C#実装。既存の製品経路と独立した比較基準として残す。|SessionState、struct、FlushRequest、SessionSummary|元版・比較に必要|
|[src/Meltype.Core/Input/KeyEvent.cs](src/Meltype.Core/Input/KeyEvent.cs)|キーと修飾キーの扱いの元版C#実装。既存の製品経路と独立した比較基準として残す。|struct、VirtualKeys|元版・比較に必要|
|[src/Meltype.Core/Input/LineContext.cs](src/Meltype.Core/Input/LineContext.cs)|周辺文・文脈の判定の元版C#実装。既存の製品経路と独立した比較基準として残す。|LineKind、CodeFocus、LineContext、LineTracker|元版・比較に必要|
|[src/Meltype.Core/Input/PendingInput.cs](src/Meltype.Core/Input/PendingInput.cs)|入力イベント・入力先の元版C#実装。既存の製品経路と独立した比較基準として残す。|PendingInput|元版・比較に必要|
|[src/Meltype.Core/Learning/UserModel.cs](src/Meltype.Core/Learning/UserModel.cs)|`UserModel` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|PrefixStats、SessionOutcome、UserModel、ModelFile|元版・比較に必要|
|[src/Meltype.Core/Meltype.Core.csproj](src/Meltype.Core/Meltype.Core.csproj)|Meltype.CoreのC#参照・テストホストのビルド条件と依存を定義する。|net10.0、Meltype.Core|元版・比較に必要|
|[src/Meltype.Mac.Native/Exports.cs](src/Meltype.Mac.Native/Exports.cs)|`Exports` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|Exports、CallbackConverter、CallbackWordChecker|元版・比較に必要|
|[src/Meltype.Mac.Native/Meltype.Mac.Native.csproj](src/Meltype.Mac.Native/Meltype.Mac.Native.csproj)|Meltype.Mac.NativeのC#参照・テストホストのビルド条件と依存を定義する。|net10.0、MeltypeNative|元版・比較に必要|
|[src/Meltype.Tests/DebugUnits.cs](src/Meltype.Tests/DebugUnits.cs)|`DebugUnits` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|DebugUnits|元版・比較に必要|
|[src/Meltype.Tests/EmojiGenerator.cs](src/Meltype.Tests/EmojiGenerator.cs)|`EmojiGenerator` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|EmojiGenerator|元版・比較に必要|
|[src/Meltype.Tests/KeyboardLayoutTests.cs](src/Meltype.Tests/KeyboardLayoutTests.cs)|キーと修飾キーの扱いの元版C#実装。既存の製品経路と独立した比較基準として残す。|KeyboardLayoutTests、FakeIme|元版・比較に必要|
|[src/Meltype.Tests/Meltype.Tests.csproj](src/Meltype.Tests/Meltype.Tests.csproj)|Meltype.TestsのC#参照・テストホストのビルド条件と依存を定義する。|net10.0-windows|元版・比較に必要|
|[src/Meltype.Tests/RuntimeClosure.cs](src/Meltype.Tests/RuntimeClosure.cs)|時刻とタイミングの元版C#実装。既存の製品経路と独立した比較基準として残す。|RuntimeClosure、Assembly|元版・比較に必要|
|[src/Meltype.Tests/TestRunner.cs](src/Meltype.Tests/TestRunner.cs)|`TestRunner` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|TestRunner|元版・比較に必要|
|[src/Meltype.Tests/TipEndToEnd.cs](src/Meltype.Tests/TipEndToEnd.cs)|`TipEndToEnd` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|TipEndToEnd、ITfInputProcessorProfileMgr|元版・比較に必要|
|[src/Meltype.Tests/TipHarness.cs](src/Meltype.Tests/TipHarness.cs)|`TipHarness` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|TipHarness|元版・比較に必要|
|[src/Meltype.Tests/WinMdProbe.cs](src/Meltype.Tests/WinMdProbe.cs)|`WinMdProbe` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|WinMdProbe|元版・比較に必要|
|[src/Meltype.Tests/WindowsTests.cs](src/Meltype.Tests/WindowsTests.cs)|OSウィンドウと表示の元版C#実装。既存の製品経路と独立した比較基準として残す。|WindowsTests|元版・比較に必要|
|[src/Meltype/AppInfo.cs](src/Meltype/AppInfo.cs)|`AppInfo` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|AppInfo|元版・比較に必要|
|[src/Meltype/Composition/ColorTextRenderer.cs](src/Meltype/Composition/ColorTextRenderer.cs)|文字列と編集範囲の元版C#実装。既存の製品経路と独立した比較基準として残す。|ColorTextRenderer、RenderTargetProperties、Rect、RectF|元版・比較に必要|
|[src/Meltype/Composition/CompositionService.cs](src/Meltype/Composition/CompositionService.cs)|未確定入力と変換状態の元版C#実装。既存の製品経路と独立した比較基準として残す。|CompositionService|元版・比較に必要|
|[src/Meltype/Composition/CompositionWindow.cs](src/Meltype/Composition/CompositionWindow.cs)|未確定入力と変換状態の元版C#実装。既存の製品経路と独立した比較基準として残す。|CompositionWindow、Palette、MeaningPopup|元版・比較に必要|
|[src/Meltype/Composition/FocusInspector.cs](src/Meltype/Composition/FocusInspector.cs)|`FocusInspector` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|FocusInfo、FocusInspector、FocusState|元版・比較に必要|
|[src/Meltype/Composition/KanjiConverter.cs](src/Meltype/Composition/KanjiConverter.cs)|`KanjiConverter` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|MsImeKanjiConverter、MORRSLT、WDD、IFELanguage|元版・比較に必要|
|[src/Meltype/Composition/ModeIndicatorWindow.cs](src/Meltype/Composition/ModeIndicatorWindow.cs)|OSウィンドウと表示の元版C#実装。既存の製品経路と独立した比較基準として残す。|ModeIndicatorWindow|元版・比較に必要|
|[src/Meltype/Composition/UiAutomation.cs](src/Meltype/Composition/UiAutomation.cs)|`UiAutomation` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|UiAutomation、Element、IUIAutomation、IUIAutomationCondition|元版・比較に必要|
|[src/Meltype/Composition/WinRtCandidates.cs](src/Meltype/Composition/WinRtCandidates.cs)|候補一覧と選択の元版C#実装。既存の製品経路と独立した比較基準として残す。|WinRtCandidates|元版・比較に必要|
|[src/Meltype/Detection/WindowsSpellChecker.cs](src/Meltype/Detection/WindowsSpellChecker.cs)|OSウィンドウと表示の元版C#実装。既存の製品経路と独立した比較基準として残す。|WindowsSpellChecker、ISpellCheckerFactory、ISpellChecker、IEnumSpellingError|元版・比較に必要|
|[src/Meltype/Diagnostics/ReportInfo.cs](src/Meltype/Diagnostics/ReportInfo.cs)|`ReportInfo` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ReportInfo|元版・比較に必要|
|[src/Meltype/ExitSignal.cs](src/Meltype/ExitSignal.cs)|`ExitSignal` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ExitSignal|元版・比較に必要|
|[src/Meltype/IME/ImeController.cs](src/Meltype/IME/ImeController.cs)|管理コマンドの実行の元版C#実装。既存の製品経路と独立した比較基準として残す。|InputLanguage、ImeMode、struct、ImeTarget|元版・比較に必要|
|[src/Meltype/IME/Imm32ImeController.cs](src/Meltype/IME/Imm32ImeController.cs)|管理コマンドの実行の元版C#実装。既存の製品経路と独立した比較基準として残す。|Imm32ImeController|元版・比較に必要|
|[src/Meltype/IME/TsfImeController.cs](src/Meltype/IME/TsfImeController.cs)|管理コマンドの実行の元版C#実装。既存の製品経路と独立した比較基準として残す。|TsfImeController、TF_INPUTPROCESSORPROFILE、ITfInputProcessorProfileMgr、IEnumTfInputProcessorProfiles|元版・比較に必要|
|[src/Meltype/Input/ForegroundTracker.cs](src/Meltype/Input/ForegroundTracker.cs)|`ForegroundTracker` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|AppInfo、ForegroundTracker|元版・比較に必要|
|[src/Meltype/Input/KeyInjector.cs](src/Meltype/Input/KeyInjector.cs)|キーと修飾キーの扱いの元版C#実装。既存の製品経路と独立した比較基準として残す。|IKeyInjector、KeyInjector|元版・比較に必要|
|[src/Meltype/Input/KeyText.cs](src/Meltype/Input/KeyText.cs)|キーと修飾キーの扱いの元版C#実装。既存の製品経路と独立した比較基準として残す。|KeyText|元版・比較に必要|
|[src/Meltype/Input/KeyboardLayoutPolicy.cs](src/Meltype/Input/KeyboardLayoutPolicy.cs)|キーと修飾キーの扱いの元版C#実装。既存の製品経路と独立した比較基準として残す。|KeyboardLayoutPolicy|元版・比較に必要|
|[src/Meltype/Input/KeyboardMonitor.cs](src/Meltype/Input/KeyboardMonitor.cs)|キーと修飾キーの扱いの元版C#実装。既存の製品経路と独立した比較基準として残す。|KeyboardMonitor|元版・比較に必要|
|[src/Meltype/Input/RemoteControl.cs](src/Meltype/Input/RemoteControl.cs)|管理コマンドの実行の元版C#実装。既存の製品経路と独立した比較基準として残す。|RemoteControl|元版・比較に必要|
|[src/Meltype/Meltype.csproj](src/Meltype/Meltype.csproj)|MeltypeのC#参照・テストホストのビルド条件と依存を定義する。|net10.0-windows、Meltype|元版・比較に必要|
|[src/Meltype/MeltypeEngine.cs](src/Meltype/MeltypeEngine.cs)|`MeltypeEngine` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|MeltypeEngine、ImeSnapshot|元版・比較に必要|
|[src/Meltype/Native.cs](src/Meltype/Native.cs)|ネイティブ境界と型幅の元版C#実装。既存の製品経路と独立した比較基準として残す。|Native、KBDLLHOOKSTRUCT、MSLLHOOKSTRUCT、INPUT|元版・比較に必要|
|[src/Meltype/Program.cs](src/Meltype/Program.cs)|`Program` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|Program|元版・比較に必要|
|[src/Meltype/SelfTest.cs](src/Meltype/SelfTest.cs)|`SelfTest` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|SelfTest|元版・比較に必要|
|[src/Meltype/Startup.cs](src/Meltype/Startup.cs)|自動起動と保守コマンドの元版C#実装。既存の製品経路と独立した比較基準として残す。|Startup|元版・比較に必要|
|[src/Meltype/Tip/TipServer.cs](src/Meltype/Tip/TipServer.cs)|`TipServer` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|TipServer、ITfInputProcessorProfileMgr|元版・比較に必要|
|[src/Meltype/UI/CorrectionReportDialog.cs](src/Meltype/UI/CorrectionReportDialog.cs)|`CorrectionReportDialog` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|CorrectionReportDialog|元版・比較に必要|
|[src/Meltype/UI/LearnedWordsForm.cs](src/Meltype/UI/LearnedWordsForm.cs)|学習の保存・反映の元版C#実装。既存の製品経路と独立した比較基準として残す。|LearnedWordsForm|元版・比較に必要|
|[src/Meltype/UI/LogForm.cs](src/Meltype/UI/LogForm.cs)|`LogForm` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|LogForm|元版・比較に必要|
|[src/Meltype/UI/ReportDialog.cs](src/Meltype/UI/ReportDialog.cs)|`ReportDialog` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ReportDialog|元版・比較に必要|
|[src/Meltype/UI/SettingsForm.cs](src/Meltype/UI/SettingsForm.cs)|設定と候補の実効動作の元版C#実装。既存の製品経路と独立した比較基準として残す。|SettingsForm、Binding|元版・比較に必要|
|[src/Meltype/UI/SnippetsForm.cs](src/Meltype/UI/SnippetsForm.cs)|`SnippetsForm` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|SnippetsForm|元版・比較に必要|
|[src/Meltype/UI/TextPrompt.cs](src/Meltype/UI/TextPrompt.cs)|文字列と編集範囲の元版C#実装。既存の製品経路と独立した比較基準として残す。|TextPrompt|元版・比較に必要|
|[src/Meltype/UI/ToggleSwitch.cs](src/Meltype/UI/ToggleSwitch.cs)|`ToggleSwitch` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|ToggleSwitch、SwitchAccessibleObject、ToggleSwitchColumn、ToggleSwitchCell|元版・比較に必要|
|[src/Meltype/UI/TrayApplicationContext.cs](src/Meltype/UI/TrayApplicationContext.cs)|周辺文・文脈の判定の元版C#実装。既存の製品経路と独立した比較基準として残す。|TrayApplicationContext、HotkeyWindow|元版・比較に必要|
|[src/Meltype/UI/UpdateChangesDialog.cs](src/Meltype/UI/UpdateChangesDialog.cs)|`UpdateChangesDialog` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|UpdateChangesDialog|元版・比較に必要|
|[src/Meltype/UI/UserDictionaryForm.cs](src/Meltype/UI/UserDictionaryForm.cs)|辞書の検索・更新の元版C#実装。既存の製品経路と独立した比較基準として残す。|UserDictionaryForm|元版・比較に必要|
|[src/Meltype/UI/WelcomeForm.cs](src/Meltype/UI/WelcomeForm.cs)|`WelcomeForm` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|WelcomeForm|元版・比較に必要|
|[src/Meltype/Updater.cs](src/Meltype/Updater.cs)|`Updater` にまとめた処理の元版C#実装。既存の製品経路と独立した比較基準として残す。|Updater|元版・比較に必要|

### 元版のテスト（1件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[tests/test_mac_scripts.lisp](tests/test_mac_scripts.lisp)|`test_mac_scripts` にまとめた処理の回帰・境界・失敗時の動作を検査する。|test_mac_scripts.lisp|回帰・比較に必要|

### Windows配布・導入（10件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[packaging/Find-InnoSetup.ps1](packaging/Find-InnoSetup.ps1)|Windowsの配布・導入で使う`Find-InnoSetup` にまとめた処理の手順。|if|実装・ビルドに必要|
|[packaging/Install.cmd](packaging/Install.cmd)|Windowsの配布・導入で使う`Install` にまとめた処理の手順。|Install.cmd|実装・ビルドに必要|
|[packaging/Meltype.iss](packaging/Meltype.iss)|packagingのビルド・導入・OS登録に使う `Meltype.iss` の宣言データ。|Meltype.iss|実装・ビルドに必要|
|[packaging/README.txt](packaging/README.txt)|Windowsの配布・導入で使う`README` にまとめた処理の手順。|94有効行|実装・ビルドに必要|
|[packaging/Uninstall.cmd](packaging/Uninstall.cmd)|Windowsの配布・導入で使う`Uninstall` にまとめた処理の手順。|Uninstall.cmd|実装・ビルドに必要|
|[packaging/install.ps1](packaging/install.ps1)|Windowsの配布・導入で使う`install` にまとめた処理の手順。|param、if、Stop-Meltype、elseif|実装・ビルドに必要|
|[packaging/meltype-ime.ps1](packaging/meltype-ime.ps1)|Windowsの配布・導入で使う`meltype-ime` にまとめた処理の手順。|Test-Arm64Windows、Test-MeltypeImeRegistered、Invoke-Elevated、Add-MeltypeImeToLanguageList|実装・ビルドに必要|
|[packaging/setup-ime.ps1](packaging/setup-ime.ps1)|Windowsの配布・導入で使う`setup-ime` にまとめた処理の手順。|param、try、catch|実装・ビルドに必要|
|[packaging/uninstall.ps1](packaging/uninstall.ps1)|Windowsの配布・導入で使う`uninstall` にまとめた処理の手順。|param、Say、Stop-Meltype、if|実装・ビルドに必要|
|[packaging/update.ps1](packaging/update.ps1)|Windowsの配布・導入で使う`update` にまとめた処理の手順。|param、try、catch|実装・ビルドに必要|

### macOSホスト・配布・互換試験（45件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[mac/Install Meltype.command](mac/Install%20Meltype.command)|`Install Meltype` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|Install Meltype.command|OS連携・互換経路|
|[mac/Package.resolved](mac/Package.resolved)|macのビルド・導入・OS登録に使う `Package.resolved` の宣言データ。|Package.resolved|OS連携・互換経路|
|[mac/Package.swift](mac/Package.swift)|`Package` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|Package.swift|OS連携・互換経路|
|[mac/README.md](mac/README.md)|「入力モードのショートカット (Mac 専用)」の手順・仕様・確認範囲を説明する。|入力モードのショートカット (Mac 専用)、設定と学習データも消すなら、使用中の入力ソースを入れ替えてよいときだけ:|説明・参照に必要|
|[mac/Resources/Info.plist](mac/Resources/Info.plist)|macのビルド・導入・OS登録に使う `Info.plist` の宣言データ。|CFBundleDevelopmentRegion、CFBundleExecutable、CFBundleIdentifier、CFBundleInfoDictionaryVersion|OS連携・互換経路|
|[mac/Resources/InputChecks.tsv](mac/Resources/InputChecks.tsv)|入力イベント・入力先をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|InputChecks.tsv|OS連携・互換経路|
|[mac/Resources/en.lproj/InfoPlist.strings](mac/Resources/en.lproj/InfoPlist.strings)|macのビルド・導入・OS登録に使う `InfoPlist.strings` の宣言データ。|InfoPlist.strings|OS連携・互換経路|
|[mac/Resources/icon.tiff](mac/Resources/icon.tiff)|OSパッケージ・画面で使う `icon` の画像リソース。|画像リソース|OS連携・互換経路|
|[mac/Resources/ja.lproj/InfoPlist.strings](mac/Resources/ja.lproj/InfoPlist.strings)|macのビルド・導入・OS登録に使う `InfoPlist.strings` の宣言データ。|InfoPlist.strings|OS連携・互換経路|
|[mac/Sources/MeltypeIME/Converter.swift](mac/Sources/MeltypeIME/Converter.swift)|`Converter` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|MeltypeConverter、resource、emojiDictionary、results|OS連携・互換経路|
|[mac/Sources/MeltypeIME/InputController.swift](mac/Sources/MeltypeIME/InputController.swift)|入力イベント・入力先をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|MeltypeInputController、activateServer、recognizedEvents、handle|OS連携・互換経路|
|[mac/Sources/MeltypeIME/InputSourceRegistration.swift](mac/Sources/MeltypeIME/InputSourceRegistration.swift)|OSの入力ソース登録をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|InputSourceRegistration、normalized、register|OS連携・互換経路|
|[mac/Sources/MeltypeIME/KeyMapping.swift](mac/Sources/MeltypeIME/KeyMapping.swift)|キーと修飾キーの扱いをmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|KeyMapping、virtualKey、modifiers|OS連携・互換経路|
|[mac/Sources/MeltypeIME/LegacyKeyMapping.swift](mac/Sources/MeltypeIME/LegacyKeyMapping.swift)|キーと修飾キーの扱いをmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|LegacyKeyMapping、virtualKey|OS連携・互換経路|
|[mac/Sources/MeltypeIME/MacUserDictionary.swift](mac/Sources/MeltypeIME/MacUserDictionary.swift)|辞書の検索・更新をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|MacUserDictionary、ImportError、entries、merge|OS連携・互換経路|
|[mac/Sources/MeltypeIME/ModeIndicator.swift](mac/Sources/MeltypeIME/ModeIndicator.swift)|`ModeIndicator` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|ModeIndicator、validCaret、labelSize、Layout|OS連携・互換経路|
|[mac/Sources/MeltypeIME/ModeServices.swift](mac/Sources/MeltypeIME/ModeServices.swift)|OS入力サービスの連携をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|selectModeFromService、ModeServices、activate、deactivate|OS連携・互換経路|
|[mac/Sources/MeltypeIME/ModeShortcuts.swift](mac/Sources/MeltypeIME/ModeShortcuts.swift)|`ModeShortcuts` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|MacInputMode、ModeShortcutAction、mode、ModeIndicatorSettings|OS連携・互換経路|
|[mac/Sources/MeltypeIME/NativeCore.swift](mac/Sources/MeltypeIME/NativeCore.swift)|ネイティブ境界と型幅をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|SessionResult、TextEdit、CompositionView、NativeCore|OS連携・互換経路|
|[mac/Sources/MeltypeIME/NativeEditorPolicy.swift](mac/Sources/MeltypeIME/NativeEditorPolicy.swift)|ネイティブ境界と型幅をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|NativeEditorPolicy、apply、composition、candidates|OS連携・互換経路|
|[mac/Sources/MeltypeIME/NativeKeyPolicy.swift](mac/Sources/MeltypeIME/NativeKeyPolicy.swift)|キーと修飾キーの扱いをmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|NativeKeyPolicy、virtualKey、modifiers|OS連携・互換経路|
|[mac/Sources/MeltypeIME/main.swift](mac/Sources/MeltypeIME/main.swift)|`main` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|check、type|OS連携・互換経路|
|[mac/Sources/RegisterInputSource/main.swift](mac/Sources/RegisterInputSource/main.swift)|`main` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|isEnabledInputSource、restartTextInputMenuAgent|OS連携・互換経路|
|[mac/TESTER-README.txt](mac/TESTER-README.txt)|`TESTER-README` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|43有効行|OS連携・互換経路|
|[mac/Tests/MeltypeIMETests/ModeIndicatorTests.swift](mac/Tests/MeltypeIMETests/ModeIndicatorTests.swift)|`ModeIndicatorTests` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|ModeIndicatorTests、testSavedSelectionIsRestoredInNewSession、testCycleStartsAfterSavedSelectionAndUsesM、testCaretWindowIs|OS連携・互換経路|
|[mac/Tests/MeltypeIMETests/ModeShortcutsTests.swift](mac/Tests/MeltypeIMETests/ModeShortcutsTests.swift)|`ModeShortcutsTests` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|ModeShortcutsTests、Target、selectModeFromService、invoke|OS連携・互換経路|
|[mac/build-cli.sh](mac/build-cli.sh)|`build-cli` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|usage、need、ensure_dotnet_path|OS連携・互換経路|
|[mac/build.sh](mac/build.sh)|`build` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|enable_input_source、register_input_source|OS連携・互換経路|
|[mac/install-app.sh](mac/install-app.sh)|`install-app` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|cleanup|OS連携・互換経路|
|[mac/install.sh](mac/install.sh)|`install` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|enable_input_source、register_input_source、select_input_source|OS連携・互換経路|
|[mac/package.sh](mac/package.sh)|`package` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|cleanup|OS連携・互換経路|
|[mac/select-input-source.swift](mac/select-input-source.swift)|入力イベント・入力先をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|sourceID|OS連携・互換経路|
|[mac/start-input-method.sh](mac/start-input-method.sh)|入力イベント・入力先をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|start-input-method.sh|OS連携・互換経路|
|[mac/test/key-policy/README.md](mac/test/key-policy/README.md)|「Rust macOS key policy validation」の手順・仕様・確認範囲を説明する。|Rust macOS key policy validation|回帰・比較に必要|
|[mac/test/key-policy/fixtures/CarbonSignatures.c](mac/test/key-policy/fixtures/CarbonSignatures.c)|`CarbonSignatures` にまとめた処理の回帰・境界・失敗時の動作を検査する。|CarbonSignatures.c|回帰・比較に必要|
|[mac/test/key-policy/fixtures/Converter.swift](mac/test/key-policy/fixtures/Converter.swift)|`Converter` にまとめた処理の回帰・境界・失敗時の動作を検査する。|MeltypeConverter、clauses、candidates|回帰・比較に必要|
|[mac/test/key-policy/fixtures/DictionaryDialog.swift](mac/test/key-policy/fixtures/DictionaryDialog.swift)|辞書の検索・更新の回帰・境界・失敗時の動作を検査する。|testDictionaryDialog、replace、optionalText、legacy|回帰・比較に必要|
|[mac/test/key-policy/fixtures/LegacyInputSourceRegistration.swift](mac/test/key-policy/fixtures/LegacyInputSourceRegistration.swift)|OSの入力ソース登録の回帰・境界・失敗時の動作を検査する。|LegacyInputSourceRegistration、normalized、repair|回帰・比較に必要|
|[mac/test/key-policy/fixtures/LegacyMacUserDictionary.swift](mac/test/key-policy/fixtures/LegacyMacUserDictionary.swift)|辞書の検索・更新の回帰・境界・失敗時の動作を検査する。|LegacyMacUserDictionary、ImportError、entries、merge|回帰・比較に必要|
|[mac/test/key-policy/fixtures/LegacyModeIndicator.swift](mac/test/key-policy/fixtures/LegacyModeIndicator.swift)|`LegacyModeIndicator` にまとめた処理の回帰・境界・失敗時の動作を検査する。|LegacyModeIndicator、validCaret、labelSize、Layout|回帰・比較に必要|
|[mac/test/key-policy/fixtures/RegistrationChecks.swift](mac/test/key-policy/fixtures/RegistrationChecks.swift)|OSの入力ソース登録の回帰・境界・失敗時の動作を検査する。|testInputSourceRegistration|回帰・比較に必要|
|[mac/test/key-policy/main.swift](mac/test/key-policy/main.swift)|`main` にまとめた処理の回帰・境界・失敗時の動作を検査する。|event、compare、legacyReplacement、response|回帰・比較に必要|
|[mac/test/key-policy/run.sh](mac/test/key-policy/run.sh)|`run` にまとめた処理の回帰・境界・失敗時の動作を検査する。|run.sh|回帰・比較に必要|
|[mac/test/register-helper/run.sh](mac/test/register-helper/run.sh)|`run` にまとめた処理の回帰・境界・失敗時の動作を検査する。|print|回帰・比較に必要|
|[mac/update.sh](mac/update.sh)|`update` にまとめた処理をmacOSの入力メソッド・Swift/Carbon・配布へ接続する。|restore_input|OS連携・互換経路|

### Linuxホスト・配布・ABI試験（32件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[linux/README.md](linux/README.md)|「設定と学習データも消すなら」の手順・仕様・確認範囲を説明する。|設定と学習データも消すなら|説明・参照に必要|
|[linux/TESTER-README.txt](linux/TESTER-README.txt)|`TESTER-README` にまとめた処理をLinux IBus/fcitx5のホスト・ビルドへ接続する。|33有効行|OS連携・互換経路|
|[linux/build-cli.sh](linux/build-cli.sh)|`build-cli` にまとめた処理をLinux IBus/fcitx5のホスト・ビルドへ接続する。|usage、need|OS連携・互換経路|
|[linux/build.sh](linux/build.sh)|`build` にまとめた処理をLinux IBus/fcitx5のホスト・ビルドへ接続する。|build.sh|OS連携・互換経路|
|[linux/fcitx5/CMakeLists.txt](linux/fcitx5/CMakeLists.txt)|linuxのビルド・導入・OS登録に使う `CMakeLists.txt` の宣言データ。|51有効行|OS連携・互換経路|
|[linux/fcitx5/README.md](linux/fcitx5/README.md)|「fcitx5 adapter migration」の手順・仕様・確認範囲を説明する。|fcitx5 adapter migration|説明・参照に必要|
|[linux/fcitx5/compat/json_reader.hpp](linux/fcitx5/compat/json_reader.hpp)|JSON形式の要求・応答をLinux IBus/fcitx5のホスト・ビルドへ接続する。|JsonReader|OS連携・互換経路|
|[linux/fcitx5/compat/key_mapping.hpp](linux/fcitx5/compat/key_mapping.hpp)|キーと修飾キーの扱いをLinux IBus/fcitx5のホスト・ビルドへ接続する。|key_mapping.hpp|OS連携・互換経路|
|[linux/fcitx5/compat/text.hpp](linux/fcitx5/compat/text.hpp)|文字列と編集範囲をLinux IBus/fcitx5のホスト・ビルドへ接続する。|text.hpp|OS連携・互換経路|
|[linux/fcitx5/json_value.hpp](linux/fcitx5/json_value.hpp)|JSON形式の要求・応答をLinux IBus/fcitx5のホスト・ビルドへ接続する。|Json、class|OS連携・互換経路|
|[linux/fcitx5/meltype-addon.conf](linux/fcitx5/meltype-addon.conf)|linuxのビルド・導入・OS登録に使う `meltype-addon.conf` の宣言データ。|meltype-addon.conf|OS連携・互換経路|
|[linux/fcitx5/meltype.conf](linux/fcitx5/meltype.conf)|linuxのビルド・導入・OS登録に使う `meltype.conf` の宣言データ。|meltype.conf|OS連携・互換経路|
|[linux/fcitx5/meltype.cpp](linux/fcitx5/meltype.cpp)|`meltype` にまとめた処理をLinux IBus/fcitx5のホスト・ビルドへ接続する。|Native、Surrounding、MeltypeEngine、MeltypeState|OS連携・互換経路|
|[linux/fcitx5/rust_json.hpp](linux/fcitx5/rust_json.hpp)|JSON形式の要求・応答をLinux IBus/fcitx5のホスト・ビルドへ接続する。|RustJsonBuilder、Frame|OS連携・互換経路|
|[linux/fcitx5/test/edit_reference.hpp](linux/fcitx5/test/edit_reference.hpp)|`edit_reference` にまとめた処理の回帰・境界・失敗時の動作を検査する。|Editor|回帰・比較に必要|
|[linux/fcitx5/test/event_abi.py](linux/fcitx5/test/event_abi.py)|外部ABIの型・所有権の回帰・境界・失敗時の動作を検査する。|Context、bind、take、Host|回帰・比較に必要|
|[linux/fcitx5/test/json_bridge.cpp](linux/fcitx5/test/json_bridge.cpp)|JSON形式の要求・応答の回帰・境界・失敗時の動作を検査する。|json_bridge.cpp|回帰・比較に必要|
|[linux/fcitx5/test/json_bridge.py](linux/fcitx5/test/json_bridge.py)|JSON形式の要求・応答の回帰・境界・失敗時の動作を検査する。|json_bridge.py|回帰・比較に必要|
|[linux/fcitx5/test/key_mapping.cpp](linux/fcitx5/test/key_mapping.cpp)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|key_mapping.cpp|回帰・比較に必要|
|[linux/fcitx5/test/state_abi.py](linux/fcitx5/test/state_abi.py)|外部ABIの型・所有権の回帰・境界・失敗時の動作を検査する。|key|回帰・比較に必要|
|[linux/fcitx5/test/testfrontend.conf](linux/fcitx5/test/testfrontend.conf)|linuxのビルド・導入・OS登録に使う `testfrontend.conf` の宣言データ。|testfrontend.conf|回帰・比較に必要|
|[linux/fcitx5/test/testim.conf](linux/fcitx5/test/testim.conf)|linuxのビルド・導入・OS登録に使う `testim.conf` の宣言データ。|testim.conf|回帰・比較に必要|
|[linux/fcitx5/test/testmeltype.cpp](linux/fcitx5/test/testmeltype.cpp)|`testmeltype` にまとめた処理の回帰・境界・失敗時の動作を検査する。|testmeltype.cpp|回帰・比較に必要|
|[linux/fcitx5/test/testui.conf](linux/fcitx5/test/testui.conf)|linuxのビルド・導入・OS登録に使う `testui.conf` の宣言データ。|testui.conf|回帰・比較に必要|
|[linux/ibus-engine-meltype](linux/ibus-engine-meltype)|`ibus-engine-meltype` にまとめた処理をLinux IBus/fcitx5のホスト・ビルドへ接続する。|ibus-engine-meltype|OS連携・互換経路|
|[linux/icon.png](linux/icon.png)|OSパッケージ・画面で使う `icon` の画像リソース。|画像リソース|OS連携・互換経路|
|[linux/install.sh](linux/install.sh)|`install` にまとめた処理をLinux IBus/fcitx5のホスト・ビルドへ接続する。|install.sh|OS連携・互換経路|
|[linux/meltype.xml](linux/meltype.xml)|linuxのビルド・導入・OS登録に使う `meltype.xml` の宣言データ。|org.freedesktop.IBus.Meltype、meltype|OS連携・互換経路|
|[linux/tests/test_daemon.py](linux/tests/test_daemon.py)|`test_daemon` にまとめた処理の回帰・境界・失敗時の動作を検査する。|SyntheticClient、__init__、clear、committed|回帰・比較に必要|
|[linux/tests/test_integration.py](linux/tests/test_integration.py)|`test_integration` にまとめた処理の回帰・境界・失敗時の動作を検査する。|IntegrationTests、setUpClass、ClientEngine、__init__|回帰・比較に必要|
|[linux/tests/test_virtual_key.py](linux/tests/test_virtual_key.py)|キーと修飾キーの扱いの回帰・境界・失敗時の動作を検査する。|FakeIBus、__init__、__getattr__、keyval_to_unicode|回帰・比較に必要|
|[linux/uninstall.sh](linux/uninstall.sh)|`uninstall` にまとめた処理をLinux IBus/fcitx5のホスト・ビルドへ接続する。|uninstall.sh|OS連携・互換経路|

### Androidホスト・画面・JNI試験（17件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[android/README.md](android/README.md)|「Meltype Rust — Android IME preview」の手順・仕様・確認範囲を説明する。|Meltype Rust — Android IME preview、Androidフレームワーク上の検証（追加、実行結果はCIで確認）|説明・参照に必要|
|[android/app/build.gradle.kts](android/app/build.gradle.kts)|androidのビルド・導入・OS登録に使う `build.gradle.kts` の宣言データ。|build.gradle.kts|OS連携・互換経路|
|[android/app/src/androidTest/java/moe/meltype/rust/NativeAndroidTest.java](android/app/src/androidTest/java/moe/meltype/rust/NativeAndroidTest.java)|ネイティブ境界と型幅をAndroidのIMEサービス・画面・JNIへ接続する。|NativeAndroidTest、ServiceFrame、ActivityFrame、PickerCapture|OS連携・互換経路|
|[android/app/src/main/AndroidManifest.xml](android/app/src/main/AndroidManifest.xml)|androidのビルド・導入・OS登録に使う `AndroidManifest.xml` の宣言データ。|AndroidManifest.xml|OS連携・互換経路|
|[android/app/src/main/java/moe/meltype/rust/MeltypeService.java](android/app/src/main/java/moe/meltype/rust/MeltypeService.java)|OS入力サービスの連携をAndroidのIMEサービス・画面・JNIへ接続する。|MeltypeService|OS連携・互換経路|
|[android/app/src/main/java/moe/meltype/rust/Native.java](android/app/src/main/java/moe/meltype/rust/Native.java)|ネイティブ境界と型幅をAndroidのIMEサービス・画面・JNIへ接続する。|Native|OS連携・互換経路|
|[android/app/src/main/java/moe/meltype/rust/SettingsActivity.java](android/app/src/main/java/moe/meltype/rust/SettingsActivity.java)|設定と候補の実効動作をAndroidのIMEサービス・画面・JNIへ接続する。|SettingsActivity|OS連携・互換経路|
|[android/app/src/main/java/moe/meltype/rust/SettingsPage.java](android/app/src/main/java/moe/meltype/rust/SettingsPage.java)|設定と候補の実効動作をAndroidのIMEサービス・画面・JNIへ接続する。|SettingsPage、Actions|OS連携・互換経路|
|[android/app/src/main/res/values/strings.xml](android/app/src/main/res/values/strings.xml)|androidのビルド・導入・OS登録に使う `strings.xml` の宣言データ。|strings.xml|OS連携・互換経路|
|[android/app/src/main/res/xml/method.xml](android/app/src/main/res/xml/method.xml)|androidのビルド・導入・OS登録に使う `method.xml` の宣言データ。|method.xml|OS連携・互換経路|
|[android/build.gradle.kts](android/build.gradle.kts)|androidのビルド・導入・OS登録に使う `build.gradle.kts` の宣言データ。|build.gradle.kts|OS連携・互換経路|
|[android/gradle.properties](android/gradle.properties)|androidのビルド・導入・OS登録に使う `gradle.properties` の宣言データ。|gradle.properties|OS連携・互換経路|
|[android/settings.gradle.kts](android/settings.gradle.kts)|androidのビルド・導入・OS登録に使う `settings.gradle.kts` の宣言データ。|settings.gradle.kts|OS連携・互換経路|
|[android/tests/.gitattributes](android/tests/.gitattributes)|`.gitattributes` にまとめた処理の回帰・境界・失敗時の動作を検査する。|.gitattributes|回帰・比較に必要|
|[android/tests/instrument.py](android/tests/instrument.py)|`instrument` にまとめた処理の回帰・境界・失敗時の動作を検査する。|verify_report、main|回帰・比較に必要|
|[android/tests/run-emulator.sh](android/tests/run-emulator.sh)|`run-emulator` にまとめた処理の回帰・境界・失敗時の動作を検査する。|cleanup|回帰・比較に必要|
|[android/tests/test_instrument_report.py](android/tests/test_instrument_report.py)|`test_instrument_report` にまとめた処理の回帰・境界・失敗時の動作を検査する。|ReportTests、test_required_completed_tests、test_empty_success_is_not_a_test_run、test_skips_do_not_satisfy_required_tests|回帰・比較に必要|

### 説明文と参照画像（178件）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)|「DEVELOPMENT」の手順・仕様・確認範囲を説明する。|DEVELOPMENT.md|説明・参照に必要|
|[docs/MAC-CLI.md](docs/MAC-CLI.md)|「MAC-CLI」の手順・仕様・確認範囲を説明する。|MAC-CLI.md|説明・参照に必要|
|[docs/RELEASE.md](docs/RELEASE.md)|「RELEASE」の手順・仕様・確認範囲を説明する。|RELEASE.md|説明・参照に必要|
|[docs/TSF_DESIGN.md](docs/TSF_DESIGN.md)|「TSF_DESIGN」の手順・仕様・確認範囲を説明する。|TSF_DESIGN.md|説明・参照に必要|
|[docs/USAGE.md](docs/USAGE.md)|「USAGE」の手順・仕様・確認範囲を説明する。|USAGE.md|説明・参照に必要|
|[docs/WINDOWS.md](docs/WINDOWS.md)|「WINDOWS」の手順・仕様・確認範囲を説明する。|WINDOWS.md|説明・参照に必要|
|[docs/WSL.md](docs/WSL.md)|「WSL」の手順・仕様・確認範囲を説明する。|WSL.md|説明・参照に必要|
|[docs/images/cards/community-discord.svg](docs/images/cards/community-discord.svg)|説明文から参照する `community-discord` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/contribute-code.svg](docs/images/cards/contribute-code.svg)|説明文から参照する `contribute-code` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/contribute-dictionary.svg](docs/images/cards/contribute-dictionary.svg)|説明文から参照する `contribute-dictionary` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/contribute-issue.svg](docs/images/cards/contribute-issue.svg)|説明文から参照する `contribute-issue` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/os-linux.svg](docs/images/cards/os-linux.svg)|説明文から参照する `os-linux` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/os-mac.svg](docs/images/cards/os-mac.svg)|説明文から参照する `os-mac` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/os-windows.svg](docs/images/cards/os-windows.svg)|説明文から参照する `os-windows` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/privacy-network.svg](docs/images/cards/privacy-network.svg)|説明文から参照する `privacy-network` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/privacy-send.svg](docs/images/cards/privacy-send.svg)|説明文から参照する `privacy-send` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/privacy-storage.svg](docs/images/cards/privacy-storage.svg)|説明文から参照する `privacy-storage` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/cards/support-sponsors.svg](docs/images/cards/support-sponsors.svg)|説明文から参照する `support-sponsors` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/demo.svg](docs/images/demo.svg)|説明文から参照する `demo` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/features.svg](docs/images/features.svg)|説明文から参照する `features` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/code-of-conduct/01.svg](docs/images/headings/code-of-conduct/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/code-of-conduct/02.svg](docs/images/headings/code-of-conduct/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/code-of-conduct/03.svg](docs/images/headings/code-of-conduct/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/code-of-conduct/04.svg](docs/images/headings/code-of-conduct/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/code-of-conduct/05.svg](docs/images/headings/code-of-conduct/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/code-of-conduct/title.svg](docs/images/headings/code-of-conduct/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/community.svg](docs/images/headings/community.svg)|説明文から参照する `community` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/01.svg](docs/images/headings/contributing/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/02.svg](docs/images/headings/contributing/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/03.svg](docs/images/headings/contributing/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/04.svg](docs/images/headings/contributing/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/05.svg](docs/images/headings/contributing/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/06.svg](docs/images/headings/contributing/06.svg)|説明文から参照する `06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/07.svg](docs/images/headings/contributing/07.svg)|説明文から参照する `07` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/s01.svg](docs/images/headings/contributing/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/s02.svg](docs/images/headings/contributing/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/s03.svg](docs/images/headings/contributing/s03.svg)|説明文から参照する `s03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/s04.svg](docs/images/headings/contributing/s04.svg)|説明文から参照する `s04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/s05.svg](docs/images/headings/contributing/s05.svg)|説明文から参照する `s05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/s06.svg](docs/images/headings/contributing/s06.svg)|説明文から参照する `s06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/contributing/title.svg](docs/images/headings/contributing/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/develop.svg](docs/images/headings/develop.svg)|説明文から参照する `develop` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/01.svg](docs/images/headings/development/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/02.svg](docs/images/headings/development/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/03.svg](docs/images/headings/development/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/04.svg](docs/images/headings/development/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/05.svg](docs/images/headings/development/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/06.svg](docs/images/headings/development/06.svg)|説明文から参照する `06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/s01.svg](docs/images/headings/development/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/s02.svg](docs/images/headings/development/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/s03.svg](docs/images/headings/development/s03.svg)|説明文から参照する `s03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/s04.svg](docs/images/headings/development/s04.svg)|説明文から参照する `s04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/s05.svg](docs/images/headings/development/s05.svg)|説明文から参照する `s05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/development/title.svg](docs/images/headings/development/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/faq.svg](docs/images/headings/faq.svg)|説明文から参照する `faq` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/features.svg](docs/images/headings/features.svg)|説明文から参照する `features` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/install.svg](docs/images/headings/install.svg)|説明文から参照する `install` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/license.svg](docs/images/headings/license.svg)|説明文から参照する `license` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/01.svg](docs/images/headings/linux/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/02.svg](docs/images/headings/linux/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/03.svg](docs/images/headings/linux/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/04.svg](docs/images/headings/linux/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/05.svg](docs/images/headings/linux/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/06.svg](docs/images/headings/linux/06.svg)|説明文から参照する `06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/s01.svg](docs/images/headings/linux/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/s02.svg](docs/images/headings/linux/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/linux/title.svg](docs/images/headings/linux/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac-cli/01.svg](docs/images/headings/mac-cli/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac-cli/02.svg](docs/images/headings/mac-cli/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac-cli/03.svg](docs/images/headings/mac-cli/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac-cli/04.svg](docs/images/headings/mac-cli/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac-cli/05.svg](docs/images/headings/mac-cli/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac-cli/06.svg](docs/images/headings/mac-cli/06.svg)|説明文から参照する `06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac-cli/07.svg](docs/images/headings/mac-cli/07.svg)|説明文から参照する `07` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac-cli/title.svg](docs/images/headings/mac-cli/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/01.svg](docs/images/headings/mac/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/02.svg](docs/images/headings/mac/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/03.svg](docs/images/headings/mac/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/04.svg](docs/images/headings/mac/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/05.svg](docs/images/headings/mac/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/06.svg](docs/images/headings/mac/06.svg)|説明文から参照する `06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/s01.svg](docs/images/headings/mac/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/s02.svg](docs/images/headings/mac/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/s03.svg](docs/images/headings/mac/s03.svg)|説明文から参照する `s03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/s04.svg](docs/images/headings/mac/s04.svg)|説明文から参照する `s04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/s05.svg](docs/images/headings/mac/s05.svg)|説明文から参照する `s05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/mac/title.svg](docs/images/headings/mac/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/01.svg](docs/images/headings/performance/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/02.svg](docs/images/headings/performance/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/s01.svg](docs/images/headings/performance/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/s02.svg](docs/images/headings/performance/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/s03.svg](docs/images/headings/performance/s03.svg)|説明文から参照する `s03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/s04.svg](docs/images/headings/performance/s04.svg)|説明文から参照する `s04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/s05.svg](docs/images/headings/performance/s05.svg)|説明文から参照する `s05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/s06.svg](docs/images/headings/performance/s06.svg)|説明文から参照する `s06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/performance/title.svg](docs/images/headings/performance/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/privacy.svg](docs/images/headings/privacy.svg)|説明文から参照する `privacy` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/readme/s01.svg](docs/images/headings/readme/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/readme/s02.svg](docs/images/headings/readme/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/readme/s03.svg](docs/images/headings/readme/s03.svg)|説明文から参照する `s03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/readme/s04.svg](docs/images/headings/readme/s04.svg)|説明文から参照する `s04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/readme/s05.svg](docs/images/headings/readme/s05.svg)|説明文から参照する `s05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/readme/s06.svg](docs/images/headings/readme/s06.svg)|説明文から参照する `s06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/release/01.svg](docs/images/headings/release/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/release/02.svg](docs/images/headings/release/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/release/03.svg](docs/images/headings/release/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/release/04.svg](docs/images/headings/release/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/release/title.svg](docs/images/headings/release/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/security/01.svg](docs/images/headings/security/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/security/02.svg](docs/images/headings/security/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/security/03.svg](docs/images/headings/security/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/security/04.svg](docs/images/headings/security/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/security/title.svg](docs/images/headings/security/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/stars.svg](docs/images/headings/stars.svg)|説明文から参照する `stars` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/start.svg](docs/images/headings/start.svg)|説明文から参照する `start` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/thanks.svg](docs/images/headings/thanks.svg)|説明文から参照する `thanks` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/01.svg](docs/images/headings/tsf-design/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/02.svg](docs/images/headings/tsf-design/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/03.svg](docs/images/headings/tsf-design/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/04.svg](docs/images/headings/tsf-design/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/05.svg](docs/images/headings/tsf-design/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/06.svg](docs/images/headings/tsf-design/06.svg)|説明文から参照する `06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/07.svg](docs/images/headings/tsf-design/07.svg)|説明文から参照する `07` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/08.svg](docs/images/headings/tsf-design/08.svg)|説明文から参照する `08` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/s01.svg](docs/images/headings/tsf-design/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/s02.svg](docs/images/headings/tsf-design/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/s03.svg](docs/images/headings/tsf-design/s03.svg)|説明文から参照する `s03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/tsf-design/title.svg](docs/images/headings/tsf-design/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/01.svg](docs/images/headings/usage/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/02.svg](docs/images/headings/usage/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/03.svg](docs/images/headings/usage/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/04.svg](docs/images/headings/usage/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/05.svg](docs/images/headings/usage/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/06.svg](docs/images/headings/usage/06.svg)|説明文から参照する `06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/07.svg](docs/images/headings/usage/07.svg)|説明文から参照する `07` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/08.svg](docs/images/headings/usage/08.svg)|説明文から参照する `08` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/09.svg](docs/images/headings/usage/09.svg)|説明文から参照する `09` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s01.svg](docs/images/headings/usage/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s02.svg](docs/images/headings/usage/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s03.svg](docs/images/headings/usage/s03.svg)|説明文から参照する `s03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s04.svg](docs/images/headings/usage/s04.svg)|説明文から参照する `s04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s05.svg](docs/images/headings/usage/s05.svg)|説明文から参照する `s05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s06.svg](docs/images/headings/usage/s06.svg)|説明文から参照する `s06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s07.svg](docs/images/headings/usage/s07.svg)|説明文から参照する `s07` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s08.svg](docs/images/headings/usage/s08.svg)|説明文から参照する `s08` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s09.svg](docs/images/headings/usage/s09.svg)|説明文から参照する `s09` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s10.svg](docs/images/headings/usage/s10.svg)|説明文から参照する `s10` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s11.svg](docs/images/headings/usage/s11.svg)|説明文から参照する `s11` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s12.svg](docs/images/headings/usage/s12.svg)|説明文から参照する `s12` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s13.svg](docs/images/headings/usage/s13.svg)|説明文から参照する `s13` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s14.svg](docs/images/headings/usage/s14.svg)|説明文から参照する `s14` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s15.svg](docs/images/headings/usage/s15.svg)|説明文から参照する `s15` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s16.svg](docs/images/headings/usage/s16.svg)|説明文から参照する `s16` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s17.svg](docs/images/headings/usage/s17.svg)|説明文から参照する `s17` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s18.svg](docs/images/headings/usage/s18.svg)|説明文から参照する `s18` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/s19.svg](docs/images/headings/usage/s19.svg)|説明文から参照する `s19` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/usage/title.svg](docs/images/headings/usage/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/01.svg](docs/images/headings/windows/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/02.svg](docs/images/headings/windows/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/03.svg](docs/images/headings/windows/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/04.svg](docs/images/headings/windows/04.svg)|説明文から参照する `04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/05.svg](docs/images/headings/windows/05.svg)|説明文から参照する `05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/06.svg](docs/images/headings/windows/06.svg)|説明文から参照する `06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/07.svg](docs/images/headings/windows/07.svg)|説明文から参照する `07` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/s01.svg](docs/images/headings/windows/s01.svg)|説明文から参照する `s01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/s02.svg](docs/images/headings/windows/s02.svg)|説明文から参照する `s02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/s03.svg](docs/images/headings/windows/s03.svg)|説明文から参照する `s03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/s04.svg](docs/images/headings/windows/s04.svg)|説明文から参照する `s04` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/s05.svg](docs/images/headings/windows/s05.svg)|説明文から参照する `s05` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/s06.svg](docs/images/headings/windows/s06.svg)|説明文から参照する `s06` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/windows/title.svg](docs/images/headings/windows/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/wsl/01.svg](docs/images/headings/wsl/01.svg)|説明文から参照する `01` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/wsl/02.svg](docs/images/headings/wsl/02.svg)|説明文から参照する `02` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/wsl/03.svg](docs/images/headings/wsl/03.svg)|説明文から参照する `03` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/headings/wsl/title.svg](docs/images/headings/wsl/title.svg)|説明文から参照する `title` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/images/logo.png](docs/images/logo.png)|説明文から参照する `logo` の画面・カード・ロゴ画像。参照を確認済み。|画像リソース|説明・参照に必要|
|[docs/meltype.jpg](docs/meltype.jpg)|OSパッケージ・画面で使う `meltype` の画像リソース。|画像リソース|説明・参照に必要|
|[docs/romaji-azik-sample.txt](docs/romaji-azik-sample.txt)|ローマ字表とかな変換の開発・比較・配布を補助する。|108有効行|説明・参照に必要|

### ChromeOS試験版の追加（2026-10-21）

|ファイル|役割|主要要素|保持理由|
|---|---|---|---|
|[.github/workflows/chromeos.yml](.github/workflows/chromeos.yml)|Linux runnerでWASM Clippy・固定版生成ツールのハッシュ検査・全ChromeOS試験・拡張ZIPのartifact保存を行う。実ChromeOSを起動するジョブではない。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/.gitignore](chromeos/.gitignore)|再生成するwasm-bindgenのpkg/を追跡から除外する。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/README.md](chromeos/README.md)|2023年の廃止予定と現在のAPI定義、必要ツール、拡張の導入、接続済み機能と実機未確認の制約を説明する。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/background.mjs](chromeos/background.mjs)|MV3 Service Workerの入口。IME listenerを同期登録し、WASMとlocal storageの初期化、設定画面の許可された要求、周辺文を接続する。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/build.py](chromeos/build.py)|Rust/WASMビルド、版を固定したバインディング生成、全Node試験、依存ライセンス本文・SHA256・ZIP作成を行う。|run, main|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/controller.mjs](chromeos/controller.mjs)|IMEの非同期キーを直列化し、フォーカス世代を検査してCore結果をcommit/composition/候補APIへ配送。一度だけのackと学習保存を管理する。|ImeController|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/keys.mjs](chromeos/keys.mjs)|DOMの物理codeとUnicode keyを既存Coreの仮想キー・修飾ビットへ変換する。OEM/JIS対応付けを含むがJIS実機確認は別。|translateKey|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/manifest.json](chromeos/manifest.json)|ChromeOSのinputとstorage権限、単一レイアウトのIME、MV3 worker、WASM専用CSPと設定画面を宣言する。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/options.css](chromeos/options.css)|設定・診断画面の余白、文字、ボタン、キーボードフォーカスと狭い画面での配置を定義する。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/options.html](chromeos/options.html)|状態表示、全Core設定のJSON編集・保存と取消、学習削除、データ書き出し、未対応項目を表示する。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/options.mjs](chromeos/options.mjs)|設定画面から許可された要求をworkerへ送り、JSON構文の検査・ローカル書き出し・学習削除の確認とエラー表示を行う。|request, show, reload|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/package.json](chromeos/package.json)|ES modulesとNode標準テストの実行コマンドを宣言。npmの外部依存はない。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/tests/background.test.mjs](chromeos/tests/background.test.mjs)|本物のService Workerモジュールを模擬Chromeで読み、API不在の状態応答、要求元拒否、同期listener登録と実WASMへのキー配送を検査する。|event, baseChrome|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/tests/controller.test.mjs](chromeos/tests/controller.test.mjs)|キー対応、UTF-16候補位置、非同期ack、初期化中のフォーカス移動、パスワード拒否、API失敗と学習保存禁止を検査する。|fixture|ChromeOSホスト・共有Core・回帰/配布に必要|
|[chromeos/tests/wasm.test.mjs](chromeos/tests/wasm.test.mjs)|実WASMのFST候補、選択学習・復元・削除、学習禁止、設定移行・未知項目、F6/F7とEnterを検査する。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[rust/browser/Cargo.toml](rust/browser/Cargo.toml)|filesystemを無効にした共通Coreと固定wasm-bindgenを依存に持つcdylib/rlib crateを定義する。|宣言・設定・文書|ChromeOSホスト・共有Core・回帰/配布に必要|
|[rust/browser/src/lib.rs](rust/browser/src/lib.rs)|BrowserSessionとして共通Sessionを公開し、キー・候補選択・周辺文・フォーカス・設定・スナップショットを橋渡しする。OS削除と括弧内移動を制限する。|new, key, select, select_prediction, view, reset, focus, context, configure, snapshot, clear_learning, validate|ChromeOSホスト・共有Core・回帰/配布に必要|
|[rust/core/src/storage/state.rs](rust/core/src/storage/state.rs)|デスクトップ/ブラウザーで同じState・学習変更・保持数制限・学習削除処理を共有する。旧storage.rsから動作を変えず抽出した。|active, rebuild_language_preferences, remember_language, apply, remove_learned, forget_since, clear_learning|ChromeOSホスト・共有Core・回帰/配布に必要|
|[rust/core/src/storage_memory.rs](rust/core/src/storage_memory.rs)|filesystemなしで同じ学習型を公開する入口。StoreやSQLiteの代用品を偽装せず、永続化をホストに委ねる。|now|ChromeOSホスト・共有Core・回帰/配布に必要|

### ChromeOS導入画面・個人情報の黒塗り（2026-10-21）

|ファイル|役割|保持理由|
|---|---|---|
|[chromeos/docs/INSTALLATION.md](chromeos/docs/INSTALLATION.md)|実機の設定経路、入力方法の追加と選択、画像の黒塗り範囲と実機検証の限界。|導入案内|
|[chromeos/docs/screenshots/add-input-method.png](chromeos/docs/screenshots/add-input-method.png)|入力方法追加の未選択画面。SSID・アカウント表示を黒塗り済み、メタデータなし。|公開許可のある導入参考画像|
|[chromeos/docs/screenshots/add-input-selected.png](chromeos/docs/screenshots/add-input-selected.png)|Meltype Rustを選び追加する画面。SSID・アカウント表示を黒塗り済み、メタデータなし。|公開許可のある導入参考画像|
|[chromeos/docs/screenshots/input-methods.png](chromeos/docs/screenshots/input-methods.png)|追加前の入力方法一覧。SSID・アカウント表示を黒塗り済み、メタデータなし。|公開許可のある導入参考画像|
|[chromeos/docs/screenshots/device-settings.png](chromeos/docs/screenshots/device-settings.png)|デバイス設定の入口。SSID・アカウント表示を黒塗り済み、メタデータなし。|公開許可のある導入参考画像|
|[chromeos/docs/screenshots/keyboard-settings.png](chromeos/docs/screenshots/keyboard-settings.png)|キーボードと入力の設定。SSID・アカウント表示を黒塗り済み、メタデータなし。|公開許可のある導入参考画像|
|[chromeos/docs/screenshots/keyboard-input-options.png](chromeos/docs/screenshots/keyboard-input-options.png)|入力設定への入口。SSID・アカウント表示を黒塗り済み、メタデータなし。|公開許可のある導入参考画像|
|[chromeos/docs/screenshots/network-settings.png](chromeos/docs/screenshots/network-settings.png)|ネットワーク設定。SSIDは中央も除去。SSID・アカウント表示を黒塗り済み、メタデータなし。|公開許可のある導入参考画像|
