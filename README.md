<p align="center">
  <img src="docs/images/logo.png" alt="Meltype" width="480"><br>
  <sub>Logo by <a href="https://github.com/Crysta1221">@Crysta1221</a></sub>
</p>

<p align="center"><b>雪解けのように、半角/全角の壁を溶かす日本語入力。</b></p>

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/releases/latest"><img src="https://img.shields.io/github/v/release/yksr-melt/Meltype?color=5ec4f0" alt="release"></a>
  <a href="https://github.com/yksr-melt/Meltype/releases"><img src="https://img.shields.io/github/downloads/yksr-melt/Meltype/total?color=ff8ab4" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-7a96f0" alt="license"></a>
  <a href="https://discord.gg/y44wBbBgHt"><img src="https://img.shields.io/badge/Discord-参加する-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
  <a href="https://github.com/sponsors/yksr-melt"><img src="https://img.shields.io/badge/Sponsor-応援する-ff8ab4?logo=githubsponsors&logoColor=white" alt="GitHub Sponsors"></a>
</p>

<p align="center" style="display: flex; justify-content: center; gap: 12px; flex-wrap: wrap;">
  <a href="https://trendshift.io/repositories/284759?utm_source=trendshift-badge&utm_medium=badge&utm_campaign=badge-trendshift-284759" target="_blank" rel="noopener noreferrer">
    <img src="https://trendshift.io/api/badge/trendshift/repositories/284759/daily?language=C%23" alt="yksr-melt%2FMeltype | Trendshift" width="250" height="55"/>
  </a>
  <a href="https://trendshift.io/repositories/284759?utm_source=trendshift-badge&utm_medium=badge&utm_campaign=badge-trendshift-284759" target="_blank" rel="noopener noreferrer">
    <img src="https://trendshift.io/api/badge/trendshift/repositories/284759/weekly?language=C%23" alt="yksr-melt%2FMeltype | Trendshift" width="250" height="55"/>
  </a>
</p>

<h3 align="center">半角/全角 キーは、もう押さなくていい！</h3>

<p align="center">
ローマ字で打つだけ。日本語か英語かは Meltype が見分けて、その場で切り替えます！
</p>

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/releases/latest"><b>ダウンロード</b></a>
  &nbsp;·&nbsp;
  <a href="docs/USAGE.md">使い方</a>
  &nbsp;·&nbsp;
  <a href="https://github.com/yksr-melt/Meltype/issues/new/choose">不具合の報告・提案</a>
  &nbsp;·&nbsp;
  <a href="https://discord.gg/y44wBbBgHt">Discord</a>
  &nbsp;·&nbsp;
  <a href="https://github.com/sponsors/yksr-melt">応援する</a>
</p>

<p align="center">
  <img src="docs/images/demo.svg" alt="kyouhagoogledekensaku と打つと、今日はgoogleで検索 になる" width="640">
</p>

<br><br>

<img src="docs/images/headings/features.svg" alt="できること" width="480"><br>

<p align="center">
  <img src="docs/images/features.svg" alt="混ぜたまま打てる / コードの手も止めない / ぜんぶ PC の中で" width="100%">
</p>

<a name="たとえば"></a>
<img src="docs/images/headings/readme/s01.svg" alt="たとえば" height="40">


```
kyouhagoogledekensaku    →  今日はgoogleで検索
ashitanomeetingwotsuika  →  明日のmeetingを追加
I want to go to the park →  I want to go to the park
```

英単語は英字のまま、日本語はかな・漢字に。英文だってそのまま入ります！
VS Code やターミナルでは英数が基本で、コメントと文字列の中だけ日本語に。AI エージェントの `/command`・`$skill`・`@ファイル名` も、変換されずにそのまま入ります。

<a name="ほかにも"></a>
<img src="docs/images/headings/readme/s02.svg" alt="ほかにも" height="40">


えがお → 😊 の絵文字変換、ブレスレッド → ブレスレット のような書き間違いの指摘、予測変換、アプリごとの設定など。

<sub>Windows 10 / 11 に対応。Mac 版・Linux 版はプレビュー版です。</sub>

<br><br>

<img src="docs/images/headings/install.svg" alt="インストール" width="480"><br>

<p align="center">
  <a href="docs/WINDOWS.md"><img src="docs/images/cards/os-windows.svg" alt="Windows" width="30%"></a>
  <a href="mac/README.md"><img src="docs/images/cards/os-mac.svg" alt="Mac" width="30%"></a>
  <a href="linux/README.md"><img src="docs/images/cards/os-linux.svg" alt="Linux" width="30%"></a>
</p>

<p align="center"><sub>カードを押すと、それぞれの OS の README (インストール・使い方・困ったとき) が開きます</sub></p>

1. [Releases](https://github.com/yksr-melt/Meltype/releases/latest) から `Meltype-<version>-setup.exe` をダウンロード
2. 開いて、案内どおりに進める
3. タスクトレイに「あ」が出たら準備完了！

途中で Meltype IME を Windows に登録するための管理者権限の確認が出るので、「はい」を選んでください。断った場合は、変換ボックスで入力する Meltype キーボードで動きます。新しい版は自動で入ります。

「Windows によって PC が保護されました」と出たら、「詳細情報」→「実行」。コード署名をしていないため表示されます。

<details>
<summary>zip 版・Mac 版・Linux 版</summary>

| | ファイル |
|---|---|
| Windows (zip) | `Meltype-<version>-windows.zip` を展開して `Install.cmd` をダブルクリック |
| Mac (プレビュー版) | `Meltype-<version>-mac.zip` ([mac/README.md](mac/README.md)) |
| Linux (プレビュー版) | `Meltype-<version>-linux.zip` (ARM64 は `Meltype-<version>-linux-arm64.zip`。IBus / fcitx5、[linux/README.md](linux/README.md)) |

インストーラーでも zip でも、入る場所は同じ `%LOCALAPPDATA%\Programs\Meltype` です。
Windows 版に必要なのは Windows 10 / 11 (64bit) と Microsoft IME だけで、.NET は同梱しています。

</details>

<details>
<summary>「ウイルスを検出しました」と出たとき</summary>

SmartScreen の警告とは別のものです。誤検知のこともありますが、検出名だけでは見分けられません。

1. Windows セキュリティ →「ウイルスと脅威の防止」→「保護の更新」で定義を新しくする
2. 公式のリリースからダウンロードし直して、もう一度検査する
3. それでも検出されるときは、保護を切ったりフォルダーを除外したりせずに、「保護の履歴」で検出名を確かめて [Issues](https://github.com/yksr-melt/Meltype/issues) で教えてください。個人名やパス、ダウンロード URL の一時トークンは隠してください

ファイルが本物か確かめたいときは、リリースのページにある SHA-256 と比べられます。PowerShell なら `Get-FileHash .\Meltype-<version>-windows.zip` です。

</details>

<details>
<summary>アンインストール</summary>

トレイのアイコンを右クリックして「アンインストール...」を選ぶか、Windows の「設定」→「アプリ」から消せます。
インストーラーで入れた場合は、最後に設定と学習データも消すかを聞かれます。zip 版は `Uninstall.cmd` でも消せて、こちらは設定と学習データもまとめて消えます。

</details>

<br><br>

<img src="docs/images/headings/start.svg" alt="使い始める" width="480"><br>

あとは、いつもどおりローマ字で打つだけ！

- **Meltype IME**: 文字は入力欄に直接入り、候補は入力位置の下に出る。ほかの IME との切り替えは <kbd>Win</kbd> + <kbd>Space</kbd>
- **Meltype IME を入れなかった場合** (Meltype キーボード): カーソルの下に変換ボックスが出る

| キー | |
|---|---|
| <kbd>Enter</kbd> | 確定 |
| <kbd>Space</kbd> | 候補を出して選ぶ (4 文字以上は打つそばから漢字になる。短い語は Space で変換) |
| <kbd>F7</kbd> / <kbd>F10</kbd> | カタカナ / 英字 |
| <kbd>半角/全角</kbd> | 英数と日本語の切り替え |
| <kbd>Ctrl</kbd> + <kbd>半角/全角</kbd> | Meltype を一時停止 |

<kbd>F10</kbd> で英字に直した語は、次から英字で出ます。使うほど自分に合っていきます！
よく使う言葉は、トレイのメニューの「ユーザー辞書...」から登録。くわしくは [docs/USAGE.md](docs/USAGE.md)。

<br><br>

<img src="docs/images/headings/faq.svg" alt="よくある質問" width="480"><br>

<details>
<summary>タスクバーの IME の表示がずっと「A」のまま</summary>

Meltype キーボードで使っているときの表示で、故障ではありません。Meltype が Windows の IME に代わって入力を受け持っているためです。
今のモードは、カーソルの近くに出る「あ」「A」か、トレイのアイコンで確認できます。

</details>

<details>
<summary>Google 日本語入力など、ほかの IME も使いたい</summary>

Meltype IME なら、<kbd>Win</kbd> + <kbd>Space</kbd> でほかの IME に切り替えられます。
Meltype キーボードのときは、<kbd>Ctrl</kbd> + <kbd>半角/全角</kbd> で Meltype を一時停止すれば使えます。

</details>

<details>
<summary>英語のつもりがかなに、かなのつもりが英字になった</summary>

<kbd>F10</kbd> (英字) か <kbd>F6</kbd> (ひらがな) で直して確定すれば、次からはその語を覚えています。
トレイのメニューの「自動判定の強さ」でも調整できます。

</details>

<details>
<summary>おかしな動きを見つけた</summary>

トレイのメニューの「不具合の報告・提案...」から送れます。
「どのアプリで」「何と打って」「どうなったか」があると、すぐに調べられます。

</details>

<br><br>

<img src="docs/images/headings/privacy.svg" alt="プライバシー" width="480"><br>

<p align="center">
  <img src="docs/images/cards/privacy-send.svg" alt="打った文字は送らない: 判定も変換も PC の中で完結" width="32%">
  <img src="docs/images/cards/privacy-network.svg" alt="通信は 2 つだけ: 自動更新の確認と、自分で開いた報告のフォーム" width="32%">
  <img src="docs/images/cards/privacy-storage.svg" alt="保存も PC の中: 設定・学習データ・辞書は %LOCALAPPDATA% の中に" width="32%">
</p>

保存するのは `%LOCALAPPDATA%\Meltype` の設定・学習データ・ユーザー辞書 (と、ON にしたときのログ) だけです。
セキュリティの方針と脆弱性の連絡先は [SECURITY.md](SECURITY.md) にあります。

<br><br>

<img src="docs/images/headings/license.svg" alt="ライセンス" width="480"><br>

<p>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-7a96f0?style=for-the-badge" alt="GPL-3.0"></a>
  <img src="https://img.shields.io/badge/%E5%80%8B%E4%BA%BA%E3%83%BB%E4%BC%9A%E7%A4%BE-%E7%84%A1%E6%96%99-5ec4f0?style=for-the-badge" alt="個人・会社 無料">
  <img src="https://img.shields.io/badge/%E6%94%B9%E9%80%A0%E3%83%BB%E5%86%8D%E9%85%8D%E5%B8%83-OK-ff8ab4?style=for-the-badge" alt="改造・再配布 OK">
</p>

[GNU GPL v3.0](LICENSE)。個人でも会社でも無料。GPL v3 の条件 (改造版もソースを公開) で、改造・再配布も自由です。

ソースを公開せずに製品へ組み込みたいなど、GPL v3 で使えない場合はご相談ください: ibutya0319@gmail.com

<details>
<summary>著作権表示・同梱物</summary>

```
Meltype
Copyright (C) 2026 雪代 / Yukishiro (@yksr_melt / @yksr-melt)

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
General Public License for more details.
```

ソースファイルには `SPDX-License-Identifier: GPL-3.0-or-later` を付けています。
同梱している .NET ランタイム (MIT) と、使っている Windows の機能については [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を見てください。
ロゴ ([docs/meltype.jpg](docs/meltype.jpg)・[docs/images/logo.png](docs/images/logo.png)) は [@Crysta1221](https://github.com/Crysta1221) さんに描いていただきました。

</details>

<br><br>

<img src="docs/images/headings/thanks.svg" alt="協力してくださった方々" width="480"><br>

<a name="super-thanks"></a>
<img src="docs/images/headings/readme/s04.svg" alt="Super Thanks" height="40">

<table>
  <tr>
    <td align="center">
      <a href="https://github.com/Crysta1221"><img src="https://github.com/Crysta1221.png?size=200" width="100" alt="@Crysta1221"><br><b>@Crysta1221</b></a><br>
      <sub>Meltype のロゴ</sub>
    </td>
    <td align="center">
      <a href="https://github.com/lnkiai"><img src="https://github.com/lnkiai.png?size=200" width="100" alt="@lnkiai"><br><b>@lnkiai</b></a><br>
      <sub>Meltype IME</sub>
    </td>
    <td align="center">
      <a href="https://github.com/MuNeNiCK"><img src="https://github.com/MuNeNiCK.png?size=200" width="100" alt="@MuNeNiCK"><br><b>@MuNeNiCK</b></a><br>
      <sub>Meltype IME の登録 (インストーラー)</sub>
    </td>
    <td align="center">
      <a href="https://github.com/konayukiw"><img src="https://github.com/konayukiw.png?size=200" width="100" alt="@konayukiw"><br><b>@konayukiw</b></a><br>
      <sub>メインメンテナー</sub>
    </td>
  </tr>
</table>

<a name="寄付してくださった方々"></a>
<img src="docs/images/headings/readme/s06.svg" alt="寄付してくださった方々" height="40">

[GitHub Sponsors](https://github.com/sponsors/yksr-melt) で応援してくださった方々です。開発を続ける力になっています、本当にありがとうございます！

<!-- sponsors:start -->
<p align="center">
  <a href="https://github.com/cpthgli"><img src="https://github.com/cpthgli.png?size=120" width="60" alt="@cpthgli" title="@cpthgli"></a>
  <a href="https://github.com/MuNeNiCK"><img src="https://github.com/MuNeNiCK.png?size=120" width="60" alt="@MuNeNiCK" title="@MuNeNiCK"></a>
  <a href="https://github.com/whitewater-png"><img src="https://github.com/whitewater-png.png?size=120" width="60" alt="@whitewater-png" title="@whitewater-png"></a>
  <a href="https://github.com/kumapuni"><img src="https://github.com/kumapuni.png?size=120" width="60" alt="@kumapuni" title="@kumapuni"></a>
  <a href="https://github.com/Maguro-Tsukiji"><img src="https://github.com/Maguro-Tsukiji.png?size=120" width="60" alt="@Maguro-Tsukiji" title="@Maguro-Tsukiji"></a>
  <a href="https://github.com/aokuraiauau"><img src="https://github.com/aokuraiauau.png?size=120" width="60" alt="@aokuraiauau" title="@aokuraiauau"></a>
  <a href="https://github.com/daddydaddy"><img src="https://github.com/daddydaddy.png?size=120" width="60" alt="@daddydaddy" title="@daddydaddy"></a>
  <a href="https://github.com/Kuraido1018"><img src="https://github.com/Kuraido1018.png?size=120" width="60" alt="@Kuraido1018" title="@Kuraido1018"></a>
  <a href="https://github.com/overdozer1124"><img src="https://github.com/overdozer1124.png?size=120" width="60" alt="@overdozer1124" title="@overdozer1124"></a>
  <a href="https://github.com/01mercurylampe"><img src="https://github.com/01mercurylampe.png?size=120" width="60" alt="@01mercurylampe" title="@01mercurylampe"></a>
  <a href="https://github.com/aster-void"><img src="https://github.com/aster-void.png?size=120" width="60" alt="@aster-void" title="@aster-void"></a>
  <a href="https://github.com/Kawayan"><img src="https://github.com/Kawayan.png?size=120" width="60" alt="@Kawayan" title="@Kawayan"></a>
  <a href="https://github.com/STD17g"><img src="https://github.com/STD17g.png?size=120" width="60" alt="@STD17g" title="@STD17g"></a>
  <a href="https://github.com/Fugu0141"><img src="https://github.com/Fugu0141.png?size=120" width="60" alt="@Fugu0141" title="@Fugu0141"></a>
  <a href="https://github.com/ryesryuuichi"><img src="https://github.com/ryesryuuichi.png?size=120" width="60" alt="@ryesryuuichi" title="@ryesryuuichi"></a>
  <a href="https://github.com/moozaru-ch"><img src="https://github.com/moozaru-ch.png?size=120" width="60" alt="@moozaru-ch" title="@moozaru-ch"></a>
  <a href="https://github.com/harapeko"><img src="https://github.com/harapeko.png?size=120" width="60" alt="@harapeko" title="@harapeko"></a>
  <a href="https://github.com/milkeclair"><img src="https://github.com/milkeclair.png?size=120" width="60" alt="@milkeclair" title="@milkeclair"></a>
  <a href="https://github.com/r67018"><img src="https://github.com/r67018.png?size=120" width="60" alt="@r67018" title="@r67018"></a>
  <a href="https://github.com/itouuuuuuuuu"><img src="https://github.com/itouuuuuuuuu.png?size=120" width="60" alt="@itouuuuuuuuu" title="@itouuuuuuuuu"></a>
</p>
<!-- sponsors:end -->
<sub>名前を公開にして応援してくださった方を載せています。</sub>

<a name="テスト版で協力してくださった方々"></a>
<img src="docs/images/headings/readme/s05.svg" alt="テスト版で協力してくださった方々" height="40">

テスト版で不具合の報告や意見をくださった皆さん、本当にありがとうございました！ (敬称略)

- くらいど！ ([@Kuraido8888](https://x.com/Kuraido8888))
- しぐれ ([@Akisameee0465](https://x.com/Akisameee0465))
- 琴音Link
- あげちゃ
- うな ([@una08142009](https://x.com/una08142009))
- かふぇらて ([@cafely_latte](https://x.com/cafely_latte))
- ウパー ([@upah_setu](https://x.com/upah_setu))
- Ray
- うぽつです ([@up2ds](https://x.com/up2ds))

<a name="コードで協力してくださった方々"></a>
<img src="docs/images/headings/readme/s03.svg" alt="コードで協力してくださった方々" height="40">

<a href="https://github.com/yksr-melt/Meltype/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=yksr-melt/Meltype&max=60" alt="コードで協力してくださった方々">
</a>

<br><br>

<img src="docs/images/headings/develop.svg" alt="開発に参加する" width="480"><br>

不具合の報告、辞書の追加、Pull Request、どれも大歓迎です！

<p align="center">
  <a href="https://github.com/yksr-melt/Meltype/issues/new/choose"><img src="docs/images/cards/contribute-issue.svg" alt="不具合の報告" width="32%"></a>
  <a href="CONTRIBUTING.md#辞書の追加"><img src="docs/images/cards/contribute-dictionary.svg" alt="辞書の追加" width="32%"></a>
  <a href="CONTRIBUTING.md"><img src="docs/images/cards/contribute-code.svg" alt="コードで協力" width="32%"></a>
</p>

- 送り方 → [CONTRIBUTING.md](CONTRIBUTING.md)
- ビルド・仕組み → [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
- Mac の CLI ビルド → [docs/MAC-CLI.md](docs/MAC-CLI.md)、WSL / Linux の CLI ビルド → [docs/WSL.md](docs/WSL.md)

<br><br>

<a name="コミュニティ応援"></a>
<img src="docs/images/headings/community.svg" alt="コミュニティ・応援" width="480"><br>

使い方の相談や雑談は Discord で！ Meltype を気に入ってもらえたら、GitHub Sponsors で応援していただけると開発の励みになります！

<p align="center">
  <a href="https://discord.gg/y44wBbBgHt"><img src="docs/images/cards/community-discord.svg" alt="Discord" width="40%"></a>
  <a href="https://github.com/sponsors/yksr-melt"><img src="docs/images/cards/support-sponsors.svg" alt="GitHub Sponsors" width="40%"></a>
</p>

<br><br>

<img src="docs/images/headings/stars.svg" alt="Star History" width="480"><br>

<a href="https://www.star-history.com/?repos=yksr-melt%2FMeltype&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=yksr-melt/Meltype&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=yksr-melt/Meltype&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=yksr-melt/Meltype&type=date&legend=top-left" />
 </picture>
</a>
