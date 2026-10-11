<a name="meltype-ime-tsf-の設計"></a>
<img src="images/headings/tsf-design/title.svg" alt="Meltype IME (TSF) の設計" height="80">


Windows 版の Meltype を、Windows 正規の IME の仕組み (TSF, Text Services Framework) でも動くようにしたもの。
打った文字が入力欄そのものに下線付きで入り、変換の候補だけを入力位置の下に一覧で出す。
判定・変換のロジック (Meltype.Core) は書き直さず、Mac 版・Linux 版と同じく `MeltypeSession` を使う。

使い方は [USAGE.md](USAGE.md) の「Meltype IME」、セキュリティの考え方は [SECURITY.md](../SECURITY.md)。

<br>

<a name="全体の形"></a>
<img src="images/headings/tsf-design/01.svg" alt="全体の形" height="53"><br>


```
アプリのプロセス (メモ帳・Chrome・VS Code …)            Meltype.exe (タスクトレイに常駐)
┌──────────────────────────────┐            ┌───────────────────────────────┐
│ MeltypeTip.dll (C++, TSF)       │  名前付き   │ TipServer                        │
│  - キーを受け取る               │  パイプ     │  - つながり・入力欄ごとに        │
│  - 未確定の文字を下線付きで置く  │ ◀───────▶ │    MeltypeSession                │
│  - 候補の一覧を描く              │ 1 メッセージ│  - 変換 (Mozc + Microsoft IME)    │
│                                  │  = 1 JSON   │  - 学習データ・ユーザー辞書        │
└──────────────────────────────┘            └───────────────────────────────┘
```

| 場所 | 中身 |
|---|---|
| `native/tip/` | TSF のテキストサービス (C++)。`build.ps1` で x64 / x86 をビルド、`Register-Tip.ps1` で登録 |
| `src/Meltype/Tip/TipServer.cs` | Meltype.exe の中のパイプのサーバー |
| `packaging/meltype-ime.ps1` | インストール・アンインストールで IME を登録する / 外す (install.ps1・uninstall.ps1・Install-Meltype.ps1 から使う) |
| `src/Meltype.Tests/TipHarness.cs` | `--tip-server` (サーバーだけ動かす)、`--tip-client <打鍵>` (パイプに打鍵を送って応答を出す) |
| `src/Meltype.Tests/TipEndToEnd.cs` | `--tip-e2e [画像の保存先]` (登録した DLL を RichEdit の入力欄で動かす。数秒間ウィンドウが前面に出る。Meltype.exe は止めておく。`MELTYPE_E2E_EXTERNAL_SERVER=1` なら常駐している Meltype.exe につなぐ) |

`--tip-e2e` の制約: Windows は、人のキーボード操作がしばらく無い状態だと、プログラムが送り込んだキー (SendInput) も、TSF のキー処理を直接呼んだキー (ITfKeystrokeMgr) も、IME に渡さない。
このときは TSF の状態がすべて正常なのに、IME のキー処理が呼ばれず、英字のまま入る。人が操作している間に流すと通る。
IME を変えたときは、このテストに加えて、メモ帳などで実際に打って確かめる。

<a name="なぜ-dll-に本体を入れないか"></a>
<img src="images/headings/tsf-design/s01.svg" alt="なぜ DLL に本体を入れないか" height="40">


TSF の IME は、入力するアプリ全部のプロセスに読み込まれる。本体 (Meltype.Core) を DLL に入れると、アプリごとに辞書を読み込んでメモリを食い、
学習データ・ユーザー辞書を複数のプロセスが同時に書き換えて壊し、Mozc のヘルパーもアプリの数だけ起動する。
そのため、Google 日本語入力 (Mozc) と同じく、本体は 1 つのプロセス (Meltype.exe) に置き、DLL は薄い殻にして通信する。

<br>

<a name="meltypetipdll"></a>
<img src="images/headings/tsf-design/02.svg" alt="MeltypeTip.dll" height="53"><br>


| インターフェース | やること |
|---|---|
| ITfTextInputProcessorEx | IME として起動・終了 |
| ITfKeyEventSink | キーを受け取り、サーバーに渡して「使ったか」を返す |
| ITfThreadMgrEventSink / ITfTextEditSink | フォーカスが変わった・キャレットが変換中の文字の外に出た → 確定する。変換していないときにキャレットが動いた → 次のキーで伝える |
| ITfCompositionSink | アプリの側が変換中の文字を確定したとき (打っている途中で、キャレットがその後ろのままなら、次のキーで変換中に戻して続ける。空のノートに最初の文字を入れたときに、アプリが入力欄を作り直して確定することがあるため) |
| ITfDisplayAttributeProvider | 下線の種類 (打っている途中は点線、変換した文節は細線、選んでいる文節は太線) |
| ITfCompartmentEventSink | 日本語 ⇔ 英数 (IME の ON/OFF)。半角/全角 と、タスクバーの「あ / A」のボタン (ITfLangBarItemButton) で切り替える |

ゲームなど、候補を自分で描くアプリ向けの UILess (ITfCandidateListUIElement) には対応していない。

<a name="キーの流れ"></a>
<img src="images/headings/tsf-design/s02.svg" alt="キーの流れ" height="40">


1. `OnTestKeyDown`: 変換中ならキーを使う。変換していないときは、文字を生むキー (英字・記号・数字) なら使う。Ctrl・Alt・Windows キー付き・ほかのソフトが送り込んだ文字 (VK_PACKET) は使わない
   (変換中なら、その場で確定してからアプリに通す。その場で確定できないアプリでは受け取り、2. で確定してから送り直す)。
   パスワード欄か (入力欄の種類 InputScope) も、ここでキーごとに読み直す (読んだ結果は、続く同じキー・同じ入力欄の `OnKeyDown` で 1 回だけ使う)。
   変換中の文字が前の入力欄に残っていれば、先に確定する
2. `OnKeyDown`: 同期の編集セッションの中で、サーバーに `{vk, ch, mods, before, after, process, moved, composing}` を送り、`SessionResult` (JSON) を受け取る。
   composing は変換中であること (サーバーに入力の続きが無ければ、新しく始めずに受け持たない。DLL は変換中の文字を消さずに確定する)。
   before / after はキャレットの前後 20 文字 (変換していないときだけ)。moved は、前に確定してからキャレットが動いたかもしれないこと
   (アプリに通したキー・クリック・別の入力欄)。サーバーはこれを見て、前に確定した語を次の語の文脈で確定し直さない (消す位置がずれるため)
3. `commits` を確定し、`view` を未確定の文字として置く。`deleteBefore` は前に確定した文字を消して入れ直す (確定し直し)。
   消す文字 `expect` が入力欄の文字と違えば (アプリが書き換えた・前の文字を読めない) 消さない。1 つの応答の中の確定は、前の確定の後ろに続けて入れる
   (クリックで確定したとき、クリックした所に入らないように)。クリック・別の入力欄・アプリが確定したときの `commit` には `moved` を付け、確定し直さない
4. 変換中で候補が 2 つ以上あるか、「もしかして」があれば、候補の一覧を出す
5. 受け取ったあとでサーバーが使わなかったキー (変換中に一時停止した・時間内に応答が無かったときの Enter など) は、確定してから SendInput でアプリに送り直す
   (送り直したキーには印 dwExtraInfo を付けて見分ける)。送り込みを許されていないアプリ (ストアアプリなど) では届かないことがあるので、
   前もって分かるキー (Ctrl・Alt 付き、パスワード欄) は受け取らずに通す。送り直したキーは、そのあいだに打ったキーより後に届く (まれに順番が入れ替わる)
6. 時間内に応答が無ければ、変換中の文字を確定し、打った文字はその場で入れる (送り直すと、その間に打った次のキーより後に届いて順番が入れ替わる)

<a name="何もしない場面"></a>
<img src="images/headings/tsf-design/s03.svg" alt="何もしない場面" height="40">


- Meltype.exe より高い権限で動いているアプリ (ふつうは管理者として動いているアプリ)、サインインや UAC の画面 (Meltype.exe の権限より上の入力を扱わない。
  UAC を切っていて、アプリも Meltype.exe も管理者として動く PC では使える)。
  半角/全角 だけは受け取って、日本語 ⇔ 英数 の表示を切り替える (アプリに通すと、PowerShell (PSReadLine) は文字の無いキーを @ として入れる)
- パスワードの入力欄 (IME を無効にしている入力欄と、入力欄の種類 InputScope がパスワード・暗証番号のもの)。
  種類は Windows が持つもの (`GetProperty`) と、アプリが知らせるもの (`GetAppProperty`) の両方を読み、どちらかがパスワードならパスワード欄とする。どちらも無い入力欄は、種類を決めていないふつうの入力欄とする
  (ブラウザーはこれで、パスワード欄では IME を無効にする)。種類の値はあるのに中身を読めないときは、パスワード欄として扱う (問い合わせ自体ができないときは、種類を決めていない入力欄と同じ)。
  変換中に入力欄がパスワード欄になったとき (IME を無効にした・種類を変えた) は、変換中の文字を確定して、次のキーから受け取らない
  入力を覚えないように求める入力欄 (InputScope が IS_PRIVATE。ブラウザーのシークレット / InPrivate ウィンドウ) も、学習しないで使う仕組みが無いので何もしない
- Meltype.exe が動いていない・一時停止中・動作モードが Meltype IME でない・アプリ別設定で OFF か「ゲーム」のアプリ (サーバーが active: false を返す)

<br>

<a name="通信"></a>
<img src="images/headings/tsf-design/03.svg" alt="通信" height="53"><br>


- 名前付きパイプ `\\.\pipe\Meltype.Tip.<ユーザーの SID>`。メッセージ単位で、1 メッセージが 1 JSON
- 1 キーごとに同期で 1 往復。待つのは最大 800ms (初めての変換で Mozc を起動するときがあるため)。応答が無ければ、変換中の文字はそのまま確定し、打った文字はその場で入れる。
  書き込み・読み取りが時間内に終わらなかったら、そのあと 3 秒はつながない (応答しない Meltype.exe を、どのアプリでも打鍵のたびに待たないように)
- 入力の本体は、パイプのつながりごとに持つ (つなぎ直した DLL が古い入力の続きを拾わない。ほかのアプリの入力を読めない)
- アクセス権: 本人・SYSTEM・管理者と、ストアアプリ (AppContainer) には読み書きだけ。整合性レベル Low の印を付ける (ストアアプリは Low で動く)
- 最初のパイプは FirstPipeInstance で作り、先に作られていたらサーバーを始めない
- DLL は、つないだ相手が自分と同じユーザーの、整合性レベル Medium 以上のプロセスかを確かめる (Meltype.exe が止まっている間に、権限の低いプログラムが同じ名前のパイプを作って打鍵を受け取るのを防ぐ)。
  相手が自分 (IME を読み込んだアプリ) より低い権限 (整合性レベル) なら、つながない。相手を調べられない (ほかのユーザーのプロセスなど) ときもつながない。
  そのため、UAC が有効な PC で Meltype.exe を管理者として動かすと、ふつうのアプリからはつながらない (Meltype.exe はふつうの権限で動かす)
- つなぎかけて切れたパイプは DisconnectNamedPipe で戻して待ち直す (閉じると、ほかにつながりが無いときに独占と印が外れる)

<br>

<a name="候補の一覧"></a>
<img src="images/headings/tsf-design/04.svg" alt="候補の一覧" height="53"><br>


DLL の中で、Direct2D + DirectWrite で描く (絵文字もカラーで出る)。Windows 11 の Microsoft IME に寄せた見た目:
角丸、Windows のライト / ダークとアクセントの色に合わせる、縦に 1 列で番号 + 候補、選んでいる行は背景を薄く塗って左端にアクセントの縦線、
1 ページ 9 個 (2 ページ以上なら下に「3 / 27」)、「もしかして」は一覧の上、候補で 1.5 秒止まったら意味を右に出す、英訳の候補は右端に「英訳」。
出す位置は、選んでいる文節の左下 (`ITfContextView::GetTextExt`)。画面の下にはみ出すなら上に出す。

<br>

<a name="meltypeexe-側"></a>
<img src="images/headings/tsf-design/05.svg" alt="Meltype.exe 側" height="53"><br>


- 動作モードに `Tsf` (Meltype IME) を足した。Tsf のときは、キーボードフックは何もしない。今までの方式 (変換ボックス・IME 自動切替) も残している
- Meltype IME が登録されていて、このユーザーのキーボードの一覧にも入っていれば、一度だけ自動で Tsf にする。
  そのとき (とトレイで Meltype IME を選んだとき) は、いま使う入力方式も Meltype IME にする (ITfInputProcessorProfileMgr::ActivateProfile の TF_IPPMF_FORSESSION)
- Meltype.exe 自身の画面 (はじめに・設定など) でも使える。そこでは UI スレッドが DLL の中で応答を待って止まるので、
  UI スレッドからの要求 (要求に付けたスレッドの ID で見分ける) は、Invoke せずにパイプのスレッドで処理する (UI スレッドは止まっているので、ほかの処理と同時には動かない)
- `CompositionService.CreateSession` で、辞書・学習データ・変換エンジンを変換ボックスと共有したセッションを作る。すべて UI スレッドで動かす

<br>

<a name="インストール"></a>
<img src="images/headings/tsf-design/06.svg" alt="インストール" height="53"><br>


- IME の登録は HKLM に要る (HKCU だけでは ActivateProfile が失敗した) ので、インストールのときだけ UAC で管理者権限を求める。断ったら、今までどおり変換ボックスの方式で動く
- DLL は `C:\Program Files\Meltype\tip` に置く (管理者として動くアプリにも読み込まれるので、ユーザーが書き換えられない場所)。使っている DLL は名前を変えて残し、次の登録で消す
- 入れ直すたびに UAC を出さないように、DLL が同じ (署名する前の中身のハッシュ `MeltypeTip.dll.sha256` が同じ) なら登録し直さない。
  登録できてからハッシュを置くので、途中で失敗したら次に入れ直したときに登録し直す。新しい DLL を登録できなければ、前の DLL に戻して登録し直す。
  断られたら覚えておき、Install.cmd・Install-Meltype.ps1 を自分で実行したときだけまた聞く
- 自動更新で入れる install.ps1 は、もう登録してあるときだけ Meltype IME を登録し直す (頼まれていないのに管理者権限の確認を出さない)。
  まだ入れていない人には、Install.cmd を自分で実行したとき (-Ask) に聞く
- 動作モードが Meltype IME なのに登録されていなければ (登録を外した・ほかのユーザーがアンインストールした)、起動したときに Meltype キーボードに戻して保存する
  (入れ直したら、トレイで選び直す)。登録していなければ、トレイでも設定画面でも Meltype IME は選べない
- 管理者として動かす前に、DLL が配布したときのハッシュ (`MeltypeTip.dll.package.sha256`、無ければ `.sha256`) と合うかを確かめる (壊れた DLL を登録しないため)。
  確かめてから使うまでにすり替えられないように、管理者として動く側は DLL と Register-Tip.ps1 を `C:\Program Files\Meltype` の下に写し、昇格する前に確かめたハッシュと合うものだけで登録する。
  その処理は、ファイルではなくコマンドとして渡す (ユーザーのフォルダーのスクリプトを、管理者として直接動かさない)
- 「言語と地域」のキーボードの一覧に Meltype を足す (今のユーザー)。既定の入力方式を自分で決めていなければ、Meltype にする (アンインストールで、Meltype のままなら戻す)
- アンインストールでは、一覧から外し、登録を外して DLL を消す。アプリが使っていて消せない DLL は、次に Windows を起動したときに消す。
  IME の登録は PC 全体なので、同じ PC のほかのユーザーも Meltype を使っていると、そのユーザーの Meltype IME も使えなくなる

<br>

<a name="未対応確かめていないこと"></a>
<img src="images/headings/tsf-design/07.svg" alt="未対応・確かめていないこと" height="53"><br>


- Meltype キーボードから移せていない機能: コードエディター・ターミナルの「コメントと文字列の中だけ日本語」、アプリの種類ごとの「最初は英数」、
  カーソルの近くの「あ」「A」の表示、選んだ文字の再変換
- UILess (候補を自分で描くアプリ)、ARM64 (ARM64 の Windows ではインストールのときに Meltype IME を入れない)
- 確かめたのは、RichEdit の入力欄 (`--tip-e2e`)、パイプ (`--tip-client`)、ストアアプリと同じ AppContainer からの接続。
  実際のアプリ (メモ帳・Chrome・VS Code など)、表示スケール 150%、コード署名をしない DLL を読み込まないアプリがあるかは、まだ広くは確かめていない

<br>

<a name="ビルドに要るもの"></a>
<img src="images/headings/tsf-design/08.svg" alt="ビルドに要るもの" height="53"><br>


- .NET 10 SDK
- Visual Studio Build Tools の「C++ によるデスクトップ開発」(MSVC と Windows SDK)。無ければ `Install-Meltype.ps1` は Meltype IME を入れずに続ける
