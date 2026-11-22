#注意
このプロジェクトはWindowsとChromeOS Linuxのみ実証済みで行っています。

# Meltype-FLASH

<!-- meltype-flash-platform:start -->
![Meltype-FLASH](assets/branding/readme/logo.svg)

<!-- meltype-flash-platform:end -->

<p align="center"><b>稲妻のように、言葉の壁を駆け抜ける日本語入力。</b></p>

本家 [Meltype](https://github.com/yksr-melt/Meltype) を参照して開発している、Rust版の日本語入力です。
Windows・Mac・Linux・Android・ChromeOS・iOSで共通の入力コアを使います。
**Flash 1.1.7です。環境によって変換エンジン・設定・学習・操作が異なります。本家C#版と全機能・精度が同じことを示す版番号ではありません。**

<p align="center">
<a href="https://github.com/appipinopi/Meltype-Flash/releases/latest"><b>ダウンロード</b></a> ·
<a href="#使い始める">使い方</a> ·
<a href="https://github.com/appipinopi/Meltype-Flash/issues/new/choose">不具合の報告・提案</a> ·
<a href="rust/README.md">実装・検証の状況</a>
</p>

<a id="できること"></a>
<h2><img src="assets/branding/readme/overview/headings/a331281f2141.svg" alt="できること" width="1000"></h2>

- **日本語と英語を混ぜて入力**：共通Coreがローマ字・かなと英単語を判定します。
- **変換して、候補を選ぶ**：かな・漢字候補、字種切替、予測などを各環境の入力方法へ接続しています。候補の内容は同梱辞書や接続先の変換エンジンで変わります。
- **使う環境に合わせる**：WindowsはTSF、MacはInput Method Kit、LinuxはIBus、モバイルはカスタムキーボードを使います。ChromeOSは専用の拡張機能です。
- **入力処理は端末内で**：通常の入力文字を外部の変換APIへ送る構成ではありません。保存と通信の範囲は各環境のREADMEに記載しています。

本家の紹介動画や変換例は本家の実装によるものです。Flashの確認には、次のように実際の候補と確定結果を確かめてください。

| 操作 | 確かめること |
| --- | --- |
| `konnnichiha` を入力して確定 | 「こんにちは」になるか |
| `hashi` を入力し、Spaceで変換 | 「橋」「箸」などの候補を選べるか |
| 英単語を含む文を入力 | 英語のまま残したい語を確認し、必要なら字種を切り替える |

<a id="インストール"></a>
<h2><img src="assets/branding/readme/overview/headings/6fb33e8c98c4.svg" alt="インストール" width="1000"></h2>

**カードを押すと、各環境の「動く環境・インストール・使い方・困ったとき」が開きます。**

<!-- meltype-flash-os-navigation:start -->
<p align="center">
  <a href="rust/WINDOWS-PACKAGE.md"><img src="assets/branding/readme/cards/os-windows.svg" alt="Windows" width="32%"></a>
  <a href="linux/README.md"><img src="assets/branding/readme/cards/os-linux.svg" alt="Linux" width="32%"></a>
  <a href="mac/README.md"><img src="assets/branding/readme/cards/os-mac.svg" alt="Mac" width="32%"></a>
</p>
<p align="center">
  <a href="android/README.md"><img src="assets/branding/readme/cards/os-android.svg" alt="Android" width="32%"></a>
  <a href="chromeos/README.md"><img src="assets/branding/readme/cards/os-chromeos.svg" alt="ChromeOS" width="32%"></a>
  <a href="ios/README.md"><img src="assets/branding/readme/cards/os-ios.svg" alt="iOS" width="32%"></a>
</p>
<!-- meltype-flash-os-navigation:end -->

このソースに対応するリリースは [v1.1.7](https://github.com/appipinopi/Meltype-Flash/releases/tag/flash-v1.1.7) です。
版番号は`1.1.7`です。ChromeOSの半角/全角キーを修正し、非同期変換（Windows TSF・IBus・macOS）、SKKライク入力の設定、ReadingLatticeの精度改善、Gamma・Qwen・ShionのAIモデル追加、ZenzaiのGPU設定を含みます。今回はリリースCIで上流C#・前版・今回のRustを比較するベンチマークを実行し、結果をリリース本文へ掲載します。[変更と確認範囲](rust/RELEASE-NOTES.md)を参照してください。iOSの実機導入には署名が必要です。AIモデルは本体と別ダウンロードです。公開状況と配布物は上のリリースページで確認してください。
[配布ファイルの選び方](rust/DOWNLOADS.md) で、OSとCPUに合うファイルを選んでください。
ZIP・APK・IPAは互換ではありません。本家のC#版の配布ファイルをFlash用として案内していません。

| 環境 | はじめの一歩 |
| --- | --- |
| Windows | `Meltype-flash-Windows-x64.zip`を展開し、`Manage-Windows.ps1`で導入。登録不要の試用も同梱。 |
| Mac | CPUに合うmacOS ZIPを全体展開し、`Install Meltype.command` を実行。 |
| Linux | Linux x64向けtar.gzを展開して導入し、IBusへ追加。 |
| Android | 開発用APKを導入し、システムのキーボード設定で有効化。 |
| ChromeOS | ZIPを展開し、拡張機能として読み込んで入力方法に追加。 |
| iOS | 未署名IPAを自分で署名、またはXcodeでビルドしてキーボードを追加。 |

<a id="使い始める"></a>
<h2><img src="assets/branding/readme/overview/headings/f02cecb06ef8.svg" alt="使い始める" width="1000"></h2>

インストール後は、そのOSの入力方法としてMeltype-FLASHを選びます。設定画面だけを起動しても、IMEの登録やOS側の切替は完了しません。

- **Space**：変換・候補の選択。
- **Enter**：確定。
- **Esc**：変換や未確定入力の取消。ソフトキーボードでは各画面のボタンを使います。
- モード切替・文節操作・学習など、細かな対応は各環境の「使い方」を参照してください。

iOSでは未確定文字をキーボード内に表示し、確定後に入力欄へ送ります。Windowsのポータブル入力も、登録したIMEとは対応範囲が異なります。

<a id="よくある質問"></a>
<h2><img src="assets/branding/readme/overview/headings/67c5c49ab38b.svg" alt="よくある質問" width="1000"></h2>

<details>
<summary>本家Meltypeと何が違いますか？</summary>

本家のC#実装を参照し、Rustの共通Coreと各OSのホストを接続しているフォークです。製品ブランチは `rust`、設定アプリの実行ファイルは `meltype-flash.exe` です。C#の `src/` は比較用の参照コードとして残しています。
[名称・登録ID・保存先の方針](rust/BRANDING.md) と [移行記録](rust/MIGRATION.md) を参照してください。

</details>

<details>
<summary>ファイルを開いても入力できません</summary>

OSごとの登録と入力方法の選択が必要です。Macの `Meltype.app` は通常のアプリのようにダブルクリックして使うものではありません。ChromeOS版は通常のWindows/macOS/LinuxのChromeではIMEとして動きません。iOSのSimulator用ZIPはiPhone実機に入りません。
各カードから、その環境の手順を確認してください。

</details>

<details>
<summary>変換結果がおかしい・候補が少ない</summary>

環境によって外部変換エンジンと同梱辞書が異なります。Linuxの公開IBus版、Android、ChromeOS、iOSは補助辞書による制限があります。
[Issues](https://github.com/appipinopi/Meltype-Flash/issues/new/choose) に、OS・版・アプリ・入力したキー・期待した結果・実際の結果を添えてください。パスワードや個人の文章は載せないでください。

</details>

<a id="プライバシー"></a>
<h2><img src="assets/branding/readme/overview/headings/733cbe4ad684.svg" alt="プライバシー" width="1000"></h2>

入力と変換の基本処理は端末内で行います。通常のデスクトップ入力やAndroidでは設定・学習・辞書を保存します。Windowsのポータブル入力は学習の永続保存に対応していません。ChromeOSでは拡張機能のローカルストレージを使います。iOSプレビューでは学習・設定の永続保存は無効です。
自動更新・不具合報告・サイト別切替に必要な権限などは、各環境のREADMEで説明しています。
セキュリティ連絡は [SECURITY.md](SECURITY.md) を参照してください。

<a id="開発者向け"></a>
<h2><img src="assets/branding/readme/overview/headings/1a30f6f268b2.svg" alt="開発者向け" width="1000"></h2>

Rust 1.88以上。WindowsはMSVC Build Tools、MacはXcode、LinuxはIBusなどの開発用ライブラリが必要です。
リポジトリのルートから、共通CoreとWindows設定画面を次のように確認できます。

```sh
cargo test -p meltype-core --locked
cargo run -p meltype-settings
# 管理トレイを起動する場合
cargo run -p meltype-settings -- --tray
```

Windowsの起動先は **meltype-flash.exe** です。各OSのビルドは、各READMEの「開発者向け」と [Rust版の構成](rust/README.md) を参照してください。

- [画面の再現状況](rust/UI-V0.1.1.md)
- [アプリ別設定](rust/APP-RULES.md)
- [上流の取り込み方針](rust/UPSTREAM-SYNC.md)
- [貢献の手順](CONTRIBUTING.md)

<a id="ライセンス協力"></a>
<h2><img src="assets/branding/readme/overview/headings/e30b57a30d65.svg" alt="ライセンス・協力" width="1000"></h2>

[GPL-3.0-or-later](LICENSE)。著作権表示・第三者ライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。
元のMeltypeと、その開発・辞書・検証に協力された方々に感謝します。[本家のREADME](https://github.com/yksr-melt/Meltype#readme) で本家の協力者と活動を確認できます。
FlashのM＋稲妻ロゴとREADME画像は [画像資産の説明](assets/branding/README.md) にまとめています。

CIの役割と手動実行は [CIの構成](docs/CI.md) を参照してください。
