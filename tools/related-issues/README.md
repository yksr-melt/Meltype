# 関連 Issue 検知の試作 (issue #1382)

誤判定の報告の打ったものを判定エンジンに通して、判定理由 (`DetectionResult.Contributions`) が似ている Issue を関連の候補にする方式を確かめた。
本番の bot とは別のもので、Issue へのコメントやラベルの変更はしない。

## 動かし方

```bash
python3 tools/related-issues/poc.py fetch    work   # 誤判定の Issue と timeline (gh api)
python3 tools/related-issues/poc.py extract  work   # 本文から 打ったもの・出たもの・期待した結果
python3 tools/related-issues/poc.py sign     work   # 判定理由 (dotnet run ... -- --related-signature)
cp tools/related-issues/labels.tsv work/
python3 tools/related-issues/poc.py eval     work   # 方式ごとの Precision / Recall
```

`sign` は `src/Meltype.Core.Tests` の `--related-signature` を使う。1 件ごとに、語ごとの ScoreEngine の Contributions と、変換ボックスの区間分けのラティスの道を JSON Lines で出す。

## 正解の付け方

- 同じ PR で閉じた Issue の組を「関連」にした。ただし 6 件以上をまとめて閉じた PR (#1185・#768・#402 など) は原因がばらばらなので数えない。
- どちらも PR で閉じていて、共通の PR が無い組は「無関係」にした。
- 各方式の上位 12 組を読んで、[labels.tsv](labels.tsv) で付け直した (重複 4 組・同じ原因 2 組を足し、別の原因 1 組を外した)。この表は Claude が付けたもので、メンテナーの確認がまだ。

2026-10-31 の時点で、誤判定の Issue 206 件のうち 199 件で打ったものが取れた。正解の組は 9563 (関連 26 / 無関係 9537)。関連の組は 0.27% しかない。

## 結果

| 方式 | AP | P@10 | 1 位が関連 | 最良の F1 (P / R / 誤検出率) | 最高点で同点の組 |
|---|---|---|---|---|---|
| Source の一致 (Jaccard) | 0.006 | 0.00 | 0.08 | 0.011 (0.006 / 0.46 / 22%) | 2122 |
| Source + Reason の一致 | 0.004 | 0.00 | 0.13 | 0.010 (0.005 / 0.19 / 11%) | 1013 |
| 語ごとの判定 + Source | 0.006 | 0.00 | 0.18 | 0.015 (0.008 / 0.42 / 15%) | 1447 |
| 区間分けのラティスの道 + 報告の向き | 0.007 | 0.01 | 0.17 | 0.013 (0.007 / 0.50 / 20%) | 1224 |
| 文字列 (題の 3-gram) | 0.004 | 0.01 | 0.10 | 0.015 (0.008 / 0.12 / 4%) | 220 |
| BM25 (本文の 2-gram) | 0.050 | 0.30 | 0.30 | 0.176 (0.375 / 0.12 / 0.05%) | 1 |

- 同点はランダムに並べた 20 回の平均。「1 位が関連」は、関連の Issue が先にある Issue について、先の Issue の中で一番似ている候補が関連だった割合。
- BM25 の P@10 は、重複の報告 (同じ入力の 2 件) を拾ったもの。上位を読んで正解を付けたので、上位に出た方式ほど関連が見つかりやすい偏りがある。

### Source の一致では関連を見分けられない

- Source は 5 種類 (Romaji・English・Dictionary・Kana・Typo)、Reason も 17 種類しかない。無関係の組の 22% で Source の集まりが完全に一致する。
- 例: #602 (data → だた) と #1193 (env → えんv) はどちらも `English(EN+4: 英語辞書の語と一致)` だけで、Source も Reason も同じ。直した場所は別。

### Contributions は誤判定が起きた場所を表していない

- ScoreEngine (Contributions を出すもの) は Windows の英数状態の検知だけで使われている。変換ボックスの中の英語・日本語の区間分け (`CompositionDetector`・`ReadingLattice`) は ScoreEngine を通らない。誤判定の報告のほとんどはこちら。
- 例: #1365 metadata は Contributions では `English(EN+4)` で正しく英語なのに、区間分けでは `EN:meta | JA:data` になって「metaだた」になる。#1347 hitomebore も Contributions は `Dictionary(JP+5)` で正しく日本語なのに、区間分けで `JA:hitome | EN:bore` になる。
- #1356 meltype.moe・#1319 iZotope・#1117 nohup は、語の Contributions が空 (判定できない) か、報告と関係のない加点しか出ない。

| Issue | 打ったもの | 語の Contributions | 今の区間分け |
|---|---|---|---|
| #1365 | metadata | English(EN+4) | EN:meta \| JA:data |
| #1356 | meltype.moe | meltype: Romaji(EN+5)・English(EN+3) / moe: なし | EN:meltype \| EN:. \| JA:moe |
| #1347 | hitomebore | Dictionary(JP+5) | JA:hitome \| EN:bore |
| #1319 | iZotope | なし | JA:i \| EN:Zotope |
| #1117 | nohup | なし | EN:nohup (直っている) |

### 版の違い・再現できない入力

- v1.4.0 と今の main で 199 件を比べると、Contributions は 1 件も変わらなかった。区間分けは 50 件で変わった。直すたびに変わるのは区間分けの方で、Contributions は報告の原因を追えていない。
- ラティスの道を出す仕組み (`LatticeTrace`) は v1.4.0 に無い。報告された版で区間分けの理由を出すには、版ごとに出し方を合わせる必要がある。
- 今の main で `--repro` すると、報告の「出たもの」と同じになったのは 199 件中 9 件。99 件は期待した結果と同じ (直っている)。残りは漢字変換の違い (Mozc が要る) か、本文の書き方で比べられないもの。報告された版で再現しないと、直った後の判定理由で比べることになる。

### 実行にかかる時間

`sign` は 199 件で 3 秒ほど (dotnet のビルド済み)。`eval` は数秒。Issue の取得 (`fetch`) は timeline を 1 件ずつ取るので 1 分ほど。

## 次の設計判断のために

- Contributions (Source・Reason) を関連 Issue の根拠にするのはやめた方がよい。分類が粗く、誤判定の起きる場所 (区間分け) も通らない。
- 判定理由を使うなら、区間分けのラティスで「選ばれた道」と「期待した結果に合う道」の差 (どの区切り・どの重みで負けたか) を、報告された版で出すのが筋。今の `--segments` の出力はその材料になるが、まだ候補を絞れる精度は出ていない (AP 0.007)。
- 本文の BM25 は重複の報告を拾える (P=0.375 のしきい値で誤検出率 0.05%)。コメントを出すなら、BM25 のしきい値を高くして重複だけを出すのが今のところ一番安全。Embedding や小型 LLM は、同じ `labels.tsv` で比べてから決める。
- 正解の組が 26 しかないので、数字のぶれは大きい。メンテナーが `labels.tsv` を見直して増やすのが先。

---

# 2 つ目の方式: PR の前後で挙動が変わるか

関連 Issue を「似ている」ではなく「同じ直し方で直る」で探す。誤判定を直した PR (100 件) ごとに、マージの前と後をビルドし、199 件の入力を `--repro` で打って結果を比べた。

```bash
python3 tools/related-issues/impact.py build  work        # 151 commit をビルドして打つ (4 コアで 15 分ほど)
python3 tools/related-issues/impact.py eval   work        # PR 用・トリアージ用の精度
python3 tools/related-issues/impact.py triage work [番号]  # 報告より前の PR で、その入力が期待どおりになったもの
```

古い版にも [ImpactBatch.cs.txt](ImpactBatch.cs.txt) を入れて、`--repro` を 1 回の起動でまとめて流す (`Repro.Type(keys, last)` は最初の版から同じ形)。151 件中 14 件 (PR 10 件分) はビルドできなかった。Mozc を使っていないので、漢字変換の違いは比べられない。

## PR 用: その PR で挙動が変わる Issue

| | 件数 |
|---|---|
| PR が閉じた Issue のうち、前後で結果が変わったもの | 103 / 179 (58%) |
| 　変わらなかったもの: 期待した結果が漢字 (Mozc が要る) | 24 |
| 　変わらなかったもの: 判定だけでは同じ結果 (Windows 専用の直し・文脈が要るものなど) | 52 |
| PR が閉じていないのに変わった Issue | 149 (PR 1 件あたり中央値 1 件、最大 36 件) |
| 　うち期待どおりになった (一緒に直った) | 66 |
| 　うち期待どおりだったのに変わった (壊れた) | 2 |
| 　うちそれ以外 (変わったが期待とも違う) | 81 |

- 「一緒に直った」の例: PR #448 で #844 .env・#597 opencode・#457 tmux・#434 i18n なども期待どおりになった。
- 「壊れた」の例: PR #105 で #1044 Androidha が「Androidは」から「Andろいでゃ」になった。
- 推測ではなく実際の挙動の差なので、PR に「この PR で #… も直る / 壊れる」と出すのには使える。漢字の Issue まで見るには Mozc を入れて流す。

## トリアージ用: 新しい Issue に近い Issue

PR が無い段階では、次の 2 つを試した。

1. **両方の入力を一緒に変えた PR の重なり** (2 つの Issue を直した PR は除く): AP 0.020、1 位が関連 0.18。Contributions の方式 (0.006) よりはよいが、BM25 (0.050) に届かない。
2. **再発の候補**: 報告より前にマージされた小さな PR (閉じた Issue が 5 件以下) で、その入力が期待どおりになったもの。199 件中 4 件で候補が出て、候補の PR 12 件のうち当たりは 3 件ほど。
   - 当たり: #717 zenn.dev → PR #497 (#462 llama.cpp と同じ、英単語の中の `.`)、#597 opencode → PR #458 (#301 opencode と同じ入力)、#844 .env → PR #458 (#431 .gitignore)。
   - 外れ: #448〜#466 は同じ時期に同じあたりを直した PR で、どれでも結果が変わるので一緒に出る。

## まとめ

- **PR 用は使える。** その PR で変わる Issue を挙動の差で出せる。誤検出は「変わったが期待とも違う」ものだけで、それも根拠 (前後の結果) を一緒に見せられる。
- **トリアージ用は、今のところ決め手が無い。** 重複は BM25 (高いしきい値) と打ったものの完全一致、再発は「過去の PR で直っていた」で拾える。それ以外の関連は、どの方式も精度が足りない。
- トリアージで関連を探すなら、PR 用の仕組みを毎晩回して「開いている Issue のうち、main の変更で結果が変わったもの」を知らせる方が、似ている Issue を探すより確実。
