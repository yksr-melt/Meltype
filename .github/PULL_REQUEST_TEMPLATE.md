## 変更の内容

<!-- 何を、なぜ変えたか。関係する Issue があれば「Fixes #123」と書いてください。
     新しい機能・設定の追加は、先に相談した Issue の番号を書いてください (CONTRIBUTING.md の「Pull Request」) -->

## 確かめること

<!-- 直した結果を「入力 → 期待」の形で書くと、Pull Request のチェック「報告の再現」が Pull Request のコードで確かめます。
     かなの入力は変換の候補に期待した語が出るか、英字の入力は打って Enter した結果を比べます。
     「Fixes #12」と書いた誤判定の報告も、自動で確かめます。 -->

- しゃおみ → Xiaomi
- `nihongowohanasu` → 日本語を話す

## 確認したこと

<!-- 当てはまるものに x を付けてください -->

- [ ] `dotnet run --project src/Meltype.Core.Tests` のテストが通る (Windows なら `src/Meltype.Tests`)
- [ ] 判定・変換を変えたときは、報告された例と、逆向き・似た入力の例をテストに足した (「精度の比較」の「よく使う語の判定の変化」も確かめた)
- [ ] 辞書だけの変更
- [ ] 実際に打って確かめた (OS とアプリ: )
- [ ] AI を使った (使った部分: ) — 中身を理解し、自分で確かめた。**使ったら必ずチェックしてください** (書かずに AI で作ったものに見える場合はクローズすることがあります) ([AI の利用について](https://github.com/yksr-melt/Meltype/blob/main/CONTRIBUTING.md#ai-の利用について))

## 貢献者ライセンス同意 (CLA)

初めての方には、bot が CLA の文面をコメントします。同意していただける場合は、その Pull Request に「CLA に同意します」と 1 行だけコメントしてください。一度同意すれば次からは不要です。詳しくは https://github.com/yksr-melt/Meltype/blob/main/CONTRIBUTING.md
