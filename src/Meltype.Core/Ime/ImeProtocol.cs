// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meltype.Composition;

namespace Meltype.Ime;

// フロント (TSF・Mac・IBus・fcitx5・Android) と ImeHost のやり取り。フロントは OS から読んだ事実だけを ImeEvent で渡し、
// どうするか (確定・表示・モード・どのキーを受け取るか) は ImeHost が ImeOutput で返す。
// 同じプロセスのフロントは ImeHost.Handle(ImeEvent) を呼び、別プロセスのフロント (TSF の DLL) は 1 メッセージ 1 JSON で送る。
// フロントは state を毎回まるごと置き換え、result をそのまま適用する。表示・時間・番号の出し分けもフロントは決めない。
// 判断がフロントに残っているのは、ImeTrait.Game・NonText の推測と、TSF の DLL のパスワード欄の判定 (打鍵を Meltype.exe に送らない) だけ。
// JSON の項目名を変えたら、JSON を読む 5 つのフロント (native/tip/TextService.cpp・linux/fcitx5/meltype.cpp・linux/ibus-engine-meltype・
// mac/Sources/MeltypeIME/NativeCore.swift・android/app/src/main/kotlin/io/github/ninjin0802/keyboard/app/core/ImeJson.kt) を直す (FrontProtocolTests が突き合わせる)。

/// <summary>フロントが送る出来事。JSON の名前はメンバー名のまま。知らない名前の出来事は、警告をログに出して受け持たない。</summary>
public enum ImeEventType
{
    KeyPressed,
    /// <summary>
    /// キーを離した (Key.Vk と Key.Interrupted)。送るのは次の 2 つだけ:
    /// ImeCapture.AltKeys のあいだに KeyPressed で送った左右の Alt (Windows のフロント)。
    /// ワークアラウンド: 親指シフトの同時打鍵のために、ImeCapture.KeyReleases のあいだに ImeKey.ReleaseFollows を付けて送ったキー
    /// (送るのは Meltype キーボードだけで、ほかのフロントでは親指シフトの同時打鍵が効かない)。
    /// </summary>
    KeyReleased,
    /// <summary>キーを使わない日本語 ⇔ 英数の切り替え (タスクバーのボタンなど)。</summary>
    ModeToggleRequested,
    /// <summary>キーを使わずに日本語を選んだ (入力メニューなど)。</summary>
    JapaneseModeSelected,
    /// <summary>キーを使わずに英数を選んだ (入力メニューなど)。</summary>
    AlphanumericModeSelected,
    /// <summary>未確定の内容を確定する (フォーカスが移ったなど)。確定する文字は応答で返す。</summary>
    CommitRequested,
    /// <summary>OS・アプリが、変換中の文字を表示のまま確定した (IBus のフォーカスの移動・TSF の OnCompositionTerminated)。応答で確定はしない。</summary>
    CommittedByApp,
    /// <summary>前の応答の一覧 (view.candidates) の Index 番目をクリックした。予測変換の候補なら確定する。</summary>
    CandidateClicked,
    /// <summary>前の応答の一覧の Index 番目にマウスで選択を動かした。予測変換の候補は確定しない。</summary>
    CandidateHighlighted,
    /// <summary>背景の変換の結果が届いた。</summary>
    ConversionFinished,
    /// <summary>
    /// フロントが自分で書き換えていないのに、キャレットが動いた・入力欄の文字が変わった (クリック・フロントがアプリに通したキー・アプリの編集)。
    /// フロントは見つけたらすぐ送る (Core は打ったキーからキャレットの位置を追いかけない)。
    /// </summary>
    CaretMoved,
    /// <summary>今の状態だけを受け取る (設定を変えたかもしれないときなど)。入力の本体は作らない。</summary>
    StateRequested,
    /// <summary>
    /// フロントの IME のインスタンス (Instance) がなくなった (TSF の Deactivate)。未確定の文字は捨てる。
    /// 1 つのインスタンスですべての入力欄を受け持つフロント (Meltype キーボード・Mac・IBus・fcitx5・Android) は送らない。
    /// </summary>
    InstanceDestroyed,
}

public sealed record ImeEvent
{
    /// <summary>プロトコルの版。<see cref="ImeHost.Version"/> と違えば、ImeHost は何も受け持たない。</summary>
    public required int Version { get; init; }

    public required ImeEventType Type { get; init; }

    /// <summary>フロントの IME のインスタンス (TSF ではスレッド) ごとにフロントが振る ID。</summary>
    // init にすると、source-gen の JSON は項目が無いときに初期値ではなく null を入れる
    public string Instance { get; set; } = "";

    public ImeKey? Key { get; init; }

    public ImeField? Field { get; init; }

    /// <summary>OS が持つ IME の開閉 (TSF の GUID_COMPARTMENT_KEYBOARD_OPENCLOSE)。OS が持たないフロントは送らない。</summary>
    public bool? ImeOpen { get; init; }

    /// <summary>CaretMoved・CommitRequested・CommittedByApp: キャレットがどう動いたか。</summary>
    public ImeCaret? Caret { get; init; }

    /// <summary>CandidateClicked・CandidateHighlighted: 前の応答の一覧 (view.candidates) の何番目か。</summary>
    public int Index { get; init; }
}

/// <summary>
/// 押したキー。Ch は入力される文字 (無ければ 0)。Vk は Windows の仮想キーコードで、文字を入力するキーなら省いてよい (Ch から決める)。
/// 文字の無いキー (Enter・矢印・F6〜F10・半角/全角 0x19・英数 0xF0・かな 0x15・変換 0x1C・無変換 0x1D など) は、対応する仮想キーコードにする。
/// </summary>
public sealed record ImeKey
{
    public int Vk { get; init; }
    public int Ch { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    /// <summary>Windows キー・Command・Super。</summary>
    public bool Command { get; init; }
    /// <summary>押したままの自動の繰り返し (OS が分かるときだけ)。入力モードのキーの繰り返しでは切り替えない。</summary>
    public bool Repeat { get; init; }
    /// <summary>1 回のキーで入る文字が複数のスカラーのとき (結合文字・ZWJ の絵文字。Mac)、その文字列。Ch は 0。本体は未確定の内容を確定して、キーをアプリに通させる。</summary>
    public string? Text { get; init; }
    /// <summary>KeyReleased: 離すまでのあいだに、ほかのキー (修飾キーを含む)・マウスのボタンを押していた (このキーより先に押していたものも含む)。</summary>
    public bool Interrupted { get; init; }
    /// <summary>ワークアラウンド (<see cref="ImeEventType.KeyReleased"/>): このキーを離したら KeyReleased を送る。false なら本体は押した直後に離したとみなす。</summary>
    public bool ReleaseFollows { get; init; }
}

/// <summary>OS (・Windows のフロントが調べた) 入力欄の性質。JSON の名前はメンバー名のまま。知らない性質のある出来事は、警告をログに出して受け持たない。</summary>
public enum ImeTrait
{
    /// <summary>ターミナル (IBus の InputPurpose.TERMINAL・fcitx5 の CapabilityFlag::Terminal)。</summary>
    Terminal,
    /// <summary>パスワード・暗証番号 (IBus の InputPurpose.PASSWORD / PIN・fcitx5 の CapabilityFlag::Password・TSF の IS_PASSWORD・UI Automation の IsPassword・Android の inputType のパスワード)。</summary>
    Password,
    /// <summary>入力を覚えないように求める (TSF の IS_PRIVATE・IBus の InputHints.PRIVATE・fcitx5 の CapabilityFlag::Sensitive・Android の IME_FLAG_NO_PERSONALIZED_LEARNING)。</summary>
    Private,
    /// <summary>ゲームらしいウィンドウ。事実でなく、Windows のフロントがウィンドウの形などの手がかりから推測した結果。</summary>
    Game,
    /// <summary>全画面表示のウィンドウ (設定「全画面のアプリでは止める」)。</summary>
    Fullscreen,
    /// <summary>
    /// フォーカスのある所が入力欄ではない。キーボードフックのフロントは UI Automation から推測し (事実でない)、
    /// IBus はどの入力欄にもフォーカスが無いときの仮の入力コンテキスト (クライアント名「fake」) で知る。
    /// </summary>
    NonText,
    /// <summary>フロントがアプリに文字を送れない (管理者として動くアプリ・Meltype 自身の画面。キーボードフックのフロント)。</summary>
    Unreachable,
    /// <summary>入力先で日本語以外の入力言語 (キーボード) を選んでいる (キーボードフックのフロント)。</summary>
    OtherLanguage,
}

/// <summary>UI Automation で調べたフォーカスのある要素 (Windows)。</summary>
public sealed record ImeElement(string Name, string ClassName);

public sealed record ImeField
{
    /// <summary>
    /// 入力欄の確定した文字のうち、キャレットより前 (変換中なら変換中の文字より前)。読めなければ null で、空文字列は入力欄の先頭。
    /// 出来事ごとに読み直して送る。コードの入力欄の判定に 4000 文字まで使う。
    /// </summary>
    public string? Before { get; init; }
    /// <summary>キャレット (変換中なら変換中の文字) より後ろ。読めなければ null。</summary>
    public string? After { get; init; }
    /// <summary>選択している文字 (変換キーで再変換する)。選択していない・読めなければ null。</summary>
    public string? Selection { get; init; }
    /// <summary>フロントの入力欄に変換中の文字がある。</summary>
    public bool Composing { get; init; }
    /// <summary>
    /// アプリ。アプリ別設定の名前と同じ形にする: Windows はプロセスの実行ファイル名 (Code.exe)、macOS は bundle ID (com.microsoft.VSCode)、
    /// Linux は fcitx5 の program・IBus のクライアント名のプログラムの部分 (code)、Android はパッケージ名 (com.example.app)。分からなければ null。
    /// </summary>
    public string? App { get; init; }
    /// <summary>ウィンドウのタイトル (README.md などの文章のファイルを開いているか)。分からなければ null。</summary>
    public string? Title { get; init; }
    /// <summary>前面のウィンドウのクラス名 (Windows。Qt のアプリかで確定した文字の入れ方を決める)。分からなければ null。</summary>
    public string? WindowClass { get; init; }
    public ImeElement? Element { get; init; }
    // init にすると、source-gen の JSON は項目が無いときに初期値ではなく null を入れる
    public IReadOnlyList<ImeTrait> Traits { get; set; } = [];
    /// <summary>キャレットの前の文字を消せる入力欄か (消せなければ確定し直さない)。</summary>
    // init にすると、source-gen の JSON は項目が無いときに初期値ではなく false を入れる
    public bool CanRemove { get; set; } = true;
}

public sealed record ImeCaret
{
    /// <summary>キャレットが動いたかもしれない (クリック・フロントが通したキー・別の入力欄)。前の語を確定し直さない。</summary>
    public bool Moved { get; init; }
    /// <summary>キャレットを動かした、フロントがアプリに通したキー。0 はキーなし (クリックなど)。null は文字が変わっただけ。</summary>
    public int? PassedKey { get; init; }
    /// <summary>PassedKey のとき Ctrl を押していたか。</summary>
    public bool Ctrl { get; init; }
    /// <summary>フォーカスが入力欄に入った (設定によっては入力モードを表示する)。</summary>
    public bool FocusEntered { get; init; }
    /// <summary>フォーカスが入力欄から外れた (日本語にしていたコードの行を英数に戻す)。</summary>
    public bool FocusLeft { get; init; }
}

/// <summary>言語モード。JSON の名前はメンバー名のまま。</summary>
public enum ImeLanguageMode
{
    Japanese,
    Alphanumeric,
}

/// <summary>出来事を処理したあとの状態。フロントは毎回まるごと置き換え、自分では状態を持たない。</summary>
public sealed record ImeState
{
    /// <summary>この入力欄を受け持つか。false ならフロントは何もせずにキーを通す。</summary>
    public bool HandlesInput { get; init; }
    /// <summary>OS の IME の開閉をこれに合わせる (ImeEvent.ImeOpen を送るフロント)。</summary>
    public bool ImeOpen { get; init; }
    /// <summary>打ったら効く言語モード (表示する「あ / A」)。コードの行では、日本語を選んでいても ImeOpen のまま Alphanumeric になる。</summary>
    public ImeLanguageMode LanguageMode { get; init; } = ImeLanguageMode.Alphanumeric;
    /// <summary>
    /// 入力モードを表示する時間 (ミリ秒。0 なら出さない)。明示的に切り替えた・入力欄に入ったとき (設定「入力モードをカーソルの近くに表示」)。
    /// 変換中は出さない。フロントはキャレットの近くに、この時間だけ出す。
    /// </summary>
    public int ModeIndicatorMs { get; init; }
    /// <summary>変換していないときに受け取るキー。キーを受け取るかをその場で決めなければならないフロント (TSF) が使う。</summary>
    public required ImeCapture Capture { get; init; }
    /// <summary>状態の世代。非同期に受け取った古い状態で、新しい状態を上書きしないように比べる。</summary>
    public long Revision { get; init; }
}

/// <summary>
/// 変換していないときに受け取るキー。変換中はすべてのキーを受け取る。
/// PrintableAscii は、Ctrl・Alt・Command を押さずに打った ASCII の文字 (! から ~ まで。空白は含まない) を受け取るか。PrintableAsciiExcept はそのうち受け取らない文字。
/// Shortcuts は文字に関係なく受け取るキー (Space・Shift+Space・入力モードのキーなど。TSF は修飾キー付きのものを PreserveKey で登録する)。
/// </summary>
public sealed record ImeCapture
{
    public bool PrintableAscii { get; init; }
    public string PrintableAsciiExcept { get; init; } = "";
    public IReadOnlyList<ImeShortcut> Shortcuts { get; init; } = [];
    /// <summary>変換中に Ctrl と一緒に押しても変換ボックスで使うキー (設定「Control キーの割り当て」)。ほかの Ctrl・Alt・Win と一緒のキーは、確定してアプリに通す。</summary>
    public IReadOnlyList<ImeShortcut> CtrlKeysWhileComposing { get; init; } = [];
    /// <summary>ワークアラウンド (<see cref="ImeEventType.KeyReleased"/>): 親指シフトのかな配列なので、送ったキーを離したことも受け取る。</summary>
    public bool KeyReleases { get; init; }
    /// <summary>
    /// 左右の Alt (0xA4・0xA5) の押下を KeyPressed で、解放を KeyReleased で送る (設定「左 Alt で英数 / 右 Alt で日本語」。Windows のフロント)。
    /// 単独押しかは本体が決める。押下は本体に送ってからアプリに通す (consumed にならない)。解放はいったん止めて、
    /// consumed なら何もしないキー 0xE8 のあとに、そうでなければそのまま送り直す。アプリには押下と解放の組がそろって届き、
    /// 単独押しで切り替えたときは、間に 0xE8 があるのでアプリの単独押しの操作 (メニューバーに移る) にならない。
    /// Meltype IME は Alt を Meltype.exe のフックで見て DLL に知らせる (TSF のキーのシンクに Alt が届かない) ので、本体の答えを待てず、
    /// いつも 0xE8 のあとに送り直す (単独押しでない解放は、送り直しても単独押しの操作にならない)。
    /// </summary>
    public bool AltKeys { get; init; }
}

/// <summary>Vk は Windows の仮想キーコード。Modifiers は Shift=1, Ctrl=2, Alt=4, Win=8 (<see cref="Input.ShortcutModifiers"/>)。</summary>
public sealed record ImeShortcut(int Vk, int Modifiers);

/// <summary>
/// 出来事への応答。result (<see cref="SessionResult.ToJson"/>) をフロントはこう適用する:
/// <list type="bullet">
/// <item>consumed: true ならキーをアプリに通さない。</item>
/// <item>commits: 順に text を確定する (前の確定の後ろに続ける)。removeBefore があれば、キャレットの前のその文字を消してから入れる (本体がキャレットの前と照合済み)。
/// moveCaretBack は確定したあとにキャレットを左へ戻す数 (UTF-16)。insertByPaste はキーを送らずに貼り付けで入れる (キーを送って入れるフロントだけが使う)。
/// currentWord は、確定し直すときに今確定した語 (TSF が Chrome で編集を 2 回に分けるのに使う)。</item>
/// <item>view: null なら変換中の表示を消す。text (確定される形)・clauses・hint・spellingSuggestion・typedKeys は、あればそのまま出す。</item>
/// <item>view.candidates: null なら一覧を出さない。番号は numbered のときだけ出す。selectedMeaning は選んでいる候補で meaningDelayMs 止まったら出す。
/// 番号で選ぶ出来事 (CandidateClicked・CandidateHighlighted) の index は、この一覧の番号。</item>
/// </list>
/// state.modeIndicatorMs が 0 でなければ、「あ / A」をキャレットの近くにその時間だけ出す。
/// </summary>
public sealed record ImeOutput(ImeState State, SessionResult Result)
{
    /// <summary>{"version":…,"state":{…},"result":{…}}。result は <see cref="SessionResult.ToJson"/> と同じ形。</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", ImeHost.Version);
            writer.WritePropertyName("state");
            JsonSerializer.Serialize(writer, State, ImeJsonContext.Default.ImeState);
            writer.WritePropertyName("result");
            writer.WriteRawValue(Result.ToJson(), skipInputValidation: true);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ImeEvent))]
[JsonSerializable(typeof(ImeState))]
internal sealed partial class ImeJsonContext : JsonSerializerContext;
