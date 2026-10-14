## 変更の内容

判定ロジック（区間分け・スコアリング・助詞前の `n` 分割・英語+する/助詞など）を **この PR に集約**しました。#607 / #628 の単語特例パッチは採用せず、一般規則とテストで再設計しています。

**Fixes** #485, #499, #268, #544, #546

### 統合により Close した PR

| PR | Issue | 扱い |
|----|-------|------|
| #514 | #499 | 先頭略語・要件を #509 で一般化 |
| #521 | #268 | 固有名詞＋助詞分割を一般規則化 |
| #605 | #544 | 日本語ローマ字直後の短い英語っぽい区間 |
| #607 | #543 | **#607 の差分パッチは採用しない**（下記「コードで対応」で一般規則化） |
| #608 | #546 | GitHub 混在・識別子英字維持 |
| #623 | #492 | 笑い `w` は **未統合**（`new` 退行のため） |
| #628 | #412 | **統合しない**（単語特例は取り下げ） |

### 主な変更ファイル

- `CompositionDetector.cs` / `ScoredSegmentation.cs`
- `CompositionText.cs` / `CompositionController.cs`（#485、確定時の `zenin`/`tenin`、`kedomala` の末尾 `l` 扱い）
- `RomajiDetector.cs`（`zenin` / `tenin` の確定読み）
- `CompositionTests.cs` / `FeedbackTests.cs` / `DetectionTests.cs`

---

## A. テストで担保している例

| 入力（Enter 確定） | 期待 | テスト（代表） |
|-------------------|------|----------------|
| `imaopenno` | 今openの | `CompositionTests` |
| `sbisinnseiginnkou` | sbiしんせいぎんこう | 同上 |
| `mendokuseena` | めんどくせえな | `Issue544_MendokuseenaStaysJapanese` |
| `betunisoreGitHubdeii` | べつにそれGitHubでいい | `Issue546_…` |
| `DeVitode` / `DeMarino` / `McClintock` / `NoCode` | 表のとおり | `SegmentationRegression_MixedNameSplits` |
| `getElementById` | getElementById | `SegmentationRegression_CamelCaseIdentifierStaysEnglish` |
| `ichimannenn` / `zenin` | いちまんねん / ぜんいん | `SegmentationRegression_NinNotSplitAsEnglish` |
| `commentsuru` / `channelya` / `episodeListView` | commentする / channelや / episodeListView | `SegmentationRegression_SpellCheckedEnglishWithParticle` |
| `kedomala` | けどまぁ | `Issue543_KedomalaStaysJapanese`（スペルが `domal` を知る想定） |
| `hotel` | hotel | 同上（末尾 `l` の英語維持） |

笑い `w` まわりは **#623 相当の退行反例**のみ（`Laughter_IsNotTakenAsWordFoundMidRomaji` 等）。`new` への統合はしていません。

---

## B. コードで対応した Issue（一般規則）

- **#485** … `imaopenno` など、日本語＋英単語境界の `n` 分割（`CompositionText.SplitEnglishFinalN`）
- **#499 / #268 / #544 / #546** … 区間判定・固有名詞・混在（上表のテスト）
- **#543（`kedomala`）** … #607 の後処理パッチではなく、`domal` のように **SCOWL 同梱に無くスペルだけが知る `l`/`x` 終わり**でローマ字として読める語は hotel の末尾 `l` 扱いにしない（`IsSpellKnownRomajiEndingInLorX`）。`commentsuru` / `channelya` と両立（`dc3c79e`）
- **確定時の `zenin` / `tenin`** … `ん` 省略綴りを `ぜんいん` / `てんいん` に（`RomajiDetector` + `MergeOmittedNWholeWordsOnCommit`）

---

## C. この PR では扱わない・別 Issue

| 項目 | 理由 |
|------|------|
| **#492**（笑い `w` と `new`/`saw`） | `new` 退行のため #623 は未統合。別 PR |
| **#412 / #628** | 単語特例は判定ロジック PR に含めない |
| **#607 の PR 差分そのもの** | 採用せず。上記 B で #543 を一般規則化 |
| **数字・読み**（2回3回、2026年） | `CompositionController` 系（#620 等）と境界 |
| **`episodeListView` の個別パッチ** | camelCase は英字維持の一般規則で足りる想定（要望が続く場合は別途） |

---

## 確かめること（報告の再現・CI）

- `imaopenno` → 今openの
- `sbisinnseiginnkou` → sbiしんせいぎんこう
- `mendokuseena` → めんどくせえな
- `betunisoreGitHubdeii` → べつにそれGitHubでいい
- `kyouhaGitHubnipushshita` → 今日はGitHubにpushした
- `getElementById` → getElementById
- `DeVitode` → DeVitoで
- `DeMarino` → DeMarino
- `McClintock` → McClintock
- `NoCode` → NoCode
- `kedomala` → けどまぁ
- `commentsuru` → commentする
- `channelya` → channelや
- `zenin` → ぜんいん

## 確認したこと

- [x] `dotnet run --project src/Meltype.Core.Tests -c Release` のテストが通る (**447/447**)
- [x] 判定・変換を変えたときは、報告された例をテストに足した
- [ ] 辞書だけの変更
- [x] 実際に打って確かめた (OS: Windows、Notepad、Meltype 自ビルド `dc3c79e` — `kedomala`・`commentsuru` を `--repro` とメモ帳で一致確認)
- [x] AI を使った (統合設計・テスト追加・実装) — 中身を理解し、自分で確かめた。**使ったら必ずチェックしてください** ([AI の利用について](https://github.com/yksr-melt/Meltype/blob/main/CONTRIBUTING.md#ai-の利用について))

### 精度の比較（`--eval-json`）

`main` と比較: **1337/1337** 対 **1337/1337**（新規の品質テスト退行なし）
