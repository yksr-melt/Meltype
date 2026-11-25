// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Ime;

namespace Meltype.Tests;

/// <summary>
/// 入力欄 1 つ分を <see cref="ImeHost"/> 経由で動かすフロント。キーはすべて本体に送り (IBus・fcitx5 と同じ)、キャレットの前後は呼ぶ側が渡す。
/// 入力欄の文字を持たないので、確定した文字は戻り値の <see cref="SessionResult"/> で見る。
/// </summary>
internal sealed class TestSession : IImeEnvironment
{
    private readonly ImeHost _host;
    private SessionResult? _last;

    /// <param name="classifyDirect">英数状態で打ち始めた英字がローマ字 (日本語) かの判定 (打った英字, もう続きが無いか)。null なら判定しない。</param>
    /// <param name="timeMs">キーを処理した時刻 (ミリ秒)。null なら実際の時刻。</param>
    /// <param name="lateResults">変換エンジンの答えを待たず、後から RefreshConversion で受け取るか (<see cref="SessionParts.LateResults"/>)。</param>
    public TestSession(Func<Settings> settings, IKanjiConverter? converter = null, CompositionServices? services = null, CompositionDetector? detector = null,
        Func<string, bool, Detection.Verdict>? classifyDirect = null, Func<long>? timeMs = null, bool lateResults = false)
        : this(new SessionParts(detector ?? CompositionTests.Detector, converter ?? new CompositionTests.FakeConverter(), services ?? new CompositionServices())
        {
            ClassifyDirect = classifyDirect is null ? null : (letters, final, _) => classifyDirect(letters, final),
            TimeMs = timeMs ?? (() => Environment.TickCount64),
            LateResults = () => lateResults,
        }, settings)
    {
    }

    /// <param name="parts">入力欄の本体を作る部品 (<see cref="MeltypeRuntime.SessionParts"/> で作ったもの)。</param>
    public TestSession(SessionParts parts, Func<Settings> settings)
    {
        MeltypeRuntime.Connect(parts.Detector, parts.Services);
        _host = new ImeHost(this, parts, settings);
    }

    public InputMode? InputMethod => null;

    public SnippetStore Snippets { get; } = new(null);

    public string? ClipboardText => null;

    public string? OtherTipVersion => null;

    /// <summary>直近の応答の状態。</summary>
    public ImeState State { get; private set; } = new() { Capture = new ImeCapture() };

    /// <summary>コードエディター (VS Code) の入力欄か。</summary>
    public bool CodeInput { get; set; }

    /// <summary>ターミナルの入力欄か。</summary>
    public bool CodeTerminal { get; set; }

    /// <summary>入力を覚えないように求める入力欄か。</summary>
    public bool Private { get; set; }

    /// <summary>英数 (直接入力) か。変えると、入力メニューで選んだときと同じく変換中の文字を確定する。</summary>
    public bool Direct
    {
        get => !State.ImeOpen;
        set => Send(value ? new ImeEvent.AlphanumericModeSelected() : new ImeEvent.JapaneseModeSelected());
    }

    /// <summary>英数状態で英語と分かった語の続きを打っているか。</summary>
    public bool DirectEnglishWord => State.Capture.PrintableAsciiExcept.Length > 0;

    /// <summary>変換ボックスに何か入っているか。</summary>
    public bool IsComposing => _last?.View is not null;

    public SessionResult Send(ImeEvent e, string? before = null, string? after = null)
    {
        var traits = new List<ImeTrait>();
        if (CodeTerminal) traits.Add(ImeTrait.Terminal);
        if (Private) traits.Add(ImeTrait.Private);
        var output = _host.Handle(e with
        {
            Instance = "test",
            Field = new ImeField { Before = before, After = after, Composing = IsComposing, App = CodeInput ? "Code.exe" : null, Traits = traits },
        });
        State = output.State;
        _last = output.Result;
        return output.Result;
    }

    public SessionResult HandleKey(int vk, char? ch, bool shift, bool control, bool alt, bool command, string? before = null, string? after = null) =>
        Send(new ImeEvent.KeyPressed(new ImeKey { Vk = vk, Ch = ch ?? 0, Shift = shift, Ctrl = control, Alt = alt, Command = command }), before, after);

    /// <summary>未確定の内容を確定する (フォーカスが移ったなど)。</summary>
    public SessionResult CommitPending() => Send(new ImeEvent.CommitRequested());

    /// <summary>前の応答の一覧の index 番目をクリックする (予測変換の候補なら確定する)。</summary>
    public SessionResult SelectCandidate(int index) => Send(new ImeEvent.CandidateClicked(index));

    /// <summary>背景の変換の結果が届いた。表示が変わらなければ null。</summary>
    public SessionResult? RefreshConversion()
    {
        var before = ViewJson(_last);
        var result = Send(new ImeEvent.ConversionFinished());
        return ViewJson(result) == before ? null : result;
    }

    private static string ViewJson(SessionResult? result) => new SessionResult(true, [], result?.View).ToJson();

    /// <summary>キャレットが動いた (passedVk はアプリに通したキー。0 ならクリックなど)。</summary>
    public SessionResult CaretMoved(int passedVk, bool control = false) =>
        Send(new ImeEvent.CaretMoved(new ImeCaret { Moved = true, PassedKey = passedVk, Ctrl = control }));

    /// <summary>キャレットの前が before のとき、打ったら英数のままアプリに通るか (入力モードの表示が A か)。</summary>
    public bool CodeEnglishAt(string before)
    {
        Send(new ImeEvent.CaretMoved(new ImeCaret()), before);
        return State is { ImeOpen: true, LanguageMode: ImeLanguageMode.Alphanumeric };
    }

    /// <summary>日本語 ⇔ 英数を切り替える (タスクバーのボタンなど)。</summary>
    public SessionResult Toggle(string before) => Send(new ImeEvent.ModeToggleRequested(), before);
}
