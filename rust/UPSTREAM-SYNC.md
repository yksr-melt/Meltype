# Sync fork と Meltype-FLASH

Flashの製品ブランチは `rust`。GitHubの既定ブランチも `rust` を使用する。
`main` にも確認済みのFlashを取り込む。どちらもRustの起動先・ブランド・IME登録IDを維持する。

## GitHubのSync forkを使う場合

1. `upstream/main` を作業ブランチへマージする。
2. 競合を解決し、Flashの識別検査とRustのテストを実行する。
3. `rust` 向けのPull Requestを作り、**merge commit** で取り込む。
4. 確認した `rust` を `main` にも取り込む。

本家の親コミットを保持するため、同期PRにはsquash/rebaseを使わない。
GitHubのSync forkが競合を報告した場合も、同じ手順でPRから取り込む。

競合時のDiscard commitsや、`--force` による上書きを製品ブランチで使用しない。
通常の同期で競合が出た場合はPull Requestで解決する。

CLIで作業ブランチに取り込む場合:

```powershell
git fetch upstream
git switch -c sync/upstream origin/rust
git merge upstream/main
python tools/check-flash-identity.py
cargo test --workspace --locked
git rev-list --left-right --count HEAD...upstream/main
```

`--force` は付けない。PCへのインストールやリリース公開は、この同期とは別に行う。

## 戻りを防ぐ仕組み

- `rust` と `main` で `Flash identity` 検査を実行する。
- 製品ブランチの強制push・削除を禁止する。
- 検査ではFlashのREADME、起動EXE名、専用IME登録ID、保存先、配布入口を確認する。
- 上流のC#ビルド／配布／.NET更新／winget／Web／実IME検査は元リポジトリに限定する。
- Flashのリリースは手動実行のみ。同期や通常pushから勝手に配布しない。
- 参照用C#コードはGitHubの言語集計から除外し、比較用ソースとして保持する。

上流のコミットでワークフローや表示が変わっても、製品検査で検出してから取り込む。
権限を持つ管理者が保護を無効化・迂回した場合まで、この仕組みで防げるとはしない。

Sync forkはGitHub上のソースの同期であり、PCにインストール済みのC#版をRust版へ変更する操作ではない。
Flashの実機導入は別の作業として行う。

## 2026-10-22の取り込み

本家 `ecb2ba1cb05777e82b3f0b1dcbbba753c0c0b182` までの未取り込み442コミットを統合した。
共有辞書、文書、本家C#実装、OS側の連携コードを更新し、FlashのブランドとRust連携を保持する。
入力中の候補列挙・低速学習による切断修正とChromeOSの確定失敗時の復旧も統合する。

履歴の同期とRustへの機能移植は別の作業である。本家C#で追加されたエンジン・設定などが
このマージだけですべてRustに実装されたとはしない。Rustの実装状況は
[移行記録](MIGRATION.md) と [ビルド・制限](README.md) を参照する。

## 2026-10-30の1.4.0対応

本家v1.4.0 `46db6ca2f941eb9580f4d5ed103ee0bee71111c7` を固定し、機能・入力・設定をRustへ移植した。ChromeOSの半角/全角切替と、ChromeOS・iOS・AndroidのAI設定除去も含む。[対応内容と検証範囲](UPGRADE-1.4.0.md)を参照する。履歴全体のマージや全OSの実機確認とは区別する。

## 2026-11-09の1.5.0対応

本家v1.5.0 `36a7c0d9900dff1f41099be027af98bd072ea68a` を固定し、共通Coreへ予測・候補・入力の変更を移植した。最後の文節からの予測、定型句、版番号の除外、括弧の系統、Markdown記号、日付候補を含む。[対応内容と未移植の範囲](../docs/UPSTREAM-1.5.0-AUDIT.md)を参照する。履歴全体のマージや全OSの実機確認とは区別する。

## 2026-11-15の1.6.0対応

本家v1.6.0 `4bbc87e19091cf1f3a085e7098563ab60214ef5b` を固定し、共通Coreへ分野の辞書・国名の語・通貨の丸め・版の候補・Zenzaiの読み照合を移植した。分野の辞書は上流の `dictionaries/domain-*.txt` をそのまま同梱する。[対応内容と未移植の範囲](../docs/UPSTREAM-1.6.0-AUDIT.md)を参照する。履歴全体のマージや全OSの実機確認とは区別する。
