# Android QA — テストの再現手順と期待結果

PR #1149 の後続開発者は **テストコード、入力例、期待結果、再実行コマンド** をこのリポジトリの同じコミットから取得できる。旧コミットの18/18合格は今回のマージ版の合格証拠ではない。

## 前提
- Linux x86_64、JDK 17、.NET SDK 10、Python 3、Android SDK platform 36 / build-tools 36.0.0、NDK 28.2.13676358、patchelf、git、curl、gcc/g++。
- Android 15（API35）Google APIs x86_64エミュレーター。実機なら接続先を ANDROID_SERIAL で指定。
- Mozc は android/mozc/MOZC_COMMIT を固定し、android/tools/build-mozc.sh（Bazelisk 1.27.0 / NDK r29を内部使用）で**ソースから**ビルド。外部の期限付きCI Artifactだけに依存しない。
- 出力APKはQA用デバッグ署名であり、正式配布バイナリではない。

## 1. ソースからビルド
リポジトリのルートから次を実行する。

    export ANDROID_HOME=${ANDROID_HOME:-$HOME/Android/Sdk}
    export ANDROID_NDK_HOME=$ANDROID_HOME/ndk/28.2.13676358
    git rev-parse HEAD
    bash android/tools/build-mozc.sh
    bash android/tools/build-meltype-native.sh
    cd android
    ./gradlew --no-daemon :engine:test :app:testDebugUnitTest :app:assembleDebug :app:assembleDebugAndroidTest

Mozcの初回ソースビルドは大量の依存を取得する。版・SHAをチェックし、異なる版の.soを黙って代用しない。CI定義は [.github/workflows/android-qa.yml](../../.github/workflows/android-qa.yml) に含める。

## 2. Android上の計測テストを実行

    adb devices
    bash tools/run-instrumentation-tests.sh
    ls -la build/qa

出力: instrumentation.log（生ログ）、instrumentation.xml（JUnit XML）、test-cases.md（ケース別成否）、summary.md、device-info.txt。実行スクリプトは実際の @Test fun 一覧と計測結果を照合し、未実行・予期しない・失敗ケースを検出する。

コード: [LanguageDetectionDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/LanguageDetectionDeviceTest.kt) / [SettingsScreenDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/SettingsScreenDeviceTest.kt)。テスト内容を変えたらこの表も更新する。

### 計測テスト18件の入力と期待値
| ID | @Test fun | 入力・操作 | 期待値 |
|---|---|---|---|
| A01 | englishStaysLatin | hello / meeting | hello / meeting（英字保持） |
| A02 | japaneseRomajiBecomesKana | arigatou / konnichiwa | ありがとう / こんにちは（またはこんにちわ） |
| A03 | mixedLanguagesWithoutManualToggle | hello arigatou | hello ありがとう |
| A04 | predictionActionsAreMappedToNativeIndices | 候補hello,help、変換候補2件 | 候補インデックス0/1が保持され、操作ボタンが出ない |
| A05 | correctionsShowRealWordsAndNeverCommandButtons | raw=nihongo、kana=にほんご | nihongo / にほんご / ニホンゴが訂正候補 |
| A06 | emojiPreferenceFiltersPredictions | ありがとうございます, ありがとう😊 | 絵文字OFFでは絵文字付きだけ除外 |
| A07 | settingsPersistJapaneseSpaceAndEmojiPreferences | 日本語スペース/絵文字をOFF→ON | 設定を読み直しても反映 |
| A08 | originalLatinCorrectionCanBeSelectedAndCommitted | nihongo→英字訂正→Enter | nihongo確定 |
| A09 | digitsStayInCompositionUntilExplicitEnter | arigatou→テンキー123 | Enter前に未確定文字が消えない |
| A10 | candidateBarTextIsReadableAndHasBreathingRoom | 410dpバー＋長い日本語候補 | 高さ≥68dp、文字・余白を保持 |
| A11 | exteriorMarginsStayUnclickableWhileLetterKeysWork | 400dp幅で左右余白・上端・Qを判定 | 余白にキーなし、Qは認識 |
| A12 | englishPredictionsCanBeCommittedOnAndroid | hell→英語予測候補を選択 | Native Coreの予測語を確定 |
| A13 | translucentKeyboardHasOnlyTopRoundedHairline | 装飾をビットマップへ描画 | 上端は半透明、内部は透明 |
| B01 | homeHasGboardStyleCategoriesWithoutDemoOrCompletedSteps | 設定ホームを開く | 4カテゴリがあり試し書き等がない |
| B02 | liveConversionSwitchChangesAndPersistsItsValue | ライブ変換を切替 | 表示値と保存値が一致 |
| B03 | systemBarsAreRespectedOnHomeAndDetailScreens | 実Insets・合成Insetsを設定 | 上端/下端のシステムUIと非重複 |
| B04 | toolbarBackAndSystemBackBothReturnToSettingsHome | 画面内戻る→再入→OS Back | 両方とも設定ホームに復帰 |
| B05 | unfoldedScreenKeepsCategoriesBesideActiveSettings | 設定を幅700dp以上/未満で表示 | 広画面2ペイン、狭画面スクロール |

A04〜A06などには固定JSONで候補表示ロジックを検証するケースもある。A01〜A03、A08〜A09、A12はNative Coreとの接続を含む。実機での画面開閉は自動計測18件とは別の手動テストである。

## 3. 画面開閉・実機での手動テスト
実機名、Androidバージョン、ビルドソースSHA、日付、画面サイズ、実際に入力された文字列、スクリーンショットや失敗手順をPRに記録。**試していないケースを合格としない。**
| ID | 条件 | 入力・操作 | 期待値 |
|---|---|---|---|
| M01 | 閉じた縦画面 | hello arigatou | hello ありがとう |
| M02 | 閉じた横画面 | 全列を打鍵 | 行の高さ・タップ精度が正常 |
| M03 | 展開した横長画面 | QWERTYと中央を打鍵 | 十分な高さがないと中央テンキー非表示 |
| M04 | 広い展開画面 | テンキー表示を確認 | しきい値とキー幅が仕様どおり |
| M05 | 開閉と回転 | 候補・絵文字パネルの表示 | レイアウト追従・重なり無し |
| M06 | 日本語QWERTY | 、。!?＋記号2ページ＋Space長押し→指を離さず上下スワイプ→選択した行で指を離す | 押下位置の真上に吹き出し表示、選択行を強調し、指を離した位置で言語が切り替わる。メニュー外ではキャンセル、空白の誤入力なし |
| M07 | 変換訂正 | nihongo→英字訂正→Space/Enter | 重複・欠落なく確定 |
| M08 | 設定 | 保存と戻る操作 | 設定保存、ステータスバー非重複 |

## 2026-10-25の統合版実機確認記録

- **テスト対象APK**: PR #1149 のコミット `965dbf33bb20300a2c906168cfbf980d930f64ad` に対応するQAデバッグAPK（[Actions #40182600362](https://github.com/KoheiTakada/Meltypetest/actions/runs/40182600362)）。
- **端末**: Pixel 10 Pro Fold、Android 17。
- **実機テスト状態**: 2026-10-25、ユーザー本人から「実機確認完了」の報告を受領。最新版に対する再確認として記録する。端末上の操作自体はCIの自動実行ではない。
- **証拠の限界**: M01〜M08各項目の個別成否、実際の文字列、画面寸法、スクリーンショットは受領していない。そのため、個別テストの実測値や8件それぞれのPASSを捏造せず、**総括の完了報告**として記録する。
- **自動テスト**: API35の計測 **18/18 PASS**、JVM **444/444 PASS**。これはユーザー報告とは独立したCI確認（[実行ログ](https://github.com/KoheiTakada/Meltypetest/actions/runs/40182600362)）。
- **ソースからの再構築**: [ForkのAndroid QA #40182575249](https://github.com/KoheiTakada/Meltype/actions/runs/40182575249)で成功。公開用署名・他機種互換性の検証とは区別する。

## 4. CI / 証跡
Actionsはビルド時のSHAとGradle単体テストJUnit XML、Android計測テストのケース別JUnit XML・ログ・端末条件をArtifactとJob Summaryで公開する。失敗時もArtifactを保存する。再検証の際は同じブランチ・コミットをcheckoutしてコマンドを再実行する。

テストコードは `android/app/src/androidTest/`、JVMテストは `android/engine/src/test/`・`android/app/src/test/` にあり、この文書だけでテストを代替しない。

## 2026-10-29：スペース長押し言語ピッカーの追加QA

- 対象: `feat/android-bilingual-adaptive-ime` の最新ソースSHA（旧APKと区別する）。
- 左端・中央・右端でスペースを300ms長押しし、指の周辺に吹き出しが現れること。端での画面外はみ出しがないこと。
- 指を離さず日本語・English間で上下にスワイプするとハイライトが移動し、その行で離したときだけ切り替わること。
- 押したままメニューの外へ移動して離したら切り替えず、スペースも入らないこと。
- 絵文字ON/OFFで項目数が正しく変わること。折りたたみ開閉・画面回転中のタッチキャンセルも確認。
- `LanguagePickerHitTest`（境界判定）と `KeyboardViewTouchTest`（単一タッチを長押しからUPまで維持）は自動テストである。
- ビルドAPKは `.github/workflows/android-qa.yml` の `android-qa-report-and-debug-apk` に含まれる。
