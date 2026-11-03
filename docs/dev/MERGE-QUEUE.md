# Mergify Merge Queue の導入と運用

対象は `main`。`.mergify.yml` は `@mergifyio queue main` で明示的に入れた PR を、一度に 1 件ずつ処理する。自動投入は設定していない。
Mergify は必要なら元の PR ブランチに最新の main をマージし、元の PR のチェックを待つ (in-place checks)。PR の履歴に main のマージコミットが入ることがある。
外部のマージで main が進んだときはキューをリセットする。これは webhook による再評価であり、外部マージとの競合を防ぐ原子的なロックではない。

## 既存 CI と CLA

2026-11-03 に確認した `Protect` ruleset (ID 24496721) の必須チェックは、GitHub Actions の `test`、`core (ubuntu-latest)`、`core (macos-latest)`、`精度の比較`、`cla`。承認は 1 件必要で、strict policy は無効だった。設定は自動変更しない。
Mergify の `branch_protection_injection_mode: queue` は ruleset が要求するチェックとレビューをキューとマージの条件に取り込む。`精度の比較` は対象外の変更で skipped になるため、`check-success` を追加して無条件に成功を要求しない。
CI の整理 (#1467) 後、必須のビルドとテストは `windows.yml`・`core.yml`、報告の再現は `pr-expectations.yml` が実行する。CLA bot のテストは `quality.yml` の `scripts` ジョブが `*.test.mjs` として実行する。main をビルドできないときは、既存の `精度の比較` は比較を省いて成功するため、その警告も確認する。

`cla` ジョブは同意の有無をジョブ終了コードではなく、元の PR の head SHA に設定する `CLA` status で報告する。status は同一 SHA の別の PR と共有されるので、bot は元の PR の作者を確認したうえで、その PR に `CLA確認済み` ラベルも付ける。キューはこのラベルを必要とする。未同意ならラベルを外す。メンテナーと bot の免除は従来の CLA bot の方針どおり。

このラベルは bot 専用。**手動で付けると CLA を迂回できるため、付けないこと。** ラベルを変更できる権限や ruleset の bypass 権限を持つ人を信頼する運用が前提。bot がラベルを作成・変更できない場合はキューに入れない。PR を作成済みでラベルがまだ無い場合は、その PR の現在の head に対応する `cla` workflow を再実行して確認する。

`batch_size: 1`、`max_parallel_checks: 1`、`mode: serial`、`max_checks_retries: 0`、追加の `merge_conditions` なしを維持する。変更すると Mergify が一時 PR でチェックする場合があり、その作者は元の PR 作者ではない。並列化・バッチ化・自動リトライを導入する前に、元の PR ごとの CLA 検証と CI を設計し直す。

## 管理者が行う導入手順

この文書は必要な手順を示すだけで、App のインストールや ruleset の変更は行わない。

1. テスト用リポジトリを用意し、Mergify GitHub App をインストールする。設定ファイルのベース条件をテスト用に合わせ、同じ構成で後述の検証を行う。本番の main では試験しない。
2. 本番の `Protect` の必須チェックとレビューを維持し、`Require branches to be up to date before merging` (`strict_required_status_checks_policy`) を有効にする。現在は無効なので、変更前はキューを通さない古い PR を通常の操作でマージできる。
3. Mergify の本番 App をリポジトリにインストールし、Mergify の公式手順に従い、対象 ruleset の bypass list に Mergify を `Exempt` で加える。対象ブランチの ruleset が増えた場合も各 ruleset を確認する。GitHub のネイティブ Merge Queue ルールは有効にしない。
4. 設定の PR を通常のレビューフローでマージし、Mergify の `Configuration changed` チェックとダッシュボードで設定を確認する。Mergify は既定ブランチ上の設定を使う。
5. 既存 PR は現在の head に対応する CLA workflow を再実行するなどして、作者に応じたラベルと status を確認する。必要なレビューとチェックを満たした PR に、権限のあるメンテナーが `@mergifyio queue main` とコメントして投入する。

Mergify の推奨する `Exempt` は ruleset 全体を App に対して免除する。Mergify が再現する必須チェックやレビューはダッシュボード上の条件で実際に確認すること。公式文書によると `non_fast_forward` など一部ルールは条件に取り込まれない。この設定は GitHub の通常の merge API を使い、force push はしないが、App の bypass 自体は広い権限である。

## 検証してから運用すること

- 2 本の PR がそれぞれ単独では通り、両方を組み合わせると失敗する例を作る。1 本目の後に 2 本目が最新 base を取り込んで再検査され、失敗すればマージされないことを確認する。
- 元の PR に対する `windows`、`core`、`pr-expectations`、`quality`、`cla` のイベントと必須チェックを確認する。`精度の比較` の実行・skipped の両方、`quality / scripts` での CLA bot テスト、CLA が未同意・同意済みの場合を確認する。
- 同じ head SHA の PR を異なる作者で作り、片方だけ同意した場合に、未同意側へ `CLA確認済み` が付かずキューに入らないことを確認する。
- 更新時に元の PR の `synchronize` が届き、CLA bot が元の作者を見て、必須チェックが新しい head に報告されることを確認する。コンフリクトや更新できない fork はキューに入ったまま成功扱いにならず、理由を確認できることも確かめる。
- Mergify の Exempt と strict を有効にした環境で、取り込まれたレビュー・チェック条件、外部マージ後のリセット、必要な権限を確認する。既存の owner bypass も含め、誰が通常マージできるか実際に試す。

## 迂回と制約

strict は、ruleset を bypass しない人による古い PR の通常マージを止めるが、キューの利用を強制しない。現在 `yksr-melt` は ruleset を常時 bypass できる。さらに Mergify を Exempt にすると、GitHub 自身は Mergify のマージに strict を強制しないため、外部のマージと Mergify の処理が競合する余地がある。リセット設定だけで完全には塞げない。

全マージをキュー経由にする場合は、検証後に Mergify のダッシュボードの **Merge Queue > Deployment** から公式の exclusive mode 手順を確認し、通常利用者の直接マージを ruleset で制限する。既存の owner bypass を残すかは緊急時の運用を含めて管理者が判断し、残すなら例外経路であることを明示する。App の bypass と無視される ruleset の影響も確認する。

GitHub の制約で、一部の fork PR (workflow の変更など) や別 App の bot による PR は in-place で更新できないことがある。PR の作者に最新 main の取り込みを依頼し、再検査する。無理に一時 PR や並列処理に切り替えない。PR の head ブランチ自体にレビュー必須の ruleset がある場合も in-place 更新は拒否される。

参考: [Mergify の in-place checks](https://docs.mergify.com/merge-queue/batches/#in-place-checks-no-batch-prs)、[GitHub rulesets との互換性](https://docs.mergify.com/merge-queue/github-rulesets/)、[キューの導入方法](https://docs.mergify.com/merge-queue/deploy/)、[設定ファイル](https://docs.mergify.com/configuration/file-format/)。
