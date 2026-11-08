// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Ime;

/// <summary>
/// 1 つの入力欄。入力モード (日本語 / 英数・コードの行) とモードを切り替えるキーの意味は、どの OS のフロントでもここで決める。
///   設定「入力モード切り替えキー」: 日本語 ⇔ 英数 (変換中は 半角/全角 と同じく、変換ボックスの英字表示の切り替え)
///   半角/全角 (切り替えキーに無いとき): 何もしない (アプリに通すと PowerShell (PSReadLine) が @ として入れる)
///   英数: 英数にする (変換中なら確定してから)
///   かな (ひらがな/カタカナ): 日本語にする
///   変換・無変換: 設定「無変換で英数 / 変換で日本語」が ON なら、変換していないときに日本語 / 英数にする
///   かな・変換・無変換は、設定「入力中のキーの役割を分ける」が ON で変換中なら、変換ボックスで使う
/// 日本語 ⇔ 英数の切り替えは、表示しているモードを入れ替える。コードの行 (英数のまま通る所) で日本語にすると、その行を日本語にする。
/// </summary>
internal sealed class ImeSession(IImeEnvironment environment)
{
    private readonly MeltypeSession _session = environment.CreateSession();

    /// <summary>コードの入力欄でないときに、判定の文脈として使うキャレットの前の文字数。</summary>
    private const int ContextLength = 20;

    public bool IsComposing => _session.IsComposing;

    public void Close() => _session.CommitPending();

    public ImeOutput Handle(ImeEvent e, bool active, long rev)
    {
        var settings = environment.Settings;
        ApplyFacts(e);
        var before = Before(e.Field);
        var wasJapanese = ShowsJapanese(before);
        var explicitChange = false;
        var result = e.Type switch
        {
            ImeEventType.Key when e.Key is { } key => active ? HandleKey(key, e.Field, before, settings, ref explicitChange) : Leave(),
            ImeEventType.Toggle when active => Switch(!wasJapanese, before, ref explicitChange),
            ImeEventType.SetMode when active => Switch(e.Japanese, before, ref explicitChange),
            ImeEventType.Commit => _session.CommitPending(),
            ImeEventType.Select => e.Prediction ? _session.SelectPrediction(e.Index) : _session.SelectCandidate(e.Index),
            ImeEventType.Refresh when active => _session.RefreshConversion() ?? Pass,
            _ => Pass,
        };
        // キーでアプリの文字が変わったあとの位置は、フロントが次の出来事 (caret) で知らせる
        var japanese = ShowsJapanese(before);
        return new(new ImeState
        {
            Active = active,
            Open = !_session.Direct,
            Japanese = active && japanese,
            Indicator = active && explicitChange && japanese != wasJapanese,
            Capture = Capture(active, _session.Direct, _session.DirectEnglishWord, settings),
            Rev = rev,
        }, result);
    }

    /// <summary>入力の本体を作る前 (まだ打っていない入力欄) の状態。</summary>
    public static ImeState InitialState(bool active, bool open, Settings settings, long rev) => new()
    {
        Active = active,
        Open = open,
        Japanese = active && open,
        Capture = Capture(active, !open, false, settings),
        Rev = rev,
    };

    private void ApplyFacts(ImeEvent e)
    {
        // OS の IME の開閉が変わった (アプリ・Windows が変えた・ほかのフロントの操作): 本体のモードを合わせる
        if (e.Open is { } open && _session.Direct == open)
        {
            _session.Direct = !open;
            _session.ForgetDirectInput();
        }
        if (e.Caret is { } caret)
        {
            if (caret.Moved)
            {
                _session.ForgetLastCommit();
                _session.ForgetDirectInput();
            }
            if (caret.Passed is { } vk) _session.CaretMoved(vk, caret.Ctrl);
        }
        if (e.Field is not { } field) return;
        _session.Private = field.Private;
        if (environment.CodeFocus(field.Terminal) is { } focus)
        {
            var code = focus != CodeFocus.None;
            if (_session.CodeInput != code) _session.CodeInput = code;
            _session.CodeTerminal = focus == CodeFocus.Terminal;
        }
    }

    private string? Before(ImeField? field) =>
        field?.Before is { Length: > ContextLength } before && !_session.CodeInput ? before[^ContextLength..] : field?.Before;

    private SessionResult HandleKey(ImeKey key, ImeField? field, string? before, Settings settings, ref bool explicitChange)
    {
        var composing = _session.IsComposing;
        var modifiers = Modifiers(key);
        var plain = modifiers == ShortcutModifiers.None;
        var vk = ToggleShortcut.NormalizeHankakuKey(key.Vk);
        if (settings.EffectiveToggleShortcuts.Any(shortcut => shortcut.Matches(key.Vk, modifiers)))
        {
            return composing
                ? _session.HandleKey(VirtualKeys.Kanji, null, false, false, false, false, toggleShortcuts: false)
                : Switch(!ShowsJapanese(before), before, ref explicitChange);
        }
        if (vk == ToggleShortcut.Hankaku && !key.Ctrl && !key.Alt && !composing) return Consumed;
        if (vk == VirtualKeys.OemAttn && plain) return Switch(false, before, ref explicitChange);
        if (vk is VirtualKeys.Kana or VirtualKeys.Convert or VirtualKeys.NonConvert && plain)
        {
            if (_session.HandleRoleKey(vk) is { } role) return role;
            if (!composing && (vk == VirtualKeys.Kana || settings.ConvertKeysSwitchKeyboard)) return Switch(vk != VirtualKeys.NonConvert, before, ref explicitChange);
        }
        // 補助面の文字 (絵文字など) は char に入らないので、未確定の内容を確定してからアプリに通す
        if (key.Ch >= 0x10000) return _session.CommitForPassThrough();
        return _session.HandleKey(key.Vk, key.Ch > 0 ? (char)key.Ch : null, key.Shift, key.Ctrl, key.Alt, key.Command,
            before, field?.After, toggleShortcuts: false);
    }

    /// <summary>表示するモードを japanese にする。変換中なら確定してから。コードの行では、IME を開いたまま行を日本語 / 英数にする。</summary>
    private SessionResult Switch(bool japanese, string? before, ref bool explicitChange)
    {
        explicitChange = true;
        var result = _session.IsComposing ? _session.CommitPending() : Consumed;
        if (japanese)
        {
            _session.Direct = false;
            if (_session.CodeEnglishAt(before)) _session.SetCodeLine(true, before);
        }
        else if (!_session.SetCodeLine(false, before))
        {
            _session.Direct = true;
        }
        return result;
    }

    /// <summary>受け持たなくなった (一時停止・動作モードの変更): 変換中の内容は確定して、キーはアプリに通す。</summary>
    private SessionResult Leave() => _session.IsComposing ? _session.CommitPending() with { Consumed = false } : Pass;

    private bool ShowsJapanese(string? before) => !_session.Direct && !_session.CodeEnglishAt(before);

    private static ShortcutModifiers Modifiers(ImeKey key) =>
        (key.Shift ? ShortcutModifiers.Shift : 0) | (key.Ctrl ? ShortcutModifiers.Control : 0) |
        (key.Alt ? ShortcutModifiers.Alt : 0) | (key.Command ? ShortcutModifiers.Win : 0);

    private static ImeCapture Capture(bool active, bool direct, bool directEnglishWord, Settings settings)
    {
        if (!active) return new ImeCapture();
        var keys = settings.EffectiveToggleShortcuts.Select(shortcut => new ImeShortcut(shortcut.Key, (int)shortcut.Modifiers))
            .Concat(ModeKeys.Select(key => new ImeShortcut(key, 0)))
            .Concat(settings.ConvertKeysSwitchKeyboard ? [new ImeShortcut(VirtualKeys.Convert, 0), new ImeShortcut(VirtualKeys.NonConvert, 0)] : [])
            .Distinct().ToList();
        if (!direct) return new ImeCapture { Word = true, Symbol = true, Space = settings.SpaceWidth == SpaceWidth.FullWidth, ShiftSpace = true, Keys = keys };
        // 英数でもローマ字を見分ける: 英語の語を打っている途中は、続きの英字をそのまま通す
        if (settings.DirectModeAutoDetect && settings.AutoDetection) return new ImeCapture { Word = !directEnglishWord, Symbol = true, ShiftSpace = true, Keys = keys };
        return new ImeCapture { Keys = keys };
    }

    /// <summary>設定によらず受け取る、モードを切り替えるキー (半角/全角 の 3 つのキーコード・英数・かな)。</summary>
    private static readonly int[] ModeKeys = [ToggleShortcut.Hankaku, VirtualKeys.OemAuto, VirtualKeys.OemEnlw, VirtualKeys.OemAttn, VirtualKeys.Kana];

    private static SessionResult Pass => new(false, [], null);

    private static SessionResult Consumed => new(true, [], null);
}
