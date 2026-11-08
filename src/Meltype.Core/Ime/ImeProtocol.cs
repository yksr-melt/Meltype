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
    /// <summary>未確定の内容を確定する (フォーカスが移った・アプリが確定したなど)。</summary>
    [JsonStringEnumMemberName("commit")] Commit,
    [JsonStringEnumMemberName("select")] Select,
    /// <summary>背景の変換の結果が届いた。</summary>
    [JsonStringEnumMemberName("refresh")] Refresh,
    /// <summary>
    /// フロントが自分で書き換えていないのに、キャレットが動いた・入力欄の文字が変わった (クリック・フロントがアプリに通したキー・アプリの編集)。
    /// フロントは見つけたらすぐ送る (Core は打ったキーからキャレットの位置を追いかけない)。
    /// </summary>
    [JsonStringEnumMemberName("caret")] Caret,
    /// <summary>今の状態だけを受け取る (フォーカスを得たときなど)。入力の本体は作らない。</summary>
    [JsonStringEnumMemberName("sync")] Sync,
    [JsonStringEnumMemberName("close")] Close,
}

public sealed record ImeEvent
{
    /// <summary>プロトコルの版。<see cref="ImeHost.Version"/> と違えば、ImeHost は何も受け持たない。</summary>
    public required int V { get; init; }

    public required ImeEventType Type { get; init; }

    /// <summary>入力欄 (TSF ではスレッド) ごとにフロントが振る ID。</summary>
    public string Sid { get; init; } = "";

    public ImeKey? Key { get; init; }

    public ImeField? Field { get; init; }

    /// <summary>OS が持つ IME の開閉 (TSF の GUID_COMPARTMENT_KEYBOARD_OPENCLOSE)。OS が持たないフロントは送らない。</summary>
    public bool? Open { get; init; }

    /// <summary>caret: どう動いたか。</summary>
    public ImeCaret? Caret { get; init; }

    /// <summary>select: 候補の番号。</summary>
    public int Index { get; init; }

    /// <summary>select: 予測変換の候補か。</summary>
    public bool Prediction { get; init; }

    /// <summary>setMode: 日本語にするか。</summary>
    public bool Japanese { get; init; }
}

/// <summary>押したキー。Vk は Windows の仮想キーコード (ほかの OS のキーも、対応する仮想キーコードにする)。Ch は入力される文字 (無ければ 0)。</summary>
public sealed record ImeKey
{
    public int Vk { get; init; }
    public int Ch { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    /// <summary>Windows キー・Command・Super。</summary>
    public bool Command { get; init; }
}

public sealed record ImeField
{
    /// <summary>
    /// 入力欄の確定した文字のうち、キャレットより前 (変換中なら変換中の文字より前)。読めなければ null で、空文字列は入力欄の先頭。
    /// 出来事ごとに読み直して送る。コードの入力欄の判定に 4000 文字まで使う。
    /// </summary>
    public string? Before { get; init; }
    /// <summary>キャレット (変換中なら変換中の文字) より後ろ。読めなければ null。</summary>
    public string? After { get; init; }
    /// <summary>フロントの入力欄に変換中の文字がある。</summary>
    public bool Composing { get; init; }
    /// <summary>入力を覚えないように求める入力欄 (TSF の IS_PRIVATE)。</summary>
    public bool Private { get; init; }
    /// <summary>OS がターミナルと知らせてきた入力欄 (IBus の InputPurpose.TERMINAL・fcitx5 の CapabilityFlag::Terminal)。</summary>
    public bool Terminal { get; init; }
}

public sealed record ImeCaret
{
    /// <summary>キャレットが動いたかもしれない (クリック・フロントが通したキー・別の入力欄)。前の語を確定し直さない。</summary>
    public bool Moved { get; init; }
    /// <summary>キャレットを動かした、フロントがアプリに通したキー。0 はキーなし (クリックなど)。null は文字が変わっただけ。</summary>
    public int? Passed { get; init; }
    /// <summary>Passed のとき Ctrl を押していたか。</summary>
    public bool Ctrl { get; init; }
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
    /// <summary>今の出来事で明示的にモードが変わった。フロントはキャレットの近くに入力モードを一瞬表示する。</summary>
    public bool Indicator { get; init; }
    /// <summary>変換していないときに受け取るキー。キーを受け取るかをその場で決めなければならないフロント (TSF) が使う。</summary>
    public required ImeCapture Capture { get; init; }
    /// <summary>状態の世代。非同期に受け取った古い状態で、新しい状態を上書きしないように比べる。</summary>
    public long Rev { get; init; }
}

/// <summary>
/// 変換していないときに受け取るキー。変換中はすべてのキーを受け取る。
/// Word は英字・数字・@・_、Symbol はそれ以外の ASCII の記号、Space は Space、ShiftSpace は Shift+Space。
/// Keys はそれ以外に受け取るキー (修飾キーの組み合わせを含む。TSF は修飾キー付きのものを PreserveKey で登録する)。
/// </summary>
public sealed record ImeCapture
{
    public bool Word { get; init; }
    public bool Symbol { get; init; }
    public bool Space { get; init; }
    public bool ShiftSpace { get; init; }
    public IReadOnlyList<ImeShortcut> Keys { get; init; } = [];
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
