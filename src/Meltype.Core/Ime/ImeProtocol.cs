// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meltype.Composition;

namespace Meltype.Ime;

// フロント (TSF・Mac・IBus・fcitx5) と ImeHost のやり取り。フロントは OS から読んだ事実だけを ImeEvent で渡し、
// どうするか (確定・表示・モード・どのキーを受け取るか) は ImeHost が ImeOutput で返す。
// 同じプロセスのフロントは ImeHost.Handle(ImeEvent) を呼び、別プロセスのフロント (TSF の DLL) は 1 メッセージ 1 JSON で送る。

public enum ImeEventType
{
    [JsonStringEnumMemberName("key")] Key,
    /// <summary>キーを使わない日本語 ⇔ 英数の切り替え (タスクバーのボタンなど)。</summary>
    [JsonStringEnumMemberName("toggle")] Toggle,
    /// <summary>モードを指定して切り替える (入力メニューで選んだなど)。</summary>
    [JsonStringEnumMemberName("setMode")] SetMode,
    /// <summary>未確定の内容を確定する (フォーカスが移ったなど)。確定する文字は応答で返す。</summary>
    [JsonStringEnumMemberName("commit")] Commit,
    /// <summary>OS・アプリが、変換中の文字を表示のまま確定した (IBus のフォーカスの移動・TSF の OnCompositionTerminated)。応答で確定はしない。</summary>
    [JsonStringEnumMemberName("committed")] Committed,
    [JsonStringEnumMemberName("select")] Select,
    /// <summary>背景の変換の結果が届いた。</summary>
    [JsonStringEnumMemberName("refresh")] Refresh,
    /// <summary>
    /// フロントが自分で書き換えていないのに、キャレットが動いた・入力欄の文字が変わった (クリック・フロントがアプリに通したキー・アプリの編集)。
    /// フロントは見つけたらすぐ送る (Core は打ったキーからキャレットの位置を追いかけない)。
    /// </summary>
    [JsonStringEnumMemberName("caret")] Caret,
    /// <summary>今の状態だけを受け取る (設定を変えたかもしれないときなど)。入力の本体は作らない。</summary>
    [JsonStringEnumMemberName("sync")] Sync,
    [JsonStringEnumMemberName("close")] Close,
}

public sealed record ImeEvent
{
    /// <summary>プロトコルの版。<see cref="ImeHost.Version"/> と違えば、ImeHost は何も受け持たない。</summary>
    public required int V { get; init; }

    public required ImeEventType Type { get; init; }

    /// <summary>入力欄 (TSF ではスレッド) ごとにフロントが振る ID。</summary>
    // init にすると、source-gen の JSON は項目が無いときに初期値ではなく null を入れる
    public string Sid { get; set; } = "";

    public ImeKey? Key { get; init; }

    public ImeField? Field { get; init; }

    /// <summary>OS が持つ IME の開閉 (TSF の GUID_COMPARTMENT_KEYBOARD_OPENCLOSE)。OS が持たないフロントは送らない。</summary>
    public bool? Open { get; init; }

    /// <summary>caret: どう動いたか。</summary>
    public ImeCaret? Caret { get; init; }

    /// <summary>select: 前の応答の一覧 (view.list) の何番目か。</summary>
    public int Index { get; init; }

    /// <summary>select: 選択を動かすだけで、予測変換の候補を確定しない (マウスでなぞったとき)。</summary>
    public bool Highlight { get; init; }

    /// <summary>setMode: 日本語にするか。</summary>
    public bool Japanese { get; init; }
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
    /// <summary>
    /// 修飾キー (Vk は左右を分けたもの: 左 Alt 0xA4・右 Alt 0xA5 など) を、ほかのキー・マウスのボタンを押さずに離した。離したときに送る。
    /// consumed なら、フロントは離したことがアプリの操作 (メニューバーに移るなど) にならないようにする。
    /// </summary>
    public bool Tap { get; init; }
}

/// <summary>OS (・Windows のフロントが調べた) 入力欄の性質。</summary>
public enum ImeTrait
{
    /// <summary>ターミナル (IBus の InputPurpose.TERMINAL・fcitx5 の CapabilityFlag::Terminal)。</summary>
    [JsonStringEnumMemberName("terminal")] Terminal,
    /// <summary>パスワード・暗証番号 (IBus の InputPurpose.PASSWORD / PIN・fcitx5 の CapabilityFlag::Password・TSF の IS_PASSWORD・UI Automation の IsPassword)。</summary>
    [JsonStringEnumMemberName("password")] Password,
    /// <summary>入力を覚えないように求める (TSF の IS_PRIVATE・IBus の InputHints.PRIVATE・fcitx5 の CapabilityFlag::Sensitive)。</summary>
    [JsonStringEnumMemberName("private")] Private,
    /// <summary>ゲームらしいウィンドウ (Windows のフロントが調べた全画面などの手がかり)。</summary>
    [JsonStringEnumMemberName("game")] Game,
    /// <summary>全画面表示のウィンドウ (設定「全画面のアプリでは止める」)。</summary>
    [JsonStringEnumMemberName("fullscreen")] Fullscreen,
    /// <summary>フォーカスのある所が入力欄ではない (UI Automation。キーボードフックのフロント)。</summary>
    [JsonStringEnumMemberName("nonText")] NonText,
    /// <summary>フロントがアプリに文字を送れない (管理者として動くアプリ・Meltype 自身の画面。キーボードフックのフロント)。</summary>
    [JsonStringEnumMemberName("unreachable")] Unreachable,
    /// <summary>入力先で日本語以外の入力言語 (キーボード) を選んでいる (キーボードフックのフロント)。</summary>
    [JsonStringEnumMemberName("otherLanguage")] OtherLanguage,
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
    /// Linux は fcitx5 の program・IBus のクライアント名のプログラムの部分 (code)。分からなければ null。
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
    public int? Passed { get; init; }
    /// <summary>Passed のとき Ctrl を押していたか。</summary>
    public bool Ctrl { get; init; }
    /// <summary>フォーカスが入力欄に入った (設定によっては入力モードを表示する)。</summary>
    public bool Entered { get; init; }
    /// <summary>フォーカスが入力欄から外れた (日本語にしていたコードの行を英数に戻す)。</summary>
    public bool Left { get; init; }
}

/// <summary>出来事を処理したあとの状態。フロントは毎回まるごと置き換え、自分では状態を持たない。</summary>
public sealed record ImeState
{
    /// <summary>この入力欄を受け持つか。false ならフロントは何もせずにキーを通す。</summary>
    public bool Active { get; init; }
    /// <summary>OS の IME の開閉をこれに合わせる (ImeEvent.Open を送るフロント)。</summary>
    public bool Open { get; init; }
    /// <summary>表示するモード (「あ」なら true)。コードの行では Open のまま「A」になる。</summary>
    public bool Japanese { get; init; }
    /// <summary>
    /// 入力モードを表示する時間 (ミリ秒。0 なら出さない)。明示的に切り替えた・入力欄に入ったとき (設定「入力モードをカーソルの近くに表示」)。
    /// 変換中は出さない。フロントはキャレットの近くに、この時間だけ出す。
    /// </summary>
    public int IndicatorMs { get; init; }
    /// <summary>変換していないときに受け取るキー。キーを受け取るかをその場で決めなければならないフロント (TSF) が使う。</summary>
    public required ImeCapture Capture { get; init; }
    /// <summary>状態の世代。非同期に受け取った古い状態で、新しい状態を上書きしないように比べる。</summary>
    public long Rev { get; init; }
}

/// <summary>
/// 変換していないときに受け取るキー。変換中はすべてのキーを受け取る。
/// Text は、Ctrl・Alt・Command を押さずに打った ASCII の文字 (! から ~ まで。空白は含まない) を受け取るか。Except はそのうち受け取らない文字。
/// Keys は文字に関係なく受け取るキー (Space・Shift+Space・入力モードのキーなど。TSF は修飾キー付きのものを PreserveKey で登録する)。
/// 左右の Alt (0xA4・0xA5) は、単独で押して離したこと (ImeKey.Tap) を受け取る。
/// </summary>
public sealed record ImeCapture
{
    public bool Text { get; init; }
    public string Except { get; init; } = "";
    public IReadOnlyList<ImeShortcut> Keys { get; init; } = [];
    /// <summary>変換中に Ctrl と一緒に押しても変換ボックスで使うキー (設定「Control キーの割り当て」)。ほかの Ctrl・Alt・Win と一緒のキーは、確定してアプリに通す。</summary>
    public IReadOnlyList<ImeShortcut> Composing { get; init; } = [];
}

/// <summary>Mods は Shift=1, Ctrl=2, Alt=4, Win=8 (<see cref="Input.ShortcutModifiers"/>)。</summary>
public sealed record ImeShortcut(int Key, int Mods);

public sealed record ImeOutput(ImeState State, SessionResult Result)
{
    /// <summary>{"v":…,"state":{…},"result":{…}}。result は <see cref="SessionResult.ToJson"/> と同じ形。</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", ImeHost.Version);
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
