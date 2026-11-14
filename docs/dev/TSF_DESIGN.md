# Meltype IME (TSF) の設計

Windows 版の Meltype を、Windows 正規の IME の仕組み (TSF, Text Services Framework) でも動くようにしたもの。
打った文字が入力欄そのものに下線付きで入り、変換の候補だけを入力位置の下に一覧で出す。
判定・変換のロジック (Meltype.Core) は書き直さず、Mac 版・Linux 版と同じく `MeltypeSession` を使う。

使い方は [USAGE.md](../USAGE.md) の「Meltype IME」、セキュリティの考え方は [SECURITY.md](../../SECURITY.md)。

## 全体の形

```
アプリのプロセス (メモ帳・Chrome・VS Code …)            Meltype.exe (タスクトレイに常駐)
┌──────────────────────────────┐            ┌───────────────────────────────┐
│ MeltypeTip.dll (C++, TSF)       │  名前付き   │ TipServer                        │
│  - キーを受け取る               │  パイプ     │  - つながり・入力欄ごとに        │
│  - 未確定の文字を下線付きで置く  │ ◀───────▶ │    MeltypeSession                │
│  - 候補の一覧を描く              │ 1 メッセージ│  - 変換 (Mozc + Zenzai)           │
│                                  │  = 1 JSON   │  - 学習データ・ユーザー辞書        │
└──────────────────────────────┘            └───────────────────────────────┘
```

| 場所 | 中身 |
|---|---|
| `native/tip/` | TSF のテキストサービス (C++)。`build.ps1` で x64 / x86 の stub (`MeltypeTip.dll`) と impl (`MeltypeTipImpl.dll`) をビルド、`Register-Tip.ps1` で登録 |
| `src/Meltype.Windows/Tip/TipServer.cs` | Meltype.exe の中のパイプのサーバー。DLL が読めない入力欄の事実 (アプリ・ウィンドウのタイトル・UI Automation の要素・ゲームらしさ) を足して `ImeHost` に渡す |
| `src/Meltype.Windows/Keyboard/KeyboardFront.cs` | もう 1 つのフロント (Meltype キーボード)。キーボードフックのキーを同じ `ImeHost` に渡し、確定を SendInput で入れ、変換ボックスを自分の窓に出す |
| `packaging/meltype-ime.ps1` | インストール・アンインストールで IME を登録する / 外す (install.ps1・uninstall.ps1・Install-Meltype.ps1 から使う) |
| `src/Meltype.Core/Ime/` | フロント (TSF・IBus・fcitx5) と本体の境目 (`ImeEvent` / `ImeOutput`)。入力モードとモードを切り替えるキーの意味は `ImeSession` が決める |
| `src/Meltype.Core.Tests/ImeContractTests.cs` | 境目の契約テスト。判断を持たないフロントで出来事を送り、確定・表示・モード・受け取るキーを確かめる (どの OS でも流れる) |
| `src/Meltype.Windows.Tests/TipHarness.cs` | `--tip-server` (サーバーだけ動かす)、`--tip-client <打鍵>` (パイプに打鍵を送って応答を出す) |
| `src/Meltype.Windows.Tests/TipEndToEnd.cs` | `--tip-e2e [画像の保存先]` (登録した DLL を RichEdit の入力欄で動かす。数秒間ウィンドウが前面に出る。Meltype.exe は止めておく。`MELTYPE_E2E_EXTERNAL_SERVER=1` なら常駐している Meltype.exe につなぐ) |

設定「入力モードをカーソルの近くに表示」(`ModeIndicator`。どのフロントも同じ設定) で、明示的に入力モードを切り替えたとき (と入力欄に入ったとき)、入力位置の近くに「あ / A」を表示します。既定 (`Default`) は、Meltype IME では表示しません (`Settings.EffectiveModeIndicator`)。
出すかと表示する時間 (応答の `state.modeIndicatorMs`) は本体が決め、表示は Meltype.exe (`TipServer`) が出します。DLL は、入力モードのキーとタスクバーのボタンの出来事に、入力スレッドの ID と TSF の選択範囲の位置 (`GetTextExt`) を付けます。
位置が分からなければ常駐側でキャレットを探し、それも分からなければマウス位置へ戻します (Terminal の入力欄の左上を入力位置と推測しません)。表示時に別の入力先へ移っていたら表示しません。
アプリ・Windows による自動的な開閉やパスワード欄への移動だけでは表示されません。

impl を更新した後の確認では、入力先アプリの再起動は要りません (下の「stub と impl」)。stub (`Dll.cpp`・`Stub.cpp`・`Common.cpp`) を変更したときは、入力先アプリのプロセスを再起動します。Windows Terminal は複数ウィンドウでプロセスを共有するため、新しいウィンドウを作るだけでは古い stub が残ることがあります。

自動開閉の検証は次で行えます。短時間テスト画面が前面に出ます。Windows の実際の TSF コンパートメントと通常欄・パスワード欄を使い、プログラムからの開閉は明示的な切り替え (`ModeToggleRequested`) として送らず、タスクバーのボタンでは 1 回ずつ送ることを確認します。パイプ通信だけを記録用に置き換えており、通常の Meltype 本体・設定・学習データには接続しません。前面に出せない場合は終了コード 77 でスキップします。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File native/tip/tests/build-mode-indicator-test.ps1
native/tip/bin/tests/ModeIndicatorEvents.exe
```

`--tip-e2e` の制約: Windows は、人のキーボード操作がしばらく無い状態だと、プログラムが送り込んだキー (SendInput) も、TSF のキー処理を直接呼んだキー (ITfKeystrokeMgr) も、IME に渡さない。
このときは TSF の状態がすべて正常なのに、IME のキー処理が呼ばれず、英字のまま入る。人が操作している間に流すと通る。
IME を変えたときは、このテストに加えて、メモ帳などで実際に打って確かめる。

### なぜ DLL に本体を入れないか

TSF の IME は、入力するアプリ全部のプロセスに読み込まれる。本体 (Meltype.Core) を DLL に入れると、アプリごとに辞書を読み込んでメモリを食い、
学習データ・ユーザー辞書を複数のプロセスが同時に書き換えて壊し、Mozc のヘルパーもアプリの数だけ起動する。
そのため、Google 日本語入力 (Mozc) と同じく、本体は 1 つのプロセス (Meltype.exe) に置き、DLL は薄い殻にして通信する。

### stub と impl

DLL は、COM に登録する stub (`MeltypeTip.dll`) と、TSF の処理を持つ impl (`MeltypeTipImpl.dll`) に分割する。プロセスがロードした DLL はプロセスの終了まで残るので、
stub が impl をロードし、Meltype の更新後は、アプリを再起動せずに impl を差し替える (#1670)。

- stub は、TSF が IME のオブジェクトに QueryInterface する `ITfTextInputProcessorEx`・`ITfDisplayAttributeProvider` を impl の `TextService` に委譲するだけ。
  下の表のシンクは、impl の `TextService` が TSF に直接 Advise する
- impl は `C:\Program Files\Meltype\tip\<x64|x86>\impl\<SHA-256>\MeltypeTipImpl.dll` に配置する。フォルダー名は署名前のバイナリの SHA-256。
  SHA-256 をファイル名にすると、Gecko (Firefox・Thunderbird など) がロードを拒否する (ファイル名の `.` より前が 16 桁以上の 16 進数の DLL を拒否する)。
  stub は impl の中身の SHA-256 を照合しない。改竄は置き場所 (Program Files) の ACL で防ぐので、管理者以外が書けるフォルダーに移すなら、中身も照合する。
  stub がロードする impl は、stub と同じフォルダーの `MeltypeTipImpl.dll.sha256` で指定する。Meltype.exe からパイプで指定する形にはしない
  (パイプのサーバーを偽装したプロセスによる、残っている古い impl へのダウングレードを防ぐ)。
  `Register-Tip.ps1` は、stub を登録できてから `MeltypeTipImpl.dll.sha256` を書き換える (stub の登録に失敗して前の stub に戻したときは、前の impl を指したまま)
- impl は要求に自分の SHA-256 (`impl`) を付ける。Meltype.exe は、同梱の `tip\<x64|x86>\MeltypeTipImpl.dll.sha256` と一致しなければ `{"stale":true}` を返す (`TipBuild`)。
  impl は stub のウィンドウに `kStubImplStale` を PostMessage し、stub は `MeltypeTipImpl.dll.sha256` を読み直して、変わっていたら前の impl を `Deactivate` し、
  新しい impl を同じ `ITfThreadMgr`・`TfClientId` で `ActivateEx` する (未確定文字列は、前の impl の `Deactivate` が確定する)。
  差し替わるまでのキーは、Meltype.exe の応答がタイムアウトしたときと同じ扱いになる
- 別ビルドの impl とは通信しないので、パイプのプロトコルに後方互換性は要らない。
  互換性が要るのは stub と impl の ABI (`Globals.h` の `MeltypeTipCreateFn`・`kStubImplStale`) で、変更したら `kStubImplAbi` を上げる。
  stub は impl の `MeltypeTipAbi` が自分の `kStubImplAbi` と一致しなければロードした impl を使わないので、ABI を変更した更新は、アプリを再起動するまでそのアプリに反映されない
- 前の impl は `FreeLibrary` しない (プロセスの終了まで残る)。ファイルは、どのプロセスもロードしていなければ次の登録で削除する
- 差し替えられないアプリ (stub と impl に分割する前の DLL が残っている・古い stub が今の impl をロードできない) は、登録済みの impl が Meltype.exe と同じビルドになってから 3 秒たっても、同じ接続から別ビルドの要求が届くことで検出する。
  分割前の DLL は更新前から開いているアプリすべてに残るので、Meltype.exe は PC の再起動を求める通知を 1 回だけ出す。通知を見逃しても打てない理由が分かるように、そのアプリで打っている間は、入力モードの表示と同じ所にアプリの再起動を求める表示も出す (届いた要求から決めるので、覚えておく状態は持たない)。分割前の DLL には今までの形式で応答しない (後方互換レイヤーを持たない)
- 登録済みの impl が Meltype.exe と別ビルドのときは、Meltype キーボードで動かす (`TipServer.IsUsable`)。Meltype IME を再登録せずに Meltype.exe だけを更新すると、こうなる
  (setup.exe のサイレント実行・前に UAC を断ったあとの自動更新・Meltype IME の再登録後に失敗して前の版に戻した更新)。
  動作モードが Meltype IME なら、Meltype.exe が起動のたびに再登録するかを聞く (`ImeDeclined` は見ない。聞いたことを覚える状態は持たない)
- `tip` フォルダーが無い Meltype.exe (`dotnet run`・テスト・ポータブル版) は、impl を照合しない

プロトコルの版の番号 (`ImeEvent.version`) では照合しない。項目名を変更しても番号が `1` のままで、不一致を検出できていなかった。

## MeltypeTip.dll・MeltypeTipImpl.dll

| インターフェース | やること |
|---|---|
| ITfTextInputProcessorEx | IME として起動・終了 |
| ITfKeyEventSink | キーを受け取り、サーバーに渡して「使ったか」を返す |
| ITfThreadMgrEventSink / ITfTextEditSink | フォーカスが変わった・キャレットが変換中の文字の外に出た → 確定する。変換していないときにキャレットが動いた・文字が変わった → `CaretMoved` ですぐ伝える (コードの行か・前の語を確定し直すかは本体がキャレットの前の文字で決め、タスクバーの「あ / A」も応答で合わせる) |
| ITfCompositionSink | アプリの側が変換中の文字を確定したとき (打っている途中で、キャレットがその後ろのままなら、次のキーで変換中に戻して続ける。空のノートに最初の文字を入れたときに、アプリが入力欄を作り直して確定することがあるため) |
| ITfDisplayAttributeProvider | 下線の種類 (打っている途中は点線、変換した文節は細線、選んでいる文節は太線) |
| ITfCompartmentEventSink | 日本語 ⇔ 英数 (IME の ON/OFF)。半角/全角 と、タスクバーの「あ / A」のボタン (ITfLangBarItemButton) で切り替える |

ゲームなど、候補を自分で描くアプリ向けの UILess (ITfCandidateListUIElement) には対応していない。

### キーの流れ

1. `OnTestKeyDown`: 変換中ならキーを使う。変換していないときは、最後の応答の `state.capture` が受け取ると言ったキーだけを使う
   (`keys` の入力モードのキー・切り替えキー、`word` の英字・数字・@・_、`symbol` の記号、`space`・`shiftSpace`)。Ctrl・Alt・Windows キー付き・ほかのソフトが送り込んだ文字 (VK_PACKET) は使わない
   (変換中なら、その場で確定してからアプリに通す。その場で確定できないアプリでは受け取り、2. で確定してから送り直す)。
   まだ応答が無い・つながらないときは、日本語入力として文字と入力モードのキーを使う。
   パスワード欄か (入力欄の種類 InputScope) も、ここでキーごとに読み直す (読んだ結果は、続く同じキー・同じ入力欄の `OnKeyDown` で 1 回だけ使う)。
   変換中の文字が前の入力欄に残っていれば、先に確定する
2. `OnKeyDown`: 同期の編集セッションの中で、本体に `KeyPressed` の出来事 (キー・IME の開閉・入力欄の事実) を送り、応答 (`state` と `result`) を受け取る。
   入力欄の事実は、キャレット (変換中なら変換中の文字) の前 4000 文字と後ろ 20 文字、変換中か、入力を覚えないように求める入力欄か。
   変換中なのに本体に入力の続きが無ければ (つなぎ直した)、本体は新しく始めずに受け持たない。DLL は変換中の文字を消さずに確定する。
   応答の `state` は毎回まるごと置き換え (`revision` が古いものは捨てる)、`state.imeOpen` を IME の開閉 (コンパートメント) に入れる
3. `commits` を確定し、`view` を未確定の文字として置く。`removeBefore` のある確定は、キャレットの前のその文字を消して入れ直す (確定し直し)。
   `removeBefore` が入力欄の文字と違えば (アプリが書き換えた・前の文字を読めない) 消さない。1 つの応答の中の確定は、前の確定の後ろに続けて入れる
   (クリックで確定したとき、クリックした所に入らないように)。クリック・別の入力欄で確定するときの `CommitRequested` には `caret.moved` を付け、確定し直さない。
   アプリが変換中の文字を確定したときは `CommittedByApp` を送る (本体は確定を返さない)
   通常のコンテキストでは、`moveCaretBack` のある確定と同じ編集セッションで選択位置を移し、続きも反映する。
   一時コンテキストでは、入力を消費して括弧だけを確定した場合に、非同期の編集セッションで入力先・確定文字・選択位置を確かめ、アプリに左キーを送る。
   修飾キーが押されている場合は、左キーによる選択範囲の変更やショートカットの実行を避けるため送らない。
   後続の確定または未確定文字がある場合は、閉じ括弧を保持するため括弧の末尾に続ける。
   確定に使った Enter は消費し、アプリへ送り直さない。
4. 変換中で候補が 2 つ以上あるか、「もしかして」があれば、候補の一覧を出す
5. 受け取ったあとでサーバーが使わなかったキー (変換中に一時停止した・時間内に応答が無かったときの Enter など) は、確定してから SendInput でアプリに送り直す
   (送り直したキーには印 dwExtraInfo を付けて見分ける)。送り込みを許されていないアプリ (ストアアプリなど) では届かないことがあるので、
   前もって分かるキー (Ctrl・Alt 付き、パスワード欄) は受け取らずに通す。送り直したキーは、そのあいだに打ったキーより後に届く (まれに順番が入れ替わる)
6. 時間内に応答が無ければ、変換中の文字を確定し、打った文字はその場で入れる (送り直すと、その間に打った次のキーより後に届いて順番が入れ替わる)

### 何もしない場面

- Meltype.exe より高い権限で動いているアプリ (ふつうは管理者として動いているアプリ)、サインインや UAC の画面 (Meltype.exe の権限より上の入力を扱わない。
  UAC を切っていて、アプリも Meltype.exe も管理者として動く PC では使える)。
  入力モードのキー (半角/全角・US 配列の Alt + `・英数・かな) だけは受け取って、DLL が IME の開閉だけを切り替える (アプリに通すと、PowerShell (PSReadLine) は文字の無いキーを @ として入れる)
- パスワードの入力欄 (IME を無効にしている入力欄と、入力欄の種類 InputScope がパスワード・暗証番号のもの)。
  種類は Windows が持つもの (`GetProperty`) と、アプリが知らせるもの (`GetAppProperty`) の両方を読み、どちらかがパスワードならパスワード欄とする。どちらも無い入力欄は、種類を決めていないふつうの入力欄とする
  (ブラウザーはこれで、パスワード欄では IME を無効にする)。種類の値はあるのに中身を読めないときは、パスワード欄として扱う (問い合わせ自体ができないときは、種類を決めていない入力欄と同じ)。
  変換中に入力欄がパスワード欄になったとき (IME を無効にした・種類を変えた) は、変換中の文字を確定して、次のキーから受け取らない
  入力を覚えないように求める入力欄 (InputScope が IS_PRIVATE。ブラウザーのシークレット / InPrivate ウィンドウ) はパスワード欄にしない。
  入力欄の事実に `"private":true` を付け、本体はその入力欄では学習しない
- Meltype.exe が動いていない・一時停止中・動作モードが Meltype IME でない・アプリ別設定で OFF か「ゲーム」のアプリ (応答の `state.handlesInput` が false)

## 通信

- 名前付きパイプ `\\.\pipe\Meltype.Tip.<ユーザーの SID>`。メッセージ単位で、1 メッセージが 1 JSON。
  要求は `{"impl":impl の SHA-256,"thread":入力スレッド,"x":…,"y":…,"event":{ImeEvent}}` (`TipRequest`)。`thread`・`x`・`y` は Meltype.exe が入力モードの表示と、自分の UI スレッドからの要求を見分けるのに使い、本体には渡さない。
  応答は `ImeOutput` (`{"v":…,"state":{…},"result":{…}}`)。別ビルドの impl には `{"stale":true}` (上の「stub と impl」)、パースできない要求には `{"active":false,…}` を返し、DLL はどちらもキーを消費しない
- 状態 (`StateRequested`) は、起動したとき・前面に戻ったとき・アプリや Windows が IME を開閉したときに受け取り直す (待つのは最大 100ms)。変換中だけ、背景の変換の結果を別のつながりの `{"wait":世代}` で待つ
- 1 キーごとに同期で 1 往復。待つのは最大 800ms。応答が無ければ、変換中の文字はそのまま確定し、打った文字はその場で入れる。
  書き込み・読み取りが時間内に終わらなかったら、閉じて、次の要求でつなぎ直す。5 秒以内にまた時間切れになったら、そのあと 3 秒はつながない (応答しない Meltype.exe を、どのアプリでも打鍵のたびに待たないように)。
  1 回の時間切れで 3 秒つながないと、Meltype.exe の画面のスレッドが少しふさがっただけで、その間に打ったキーが打った文字のまま入る。`StateRequested`・`CaretMoved` など、待つ時間の短い要求の時間切れは数えない。
  Meltype.exe を起動し直して前のつながりが切れていたら (書き込みが ERROR_NO_DATA などで失敗する)、つなぎ直して 1 回だけ送り直す
- 入力の本体は、パイプのつながりごとに持つ (つなぎ直した DLL が古い入力の続きを拾わない。ほかのアプリの入力を読めない)
- アクセス権: 本人・SYSTEM・管理者と、ストアアプリ (AppContainer) には読み書きだけ。整合性レベル Low の印を付ける (ストアアプリは Low で動く)
- 最初のパイプは FirstPipeInstance で作り、先に作られていたらサーバーを始めない
- DLL は、つないだ相手が自分と同じユーザーの、整合性レベル Medium 以上のプロセスかを確かめる (Meltype.exe が止まっている間に、権限の低いプログラムが同じ名前のパイプを作って打鍵を受け取るのを防ぐ)。
  相手が自分 (IME を読み込んだアプリ) より低い権限 (整合性レベル) なら、つながない。相手を調べられない (ほかのユーザーのプロセスなど) ときもつながない。
  ただし自分が Low (ストアアプリなど) で相手のプロセスを開けないときは、パイプの所有者が自分と同じユーザーかだけを確かめる (Meltype.exe もパイプの印を Low にしているので、印では見分けられない)
  そのため、UAC が有効な PC で Meltype.exe を管理者として動かすと、ふつうのアプリからはつながらない (Meltype.exe はふつうの権限で動かす)
- つなぎかけて切れたパイプは DisconnectNamedPipe で戻して待ち直す (閉じると、ほかにつながりが無いときに独占と印が外れる)

## 候補の一覧

DLL の中で、Direct2D + DirectWrite で描く (絵文字もカラーで出る)。Windows 11 の Microsoft IME に寄せた見た目:
角丸、Windows のライト / ダークとアクセントの色に合わせる、縦に 1 列で番号 + 候補、選んでいる行は背景を薄く塗って左端にアクセントの縦線、
1 ページ 9 個 (2 ページ以上なら下に「3 / 27」)、「もしかして」は一覧の上、候補で 1.5 秒止まったら意味を右に出す、英訳の候補は右端に「英訳」。
出す位置は、選んでいる文節の左下 (`ITfContextView::GetTextExt`)。画面の下にはみ出すなら上に出す。

## Meltype.exe 側

- 動作モードは `Tsf` (Meltype IME) と `Keyboard` (Meltype キーボード)。どちらのフロントも事実を同じ `ImeHost` に送るだけで、入力の判断は Core がする。
  `MeltypeEngine` はアプリの部分 (設定・キーボードフックの取り込み・Ctrl + 半角/全角 の一時停止・打鍵数) だけを持ち、動作モードが Keyboard のときだけフックのキーを `KeyboardFront` に渡す
- 左右の Alt の単独押し: キーのシンクには Alt が届かないので、`TipServer.OnKey` が低レベルフックで見て、DLL の入力スレッドのメッセージ用のウィンドウに知らせる。
  DLL は押下を `KeyPressed`、解放を `KeyReleased` で送り、単独押しかは Core が決める。前面のスレッドの最後の応答が `capture.altKeys` のときだけ知らせる。
  解放は、単独押しでないとフックで分かるとき以外は 0xE8 のあとに送り直す (Core の答えは待たない)
- Meltype IME が登録されていて、このユーザーのキーボードの一覧にも入っていれば、一度だけ自動で Tsf にする。
  そのとき (とトレイで Meltype IME を選んだとき) は、いま使う入力方式も Meltype IME にする (ITfInputProcessorProfileMgr::ActivateProfile の TF_IPPMF_FORSESSION)
- Meltype.exe 自身の画面 (はじめに・設定など) でも使える。そこでは UI スレッドが DLL の中で応答を待って止まるので、
  UI スレッドからの要求 (要求の `thread` で見分ける) は、UI スレッドに移さずにパイプのスレッドで処理する (UI スレッドは止まっているので、ほかの処理と同時には動かない)
- `CompositionService.CreateSession` で、辞書・学習データ・変換エンジンを変換ボックスと共有したセッションを作る。すべて UI スレッドで動かす

## インストール

- IME の登録は HKLM に要る (HKCU だけでは ActivateProfile が失敗した) ので、インストールのときだけ UAC で管理者権限を求める。断ったら、今までどおり変換ボックスの方式で動く
- DLL は `C:\Program Files\Meltype\tip` に置く (管理者として動くアプリにも読み込まれるので、ユーザーが書き換えられない場所)。ロードされている stub はリネームして残し、次の登録で削除する
- 入れ直すたびに UAC を出さないように、stub と impl が同じ (署名する前の中身のハッシュ `MeltypeTip.dll.sha256`・`MeltypeTipImpl.dll.sha256` が同じ) なら登録し直さない。
  登録できてからハッシュを置くので、途中で失敗したら次に入れ直したときに登録し直す。新しい DLL を登録できなければ、前の DLL に戻して登録し直す。
  TIP のキーだけ残り、COM の登録 (`InprocServer32`) か DLL が無いとき (セキュリティソフトが登録を途中で止めたとき) も、DLL が同じでも登録し直す。
  断られたら覚えておき、Install.cmd・Install-Meltype.ps1 を自分で実行したときだけまた聞く
- 自動更新で入れる install.ps1 は、もう登録してあるときだけ Meltype IME を登録し直す (頼まれていないのに管理者権限の確認を出さない)。
  まだ入れていない人には、Install.cmd を自分で実行したとき (-Ask) に聞く。
  Meltype IME が無かった版から更新した人には、Meltype.exe が起動したときに一度だけ聞き (`TsfOffered`)、「はい」ならインストール先の meltype-ime.ps1 で登録する。
  初めて起動したとき (インストーラーが聞いている)・前に Meltype IME を使っていたとき (`TsfIntroduced`)・前に断ったとき (ImeDeclined) は聞かない。
  ImeDeclined は、UAC で断ったときと、setup.exe で「Meltype IME を登録する」のチェックを外したときに残す
- 動作モードが Meltype IME なのに、ポータブル版か、ARM64 の Windows か、登録されていないか (登録を外した・ほかのユーザーがアンインストールした・TIP のキーだけ残り、64 ビットの `InprocServer32` か DLL が無い)、
  このユーザーのキーボードの一覧に無ければ、Meltype キーボードで動かす。起動したとき・config.json を読み直したとき・トレイや設定画面で変えたときのどれでも同じ。
  config.json は書き換えない (動かしている動作モードは `MeltypeEngine.Mode`)。Meltype IME を入れ直したら、Meltype.exe を起動し直すか、トレイで Meltype IME を選び直すと Meltype IME で動く
- 管理者として動かす前に、DLL が配布したときのハッシュ (`MeltypeTip.dll.package.sha256`、無ければ `.sha256`) と合うかを確かめる (壊れた DLL を登録しないため)。
  確かめてから使うまでにすり替えられないように、管理者として動く側は DLL と Register-Tip.ps1 を `C:\Program Files\Meltype` の下に写し、昇格する前に確かめたハッシュと合うものだけで登録する。
  その処理は、ファイルではなくコマンドとして渡す (ユーザーのフォルダーのスクリプトを、管理者として直接動かさない)
- 「言語と地域」のキーボードの一覧に Meltype を足す (今のユーザー)。既定の入力方式を自分で決めていなければ、Meltype にする (アンインストールで、Meltype のままなら戻す)
- アンインストールでは、一覧から外し、登録を外して DLL を消す。アプリが使っていて消せない DLL は、次に Windows を起動したときに消す。
  IME の登録は PC 全体なので、同じ PC のほかのユーザーも Meltype を使っていると、そのユーザーの Meltype IME も使えなくなる

## セキュリティ上の制限

- 同じユーザーの権限で動くほかのプログラムが Meltype.exe より先に同じ名前のパイプを作ると、Meltype IME はそのプログラムにつながり、打鍵とキャレットの前後の文字がそのプログラムに渡り、返ってきた文字が入力欄に入る。同じユーザーの権限で動くプログラムはもともとキーボードの入力を読めるので、それ以上の被害にはならない
- ストアアプリ (自分も Low で、相手のプロセスを調べられない) は、パイプの所有者が同じユーザーかだけを確かめる。同じユーザーの Low のプログラムが先に作ったパイプにはつながってしまう
- ストアアプリは自分で入力の本体を作って変換を頼めるので、変換の候補の順番 (学習した結果)・ユーザー辞書の語を間接的に知り、学習データに書き込める。ほかのアプリの入力やファイルは読めない
- 登録のときに管理者として動くスクリプトと DLL は、ユーザーが書き換えられるインストール先から読む。ハッシュを確かめる前に、同じユーザーの権限で動くプログラムがハッシュのファイルやスクリプトごと書き換えていれば防げない (インストーラーをユーザーのフォルダーに置く作りに共通の制限)

## 未対応・確かめていないこと

- UILess (候補を自分で描くアプリ)、ARM64 (ARM64 の Windows ではインストールのときに Meltype IME を入れない)
- 確かめたのは、RichEdit の入力欄 (`--tip-e2e`)、パイプ (`--tip-client`)、ストアアプリと同じ AppContainer からの接続。
  実際のアプリ (メモ帳・Chrome・VS Code など)、表示スケール 150%、コード署名をしない DLL を読み込まないアプリがあるかは、まだ広くは確かめていない

**自動テストで確かめている範囲** (issue #1215):

| 層 | テスト | CI | 確かめること |
|---|---|---|---|
| 入力の本体 (MeltypeSession) | `src/Meltype.Core.Tests/SessionFacadeTests.cs`・`InputRegressionTests.cs` など | Windows・Linux・Mac | 打鍵ごとの確定・表示、予測の候補と Tab、変換中のショートカット、コードの行、英数の自動判定 |
| フロントと本体の境目 (ImeHost の JSON) | `src/Meltype.Core.Tests/ImeContractTests.cs` | Windows・Linux・Mac | 入力 → 変換 → 確定 → 続けて入力、BackSpace・Esc、確定で変換を終える、キャレットを動かしたら前の確定を直さない、入力欄 (instance) ごとに分ける・閉じる、つなぎ直したフロントの続きを受け持たない、受け持たなくなったときの確定、入力モードのキー、受け取るキー、コードの行 |
| TipServer の要求と入力モードの表示 | `src/Meltype.Windows.Tests/TipIndicatorTests.cs` | Windows (build.yml の test) | 要求の位置の読み取り、明示的な切り替えで設定が ON のときだけ表示する |
| パイプ | `--tip-client` | 手で流す | 実際のパイプでのやり取り |
| DLL を含めた全体 | `--tip-e2e` | 手で流す (人が操作しているデスクトップが要る) | 登録した DLL で、RichEdit の入力欄に実際に打つ |

- **契約テストが通っても、実際の Windows のデスクトップでキーが届き、正しく打てることは保証しない。** DLL (native/tip、C++) の TSF の処理 (キーの受け取り・未確定の文字の表示・フォーカスの通知・候補の一覧の描画) と、OS がキーを IME に配る部分は、自動テストでは通らない
- 自動テストに無いもの: Tab で選んだ予測の候補のあとに続けて打つとき (#974 で入った動き)、選んだ文字の再変換 (#313、#1182 で作っている)、設定の読み直し (config.json の書き換え) をはさむ流れ
- DLL まで含めた実際の打鍵のテスト (`--tip-e2e`) を定期的に流すなら、隔離した対話型の Windows で、任意または定期の実行にする。外部の Pull Request のコードを、個人の PC や権限の強いセルフホストランナーで無条件に動かさない
- 不具合の報告を直したら、Core で確かめられることは Core のテストに、フロントとの境目の受け渡しで起きることは `ImeContractTests` に、操作の順番どおりに足す

## ビルドに要るもの

- .NET 10 SDK
- Visual Studio Build Tools の「C++ によるデスクトップ開発」(MSVC と Windows SDK)。無ければ `Install-Meltype.ps1` は Meltype IME を入れずに続ける
