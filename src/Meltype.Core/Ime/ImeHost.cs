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

    /// <summary>漢字かな交じりの文の読み (再変換)。OS の機能で分からなければ null。</summary>
    string? ReadingOf(string text);

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

    /// <summary>1 つの ImeHost が保持するインスタンスの上限 (TSF ではふつうは 1 つ)。</summary>
    private const int MaxInstances = 16;

    private sealed class Instance(string id, long registeredAt)
    {
        public string Id { get; } = id;
        public long RegisteredAt { get; } = registeredAt;
        public ImeSession? Session { get; set; }
        // 入力の本体を作る前は、入力を受け持つか決めるための事実だけを持つ。
        public ImeField? Field { get; set; }
    }

    private readonly Instance?[] _instances = new Instance?[MaxInstances];
    private static long s_rev;

    public int SessionCount => _instances.Count(instance => instance?.Session is not null);

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
            Diagnostics.Log.Warn("フロントから知らない値の出来事が届いたので、受け持ちません");
            return null;
        }
        return e;
    }

    /// <summary>フロントがいなくなった (TSF のパイプが切れた): 入力欄をすべて閉じる。</summary>
    public void CloseAll()
    {
        foreach (var instance in _instances) instance?.Session?.Close();
        Array.Clear(_instances);
    }

    public ImeOutput Handle(ImeEvent e)
    {
        if (e.Version != Version)
        {
            Diagnostics.Log.Warn($"フロントのプロトコルの版 ({e.Version}) が本体 ({Version}) と違うので、受け持ちません");
            return Inactive();
        }
        var id = e.Instance ?? "";
        var slot = Array.FindIndex(_instances, instance => instance?.Id == id);
        if (e.Type == ImeEventType.InstanceDestroyed)
        {
            if (slot >= 0)
            {
                var closing = _instances[slot];
                _instances[slot] = null;
                closing?.Session?.Close(e.Field);
            }
            return Inactive();
        }

        var rev = NextRev();
        var instance = slot >= 0 ? _instances[slot] : null;
        if (instance?.Session is { } session) return session.Handle(e, rev);

        var current = settings();
        var field = e.Field ?? instance?.Field;
        if (field is null)
        {
            // TSF は起動時に設定による捕捉条件を先に問い合わせる。入力を始めるには入力欄の通知が要る。
            var capture = e.Type == ImeEventType.StateRequested && ImeSession.IsActive(environment, current, null);
            return new(ImeSession.InitialState(capture, e.ImeOpen ?? true, current, false, rev), Pass);
        }

        if (instance is null)
        {
            slot = FindAvailableSlot();
            if (slot < 0) return new(ImeSession.InitialState(false, e.ImeOpen ?? true, current, false, rev), Pass);
            // 以前の入力先の情報は、この枠を使う新しい instance に引き継がない。
            instance = new Instance(id, rev);
        }

        var active = ImeSession.IsActive(environment, current, field);
        // フロントは変換中なのに入力の続きが無い (つなぎ直した): 新しく始めると変換中の文字を消したり置き換えたりするので受け持たない
        // (フロントは変換中の文字をそのまま確定し、キーをアプリに通す)
        if (!active || field.Composing ||
            e.Type is not (ImeEventType.KeyPressed or ImeEventType.ModeToggleRequested or ImeEventType.JapaneseModeSelected or ImeEventType.AlphanumericModeSelected))
        {
            if (e.Field is not null)
                instance.Field = new ImeField { App = field.App, Traits = [.. field.Traits], Composing = field.Composing, CanRemove = field.CanRemove };
            _instances[slot] = instance;
            return new(ImeSession.InitialState(active, e.ImeOpen ?? true, current.ForApp(field.App), ImeSession.AnnouncesFocus(e), rev), Pass);
        }
        session = instance.Session = new ImeSession(environment, settings);
        instance.Field = null;
        _instances[slot] = instance;
        return session.Handle(e.Field is null ? e with { Field = field } : e, rev);
    }

    /// <summary>空き枠を優先し、満杯なら入力が残っていない最古の枠を再利用する。</summary>
    private int FindAvailableSlot()
    {
        var oldest = -1;
        var registeredAt = long.MaxValue;
        for (var slot = 0; slot < _instances.Length; slot++)
        {
            if (_instances[slot] is not { } instance) return slot;
            if (instance.Session?.HasPendingInput == true || instance.RegisteredAt >= registeredAt) continue;
            oldest = slot;
            registeredAt = instance.RegisteredAt;
        }
        return oldest;
    }

    private static long NextRev() => Interlocked.Increment(ref s_rev);

    private static SessionResult Pass => new(false, [], null);

    private static ImeOutput Inactive() =>
        new(new ImeState { Capture = new ImeCapture(), Revision = NextRev() }, Pass);
}
