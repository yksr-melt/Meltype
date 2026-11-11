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
    ./gradlew --no-daemon :mozc:connectedDebugAndroidTest
    ls -la build/qa

出力: instrumentation.log（生ログ）、instrumentation.xml（JUnit XML）、test-cases.md（ケース別成否）、summary.md、device-info.txt。実行スクリプトは実際の @Test fun 一覧と計測結果を照合し、未実行・予期しない・失敗ケースを検出する。

アプリの計測は下表のクラスに加え、文字削除・タッチ操作の検証と、選んだキーボードで実際に打つ ImeLaunchDeviceTest も実行する。Coreのデータディレクトリはプロセスごとに固定されるため、別のディレクトリを使うブリッジ試験は別プロセスで実行する。実IMEの入力試験も含む3回の計測結果を集計する。各ケースの結果は `test-cases.md` と `instrumentation.xml` に記録する。Mozcの計測は別のGradleタスクで実行し、結果を `mozc/build/outputs/androidTest-results/connected/` に記録する。

コード: [LanguageDetectionDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/LanguageDetectionDeviceTest.kt) / [CoreEditingDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/CoreEditingDeviceTest.kt) / [CoreConfigDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/CoreConfigDeviceTest.kt) / [ImeLaunchDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/ImeLaunchDeviceTest.kt) / [SettingsScreenDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/SettingsScreenDeviceTest.kt) / [DeleteLengthDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/DeleteLengthDeviceTest.kt) / [KeyboardViewTouchTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/KeyboardViewTouchTest.kt) / [MeltypeCoreBridgeDeviceTest.kt](../app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/MeltypeCoreBridgeDeviceTest.kt) / [MozcConverterTest.kt](../mozc/src/androidTest/kotlin/io/github/ninjin0802/keyboard/mozc/MozcConverterTest.kt)。

### 日英判定・設定の計測テストの入力と期待値
| ID | @Test fun | 入力・操作 | 期待値 |
|---|---|---|---|
| A01 | englishStaysLatin | hello / meeting | hello / meeting（英字保持） |
| A02 | japaneseRomajiBecomesKana | arigatou / konnichiwa | ありがとう / こんいちわ（デスクトップ版と同じく nn は ん） |
| A03 | mixedLanguagesWithoutManualToggle | hello arigatou | hello ありがとう |
| A04 | choicesKeepTheCoreIndicesForClicks | Coreの一覧 hello,help、変換中の一覧2件 | Coreの番号0/1を保持し、入力中も変換中もCoreの一覧だけを出す |
| A05 | typingWithoutCoreCandidatesShowsNoChoices | 入力中（一覧なし） | 候補を出さない（固定の候補を足さない） |
| A06 | emojiPreferenceFiltersPredictionsWithoutShiftingIndices | ありがとう😊, ありがとうございます | 絵文字OFFでは絵文字付きだけ除外し、残りはCoreの番号のまま |
| A07 | settingsPersistJapaneseSpaceAndEmojiPreferences | 日本語スペース/絵文字をOFF→ON | 設定を読み直しても反映 |
| A08 | typingShowsOnlyTheCoreCandidates | nihongo→Enter | 入力中はCoreの一覧の候補だけ（ひらがな・カタカナ・英字の固定の候補なし）、確定後は候補なし |
| A09 | digitsStayInCompositionUntilExplicitEnter | arigatou→テンキー123 | Enter前に未確定文字が消えない |
| A10 | candidateBarTextIsReadableAndHasBreathingRoom | 410dpバー＋長い日本語候補 | 高さ≥68dp、文字・余白を保持 |
| A11 | exteriorMarginsStayUnclickableWhileLetterKeysWork | 400dp幅で左右余白・上端・Qを判定 | 余白にキーなし、Qは認識 |
| A12 | englishPredictionsCanBeCommittedOnAndroid | hell→英語予測候補をクリック（CandidateClicked） | Native Coreの予測語を確定 |
| A13 | translucentKeyboardHasOnlyTopRoundedHairline | 装飾をビットマップへ描画 | 上端は半透明、内部は透明 |
| A14 | malformedResponseLeavesTheEditorAlone | 項目の欠けたCoreの応答 | 例外になり、入力欄は書き換えない |
| A15 | removeBeforeReplacesTheTextBeforeTheCaret | removeBefore=今日は、text=京は | キャレットの前を消してから入れる |
| A16 | composingTextIsNotSentAsTextBeforeTheCaret | 「abc 」のあとにari | field.before に変換中の文字を含めない |
| B01 | homeHasGboardStyleCategoriesWithoutDemoOrCompletedSteps | 設定ホームを開く | 4カテゴリがあり試し書き等がない |
| B02 | liveConversionSwitchChangesAndPersistsItsValue | ライブ変換を切替 | 表示値と保存値が一致し、Core の config.json の LiveConversion に書かれる |
| B03 | systemBarsAreRespectedOnHomeAndDetailScreens | 実Insets・合成Insetsを設定 | 上端/下端のシステムUIと非重複 |
| B04 | toolbarBackAndSystemBackBothReturnToSettingsHome | 画面内戻る→再入→OS Back | 両方とも設定ホームに復帰 |
| B05 | unfoldedScreenKeepsCategoriesBesideActiveSettings | 設定を幅700dp以上/未満で表示 | 広画面2ペイン、狭画面スクロール |
| C01 | aRecommitReplacesThePreviousWordInTheEditor | i want␣ | I want␣（前の語の確定し直しが入力欄に正しく入り、キャレットは末尾） |
| C02 | aCaretMovedElsewhereKeepsThePreviousWord | i␣Enter のあと CaretMoved→want␣ | キャレット移動前に確定した語を書き換えない |
| C03 | removeBeforeTakesAwayTheComposingTextFirst | 「い 」＋変換中 want → removeBefore=「い 」 | 変換中の文字を消してから前を消し、I want␣ を入れる |
| C04 | moveCaretBackPutsTheCaretInsideTheCommit | 「」を moveCaretBack=1 で確定 | キャレットが括弧の中 |
| C05 | leavingAnEditorWhileComposingKeepsTheTextThereAndStartsAfresh | 欄Aで ka → 欄を移る → 欄Bで ki Enter | 欄Aは か、欄Bは き（持ち越さない） |
| C06 | anEditorWithoutItsOwnTextSendsNoTextAndCannotRemove | 読めない入力欄（TYPE_NULL） | before・after を送らず canRemove=false |
| C07 | theSelectedTextIsSentOnlyWhenTheEditorReportedASelection | abcd の bc を選択 | selection=bc、選択がないと送らない |
| C08 | aCommittedWordCanBeRecommittedWhenCaretHasNotMoved | i␣Enter→want␣、キャレットを動かさない | 文脈が英語と判明したとき、直前の語を I に確定し直せる |
| D01 | liveConversionTakesEffectFromTheNextKey | config.json の LiveConversion を OFF→ON、kyouha | OFF は きょうは、ON は 今日は（キーボードを作り直さずに効く） |
| D02 | bracketsPutTheCaretInsideWhenTheSettingIsOn | CursorIntoBrackets を ON、[]Enter | 「」の中にキャレット |
| D03 | settingOneItemKeepsTheRestOfTheFile | 項目の多い config.json の1項目を変える | ほかの項目・順序はそのまま。一時ファイルを残さない |
| D04 | aFileThatCannotBeReadIsLeftAlone | 壊れた config.json | 書かずに失敗を返し、ファイルはそのまま |
| D05 | aNewFileNamesTheSchemaAndTheVersion | config.json がない | $schema と SettingsVersion（schema の既定）を書く |
| E01 | selectedKeyboardOpensAndTypesThroughOnscreenTouch | 選んだキーボードで asd、日本語欄で a | asd、日本語は Native Core を通る |
| E02 | caretMovesByTheAppStopRecommit | 日本語欄で i␣Enter のあとアプリがキャレットを先頭へ動かし末尾へ戻して want␣ | 前の語を確定し直さない |
| E03 | leavingAnEditorCommitsTheCompositionThere | 日本語欄で ka → 別の欄 → 戻って a Enter | 日本語欄の か は確定され、かあ になる |
| E04 | ownEditorWritesDoNotLookLikeCaretMoves | 日本語欄で i␣Enter→want␣、キャレットを外から動かさない | I に確定し直し、キーボード自身の編集は外からの移動と数えない |

A04〜A06、A14〜A15、C03〜C04はCoreの応答の形（ImeOutput）の固定JSONで検証する。A01〜A03、A08〜A09、A12、A16、C01〜C02、C05、C08、D01〜D02はNative Coreとの接続を含む。E01〜E04は端末で選んだキーボードの画面のキーを押して確かめる。実機での画面開閉は自動計測とは別の手動テストである。

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
| M07 | 変換確定 | nihongo→Space→Enter | 重複・欠落なく確定。候補バーにはCoreの一覧だけが出る |
| M08 | 設定 | 保存と戻る操作 | 設定保存、ステータスバー非重複 |
| M09 | 日本語QWERTY | i␣Enter のあと前の文をタップしてから want␣ | タップした所の前の語を書き換えない |
| M10 | 日本語QWERTY | 変換中に入力欄の別の所をタップ | 出ていた文字のまま確定し、続けて打つと新しい位置に入る |
| M11 | 設定 | ライブ変換をOFFにして、キーボードを閉じずに打つ | 次のキーから漢字にならない |

## 2026-10-25の統合版実機確認記録

- **テスト対象APK**: PR #1149 のコミット `965dbf33bb20300a2c906168cfbf980d930f64ad` に対応するQAデバッグAPK（[Actions #40182600362](https://github.com/KoheiTakada/Meltypetest/actions/runs/40182600362)）。
- **端末**: Pixel 10 Pro Fold、Android 17。
- **実機テスト状態**: 2026-10-25、ユーザー本人から「実機確認完了」の報告を受領。対象は上記 `965dbf3` であり、後続コミットの実機検証とは区別する。端末上の操作自体はCIの自動実行ではない。
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
