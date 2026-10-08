# Issue 下書き: 助詞「や」と英単語の誤判定

GitHub の [変換・判定の間違い](https://github.com/yksr-melt/Meltype/issues/new?template=2-misdetection.yml) テンプレート向け。
コピーして Issue を作成し、修正 PR から `Fixes #<番号>` で紐づける。

`pedia` → `ぺぢあ` は既存の [#207](https://github.com/yksr-melt/Meltype/issues/207) を使い、新規 Issue は立てない。

---

## タイトル

`[誤判定] 助詞「や」が英単語に飲み込まれて yagithub / githubya になる`

## 種類

日本語のつもりが英字になった (にほんgo)

## 打ったもの (キーをそのまま)

`yagithub`  
（関連）`githubya` / `kyouhayagithub`

## 出たもの

- `yagithub`（期待は `やgithub`。報告時点の版では助詞側が英字のまま残ることがある）
- `githubya` → `ぎてゅびゃ`（最新 main でも再現。期待は `githubや`）

## 期待した結果

- `yagithub` → `やgithub`
- `kyouhayagithub` → `きょうはやgithub`
- `githubya` → `githubや`
- 対照: `hagithub` → `はgithub`、`githubni` → `githubに`、`yahoo` → `yahoo`

## 最後に押したキー

Enter (確定)

## 前後の文 (分かれば)

日本語の文の途中で「〜や GitHub」のように助詞「や」の直後／直前に英単語を続けるとき。

## どのアプリで

メモ帳 / ブラウザーの入力欄

## OS

Windows

## Meltype の版

報告時の版を記入（例: 1.0.4）。再現確認は main の判定テストでも可。

## 補足（任意）

- 先頭の `yagithub` → `やgithub` は最新 main の判定では既に通ることがある
- 末尾の `githubya` → `ぎてゅびゃ` は最新 main でも再現。英単語末尾子音 `b` が `ya` と結合して拗音 `びゃ` になる
- 助詞リスト: `DictionaryDetector.Particles` / `CompositionDetector.TrailingParticles` に `ya` が無かった
- 関連: #207（`pedia` → `ぺぢあ` / `protopedia` → `proとぺぢあ`）
- 壊したくない例: `yahoo`

---

## PR 本文テンプレート

```markdown
Fixes #<このIssueの番号>
Fixes #207

## 確かめること
- yagithub → やgithub
- kyouhayagithub → きょうはやgithub
- githubya → githubや
- hagithub → はgithub
- yahoo → yahoo
- pedia → pedia
- protopedia → protopedia
- wikipedia → wikipedia

## AI の利用
（使った場合）下書き・調査に Cursor を使った。判定ロジックとテストは自分で確認した。
```

初回 PR では CLA bot に `CLA に同意します` とコメントする。
