# 上流1.6.0系の照合（2026-11-15）

Meltype-FLASH 1.1.19で、上流 [v1.6.0](https://github.com/yksr-melt/Meltype/releases/tag/v1.6.0)（`4bbc87e19091cf1f3a085e7098563ab60214ef5b`）の共通Core機能をRustへ移植した記録です。上流と同じ版番号・全機能互換を示すものではありません。

## 今回取り込んだもの

| 対象 | Flashでの対応 |
| --- | --- |
| 分野の辞書（#1650・#1861・#1866） | 上流の `dictionaries/domain-*.txt`（医療・IT・駅名・固有名詞・人名・法令・SudachiDict）を同梱し、変換候補に出す。ふだんの候補（変換エンジン・同音異義語・ひらがな・カタカナ）を後ろに押さないよう、半角カタカナの後ろ・絵文字の前に並べる。候補の読みが助詞などを含むときは、辞書にある最長の先頭部分に残りを付ける（せんたんきょだいしょうの → 先端巨大症の）。`Contains` と逆引きは分野の語を使わない |
| 国名の語（#1615・#1861） | 上流の `dictionaries/countries.txt`（Unicode CLDR の国・地域名）を固有名詞として読み、大文字で始まる国名を日本語の文の中でも英字のまま残す。ローマ字として最後まで読める語・2文字の略・大文字だけの略・日本語の語のローマ字の途中と重なる語は入れない（下の「Flash独自の調整」） |
| 通貨の換算の丸め（#1871・#1882） | 換算した額を丸めて1万以上は万・億・兆で書く（15,923 → ≒1.6万、62.8 → ≒62.8）。丸めは Flash 独自に小数第1位（上流は有効数字2桁）。Flash はさらにレートの出どころを固定・自動・手入力から選べる（上流に無い追加）。表は `tools/make-exchange-rates.mjs` のまま |
| 「ばーじょん」の候補（#1868） | `Meltype-FLASH 1.1.19 (Windows / IME方式)` のように、OSと入力方式（Windowsは設定の動作モードでIME方式・キーボード方式）を書く。入力欄では設定を知っているのでフロントまで、辞書だけを使う所ではOS名だけを書く |
| AIの読み照合（#1675・#1511・#1800） | 上流 `ZenzaiConverter.IsPlausible` と同じ照合を共通Coreの `plausible::plausible` に置き、Zenzai 以外の AI にも当てた（Gamma・Qwen・Shion・Jinen・kanakanji-edge・shuntelligence）。Mozcの文節候補で作れず、かなが読みと合わない答え（繰り返し・直前の文節の混入）は使わずMozcに任せる。読みと表記が違う語（きずく → 気づく）は、Mozcの候補で作れるときだけMozcの区切りで使う |
| 法令名の読み（#1650） | 上流の辞書のまま。大きいツをっにした読み（あんままっさーじ…）で引け、改題前の題名の読みでは引けないことを試験で確かめる |
| 辞書のライセンス | `THIRD-PARTY-NOTICES.md` に分野の辞書と国名の語の出典・ライセンスを足した。`tools/check-dictionaries.mjs` は `countries.txt` と `domain-*.txt` の形式を確かめる |

## 1.1.25で追加したもの

- 設定画面の「適用」で、ウィンドウを開いたまま変更を保存できる（#1853）。
- ChromeOS・Android・iOSにMozcの語彙と接続コストを同梱。OSにMozc本体がなくてもオフラインで漢字変換できる。ChromeOSでは辞書をWASM側で受け取る際の全体コピーを1回省いた。

## 移植していないもの

| 対象 | 扱い |
| --- | --- |
| 変換エンジンを全OSでMozc＋Zenzaiに統一（#1749） | 対象外。FlashはMozc・Zenzai・shuntelligenceなどを設定「AIモデル」で選ぶ独自の構成なので、「変換エンジン」設定とOS側の変換エンジンは残す |
| Windowsでゲームの中でもIMEを使えるようにする（#1805・#1859） | 未移植。FlashのTSF・macOS・IBusの各入力層はゲームの判定と英数への切り替えを受け持っていない。ゲームでは止める既存の設定（`StopInGames`）のまま |
| 前の版のIMEが残っているアプリへの再起動の案内（#1844） | 未移植。WindowsのTSF側の作業になる |
| WindowsキーボードのShift記号の修正（#1836） | 未移植。Flashのキーボード方式はC#版とは別の実装 |
| Macの最低macOS 13.3・azooKeyの除去（#1749） | 対象外。FlashのMac版は独自の実装で、azooKeyを同梱していない |
| 不具合報告フォームの項目削減（#1563）と報告の種類の削除（#1644） | 未移植。Flashの報告フォームとひな形は別の運用 |
| 確定のたびに区切りの理由をログに残す（#1644・#1865） | 未移植。Flashの共通Coreには上流の `Diagnostics.Log.RecordText` に当たる確定ログがない |
| WindowsのIMEの入れ替え（#1753・#1767）・Mac/Linuxの個別修正 | 各OSの実装固有。Rustの別実装のため、そのままの移植対象ではない。同じ不具合がないことの保証でもない |

## Flash独自の調整

- `countries.txt` は上流のファイルをそのまま同梱するが、Coreが読むときに次の語を外す。
  - ローマ字として最後まで読める語（Oman・San・Mayotte）: 上流と同じワークアラウンド（#1869、`omanjuu` → `oman中` を防ぐ）。
  - 2文字の略（St・El）: 英単語として扱うとローマ字の語（`ost` → `おst`）の区切りが変わる（#1123）。
  - 大文字だけの略（SAR・UK・DRC）: 国名として打つ語ではない。
  - 日本語の語のローマ字の途中と重なる語（Hong は nihongo）: 語の途中で英語にすると、もとの日本語の区切りが変わる。Flashの凍結したC#参照試験（`upstream-core-data.json`・`upstream-balanced-keys.json`）と、報告された誤判定を保つため。
- 国名の語は `proper`（固有名詞）と英字候補の正しい大文字小文字に足す。`english` の語彙と判定の索引（`detection::lexicon`・`lattice::proper_nouns`）には足さない。足すと `mya`（Myanmar の頭）が英語と判定されるなど、Flashの判定の凍結試験が変わるため。上流の「日本語の文の中でも英語」は、大文字で始まる国名について満たす。

## 検証

- `cargo test -p meltype-core`：共通Coreの単体・統合試験が成功。新規に `upstream_v160.rs` を追加し、分野の辞書の語と並び順、国名の英字と日本語の逆方向、通貨の丸め（単体試験）、「ばーじょん」のOS・フロントを確認する。
- `cargo test -p meltype-core`・`-p meltype-native --lib`：読み照合（`ZenzaiConverter.IsPlausible` に当たる `plausible::plausible`）の単体試験と、shuntelligence の文節が読みと合わないときに Mozc へ任せる試験を追加した。既存のワーカー・HTTP・プロセス管理の試験とともに成功。
- `node tools/check-dictionaries.mjs`：`countries.txt` と `domain-*.txt` を含めて形式のエラーなし。
- `cargo check --workspace --locked`：Windows上で全クレートのコンパイルを確認。

この記録は共通Coreの移植と自動試験の範囲です。各OSのアプリ・IME・実機入力の確認ではありません。Windows・macOS・Linux・Android・ChromeOS・iOSのビルドと実機確認は、それぞれ別に行います。
