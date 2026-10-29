<a name="リリースの手順"></a>
<img src="images/headings/release/title.svg" alt="リリースの手順" height="80">


<br>

<a name="版を出す"></a>
<img src="images/headings/release/01.svg" alt="版を出す" height="53"><br>


1. 版を上げる
   - `src/Meltype/Meltype.csproj`・`src/Meltype.Core/Meltype.Core.csproj` の `<Version>`
   - `mac/Resources/Info.plist` の `CFBundleShortVersionString` と `CFBundleVersion` (Mac 版の表示の版は Info.plist から読みます)
   - `docs/release-notes/<版>.md` にリリースノートを書く (例: `docs/release-notes/1.4.0.md`)。配布物に同梱し、更新したあとの初めての起動で Meltype (Windows) が見せる。GitHub のリリースの本文にも同じものを使う。タグのビルドでは、このファイルが無ければ Windows 版のビルドが止まる
2. コミットして main に入れ、タグを付けて push する: `git tag v1.1.1 && git push origin v1.1.1`
3. GitHub Actions が Windows (build.yml)・Mac (mac.yml)・Linux (linux.yml) の配布物を作り、リリースに付ける
   - Windows: `Meltype-<版>-setup.exe` (インストーラー)、`Meltype-<版>-windows.zip`、`Meltype-<版>-windows-portable.zip`
   - Mac: `Meltype-<版>-mac.zip`、Linux: `Meltype-<版>-linux.zip` と `meltype_<版>_amd64.deb`、ARM64 の `Meltype-<版>-linux-arm64.zip` と `meltype_<版>_arm64.deb`、Fedora 向けの `meltype-<版>-1.x86_64.rpm` と `meltype-<版>-1.aarch64.rpm` (どれもプレビュー版。.deb と .rpm の版は `1.2.2-pre.2` なら `1.2.2~pre.2`)
   - Mac はタグのとき、Homebrew Cask の更新 PR を作ります。PR を main に入れると、このリポジトリの `Casks/meltype.rb` が更新されます
4. リリースのページの本文が `docs/release-notes/<版>.md` と同じか確かめる (bot を使うと、そのファイルを本文にした下書きまで自動で作ります。下の「bot でリリースする」)。リリースのページだけ書き直すと、アプリで見せる本文と食い違うので、直すときはファイルも直す
5. 使っている人の Meltype (Windows) は、自動で新しい版に更新されます

Windows の [Code signing policy・SignPath のテスト署名の設定](CODE-SIGNING.md)。現時点ではテスト用の独立した経路で、上の配布手順は変えていません。本番署名の必須化は、本番証明書の有効化を確認した後に別途行います。

### bot でリリースする

版を上げて main に入れたあと、Issue か Pull Request のコメントの 1 行目に、管理者 (admin / maintain) が次のように書きます。

- `/release 1.2.3` … 通常の版
- `/pre-release 1.2.3-pre.1` … プレリリース

bot は、main の `Meltype.csproj`・`Meltype.Core.csproj`・`Info.plist` の版が書いた版と合っているかを確かめ (版の上げ忘れを防ぐため、合っていなければ止めます)、`docs/release-notes/<版>.md` があるかを確かめ (無ければ止めます)、タグ `v1.2.3` とリリースの下書き (本文はそのファイル) を作って、Windows・Mac・Linux のビルドを始めます。ビルドが終わると下書きに配布物が付くので、中身を確かめてから公開します。

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


リリースの zip から、winget・Scoop・Homebrew のマニフェストを作れます。Linux の .deb と .rpm は linux.yml が作ります。

```
node tools/make-manifests.mjs 1.2.0 dist/Meltype-1.2.0-windows.zip dist/Meltype-1.2.0-mac.zip dist/Meltype-1.2.0-setup.exe
```

`dist/manifests/` にできたものを出します。

- **winget**: `winget/manifests/...` を [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) に Pull Request で出す (`winget validate` で確かめてから)。審査があり、署名の無い実行ファイルは止められることがあります
  - setup.exe を渡すと、インストーラー (このユーザーだけに入れる・管理者権限なし) のマニフェストになります。Meltype IME・起動時の起動が入り、Meltype の自動更新と同じ場所に入るので、こちらを使います (渡さなければ zip の portable)
  - **2 つ目の版からは自動**: 最初の版が取り込まれたら、`microsoft/winget-pkgs` をフォークし、`public_repo` の権限のトークンを秘密 `WINGET_TOKEN` に登録します。以後はリリースを公開するたびに `.github/workflows/winget.yml` が新しい版の Pull Request を出します (`WINGET_TOKEN` が無ければ何もしません)。使う人は `winget install Yukishiro.Meltype` / `winget upgrade Yukishiro.Meltype`
- **Scoop**: 自分のバケット (例: `yksr-melt/scoop-bucket`) を作り、`scoop/meltype.json` を `bucket/` に置く。使う人は `scoop bucket add yksr-melt https://github.com/yksr-melt/scoop-bucket` → `scoop install meltype`
- **Homebrew Cask**: タグのリリース時に `Casks/meltype.rb` の更新 PR が自動で作られます。PR を main に入れると、新しい版をこのリポジトリからインストールできます。このリポジトリを tap として登録します:
  ```bash
  brew tap yksr-melt/Meltype https://github.com/yksr-melt/Meltype.git
  brew install --cask yksr-melt/Meltype/meltype
  ```
  次回から `brew upgrade --cask meltype` で更新できます。Mac の zip から cask を作る場合は `node tools/make-manifests.mjs 1.2.0 dist/Meltype-1.2.0-mac.zip` です
  - cask は Meltype.app を `~/Library/Input Methods` に入れ、`mac/install.sh` と同じく、隔離属性を外す・入力ソースに登録する・GUI のセッションで起動するところまでします。`brew upgrade` で更新、`brew uninstall --cask meltype` で外せます (`--zap` で設定と学習データも)
  - 署名と公証 (mac.yml の秘密) が無い版は、自分用の署名 (ad hoc) です。cask は隔離属性を外しますが、Homebrew の本家 (homebrew/cask) には出せません (自分の tap なら使えます)
- **apt (.deb)**: linux.yml が amd64 / arm64 の `.deb` を作り、リリースに付けます。署名鍵を登録すると、署名付き apt リポジトリを GitHub Pages に自動公開します。
  - 初回だけ GitHub の Settings → Pages → Build and deployment → Source を「GitHub Actions」にします
  - Settings → Environments → `github-pages` → Deployment branches and tags で、タグのルール `v*` を許可します (リリースタグからの公開に必要です)
  - Linux / WSL で GPG 鍵を作り、できた Base64 の文字列を Actions secret `APT_SIGNING_KEY_B64` に登録します。秘密鍵はコミットしません:
    ```bash
    gpg --batch --pinentry-mode loopback --passphrase '' --quick-generate-key "Meltype APT repository" rsa4096 sign 0
    fingerprint="$(gpg --batch --with-colons --list-secret-keys "Meltype APT repository" | awk -F: '$1 == "fpr" { print $10; exit }')"
    gpg --batch --pinentry-mode loopback --passphrase '' --armor --export-secret-keys "$fingerprint" | base64 -w0 > meltype-apt-signing-key.b64
    ```
    GitHub Settings → Secrets and variables → Actions で `APT_SIGNING_KEY_B64` を作り、ファイルの内容を登録します
  - 使う人は、次の設定を一度行うと `sudo apt update && sudo apt upgrade` で更新できます:
    ```bash
    sudo install -d -m 0755 /etc/apt/keyrings
    curl -fsSL https://yksr-melt.github.io/Meltype/apt/meltype-archive-key.asc | sudo tee /etc/apt/keyrings/meltype-archive-key.asc >/dev/null
    sudo chmod 0644 /etc/apt/keyrings/meltype-archive-key.asc
    sudo tee /etc/apt/sources.list.d/meltype.sources >/dev/null <<'EOF'
    Types: deb
    URIs: https://yksr-melt.github.io/Meltype/apt
    Suites: stable
    Components: main
    Architectures: amd64 arm64
    Signed-By: /etc/apt/keyrings/meltype-archive-key.asc
    EOF
    sudo apt update
    sudo apt install meltype
    ```
- **dnf (.rpm)**: linux.yml が x86_64 / aarch64 の `.rpm` を Fedora 44 で作り、リリースに付けます。apt と同じ署名鍵 (`APT_SIGNING_KEY_B64`) で署名した dnf リポジトリを、apt リポジトリと一緒に GitHub Pages の `/rpm` に公開します (Pages の設定は apt と共通です)。
  - 使う人は `sudo dnf config-manager addrepo --from-repofile=https://yksr-melt.github.io/Meltype/rpm/meltype.repo` を一度行うと、`sudo dnf upgrade` で更新できます


winget・Scoop で入れた場合は Install.cmd を使わないので、Windows の起動時に起動するには、トレイの「Windows の起動時に起動」を ON にしてもらいます。
