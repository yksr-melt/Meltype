---
description: 指示の内容で Issue を立て、その Issue を直す PR を作る
argument-hint: <やりたいこと・直したいこと>
---

次の内容で Issue を立て、それを直す PR を作る。

$ARGUMENTS

## 手順

1. **似た Issue・PR を探す**。あれば、それを使うかを聞いて止まる
2. **小さな直しか、機能の追加かを分ける**。機能・設定の追加や枠組みの変更は、先に Issue で相談する決まり (AGENTS.md・CONTRIBUTING.md)。その場合は Issue だけを立て、PR は作らずに止まる
3. **Issue を立てる**: Issue のテンプレート (`.github/ISSUE_TEMPLATE/`) から合うものを選び、その項目に沿って日本語で書く
4. **直す**: AGENTS.md に従って直し、テストを足す。報告の例は `src/Meltype.Core.Tests/data/issue-cases.tsv` にも足す
5. **確かめる**: AGENTS.md の「ビルドとテスト」「作業後の確認」を流す
6. **PR を作る**: テンプレート (`.github/PULL_REQUEST_TEMPLATE.md`) どおりに書き、本文に `Fixes #<立てた Issue>` を入れる。「確かめること」に `打ったもの → 期待する結果` を書く。AI を使ったことを書く

## 守ること

- 新しいソースファイルの著作権の名前は、PR を出す人の名前にする。分からなければ聞く
- 1 つの PR は 1 つの目的にする
