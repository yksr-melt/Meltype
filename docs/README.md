# Meltype の文書

使い方・インストールと開発資料の入口です。文書は main の実装に合わせて更新します。公開済みの版の変更点は [リリースノート](release-notes/) を参照してください。

## 使う

| 目的・環境 | 文書 |
| --- | --- |
| デスクトップ版のキー操作・判定・変換・辞書・学習 | [共通の使い方](USAGE.md) |
| Windows: Meltype IME / Meltype キーボード | [Windows](WINDOWS.md) |
| macOS: 入力ソース | [Mac](MACOS.md) |
| Linux: IBus / fcitx5、apt / dnf | [Linux](LINUX.md) |
| Linux: GUI のない端末・pty | [TTY](TTY.md) |
| Android: QWERTY ソフトウェアキーボード | [Android](ANDROID.md) |
| 通信・保存するデータ・脆弱性の報告 | [セキュリティ](../SECURITY.md) |

Mac・Linux・Android はプレビュー版です。Android の APK は、デスクトップ版とは別の `android-v<版>` のプレリリースで配布します。

## 開発・配布する

| 目的 | 文書 |
| --- | --- |
| 構成・ビルド・テスト・Windows のパッケージ | [開発](dev/DEVELOPMENT.md) |
| Windows の TSF・プロセス・通信・DLL 更新 | [Meltype IME の設計](dev/TSF_DESIGN.md) |
| macOS のビルド・入力ソースの登録 | [mac/README.md](../mac/README.md) |
| Linux のビルド・パッケージ | [linux/README.md](../linux/README.md) |
| TTY 版のビルド | [tui/README.md](../tui/README.md) |
| Android の実装・ビルド | [android/README.md](../android/README.md) |
| Android の署名・公開 | [Android の署名と配布](../android/docs/SIGNING-DISTRIBUTION.md) |
| Mozc ヘルパー・辞書の組み込み | [native/mozc/README.md](../native/mozc/README.md) |
| 任意の Mozc-UT 辞書 | [Mozc-UT](dev/MOZC-UT.md) / [英語版](dev/MOZC-UT.en.md) |
| 性能計測 | [tools/performance/README.md](../tools/performance/README.md) |
| ブラウザーでの実 IME テスト | [tools/ime-tests/README.md](../tools/ime-tests/README.md) |
| 公式サイト・報告フォーム・ガイドの生成 | [web/README.md](../web/README.md) |
| 版を出す・パッケージマネージャー | [リリース手順](dev/RELEASE.md) |
| Windows の署名 | [コード署名](CODE-SIGNING.md) / [SignPath のテスト設定](SIGNPATH-SETUP.md) |
| 貢献・PR・bot の使い方 | [CONTRIBUTING.md](../CONTRIBUTING.md) |

## 文書を更新するとき

挙動の根拠は [設定の定義](../src/Meltype.Core/Config/Settings.cs)・[共通の入力プロトコル](../src/Meltype.Core/Ime/ImeProtocol.cs)・各 OS の実装です。配布物と CI は [ワークフロー](../.github/workflows/)・[パッケージのスクリプト](../packaging/) を確認してください。

構造や処理順序の図は Mermaid、環境ごとの対応や操作の比較は表で記述します。公式サイトに転載する 4 ページ (`USAGE.md`・`WINDOWS.md`・`MACOS.md`・`LINUX.md`) は現在 Mermaid を描画しないため、見出し・表・手順で説明します。確認方法は [開発文書](dev/DEVELOPMENT.md#変更後の確認) にあります。

過去のリリースノートと実測値は、その版・計測環境の記録として扱います。最新仕様に合わせるために、過去の記録の意味を書き換えないでください。
