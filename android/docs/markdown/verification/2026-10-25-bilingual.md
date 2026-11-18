# 2026-10-25 Android日英自動判別・画面適応の検証

## 対象

- ソース: [KoheiTakada/Meltypetest @ 7a44ba5](https://github.com/KoheiTakada/Meltypetest/commit/7a44ba535f8864ef805f20b117f8074bcde87a79)
- 提出ブランチ: `KoheiTakada/Meltype:feat/android-bilingual-adaptive-ime`（同一ソース62ファイルをblob SHAで照合済み）
- Android 15（API 35）エミュレーター: [GitHub Actions #40163689812](https://github.com/KoheiTakada/Meltypetest/actions/runs/40163689812)
- 実機: Pixel 10 Pro Fold（Android 17）、2026-10-25

## 確認済み

- ソースからQA APKをビルドし、単体テストと署名検証に成功。
- エミュレーターのInstrumentationテスト **18件中18件成功**。
- 日英入力と混在入力、候補インデックス・訂正候補の処理、設定の永続化、設定画面のシステムバー余白・画面内／システムBack、候補バーを回帰テスト。
- 実機の縦横回転、画面展開時のキーサイズ、分割レイアウト、中央テンキーの条件付き表示について確認完了。

## 統合版PR #1149の最新QA（2026-10-25追記）

- **統合済みソース**: `965dbf33bb20300a2c906168cfbf980d930f64ad`（[コミット](https://github.com/KoheiTakada/Meltype/commit/965dbf33bb20300a2c906168cfbf980d930f64ad)）。
- **自動テスト**: [CI #40182600362](https://github.com/KoheiTakada/Meltypetest/actions/runs/40182600362)、Android 15計測18/18件合格、JVM単体444/444件合格。失敗・エラー・スキップなし。ケース別JUnit XMLと生ログをArtifactsとして保存。
- **ソースからのビルド**: [CI #40182575249](https://github.com/KoheiTakada/Meltype/actions/runs/40182575249)成功。固定Mozcソース・NativeAOTを含む。
- **実機**: Pixel 10 Pro Fold（Android 17）。最新版APKの実機確認完了との本人報告を2026-10-25に受領。
- **記録粒度**: [M01〜M08の操作・期待値](../../QA-TESTING.md)は公開済み。ただし、個々の結果、スクリーンショット、画面サイズの実測値は添付されていない。各項目の結果が個別に検証・報告されたとは主張しない。

## 対応範囲と残件

- 日本語QWERTYは**C# `Meltype.Core`をNativeAOT/JNIで共有する日英自動判別**。12キーフリックへの自動判別拡張は未対応。
- Android 8.0からのサポートを宣言しているが、全APIバージョン・全端末を今回テストしたわけではない。
- 音声入力、TalkBackの詳細検証、独立した「読みから英字」機能、公開用の署名とアプリIDは未対応または要調整。
- これはデバッグ／QAの検証結果。Playストア公開や本家へのマージ完了を意味しない。

2026-10-13の第1段階記録にある「自動判別未実装」「実機未検証」等の記述は、その日付当時の状態を表す履歴です。
