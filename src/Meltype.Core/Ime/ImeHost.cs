// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Ime;

/// <summary>ImeHost が使う OS 側の機能。1 つのフロント (TSF ではパイプのつながり = 1 つのアプリ) ごとに 1 つ。判断はしない。</summary>
public interface IImeEnvironment
{
    /// <summary>このフロントが Windows のどちらの入力方式 (設定「動作モード」) か。入力方式が 1 つしかない OS では null。</summary>
    InputMode? InputMethod { get; }

    /// <summary>入力欄ごとの入力の本体。settings は、その入力欄のアプリの設定。辞書・学習データはフロントの中で共有する。</summary>
    MeltypeSession CreateSession(Func<Settings> settings);

    /// <summary>ユーザーが登録した定型文。</summary>
    SnippetStore Snippets { get; }

    /// <summary>クリップボードの文字 (設定「クリップボードの文字を変換候補に出す」のときだけ、変換した瞬間に読む)。読めなければ null。</summary>
    string? ClipboardText { get; }
}

/// <summary>
/// フロントからの出来事を受け取り、入力欄ごとの <see cref="ImeSession"/> に振り分ける。
/// 別の ImeHost の instance には触れない (TSF では、つなぎ直した DLL が古い入力の続きを拾わない・ほかのアプリの入力を読めない)。
/// 呼ぶスレッドは 1 つに決める (排他しない)。
/// </summary>
/// <param name="settings">設定 (config.json)。アプリ別設定は ImeSession が ImeField.App で反映する。フロントは読まない。</param>
public sealed class ImeHost(IImeEnvironment environment, Func<Settings> settings)
{
    public const int Version = 1;

    /// <summary>1 つの ImeHost で持つ入力欄の数の上限 (TSF では 1 つのスレッドに 1 つなので、ふつうは 1 つ)。</summary>
    private const int MaxSessions = 16;

    private readonly Dictionary<string, ImeSession> _sessions = [];
    private static long s_rev;

    public int SessionCount => _sessions.Count;

    /// <summary>JSON の出来事を処理する。読めない・版が違う出来事には、何も受け持たない応答を返す。</summary>
    public string Handle(string json) => (Parse(json) is { } e ? Handle(e) : Inactive()).ToJson();

    /// <summary>JSON の出来事を読む。読めない・知らない項目や値 (出来事・入力欄の性質の名前) がある出来事なら、警告をログに出して null。</summary>
    public static ImeEvent? Parse(string json)
    {
        ImeEvent? e;
        try
        {
            e = JsonSerializer.Deserialize(json, ImeJsonContext.Default.ImeEvent);
        }
        catch (JsonException ex)
        {
            Diagnostics.Log.Warn($"フロントから読めない出来事が届いたので、受け持ちません: {ex.Message}");
            return null;
        }
        // 文字列の名前のほかに数も読めてしまうので、名前の無い値も知らない値として扱う
        if (e is not null && (!Enum.IsDefined(e.Type) || e.Field?.Traits.Any(trait => !Enum.IsDefined(trait)) == true))
        {
            Diagnostics.Log.Warn($"フロントから知らない値の出来事が届いたので、受け持ちません: {json}");
            return null;
        }
        return e;
    }

    /// <summary>フロントがいなくなった (TSF のパイプが切れた): 入力欄をすべて閉じる。</summary>
    public void CloseAll()
    {
        foreach (var session in _sessions.Values)
        {
            if (session.IsComposing) session.Close();
        }
        _sessions.Clear();
    }

    public ImeOutput Handle(ImeEvent e)
    {
        if (e.Version != Version)
        {
            Diagnostics.Log.Warn($"フロントのプロトコルの版 ({e.Version}) が本体 ({Version}) と違うので、受け持ちません");
            return Inactive();
        }
        if (e.Type == ImeEventType.InstanceDestroyed)
        {
            if (_sessions.Remove(e.Instance, out var closing) && closing.IsComposing) closing.Close();
            return Inactive();
        }
        if (!_sessions.TryGetValue(e.Instance, out var session))
        {
            var current = settings();
            var active = ImeSession.IsActive(environment, current, e.Field);
            // フロントは変換中なのに入力の続きが無い (つなぎ直した): 新しく始めると変換中の文字を消したり置き換えたりするので受け持たない
            // (フロントは変換中の文字をそのまま確定し、キーをアプリに通す)
            if (!active || e.Type is not (ImeEventType.KeyPressed or ImeEventType.ModeToggleRequested or ImeEventType.JapaneseModeSelected or ImeEventType.AlphanumericModeSelected) ||
                e.Field?.Composing == true || _sessions.Count >= MaxSessions)
            {
                return new(ImeSession.InitialState(active, e.ImeOpen ?? true, current.ForApp(e.Field?.App), ImeSession.IsGame(current, e.Field), ImeSession.AnnouncesFocus(e), NextRev()), Pass);
            }
            session = new ImeSession(environment, settings);
            _sessions[e.Instance] = session;
        }
        return session.Handle(e, NextRev());
    }

    private static long NextRev() => Interlocked.Increment(ref s_rev);

    private static SessionResult Pass => new(false, [], null);

    private static ImeOutput Inactive() =>
        new(new ImeState { Capture = new ImeCapture(), Revision = NextRev() }, Pass);
}
