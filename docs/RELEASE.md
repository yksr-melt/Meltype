<a name="リリースの手順"></a>
<img src="images/headings/release/title.svg" alt="リリースの手順" height="80">


<br>

<a name="版を出す"></a>
<img src="images/headings/release/01.svg" alt="版を出す" height="53"><br>


1. 版を上げる
   - `src/Meltype/Meltype.csproj`・`src/Meltype.Core/Meltype.Core.csproj` の `<Version>`
   - `mac/Resources/Info.plist` の `CFBundleShortVersionString` と `CFBundleVersion` (Mac 版の表示の版は Info.plist から読みます)
2. コミットして main に入れ、タグを付けて push する: `git tag v1.1.1 && git push origin v1.1.1`
3. GitHub Actions が Windows (build.yml)・Mac (mac.yml)・Linux (linux.yml) の配布物を作り、リリースに付ける
   - Windows: `Meltype-<版>-setup.exe` (インストーラー) と `Meltype-<版>-windows.zip`
   - Mac: `Meltype-<版>-mac.zip`、Linux: `Meltype-<版>-linux.zip` (どちらもプレビュー版)
4. リリースのページにリリースノートを書く
5. 使っている人の Meltype (Windows) は、自動で新しい版に更新されます

<br>

<a name="同梱の-net-の更新"></a>
<img src="images/headings/release/02.svg" alt="同梱の .NET の更新" height="53"><br>


配布物の .NET の版は、どこにも固定していません。ビルドのたびに setup-dotnet (`10.0.x`) がその時の最新の SDK とランタイムを入れます。
Windows は `Build-Package.ps1` が、入っている最新の 10.0 のランタイムを `app\dotnet` に同梱し、Mac・Linux は NativeAOT で組み込みます。
なので、.NET に新しいパッチ (セキュリティの修正など) が出たら、コードを変えずに版を上げてリリースし直せば済みます。

`.github/workflows/dotnet-update.yml` が毎週水曜日に、最新のリリースの zip に入っている版と、Microsoft が公開している最新の版を比べます。
新しい版が出ていれば「同梱の .NET を <版> に更新する (リリースし直す)」の Issue を立てます (Actions の画面から手で実行することもできます)。
.NET のメジャー版を上げる (net10.0 → net11.0) ときは、`*.csproj` の `TargetFramework` と、ワークフローの `dotnet-version` を手で変えます。

<br>

<a name="github-の設定"></a>
<img src="images/headings/release/03.svg" alt="GitHub の設定" height="53"><br>


- Settings → Security → **Private vulnerability reporting** を ON にする ([SECURITY.md](../SECURITY.md) の報告先)
- Settings → Branches → main のブランチ保護で、必須のチェックに **CLA**・**build**・**精度の比較**・**辞書の形式** を入れる
- (任意) bot の名前を変える: GitHub App を作り、変数 `BOT_APP_ID` と秘密 `BOT_APP_PRIVATE_KEY` を登録する
- 不具合報告のフォーム: [tools/report-form/README.md](../tools/report-form/README.md) の手順で作り、`src/Meltype.Core/Config/ProjectInfo.cs` の `ReportForm` に URL を書く

<br>

<a name="パッケージマネージャー"></a>
<img src="images/headings/release/04.svg" alt="パッケージマネージャー" height="53"><br>


リリースの zip から、winget・Scoop・Homebrew のマニフェストを作れます。

```
node tools/make-manifests.mjs 1.2.0 dist/Meltype-1.2.0-windows.zip dist/Meltype-1.2.0-mac.zip dist/Meltype-1.2.0-setup.exe
```

`dist/manifests/` にできたものを出します。

- **winget**: `winget/manifests/...` を [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) に Pull Request で出す (`winget validate` で確かめてから)。審査があり、署名の無い実行ファイルは止められることがあります
  - setup.exe を渡すと、インストーラー (このユーザーだけに入れる・管理者権限なし) のマニフェストになります。Meltype IME・起動時の起動が入り、Meltype の自動更新と同じ場所に入るので、こちらを使います (渡さなければ zip の portable)
  - **2 つ目の版からは自動**: 最初の版が取り込まれたら、`microsoft/winget-pkgs` をフォークし、`public_repo` の権限のトークンを秘密 `WINGET_TOKEN` に登録します。以後はリリースを公開するたびに `.github/workflows/winget.yml` が新しい版の Pull Request を出します (`WINGET_TOKEN` が無ければ何もしません)。使う人は `winget install Yukishiro.Meltype` / `winget upgrade Yukishiro.Meltype`
- **Scoop**: 自分のバケット (例: `yksr-melt/scoop-bucket`) を作り、`scoop/meltype.json` を `bucket/` に置く。使う人は `scoop bucket add yksr-melt https://github.com/yksr-melt/scoop-bucket` → `scoop install meltype`
- **Homebrew**: 自分の tap (例: `yksr-melt/homebrew-tap`) を作り、`homebrew/Casks/meltype.rb` を置く。使う人は `brew install --cask yksr-melt/tap/meltype`

winget・Scoop で入れた場合は Install.cmd を使わないので、Windows の起動時に起動するには、トレイの「Windows の起動時に起動」を ON にしてもらいます。
