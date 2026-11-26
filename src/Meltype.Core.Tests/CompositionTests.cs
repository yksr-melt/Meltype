// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Composition.Utility;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Diagnostics;
using Meltype.Dictionary;
using Meltype.Engine;
using Meltype.Ime;
using Meltype.Input;
using Meltype.Learning;
using Meltype.Libraries.Chars;
using Meltype.Notation;

namespace Meltype.Tests;

/// <summary>Meltype キーボード (変換ボックス)。</summary>
internal static class CompositionTests
{
    internal static readonly CompositionDetector Detector = CompositionDetector.CreateDefault();

    internal sealed class FakeConverter : TestConverter
    {
        public override string? Convert(string hiragana, string? before) => hiragana switch
        {
            "きょう" => "今日",
            "にほんご" => "日本語",
            "こんにちは" => "今日は",
            "きょうは" => "今日は",
            "でけんさく" => "で検索",
            "たんい" => "単位",
            "な～～～" => "名～～～",
            "かんたん" => "簡単",
            "かん" => "感",
            "みぢか" => "身近",
            _ => null,
        };

        /// <summary>直近の ConvertClauses に渡された文脈 (文脈なしの呼び出しは数えない)。</summary>
        public string? LastContext { get; private set; }

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before)
        {
            if (before is not null) LastContext = before;
            return hiragana switch
            {
                "たんいをとる" => [new("たんいを", "単位を"), new("とる", "取る")],
                // 英日の区切りの別案が無い 2 文節 (tanniwotoru は tan|niwotoru の別案があり、Space で文全体の 1 文節になる)
                "にほんごをかく" => [new("にほんごを", "日本語を"), new("かく", "書く")],
                // 英語として見せている語 (Make) も、読み (まけ) にして区切る
                "きょうはまけ" => [new("きょうは", "今日は"), new("まけ", "負け")],
                "ひるか" => [new("ひる", "昼"), new("か", "化")],
                // 本物の変換エンジンは、前に同じ語があると区切りを変える (「記号等」含め、 + きごうとう → 気強盗)。
                "きごうとう" when before?.Contains("記号等") == true => [new("き", "気"), new("ごうとう", "強盗")],
                "きごうとう" => [new("きごう", "記号"), new("とう", "等")],
                "あつい" => [new("あつい", "熱い")],
                "かわ" => [new("かわ", "川")],
                "すぱいだーまっ" => [new("すぱいだーま", "スパイダーマ"), new("っ", "っ")],
                "はしを" => [new("はしを", "橋を")],
                "かくにん" => [new("かくにん", "確認")],
                "かくてい" => [new("かくてい", "確定")],
                "かくにんしたら" => [new("かくにんしたら", "確認したら")],
                "かくていして" => [new("かくていして", "確定して")],
                // 本物の変換エンジン (Mozc) の苦手なもの: え、 → 得、、がちで → 勝ちで
                "え、しらん" => [new("え", "得"), new("、", "、"), new("しらん", "知らん")],
                "えかく" => [new("え", "絵"), new("かく", "描く")],
                "がちでやばい" => [new("がちで", "勝ちで"), new("やばい", "ヤバい")],
                // 英単語の後ろの付属語・する の活用: なら → 奈良、した → 下、したい → 死体
                "なら" => [new("なら", "奈良")],
                "した" => [new("した", "下")],
                "したい" => [new("したい", "死体")],
                // 1 拍の拗音: 本物の変換エンジンは きょ を 虚 にする
                "きょ" => [new("きょ", "虚")],
                _ => null,
            };
        }
    }

    /// <summary>
    /// 入力欄のアプリ。確定した文字・アプリに通したキーを入力欄の文字 (Document) に入れ、出来事を Events に記録する
    /// (text:確定した文字、bs:確定し直しで消した数、down:本体が受け取ってからアプリに通したキー、passed:本体を通らずにアプリに届いたキー、
    /// replace:選択していた文字:置き換えた文字)。
    /// </summary>
    internal sealed class FakeHost
    {
        public List<string> Output { get; } = [];
        public List<string> Events { get; } = [];

        /// <summary>変換ボックスの表示 (本体がフロントに渡したもの)。</summary>
        public CompositionView? View { get; private set; }
        public bool PhysicalShift { get; set; }
        public bool CanDeleteBackward { get; set; } = true;

        /// <summary>入力欄で選んでいる文字と、OS が求めたその読み (再変換)。確定した文字で置き換わる。</summary>
        public ReconversionSelection? Selection { get; set; }

        /// <summary>入力欄のキャレットの直前にある (と見なす) 確定済みの文字列。null なら入力欄の文字。</summary>
        public string? PrecedingText { get; set; }

        /// <summary>入力欄のキャレットの後ろにある (と見なす) 文字列。</summary>
        public string? FollowingText { get; set; }

        /// <summary>フロントが渡すキャレットの前の文字。</summary>
        public string Before => PrecedingText ?? Document;

        /// <summary>入力欄の中身 (確定した文字列と、確定し直すときの削除を反映したもの)。</summary>
        public string Document { get; private set; } = "";

        public string? ClipboardText { get; set; }
        public (string Title, string Artist)? NowPlaying { get; set; }

        /// <summary>本体が返した結果を入力欄に当てる。キーをアプリに通したなら、そのキーの文字も入れる。</summary>
        public void Apply(SessionResult result, int vk, bool shift, bool control = false, bool captured = true)
        {
            View = result.View;
            foreach (var edit in result.Commits)
            {
                if (edit.DeleteBefore > 0)
                {
                    Events.Add($"bs:{edit.DeleteBefore}");
                    Document = Document[..Math.Max(0, Document.Length - edit.DeleteBefore)];
                }
                if (Selection is { } selection)
                {
                    Events.Add($"replace:{selection.Text}:{edit.Text}");
                    Selection = null;
                    Document = edit.Text;
                }
                else if (edit.Text.Length > 0)
                {
                    Output.Add(edit.Text);
                    Events.Add($"text:{edit.Text}");
                    Document += edit.Text;
                }
                for (var i = 0; i < edit.CaretBack; i++) Events.Add("down:25");
            }
            if (result.Consumed || vk == 0) return;
            Events.Add($"{(captured ? "down" : "passed")}:{vk:X2}");
            // Ctrl と一緒のキーは、アプリの操作 (コピーなど) で文字を入れない
            if (control) return;
            if (vk == VirtualKeys.Back) Document = Document[..Math.Max(0, Document.Length - 1)];
            else if (vk == VirtualKeys.Return) Document += "\n";
            else if (vk == VirtualKeys.Space) Document += " ";
            else if (CharFromKey(vk, shift) is { } c && !char.IsControl(c)) Document += c;
        }

        public char? CharFromKey(int vk, bool shift)
        {
            if (VirtualKeys.IsLetter(vk)) return shift || PhysicalShift ? (char)vk : char.ToLowerInvariant((char)vk);
            // テンキー (NumLock が ON のときの数字・記号)
            if (vk is >= 0x60 and <= 0x6F && "0123456789*+\0-./"[vk - 0x60] is var pad and not '\0') return pad;
            var shifted = shift || PhysicalShift;
            // JIS の ろ のキー (Shift なしは \。\ は ¥ のキーに割り当てた)
            if (vk == 0xE2 && !shifted) return '\\';
            foreach (var (c, key) in JisKeys)
            {
                if (key.Vk == vk && key.Shift == shifted) return c;
            }
            return null;
        }

        /// <summary>JIS 配列の数字・記号のキー (文字 → 仮想キー, Shift)。</summary>
        public static readonly Dictionary<char, (int Vk, bool Shift)> JisKeys = new()
        {
            ['0'] = (0x30, false), ['1'] = (0x31, false), ['2'] = (0x32, false), ['3'] = (0x33, false), ['4'] = (0x34, false),
            ['5'] = (0x35, false), ['6'] = (0x36, false), ['7'] = (0x37, false), ['8'] = (0x38, false), ['9'] = (0x39, false),
            ['!'] = (0x31, true), ['"'] = (0x32, true), ['#'] = (0x33, true), ['$'] = (0x34, true), ['%'] = (0x35, true),
            ['&'] = (0x36, true), ['\''] = (0x37, true), ['('] = (0x38, true), [')'] = (0x39, true),
            ['-'] = (0xBD, false), ['='] = (0xBD, true), ['^'] = (0xDE, false), ['~'] = (0xDE, true), ['\\'] = (0xDC, false), ['|'] = (0xDC, true),
            ['@'] = (0xC0, false), ['`'] = (0xC0, true), ['['] = (0xDB, false), ['{'] = (0xDB, true),
            [';'] = (0xBB, false), ['+'] = (0xBB, true), [':'] = (0xBA, false), ['*'] = (0xBA, true), [']'] = (0xDD, false), ['}'] = (0xDD, true),
            [','] = (0xBC, false), ['<'] = (0xBC, true), ['.'] = (0xBE, false), ['>'] = (0xBE, true), ['/'] = (0xBF, false), ['?'] = (0xBF, true),
            ['_'] = (0xE2, true),
        };

    }

    /// <summary>
    /// Meltype キーボード (<c>KeyboardFront</c>) と同じに <see cref="ImeHost"/> を呼ぶフロント。本体が受け取るキー (state.capture。入力中はすべて) だけを送り、
    /// 受け取らないキーはアプリ (<see cref="FakeHost"/>) に入れて caret で知らせる。表示はフロントに渡した形 (result.view) を見る。
    /// </summary>
    /// <summary>打って Enter で確定した文字を比べる。外れたケースは 1 つ目で止めずにまとめて出す。</summary>
    internal static void AssertCommitted(params (string Typed, string Committed)[] cases)
    {
        var failures = new List<string>();
        foreach (var (typed, committed) in cases)
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            if (k.Host.Document != committed) failures.Add($"{typed} → {k.Host.Document} (期待: {committed})");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    internal sealed class Keyboard : IImeEnvironment
    {
        private long _now = 1000;
        private bool _ctrlHeld;
        private readonly ImeHost _ime;
        private ImeState _state = new() { Capture = new ImeCapture() };
        private bool _composing;
        private bool _stale = true;
        // 押しているキー (自動の繰り返しを見分ける) と、離したことも本体に送るキー (ImeKey.ReleaseFollows を付けて押したもの)
        private readonly HashSet<int> _held = [];
        private readonly HashSet<int> _releaseFollows = [];
        public FakeHost Host { get; } = new();

        /// <summary>入力欄のアプリの設定。下の設定のプロパティはこれを読み書きする。</summary>
        public Meltype.Config.Settings Settings { get; }

        /// <summary>ユーザーが判定を直した記録 (トレイの「直した誤判定を報告...」)。</summary>
        public CorrectionLog Corrections { get; } = new();

        private static readonly Detection.ScoreEngine DirectEngine = TestSupport.CreateEngine();
        private static readonly CandidateDictionary Candidates = CandidateDictionary.Load(null);
        private static readonly ContextRules Rules = ContextRules.Load(null);
        private static readonly MisspellingDictionary Misspellings = MisspellingDictionary.Load(null);
        private static readonly Lazy<RomajiTypoCorrector> SharedTypos = new(() => RomajiTypoCorrector.Load(Detector.Romaji));
        private static RomajiTypoCorrector Typos => SharedTypos.Value;

        /// <summary>自動判定を使わない (設定 AutoDetection が OFF)。</summary>
        public bool Manual { get => !Settings.AutoDetection; set => Settings.AutoDetection = !value; }

        private bool _shiftHeld;

        /// <summary>かな入力 (JIS) か。</summary>
        public bool Kana { get => Settings.InputStyle == Meltype.Config.InputStyle.Kana; set => Settings.InputStyle = value ? Meltype.Config.InputStyle.Kana : Meltype.Config.InputStyle.Romaji; }
        public KanaLayout KanaLayout { get => Settings.KanaLayout; set => Settings.KanaLayout = value; }
        public RomajiLayout RomajiLayout { get => Settings.RomajiLayout; set => Settings.RomajiLayout = value; }
        public bool CorrectTypos { get => Settings.CorrectTypos; set => Settings.CorrectTypos = value; }
        public bool ZSymbols { get => Settings.ZSymbols; set => Settings.ZSymbols = value; }
        public bool NumberKeysSelectCandidates { get => Settings.NumberKeysSelectCandidates; set => Settings.NumberKeysSelectCandidates = value; }
        public bool SpaceAroundEnglish { get => Settings.SpaceAroundEnglish; set => Settings.SpaceAroundEnglish = value; }
        public Meltype.Config.SpaceWidth SpaceWidth { get => Settings.SpaceWidth; set => Settings.SpaceWidth = value; }
        public bool AlphanumericKeyStartsAsTyped { get => Settings.AlphanumericKeyStartsAsTyped; set => Settings.AlphanumericKeyStartsAsTyped = value; }
        public bool FullWidthParentheses { get => Settings.FullWidthParentheses; set => Settings.FullWidthParentheses = value; }
        public Meltype.Config.SlashStyle Slash { get => Settings.Slash; set => Settings.Slash = value; }
        public bool SquareBrackets { get => Settings.SquareBrackets; set => Settings.SquareBrackets = value; }
        public bool EscStepsBack { get => Settings.EscStepsBack; set => Settings.EscStepsBack = value; }
        public bool EscRevertsSelectedClause { get => Settings.EscRevertsSelectedClause; set => Settings.EscRevertsSelectedClause = value; }
        public bool FunctionKeysAffectSelectedClause { get => Settings.FunctionKeysAffectSelectedClause; set => Settings.FunctionKeysAffectSelectedClause = value; }
        public Meltype.Config.ControlKeyLayout ControlKeys { get => Settings.ControlKeys; set => Settings.ControlKeys = value; }
        public bool PartialCommit { get => Settings.PartialCommit; set => Settings.PartialCommit = value; }
        public bool ConversionStartsAtLastClause { get => Settings.ConversionStartsAtLastClause; set => Settings.ConversionStartsAtLastClause = value; }
        public bool CursorIntoBrackets { get => Settings.CursorIntoBrackets; set => Settings.CursorIntoBrackets = value; }
        public string ControlKeyBindings { get => Settings.ControlKeyBindings; set => Settings.ControlKeyBindings = value; }
        public bool CalculatorCandidates { get => Settings.CalculatorCandidates; set => Settings.CalculatorCandidates = value; }
        public bool ContextualLearning { get => Settings.ContextualLearning; set => Settings.ContextualLearning = value; }
        public string GitHubRepository { get => Settings.GitHubRepository; set => Settings.GitHubRepository = value; }
        public GitHubIssueTitles? GitHubTitles { get; set; }
        public bool ClipboardCandidates { get => Settings.ClipboardCandidates; set => Settings.ClipboardCandidates = value; }
        public bool NowPlayingCandidates { get => Settings.NowPlayingCandidates; set => Settings.NowPlayingCandidates = value; }
        public bool ContinueAfterConversion { get => Settings.ContinueAfterConversion; set => Settings.ContinueAfterConversion = value; }
        public bool Learning { get => Settings.LearningEnabled; set => Settings.LearningEnabled = value; }

        /// <summary>覚えないように求める入力欄で打っているか (覚えたものは読む)。</summary>
        public bool Private { get; set; }
        public bool SeparateKeyRoles { get => Settings.SeparateKeyRoles; set => Settings.SeparateKeyRoles = value; }
        public bool NonConvertCyclesKana { get => Settings.NonConvertCyclesKana; set => Settings.NonConvertCyclesKana = value; }
        public bool EnglishFirstInput { get => Settings.EnglishFirstInput; set => Settings.EnglishFirstInput = value; }
        public bool ConsonantAbbreviations { get => Settings.ConsonantAbbreviations; set => Settings.ConsonantAbbreviations = value; }
        public bool KeigoMode { get => Settings.KeigoMode; set => Settings.KeigoMode = value; }

        /// <summary>句読点の組み合わせ (設定)。</summary>
        public Meltype.Config.PunctuationStyle Punctuation { get => Settings.Punctuation; set => Settings.Punctuation = value; }

        /// <summary>かな入力で、仮想キーを順に打つ (shift: その打鍵で Shift を押す)。</summary>
        public void TypeKeys(params (int Vk, bool Shift)[] keys)
        {
            foreach (var (vk, shift) in keys)
            {
                if (shift) Key(VirtualKeys.LShift);
                Press(vk);
                if (shift) Key(VirtualKeys.LShift, up: true);
            }
        }

        /// <summary>かな入力で、英字キー (と , . / - の記号キー) を打つ。大文字は Shift を押して打つ。</summary>
        public void TypeKanaKeys(string keys) =>
            TypeKeys(keys.Select(c => (c switch { ',' => 0xBC, '.' => 0xBE, '/' => 0xBF, '-' => 0xBD, '@' => 0xC0, '[' => 0xDB, _ => (int)char.ToUpperInvariant(c) }, char.IsAsciiLetterUpper(c))).ToArray());

        /// <summary>英数 (直接入力) 状態か。変えると、入力メニューで選んだときと同じく変換中の文字を確定する。</summary>
        public bool Direct
        {
            get => !_state.ImeOpen;
            set => Send(value ? new ImeEvent.AlphanumericModeSelected() : new ImeEvent.JapaneseModeSelected());
        }

        /// <summary>先頭か空白の直後の / $ @ で始まる語をそのまま入力するか (設定の SigilWordsDirect)。</summary>
        public bool SigilWords { get => Settings.SigilWordsDirect; set => Settings.SigilWordsDirect = value; }

        /// <summary>先頭か空白の直後の / $ @ で始まる名前を打っている途中か。</summary>
        public bool InSigilWord => SigilWord.InNameAfter(Host.Before);

        /// <summary>コードエディター (VS Code) で打っているか。</summary>
        public bool Code { get; set; }

        public long Now => _now;

        public FakeConverter Converter { get; } = new();

        public Keyboard(bool live = false, bool direct = false, ConversionHistory? history = null, IKanjiConverter? converter = null,
            Func<string, IReadOnlyList<string>>? moreCandidates = null, UserDictionary? userDictionary = null, LanguageMemory? languages = null,
            TranslationDictionary? translations = null, TranslationHistory? translationHistory = null, Predictor? predictor = null,
            Func<DateTime>? now = null, Meltype.Config.TypedKeysStyle typedKeys = Meltype.Config.TypedKeysStyle.Off, bool tabConversion = false, bool englishNamePredictions = false,
            CompositionDetector? detector = null)
        {
            detector ??= Detector;
            Settings = new Meltype.Config.Settings
            {
                Mode = Meltype.Config.InputMode.Keyboard,
                LiveConversion = live,
                TypedKeys = typedKeys,
                TabConversion = tabConversion,
                PredictiveCandidates = predictor is not null,
                EnglishNamePredictions = englishNamePredictions,
                NowPlayingCandidates = false,
            };
            var services = new CompositionServices
            {
                Candidates = Candidates,
                ContextRules = Rules,
                History = history ?? new ConversionHistory(null),
                MoreCandidates = moreCandidates is null ? null : (reading, _) => moreCandidates(reading),
                UserDictionary = userDictionary,
                Misspellings = Misspellings,
                Languages = languages,
                Translations = translations,
                TranslationHistory = translationHistory,
                RomajiTypos = Typos,
                GitHubTitles = () => GitHubTitles,
                Predictor = predictor,
                Now = now ?? (() => DateTime.Now),
            };
            MeltypeRuntime.Connect(detector, services);
            _ime = new ImeHost(this, new SessionParts(detector, converter ?? Converter, services)
            {
                ClassifyDirect = (letters, final, settings) => MeltypeRuntime.ClassifyDirect(DirectEngine, letters, final, settings),
                ReadingOf = text => Host.Selection is { } selection && selection.Text == text ? selection.Reading : null,
                NowPlaying = () => Host.NowPlaying,
                TimeMs = () => _now,
                Corrected = Corrections.Add,
            }, () => Settings);
            if (direct) Direct = true;
        }

        InputMode? IImeEnvironment.InputMethod => InputMode.Keyboard;

        public SnippetStore Snippets { get; } = new(null);

        string? IImeEnvironment.ClipboardText => Host.ClipboardText;

        public string? OtherTipVersion { get; set; }

        /// <summary>今の「ばーじょん」の候補。</summary>
        public string VersionCandidate => Composition.Utility.VersionConversion.Candidate(Settings, OtherTipVersion);

        /// <summary>出来事を送り、応答を入力欄に当てる。vk は、本体が使わなかったらアプリに入れるキー (0 ならなし)。</summary>
        private SessionResult Send(ImeEvent e, int vk = 0, bool shift = false)
        {
            var output = _ime.Handle(e with
            {
                Instance = "keyboard",
                Field = new ImeField
                {
                    Before = Host.Before,
                    After = Host.FollowingText,
                    Selection = Host.Selection?.Text,
                    Composing = _composing,
                    App = Code ? "Code.exe" : null,
                    Traits = Private ? [ImeTrait.Private] : [],
                    CanRemove = Host.CanDeleteBackward,
                },
            });
            _state = output.State;
            _stale = false;
            if (output.State.HandlesInput) _composing = output.Result.View is not null;
            Host.Apply(output.Result, vk, shift, _ctrlHeld);
            return output.Result;
        }

        /// <summary>本体が受け取るキーか (KeyboardFront.Captures)。入力中はすべて。</summary>
        private bool Captures(int vk, bool shift)
        {
            if (_composing) return true;
            if (VirtualKeys.IsModifier(vk) || !_state.HandlesInput) return false;
            var mods = (shift ? 1 : 0) | (_ctrlHeld ? 2 : 0);
            if (_state.Capture.Shortcuts.Any(key => ToggleShortcut.NormalizeKey(key.Vk) == ToggleShortcut.NormalizeKey(vk) && key.Modifiers == mods)) return true;
            return !_ctrlHeld && Host.CharFromKey(vk, shift) is { } c && c is > ' ' and <= '~' && _state.Capture.PrintableAscii && !_state.Capture.PrintableAsciiExcept.Contains(c);
        }

        /// <summary>キーを押す・離す。本体が受け取った (アプリに通さなかった) なら true。</summary>
        public bool Key(int vk, bool up = false)
        {
            _now += 30;
            // 打つ前に入力欄にフォーカスが入る
            if (_stale) Send(new ImeEvent.CaretMoved(new ImeCaret { Moved = true, PassedKey = 0, FocusEntered = true }));
            if (vk is VirtualKeys.LControl or VirtualKeys.RControl) _ctrlHeld = !up;
            if (vk is VirtualKeys.LShift or VirtualKeys.RShift) _shiftHeld = !up;
            var shift = _shiftHeld || Host.PhysicalShift;
            if (up)
            {
                _held.Remove(vk);
                if (_releaseFollows.Remove(vk)) Send(new ImeEvent.KeyReleased(new ImeKeyRelease(vk)));
                return false;
            }
            var repeat = !_held.Add(vk);
            if (!Captures(vk, shift))
            {
                Host.Apply(new SessionResult(false, [], null), vk, shift, _ctrlHeld, captured: false);
                // アプリに通したキーはキャレットを動かすかもしれない
                if (!VirtualKeys.IsModifier(vk)) Send(new ImeEvent.CaretMoved(new ImeCaret { Moved = true, PassedKey = vk, Ctrl = _ctrlHeld }));
                return false;
            }
            var releaseFollows = _state.Capture.KeyReleases && !VirtualKeys.IsModifier(vk);
            if (releaseFollows) _releaseFollows.Add(vk);
            var ch = !_ctrlHeld && Host.CharFromKey(vk, shift) is { } c && !char.IsControl(c) ? c : 0;
            return Send(new ImeEvent.KeyPressed(new ImeKey { Vk = vk, Ch = ch, Shift = shift, Ctrl = _ctrlHeld, Repeat = repeat, ReleaseFollows = releaseFollows }),
                vk: vk, shift: shift).Consumed;
        }

        /// <summary>候補の一覧 (フロントに渡した一覧) の index 番目をクリックする。</summary>
        public void SelectCandidate(int index) => Send(new ImeEvent.CandidateClicked(index));

        /// <summary>入力欄をクリックした (キャレットが動いたかもしれない)。</summary>
        public void Click() => Send(new ImeEvent.CaretMoved(new ImeCaret { Moved = true, PassedKey = 0 }));

        /// <summary>変換ボックスに入力中か (本体がキーを受け取るか)。</summary>
        public bool Captured => _composing;

        /// <summary>変換ボックスを出していないときに、英字のキーを本体が受け取るか (英数状態で、打ち始めた語を判定している間)。</summary>
        public bool CapturesLetters => _state.Capture.PrintableAscii && !_state.Capture.PrintableAsciiExcept.Contains('a');

        /// <summary>次の打鍵まで時間を空ける (ミリ秒)。</summary>
        public void Wait(int ms) => _now += ms;

        public void Press(int vk)
        {
            Key(vk);
            Key(vk, up: true);
        }

        public void Type(string text)
        {
            foreach (var c in text)
            {
                if (char.IsAsciiLetterUpper(c))
                {
                    Key(VirtualKeys.LShift);
                    Press(c);
                    Key(VirtualKeys.LShift, up: true);
                }
                else if (c == ' ') Press(VirtualKeys.Space);
                else if (c == '\n') Press(VirtualKeys.Return);
                else if (c == '\b') Press(VirtualKeys.Back);
                else if (FakeHost.JisKeys.TryGetValue(c, out var key) && key.Shift)
                {
                    Key(VirtualKeys.LShift);
                    Press(key.Vk);
                    Key(VirtualKeys.LShift, up: true);
                }
                else if (FakeHost.JisKeys.TryGetValue(c, out key)) Press(key.Vk);
                else Press(char.ToUpperInvariant(c));
            }
        }

        public string? Showing => Host.View?.Text;
    }

    private sealed class ContextConverter : TestConverter
    {
        /// <summary>直近の、文脈付きの依頼の文脈。</summary>
        public string? Before { get; private set; }

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before)
        {
            if (before is not null) Before = before;
            return null;
        }
    }

    [Test]
    public static void Context_IsTheTextBeforeTheCaretAsIs()
    {
        // 変換エンジンには、キャレットの前の文字をそのまま渡す。どこまで使うかは変換エンジンが決める
        var converter = new ContextConverter();
        var k = new Keyboard(converter: converter);
        k.Host.PrecedingText = "昨日は雨でしたが、\n今日は";
        k.Type("kyou ");
        Assert.Equal("昨日は雨でしたが、\n今日は", converter.Before ?? "(なし)");
    }

    [Test]
    public static void PrecedingText_PassageCrossesSentences()
    {
        // Zenzai の文脈は、改行を除いて文をまたぐ文章 (最大 100 文字)
        Assert.Equal("昨日は雨でしたが、今日は", EngineContext.Passage("昨日は雨でしたが、\n今日は") ?? "(なし)", "改行を除いて文をまたいで渡す");
        var passage = EngineContext.Passage(string.Concat(Enumerable.Repeat("あ", 150)) + "終");
        Assert.Equal(100, passage?.Length ?? 0, "最大 100 文字");
        Assert.True(passage!.EndsWith('終'), "キャレットに近い方を残す");
        Assert.Equal("Hello, this is a pen.", EngineContext.Passage("Hello, this is a pen. ") ?? "(なし)", "英文もそのまま渡す");
        // Mozc の文脈は、同じ文の日本語 (最大 10 文字)
        Assert.Equal("今日は", EngineContext.Sentence("昨日は雨でした。今日は") ?? "(なし)");
    }

    [Test]
    public static void Issue887_ExeStaysEnglish()
    {
        // xe (ぇ) の小書きの綴りで最後まで読めても、拡張子の exe は英字のまま (えぇ になっていた)
        var k = new Keyboard();
        k.Type("exe");
        Assert.Equal("exe", k.Showing, "exe は英字のまま");
    }

    [Test]
    public static void Issue835_DocsSlashStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("docs/");
        Assert.Equal("docs/", k.Showing, "docs/ は英字のまま");
    }

    [Test]
    public static void Issue833_ConvertKeyOnEnglishTenOffersKana()
    {
        // 英字で見えている TEN も、変換キーで てん の候補が出る (HOGE → ほげ と同じ)
        var k = new Keyboard();
        k.Type("TEN");
        k.Press(VirtualKeys.Convert);
        Assert.True(k.Host.View?.Candidates.Contains("てん") == true, "てん が候補に: " + string.Join(",", k.Host.View?.Candidates ?? []));
        Assert.Equal("てん", k.Showing, "日本語を最初に");
    }

    [Test]
    public static void Issue839_EnglishFirstInput()
    {
        // Enter は打った英字のまま、Space は空白 (ls -la)
        var k = new Keyboard { EnglishFirstInput = true };
        k.Type("ls -la");
        Assert.Equal("ls -la", k.Showing, "英字のまま入力中 (空白も)");
        k.Type("\n");
        Assert.Equal("ls -la", k.Host.Document, "Enter は英字のまま確定");

        // sakura を打って変換キー / ↓ で日本語にする
        foreach (var key in new[] { VirtualKeys.Convert, VirtualKeys.Down })
        {
            var j = new Keyboard { EnglishFirstInput = true };
            j.Type("sakura");
            Assert.Equal("sakura", j.Showing, "変換するまで英字");
            j.Press(key);
            Assert.True(j.Host.View?.Converting == true && j.Host.View.Candidates.Contains("さくら"), "さくら が候補に: " + string.Join(",", j.Host.View?.Candidates ?? []));
            Assert.Equal("さくら", j.Showing, "日本語が最初");
            j = new Keyboard { EnglishFirstInput = true };
            j.Type("tanniwotoru");
            j.Press(key);
            Assert.True(j.Host.View?.Converting == true, $"英字から変換: {key}");
            Assert.Equal("単位を|取る", string.Join("|", j.Host.View!.Clauses!), $"文節: {key}");
            Assert.Equal(0, j.Host.View.SelectedClause, $"先頭の文節: {key}");
        }

        // 設定が OFF なら今までどおり (kyou は日本語)
        var off = new Keyboard();
        off.Type("kyou");
        Assert.Equal("きょう", off.Showing, "OFF は自動判定");
    }

    [Test]
    public static void Issue674_ExtendedYoonSpellings()
    {
        foreach (var (typed, expected) in new[] { ("kye", "きぇ"), ("gwi", "ぐぃ"), ("zwa", "ずぁ"), ("fya", "ふゃ"), ("mye", "みぇ"), ("syi", "しぃ"), ("ryi", "りぃ"), ("hwa", "ふぁ"), ("hye", "ひぇ"), ("swe", "すぇ"), ("gwo", "ぐぉ"), ("dwo", "どぉ") })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, typed);
        }
        // これらの綴りを含む英単語は、かなにしない
        foreach (var word in new[] { "trying", "swan", "swine", "highway", "pathway", "hyena", "babying", "studying", "two", "network", "bye", "rye" })
        {
            var e = new Keyboard();
            e.Type(word);
            Assert.Equal(word, e.Showing, word);
        }
    }

    [Test]
    public static void Reconversion_RomajiTextBecomesJapanese()
    {
        // #288: ローマ字のまま入った文を選んで変換キー → Meltype の判定で日本語の所だけを日本語にして候補に出す。Enter で置き換える
        foreach (var (selected, expected) in new[]
        {
            ("kyouha iitenki desu ne", "きょうはいいてんきですね"),
            ("asita no meeting de review shimasu", "あしたの meeting で review します"),
            ("PR no review onegai shimasu", "PR の review おねがいします"),
            ("iphone wo kau", "iPhone をかう"),
        })
        {
            var k = new Keyboard();
            var selection = new ReconversionSelection(selected, selected);
            k.Host.Selection = selection;
            k.Press(VirtualKeys.Convert);
            Assert.True(k.Host.View?.Converting == true, selected + ": 候補を出す");
            Assert.Equal(expected, k.Showing, selected);
            Assert.True(k.Host.View!.Candidates.Contains(selected), "選んだままの文も候補に");
            Assert.Equal(0, k.Host.Output.Count, "Enter まで置き換えない");
            k.Press(VirtualKeys.Return);
            Assert.True(k.Host.Events.Any(e => e == $"replace:{selected}:{expected}"), string.Join(" ", k.Host.Events));
        }
        // 選んだ文は、設定「自動判定」が OFF でも同じく読む (打っている入力の設定は使わない)
        var manual = new Keyboard { Manual = true };
        manual.Host.Selection = new ReconversionSelection("asita no meeting de review shimasu", "asita no meeting de review shimasu");
        manual.Press(VirtualKeys.Convert);
        Assert.Equal("あしたの meeting で review します", manual.Showing);
        // 日本語にする所が無い英文は、今までの再変換と同じく扱う (英文を勝手に日本語にしない)
        var english = new Keyboard(direct: true);
        english.Host.Selection = new ReconversionSelection("this is a pen", "this is a pen");
        english.Press(VirtualKeys.Convert);
        Assert.True(english.Showing is null || !english.Showing.Any(c => c is >= 'ぁ' and <= 'ゖ'), "英文を日本語にしない: " + english.Showing);
    }

    [Test]
    public static void SeparateKeyRoles_SpaceAlwaysOpensCandidates()
    {
        // #199: 設定「入力中のキーの役割を分ける」。英字で見えている語でも、Space で確定して空白を入れずに候補を開く
        // (英字を最初の候補に残し、ローマ字として読めれば日本語の候補も選べる)
        var k = new Keyboard { SeparateKeyRoles = true };
        k.Host.PrecedingText = "I want ";
        k.Type("made");
        Assert.Equal("made", k.Showing, "前が英語なので英字");
        k.Press(VirtualKeys.Space);
        Assert.Equal(0, k.Host.Output.Count, "確定しない");
        Assert.True(k.Host.View?.Converting == true, "候補を開く");
        Assert.Equal("made", k.Host.View!.Candidates[0]);
        Assert.True(k.Host.View!.Candidates.Contains("まで"), string.Join(" ", k.Host.View!.Candidates));
        k.Press(VirtualKeys.Return);
        Assert.Equal("made", k.Host.Document, "確定は Enter");

        // 設定が OFF なら今までどおり、英字で確定して空白を入れる
        var normal = new Keyboard();
        normal.Host.PrecedingText = "I want ";
        normal.Type("made ");
        Assert.Equal("made ", normal.Host.Document);
    }

    [Test]
    public static void Issue1122_EnglishAndJapaneseReadingsAreBothCandidates()
    {
        // midika は 身近 とも midi|か とも読める。日本語の候補と、英単語で区切った候補の両方を出す
        var k = new Keyboard();
        k.Type("midika");
        Assert.Equal("みぢか", k.Showing);
        k.Press(VirtualKeys.Space);
        Assert.Equal("身近", k.Host.View!.Candidates[0], string.Join(" ", k.Host.View!.Candidates));
        Assert.True(k.Host.View!.Candidates.Contains("midiか"), string.Join(" ", k.Host.View!.Candidates));

        // みぢか に続く形 (midi|かくにん・midi|かも) も、かなで見えたまま Space で英単語で区切った形を選べる
        foreach (var (typed, kana, split) in new[] { ("midikakuninn", "みぢかくにん", "midiかくにん"), ("midikamo", "みぢかも", "midiかも") })
        {
            var j = new Keyboard();
            j.Type(typed);
            Assert.Equal(kana, j.Showing);
            j.Press(VirtualKeys.Space);
            Assert.True(j.Host.View!.Candidates.Contains(split), $"「{typed}」: " + string.Join(" ", j.Host.View!.Candidates));
        }
    }

    [Test]
    public static void SeparateKeyRoles_ConvertNonConvertAndKanaKeys()
    {
        // 変換: 日本語を優先して変換する
        var k = new Keyboard { SeparateKeyRoles = true };
        k.Host.PrecedingText = "I want ";
        k.Type("made");
        k.Press(VirtualKeys.Convert);
        Assert.Equal("まで", k.Host.View!.Candidates[0], string.Join(" ", k.Host.View!.Candidates));

        // 無変換: 英字にする
        k = new Keyboard { SeparateKeyRoles = true };
        k.Type("kana");
        Assert.Equal("かな", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("kana", k.Showing);

        // ひらがな/カタカナ: 押すたびに ひらがな ⇔ カタカナ。英字からはまずひらがな
        k.Press(VirtualKeys.Kana);
        Assert.Equal("かな", k.Showing);
        k.Press(VirtualKeys.Kana);
        Assert.Equal("カナ", k.Showing);
        k.Press(VirtualKeys.Kana);
        Assert.Equal("かな", k.Showing);
        Assert.Equal(0, k.Host.Output.Count, "確定しない");
    }

    [Test]
    public static void NonConvertCyclesKana_BeforeConversion()
    {
        // 未変換: 全角カタカナ → 半角カタカナ → ひらがな → 全角カタカナ
        var k = new Keyboard { SeparateKeyRoles = true, NonConvertCyclesKana = true };
        k.Type("kana");
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("カナ", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("ｶﾅ", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("かな", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("カナ", k.Showing);
        Assert.Equal(0, k.Host.Output.Count, "確定しない");
    }

    [Test]
    public static void NonConvertCyclesKana_AfterConversion()
    {
        // 変換後: ひらがな → 全角カタカナ → 半角カタカナ → ひらがな
        var k = new Keyboard { SeparateKeyRoles = true, NonConvertCyclesKana = true };
        k.Type("made");
        k.Press(VirtualKeys.Convert);
        Assert.True(k.Host.View?.Converting == true, "変換中");
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("まで", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("マデ", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("ﾏﾃﾞ", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("まで", k.Showing);
    }

    [Test]
    public static void NonConvertCyclesKana_StartsWithHiraganaWhenNotShownAsHiragana()
    {
        // 英字で見えているとき (ひらがなではないとき) は、まずひらがな。そのあとは輪の次へ
        var k = new Keyboard { SeparateKeyRoles = true, NonConvertCyclesKana = true };
        k.Type("camera");
        Assert.Equal("camera", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("かめら", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("カメラ", k.Showing);
    }

    [Test]
    public static void NonConvertCyclesKana_DigitsDoNotPreventKatakana()
    {
        // 数字が混ざっていても、ひらがなが含まれていれば最初の無変換で全角カタカナにする
        var k = new Keyboard { SeparateKeyRoles = true, NonConvertCyclesKana = true };
        k.Type("123ka");
        Assert.Equal("123か", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("123カ", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("123ｶ", k.Showing);
    }

    [Test]
    public static void NonConvertCyclesKana_WorksWithoutSeparateKeyRoles()
    {
        var k = new Keyboard { SeparateKeyRoles = false, NonConvertCyclesKana = true };
        k.Type("kana");
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("カナ", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("ｶﾅ", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("かな", k.Showing);
    }

    [Test]
    public static void NonConvertCyclesKana_AfterHalfWidthKatakanaKeyReturnsToHiragana()
    {
        // F8 で半角カタカナにしたあとに押すと、輪の次のひらがなへ
        var k = new Keyboard { SeparateKeyRoles = true, NonConvertCyclesKana = true };
        k.Type("kana");
        k.Press(VirtualKeys.F8);
        Assert.Equal("ｶﾅ", k.Showing);
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("かな", k.Showing);
    }

    [Test]
    public static void NonConvertCyclesKana_OffKeepsAlphanumeric()
    {
        // 「役割を分ける」だけ ON で循環が OFF なら、今までどおり無変換で英字にする
        var k = new Keyboard { SeparateKeyRoles = true };
        k.Type("kana");
        k.Press(VirtualKeys.NonConvert);
        Assert.Equal("kana", k.Showing);
    }

    [Test]
    public static void Reconversion_ReplacesSelectedTextOnlyOnCommit()
    {
        var k = new Keyboard();
        var selection = new ReconversionSelection("今日", "きょう");
        k.Host.Selection = selection;
        k.Press(VirtualKeys.Convert);
        Assert.True(k.Host.View?.Converting == true, "選択文字の候補を表示する");
        Assert.True(ReferenceEquals(selection, k.Host.Selection), "確定前は選択文字を維持する");
        Assert.Equal(0, k.Host.Output.Count);
        var candidate = k.Showing;
        k.Press(VirtualKeys.Return);
        Assert.Equal(candidate, k.Host.Document);
        Assert.True(k.Host.Events.Any(e => e.StartsWith("replace:今日:", StringComparison.Ordinal)), "選択範囲を置き換える");
        Assert.True(!k.Captured, "確定後はキーを解放する");

        k = new Keyboard();
        k.Host.Selection = new ReconversionSelection("単位を取る", "たんいをとる");
        k.Press(VirtualKeys.Convert);
        Assert.True(k.Host.View?.Converting == true, "選択範囲を再変換");
        Assert.Equal("単位を|取る", string.Join("|", k.Host.View!.Clauses!));
        Assert.Equal(0, k.Host.View.SelectedClause, "先頭の文節を選ぶ");
        Assert.Equal(0, k.Host.Output.Count, "確定までは選択範囲を置き換えない");
    }

    [Test]
    public static void Reconversion_EscapeLeavesSelectionUntouched()
    {
        var k = new Keyboard();
        var selection = new ReconversionSelection("今日", "きょう");
        k.Host.Selection = selection;
        k.Press(VirtualKeys.Convert);
        k.Press(VirtualKeys.Escape);
        Assert.True(ReferenceEquals(selection, k.Host.Selection), "Esc で元の選択文字が残る");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:", StringComparison.Ordinal)), "取消時は置換しない");
        Assert.True(!k.Captured, "取消後はキーを解放する");
        k.Host.Selection = null; // 選択を外してから打つ
        k.Type("kana");
        k.Press(VirtualKeys.Return);
        Assert.True(k.Host.Output.Count > 0, "取消後も通常入力できる");
    }

    [Test]
    public static void ShiftSpace_WhenIdle_TypesFullWidthSpace()
    {
        // #24: 何も打っていないときの Shift+Space は全角スペース (名前の間など)。Space だけなら今までどおり半角
        var k = new Keyboard();
        k.Type("tanaka\n");
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
        k.Type("tarou\n");
        Assert.Equal("たなか　たろう", k.Host.Document);
        Assert.True(!k.Captured, "全角スペースの後は横取りしない");
    }

    [Test]
    public static void ShiftSpace_InDirectMode_IsNotFullWidth()
    {
        var k = new Keyboard(direct: true);
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.True(!k.Host.Document.Contains('　'), "英数状態では全角スペースにしない");
    }

    [Test]
    public static void Prediction_RemembersCommittedPhrase()
    {
        // #38: 確定した語句を覚え、次に読みを打ちかけたら予測の候補に出す。Tab で選んで Enter で確定
        var k = new Keyboard(predictor: new Predictor(new PhraseHistory(null), null, null));
        k.Type("kyou ");
        k.Press(VirtualKeys.Return);
        Assert.Equal("今日", k.Host.Document);
        k.Type("kyo");
        Assert.True(k.Host.View!.Predictions?.Contains("今日") == true, "予測の候補に出る: " + string.Join(",", k.Host.View.Predictions ?? []));
        k.Press(VirtualKeys.Tab);
        Assert.Equal("今日", k.Showing);
        Assert.Equal(0, k.Host.View!.SelectedPrediction);
        k.Press(VirtualKeys.Return);
        Assert.Equal("今日今日", k.Host.Document);
    }

    [Test]
    public static void Prediction_FromLastClause()
    {
        // 読み全体 (たんいをとる) の続きが無くても、最後の文節 (とる) の続きを出す。選ぶと前の文節は見えているとおりに残す
        // 前の文節とつながらない候補 (単位をトルコ料理) は出さない。最後の文節の文字 (取る) か読み (とる) で始まる候補だけ
        var phrases = new PhraseHistory(null);
        phrases.Remember("とるこりょうり", "トルコ料理");
        phrases.Remember("とることができる", "取ることができる");
        var k = new Keyboard(live: true, predictor: new Predictor(phrases, null, null));
        k.Type("tanniwotoru");
        Assert.Equal("単位を取る", k.Showing);
        Assert.Equal("取ることができる", string.Join(",", k.Host.View!.Predictions ?? []));
        k.Press(VirtualKeys.Tab);
        Assert.Equal("単位を取ることができる", k.Showing);
        k.Press(VirtualKeys.Return);
        Assert.Equal("単位を取ることができる", k.Host.Document);

        // ライブ変換が OFF なら、最後の文節では引かない (打鍵ごとに変換を増やさない)
        var off = new Keyboard(predictor: new Predictor(phrases, null, null));
        off.Type("tanniwotoru");
        Assert.True(off.Host.View!.Predictions is null, "OFF: " + string.Join(",", off.Host.View.Predictions ?? []));
    }

    [Test]
    public static void Prediction_EnglishAtEndUsesWholeReading()
    {
        // 英語として見せている語で終わるとき (きょうはMake) は、前に残す文字が分からないので、最後の文節 (まけ) の続きは出さずに読み全体の続きだけにする
        var phrases = new PhraseHistory(null);
        phrases.Remember("まけおしみ", "負け惜しみ");
        phrases.Remember("きょうはまけた", "今日は負けた");
        var k = new Keyboard(live: true, predictor: new Predictor(phrases, null, null));
        k.Type("kyouhaMake");
        Assert.True(k.Showing!.EndsWith("Make", StringComparison.Ordinal), k.Showing);
        Assert.Equal("今日は負けた", string.Join(",", k.Host.View!.Predictions ?? []));

        // 同じ読みでも、日本語で見えていれば最後の文節の続きを出す
        var japanese = new Keyboard(live: true, predictor: new Predictor(phrases, null, null));
        japanese.Type("kyouhamake");
        Assert.Equal("今日は負け", japanese.Showing);
        Assert.True(japanese.Host.View!.Predictions?.Contains("負け惜しみ") == true, "最後の文節の続き: " + string.Join(",", japanese.Host.View.Predictions ?? []));
    }

    [Test]
    public static void Prediction_TypingAfterTabCommitsIt()
    {
        // Tab で予測の候補を選んだあと、Enter を押さずに続けて打ったら、その候補で確定して続きを入力する (選ぶ前の打ちかけに戻さない)
        var phrases = new PhraseHistory(null);
        phrases.Remember("きょうのよてい", "今日の予定");
        var k = new Keyboard(predictor: new Predictor(phrases, null, null));
        k.Type("kyou");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("今日の予定", k.Showing);
        k.Type("ha");
        Assert.Equal("今日の予定", k.Host.Document);
        Assert.Equal("は", k.Showing);
    }

    [Test]
    public static void Prediction_BackSpaceAfterTabDoesNotCommit()
    {
        // Tab で予測の候補を選んだあとの BackSpace は、確定せずに、選ぶ前の打ちかけから 1 文字消す
        var phrases = new PhraseHistory(null);
        phrases.Remember("きょうのよてい", "今日の予定");
        var k = new Keyboard(predictor: new Predictor(phrases, null, null));
        k.Type("kyou");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("今日の予定", k.Showing);
        k.Press(VirtualKeys.Back);
        Assert.Equal("", k.Host.Document);
        Assert.Equal("きょ", k.Showing);
    }

    [Test]
    public static void Prediction_CompletesEnglishWord()
    {
        // #38: 英単語の打ちかけ (decis) から続きの候補 (decision, decisions …)
        var k = new Keyboard(predictor: new Predictor(null, null, null));
        k.Host.PrecedingText = "I made ";
        k.Type("decis");
        var predictions = k.Host.View!.Predictions ?? [];
        Assert.True(predictions.Contains("decision") && predictions.Contains("decisions"), string.Join(",", predictions));
        var typing = k.Showing;
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Escape);
        Assert.Equal(typing, k.Showing, "Esc で選ぶのをやめる (打った内容は残る)");
        Assert.Equal(-1, k.Host.View!.SelectedPrediction);
        k.Press(VirtualKeys.Tab);
        var first = predictions[0];
        k.Press(VirtualKeys.Return);
        Assert.Equal(first, k.Host.Document);
    }

    [Test]
    public static void Issue1213_GreetingPredictions_WithoutLearning()
    {
        // #1213: 学習がなくても、よく使うあいさつを予測の候補に出す
        var predictor = new Predictor(null, null, null);
        foreach (var (kana, phrase) in new[] { ("おは", "おはようございます"), ("ありがとう", "ありがとうございます"), ("おつかれ", "お疲れ様です"), ("よろし", "よろしくお願いします"), ("こんにち", "こんにちは") })
            Assert.True(predictor.PredictJapanese(kana).Contains(phrase), kana + ": " + string.Join(",", predictor.PredictJapanese(kana)));
        Assert.Equal(0, predictor.PredictJapanese("お").Count, "1 文字では出さない");
        // ほかの語の打ち始めにも出る読みでは出さない (どうして・もう少し・今回・おつかい・ありがたい・よろこぶ)
        foreach (var kana in new[] { "どう", "もう", "こん", "おつか", "ありが", "よろ" })
            Assert.Equal(0, predictor.PredictJapanese(kana).Count, kana + ": " + string.Join(",", predictor.PredictJapanese(kana)));
        Assert.True(!predictor.PredictJapanese("こんに").Contains("こんばんは"), "こんにちは と こんばんは は見分けがつくまで出さない");
        // よろしく と打ったときも、どうぞよろしくお願いします を出す (読みの途中から)
        Assert.True(predictor.PredictJapanese("よろしく") is { } yoroshiku && yoroshiku.Contains("よろしくお願いします") && yoroshiku.Contains("どうぞよろしくお願いします"),
            string.Join(",", predictor.PredictJapanese("よろしく")));
        Assert.Equal(0, predictor.PredictJapanese("きょうは").Count, "定型句にない読みでは出さない");
        Assert.True(!predictor.PredictJapanese("こんにちは").Contains("こんにちは"), "打ち終えた読みと同じ語句は出さない");

        // 前に確定した語句を、定型句より先に出す
        var phrases = new PhraseHistory(null);
        phrases.Remember("おはよう", "お早う");
        Assert.Equal("お早う", new Predictor(phrases, null, null).PredictJapanese("おは")[0]);

        // Tab で選んで Enter で確定する。選ばなければ、今までどおり打った読みを確定する
        var k = new Keyboard(predictor: predictor);
        k.Type("oha");
        Assert.True(k.Host.View!.Predictions?.Contains("おはようございます") == true, string.Join(",", k.Host.View.Predictions ?? []));
        k.Press(VirtualKeys.Tab);
        Assert.Equal("おはようございます", k.Showing);
        k.Press(VirtualKeys.Return);
        Assert.Equal("おはようございます", k.Host.Document);

        var plain = new Keyboard(predictor: predictor);
        plain.Type("oha");
        plain.Press(VirtualKeys.Return);
        Assert.Equal("おは", plain.Host.Document);

        // Esc で選ぶのをやめる (打った読みは残る)
        var cancel = new Keyboard(predictor: predictor);
        cancel.Type("yorosi");
        cancel.Press(VirtualKeys.Tab);
        cancel.Press(VirtualKeys.Escape);
        Assert.Equal(-1, cancel.Host.View!.SelectedPrediction);
        cancel.Press(VirtualKeys.Return);
        Assert.Equal("よろし", cancel.Host.Document);
    }

    [Test]
    public static void Issue890_EnglishNamePredictions_FromKanaPrefix()
    {
        // #890: 社名・サービス名を読みで打ちかけたら、英字の名前も予測の候補に出す (設定が ON のとき)
        var predictor = new Predictor(null, null, null);
        Assert.True(Predictor.PredictEnglishNames("しゃお").Contains("Xiaomi"), string.Join(",", Predictor.PredictEnglishNames("しゃお")));
        Assert.True(Predictor.PredictEnglishNames("ぎっと") is { } git && git.Contains("GitHub") && git.Contains("GitLab"), string.Join(",", Predictor.PredictEnglishNames("ぎっと")));
        Assert.True(Predictor.PredictEnglishNames("しゃおみ").Contains("Xiaomi"), "読みを打ち終わっても出す");
        Assert.Equal(1, Predictor.PredictEnglishNames("しゃおみ").Count(w => w == "Xiaomi"), "同じ語は 1 つだけ (しゃおみ・しゃおみー)");
        // #1040・#959: めるたいぷ → Meltype (候補と予測の両方)
        Assert.True(Predictor.PredictEnglishNames("めるた").Contains("Meltype"), string.Join(",", Predictor.PredictEnglishNames("めるた")));
        Assert.True(CandidateDictionary.Load(null).Lookup("めるたいぷ").Contains("Meltype"), "めるたいぷ の候補に Meltype");
        Assert.Equal(0, Predictor.PredictEnglishNames("しゃ").Count, "2 文字では出さない");
        Assert.True(Predictor.PredictEnglishNames("あいあ").Count <= Predictor.MaxPredictions, "多すぎない");
        Assert.Equal(0, Predictor.PredictEnglishNames("ゐゐゐ").Count, "無い読みは出さない");

        var on = new Keyboard(predictor: predictor, englishNamePredictions: true);
        on.Type("shao");
        Assert.True(on.Host.View!.Predictions?.Contains("Xiaomi") == true, "ON なら出る: " + string.Join(",", on.Host.View.Predictions ?? []));
        on.Press(VirtualKeys.Tab);
        while (on.Showing != "Xiaomi" && on.Host.View!.SelectedPrediction < (on.Host.View.Predictions?.Count ?? 0) - 1) on.Press(VirtualKeys.Tab);
        on.Press(VirtualKeys.Return);
        Assert.Equal("Xiaomi", on.Host.Document);

        var off = new Keyboard(predictor: predictor);
        off.Type("shao");
        Assert.True(off.Host.View!.Predictions?.Contains("Xiaomi") != true, "OFF (既定) なら出さない");
    }

    [Test]
    public static void Issue890_EnglishNamePredictions_OffByDefault() =>
        Assert.True(!new Meltype.Config.Settings().EnglishNamePredictions, "既定で OFF");

    [Test]
    public static void Prediction_OffWithoutPredictor()
    {
        var k = new Keyboard();
        k.Type("decis");
        Assert.True(k.Host.View!.Predictions is null, "予測の元が無ければ出さない");
    }

    [Test]
    public static void Issue545_ExclamationOffersBangBangEmoji()
    {
        var k = new Keyboard();
        k.Type("! ");
        Assert.True(k.Host.View!.Candidates.Contains("‼️"), string.Join(" ", k.Host.View.Candidates));
    }

    [Test]
    public static void Emoji_AreLastAndReachedByUp()
    {
        // #133: 絵文字・顔文字は候補の最後に逆順でまとめる。変換してすぐ ↑ で、いちばんよく使う絵文字 (えがお → 😊) になる
        var k = new Keyboard();
        k.Type("egao ");
        var view = k.Host.View!;
        Assert.Equal("😊", view.Candidates[^1], string.Join(" ", view.Candidates));
        var firstEmoji = view.Candidates.ToList().FindIndex(c => c is "😊" or "😄" or "(^^)" or "😀");
        Assert.True(firstEmoji > view.Candidates.ToList().IndexOf("エガオ"), "絵文字はカタカナより後ろ: " + string.Join(" ", view.Candidates));
        k.Press(VirtualKeys.Up);
        Assert.Equal("😊", k.Showing);
        k.Press(VirtualKeys.Up);
        Assert.Equal("😄", k.Showing);
    }

    [Test]
    public static void Issue2111_KanaChoiceReplacesLearnedWord()
    {
        // では → 出端 を覚えたあと、かなの では を選んで確定したら、次の では は かなを最初の候補にする
        var history = new ConversionHistory(null);
        history.Remember("では", "出端");
        var k = new Keyboard(history: history);
        k.Type("deha ");
        Assert.Equal("出端", k.Showing, "覚えた語が最初の候補");
        for (var i = 0; i < 30 && k.Showing != "では"; i++) k.Press(VirtualKeys.Space);
        Assert.Equal("では", k.Showing, "かなの候補まで進める");
        k.Type("\n");
        var next = new Keyboard(history: history);
        next.Type("deha ");
        Assert.Equal("では", next.Showing, "選んだかなを覚える");
    }

    [Test]
    public static void FunctionKeyOnClauseIsLearned()
    {
        // 文節だけを F6 でかなにして確定したら、候補からかなを選んだのと同じく覚える
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history) { FunctionKeysAffectSelectedClause = true };
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.F6);
        Assert.Equal("たんいを取る", string.Concat(k.Host.View!.Clauses!));
        k.Type("\n");
        var next = new Keyboard(history: history);
        next.Type("tanniwotoru ");
        Assert.Equal("たんいを取る", next.Showing, "かなにした文節を覚える");
    }

    [Test]
    public static void EscOnClauseIsLearned()
    {
        // 設定「変換中の Esc は選んだ文節だけをかなに戻す」で戻して確定したのも、かなを選んだのと同じく覚える
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history) { EscRevertsSelectedClause = true };
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Escape);
        k.Type("\n");
        var next = new Keyboard(history: history);
        next.Type("tanniwotoru ");
        Assert.Equal("たんいを取る", next.Showing, "かなに戻した文節を覚える");
    }

    [Test]
    public static void Now_ChosenTimeIsNotLearned()
    {
        // いま → 17:22 を選んで確定しても覚えない (覚えると、次の いま で古い時刻が最初に出る)
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history, now: () => new DateTime(2026, 10, 8, 17, 22, 0));
        k.Type("ima ");
        var first = k.Showing;
        for (var i = 0; i < 30 && k.Showing != "17:22"; i++) k.Press(VirtualKeys.Space);
        Assert.Equal("17:22", k.Showing, "時刻の候補まで進める");
        k.Type("\n");
        var next = new Keyboard(history: history, now: () => new DateTime(2026, 10, 8, 18, 5, 0));
        next.Type("ima ");
        Assert.Equal(first, next.Showing, "最初の候補は前と同じ (時刻を覚えていない)");
    }

    [Test]
    public static void TypedKeys_ShownWhenEnabled()
    {
        // #224: 設定「打ったキーを表示」が ON なら、変換ボックスに打ったキーを渡す (変換中も)
        var k = new Keyboard(typedKeys: Meltype.Config.TypedKeysStyle.Keys);
        k.Type("kyouha");
        Assert.Equal("kyouha", k.Host.View!.Typed);
        k.Type(" ");
        Assert.True(k.Host.View!.Converting, "変換中");
        Assert.Equal("kyouha", k.Host.View!.Typed, "変換中も打ったキーを出す");
        var off = new Keyboard();
        off.Type("kyouha");
        Assert.True(off.Host.View!.Typed is null, "OFF なら出さない");
    }

    [Test]
    public static void TypedKeys_KanaShowsReadingBeforeKanji()
    {
        // 「漢字変換前」は、英日に分けたあと・漢字にする前の形を出す (漢字になっても読みを確かめられる)
        var k = new Keyboard(typedKeys: Meltype.Config.TypedKeysStyle.Kana);
        k.Type("kyouha ");
        Assert.True(k.Host.View!.Converting, "変換中");
        Assert.Equal("きょうは", k.Host.View!.Typed, k.Host.View!.Text);
        // 表示と同じなら出さない (かなのまま・語末の n・記号と数字だけ)
        foreach (var keys in new[] { "kyouha", "hon", "hon ", "---", "2026-11-25" })
        {
            var same = new Keyboard(typedKeys: Meltype.Config.TypedKeysStyle.Kana);
            same.Type(keys);
            Assert.True(same.Host.View!.Typed is null, $"{keys}: {same.Host.View.Text} に {same.Host.View.Typed}");
        }
        // 区切りの別案・打ち間違いの直し・語末の n も、今の変換が使っている読みで出す
        foreach (var (keys, typed) in new[]
        {
            ("bottoshuuseisita  ", null),
            ("bottoshuuseisita   ", "ぼっとしゅうせいした"),
            ("onegaisimsu", null),
            ("onegaisimsu  ", "おねがいします"),
            ("hon  ", "ほん"),
        })
        {
            var reading = new Keyboard(typedKeys: Meltype.Config.TypedKeysStyle.Kana);
            reading.Type(keys);
            Assert.Equal(typed, reading.Host.View!.Typed, $"{keys}: {reading.Host.View.Text}");
        }
    }

    [Test]
    public static void Brand_GitHubFromKana()
    {
        // #231: ぎっとはぶ・ギットハブ を変換すると GitHub が候補に出る
        foreach (var keys in new[] { "gittohabu ", "gittohabude " })
        {
            var k = new Keyboard();
            k.Type(keys);
            var candidates = k.Host.View!.Candidates;
            Assert.True(candidates.Any(c => c.StartsWith("GitHub", StringComparison.Ordinal)), keys + ": " + string.Join(" ", candidates));
        }
    }

    [Test]
    public static void Issue231_ImeOffersIme()
    {
        // #231: いめ を変換すると IME が候補に出る。後ろの助詞はかなのまま。
        foreach (var (keys, expected) in new[] { ("ime ", "IME"), ("imede ", "IMEで") })
        {
            var k = new Keyboard();
            k.Type(keys);
            var candidates = k.Host.View!.Candidates;
            Assert.True(candidates.Contains(expected), keys + ": " + string.Join(" ", candidates));
        }
    }

    [Test]
    public static void Now_ShowsCurrentTime()
    {
        // #208: いま・なう を変換すると、今の時刻 (17:22 / 17時22分 / 午後5時22分) も候補に出る
        foreach (var keys in new[] { "ima ", "nau " })
        {
            var k = new Keyboard(now: () => new DateTime(2026, 10, 8, 17, 22, 0));
            k.Type(keys);
            var candidates = k.Host.View!.Candidates;
            foreach (var expected in new[] { "17:22", "17時22分", "午後5時22分" })
                Assert.True(candidates.Contains(expected), keys + ": " + string.Join(" ", candidates));
        }
        var morning = new Keyboard(now: () => new DateTime(2026, 10, 8, 9, 5, 0));
        morning.Type("ima ");
        Assert.True(morning.Host.View!.Candidates.Contains("09:05") && morning.Host.View!.Candidates.Contains("午前9時5分"),
            string.Join(" ", morning.Host.View!.Candidates));
        // 日付と曜日つき (2026/10/8 は木曜日)
        var dated = new Keyboard(now: () => new DateTime(2026, 10, 8, 17, 22, 0));
        dated.Type("ima ");
        foreach (var expected in new[] { "2026年10月8日(木) 17時22分", "10月8日(木) 17時22分", "2026/10/08 17:22" })
            Assert.True(dated.Host.View!.Candidates.Contains(expected), string.Join(" ", dated.Host.View!.Candidates));
        // きょう → 今日の日付
        var today = new Keyboard(now: () => new DateTime(2026, 10, 8, 17, 22, 0));
        today.Type("kyou ");
        foreach (var expected in new[] { "2026年10月8日", "2026年10月8日(木)", "10月8日(木)", "2026/10/08", "2026/10/08(木)", "10/8(木)", "2026-10-08", "木曜日", "20261008" })
            Assert.True(today.Host.View!.Candidates.Contains(expected), string.Join(" ", today.Host.View!.Candidates));
        var other = new Keyboard(now: () => new DateTime(2026, 10, 8, 17, 22, 0));
        other.Type("imada ");
        Assert.True(!other.Host.View!.Candidates.Contains("17:22"), "いま だけの文節のとき");
    }

    [Test]
    public static void TypedDate_OffersSlashForms()
    {
        // #294: 打った日付 (10がつ9にち) にも 10/9 の形を候補に出す。いつもの候補 (変換エンジンの結果) は最初のまま。
        // 変換エンジンが 10がつ|9にち に分けても 1 つの文節にまとめる
        var converter = new SplitConverter(new()
        {
            ["10がつ9にち"] = [new("10がつ", "10月"), new("9にち", "9日")],
            ["12がつ25にち"] = [new("12がつ25にち", "12月25日")],
            ["3じ15ふん"] = [new("3じ", "3時"), new("15ふん", "15分")],
        });
        foreach (var (keys, first, expected) in new[]
        {
            ("10gatsu9nichi ", "10月9日", new[] { "10/9", "10/09", "10/9(金)", "10月9日(金)", "2026/10/09", "2026/10/09(金)" }),
            ("12gatsu25nichi ", "12月25日", new[] { "12/25", "12/25(金)" }),
            ("3ji15hun ", "3時15分", new[] { "3:15", "03:15" }),
        })
        {
            var k = new Keyboard(converter: converter, now: () => new DateTime(2026, 10, 8, 17, 22, 0));
            k.Type(keys);
            var candidates = k.Host.View!.Candidates;
            Assert.Equal(first, candidates[0], keys);
            foreach (var form in expected) Assert.True(candidates.Contains(form), keys + ": " + string.Join(" ", candidates));
        }
        // ありえない日付には出さない
        var bad = new Keyboard(converter: new SplitConverter(new() { ["2がつ30にち"] = [new("2がつ30にち", "2月30日")] }), now: () => new DateTime(2026, 10, 8));
        bad.Type("2gatsu30nichi ");
        Assert.True(!bad.Host.View!.Candidates.Any(c => c.Contains('/')), string.Join(" ", bad.Host.View!.Candidates));

        // 選んでも学習しない (次に違う日付を打ったときに邪魔になる)
        var history = new ConversionHistory(null);
        var chosen = new Keyboard(converter: converter, history: history, now: () => new DateTime(2026, 10, 8));
        chosen.Type("10gatsu9nichi ");
        var index = chosen.Host.View!.Candidates.ToList().IndexOf("10/9");
        for (var i = 0; i < index; i++) chosen.Press(VirtualKeys.Space);
        chosen.Press(VirtualKeys.Return);
        Assert.Equal("10/9", chosen.Host.Document);
        Assert.True(history.Get("10がつ9にち") is null, "学習しない");
        // #935: 曜日付きの 10月9日(金) を選んでも学習しない
        var weekday = new Keyboard(converter: converter, history: history, now: () => new DateTime(2026, 10, 8));
        weekday.Type("10gatsu9nichi ");
        var weekdayIndex = weekday.Host.View!.Candidates.ToList().IndexOf("10月9日(金)");
        for (var i = 0; i < weekdayIndex; i++) weekday.Press(VirtualKeys.Space);
        weekday.Press(VirtualKeys.Return);
        Assert.Equal("10月9日(金)", weekday.Host.Document);
        Assert.True(history.Get("10がつ9にち") is null, "曜日付きも学習しない");
    }

    /// <summary>決めた文節に区切る変換エンジン。</summary>
    private sealed class SplitConverter(Dictionary<string, ConversionClause[]> clauses) : TestConverter
    {
        private readonly FakeConverter _inner = new();
        public override string? Convert(string hiragana, string? before) => _inner.Convert(hiragana, before);
        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) =>
            clauses.TryGetValue(hiragana, out var result) ? result : _inner.ConvertClauses(hiragana, before);
    }

    [Test]
    public static void ShiftSpace_DuringConversion_GoesBack()
    {
        // #140: 変換中の Shift+Space は前の候補へ (Space と同じに進んでいた)
        var k = new Keyboard();
        k.Type("kawa ");
        k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Space);
        Assert.Equal(2, k.Host.View!.SelectedIndex);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal(1, k.Host.View!.SelectedIndex);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal(k.Host.View!.Candidates.Count - 1, k.Host.View!.SelectedIndex, "先頭から戻ると最後の候補へ");
    }

    [Test]
    public static void F10_CyclesLetterCase()
    {
        // #58 #61: F10 を続けて押すと ai → AI → Ai → ai
        var k = new Keyboard();
        k.Type("aiueo");
        Assert.Equal("あいうえお", k.Showing);
        var shown = new List<string?>();
        for (var i = 0; i < 4; i++)
        {
            k.Press(VirtualKeys.F10);
            shown.Add(k.Showing);
        }
        Assert.Equal("aiueo,AIUEO,Aiueo,aiueo", string.Join(",", shown));
        k.Press(VirtualKeys.F10);
        k.Press(VirtualKeys.Return);
        Assert.Equal("AIUEO", k.Host.Document);
    }

    [Test]
    public static void F9_CyclesLetterCase_FullWidth()
    {
        var k = new Keyboard();
        k.Type("aiueo");
        k.Press(VirtualKeys.F9);
        Assert.Equal("ａｉｕｅｏ", k.Showing);
        k.Press(VirtualKeys.F9);
        Assert.Equal("ＡＩＵＥＯ", k.Showing);
        // F10 に切り替えたら打ったまま (小文字) から
        k.Press(VirtualKeys.F10);
        Assert.Equal("aiueo", k.Showing);
    }

    [Test]
    public static void F10_OnEnglishWord_GoesStraightToUpperCase()
    {
        // 英単語と判定して英字で見せている語は、1 回目の F10 で大文字にする (押しても何も変わらないように見えないように)
        var k = new Keyboard();
        k.Type("hello");
        Assert.Equal("hello", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("HELLO", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("Hello", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("hello", k.Showing);
    }

    [Test]
    public static void F10_CaseKeepsDigitsAndSymbols()
    {
        // 英字に数字・記号が混ざっていても、変わるのは英字だけ (api2.0 → API2.0 → Api2.0 → api2.0)
        var k = new Keyboard();
        k.Type("api2.0");
        var shown = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            k.Press(VirtualKeys.F10);
            shown.Add(k.Showing);
        }
        Assert.Equal("API2.0,Api2.0,api2.0", string.Join(",", shown));
    }

    [Test]
    public static void F9F10_SwitchingResetsCase()
    {
        // F10 で大文字にした後に F9 (全角) にすると、打ったまま (小文字) から。F9 の大文字の後に F10 でも同じ
        var k = new Keyboard();
        k.Type("abc");
        k.Press(VirtualKeys.F10);
        Assert.Equal("ABC", k.Showing);
        k.Press(VirtualKeys.F9);
        Assert.Equal("ａｂｃ", k.Showing);
        k.Press(VirtualKeys.F9);
        Assert.Equal("ＡＢＣ", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("abc", k.Showing);
    }

    [Test]
    public static void F10_CaseResetsAfterCommit()
    {
        var k = new Keyboard();
        k.Type("ai");
        k.Press(VirtualKeys.F10);
        k.Press(VirtualKeys.F10);
        k.Press(VirtualKeys.Return);
        k.Type("aiueo");
        k.Press(VirtualKeys.F10);
        Assert.Equal("aiueo", k.Showing);
    }

    private static void CtrlPress(Keyboard k, int vk)
    {
        k.Key(VirtualKeys.LControl);
        k.Press(vk);
        k.Key(VirtualKeys.LControl, up: true);
    }

    [Test]
    public static void CtrlUiop_SwitchesKanaAndLetters()
    {
        // #53: Ctrl+P → 全角英数、Ctrl+O → 半角英数 (続けて押すと大文字)、Ctrl+I → カタカナ、Ctrl+U → ひらがな
        var k = new Keyboard();
        k.Type("aiueo");
        CtrlPress(k, 'P');
        Assert.Equal("ａｉｕｅｏ", k.Showing);
        CtrlPress(k, 'O');
        Assert.Equal("aiueo", k.Showing);
        CtrlPress(k, 'O');
        Assert.Equal("AIUEO", k.Showing);
        CtrlPress(k, 'I');
        Assert.Equal("アイウエオ", k.Showing);
        CtrlPress(k, 'U');
        Assert.Equal("あいうえお", k.Showing);
        Assert.Equal(0, k.Host.Output.Count);
        Assert.True(!k.Host.Events.Any(e => e == "down:A2"), "Ctrl はアプリに送らない");
    }

    [Test]
    public static void CtrlH_IsBackSpaceWhileComposing()
    {
        // #150: 入力中の Ctrl+H は BackSpace (確定しない)。変換中なら変換をやめてかなに戻す
        var k = new Keyboard();
        k.Type("aiueo");
        CtrlPress(k, 'H');
        Assert.Equal("あいうえ", k.Showing);
        Assert.Equal(0, k.Host.Output.Count);
        Assert.True(!k.Host.Events.Any(e => e.EndsWith(":A2", StringComparison.Ordinal)), "Ctrl を送らない: " + string.Join(" ", k.Host.Events));

        k.Press(VirtualKeys.Space);
        CtrlPress(k, 'H');
        Assert.Equal("あいうえ", k.Showing);
        k.Press(VirtualKeys.Return);
        Assert.Equal("あいうえ", k.Host.Document);

        // 変換ボックスが空なら、Ctrl+H はアプリの操作 (置換など) としてそのまま送る
        k = new Keyboard();
        CtrlPress(k, 'H');
        Assert.True(k.Host.Events.Contains("passed:48"), "Ctrl+H をアプリに通す: " + string.Join(" ", k.Host.Events));
    }

    [Test]
    public static void CtrlHeld_RepeatsShortcutAndMatchesUps()
    {
        // Ctrl を押したまま O を 2 回 (半角英数 → 大文字)。右 Ctrl でも同じ。Ctrl はアプリに送らず、上げ下げもそろったまま
        foreach (var control in new[] { VirtualKeys.LControl, VirtualKeys.RControl })
        {
            var k = new Keyboard();
            k.Type("aiueo");
            k.Key(control);
            k.Press('O');
            k.Press('O');
            k.Key(control, up: true);
            Assert.Equal("AIUEO", k.Showing);
            Assert.True(!k.Host.Events.Any(e => e.EndsWith($":{control:X2}", StringComparison.Ordinal)), "Ctrl を送らない: " + string.Join(" ", k.Host.Events));
            k.Press(VirtualKeys.Return);
            Assert.Equal("AIUEO", k.Host.Document);
        }
    }

    [Test]
    public static void CtrlShiftShortcut_SendsModifiersInOrder()
    {
        // Ctrl+Shift+Z: 確定してから Z をアプリに通す (Ctrl と Shift は Z と一緒に届いている)
        var k = new Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        k.Key(VirtualKeys.LShift);
        k.Press('Z');
        k.Key(VirtualKeys.LShift, up: true);
        k.Key(VirtualKeys.LControl, up: true);
        Assert.Equal("text:かな|down:5A", string.Join("|", k.Host.Events));
        Assert.True(!k.Captured, "ショートカットの後は受け取らない");
    }

    [Test]
    public static void CtrlOtherShortcut_CommitsThenPasses()
    {
        var k = new Keyboard();
        k.Type("aiueo");
        CtrlPress(k, 'C');
        Assert.Equal("あいうえお", k.Host.Document);
        Assert.Equal("text:あいうえお|down:43", string.Join("|", k.Host.Events), "確定してから Ctrl+C を通す");
    }

    [Test]
    public static void DigitKey_SelectsCandidateByNumber()
    {
        // #35: 変換中に候補の番号 (1〜9) を押すと、その候補を選ぶ (打った数字が入るのではなく)。
        var k = new Keyboard();
        k.Type("kawa ");
        var view = k.Host.View!;
        Assert.True(view.Converting && view.Candidates.Count >= 2, "候補の一覧が出る");
        var second = view.Candidates[1];
        k.Press('2');
        Assert.Equal(second, k.Host.Document);
        Assert.True(!k.Captured, "最後の文節を番号で選んだら確定する");
    }

    [Test]
    public static void DigitKey_MovesToNextClause()
    {
        var k = new Keyboard();
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        var first = k.Host.View!.Candidates;
        k.Press('2');
        var view = k.Host.View!;
        Assert.True(view.Converting, "途中の文節なら確定せずに次の文節へ");
        Assert.Equal(1, view.SelectedClause);
        Assert.Equal(first[1], view.Clauses![0]);
        Assert.Equal(0, k.Host.Output.Count);
    }

    [Test]
    public static void DigitKey_WithoutCandidateTypesDigit()
    {
        // 候補の数ちょうど (最後の候補) は選べて、その次の番号 (候補の無い番号) は打った数字になる (境界を必ず確かめる)。
        // 辞書に無い読み (ぞぞぞ) にして、候補を ひらがな・カタカナ・半角カタカナ・打ったままの英字・全角の英字 に決める
        // (辞書の語が増えても候補の数が変わらないように)
        var k = new Keyboard();
        k.Type("zozozo ");
        var candidates = k.Host.View!.Candidates;
        var count = candidates.Count;
        Assert.Equal("ぞぞぞ,ゾゾゾ,ｿﾞｿﾞｿﾞ,zozozo,ｚｏｚｏｚｏ", string.Join(",", candidates));
        k.Press('0' + count);
        Assert.Equal(candidates[^1], k.Host.Document, "最後の番号は最後の候補");

        k = new Keyboard();
        k.Type("zozozo ");
        var digit = (char)('0' + count + 1);
        k.Press(digit);
        Assert.True(k.Host.Document.EndsWith(digit) || k.Showing == digit.ToString(), "番号の無い数字は普通に打った数字: " + k.Host.Document + " / " + k.Showing);
    }

    [Test]
    public static void KanaInput_DigitRowKeyTypesKana_WhileConverting()
    {
        // かな入力では数字の段のキーは ぬふあうえおやゆよ。変換中に打っても番号で選ばず、今の候補で確定して打ったかなを入れる。
        var k = new Keyboard { Kana = true };
        k.TypeKanaKeys("tu");
        k.Press(VirtualKeys.Space);
        var current = k.Host.View!.Clauses![0];
        k.Press(0x33);
        Assert.Equal(current, k.Host.Document, "今の候補で確定する");
        Assert.Equal("あ", k.Showing, "3 のキーは あ");
    }

    [Test]
    public static void KanaInput_NumpadSelectsCandidateByNumber()
    {
        // かな入力でも、テンキーの数字なら候補の番号で選べる。
        var k = new Keyboard { Kana = true };
        k.TypeKanaKeys("tu");
        k.Press(VirtualKeys.Space);
        var view = k.Host.View!;
        Assert.True(view.Converting && view.Candidates.Count >= 2, "候補の一覧が出る");
        var second = view.Candidates[1];
        k.Press(0x62);
        Assert.Equal(second, k.Host.Document);
        Assert.True(!k.Captured, "最後の文節を番号で選んだら確定する");
    }

    [Test]
    public static void Slash_AsMiddleDot_WhenEnabled()
    {
        // #122: 設定が ON なら、かなの後ろの / は ・。英字・数字の後ろ・打ち始めは / のまま。OFF なら今までどおり /
        foreach (var (typed, on, expected) in new[]
        {
            ("iron/masuku", true, "いろん・ますく"), ("iron/masuku", false, "いろん/ますく"),
            ("3/4", true, "3/4"), ("/help", true, "/help"), ("and/or", true, "and/or"),
        })
        {
            var k = new Keyboard { SigilWords = false, Slash = on ? Meltype.Config.SlashStyle.MiddleDot : Meltype.Config.SlashStyle.Slash }; // 先頭の /help は #193 でそのまま入力になるので切って比べる
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, $"{typed} ({(on ? "ON" : "OFF")})");
        }
    }

    [Test]
    public static void Reconversion_WithoutSelectionDoesNothing()
    {
        var k = new Keyboard();
        k.Press(VirtualKeys.Convert);
        Assert.True(!k.Captured, "選択文字なしでは変換を開始しない");
        Assert.True(!k.Host.Events.Contains("down:1C"), "Windows IME に変換キーを渡さない");
        Assert.Equal(0, k.Host.Output.Count);
    }

    [Test]
    public static void Reconversion_DeletingReadingDoesNotAffectNextInput()
    {
        var k = new Keyboard();
        k.Host.Selection = new ReconversionSelection("橋", "はし");
        k.Press(VirtualKeys.Convert);
        k.Press(VirtualKeys.Back); // 候補選択から読みの編集に戻る。
        k.Press(VirtualKeys.Back);
        k.Press(VirtualKeys.Back);
        Assert.True(!k.Captured, "読みを消し切ったら再変換を終了する");
        k.Host.Selection = null; // 選択を外してから打つ
        k.Type("kana");
        k.Press(VirtualKeys.Return);
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:", StringComparison.Ordinal)), "次の入力に再変換の状態を持ち越さない");
        Assert.True(k.Host.Output.Count > 0, "次の入力は普通に確定する");
    }

    [Test]
    public static void ConvertKey_ConvertsInputAndCyclesCandidates()
    {
        var k = new Keyboard();
        k.Type("hashi");
        k.Press(VirtualKeys.Convert);
        Assert.True(k.Host.View?.Converting == true, "変換キーで入力中の文字を変換する");
        var first = k.Showing;
        k.Press(VirtualKeys.Convert);
        Assert.True(k.Showing != first, "変換キーで次の候補に進む");
        var selected = k.Showing;
        k.Press(VirtualKeys.Return);
        Assert.Equal(selected, k.Host.Document);
    }

    [Test]
    public static void Romaji_IsShownAsKanaAndCommittedWithEnter()
    {
        var k = new Keyboard();
        k.Type("konnnichiha");
        Assert.Equal("こんにちは", k.Showing, "未確定の間は変換ボックスにかなで表示");
        Assert.Equal(0, k.Host.Output.Count, "Enter まではテキストボックスに何も入らない");
        k.Type("\n");
        Assert.Equal("こんにちは", k.Host.Output.Single());
        Assert.True(k.Showing is null, "確定したら変換ボックスを閉じる");
        Assert.True(!k.Captured, "確定後はキーを横取りしない");
    }

    [Test]
    public static void Google_IsShownAsEnglish_NotGoogle_Kana()
    {
        var k = new Keyboard();
        k.Type("goog");
        Assert.Equal("goog", k.Showing, "固有名詞 (Google) の先頭と分かった時点で英字");
        k.Type("le");
        Assert.Equal("google", k.Showing, "英単語と分かった時点で英字に切り替わる (ごおｇぇ にならない)");
        k.Type("\n");
        Assert.Equal("google", k.Host.Output.Single());
    }

    [Test]
    public static void OnlyTheEnglishWordBecomesEnglish()
    {
        // 前に日本語があっても、英単語の部分だけが英字になる。
        var cases = new Dictionary<string, string>
        {
            ["kyouhagoogle"] = "きょうはgoogle",
            ["kyouhanikonha"] = "きょうはにこんは",
            ["googlede"] = "googleで",
            ["kyouhagoogledekensaku"] = "きょうはgoogleでけんさく",
            ["githubnipush"] = "GitHubにpush",
            ["repo"] = "repo",
            ["who"] = "who",
            ["koreha"] = "これは",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
    }

    // 実機で報告: 「やあやあ、私だよ」→「やあやあ、だよ私」、「ところでgoogleって」→「ところでってgoogle」
    [Test]
    public static void ReportedOrderSwaps()
    {
        foreach (var live in new[] { false, true })
        {
            var k = new Keyboard(live);
            k.Type("tokorodegooglette\n");
            Assert.Equal("ところでgoogleって", string.Concat(k.Host.Output), $"live={live}");

            k = new Keyboard(live);
            k.Type("yaayaa,watashidayo\n");
            Assert.Equal("やあやあ、わたしだよ", string.Concat(k.Host.Output), $"live={live}");
        }
    }

    [Test]
    public static void Clauses_SokuonAfterKatakanaIsKatakana()
    {
        // 報告: すぱいだーまっ → スパイダーマっ。カタカナの語の後ろの「っ」の文節はカタカナの「ッ」にする。
        var k = new Keyboard();
        k.Type("supaida-maxtu ");
        Assert.Equal("スパイダーマ|ッ", string.Join("|", k.Host.View!.Clauses!));
    }

    [Test]
    public static void Clauses_InterjectionE_AndGachi()
    {
        // summary.json: 文頭の「え、」が 得、、がちで が 勝ちで になっていた (勝ち の読みは かち)
        // 英日の区切りの別案 (え、し|ran) があると文全体の 1 文節になるので、文節の区切りでなく変換した文で比べる
        foreach (var (typed, expected) in new[] { ("e,shiran ", "え、知らん"), ("ekaku ", "絵描く"), ("gachideyabai ", "ガチでヤバい"),
            // 英単語 + 付属語・する: python|奈良、push|下、commit|死体 になっていた。英単語の無いところ (なら = 奈良、した = 下) は変換エンジンのまま
            ("pythonnara ", "pythonなら"), ("nara ", "奈良"), ("pushshita ", "pushした"), ("commitshitai ", "commitしたい"), ("shita ", "下") })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, string.Concat(k.Host.View!.Clauses!), typed);
        }
    }

    [Test]
    public static void EnglishParticle_SpaceCommitEndingWithEnglish_KeepsKana()
    {
        // 英語で終わる入力は Space で変換せずに確定する。そのときも英単語の後ろの付属語はかなのまま (python奈良github になっていた)
        var k = new Keyboard(live: true);
        k.Type("pythonnaragithub ");
        Assert.Equal("pythonならGitHub ", k.Host.Output.Single());
    }

    [Test]
    public static void Clauses_SelectWithArrowsAndConvertEach()
    {
        var k = new Keyboard();
        k.Type("nihongowokaku ");
        Assert.Equal("日本語を|書く", string.Join("|", k.Host.View!.Clauses!), "Space で文節に区切って変換");
        Assert.Equal(0, k.Host.View.SelectedClause, "最初は先頭の文節を選択");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View.SelectedClause, "→ で次の文節");
        k.Type(" ");
        Assert.Equal("日本語を|かく", string.Join("|", k.Host.View.Clauses!), "Space で選択中の文節だけ次の候補");
        k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View.SelectedClause, "← で前の文節");
        k.Type("\n");
        Assert.Equal("日本語をかく", k.Host.Output.Single());
    }

    [Test]
    public static void ConversionStartsAtTheFirstOrLastClause()
    {
        foreach (var last in new[] { false, true })
            foreach (var key in new[] { "space", "shift-space", "tab", "convert", "down", "left", "right" })
            {
                var converter = new SplitConverter(new Dictionary<string, ConversionClause[]>
                {
                    ["きょうはてんきがいいから"] = [new("きょうは", "今日は"), new("てんきが", "天気が"), new("いいから", "いいから")],
                });
                var k = new Keyboard(converter: converter, tabConversion: true) { ConversionStartsAtLastClause = last };
                k.Type("kyouhatenkigaiikara");
                if (key == "shift-space") k.Key(VirtualKeys.LShift);
                k.Press(key switch
                {
                    "tab" => VirtualKeys.Tab,
                    "convert" => VirtualKeys.Convert,
                    "down" => VirtualKeys.Down,
                    "left" => VirtualKeys.Left,
                    "right" => VirtualKeys.Right,
                    _ => VirtualKeys.Space,
                });
                if (key == "shift-space") k.Key(VirtualKeys.LShift, up: true);
                var view = k.Host.View!;
                Assert.Equal("今日は|天気が|いいから", string.Join("|", view.Clauses!), $"{key} {last}");
                var expected = key switch { "left" => 2, "right" => 0, _ => last ? 2 : 0 };
                Assert.Equal(expected, view.SelectedClause, $"{key} {last}");
            }
    }

    [Test]
    public static void Arrows_BeforeSpace_EnterClauseSelection()
    {
        // 報告: 矢印キーで文節を選ぼうとすると確定してしまう → 変換前でも矢印で文節の選択に入る。
        var k = new Keyboard();
        k.Type("tanniwotoru");
        k.Press(VirtualKeys.Left);
        Assert.True(k.Host.View!.Converting, "← で文節の選択に入る (確定しない)");
        Assert.Equal(0, k.Host.Output.Count);
        Assert.Equal(1, k.Host.View.SelectedClause, "← なら最後の文節から");
        k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View.SelectedClause);
        k.Type(" ");
        k.Type("\n");
        Assert.Equal("たんいを取る", k.Host.Output.Single(), "選んだ文節だけ候補が変わる");
        var right = new Keyboard();
        right.Type("tanniwotoru");
        right.Press(VirtualKeys.Right);
        Assert.True(right.Host.View!.Converting, "→ でも変換を始める");
        Assert.Equal(0, right.Host.View.SelectedClause, "→ なら先頭の文節から");

        foreach (var key in new[] { VirtualKeys.Up, VirtualKeys.Down })
        {
            var arrow = new Keyboard();
            arrow.Type("tanniwotoru");
            arrow.Press(key);
            Assert.True(arrow.Host.View?.Converting == true, $"矢印キーで変換: {key}");
            Assert.Equal(2, arrow.Host.View!.Clauses!.Count, $"文節数: {key}");
            Assert.Equal("取る", arrow.Host.View.Clauses[1], $"後ろの文節は変えない: {key}");
            Assert.Equal(0, arrow.Host.View.SelectedClause, $"先頭の文節を選ぶ: {key}");
            if (key == VirtualKeys.Up)
            {
                Assert.True(arrow.Host.View.Candidates.Count > 1, "前の候補がある");
                Assert.Equal(arrow.Host.View.Candidates.Count - 1, arrow.Host.View.SelectedIndex, "↑ は先頭の文節の前の候補を選ぶ");
            }
            else
                Assert.Equal("単位を", arrow.Host.View.Clauses[0], "↓ は最初の候補を選ぶ");
        }
    }

    [Test]
    public static void Candidates_IncludeHomophonesFromDictionary()
    {
        // 報告: とうてん が 当店 しか出ない。
        var k = new Keyboard();
        k.Type("toutenn ");
        Assert.True(k.Host.View!.Candidates.Contains("読点"), string.Join(",", k.Host.View.Candidates));
        k = new Keyboard();
        k.Type("hashiwo ");
        Assert.True(k.Host.View!.Candidates.Contains("箸を") && k.Host.View.Candidates.Contains("端を"), "助詞付きの文節でも同音異義語を出す: " + string.Join(",", k.Host.View.Candidates));
        var extra = CandidateDictionary.Load(null);
        Assert.True(extra.Lookup("おんげー").Contains("音ゲー"), "おんげー → 音ゲー");
        Assert.True(extra.Lookup("りあとも").Contains("リア友"), "りあとも → リア友");
        Assert.True(extra.Lookup("すこんぶ").Contains("すこん部"), "すこんぶ → すこん部");
        Assert.True(extra.Lookup("かちで").Contains("ガチで"), "かちで → ガチで");
        Assert.True(extra.Lookup("いんゆめ").Contains("淫夢"), "いんゆめ → 淫夢");
        Assert.True(extra.Lookup("いん").Contains("淫"), "文節が分かれた いん + ゆめ でも 淫夢 にできる");
        Assert.True(extra.Lookup("おとこのこ").Contains("男の娘"), "おとこのこ → 男の娘");
        Assert.True(extra.Lookup("しょたこん").Contains("ショタコン"), "しょたこん → ショタコン");
    }

    [Test]
    public static void CandidateDictionary_MergedClausesKeepZenzaiAttribution()
    {
        var candidates = new CandidateDictionary();
        candidates.Add("つるまきまき", ["弦巻マキ"]);
        foreach (var zenzai in new[] { true, false })
        {
            var converter = new SplitConverter(new()
            {
                ["つるまきまき"] = [new("つるまき", "ツルマキ", zenzai), new("まき", "マキ", zenzai)],
            });
            var session = new TestSession(() => new Settings { LiveConversion = false }, converter,
                new CompositionServices { Candidates = candidates });
            var view = SessionFacadeTests.Type(session, "tsurumakimaki ")[^1].View!;

            Assert.Equal("ツルマキマキ", view.Candidates[view.SelectedIndex]);
            Assert.Equal(zenzai ? "Zenzai" : null, view.ContextHint);
            Assert.Equal(zenzai ? CandidateNote.Zenzai : CandidateNote.None, view.Notes?[view.SelectedIndex] ?? CandidateNote.None);

            var dictionaryIndex = view.Candidates.ToList().IndexOf("弦巻マキ");
            Assert.True(dictionaryIndex >= 0, "補助辞書の候補が出る");
            Assert.True(view.Notes?[dictionaryIndex] is not CandidateNote.Zenzai, "補助辞書の候補に Zenzai の注記は付けない");
        }
    }

    [Test]
    public static void CorpusContextRules_PreferChatFormsWhenCued()
    {
        var rules = ContextRules.Load(null);
        Assert.Equal("ガチで", ChooseKana(rules, "かちで", "終わってる")!);
        Assert.Equal("垢", ChooseKana(rules, "あか", "Twitterのアカウント")!);
        Assert.Equal("鯖", ChooseKana(rules, "さば", "Discordコミュ")!);
        Assert.Equal("めるちゃん", ChooseKana(rules, "めるちゃん", "先輩")!);
        Assert.Equal("ガチで", ChooseKana(rules, "かちで", "オンゲーしかしてないから知らん")!);
        Assert.Equal("音ゲーしか", ChooseKana(rules, "おんげーしか", "ACの曲を漁ろう")!);
        Assert.Equal("淫夢", ChooseKana(rules, "いんゆめ", "R18画像")!);
        Assert.Equal("すこん部", ChooseKana(rules, "すこんぶ", "鯖のオーナー")!);
        Assert.Equal("音ゲーしか", ChooseKana(rules, "おんげーしか", "してないから")!);
        Assert.Equal("淫", ChooseKana(rules, "いん", "夢のr－18画像")!);
        Assert.Equal("鯖", ChooseKana(rules, "さば", "すこん部のオーナ人")!);
        Assert.Equal("え", ChooseKana(rules, "え", "ほんと")!);
        Assert.Equal("うちの", ChooseKana(rules, "うちの", "家族は")!);
        Assert.Equal("ねむ", ChooseKana(rules, "ねむ", "先輩に聞いて")!);
        Assert.Equal("いま", ChooseKana(rules, "いま", "やるなら")!);
        Assert.Equal("垢", ChooseKana(rules, "あか", "ログインできなくなって")!);
        Assert.Equal("なに", ChooseKana(rules, "なに", "って")!);
        Assert.Equal("めるちゃん", ChooseKana(rules, "めるちゃん", "後輩")!);
    }

    [Test]
    public static void QuestionParticle_KaStaysKana()
    {
        var k = new Keyboard();
        k.Type("hiruka ");
        Assert.Equal("昼|か", string.Join("|", k.Host.View!.Clauses!));
    }

    [Test]
    public static void NAndSmallKanaSpellings()
    {
        var cases = new Dictionary<string, string>
        {
            ["kaxnji"] = "かんじ", ["kannji"] = "かんじ", ["kanji"] = "かんじ", // ん: n / xn / nn
            ["whotto"] = "うぉっと",
            ["ulo"] = "うぉ",
            ["uxo"] = "うぉ",
            ["xtu"] = "っ", ["ltsu"] = "っ", ["vu"] = "ゔ", ["thi"] = "てぃ",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
    }

    [Test]
    public static void ReadableEnglishWords_DoNotSwallowJapanese()
    {
        // 英単語と同じ綴りの日本語 (mite = 見て、kite = 来て) が、英単語の辞書・よく使う英単語の一覧の語より日本語として読まれる
        var cases = new Dictionary<string, string>
        {
            ["mitemite"] = "みてみて",
            ["mitekudasai"] = "みてください",
            ["kitekudasai"] = "きてください",
            ["kitemo"] = "きても",
            ["kitekara"] = "きてから",
            ["mitemiru"] = "みてみる",
            ["kitekure"] = "きてくれ",
            ["here"] = "here",
            ["hero"] = "hero",
            ["I fly a kite"] = "I fly a kite",
            // て の縮まった ちゃ (kit + や にしない)。英単語の後ろの や は英語のまま
            ["kitya"] = "きちゃ",
            ["kityatta"] = "きちゃった",
            ["mityatta"] = "みちゃった",
            ["catya"] = "catや",
            ["robotya"] = "robotや",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, $"「{typed}」");
        }
    }

    [Test]
    public static void EnglishWord_StaysBeforeWordStartingWithParticleKana()
    {
        // カタカナ語の読みもある英単語 (java = じゃゔぁ) の後ろで、助詞と同じかなで始まる語 (のこ・でかく) が続いても、英単語のまま
        // 2 文字の英単語 (so・ma・in・an) の後ろは日本語のまま
        foreach (var (typed, expected) in new[]
        {
            ("javanoko", "javaのこ"), ("javadekaku", "javaでかく"), ("pythonnokoto", "pythonのこと"),
            ("sodesuka", "そですか"), ("manonaka", "まのなか"), ("inochi", "いのち"), ("anone", "あのね"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, $"「{typed}」");
        }
        // 打っている途中に javaの から じゃゔぁのこ に変わらない
        var typing = new Keyboard();
        typing.Type("javano");
        Assert.Equal("javaの", typing.Showing);
        typing.Type("ko");
        Assert.Equal("javaのこ", typing.Showing);
    }

    [Test]
    public static void Context_FromTextBeforeCaret()
    {
        // 直前に確定済みの文字 (入力欄から読む) が英語なら英語、日本語なら日本語。
        var k = new Keyboard();
        k.Host.PrecedingText = "I love ";
        k.Type("sushi");
        Assert.Equal("sushi", k.Showing);

        k = new Keyboard();
        k.Host.PrecedingText = "今日は";
        k.Type("sushi");
        Assert.Equal("すし", k.Showing);

        k = new Keyboard();
        k.Host.PrecedingText = "hello";
        k.Type(",");
        Assert.Equal(",", k.Showing, "英文の続きのカンマは半角");

        k = new Keyboard();
        k.Host.PrecedingText = "今日は";
        k.Type(",");
        Assert.Equal("、", k.Showing, "日本語の続きなら読点");
    }

    [Test]
    public static void Comma_AtEnd_DoesNotBreakFollowingInput()
    {
        // 報告: 読点を最後に打つと英数に固定される。
        var k = new Keyboard();
        k.Type("kyouha,\n");
        k.Type("konnnichiha\n");
        Assert.Equal("きょうは、|こんにちは", string.Join("|", k.Host.Output));
    }

    [Test]
    public static void DirectMode_RomajiSwitchesBackToJapanese()
    {
        var k = new Keyboard(direct: true);
        k.Type("konnnichiha");
        Assert.True(!k.Direct, "ローマ字だと分かったら日本語入力に戻る");
        Assert.Equal("こんにちは", k.Showing, "判定中に打った英字も変換ボックスに入る");
        Assert.Equal(0, k.Host.Output.Count);
        AssertSentThenErased(k.Host.Events);
    }

    [Test]
    public static void DirectMode_LongVowelDashSwitchesToJapanese()
    {
        // 英数状態で ro-maji と打つと、- の時点で英語として出してしまっていた (ローマ字 が打てない)。
        var k = new Keyboard(direct: true);
        k.Type("ro-maji");
        Assert.True(!k.Direct, "母音の後の - (長音) で日本語に戻る");
        Assert.Equal("ろーまじ", k.Showing);
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:", StringComparison.Ordinal)), "- の前の ro だけ送っていた (- は送らない)");
        AssertSentThenErased(k.Host.Events);

        // 英語の接頭辞 (e-) の後は - の後ろで決める: e-mail・co-op は英語、e-me-ru は日本語
        foreach (var word in new[] { "e-mail ", "co-op ", "re-do " })
        {
            k = new Keyboard(direct: true);
            k.Type(word);
            Assert.True(k.Direct, word + "は英数のまま");
        }
        // 2 文字の英単語 (up) でも、続きがローマ字なら日本語 (upa- → うぱー)。打ち終われば英語のまま。
        k = new Keyboard(direct: true);
        k.Type("upa-");
        Assert.Equal("うぱー", k.Showing ?? "(なし)");
        k = new Keyboard(direct: true);
        k.Type("up ");
        Assert.True(k.Direct, "up + Space は英数のまま");
        k = new Keyboard(direct: true);
        k.Type("e-me-ru");
        Assert.True(!k.Direct, "e-me-ru は日本語に戻る");
        Assert.Equal("えーめーる", k.Showing);
    }

    [Test]
    public static void HyphenatedEnglishWords_StayEnglish()
    {
        // えーmail、こーおp になっていた
        AssertCommitted(
            ("e-mail", "e-mail"), ("co-op", "co-op"), ("bio-break", "bio-break"), ("e-maildeokuru", "e-mailでおくる"), ("x-ray", "x-ray"), ("lo-fi", "lo-fi"), ("Lo-fi", "Lo-fi"), ("sci-fi", "sci-fi"), ("lo-fiwokiku", "lo-fiをきく"), ("r-18", "r-18"), ("sub-6", "sub-6"), ("GPT-6.7", "GPT-6.7"), ("Blu-ray", "Blu-ray"),
            ("e-to", "えーと"), ("su-pa-", "すーぱー"), ("o-bun", "おーぶん"));
    }

    [Test]
    public static void Loanwords_OfferLatinSpelling()
    {
        // リナックス を変換しても Linux が出なかった
        var dictionary = CandidateDictionary.Load(null);
        Assert.True(dictionary.Lookup("りなっくす").Contains("Linux"), "リナックス → Linux");
        Assert.True(dictionary.Lookup("りなっくすで").Contains("Linuxで"), "助詞が付いても");
        Assert.True(dictionary.Lookup("じゃばすくりぷと").Contains("JavaScript"), "ジャバスクリプト → JavaScript");
        // JMdict に無い社名 (brands.txt)
        Assert.True(dictionary.Lookup("しゃおみ").Contains("Xiaomi"), "しゃおみ → Xiaomi");
        Assert.True(dictionary.Lookup("でぃすこーど").Contains("Discord"), "ディスコード → Discord");
    }

    [Test]
    public static void Issue1650_DomainDictionariesOfferTerms()
    {
        // #1650: 分野の辞書 (医療・情報・駅名・固有名詞・人名) の語を候補に出す
        var dictionary = CandidateDictionary.Load(null);
        Assert.True(dictionary.LookupDomain("せんたんきょだいしょう").Contains("先端巨大症"), "医療 (JMED-DICT)");
        Assert.True(dictionary.LookupDomain("いくせいしゃけん").Contains("育成者権"), "情報 (IDiME)");
        Assert.True(dictionary.LookupDomain("あいづかのうえき").Contains("会津加納駅"), "駅名 (SKK)");
        Assert.True(dictionary.LookupDomain("せんたんきょだいしょうの").Contains("先端巨大症の"), "助詞が付いても");
        Assert.True(dictionary.LookupDomain("どくにんせい").Contains("独任制"), "一般の語 (SudachiDict)");
        Assert.True(dictionary.LookupDomain("ちゅうえつぱるぷ").Contains("中越パルプ"), "固有名詞 (SudachiDict)");
        Assert.True(dictionary.LookupDomain("あまみぐんとうしんこうかいはつとくべつそちほう").Contains("奄美群島振興開発特別措置法"), "法令名 (e-Gov)");
        // 法令名: 題名の大きい ツ (マツサージ) は、ふだん打つ読み (まっさーじ) でも引ける。読みが改題前の題名のままの法律は入れない
        Assert.True(dictionary.LookupDomain("あんままっさーじしあつしはりしきゆうしとうにかんするほうりつ").Count > 0, "あんままっさーじ…");
        Assert.True(dictionary.LookupDomain("おきなわけんにおけるちゅうりゅうぐんようちのへんかんにともなうとくべつそちにかんするほうりつ").Count == 0, "改題前の読み");
        // 異表記 (洗ひざらし) は入れない
        Assert.True(!dictionary.LookupDomain("あらいざらし").Contains("洗ひざらし"), "異表記");
        // 逆向き: よく使う語の読みには分野の語を足さない (きょう → 京・恭 が ひらがな より前に出ていた)
        Assert.Equal(0, dictionary.LookupDomain("きょう").Count);
        Assert.Equal(0, dictionary.LookupDomain("あい").Count);
    }

    [Test]
    public static void Issue1650_DomainTermsComeAfterEverydayCandidates()
    {
        // 分野の語は、使わない人には要らない候補なので、ふだんの候補 (変換エンジンの答え・ひらがな・カタカナ) を後ろに押さない
        var k = new Keyboard();
        k.Type("sentankyodaishou ");
        var shown = k.Host.View!.Candidates.ToList();
        Assert.Equal("せんたんきょだいしょう,センタンキョダイショウ,ｾﾝﾀﾝｷｮﾀﾞｲｼｮｳ,先端巨大症", string.Join(",", shown.Take(4)), string.Join(" ", shown));
    }

    [Test]
    public static void Issue409_TanomeOffersBirthdaySlang()
    {
        var dictionary = CandidateDictionary.Load(null);
        Assert.True(dictionary.Lookup("たんおめ").Contains("誕おめ"), "たんおめ → 誕おめ");
    }

    /// <summary>報告された誤判定 (Issue の番号) と、同じ原因で崩れやすい入力。</summary>
    [Test]
    public static void ReportedInputs_AreCommittedAsExpected() => AssertCommitted(
        ("Gemma4", "Gemma4"), // #404
        ("holoX", "holoX"), // #469
        ("fusion", "fusion"), // #346
        ("enumerate", "enumerate"), // #506
        ("fatigue", "fatigue"), // #536
        ("infobesity", "infobesity"), // #538
        ("whois", "whois"), // #616
        ("siri-zu", "しりーず"), // #656
        ("imaopenshita", "いまopenした"), // #669
        ("fyu", "ふゅ"), // #674
        ("guesstimation", "guesstimation"), // #683
        ("enshittification", "enshittification"), // #686
        ("Finfluencer", "Finfluencer"), // #689
        ("datamation", "datamation"), // #690
        ("adware", "adware"), // #691
        ("greedflation", "greedflation"), // #692
        ("englishtojapanesewomazeteuteru", "englishとjapaneseをまぜてうてる"), // #712
        ("binge-watch", "binge-watch"), // #724
        ("openstreetmap", "openstreetmap"), // #725
        ("malvertising", "malvertising"), // #726
        ("ad-blocker", "ad-blocker"), // #727
        ("adtech", "adtech"), // #730
        ("robonomics", "robonomics"), // #731
        ("mobisode", "mobisode"), // #732
        ("netizenry", "netizenry"), // #733
        ("tver", "tver"), // #735
        ("changesuru", "changeする"), // #411
        ("imaopenno", "いまopenの"), // #485
        ("mozcgamiteirutohaie", "mozcがみているとはいえ"), // #249
        ("DeVitode", "DeVitoで"), // #268
        ("kachiwodefinesuru", "かちをdefineする"), // #421
        ("mondaiwodefusesuru", "もんだいをdefuseする"), // #419
        ("Blu-ray", "Blu-ray"), // #435
        ("kameramotteschoolheiku", "かめらもってschoolへいく"), // 後ろの長い英単語が短い英単語に勝つ (motteschoolhe が mottes + ちょおl + へ になっていた)
        ("abctodef", "abcとdef")); // #154 英字のあいだの と は助詞 (全体を 1 つの英単語にしない)

    // Markdown の記号を確定したあとに本文を続けて打てる
    [Test]
    public static void MarkdownMarker_ThenContent()
    {
        var k = new Keyboard();
        k.Type("# hello ");
        Assert.Equal("# hello ", k.Host.Document);
    }

    [Test]
    public static void MixiStaysEnglish()
    {
        // みぃ (mi + xi)
        var k = new Keyboard();
        k.Type("mixi\n");
        Assert.Equal("mixi", k.Host.Document);

        k = new Keyboard();
        k.Type("mixi2\n");
        Assert.Equal("mixi2", k.Host.Document);

        k = new Keyboard();
        k.Type("mixidetomodachi\n");
        Assert.Equal("mixiでともだち", k.Host.Document);
    }

    [Test]
    public static void DictionaryAdditions_ReportedWordsStayEnglish()
    {
        // #1541・#1637・#1530・#1638: IME や OS の名前がかなに割れる
        // #1615: 国名 / #1651: アーティスト名
        foreach (var word in new[] { "fcitx5", "iGPU", "iCloud", "AsahiLinux", "azooKey", "hazkey", "mozkey", "Benin", "Panama", "Tonga", "Madagascar", "picco", "nqrse",
            // #1650: CLDR の国・地域名から作った countries.txt
            // Congo - Kinshasa は、フロントを通すと Kinしゃさ になる
            "Bermuda", "Puerto Rico", "Central African Republic", "Clipperton Island", "Palestinian Territories" })
        {
            var k = new Keyboard();
            k.Type(word + "\n");
            Assert.Equal(word, k.Host.Document);
        }

        // #1607・#1557・#1556・#1487・#1625・#1389: 英字のまま打つ語
        foreach (var word in new[] { "colab", "qwen", "quickshare", "alma", "undef", "ifndef", "elifndef", "include", "pragma", "inotify" })
        {
            var k = new Keyboard();
            k.Type(word + "\n");
            Assert.Equal(word, k.Host.Document);
        }

        // 逆向き: 語の後ろに続く日本語は日本語のまま
        var follow = new Keyboard();
        follow.Type("fcitx5no\n");
        Assert.Equal("fcitx5の", follow.Host.Document);

        follow = new Keyboard();
        follow.Type("iGPUde\n");
        Assert.Equal("iGPUで", follow.Host.Document);

        // 逆向き (#1650): 国名の語に似たローマ字 (San・Jan・Rica・Togo・Oman) の日本語は日本語のまま。ローマ字として読める国名の語 (Oman) と小文字の語 (da) は国名の語に入れない
        foreach (var (typed, expected) in new[] { ("tanakasan", "たなかさん"), ("iijan", "いいじゃん"), ("rikanojugyou", "りかのじゅぎょう"), ("iranai", "いらない"), ("togo", "とご"), ("sorehada", "それはだ"), ("omanjuutabeta", "おまんじゅうたべた"), ("sannindekita", "さんにんできた"), ("janakatta", "じゃなかった") })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.True(k.Host.Document.All(c => c >= 0x3000), $"{typed} → {k.Host.Document} (期待: {expected} のような日本語)");
        }
    }

    [Test]
    public static void DictionaryAdditions2_ReportedWords()
    {
        // #1609: 固有名詞がかなに割れる
        var k = new Keyboard();
        k.Type("Kaggle\n");
        Assert.Equal("Kaggle", k.Host.Document);

        k = new Keyboard();
        k.Type("kaggle\n");
        Assert.Equal("kaggle", k.Host.Document);

        // 逆向き: 語の後ろに続く日本語は日本語のまま
        k = new Keyboard();
        k.Type("kaggleno\n");
        Assert.Equal("kaggleの", k.Host.Document);

        // #1390: stat の後ろに続く日本語は日本語のまま
        foreach (var word in new[] { "stat", "stats" })
        {
            var e = new Keyboard();
            e.Type(word + "\n");
            Assert.Equal(word, e.Host.Document);
        }

        k = new Keyboard();
        k.Type("statsuruno\n");
        Assert.Equal("statするの", k.Host.Document);

        // #1652・#1614・#1653・#1442: 読みが無くて英日に割れる日本語
        foreach (var (typed, expected) in new[]
        {
            ("pien", "ぴえん"),
            ("onnatsu", "おんあつ"),
            ("guguru", "ぐぐる"),
            ("himoduke", "ひもづけ"),
        })
        {
            var j = new Keyboard();
            j.Type(typed + "\n");
            Assert.Equal(expected, j.Host.Document);
        }

        // 逆向き・似た入力: 追加した読みの前後で既存の判定が変わらない
        foreach (var (typed, expected) in new[]
        {
            ("korosuzobot", "ころすぞbot"),
            ("bot", "bot"),
            ("guru", "guru"),
            ("sinbun", "しんぶん"),
        })
        {
            var r = new Keyboard();
            r.Type(typed + "\n");
            Assert.Equal(expected, r.Host.Document);
        }
    }

    [Test]
    public static void DictionaryAdditions3_ReportedWords()
    {
        // #1696・#499: 固有名詞がかなに割れる
        foreach (var (typed, expected) in new[]
        {
            ("pixiv", "pixiv"),
            ("sbisinnseiginnkou", "sbiしんせいぎんこう"),
        })
        {
            var e = new Keyboard();
            e.Type(typed + "\n");
            Assert.Equal(expected, e.Host.Document);
        }

        // 固有名詞の後ろに続く日本語は日本語のまま
        foreach (var (typed, expected) in new[]
        {
            ("pixivno", "pixivの"),
            ("sbini", "sbiに"),
        })
        {
            var e = new Keyboard();
            e.Type(typed + "\n");
            Assert.Equal(expected, e.Host.Document);
        }

        // #1456・#1491: 読みが無くて英日に割れる日本語 (確定はかな。変換は変換エンジンに任せる)
        foreach (var (typed, expected) in new[]
        {
            ("supika", "すぴか"),
            ("kennpa", "けんぱ"),
        })
        {
            var j = new Keyboard();
            j.Type(typed + "\n");
            Assert.Equal(expected, j.Host.Document);
        }

        // #365 の語句は BuiltInPhrases_AreSplitOut で確かめる
        // (語句の表面は変換エンジンが出すので、打鍵の確定では読みのままになる)

        // 副作用の開示: kenpachi は main では けんpachi と確定していたものが、
        // けんぱ の追加で けんぱち に変わる (元々壊れていた入力。PR 本文に記載)
        foreach (var (typed, expected) in new[]
        {
            ("kenpachi", "けんぱち"),
        })
        {
            var r = new Keyboard();
            r.Type(typed + "\n");
            Assert.Equal(expected, r.Host.Document);
        }
    }

    [Test]
    public static void DictionaryAdditions_CandidatesAndBrandNames()
    {
        // #1445・#1651・#1924: 変換の候補に出す語
        var candidates = CandidateDictionary.Load(null);
        Assert.True(candidates.Lookup("おねしょた").Contains("おねショタ"), "#1445 おねしょた");
        Assert.True(candidates.Lookup("ちゅうに").Contains("厨二"), "#1651 ちゅうに");
        Assert.True(candidates.Lookup("きめつ").Contains("鬼滅"), "#1651 きめつ");
        Assert.True(candidates.Lookup("おしのこ").Contains("推しの子"), "#1651 おしのこ");
        Assert.True(candidates.Lookup("じゃくしゃだんせい").Contains("ふにゃオス"), "#1924 じゃくしゃだんせい");
        Assert.True(candidates.Lookup("ふにゃおす").Contains("弱者男性"), "#1924 ふにゃおす");

        // #1548・#1490・#1460: 読みで英字の名前を出す
        Assert.True(Predictor.PredictEnglishNames("ぜんざい").Contains("Zenzai"), "#1548 ぜんざい");
        Assert.True(Predictor.PredictEnglishNames("あずーきー").Contains("azooKey"), "#1548 あずーきー");
        Assert.True(Predictor.PredictEnglishNames("もずきー").Contains("Mozkey"), "#1548 もずきー");
        Assert.True(Predictor.PredictEnglishNames("えくすぷろーらー").Contains("explorer"), "#1490 えくすぷろーらー");
        Assert.True(Predictor.PredictEnglishNames("さいき").Contains("PSYQUI"), "#1460 さいき");
    }

    [Test]
    public static void Issue537_ProcrasticleStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("procrasticle");
        Assert.Equal("procrasticle", k.Showing);
        k.Type("\n");
        Assert.Equal("procrasticle", k.Host.Document);
    }

    [Test]
    public static void Issue1132_ShortEnglishThenJapanese()
    {
        // 短い英単語の後ろが読める日本語でも、#537 の補正で全文英字に戻っていた。
        foreach (var live in new[] { false, true })
        {
            foreach (var (typed, expected) in new[]
            {
                ("usbtunaide", "usbつないで"), ("usbtsunaide", "usbつないで"),
                ("USBtunaide", "USBつないで"), ("pctunaide", "pcつないで"),
                ("usbsuru", "usbする"), ("usbtsukatte", "usbつかって"),
                ("usbnuite", "usbぬいて"), ("usbwotunaide", "usbをつないで"),
                ("hdmitunaide", "hdmiつないで"),
            })
            {
                var message = $"{typed} (ライブ変換: {live})";
                var k = new Keyboard(live: live);
                k.Type(typed);
                Assert.Equal(expected, k.Showing, message + " 表示");
                k.Type("\n");
                Assert.Equal(expected, k.Host.Document, message + " Enter");

                k = new Keyboard(live: live);
                k.Type(typed + " ");
                Assert.True(k.Host.View?.Converting == true, message + " Space で変換を始める");
                k.Type("\n");
                Assert.Equal(expected, k.Host.Document, message + " Space → Enter");
            }
        }
    }

    [Test]
    public static void Issue1132_ShortEnglishThenPendingJapanese()
    {
        foreach (var live in new[] { false, true })
        {
            foreach (var (typed, showing, committed) in new[]
            {
                ("usbtun", "usbつん", "usbつん"),
                ("usbtsukatt", "usbつかっt", "usbつかっt"),
            })
            {
                var message = $"{typed} (ライブ変換: {live})";
                var k = new Keyboard(live: live);
                k.Type(typed);
                Assert.Equal(showing, k.Showing, message + " 入力途中");
                k.Type("\n");
                Assert.Equal(committed, k.Host.Document, message + " Enter");

                k = new Keyboard(live: live);
                k.Type(typed + " ");
                if (committed.All(char.IsAsciiLetter))
                {
                    Assert.True(k.Host.View?.Converting != true, message + " Space で英字を確定する");
                    Assert.Equal(committed + " ", k.Host.Document, message + " Space");
                }
                else
                {
                    Assert.True(k.Host.View?.Converting == true, message + " Space で日本語を変換する");
                    k.Type("\n");
                    Assert.Equal(committed, k.Host.Document, message + " Space → Enter");
                }
            }
        }
    }

    [Test]
    public static void Issue1132_KnownLimitation_ReadableEnglishAfterShortPrefix()
    {
        // 既知の問題: 本来は pronate のまま英字にしたいが、pro + なて と読めるため日本語との区別がつかない。
        var k = new Keyboard();
        k.Type("pronate");
        Assert.Equal("proなて", k.Showing, "既知の問題の現在の表示");
        k.Type("\n");
        Assert.Equal("proなて", k.Host.Document, "本来の期待は pronate。判定を改善したときに更新する");
    }

    [Test]
    public static void Issue505_LiveConvertIgnoresSymbolsInLength()
    {
        var k = new Keyboard(live: true);
        k.Type("na~~~\n");
        Assert.Equal("な～～～", k.Host.Document);
    }

    [Test]
    public static void Issue653_ShotaOffersKatakana()
    {
        var dictionary = CandidateDictionary.Load(null);
        Assert.True(dictionary.Lookup("しょた").Contains("ショタ"), "しょた → ショタ");
    }

    [Test]
    public static void Issue654_LatticeOffersKatakana()
    {
        var dictionary = CandidateDictionary.Load(null);
        Assert.True(dictionary.Lookup("らてぃす").Contains("ラティス"), "らてぃす → ラティス");
    }

    [Test]
    public static void Issue706_AonishikiOffersName()
    {
        var dictionary = CandidateDictionary.Load(null);
        Assert.True(dictionary.Lookup("あおにしき").Contains("安青錦"), "あおにしき → 安青錦");
    }

    [Test]
    public static void Issue710_UserDictionaryAcceptsOneCharacterReading()
    {
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Add("お", "お父さん") is null, "1 文字の読みも登録できる");
        Assert.True(dictionary.Add("", "記") is not null, "空の読みは登録できない");
        Assert.Equal("お父さん", dictionary.EngineWords().Words.Single(w => w.Reading == "お").Word, "Mozc に渡す");
    }

    [Test]
    public static void Issue708_ShiftSpaceCanBeHalfWidth()
    {
        var k = new Keyboard { SpaceWidth = Meltype.Config.SpaceWidth.HalfWidth };
        k.Type("tanaka\n");
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
        k.Type("tarou\n");
        Assert.Equal("たなか たろう", k.Host.Document);
    }

    [Test]
    public static void Issue1980_TypoFixIsShownWhileTyping()
    {
        // 打ち間違いは、Enter で直すのと同じ形で打っている途中から見せる (見えていたものと違う文字が入っていた: #677・#1980)
        foreach (var (typed, expected) in new[] { ("onegaishimsu", "おねがいします"), ("sumimasne", "すみません"), ("ikimahsou", "いきましょう") })
        {
            var k = new Keyboard();
            k.Type(typed);
            var front = SessionResult.ForFront(k.Host.View!, showGuide: true, numberKeysSelect: true).Text;
            k.Type("\n");
            Assert.Equal(expected, k.Host.Document, typed);
            Assert.Equal(k.Host.Document, front, $"見えていたとおりに確定 ({typed})");
        }
        // 打ち間違いの無い入力・英単語は、今までどおり直さずに見せる
        foreach (var (typed, expected) in new[] { ("kantan", "かんたん"), ("githubnipushsuru", "GitHubにpushする") })
        {
            var k = new Keyboard();
            k.Type(typed);
            var front = SessionResult.ForFront(k.Host.View!, showGuide: true, numberKeysSelect: true).Text;
            k.Type("\n");
            Assert.Equal(expected, k.Host.Document, typed);
            Assert.Equal(k.Host.Document, front, $"見えていたとおりに確定 ({typed})");
        }
        // 英語と判定して英字で見せている所は、直さずに見えているとおりに確定する (inclu が いんこう で入っていた)
        foreach (var (typed, expected) in new[] { ("inclu", "inclu"), ("obsidi", "obsidi"), ("mainnipushsu", "mainにpushす") })
        {
            var k = new Keyboard();
            k.Type(typed);
            var front = SessionResult.ForFront(k.Host.View!, showGuide: true, numberKeysSelect: true).Text;
            k.Type("\n");
            Assert.Equal(expected, k.Host.Document, typed);
            Assert.Equal(k.Host.Document, front, $"見えていたとおりに確定 ({typed})");
        }
        // 打っている音の途中 (kyouh の h) は直さずに見せる (打った子音が表示から消えない)
        var typing = new Keyboard();
        typing.Type("kyouh");
        Assert.Equal("きょうh", SessionResult.ForFront(typing.Host.View!, showGuide: true, numberKeysSelect: true).Text, "打っている音の途中");
    }

    [Test]
    public static void Issue515_F10FirstPressCanStayAsTyped()
    {
        var k = new Keyboard { AlphanumericKeyStartsAsTyped = true };
        k.Type("hello");
        k.Press(VirtualKeys.F10);
        Assert.Equal("hello", k.Showing, "1 回目は打ったまま");
        k.Press(VirtualKeys.F10);
        Assert.Equal("HELLO", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("Hello", k.Showing);
        var kana = new Keyboard { AlphanumericKeyStartsAsTyped = true };
        kana.Type("hoge");
        kana.Press(VirtualKeys.F6);
        kana.Press(VirtualKeys.F10);
        Assert.Equal("hoge", kana.Showing, "かなに見えているときも 1 回目は打ったまま");
    }

    [Test]
    public static void Issue443_ParenthesesCanBeFullWidth()
    {
        var k = new Keyboard { FullWidthParentheses = true };
        k.Type("kyouha(nihongo)\n");
        Assert.Equal("きょうは（にほんご）", k.Host.Document, "日本語の中");
        var after = new Keyboard { FullWidthParentheses = true };
        after.Type("Google(kensaku)\n");
        Assert.Equal("Google（けんさく）", after.Host.Document, "英字の直後でも全角");
        var normal = new Keyboard();
        normal.Type("kyouha(nihongo)\n");
        Assert.Equal("きょうは(にほんご)", normal.Host.Document, "OFF は今までどおり");
    }

    [Test]
    public static void Issue502_SymbolStyleCanBeChosen()
    {
        var k = new Keyboard { SquareBrackets = true, Slash = Meltype.Config.SlashStyle.FullWidth };
        k.Type("[nihongo]aiueo/kakikukeko\n");
        Assert.Equal("［にほんご］あいうえお／かきくけこ", k.Host.Document);
        var normal = new Keyboard();
        normal.Type("[nihongo]\n");
        Assert.Equal("「にほんご」", normal.Host.Document, "OFF は今までどおり");
    }

    [Test]
    public static void EllipsisIsOfferedForTententenReading()
    {
        Assert.True(CandidateDictionary.Load(null).Lookup("てんてんてん").Contains("…"), "てんてんてん → …");
        var k = new Keyboard();
        k.Type("tenntenntenn");
        k.Press(VirtualKeys.Space);
        Assert.True(k.Host.View!.Candidates.Contains("…"), string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void NamedSymbolsAreAvailableAsConversionCandidates()
    {
        foreach (var (raw, reading, symbol) in new[] {
            ("sanntenn", "さんてん", "…"),
            ("sanntennri-da-", "さんてんりーだー", "…"),
            ("sanntennri-da", "さんてんりーだ", "…"),
            ("nitennri-da-", "にてんりーだー", "‥"),
            ("nitennri-da", "にてんりーだ", "‥"),
            ("namidassyu", "なみだっしゅ", "〜"),
            ("natyuraru", "なちゅらる", "♮"),
            ("sessi", "せっし", "℃"),
            ("mugenndai", "むげんだい", "∞"),
            ("batu", "ばつ", "×"),
            ("koronn", "ころん", "："),
            ("semikoronn", "せみころん", "；"),
            ("surassyu", "すらっしゅ", "／"),
            ("bakkusurassyu", "ばっくすらっしゅ", "＼"),
        })
        {
            Assert.True(CandidateDictionary.Load(null).LookupWords(reading).Contains(symbol), reading + " → " + symbol);
            var k = new Keyboard();
            k.Type(raw);
            k.Press(VirtualKeys.Space);
            Assert.True(k.Host.View!.Candidates.Contains(symbol), raw + ": " + string.Join(",", k.Host.View.Candidates));
        }
    }

    [Test]
    public static void SymbolConversionOffersEllipsisWithoutChangingLiteralPunctuation()
    {
        var k = new Keyboard();
        k.Type("...");
        k.Press(VirtualKeys.Space);
        Assert.True(k.Host.View!.Candidates.Contains("…"), string.Join(",", k.Host.View.Candidates));
        Assert.True(k.Host.View.Candidates.Contains("..."), "打ったままのピリオドも残す");
        var literal = new Keyboard();
        literal.Type("...\n");
        Assert.Equal("...", literal.Host.Document);
    }

    [Test]
    public static void Issue495_MoreThanThreeSlashesExtendTheEllipsis()
    {
        var expected = new[] { (3, "…"), (4, "……"), (6, "……"), (7, "………") };
        foreach (var (count, ellipsis) in expected)
        {
            var k = new Keyboard();
            k.Type("naruhodo" + new string('/', count) + "\n");
            Assert.Equal("なるほど" + ellipsis, k.Host.Document, $"/ を {count} 個");
        }
    }

    [Test]
    public static void Issue676_EscStepsBackOneLevelAtATime()
    {
        var k = new Keyboard { EscStepsBack = true };
        k.Type("kyou");
        k.Press(VirtualKeys.F7);
        Assert.Equal("キョウ", k.Showing);
        k.Press(VirtualKeys.Escape);
        Assert.Equal("きょう", k.Showing, "1 回目: ひらがな");
        k.Press(VirtualKeys.Escape);
        Assert.Equal("kyou", k.Showing, "2 回目: 打ったローマ字");
        k.Press(VirtualKeys.Escape);
        Assert.True(string.IsNullOrEmpty(k.Showing), "3 回目: 消える: " + k.Showing);
        // 英字のまま見えている入力 (hello) は、かな (へっぉ) に変えず、そのまま全部消す
        var english = new Keyboard { EscStepsBack = true };
        english.Type("hello");
        Assert.Equal("hello", english.Showing);
        english.Press(VirtualKeys.Escape);
        Assert.True(string.IsNullOrEmpty(english.Showing), "英字は一段戻さず消える: " + english.Showing);
        var normal = new Keyboard();
        normal.Type("kyou");
        normal.Press(VirtualKeys.Escape);
        Assert.True(string.IsNullOrEmpty(normal.Showing), "OFF は今までどおり全部消す");
    }

    [Test]
    public static void Issue670_EscRevertsOnlyTheSelectedClause()
    {
        var k = new Keyboard { EscRevertsSelectedClause = true };
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        Assert.Equal("単位を取る", string.Concat(k.Host.View!.Clauses!), "文節に分かれて変換");
        k.Press(VirtualKeys.Escape);
        Assert.True(k.Host.View!.Converting, "変換は続く");
        Assert.Equal("たんいを取る", string.Concat(k.Host.View!.Clauses!), "選んでいる文節だけひらがなに");
        k.Press(VirtualKeys.Escape);
        Assert.True(!k.Host.View!.Converting, "もうひらがなの文節なら、今までどおり変換をやめる");
    }

    [Test]
    public static void Issue678_FunctionKeysCanAffectOnlyTheSelectedClause()
    {
        var k = new Keyboard { FunctionKeysAffectSelectedClause = true };
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        Assert.Equal("単位を取る", string.Concat(k.Host.View!.Clauses!));
        k.Press(VirtualKeys.F7);
        Assert.True(k.Host.View!.Converting, "変換は続く");
        Assert.Equal("タンイヲ取る", string.Concat(k.Host.View!.Clauses!), "選んでいる文節だけカタカナ");
        k.Press(VirtualKeys.F6);
        Assert.Equal("たんいを取る", string.Concat(k.Host.View!.Clauses!), "F6 でひらがな");
    }

    private static void ShiftArrow(Keyboard k, int vk)
    {
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(vk);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
    }

    [Test]
    public static void Issue678_FunctionKeysStillAffectOnlyTheSelectedClauseAfterExtending()
    {
        // 文節の長さを変えると、その文節の打った英字が分からなくなり、F9 / F10 が文全体に効いていた
        foreach (var (vk, expected) in new[] { (VirtualKeys.F10, "3e4wotuka"), (VirtualKeys.F9, "３ｅ４ｗｏｔｕｋａ") })
        {
            var k = new Keyboard { FunctionKeysAffectSelectedClause = true };
            k.Type("m3e4wotukau ");
            k.Press(VirtualKeys.Right);
            ShiftArrow(k, VirtualKeys.Left);
            ShiftArrow(k, VirtualKeys.Left);
            ShiftArrow(k, VirtualKeys.Right);
            var before = k.Host.View!.Clauses!;
            Assert.Equal("m|3え4をつか|う", string.Join("|", before), "縮めた文節を伸ばす");
            k.Press(vk);
            Assert.True(k.Host.View!.Converting, "変換は続く");
            Assert.Equal($"m|{expected}|う", string.Join("|", k.Host.View!.Clauses!), "選んだ文節だけが英字になる");
        }
    }

    [Test]
    public static void Issue678_FunctionKeysStillAffectOnlyTheSelectedClauseAfterShrinking()
    {
        foreach (var (vk, expected) in new[] { (VirtualKeys.F10, "3e4wotuka"), (VirtualKeys.F9, "３ｅ４ｗｏｔｕｋａ") })
        {
            var k = new Keyboard { FunctionKeysAffectSelectedClause = true };
            k.Type("m3e4wotukau ");
            k.Press(VirtualKeys.Right);
            ShiftArrow(k, VirtualKeys.Left);
            var before = k.Host.View!.Clauses!;
            Assert.Equal("m|3え4をつか|う", string.Join("|", before), "後ろの文節が縮む");
            k.Press(vk);
            Assert.True(k.Host.View!.Converting, "変換は続く");
            Assert.Equal($"m|{expected}|う", string.Join("|", k.Host.View!.Clauses!), "選んだ文節だけが英字になる");
        }
    }

    [Test]
    public static void Issue678_FunctionKeysDoNothingWhenTheTypedLettersOfTheClauseAreUnknown()
    {
        // きゃ を き|ゃ に分けると、打った英字 (kya) を文節に割り当てられない。文全体には効かせない
        var k = new Keyboard { FunctionKeysAffectSelectedClause = true };
        k.Type("kyakuwotoru ");
        k.Press(VirtualKeys.Left);
        for (var i = 0; i < 6 && k.Host.View!.Clauses![0] != "き"; i++) ShiftArrow(k, VirtualKeys.Left);
        var before = k.Host.View!.Clauses!.ToArray();
        Assert.Equal("き", before[0], "先頭の文節が き だけになる");
        k.Press(VirtualKeys.F10);
        Assert.True(k.Host.View!.Converting, "変換は続く");
        Assert.Equal(string.Concat(before), string.Concat(k.Host.View!.Clauses!), "どの文節も変わらない");
    }

    [Test]
    public static void Issue678_FunctionKeysAffectOnlyTheSelectedEnglishClause()
    {
        foreach (var (vk, expected) in new[] { (VirtualKeys.F9, "ｇｏｏｇｌｅ"), (VirtualKeys.F7, "ゴオgェ") })
        {
            var k = new Keyboard { FunctionKeysAffectSelectedClause = true };
            k.Type("nihongowogoogleto ");
            k.Press(VirtualKeys.Right);
            Assert.Equal("にほんごを|google|と", string.Join("|", k.Host.View!.Clauses!), "2 番目の英語の文節を選ぶ");
            k.Press(vk);
            Assert.True(k.Host.View!.Converting, "変換は続く");
            Assert.Equal($"にほんごを|{expected}|と", string.Join("|", k.Host.View!.Clauses!), "選んだ英語の文節だけが変わる");
        }
    }

    [Test]
    public static void Issue599_CtrlKLResizeTheClause()
    {
        var k = new Keyboard { ControlKeys = Meltype.Config.ControlKeyLayout.Atok };
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        Assert.Equal("単位を取る", string.Concat(k.Host.View!.Clauses!));
        k.Key(VirtualKeys.LControl);
        k.Key('K');
        k.Key('K', up: true);
        k.Key(VirtualKeys.LControl, up: true);
        var shrunk = string.Concat(k.Host.View!.Clauses!);
        Assert.True(k.Host.View!.Converting && shrunk != "単位を取る", "Ctrl+K で文節が縮む: " + shrunk);
    }

    [Test]
    public static void Issue493_ShiftEnterCommitsUpToTheSelectedClause()
    {
        var k = new Keyboard { PartialCommit = true };
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        Assert.Equal("単位を取る", string.Concat(k.Host.View!.Clauses!));
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Return);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
        Assert.Equal("単位を", k.Host.Document, "選んでいる文節までを確定");
        Assert.Equal("とる", k.Showing, "残りは未確定のまま");
        k.Type("\n");
        Assert.Equal("単位をとる", k.Host.Document);
    }

    [Test]
    public static void Issue339_JikouOffersSeasonalGreetings()
    {
        var k = new Keyboard(now: () => new DateTime(2026, 10, 9));
        k.Type("jikou ");
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("秋冷の候") && candidates.Contains("拝啓　秋冷の候、"), "10 月の時候の挨拶: " + string.Join(",", candidates));
        var spring = new Keyboard(now: () => new DateTime(2026, 4, 3));
        spring.Type("jikou ");
        Assert.True(spring.Host.View!.Candidates.Contains("陽春の候"), "4 月");
    }

    [Test]
    public static void Issue340_TraditionalUnitsOfferMetricSize()
    {
        Assert.Equal("3尺|約90.91cm", string.Join("|", TraditionalUnitConversion.Candidates("3しゃく")!), "3 尺");
        Assert.Equal("1坪|約3.31㎡", string.Join("|", TraditionalUnitConversion.Candidates("1つぼ")!), "1 坪");
        Assert.Equal("2貫|約7.5kg", string.Join("|", TraditionalUnitConversion.Candidates("2かん")!), "2 貫");
        Assert.True(TraditionalUnitConversion.Candidates("かん") is null, "数字が無ければ対象外");
        var k = new Keyboard();
        k.Type("3syaku ");
        Assert.True(k.Host.View!.Candidates.Contains("3尺") && k.Host.View.Candidates.Contains("約90.91cm"), string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void Issue349_DeleteForgetsTheSelectedPrediction()
    {
        var phrases = new PhraseHistory(null);
        var k = new Keyboard(predictor: new Predictor(phrases, null, null));
        k.Type("kyou ");
        k.Press(VirtualKeys.Return);
        k.Type("kyo");
        Assert.True(k.Host.View!.Predictions?.Contains("今日") == true, "予測の候補に出る");
        k.Press(VirtualKeys.Tab);
        k.Press(0x2E); // Delete
        Assert.True(!phrases.StartingWith("kyo").Contains("今日") && !phrases.StartingWith("きょ").Contains("今日"), "学習から消える");
        Assert.True(k.Host.View!.Predictions?.Contains("今日") != true, "予測の候補から消える");
    }

    [Test]
    public static void Issue643_MacControlKeysConvertLikeKotoeri()
    {
        var k = new Keyboard { ControlKeys = Meltype.Config.ControlKeyLayout.Mac };
        k.Type("toukyou");
        k.Key(VirtualKeys.LControl);
        k.Key('K');
        k.Key('K', up: true);
        k.Key(VirtualKeys.LControl, up: true);
        Assert.Equal("トウキョウ", k.Showing, "Control+K でカタカナ");
        k.Key(VirtualKeys.LControl);
        k.Key('J');
        k.Key('J', up: true);
        k.Key(VirtualKeys.LControl, up: true);
        Assert.Equal("とうきょう", k.Showing, "Control+J でひらがな");
        var normal = new Keyboard();
        normal.Type("toukyou");
        normal.Key(VirtualKeys.LControl);
        normal.Key('K');
        normal.Key('K', up: true);
        normal.Key(VirtualKeys.LControl, up: true);
        Assert.True(normal.Showing != "トウキョウ", "OFF は今までどおり");
    }

    [Test]
    public static void Issue821_LongJapaneseWithTwoCamelCaseNames()
    {
        var k = new Keyboard();
        k.Type("korewoiPhonedesatueisi,YouTubeniappuro-dosiyou");
        Assert.Equal("これをiPhoneでさつえいし、YouTubeにあっぷろーどしよう", k.Showing, "iPhone と YouTube を含む長い文も日本語");
    }

    [Test]
    public static void Issue822_826_AutoclickerAndExtStayEnglish()
    {
        var k = new Keyboard();
        k.Type("autoclicker");
        Assert.Equal("autoclicker", k.Showing, "autoclicker は英字");
        var e = new Keyboard();
        e.Type("ext");
        Assert.Equal("ext", e.Showing, "ext は英字");
    }

    [Test]
    public static void Enter_CommitsWhatIsShown()
    {
        // 入力途中の子音 (bookoff の ff) で、表示 (booこっf) と確定 (bookoff) の区切りが違っていた
        foreach (var typed in new[] { "bookoff", "tabemas", "abacus", "tetr.i" })
        {
            var k = new Keyboard();
            k.Type(typed);
            var showing = k.Showing;
            k.Type("\n");
            Assert.Equal(showing, k.Host.Document, $"「{typed}」");
        }
    }

    [Test]
    public static void Issue72_CursorMovesIntoBrackets()
    {
        var k = new Keyboard { CursorIntoBrackets = true };
        k.Type("[]\n");
        Assert.Equal("「」", k.Host.Document);
        Assert.True(k.Host.Events.Contains("down:25"), "← を送って中に入る: " + string.Join(",", k.Host.Events));
        var normal = new Keyboard();
        normal.Type("[]\n");
        Assert.True(!normal.Host.Events.Contains("down:25"), "OFF は送らない");
    }

    [Test]
    public static void Issue63_ControlKeysCanBeBound()
    {
        var k = new Keyboard { ControlKeyBindings = "T=F7; Y=F6" };
        k.Type("toukyou");
        k.Key(VirtualKeys.LControl);
        k.Key('T');
        k.Key('T', up: true);
        k.Key(VirtualKeys.LControl, up: true);
        Assert.Equal("トウキョウ", k.Showing, "Control+T = F7 (カタカナ)");
        k.Key(VirtualKeys.LControl);
        k.Key('Y');
        k.Key('Y', up: true);
        k.Key(VirtualKeys.LControl, up: true);
        Assert.Equal("とうきょう", k.Showing, "Control+Y = F6 (ひらがな)");
    }

    [Test]
    public static void Issue460_NumpadDirectInput()
    {
        // #460: 何も打っていないときのテンキーの数字・記号は、変換ボックスに入れず、半角でそのまま入れる
        var k = new Keyboard();
        k.Press(0x61); k.Press(0x6B); k.Press(0x62); k.Press(0x6E); k.Press(0x65);
        Assert.Equal("1+2.5", k.Host.Document, "何も打っていないとき");
        Assert.True(!k.Captured, "変換ボックスは出さない");

        // 入力中なら今までどおり変換ボックスに入れる
        k = new Keyboard();
        k.Type("ai");
        k.Press(0x61);
        Assert.Equal("あい1", k.Host.View?.Text, "入力中");
        Assert.Equal("", k.Host.Document);

        // 上の段の数字は、何も打っていないときも今までどおり変換ボックスに入る
        k = new Keyboard();
        k.Type("1");
        Assert.True(k.Captured, "上の段の数字");
    }

    [Test]
    public static void Issue209_MicrosoftImeControlKeysCanBeBound()
    {
        // #209: 使い方 (docs/USAGE.md) に書いた、Microsoft IME と同じ割り当て。Ctrl+O は「Control キーの割り当て」の F10 より、個別の割り当ての F8 を優先する
        foreach (var (letter, expected) in new[] { ('U', "とうきょう"), ('I', "トウキョウ"), ('O', "ﾄｳｷｮｳ"), ('P', "ｔｏｕｋｙｏｕ"), ('T', "toukyou") })
        {
            var k = new Keyboard { ControlKeyBindings = "U=F6;I=F7;O=F8;P=F9;T=F10" };
            k.Type("toukyou");
            k.Key(VirtualKeys.LControl);
            k.Key(letter);
            k.Key(letter, up: true);
            k.Key(VirtualKeys.LControl, up: true);
            Assert.Equal(expected, k.Showing, $"Control+{letter}");
        }
    }

    [Test]
    public static void Issue62_ConsonantAbbreviationOffersWord()
    {
        var k = new Keyboard { ConsonantAbbreviations = true };
        k.Type("ast ");
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("明日"), "ast → 明日: " + string.Join(",", candidates));
        // 設定が OFF (既定) なら、略入力の候補は出さない
        var off = new Keyboard();
        off.Type("ast ");
        Assert.True(off.Host.View is null || !off.Host.View.Candidates.Contains("明日"), "OFF は出さない");
    }

    [Test]
    public static void Issue295_CalculatorOffersResult()
    {
        Assert.Equal("7", Calculator.Evaluate("1+2*3"), "掛け算が先");
        Assert.Equal("9", Calculator.Evaluate("(1+2)*3="), "括弧と末尾の =");
        Assert.Equal("0.5", Calculator.Evaluate("1/2"), "小数");
        Assert.True(Calculator.Evaluate("1/0") is null && Calculator.Evaluate("123") is null && Calculator.Evaluate("1+") is null, "割れない・式でない・途中は null");
        var k = new Keyboard { CalculatorCandidates = true };
        k.Type("1+2*3 ");
        Assert.True(k.Host.View!.Candidates.Contains("7") && k.Host.View.Candidates.Contains("1+2*3=7"), string.Join(",", k.Host.View.Candidates));
        var off = new Keyboard();
        off.Type("1+2*3 ");
        Assert.True(off.Host.View is null || !off.Host.View.Candidates.Contains("7"), "OFF は候補に出さない");
    }

    [Test]
    public static void Issue1403_CalculatorOffersPower()
    {
        Assert.Equal("1024", Calculator.Evaluate("2^10"), "べき乗");
        Assert.Equal("1024", Calculator.Evaluate("2＾10"), "全角の＾");
        Assert.Equal("512", Calculator.Evaluate("2^3^2"), "右結合");
        Assert.Equal("27", Calculator.Evaluate("3*6^2/4"), "べき乗は乗除より優先");
        Assert.Equal("-1024", Calculator.Evaluate("-2^10"), "符号より累乗優先");
        Assert.Equal("1024", Calculator.Evaluate("(-2)^10"), "括弧で括った場合");
        Assert.Equal("0.5", Calculator.Evaluate("2^-1"), "負の指数");
        Assert.Equal("1", Calculator.Evaluate("0^0"), "0^0 は 1");
        Assert.Equal("95367431640625", Calculator.Evaluate("5^20"), "10^15 未満は出す");
        Assert.True(Calculator.Evaluate("10^15") is null, "大きすぎる結果は出さない");
        Assert.True(Calculator.Evaluate("2^") is null && Calculator.Evaluate("^2") is null && Calculator.Evaluate("(-2)^0.5") is null, "途中・虚数は null");
        var k = new Keyboard { CalculatorCandidates = true };
        k.Type("2^10 ");
        Assert.True(k.Host.View!.Candidates.Contains("1024") && k.Host.View.Candidates.Contains("2^10=1024"), string.Join(",", k.Host.View.Candidates));
        var off = new Keyboard();
        off.Type("2^10 ");
        Assert.True(off.Host.View is null || !off.Host.View.Candidates.Contains("1024"), "OFF は候補に出さない");
    }


    [Test]
    public static void Laughter_IsShownAsWWhileTyping()
    {
        // 打っている途中も、確定と同じく笑いの w で見せる (くさっっw と見せて、確定で くさwww になっていた: #2101)
        var k = new Keyboard();
        k.Type("kusawww");
        Assert.Equal("くさwww", SessionResult.ForFront(k.Host.View!, showGuide: true, numberKeysSelect: true).Text, "打っている途中");
        k.Type("\n");
        Assert.Equal("くさwww", k.Host.Document);
        // w の後ろに母音を打てば っ (wwa = っわ)
        var sokuon = new Keyboard();
        sokuon.Type("kawwa");
        Assert.Equal("かっわ", sokuon.Showing);
    }

    [Test]
    public static void Issue491_LearningCanBeSeparatedByContext()
    {
        var memory = new LanguageMemory(null);
        memory.Remember("nw", english: false, explicitChoice: true, context: false);
        memory.Remember("nw", english: true, explicitChoice: true, context: true);
        Assert.Equal(false, memory.Get("nw", context: false), "日本語の後では日本語");
        Assert.Equal(true, memory.Get("nw", context: true), "英語の後では英語");
        // 学習した語の一覧には、印 (@e / @j) を出さず、語そのものと文脈を分けて返す。消すときは印付きの名前
        var listed = memory.Entries().OrderBy(e => e.Context).ToList();
        Assert.True(listed.All(e => e.Word == "nw"), "語に印が付かない: " + string.Join(",", listed.Select(e => e.Word)));
        Assert.True(listed.Select(e => e.Context).SequenceEqual(["日本語の後", "英語の後"]) || listed.Select(e => e.Context).SequenceEqual(["英語の後", "日本語の後"]), "文脈が分かる");
        memory.Remove([listed.First(e => e.Context == "英語の後").EntryKey]);
        Assert.Equal(1, memory.Entries().Count, "文脈ごとに消せる");
        memory.Remember("nw", english: true, explicitChoice: true, context: true);
        Assert.True(memory.Get("nw") is null, "文脈が分からなければ、文脈ごとの学習は使わない");
        var plain = new LanguageMemory(null);
        plain.Remember("nw", english: true, explicitChoice: true);
        Assert.Equal(true, plain.Get("nw", context: false), "OFF (文脈で分けずに覚えたもの) は今までどおり 1 つだけ");
    }

    [Test]
    public static void Issue672_DigitChoiceIsLearned()
    {
        var k = new Keyboard(history: new ConversionHistory(null));
        k.Type("1 ");
        var candidates = k.Host.View!.Candidates.ToList();
        Assert.True(candidates.Count >= 2, "候補が 2 つ以上: " + string.Join(",", candidates));
        k.Press(VirtualKeys.Space);
        var chosen = k.Host.View!.Candidates[k.Host.View.SelectedIndex];
        k.Press(VirtualKeys.Return);
        k.Type("1 ");
        Assert.Equal(chosen, k.Host.View!.Candidates[k.Host.View.SelectedIndex], "次に同じ数字を変換したら、選んだ候補が先に出る");
    }

    [Test]
    public static void Issue259_SpaceCanBeFullWidthByDefault()
    {
        var k = new Keyboard { SpaceWidth = Meltype.Config.SpaceWidth.FullWidth };
        k.Type("tanaka\n");
        k.Press(VirtualKeys.Space);
        k.Type("tarou\n");
        Assert.Equal("たなか　たろう", k.Host.Document, "Space が全角");
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
        Assert.Equal("たなか　たろう ", k.Host.Document, "Shift+Space が半角");
    }

    [Test]
    public static void Issue530_ToggleShortcutsAreParsed()
    {
        var list = KeyShortcut.ParseList("Ctrl+Space; Alt+Backquote, Shift+F10, bad+key, Muhenkan");
        Assert.Equal(4, list.Count, "読める 4 つだけ");
        Assert.True(list[0].Matches(0x20, true, false, false, false) && !list[0].Matches(0x20, false, false, false, false), "Ctrl+Space");
        Assert.True(list[1].Matches(0xC0, false, true, false, false), "Alt+Backquote");
        Assert.True(list[2].Matches(0x79, false, false, true, false), "Shift+F10");
        Assert.True(list[3].Matches(0x1D, false, false, false, false), "Muhenkan");
        Assert.Equal(0, KeyShortcut.ParseList("").Count, "空");
        // #955: CapsLock・JIS の 英数 キーも書ける
        Assert.True(KeyShortcut.TryParse("Shift+CapsLock", out var caps) && caps.Matches(0x14, false, false, true, false), "Shift+CapsLock");
        Assert.True(KeyShortcut.TryParse("Eisu", out var eisu) && eisu.Matches(0xF0, false, false, false, false), "Eisu");
        Assert.True(!KeyShortcut.TryParse("Ctrl+", out _) && !KeyShortcut.TryParse("A+B", out _), "キーが無い・2 つ");
    }

    [Test]
    public static void Issue291_HashNumberOffersGitHubTitle()
    {
        var titles = new GitHubIssueTitles((repository, number) => Task.FromResult<string?>(repository == "yksr-melt/Meltype" && number == 123 ? "変換ボックスが隠れる" : null));
        var k = new Keyboard { GitHubRepository = "yksr-melt/Meltype", GitHubTitles = titles };
        k.Type("#123 ");
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("#123 変換ボックスが隠れる") && candidates.Contains("yksr-melt/Meltype#123"), string.Join(",", candidates));
        Assert.True(GitHubIssueTitles.IsRepository("a-b/c_d.e") && !GitHubIssueTitles.IsRepository("a/b/c") && !GitHubIssueTitles.IsRepository("a b/c"), "リポジトリの形");
        Assert.True(new GitHubIssueTitles((_, _) => throw new InvalidOperationException()).Lookup("a/b", 1) is null, "失敗は null");
        // 調べられなかった番号 (404・レート制限) は、しばらくしたらもう一度調べる。ずっと覚えて二度と調べない、ということはしない
        var calls = 0;
        long clock = 0;
        var retry = new GitHubIssueTitles((_, _) => Task.FromResult<string?>(++calls == 1 ? null : "直った"), () => clock);
        Assert.True(retry.Lookup("a/b", 7) is null, "1 回目は調べられなかった");
        Assert.True(retry.Lookup("a/b", 7) is null && calls == 1, "すぐには調べ直さない");
        clock += 61_000;
        retry.Lookup("a/b", 7); // 期限が切れたので覚えたものを捨てる
        Assert.Equal("直った", retry.Lookup("a/b", 7) ?? "(調べ直さなかった)", "しばらくしたら調べ直す");
        // 調べている途中は、最初の 1 回だけ待つ (打鍵のたびに UI スレッドを止めない)
        var slow = new GitHubIssueTitles((_, _) => Task.Delay(400).ContinueWith(_ => (string?)"遅い")) { Timeout = TimeSpan.FromMilliseconds(30) };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        slow.Lookup("a/b", 9);
        var first = watch.ElapsedMilliseconds;
        watch.Restart();
        slow.Lookup("a/b", 9);
        Assert.True(first >= 20 && watch.ElapsedMilliseconds < 20, $"最初の 1 回だけ待つ: 1 回目 {first}ms、2 回目 {watch.ElapsedMilliseconds}ms");
    }

    [Test]
    public static void Issue865_HashNumberIsOneClause()
    {
        // #123 は # と 123 を別々の文節に変換しない (タイトルが無い・リポジトリが空でも)
        var k = new Keyboard();
        k.Type("#123 ");
        Assert.True(k.Host.View is { } view && view.Candidates.Contains("#123") && view.Candidates.Contains("＃１２３"), string.Join(",", k.Host.View?.Candidates ?? []));
        var none = new Keyboard { GitHubRepository = "a/b", GitHubTitles = new GitHubIssueTitles((_, _) => Task.FromResult<string?>(null)) };
        none.Type("#45 ");
        Assert.True(none.Host.View is { } v && v.Candidates.Contains("#45"), string.Join(",", none.Host.View?.Candidates ?? []));
    }

    [Test]
    public static void Issue1934_GitHubTitlesNeedOnlyTheRepository()
    {
        // リポジトリが書いてあれば出す。空なら問い合わせない (ON/OFF の項目は消した)
        var asked = 0;
        var titles = new GitHubIssueTitles((_, _) => { asked++; return Task.FromResult<string?>("タイトル"); });
        var empty = new Keyboard { GitHubTitles = titles };
        empty.Type("#12 ");
        Assert.True(asked == 0 && empty.Host.View is { } view && !view.Candidates.Any(c => c.Contains("タイトル", StringComparison.Ordinal)), string.Join(",", empty.Host.View?.Candidates ?? []));
        var set = new Keyboard { GitHubRepository = "a/b", GitHubTitles = titles };
        set.Type("#12 ");
        Assert.True(set.Host.View is { } shown && shown.Candidates.Contains("#12 タイトル"), string.Join(",", set.Host.View?.Candidates ?? []));
    }

    [Test]
    public static void Issue866_GitHubRepositorySettingIsNormalized()
    {
        // 設定のリポジトリは、空白・URL・.git が付いていても「所有者/名前」として問い合わせる
        foreach (var setting in (string[])[" yksr-melt/Meltype ", "https://github.com/yksr-melt/Meltype", "github.com/yksr-melt/Meltype.git", "https://github.com/yksr-melt/Meltype/issues/1"])
        {
            Assert.Equal("yksr-melt/Meltype", GitHubIssueTitles.Normalize(setting), setting);
            var titles = new GitHubIssueTitles((repository, number) => Task.FromResult<string?>(repository == "yksr-melt/Meltype" && number == 866 ? "候補に出ない" : null));
            var k = new Keyboard { GitHubRepository = setting, GitHubTitles = titles };
            k.Type("#866 ");
            var candidates = k.Host.View?.Candidates ?? [];
            Assert.True(candidates.Contains("#866 候補に出ない") && candidates.Contains("yksr-melt/Meltype#866"), setting + ": " + string.Join(",", candidates));
        }
    }

    [Test]
    public static void Issue281_ClipboardTextIsOfferedOnlyWhenEnabled()
    {
        var k = new Keyboard { ClipboardCandidates = true };
        k.Host.ClipboardText = "  コピーした語  ";
        k.Type("kurippubo-do ");
        Assert.True(k.Host.View!.Candidates.Contains("コピーした語"), "候補に出る: " + string.Join(",", k.Host.View.Candidates));
        var off = new Keyboard();
        off.Host.ClipboardText = "コピーした語";
        off.Type("kurippubo-do ");
        Assert.True(off.Host.View is null || !off.Host.View.Candidates.Contains("コピーした語"), "OFF はクリップボードを読まない");
        Assert.True(ClipboardConversion.Line("a\nb") is null && ClipboardConversion.Line(new string('あ', 201)) is null && ClipboardConversion.Line(" ") is null, "複数行・長い・空は出さない");
    }

    [Test]
    public static void Issue810_PauseHotkeySettingIsParsed()
    {
        // 一時停止 / 再開のホットキーの設定 (Ctrl+Alt+F10) は、切り替えキーの設定と同じ書き方で読む
        var list = KeyShortcut.ParseList("Ctrl+Alt+F10");
        Assert.Equal(1, list.Count);
        Assert.True(list[0].Matches(0x79, true, true, false, false), "Ctrl+Alt+F10");
        Assert.Equal("", new Meltype.Config.Settings().PauseHotkey, "既定は空 (組み込みの Ctrl+Alt+F12)");
    }

    [Test]
    public static void Issue812_CwdThenNihaIsJapanese()
    {
        var k = new Keyboard();
        k.Type("cwdniha\n");
        Assert.Equal("cwdには", k.Host.Document, "cwd + には");
    }

    [Test]
    public static void Issue677_EnterCommitsWhatTheFrontShows()
    {
        // 語末の n で終わる入力 (かn・かんたn) も、フロントに見せている形 (末尾の n を ん にして変換した形) のまま確定する
        foreach (var (typed, expected) in new[] { ("kan", "感"), ("kantan", "簡単") })
        {
            var shown = new Keyboard(live: true);
            shown.Type(typed);
            var front = SessionResult.ForFront(shown.Host.View!, showGuide: true, numberKeysSelect: true).Text;
            shown.Type("\n");
            Assert.Equal(expected, front, typed);
            Assert.Equal(front, shown.Host.Document, $"見えているとおりに確定 ({typed})");
        }
        var done = new Keyboard(live: true);
        done.Type("kantann\n");
        Assert.Equal("簡単", done.Host.Document, "nn まで打てば漢字");
    }

    [Test]
    public static void Issue535_OverchoiceStaysEnglish()
    {
        var k = new Keyboard(live: true);
        k.Type("overchoice\n");
        Assert.Equal("overchoice", k.Host.Document);
    }

    [Test]
    public static void CandidateMeaning_FromTranslations()
    {
        // 候補で止まったら意味を出す (同音異義語の手がかり)
        var translations = TranslationDictionary.Load();
        Assert.Equal("bridge", translations.Meaning("橋"));
        Assert.Equal("chopsticks", translations.Meaning("箸を"), "助詞が付いても");
        Assert.Equal(null, translations.Meaning("はし"), "かなだけの候補には出さない");
        Assert.Equal(null, translations.Meaning("bridge"));
    }

    [Test]
    public static void CandidateMeaning_Japanese()
    {
        // 日本語の意味 (ウィクショナリー)。読みで意味を選ぶ、活用した形・助詞付きでも引ける
        var meanings = MeaningDictionary.Load();
        Assert.True(meanings.Lookup("箸を", "はしを")?.Contains("食器") == true, "箸を → 食器の一種");
        Assert.True(meanings.Lookup("橋", "はし")?.Contains("渡る") == true, "はし と読んだ 橋");
        Assert.True(meanings.Lookup("橋", "きょう")?.StartsWith("中脳", StringComparison.Ordinal) == true, "きょう と読んだ 橋 は脳橋の意味");
        Assert.True(meanings.Lookup("持って", "もって") is not null, "活用した形 (持って → 持つ)");
        Assert.True(meanings.Lookup("美しかった", "うつくしかった") is not null, "い形容詞の活用");
        Assert.Equal(null, meanings.Lookup("はし", "はし"), "かなだけの候補には出さない");
    }

    [Test]
    public static void SingleCapital_ThenParticle_IsSplit()
    {
        // Anisiyouka が英字のままになっていた (A にしようか)。名前 (Tanaka) は区切らない。
        AssertCommitted(("Anisiyouka", "Aにしようか"), ("Xgawakaru", "Xがわかる"), ("Tanaka", "Tanaka"));
    }

    [Test]
    public static void CommaAfterDigit_IsTouten_UnlessDigitFollows()
    {
        // x64,arm64 の , は読点 (x64、arm64)。1,000 の , は桁区切りのまま。
        AssertCommitted(("x64,arm64", "x64、arm64"), ("1,000en", "1,000えん"), ("10,000", "10,000"));
    }

    [Test]
    public static void CommaAfterDigit_AtEnd_IsTouten()
    {
        // #1989: 100, は打ち終わっても , のままだった
        AssertCommitted(("100,", "100、"), ("1,000,", "1,000、"), ("1.5,", "1.5、"), ("x64,", "x64、"), ("10:00,", "10:00、"), ("kyouha100,", "きょうは100、"));
        // 小数点と、数字の間の記号は打ったまま
        AssertCommitted(("100.", "100."), ("1.5", "1.5"), ("10:00", "10:00"), ("1/2,", "1/2、"), ("1,-", "1、-"));
        var space = new Keyboard();
        space.Type("100, \n");
        Assert.Equal("100、", space.Host.Document, "Space");
        var setting = new Keyboard { Punctuation = Meltype.Config.PunctuationStyle.FullWidthCommaPeriod };
        setting.Type("100,\n");
        Assert.Equal("100，", setting.Host.Document, "設定の句読点");
        // 英文の続きと、確定した数字の続きは半角
        foreach (var (before, keys, expected) in new[] { ("I want ", "2020,\n", "2020,"), ("I want ", "2020, ", "2020, "), ("I want ", "x64,\n", "x64,"), ("1", ",000\n", ",000") })
        {
            var k = new Keyboard();
            k.Host.PrecedingText = before;
            k.Type(keys);
            Assert.Equal(expected, k.Host.Document, before + keys);
        }
    }

    [Test]
    public static void Marks_AfterBackspace_AreReadAsIfRetyped()
    {
        // 後ろの文字を消したら、, . は残った文字を打ち直したのと同じになる
        foreach (var (typed, retyped) in new[] { (",,,\b", ",,"), ("...\b", ".."), ("x64,ka\b", "x64,"), ("google.a\b", "google."), ("google.1\b", "google.") })
        {
            var (erased, fresh) = (new Keyboard(), new Keyboard());
            erased.Settings.JapanesePunctuationAfterEnglish = fresh.Settings.JapanesePunctuationAfterEnglish = true;
            erased.Type(typed);
            fresh.Type(retyped);
            Assert.Equal(fresh.Showing, erased.Showing, typed + ": 表示");
            erased.Type("\n");
            fresh.Type("\n");
            Assert.Equal(fresh.Host.Document, erased.Host.Document, typed + ": 確定");
        }
    }

    [Test]
    public static void Punctuation_FollowsSetting()
    {
        // 句読点の設定 (Microsoft IME と同じ 4 通り)。数字の間の . , (1.5、1,000) は半角のまま。
        foreach (var (style, expected) in new[]
        {
            (Meltype.Config.PunctuationStyle.Japanese, "はい、そうです。1.5と1,000"),
            (Meltype.Config.PunctuationStyle.FullWidthCommaPeriod, "はい，そうです．1.5と1,000"),
            (Meltype.Config.PunctuationStyle.FullWidthCommaKuten, "はい，そうです。1.5と1,000"),
            (Meltype.Config.PunctuationStyle.ToutenFullWidthPeriod, "はい、そうです．1.5と1,000"),
        })
        {
            var k = new Keyboard { Punctuation = style };
            k.Type("hai,soudesu.1.5to1,000\n");
            Assert.Equal(expected, k.Host.Document, style.ToString());
        }
    }

    [Test]
    public static void Punctuation_CommaAfterDigit_FollowsSetting()
    {
        // x64,arm64 の , も設定の読点にする
        var k = new Keyboard { Punctuation = Meltype.Config.PunctuationStyle.FullWidthCommaPeriod };
        k.Type("x64,arm64\n");
        Assert.Equal("x64，arm64", k.Host.Document);
    }

    /// <summary>読みの 、。 はそのまま、，． は半角の , . にして返す変換エンジン (半角の , . を確定して覚えた Mozc の代わり)。</summary>
    private sealed class PunctuationEchoConverter : TestConverter
    {
        private static readonly (string Kana, string Text)[] Words = [("わたしは", "私は"), ("りんご", "林檎"), ("3こ", "3個")];

        public List<string> Received { get; } = [];

        public override string? Convert(string hiragana, string? before)
        {
            Received.Add(hiragana);
            return Map(hiragana);
        }

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before)
        {
            Received.Add(hiragana);
            return [new(hiragana, Map(hiragana))];
        }

        private static string Map(string reading)
        {
            foreach (var (kana, text) in Words) reading = reading.Replace(kana, text, StringComparison.Ordinal);
            return reading.Replace('，', ',').Replace('．', '.');
        }
    }

    [Test]
    public static void Punctuation_SettingReachesConvertedText()
    {
        // #498: 設定の句読点が、Space の変換・ライブ変換のどちらでも変換結果に出る
        foreach (var (style, expected) in new[]
        {
            (Meltype.Config.PunctuationStyle.Japanese, "私は、林檎、3個。"),
            (Meltype.Config.PunctuationStyle.FullWidthCommaPeriod, "私は，林檎，3個．"),
            (Meltype.Config.PunctuationStyle.FullWidthCommaKuten, "私は，林檎，3個。"),
            (Meltype.Config.PunctuationStyle.ToutenFullWidthPeriod, "私は、林檎、3個．"),
        })
        {
            var name = style.ToString();
            var converter = new PunctuationEchoConverter();
            var k = new Keyboard(converter: converter) { Punctuation = style };
            k.Type("watasiha,ringo,3ko. ");
            Assert.Equal(expected, k.Showing, name + ": Space で変換");
            k.Type("\n");
            Assert.Equal(expected, k.Host.Output.Single(), name + ": 変換して確定");
            Assert.True(converter.Received.All(r => !r.Contains('，') && !r.Contains('．')), name + ": 変換エンジンには 、。 で渡す: " + string.Join(" / ", converter.Received));

            var live = new Keyboard(live: true, converter: new PunctuationEchoConverter()) { Punctuation = style };
            live.Type("watasiha,ringo,3ko.");
            Assert.Equal(expected, live.Showing, name + ": ライブ変換");
            live.Type("\n");
            Assert.Equal(expected, live.Host.Output.Single(), name + ": ライブ変換のまま確定");
        }
    }

    [Test]
    public static void Punctuation_SettingKeepsHalfWidthDotsAndCommas()
    {
        // 設定の句読点を当てるのは 、。 だけ。ドメイン・数字の半角の . , は変えない
        foreach (var style in Enum.GetValues<Meltype.Config.PunctuationStyle>())
        {
            foreach (var (keys, expected) in new[] { ("example.com\n", "example.com"), ("1.5to1,000\n", "1.5と1,000") })
            {
                var k = new Keyboard { Punctuation = style };
                k.Type(keys);
                Assert.Equal(expected, k.Host.Document, style.ToString());
            }
        }
    }

    [Test]
    public static void Punctuation_SettingReachesShortAndPunctuationOnlyInput()
    {
        const Meltype.Config.PunctuationStyle Period = Meltype.Config.PunctuationStyle.FullWidthCommaPeriod;
        foreach (var (keys, expected) in new[] { ("ha, ", "は，"), (", ", "，"), (". ", "．") })
        {
            var k = new Keyboard(converter: new PunctuationEchoConverter()) { Punctuation = Period };
            k.Type(keys);
            Assert.Equal(expected, k.Showing, keys + "Space で変換");
            k.Type("\n");
            Assert.Equal(expected, k.Host.Output.Single(), keys);
        }

        var after = new Keyboard(converter: new PunctuationEchoConverter()) { Punctuation = Period };
        after.Type("watasiha \n,\n");
        Assert.Equal("私は，", after.Host.Document, "確定したあとに句読点だけを打つ");

        var typing = new Keyboard(converter: new PunctuationEchoConverter()) { Punctuation = Period };
        typing.Type("watasiha,");
        Assert.Equal("わたしは，", typing.Showing, "ライブ変換 OFF で変換する前");
    }

    /// <summary>数字と続くかなをまとめて渡されたときだけ「3個」にする変換エンジン。</summary>
    private sealed class CounterConverter : TestConverter
    {
        public List<string> Received { get; } = [];

        public override string? Convert(string hiragana, string? before) => Map(hiragana);

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before)
        {
            Received.Add(hiragana);
            return Map(hiragana) is { } text ? [new(hiragana, text)] : null;
        }

        private static string? Map(string hiragana) => hiragana switch
        {
            "りんご" => "林檎",
            "こ" => "子",
            "3こ" => "3個",
            "りんご、3こ" => "林檎、3個",
            "3こ、りんご" => "3個、林檎",
            "りんご、3こ。" => "林檎、3個。",
            _ => null,
        };
    }

    [Test]
    public static void Punctuation_SettingDoesNotSplitDigits()
    {
        // #1712: 句読点の隣の数字も、かなと続けて変換エンジンに渡す (ringo,3ko の こ だけを渡すと「3個」にならない)
        (string? Showing, string Output, List<string> Received) Type(string keys, Meltype.Config.PunctuationStyle style)
        {
            var converter = new CounterConverter();
            var k = new Keyboard(live: true, converter: converter) { Punctuation = style };
            k.Type(keys);
            var showing = k.Showing;
            k.Type("\n");
            return (showing, k.Host.Output.Single(), converter.Received);
        }

        const Meltype.Config.PunctuationStyle Period = Meltype.Config.PunctuationStyle.FullWidthCommaPeriod;
        const Meltype.Config.PunctuationStyle Japanese = Meltype.Config.PunctuationStyle.Japanese;
        var full = Type("ringo,3ko", Period);
        Assert.Equal("林檎，3個", full.Showing);
        Assert.Equal("林檎，3個", full.Output);
        Assert.True(!full.Received.Contains("こ"), "3 と こ を分けて渡さない: " + string.Join(" / ", full.Received));
        Assert.Equal("林檎、3個", Type("ringo,3ko", Japanese).Output);
        Assert.Equal("3個，林檎", Type("3ko,ringo", Period).Output, "数字が句読点の前");
        Assert.Equal("3個、林檎", Type("3ko,ringo", Japanese).Output);
        Assert.Equal("林檎，3個．", Type("ringo,3ko.", Period).Output);
    }

    [Test]
    public static void Punctuation_CandidatesAreDistinctAsShown()
    {
        // 変換エンジンは 、 の候補に ， も返す。「，．」の設定ではどちらも ， になるので、一覧には 1 つだけ出す
        IReadOnlyList<string> More(string reading) => reading == "、" ? ["、", "，", ",", "・"] : [];
        var k = new Keyboard(converter: new PunctuationEchoConverter(), moreCandidates: More) { Punctuation = Meltype.Config.PunctuationStyle.FullWidthCommaPeriod };
        k.Type(", ");
        k.Press(VirtualKeys.Space);
        var shown = k.Host.View!.Candidates;
        Assert.Equal(1, shown.Count(c => c == "，"), string.Join(" ", shown));
        Assert.True(shown.Contains(",") && shown.Contains("・"), "ほかの候補は残す: " + string.Join(" ", shown));

        var japanese = new Keyboard(converter: new PunctuationEchoConverter(), moreCandidates: More);
        japanese.Type(", ");
        japanese.Press(VirtualKeys.Space);
        Assert.True(japanese.Host.View!.Candidates.Contains("、") && japanese.Host.View.Candidates.Contains("，"), "「、。」の設定では 、 と ， の両方を出す");
    }

    [Test]
    public static void Punctuation_ChoiceStaysOnPunctuationOnlyInput()
    {
        // 句読点だけを変換して選んだ候補 (、 → ・) は、句読点だけを変換したときにだけ出す。「わたしは、」の 、 は、覚えた変換履歴・Mozc の学習があっても 、 のまま
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history, moreCandidates: reading => reading == "、" ? ["、", "・"] : []);
        k.Type(", ");
        for (var i = 0; i < 10 && k.Host.View!.Candidates[k.Host.View.SelectedIndex] != "・"; i++) k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal("・", k.Host.Document);

        var again = new Keyboard(history: history);
        again.Type(", ");
        Assert.Equal("・", again.Showing, "句読点だけを変換すると覚えた候補");

        foreach (var (style, expected) in new[] { (Meltype.Config.PunctuationStyle.Japanese, "私は、"), (Meltype.Config.PunctuationStyle.FullWidthCommaPeriod, "私は，"),
            (Meltype.Config.PunctuationStyle.FullWidthCommaKuten, "私は，"), (Meltype.Config.PunctuationStyle.ToutenFullWidthPeriod, "私は、") })
        {
            var live = new Keyboard(live: true, history: history, converter: new SymbolChangingConverter(split: true)) { Punctuation = style };
            live.Type("watasiha,");
            Assert.Equal(expected, live.Showing, style + ": かなと一緒に打った句読点は覚えた候補にしない");
        }
    }

    [Test]
    public static void Punctuation_TextReadFromAppUsesKutouten()
    {
        // 入力欄から読んだキャレットの前の文字は、設定の句読点 (，) を 、 に戻して文脈にする
        var k = new Keyboard { Punctuation = Meltype.Config.PunctuationStyle.FullWidthCommaPeriod };
        k.Host.PrecedingText = "はい，";
        k.Type("kyou ");
        Assert.Equal("はい、", k.Converter.LastContext, "文脈");
    }

    [Test]
    public static void Punctuation_ReconversionReadingUsesKutouten()
    {
        // 再変換する文字の読みに入った設定の句読点 (，) は、、 にして変換エンジンに渡す
        var converter = new PunctuationEchoConverter();
        var k = new Keyboard(converter: converter) { Punctuation = Meltype.Config.PunctuationStyle.FullWidthCommaPeriod };
        k.Host.Selection = new ReconversionSelection("わたしは，", "わたしは，");
        k.Press(VirtualKeys.Convert);
        Assert.Equal("私は，", k.Showing);
        Assert.True(converter.Received.All(r => !r.Contains('，')), "変換エンジンには 、 で渡す: " + string.Join(" / ", converter.Received));
    }

    [Test]
    public static void Punctuation_AfterEnglish_FollowsSetting()
    {
        // #416: 英字のすぐ後ろの , . も句読点にする設定。OFF なら今までどおり半角
        string Type(string keys, bool on, string? before = null, Meltype.Config.PunctuationStyle style = default)
        {
            var k = new Keyboard { Punctuation = style };
            k.Settings.JapanesePunctuationAfterEnglish = on;
            k.Host.PrecedingText = before;
            k.Type(keys);
            return k.Host.Document;
        }
        Assert.Equal("IME。", Type("IME.\n", on: true), "略語の後ろも句点");
        Assert.Equal("Google.", Type("Google.\n", on: false), "OFF は半角のまま");
        Assert.Equal("Google。", Type("Google.\n", on: true), "ON は句点");
        Assert.Equal("Google、", Type("Google,\n", on: true), "ON は読点");
        Assert.Equal("Google．", Type("Google.\n", on: true, style: Meltype.Config.PunctuationStyle.FullWidthCommaPeriod), "設定「句読点」の形");
        Assert.Equal("world.", Type("world.\n", on: true, before: "Hello "), "キャレットの前が英語 (Hello ) なら英文の続き");
        Assert.Equal("tetr.io", Type("tetr.io\n", on: true), "英単語の中の . は半角");
        // #1424: 英単語を確定したすぐ後
        Assert.Equal("、", Type(",\n", on: true, before: "IME"), "確定した英単語のすぐ後");
        Assert.Equal("IME、", Type("IME\n,\n", on: true), "入力欄の文字が読めないとき");
        Assert.Equal("。", Type(".\n", on: true, before: "IME"), "確定した英単語のすぐ後の句点");
        Assert.Equal(",", Type(",\n", on: false, before: "IME"), "OFF は半角のまま");
        Assert.Equal(",", Type(",\n", on: true, before: "GitHub "), "空白の後は英文の続き");
        Assert.Equal("google.com", Type("google.com\n", on: true, before: "IME"), "次が英数字の . は半角");
    }

    [Test]
    public static void Punctuation_IsSavedAsName()
    {
        // 設定ファイルにはほかの選択肢と同じく名前で保存する
        var settings = new Meltype.Config.Settings { Punctuation = Meltype.Config.PunctuationStyle.FullWidthCommaKuten };
        Assert.True(settings.ToJson().Contains("\"Punctuation\": \"FullWidthCommaKuten\""), "名前で保存");
        var path = Path.Combine(Path.GetTempPath(), $"meltype-punctuation-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            Assert.Equal(Meltype.Config.PunctuationStyle.FullWidthCommaKuten, Meltype.Config.Settings.Load(path).Punctuation);
            Assert.Equal(Meltype.Config.PunctuationStyle.Japanese, new Meltype.Config.Settings().Punctuation, "既定は 、。");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void ShortProperNoun_AfterJapanese_IsJapanese()
    {
        // ある程度は (aruteidoha) の doha を固有名詞 (Doha) として英字にしていた
        var k = new Keyboard();
        k.Type("aruteidoha\n");
        Assert.Equal("あるていどは", k.Host.Document);
        k = new Keyboard();
        k.Type("doha\n");
        Assert.Equal("doha", k.Host.Document, "単独なら固有名詞のまま");
    }

    /// <summary>判定を待たずにアプリへ送った英字 (down) を、ローマ字と分かった時点で同じ数だけ BackSpace で消している。</summary>
    private static void AssertSentThenErased(List<string> events)
    {
        var downs = events.Count(e => e.StartsWith("down:", StringComparison.Ordinal));
        Assert.True(downs > 0, "判定を待たずに英字を送っている");
        var bs = events.FindIndex(e => e.StartsWith("bs:", StringComparison.Ordinal));
        Assert.Equal($"bs:{downs}", bs < 0 ? "(なし)" : events[bs], "送った英字を消す");
        Assert.True(events.FindLastIndex(e => e.StartsWith("down:", StringComparison.Ordinal)) < bs, "送った後に消す");
    }

    [Test]
    public static void DirectMode_EnglishIsSentWithoutWaiting()
    {
        // 英数状態で打った英字が、判定 (ローマ字かどうか) が終わるまで出てこなかった。
        // can・game・today のようにローマ字としても読める語は、Space を押すまで丸ごと出なかった。
        foreach (var word in new[] { "can", "game", "today", "hello" })
        {
            var k = new Keyboard(direct: true);
            k.Type(word);
            Assert.Equal(string.Join(",", word.ToCharArray()), Letters(k.Host.Events), $"{word}: 打ったそばから送る (英語と決まった後は素通し)");
            k.Type(" ");
            Assert.True(k.Direct, $"{word}: 英数のまま");
            Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), $"{word}: 英語なら消さない");
        }
    }

    [Test]
    public static void DirectMode_EnglishPassesThroughInOrder()
    {
        var k = new Keyboard(direct: true);
        k.Type("hello world");
        Assert.True(k.Direct, "英語なら英数のまま");
        Assert.True(k.Showing is null, "変換ボックスは出さない");
        Assert.Equal("h,e,l,l,o, ,w,o,r,l,d", Letters(k.Host.Events), "保留した分も素通しした分も、打った順番どおりに届く");
    }

    private static string Letters(List<string> events)
    {
        // 再生した押下 (down) と素通しした押下 (passed) を順に並べる。キーアップは数えない。
        return string.Join(",", events
            .Select(e => e.Split(':'))
            .Where(p => p[0] is "down" or "passed")
            .Select(p => System.Convert.ToInt32(p[1], 16))
            .Select(vk => vk == 0x20 ? " " : char.ToLowerInvariant((char)vk).ToString()));
    }

    [Test]
    public static void DirectMode_IdleReleasesHeldKeys()
    {
        var k = new Keyboard(direct: true);
        k.Type("ka");
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:", StringComparison.Ordinal)), "判定中でも打った英字はすぐ送る");
        Assert.True(k.CapturesLetters, "判定中は打鍵を受け取る");
        k.Wait(1000);
        k.Type("n");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), "しばらく打たなければ英語とみなす (消さない)");
        Assert.Equal("kan", k.Host.Document);
        Assert.True(!k.CapturesLetters, "判定をやめたら横取りもやめる");
    }

    [Test]
    public static void DirectMode_ShiftAfterHeldWordReachesApp()
    {
        // 英数状態で英字を打った直後に押した Shift がアプリに届かず、(bow) の ) が Shift なしの 0 になっていた。
        var k = new Keyboard(direct: true);
        k.Type("bow");
        Assert.True(k.CapturesLetters, "bow はまだ判定中");
        k.Key(VirtualKeys.LShift);
        k.Press(0x39); // JIS 配列の Shift+9 は )
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("bow)", k.Host.Document, "Shift と一緒のキーは ) のままアプリに届く");
        Assert.True(!k.Captured, "判定をやめたら受け取らない");
    }

    [Test]
    public static void ProperNouns_AreEnglishEvenIfRomajiReadable()
    {
        var cases = new Dictionary<string, string>
        {
            ["amazon"] = "amazon", ["adobe"] = "adobe", ["netflix"] = "netflix", ["spotify"] = "spotify",
            ["kyouhaamazondekaimono"] = "きょうはamazonでかいもの",
            ["nihongonobenkyou"] = "にほんごのべんきょう", // 短い名前 (Ben) は文の途中で英語にしない
            ["suzuki"] = "すずき", // 日本語で書くことが多い名前は固有名詞辞書に入れていない
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
    }

    [Test]
    public static void ProperNouns_OfferCanonicalCasing()
    {
        var k = new Keyboard();
        k.Type("googlede");
        k.Press(VirtualKeys.Left); // 最後の文節 (で)
        k.Press(VirtualKeys.Left); // google
        k.Type(" ");
        Assert.Equal("Google", k.Host.View!.Clauses![0], "候補に正しい大文字小文字の形がある");
    }

    [Test]
    public static void ProperNouns_AutoCapitalizeMixedInternalUpper()
    {
        var cases = new Dictionary<string, string>
        {
            ["iphone"] = "iPhone",
            ["github"] = "GitHub",
            ["macos"] = "macOS",
            ["iphonede"] = "iPhoneで",
            ["kyouhagithubnipush"] = "きょうはGitHubにpush",
            ["javascript"] = "JavaScript",
            ["typescript"] = "TypeScript",
            ["uikit"] = "UIKit",
            ["uikittoswiftui"] = "UIKitとSwiftUI",
            ["uikitsune"] = "ういきつね",
            ["github."] = "GitHub.",
            ["github..."] = "GitHub...",
            ["...github"] = "...GitHub",
            ["iphone's"] = "iPhone's",
            ["github.example.com"] = "github.example.com",
            ["google.github"] = "google.github",
            ["github@example.com"] = "github@example.com",
            ["example.com/github"] = "example.com/github",
            ["newyork"] = "newyork",
            ["cocacola"] = "cocacola",
            ["mcdonalds"] = "mcdonalds",
            ["google"] = "google",   // 先頭だけ大文字の固有名詞は自動で変えない
            ["python"] = "python",   // コマンド・言語名 (先頭だけ大文字) もそのまま
            ["line"] = "line",       // 全大文字の略語も自動で変えない
            ["IPHONE"] = "IPHONE",   // ユーザーが自分で大文字を打ったときはそのまま
            ["Iphone"] = "Iphone",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }

        // 変換候補では iPhone が先頭、Space で小文字の iphone にも戻せる
        var conv = new Keyboard();
        conv.Type("iphonede");
        conv.Press(VirtualKeys.Left); // で
        conv.Press(VirtualKeys.Left); // iPhone
        Assert.Equal("iPhone", conv.Host.View!.Clauses![0], "自動大文字化の対象は第1候補になる");
        conv.Type(" ");
        Assert.Equal("iphone", conv.Host.View!.Clauses![0], "Space で小文字のままにも戻せる");

        // ドメインの一部 (github.com・google.github) は、変換しても小文字が第1候補のまま
        var domain = new Keyboard { SeparateKeyRoles = true };
        domain.Type("github.com ");
        Assert.Equal("github", domain.Host.View!.Clauses![0], "ドメインの先頭は小文字が第1候補");
        domain.Type("\ngithub");
        Assert.Equal("GitHub", domain.Showing, "ドメインのまま確定しても小文字として学習しない");
        var sub = new Keyboard { SeparateKeyRoles = true };
        sub.Type("google.github ");
        Assert.Equal("github", sub.Host.View!.Clauses![2], "ドメインの後ろも小文字が第1候補");

        // ユーザー辞書に登録した読み (HasRegisteredReading) でも、登録語の次の英語候補は iPhone → iphone の順
        var userDict = new UserDictionary(null);
        userDict.Add("iphone", "アイフォーン");
        var reg = new Keyboard(userDictionary: userDict);
        reg.Type("iphone ");
        Assert.Equal("アイフォーン|iPhone|iphone", string.Join("|", reg.Host.View!.Candidates.Take(3)), "登録した読みの英語候補も正式表記が先");

        // 予測変換 (Tab + Enter) と自動判定 OFF の英字提案でも同じ表記になる
        foreach (var (typed, expected) in new[] { ("githu", "GitHub"), ("iphon", "iPhone"), ("javascrip", "JavaScript"), ("typescrip", "TypeScript"), ("uiki", "UIKit") })
        {
            var pred = new Keyboard(predictor: new Predictor(null, null, null));
            pred.Type(typed);
            pred.Press(VirtualKeys.Tab);
            pred.Type("\n");
            Assert.Equal(expected, pred.Host.Document, $"予測: {typed}");
        }
        var manual = new Keyboard { Manual = true };
        manual.Type("github");
        Assert.True(manual.Host.View?.Hint?.StartsWith("Tab → GitHub (英字に)", StringComparison.Ordinal) == true, manual.Host.View?.Hint ?? "");

        // F9 (全角英数) や F10 2 回 (すべて大文字) で確定しても、小文字としては学習しない
        var f9 = new Keyboard();
        f9.Type("github");
        f9.Press(VirtualKeys.F9);
        f9.Type("\n");
        Assert.Equal("ｇｉｔｈｕｂ", f9.Host.Document);
        f9.Type("github");
        Assert.Equal("GitHub", f9.Showing, "F9 で全角にした後は小文字として学習しない");

        var f10Upper = new Keyboard();
        f10Upper.Type("github");
        f10Upper.Press(VirtualKeys.F10);
        f10Upper.Press(VirtualKeys.F10);
        f10Upper.Type("\n");
        Assert.Equal("GITHUB", f10Upper.Host.Document);
        f10Upper.Type("github");
        Assert.Equal("GitHub", f10Upper.Showing, "F10 で大文字にした後は小文字として学習しない");

        // F10 で小文字にして確定した語は、次から所有格・再変換も含め小文字のままにする
        var f10 = new Keyboard();
        f10.Type("github");
        f10.Press(VirtualKeys.F10);
        f10.Type("\n");
        Assert.Equal("github", f10.Host.Document);
        f10.Type("github");
        Assert.Equal("github", f10.Showing, "F10 で小文字を選んだ後は小文字のまま");
        f10.Type("'s");
        Assert.Equal("github's", f10.Showing, "小文字を覚えた語は所有格も小文字のまま");
        f10.Type("\n");
        f10.Host.Selection = new ReconversionSelection("github wo miru", "github wo miru");
        f10.Press(VirtualKeys.Convert);
        Assert.Equal("github をみる", f10.Showing, "小文字を覚えた語は再変換でも小文字のまま");

        // 変換候補で小文字を選んだ語も、次から小文字のままにし、正式表記を選び直したときだけ戻す
        var choice = new Keyboard();
        choice.Type("iphonede");
        choice.Press(VirtualKeys.Left); // で
        choice.Press(VirtualKeys.Left); // iPhone
        choice.Type(" ");               // iphone
        choice.Type("\n");
        Assert.Equal("iphoneで", choice.Host.Document);
        choice.Type("iphonede");
        Assert.Equal("iphoneで", choice.Showing, "候補で小文字を選んだ後は小文字のまま");
        choice.Press(VirtualKeys.Left); // で
        choice.Press(VirtualKeys.Left); // iphone
        choice.Type("   ");             // iPhone → Iphone → IPHONE
        Assert.Equal("IPHONE", choice.Host.View!.Clauses![0]);
        choice.Type("\n");
        choice.Type("iphonede");
        Assert.Equal("iphoneで", choice.Showing, "全大文字を選んでも小文字の学習は消さない");
        choice.Press(VirtualKeys.Left); // で
        choice.Press(VirtualKeys.Left); // iphone
        choice.Type(" ");               // iPhone
        choice.Type("\n");
        choice.Type("iphone");
        Assert.Equal("iPhone", choice.Showing, "候補で正式表記を選び直したら戻る");

        // ユーザーの propernouns.txt は同梱の表記を上書きでき、追加した語も自動で直す
        var userDir = Path.Combine(Path.GetTempPath(), $"meltype-proper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(userDir);
        try
        {
            File.WriteAllText(Path.Combine(userDir, "propernouns.txt"), "github MyApp");
            var nouns = ProperNouns.Load(userDir);
            Assert.Equal(null, nouns.AutoCanonical("github"), "ユーザーの propernouns.txt で小文字に上書きできる");
            Assert.Equal("MyApp", nouns.AutoCanonical("myapp"), "ユーザーの propernouns.txt で追加した語も自動で直す");
        }
        finally
        {
            Directory.Delete(userDir, recursive: true);
        }
    }

    [Test]
    public static void Ambiguous_UsesBothSides()
    {
        (string? Before, string? After, string Expected)[] cases =
        [
            ("I love ", null, "sushi"),      // 前が英語
            (null, " is great", "sushi"),    // 後ろが英語
            ("I love ", " is great", "sushi"),
            ("今日は", null, "すし"),         // 前が日本語
            (null, "が好き", "すし"),         // 後ろが日本語
            (null, null, "すし"),             // 分からなければ日本語
        ];
        foreach (var (before, after, expected) in cases)
        {
            var k = new Keyboard();
            k.Host.PrecedingText = before;
            k.Host.FollowingText = after;
            k.Type("sushi");
            Assert.Equal(expected, k.Showing, $"前=「{before}」 後ろ=「{after}」");
        }
    }

    [Test]
    public static void Conversion_UsesTextBeforeCaretAsContext()
    {
        var k = new Keyboard();
        k.Host.PrecedingText = "この本は";
        k.Type("atsui ");
        Assert.Equal("この本は", k.Converter.LastContext, "キャレットの前の日本語を変換エンジンに文脈として渡す");
    }

    [Test]
    public static void Conversion_ContextRulesIke()
    {
        var rules = ContextRules.Load(null);
        Assert.Equal("行け", ChooseKana(rules, "いけ", "ねむ学校"), "学校いけ → 行け (池にしない)");
        Assert.Equal("行けよ", ChooseKana(rules, "いけよ", "早く"), "助詞付きでも 行け");
        Assert.Equal("池", ChooseKana(rules, "いけ", "公園の"), "公園なら 池");
        Assert.Equal(null, ChooseKana(rules, "いけん", "学校の"), "意見 を 行けん にしない");
        Assert.Equal(null, ChooseKana(rules, "いけない", "学校で"), "いけない を 行けない にしない");
    }

    /// <summary>文脈の手がかり辞書を、変換エンジンがかなのまま返した文節に使う (残りの前の部分は先頭部分の読みのまま)。</summary>
    private static string? ChooseKana(ContextRules rules, string reading, string surrounding) =>
        rules.Choose(reading, surrounding, reading, head => head);

    /// <summary>先頭部分だけを変換した結果 (変換エンジンの代わり)。</summary>
    private static string? ConvertStem(string head) => head switch
    {
        "かく" => "書く",
        "かわ" => "川",
        "かち" => "勝ち",
        "はし" => "橋",
        _ => null,
    };

    [Test]
    public static void Conversion_ContextRulesDoNotReplacePartOfAnotherWord()
    {
        var rules = ContextRules.Load(null);
        foreach (var surrounding in new[] { "メール", "コード", "メール。" })
        {
            foreach (var (reading, engine) in new[]
            {
                ("かくにん", "確認"), ("かくてい", "確定"), ("かくにんを", "確認を"), ("かくにんしたら", "確認したら"),
                ("かくていして", "確定して"), ("かくとう", "格闘"), ("かくりつ", "確率"),
            })
                Assert.Equal(null, rules.Choose(reading, surrounding, engine, ConvertStem), $"{surrounding}: {reading} ({engine}) の途中を 書く にしない");
        }
        foreach (var (reading, surrounding, engine) in new[] { ("はしる", "ご飯", "走る"), ("かわいい", "財布", "可愛い"), ("かみなり", "神社", "雷"), ("いしき", "病院", "意識") })
            Assert.Equal(null, rules.Choose(reading, surrounding, engine, ConvertStem), $"{surrounding}: {reading} も語の途中で補正しない");
    }

    [Test]
    public static void Conversion_ContextRulesDoNotReplaceVerbStems()
    {
        // 送り仮名が助詞の頭の文字 (し・さ・っ・す・せ) で始まる動詞も、語の途中で置き換えない (#1352 のレビュー)。
        // 変換エンジンの 隠 / 変わ / 交わ は、かく / かわ の変換ではない。
        var rules = ContextRules.Load(null);
        foreach (var (reading, surrounding, engine) in new[]
        {
            ("かくして", "メール", "隠して"), ("かくした", "メール", "隠した"), ("かくさない", "メール", "隠さない"),
            ("かわった", "泳ぐ", "変わった"), ("かわす", "泳ぐ", "交わす"), ("かわせる", "泳ぐ", "交わせる"),
        })
            Assert.Equal(null, rules.Choose(reading, surrounding, engine, ConvertStem), $"{surrounding}: {reading} ({engine}) の途中を置き換えない");
    }

    [Test]
    public static void Conversion_ContextRulesKeepOpenEndedParticles()
    {
        // 語尾は列挙しない。変換エンジンが 先頭部分の語 + 残りのかな と区切っていれば補正する (#1352 のレビュー)。
        var rules = ContextRules.Load(null);
        foreach (var (reading, surrounding, engine, expected) in new[]
        {
            ("はしには", "ご飯", "橋には", "箸には"), ("かわでは", "泳ぐ", "皮では", "川では"),
            ("あついので", "気温", "厚いので", "暑いので"), ("あついけど", "気温", "厚いけど", "暑いけど"), ("あついよね", "気温", "厚いよね", "暑いよね"),
            ("かくけど", "メール", "描くけど", "書くけど"), ("しようでした", "API", "使用でした", "仕様でした"),
            ("せんたくしてる", "服", "選択してる", "洗濯してる"), ("しょうめいされる", "部屋", "証明される", "照明される"),
            // 規則に無い語でも、先頭部分だけを変換した結果なら同じ区切り (勝ち|で)
            ("かちで", "終わってる", "勝ちで", "ガチで"),
        })
            Assert.Equal(expected, rules.Choose(reading, surrounding, engine, ConvertStem), $"{surrounding}: {reading} ({engine}) を補正する");
    }

    [Test]
    public static void Conversion_ContextRulesKeepParticlesAndInflections()
    {
        var rules = ContextRules.Load(null);
        foreach (var (reading, surrounding, engine, expected) in new[]
        {
            ("かく", "メール", "核", "書く"), ("かく", "イラスト", "書く", "描く"), ("かくよ", "メール", "描くよ", "書くよ"),
            ("はしを", "ご飯", "橋を", "箸を"), ("かわから", "泳ぐ", "皮から", "川から"), ("おんげーしか", "ACの曲", "おんげーしか", "音ゲーしか"),
            ("かいとうして", "質問", "解答して", "回答して"), ("しようです", "API", "使用です", "仕様です"),
        })
            Assert.Equal(expected, rules.Choose(reading, surrounding, engine, ConvertStem), $"{surrounding}: {reading} ({engine}) の補正は残す");
        Assert.Equal(null, ChooseKana(rules, "いけないよ", "学校で"), "長い規則に手がかりがなくても、短い いけ に戻らない");
        rules.AddText("かくにん 確認 : メール");
        Assert.Equal("確認を", rules.Choose("かくにんを", "メール", "確認を", ConvertStem), "ユーザーの長い規則を優先する");
        Assert.Equal(null, rules.Choose("かくにんを", "コード", "確認を", ConvertStem), "長い規則に手がかりがなければ変換エンジンに任せる");
    }

    [Test]
    public static void Conversion_ContextRulesPreserveKakuninAndKakutei()
    {
        // #348 のコメント: メール・コードの文脈でも「確認」「確定」を変換エンジンの結果から壊さない。
        foreach (var live in new[] { false, true })
        {
            foreach (var context in new[] { "メールを", "コードで" })
            {
                foreach (var (typed, expected) in new[]
                {
                    ("kakuninn", "確認"), ("kakutei", "確定"),
                    ("kakuninshitara", "確認したら"), ("kakuteishite", "確定して"),
                })
                {
                    var k = new Keyboard(live: live);
                    k.Host.PrecedingText = context;
                    k.Type(typed + " \n");
                    Assert.Equal(expected, k.Host.Document, $"live={live}, {context}: {typed}");
                }
            }
        }
    }

    [Test]
    public static void Conversion_ContextRulesPickTheRightWord()
    {
        var k = new Keyboard();
        k.Host.PrecedingText = "今日の気温は";
        k.Type("atsui ");
        Assert.Equal("暑い", k.Host.View!.Clauses![0], "気温 が前にあれば あつい → 暑い");

        k = new Keyboard();
        k.Host.PrecedingText = "財布の";
        k.Type("kawa ");
        Assert.Equal("革", k.Host.View!.Clauses![0], "財布 が前にあれば かわ → 革");

        k = new Keyboard();
        k.Type("kawa ");
        Assert.Equal("川", k.Host.View!.Clauses![0], "手がかりが無ければ変換エンジンの結果");
    }

    [Test]
    public static void Conversion_LearnsUserChoice()
    {
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history);
        k.Type("hashiwo ");
        var candidates = k.Host.View!.Candidates;
        var index = candidates.ToList().IndexOf("箸を");
        Assert.True(index > 0, string.Join(",", candidates));
        for (var i = 0; i < index; i++) k.Type(" ");
        k.Type("\n");
        Assert.Equal("箸を", k.Host.Output.Single());

        k = new Keyboard(history: history);
        k.Type("hashiwo ");
        Assert.Equal("箸を", k.Host.View!.Clauses![0], "前に選び直した変換が最初の候補になる");
    }

    [Test]
    public static void LearningOff_RemembersNothing()
    {
        // #277: 設定「ユーザー学習を使う」が OFF なら、選び直した変換も、F10 で英字に直した語も覚えない
        var history = new ConversionHistory(null);
        var languages = new LanguageMemory(null);
        var k = new Keyboard(history: history, languages: languages) { Learning = false };
        k.Type("hashiwo ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("箸を");
        for (var i = 0; i < index; i++) k.Type(" ");
        k.Type("\n");
        Assert.Equal("箸を", k.Host.Output.Single());
        k.Type("api");
        k.Press(VirtualKeys.F10);
        k.Type("\n");
        Assert.Equal(0, history.Count, "変換を覚えない");
        Assert.Equal(0, languages.Entries().Count, "英字に直した語を覚えない");
    }

    [Test]
    public static void LearningOff_DoesNotUseLearnedData()
    {
        // 設定「ユーザー学習を使う」が OFF なら、今まで覚えた分も使わない。消しはしないので、ON に戻すとまた使う
        var history = new ConversionHistory(null);
        history.Remember("はしを", "箸を");
        var languages = new LanguageMemory(null);
        languages.Remember("tao", english: true, explicitChoice: true);
        languages.Remember("google", english: false, explicitChoice: true);
        var phrases = new PhraseHistory(null);
        phrases.Remember("にほんごでにゅうりょく", "日本語で入力");
        var dictionary = TranslationDictionary.Parse("川\tn\triver,stream");
        var translations = new TranslationHistory(null);
        translations.Remember("かわ", "stream");
        var predictor = new Predictor(phrases, () => [("にほんごかいわ", "日本語会話")], history);
        Keyboard Create(bool learning) => new(history: history, languages: languages, predictor: predictor, translations: dictionary, translationHistory: translations) { Learning = learning };
        string Typed(bool learning, string keys)
        {
            var k = Create(learning);
            k.Type(keys);
            return k.Showing ?? "";
        }
        List<string> Candidates(bool learning, string keys)
        {
            var k = Create(learning);
            k.Type(keys);
            return k.Host.View!.Candidates.ToList();
        }
        List<string> Predictions(bool learning, string keys)
        {
            var k = Create(learning);
            k.Type(keys);
            return k.Host.View!.Predictions?.ToList() ?? [];
        }
        Assert.Equal("箸を", Candidates(true, "hashiwo ")[0], "ON: 選び直した変換");
        Assert.True(Candidates(false, "hashiwo ")[0] != "箸を", "OFF: 選び直した変換");
        Assert.Equal("tao", Typed(true, "tao"), "ON: 英字に直した語");
        Assert.Equal("たお", Typed(false, "tao"), "OFF: 英字に直した語");
        Assert.True(Typed(true, "google") != "google", "ON: かなに直した語");
        Assert.Equal("google", Typed(false, "google"), "OFF: かなに直した語");
        Assert.True(Predictions(true, "nihongo").Contains("日本語で入力"), "ON: 予測変換の語句");
        Assert.True(!Predictions(false, "nihongo").Contains("日本語で入力"), "OFF: 予測変換の語句");
        Assert.True(Predictions(false, "nihongo").Contains("日本語会話"), "OFF でもユーザー辞書の語は予測する");
        int Place(bool learning) => Candidates(learning, "kawa ").IndexOf("stream");
        Assert.True(Place(true) < Place(false), $"選んだ英訳: ON {Place(true)} / OFF {Place(false)}");
        Assert.Equal("箸を", history.Get("はしを"), "OFF で打っても消えない");
        Assert.Equal(true, languages.Get("tao"), "OFF で打っても消えない");

        // 同じ入力欄で ON に戻したら、次の入力から使う
        var toggled = Create(learning: false);
        toggled.Type("tao\n");
        toggled.Learning = true;
        toggled.Type("tao");
        Assert.Equal("tao", toggled.Showing, "ON に戻すと使う");
    }

    [Test]
    public static void CapitalI_IsEnglish()
    {
        // 報告: I want の I が「い」になる。
        var k = new Keyboard();
        k.Type("I");
        Assert.Equal("I", k.Showing, "大文字 1 文字でも英語");
        k.Type(" want ");
        Assert.Equal("I want ", k.Host.Document);
    }

    [Test]
    public static void AutoCorrect_JapaneseToEnglishAfterCommit()
    {
        // i を Space で変換して確定した後に、はっきり英語の語 (want) が続いたら「I 」に確定し直す。
        var k = new Keyboard();
        k.Type("i want ");
        Assert.Equal("I want ", k.Host.Document, string.Join("|", k.Host.Events));
    }

    [Test]
    public static void AutoCorrect_NotToEnglishWithoutSpaceBetween()
    {
        // Enter で確定した日本語の語の後ろに英語の語を続けても、英語に確定し直さない (英語にすると、空白なしで前の語とつながる)
        foreach (var (typed, expected) in new[] { ("are\nON ", "あれON "), ("i\nwant ", "いwant "), ("sushi\nPR\n", "すしPR") })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed.Replace("\n", "⏎"));
            Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), "消さない: " + string.Join(" ", k.Host.Events));
        }
        // Space で区切ったときは今までどおり直す
        var spaced = new Keyboard();
        spaced.Type("are ON ");
        Assert.Equal("are ON ", spaced.Host.Document);
    }

    [Test]
    public static void AutoCorrect_KeepsSuruFormBeforeEnglish()
    {
        // site → して のあと英字 (PR) が続いても、する活用を英単語 site に確定し直さない。
        foreach (var (typed, expected) in new[]
        {
            ("sitePR\n", "してPR"),
            ("site PR\n", "して PR"), // Space で変換開始していた空白は残す
            ("sitaOK\n", "したOK"),
            ("pushsitePR\n", "pushしてPR"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed.Trim());
        }
        // sushi など、する活用でない曖昧語は、後ろが英語なら今までどおり英語に直す。
        var sushi = new Keyboard();
        sushi.Type("sushi PR\n");
        Assert.Equal("sushi PR", sushi.Host.Document);
    }

    [Test]
    public static void AutoCorrect_EnglishToJapaneseAfterCommit()
    {
        var k = new Keyboard();
        k.Host.PrecedingText = "I love ";
        k.Type("sushi ");
        Assert.Equal("sushi ", k.Host.Document, "前が英語なので英語で確定");
        k.Host.PrecedingText = null;
        k.Type("gasuki\n");
        Assert.Equal("すしがすき", k.Host.Document, "後ろに日本語が続いたので日本語に確定し直す");
    }

    [Test]
    public static void AutoCorrect_KeepsEnglishWordInsideEnglishSentence()
    {
        // #265: 英文の中の普通の英単語 (Are you の you) は、後ろに日本語 (すし？) が続いても よう に確定し直さない。
        // (#258 の Are yoよう人間？ も、この確定し直しで起きていた)
        foreach (var (typed, expected) in new[] { ("Are you susi?\n", "Are you すし？"), ("Are you ningen?\n", "Are you にんげん？"), ("Are you susi\n", "Are you すし") })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed);
            Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal) || e.StartsWith("replace", StringComparison.Ordinal)), "消さない: " + string.Join(" ", k.Host.Events));
        }
        // 先頭の you は今までどおり、後ろの日本語に合わせて直す
        var start = new Keyboard();
        start.Type("you ninaru\n");
        Assert.Equal("ようになる", start.Host.Document);
    }

    [Test]
    public static void AutoCorrect_SendsReplacementTogetherWithNextText()
    {
        // #258: 確定し直し (消して すし を入れ直す) と、続けて確定する がすき を 1 回で送る
        // (別々に送ると、その間で文字の順番が入れ替わるアプリがある: Are yoよ人間う？)
        var k = new Keyboard();
        k.Host.PrecedingText = "I love ";
        k.Type("sushi ");
        k.Host.PrecedingText = null;
        k.Host.Events.Clear();
        k.Type("gasuki\n");
        Assert.Equal("すしがすき", k.Host.Document);
        var edits = k.Host.Events.Where(e => e.StartsWith("bs:", StringComparison.Ordinal) || e.StartsWith("text:", StringComparison.Ordinal)).ToList();
        Assert.Equal("bs:6 text:すしがすき", string.Join(" ", edits));
    }

    [Test]
    public static void AutoCorrect_NotWhenHostCannotDelete()
    {
        // #124: 確定済みの文字を消せない入力欄 (Linux で周りの文字に対応していないアプリ) では確定し直さない (sushi が残って すし が足されないように)
        var k = new Keyboard();
        k.Host.CanDeleteBackward = false;
        k.Host.PrecedingText = "I love ";
        k.Type("sushi ");
        k.Host.PrecedingText = null;
        k.Type("gasuki\n");
        Assert.Equal("sushi がすき", k.Host.Document);
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), "消さない");
    }

    [Test]
    public static void AutoCorrect_KeepsExplicitlyChosenLanguage()
    {
        // #124: F10 で英字にして確定した語 (sushi) は、後ろに日本語が続いても かな に確定し直さない
        var k = new Keyboard(languages: new LanguageMemory(null));
        k.Type("sushi");
        k.Press(VirtualKeys.F10);
        k.Type("\n");
        k.Type("tte\n");
        Assert.Equal("sushiって", k.Host.Document, string.Join("|", k.Host.Events));
    }

    [Test]
    public static void AutoCorrect_KeepsEnglishWordThatIsNotJapanese()
    {
        // #121: issue を確定した後に たてた と続けても、issue を いっすえ に確定し直さない (いっすえ は日本語の語ではない)
        // #124: api の後の って で、api を あぴ にしない
        foreach (var (word, next, expected) in new[] { ("issue", "tateta", "issueたてた"), ("api", "tte", "apiって") })
        {
            var k = new Keyboard();
            k.Host.PrecedingText = "I love ";
            k.Type(word + "\n");
            k.Host.PrecedingText = null;
            k.Type(next + "\n");
            Assert.Equal(expected, k.Host.Document, string.Join("|", k.Host.Events));
        }
    }

    [Test]
    public static void AutoCorrect_NotAfterCaretMoved()
    {
        var k = new Keyboard();
        k.Type("i ");
        k.Type("w");
        k.Click(); // 入力欄をクリックした
        k.Type("ant ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), "キャレットが動いたかもしれないので消さない");
    }

    [Test]
    public static void AutoCorrect_NotWhenCaretMovedOnMac()
    {
        // 報告 (#137): 確定した後に別の場所をクリックしてから英語の語を確定すると、前の語ではなく
        // 「今のキャレットの前の文字」が消される。Mac はクリックを知らされない (ForgetLastCommit が呼ばれない) ので、
        // 消す前に「直前に確定した語が今のキャレットの位置にあるか」を入力欄に確かめる。
        var stays = new Keyboard();
        stays.Type("i ");
        stays.Press(VirtualKeys.Return);
        stays.Type("want ");
        Assert.True(stays.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), $"キャレットが前の語の後ろにあれば直す: {string.Join("|", stays.Host.Events)}");
        var k = new Keyboard();
        k.Type("i ");
        k.Press(VirtualKeys.Return);
        k.Host.PrecedingText = "ほかのところ"; // 確定した後で別の場所をクリックした (クリックを知らされない)
        k.Type("want ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), $"前の語が今のキャレットの位置に無いので消さない: {string.Join("|", k.Host.Events)}");
    }

    [Test]
    public static void AutoCorrect_StillCorrectsWhenCaretStaysAtTheWord()
    {
        // Mac (入力欄の文字をその場で読める) でも、キャレットの直前に前の語があるなら今までどおり直す。
        var k = new Keyboard();
        k.Type("i want ");
        Assert.Equal("I want ", k.Host.Document, string.Join("|", k.Host.Events));
    }

    [Test]
    public static void AutoCorrect_NotWhenUserChoseCandidate()
    {
        var k = new Keyboard();
        k.Type("i  "); // 2 回目の Space で候補を選び直した
        k.Type("want ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), "自分で選んだ変換は直さない");
    }

    [Test]
    public static void Symbols_StartComposition()
    {
        // 報告: かぎかっこが入力できない。
        var cases = new Dictionary<string, string>
        {
            ["[kagi]"] = "「かぎ」", ["-"] = "ー", ["/"] = "/", ["z/"] = "・", ["#"] = "#", ["("] = "(", [")"] = ")", ["]"] = "」", ["@"] = "@", [",,,"] = "...", ["\\"] = "￥", [","] = "、",
            // 報告: Shift で打つ記号が全角で打てない、/ が打てない。英語の中では半角のまま。
            ["$%&"] = "＄％＆", ["kyouha(tenki)"] = "きょうは(てんき)", ["hello@example"] = "hello@example",
            ["tetr.io"] = "tetr.io", ["Wakatte.TV"] = "Wakatte.TV", ["J-core"] = "J-core", ["p-hub"] = "p-hub", ["talk-admin"] = "talk-admin",
            // 報告 (#1349): かなの後ろの子音だけの語の後の - が長音になる (きょうはptーquery-digest)。ん (n-to・nn-to) の後は長音
            ["pt-query-digest"] = "pt-query-digest",
            ["kyouhapt-query-digest"] = "きょうはpt-query-digest",
            ["kyouhals-files"] = "きょうはls-files",
            ["n-to"] = "んーと",
            ["nn-to"] = "んーと",
            // 報告: ca / cu / co で か く こ
            ["cacuco"] = "かくこ", ["iijane"] = "いいじゃね", ["shoucanshi"] = "しょうかんし", ["vanpaia"] = "ゔぁんぱいあ", ["wyiwye"] = "ゐゑ", ["GPL3.0"] = "GPL3.0", ["3.14desu"] = "3.14です", ["oknotasuku"] = "okのたすく",
            // Markdown 記法の ` を半角にする (#1301)。
            ["`"] = "`", ["```"] = "```", ["kyouha`made"] = "きょうは`まで",
        };
        foreach (var (typed, expected) in cases)
        {
            // 先頭の / $ @ の語 (#193) は SigilWord_* で確かめる。ここは変換ボックスに入れたときの記号。
            var k = new Keyboard { SigilWords = false };
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
        var ramen = new Keyboard();
        ramen.Type("ra-men\n");
        Assert.Equal("らーめん", ramen.Host.Document, "日本語の長音の打ち方は変えない");
    }

    [Test]
    public static void MarkdownMarker_SpaceCommitsHalfWidth()
    {
        // #1301: 見出し・リスト・引用の記号を、変換区間の先頭で Space を押すと半角のまま確定する。
        foreach (var (typed, committed) in new Dictionary<string, string>
        {
            ["-"] = "- ",
            [">"] = "> ",
            ["*"] = "* ",
            ["+"] = "+ ",
            ["#"] = "# ",
            ["##"] = "## ",
            ["######"] = "###### ",
        })
        {
            var k = new Keyboard();
            k.Type(typed + " ");
            Assert.Equal(committed, k.Host.Document, $"「{typed}」+ Space");
        }
    }

    [Test]
    public static void MarkdownMarker_EnterCommitsAsBefore()
    {
        foreach (var (typed, committed) in new Dictionary<string, string>
        {
            ["-"] = "ー",
            [">"] = "＞",
            ["*"] = "＊",
            ["+"] = "＋",
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(committed, k.Host.Document, $"「{typed}」+ Enter");
        }
    }

    [Test]
    public static void MarkdownMarker_FullWidthCandidatesViaTabAndShiftSpace()
    {
        // Space は半角の記号 + 空白で確定するので、全角の候補は Tab か Shift+Space で出す。
        foreach (var (marker, full) in new[] { ("#", "＃"), ("+", "＋") })
        {
            var k = new Keyboard(tabConversion: true);
            k.Type(marker);
            k.Press(VirtualKeys.Tab);
            Assert.True(k.Host.View?.Converting == true, $"Tab: 「{marker}」で変換が始まる");
            Assert.True(k.Host.View!.Candidates.Contains(full),
                $"Tab: 「{marker}」の候補に {full} がある: " + string.Join(",", k.Host.View.Candidates));

            k = new Keyboard();
            k.Type(marker);
            k.TypeKeys((VirtualKeys.Space, true));
            Assert.True(k.Host.View?.Converting == true, $"Shift+Space: 「{marker}」で変換が始まる");
            Assert.True(k.Host.View!.Candidates.Contains(full),
                $"Shift+Space: 「{marker}」の候補に {full} がある: " + string.Join(",", k.Host.View.Candidates));
        }
    }

    [Test]
    public static void MarkdownMarker_HalfWidthStars()
    {
        // #1301: 変換区間に * が 2 個以上あるときだけ、表示の ＊ を全部半角の * にする (強調 **bold**・*italic*)。
        foreach (var (typed, committed) in new Dictionary<string, string>
        {
            ["**"] = "**",
            ["****"] = "****",
            ["**kyou**"] = "**きょう**",
            ["*kyou*"] = "*きょう*",
            ["*italic*"] = "*italic*",
            ["*1*"] = "*1*",
            ["**aiueo**to**kakikukeko**"] = "**あいうえお**と**かきくけこ**",
            ["*chuui"] = "＊ちゅうい",
            ["kyou*"] = "きょう＊",
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(committed, k.Host.Document, $"「{typed}」");
        }
    }

    [Test]
    public static void MarkdownMarker_StarsSurviveSpaceCommit()
    {
        // Space で確定しても、表示と同じ半角のまま (Display を通らない確定の経路)
        foreach (var typed in new[] { "*italic* ", "**bold** " })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(typed, k.Host.Document, $"「{typed}」");
        }
    }

    [Test]
    public static void MarkdownMarker_SpaceConversionStartsWithHalfWidth()
    {
        // Space で変換したときも、最初の候補は入力中の表示と同じ半角
        foreach (var (typed, expected) in new Dictionary<string, string>
        {
            ["**kyou** "] = "**きょう**",
            ["*kyou* "] = "*きょう*",
            ["--- "] = "---",
            // 条件に当たらないものは、最初の候補が今までどおり
            ["*chuui "] = "＊ちゅうい",
            ["-- "] = "ーー",
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.View!.Candidates[0], $"「{typed}」の最初の候補");
        }

        // 全角の候補も残る
        var star = new Keyboard();
        star.Type("**kyou** ");
        Assert.True(star.Host.View!.Candidates.Contains("＊＊きょう＊＊"), "全角の候補が残る: " + string.Join(",", star.Host.View.Candidates));

        // Space で変換して Enter で確定しても、確定結果が半角のまま
        foreach (var (typed, expected) in new[]
        {
            ("**kyou** ", "**きょう**"),
            ("*kyou* ", "*きょう*"),
            ("--- ", "---"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            k.Press(VirtualKeys.Return); // Space の後に Enter
            Assert.Equal(expected, k.Host.Document, $"「{typed}」+ Space → Enter");
        }
    }

    [Test]
    public static void NotMarkdownMarker_WhenKanaBefore()
    {
        // かなのあとのマーカーは、Space で記号として確定せず、変換を始める。
        foreach (var marker in new[] { ">", "#" })
        {
            var k = new Keyboard();
            k.Type("kyou" + marker + " ");
            Assert.True(k.Host.View?.Converting == true, $"「kyou{marker}」+ Space");
            Assert.Equal("", k.Host.Document, $"「kyou{marker}」+ Space");
        }
    }

    [Test]
    public static void NotMarkdownMarker_TooManyHashes()
    {
        // 見出しは 6 段まで。7 つは確定せず (変換に回る)。
        var k = new Keyboard();
        k.Type("#######");
        k.Type(" ");
        Assert.True(k.Host.View?.Converting == true, "Space で変換を始める");
        Assert.Equal("", k.Host.Document, "見出しとして確定しない");
    }

    [Test]
    public static void MarkdownMarker_HorizontalRule()
    {
        // #1301: 3 個以上の - だけの入力は、水平線・front matter の区切りとして打った半角のまま表示・確定する。
        foreach (var (typed, committed) in new Dictionary<string, string>
        {
            ["---"] = "---",
            ["--"] = "ーー",
            ["----"] = "----",
            ["aa---"] = "ああーーー",   // かなのあとの - は長音のまま
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(committed, k.Host.Document, $"「{typed}」+ Enter");
        }
    }

    [Test]
    public static void DotSuffix_FileExtensionsStayEnglish()
    {
        // 報告 (#260・#254): 英語の語.拡張子 の拡張子がひらがなになる (MeltypeTip.dっl、Meltype.coんふぃg、Meltype.ご。mod)
        var cases = new Dictionary<string, string>
        {
            ["MeltypeTip.dll"] = "MeltypeTip.dll", ["Meltype.aab"] = "Meltype.aab", ["Meltype.config"] = "Meltype.config", ["Meltype.index"] = "Meltype.index",
            ["Meltype.ini"] = "Meltype.ini", ["Meltype.go.mod"] = "Meltype.go.mod", ["Meltype.abi.json"] = "Meltype.abi.json", ["Meltype.so.1"] = "Meltype.so.1",
            ["Meltype.e57"] = "Meltype.e57", ["Meltype.cargo/config.toml"] = "Meltype.cargo/config.toml", ["Meltype.db-journal"] = "Meltype.db-journal",
            // 表示で英字にした拡張子を、確定でローマ字の打ち間違いとして直さない (amr → あめ、bas → ば)
            ["Meltype.amr"] = "Meltype.amr", ["Meltype.bas"] = "Meltype.bas", ["Meltype.ann"] = "Meltype.ann",
            // issue #462: ローマ字読める語尾 (llama → っぁま) + 読めない拡張子は英字スパンとしてまとめる
            ["llama.cpp"] = "llama.cpp",
            // 長い名前の途中の助詞・ドメインの頭では分けない
            ["Meltype.gitignore"] = "Meltype.gitignore", ["Meltype.gcno"] = "Meltype.gcno", ["Meltype.automount"] = "Meltype.automount",
            // 先頭のドットファイルも英字のまま (issue #431: 。ぎちgのれ にしない)
            [".gitignore"] = ".gitignore", [".env"] = ".env", [".dll"] = ".dll", [".json"] = ".json", [".go"] = ".go",
            // 拡張子・ドメインの後ろの助詞からは日本語
            ["MeltypeTip.dllwokesu"] = "MeltypeTip.dllをけす", ["setup.exewojikkou"] = "setup.exeをじっこう", ["github.comnipush"] = "github.comにpush",
            ["tetr.iode"] = "tetr.ioで", ["google.comdekensaku"] = "google.comでけんさく",
            // 日本語の語と同じ綴りの固有名詞 (Wakatte) のドメインは、小文字で打っても英字
            ["wakatte.tv"] = "wakatte.tv",
            ["wakatte.tvdemiru"] = "wakatte.tvでみる",
            ["wakatte."] = "わかって。",
            // 版の番号のプレリリースの名前は英字。x.y.z でない数字の後ろの - は のばす音
            ["ver1.4.1-pre.1"] = "ver1.4.1-pre.1",
            ["v1.10.0-rc.1"] = "v1.10.0-rc.1",
            ["1.4.1-beta"] = "1.4.1-beta",
            ["ver1.4.1-pre.1wodashita"] = "ver1.4.1-pre.1をだした",
            ["1.5-desu"] = "1.5ーです",
            // 名前の後ろの日本語。決まった名前でない語 (desu) は のばす音 + 日本語
            ["ver1.4.1-prewodashita"] = "ver1.4.1-preをだした",
            ["v1.2.3-alphawotameshita"] = "v1.2.3-alphaをためした",
            ["v1.2.3-rcnitsuite"] = "v1.2.3-rcについて",
            ["v1.2.3-alpha.2wodashita"] = "v1.2.3-alpha.2をだした",
            ["v1.2.3-preview"] = "v1.2.3-preview",
            ["v1.2.3-desu"] = "v1.2.3ーです",
            // 読めない拡張子の後ろの、(助詞 +) 3 音以上の日本語の語からは日本語
            [".envfairu"] = ".envふぁいる",
            [".zshrcnosettei"] = ".zshrcのせってい",
            [".gitignorefairu"] = ".gitignoreふぁいる",
            ["MeltypeTip.dllfairu"] = "MeltypeTip.dllふぁいる",
            [".jsonwoyomu"] = ".jsonをよむ",
            // 句点の後ろの、ドメインと同じ綴りで始まる日本語 (。では・。でも)。前が長い文ほど日本語 (issue #1332)
            ["ryoukaidesu.deha"] = "りょうかいです。では",
            ["arigatougozaimasu.dehaonegaishimasu"] = "ありがとうございます。ではおねがいします",
            ["junbigadekita.dehashuppatsushiyou"] = "じゅんびができた。ではしゅっぱつしよう",
            ["kyouhaame.demo"] = "きょうはあめ。でも",
            ["hoge.io"] = "hoge.io",
            ["hogehoge.io"] = "hogehoge.io",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, $"「{typed}」");
        }
        // 最後までローマ字として読める語は日本語のまま (ドメインの de を英字にして deす にしていた)
        var desu = new Keyboard();
        desu.Type("Meltype.desu\n");
        Assert.True(desu.Host.Document.EndsWith("です", StringComparison.Ordinal), desu.Host.Document);
    }

    [Test]
    public static void ReadableToolNames_StayEnglish()
    {
        // 報告 (#301 / #457): おぺんこで・tむx に割れず、ツール名のまま英字にする
        // discode は固有名詞でないので discoで (#145 メンテナー判断)
        var cases = new Dictionary<string, string>
        {
            ["discode"] = "discoで",
            ["opencode"] = "opencode",
            ["tmux"] = "tmux",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, $"「{typed}」");
        }
    }

    [Test]
    public static void ReportedWords_AreCandidates()
    {
        // 報告 (#968): かんそうりつ の候補に 乾燥率
        Assert.True(CandidateDictionary.Load(null).Lookup("かんそうりつ").Contains("乾燥率"), "かんそうりつ → 乾燥率");
        Assert.True(CandidateDictionary.Load(null).Lookup("ぶるあか").Contains("ブルーアーカイブ"), "ぶるあか → ブルーアーカイブ (#1106)");
        Assert.True(CandidateDictionary.Load(null).Lookup("ー＞").Contains("→"), "ー＞ → → (#1135)");
        Assert.True(CandidateDictionary.Load(null).Lookup("みかんせい").Contains("蜜柑星"), "みかんせい → 蜜柑星 (#1155)");
        Assert.True(CandidateDictionary.Load(null).Lookup("ぶいすぽ").Contains("ぶいすぽっ！"), "ぶいすぽ → ぶいすぽっ！ (#1286)");
    }

    [Test]
    public static void Symbols_HalfWidthCandidate()
    {
        // 報告: Space を続けて押して、記号も半角で出せるように。
        var k = new Keyboard();
        k.Type("( ");
        Assert.True(k.Host.View!.Candidates.Contains("("), "（ の候補に ( がある: " + string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void CapitalizedWord_FollowedByJapanese()
    {
        // 報告: 今日はAutoIMEnotesutowosimasu が全部英字になる。大文字で始まる語の後ろの日本語は日本語にする。
        var cases = new Dictionary<string, string>
        {
            ["AutoIMEnotesuto"] = "AutoIMEのてすと", ["Githubdekaku"] = "Githubでかく", ["OKdesu"] = "OKです",
            ["Tokyo"] = "Tokyo", ["Hello"] = "Hello",
            // 報告: I don't → どん't。短縮形は英語。
            ["don't"] = "don't", ["I'm"] = "I'm",
            // 報告: TSユーザー → Tシューざー、issue → いっすえ
            ["TSyu-za-"] = "TSゆーざー", ["issue"] = "issue",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
    }

    [Test]
    public static void Numbers_StayHalfWidth()
    {
        var k = new Keyboard();
        k.Type("2025");
        Assert.Equal("2025", k.Showing, "数字だけなら半角のまま");
        k.Type(" ");
        // 報告: 1 だけで変換しても ① などが出ない。日本語の中・文の頭では Space で変換して候補を出す (1 番目は半角の数字のまま)。
        Assert.True(k.Host.View is { Converting: true } view && view.Clauses![0] == "2025", "数字だけでも Space で変換する");
        k = new Keyboard();
        k.Host.PrecedingText = "I have ";
        k.Type("2 ");
        Assert.Equal("2 ", k.Host.Output.Single(), "英文の中の数字は Space で確定して空白");

        k = new Keyboard();
        k.Type("3ji");
        Assert.Equal("3じ", k.Showing, "数字の後にかなが続けば日本語");
        Assert.Equal("2025年10月", TextChars.NormalizeHalfWidth("２０２５年１０月", "2025ねん10がつ"), "変換エンジンが全角にした数字は半角に戻す");
        Assert.Equal("１つ", TextChars.NormalizeHalfWidth("１つ", "ひとつ"), "読みに半角数字が無ければそのまま");
    }

    [Test]
    public static void UnitThenKanaThenNumber_KeepsTypedOrder()
    {
        // 数字・単位・かな・数字の順に打つと、かなが後ろの数字の後ろに回っていた (100mlnomizu2 → 100ml2のみず)
        foreach (var (typed, expected) in new[] { ("100mlnomizu2", "100mlのみず2"), ("3kmhashitte5", "3kmはしって5") })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, typed);
            k.Type("\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Candidates_ExpandWithWindowsCandidates()
    {
        var k = new Keyboard(moreCandidates: reading => reading == "かわ" ? ["川", "皮", "河", "革"] : []);
        k.Type("kawa");
        Assert.True(k.Host.View!.Candidates.Count == 0, "打っている間は候補を取りに行かない");
        // 報告: Space を 1 回押しただけでは候補が一部しか出ず、選び間違えやすい。最初から一覧をすべて出す。
        k.Type(" ");
        Assert.Equal("川", k.Host.View!.Clauses![0]);
        Assert.True(k.Host.View.Candidates.Contains("河") && k.Host.View.Candidates.Contains("革"), string.Join(",", k.Host.View.Candidates));
        k.Type(" ");
        Assert.Equal("皮", k.Host.View!.Clauses![0], "次の候補");
    }

    [Test]
    public static void Issue1016_PageKeysTurnCandidatePages()
    {
        // #1016: 変換中の PageDown / PageUp で候補の一覧のページを送る (アプリに通して確定しない)
        var many = Enumerable.Range(1, 20).Select(i => "候補" + i).ToArray();
        var k = new Keyboard(moreCandidates: reading => reading == "かわ" ? many : []);
        k.Type("kawa ");
        k.Press(VirtualKeys.PageDown);
        Assert.True(k.Host.View is { Converting: true }, "確定しない");
        var page2 = k.Host.View!.Clauses![0];
        Assert.Equal(k.Host.View.Candidates[9], page2, "次のページの最初の候補");
        k.Press(VirtualKeys.PageUp);
        Assert.Equal(k.Host.View!.Candidates[0], k.Host.View.Clauses![0], "前のページに戻る");
        k.Press(VirtualKeys.PageUp); // 最初のページの前は最後のページ
        Assert.Equal(k.Host.View!.Candidates[(k.Host.View.Candidates.Count - 1) / 9 * 9], k.Host.View.Clauses![0], "最後のページ");
        Assert.Equal("", k.Host.Document, "まだ確定しない");
    }

    [Test]
    public static void Candidates_GoConvertShowsEngineCandidates()
    {
        // #1015・#1088: go の候補が go ご だけで、語 碁 後 が出ない。go は英単語として見えるので、Space は英字で確定し、変換キーで ご を変換する
        var k = new Keyboard(moreCandidates: reading => reading == "ご" ? ["後", "語", "碁"] : []);
        k.Type("go");
        k.Press(VirtualKeys.Convert);
        Assert.True(k.Host.View!.Candidates.Take(4).SequenceEqual(["ご", "後", "語", "碁"]), string.Join(",", k.Host.View.Candidates));
        Assert.True(k.Host.View.Candidates.Contains("go"), "打ったままの英字も候補に残す: " + string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void Backspace_ThenRetype_RereadsRomaji()
    {
        // 報告: test の最後の t を消して u → 「てsう」になる。
        var k = new Keyboard();
        k.Type("test\bu");
        Assert.Equal("てす", k.Showing, "読めなかった s も次の文字と合わせて読み直す");
    }

    [Test]
    public static void LetterThenJapanese_IsNotWholeEnglish()
    {
        // 報告: sだけが と打つと sdakega になる。
        var k = new Keyboard();
        k.Type("sdakega");
        Assert.Equal("sだけが", k.Showing);
        Assert.Equal("stackoverflow", new Func<string?>(() => { var s = new Keyboard(); s.Type("stackoverflow"); return s.Showing; })(),
            "続きもローマ字として読めない語は英単語のまま");
    }

    [Test]
    public static void HalfWidth_IsKeptAfterConversion()
    {
        Assert.Equal("sだけが", TextChars.NormalizeHalfWidth("ｓだけが", "sだけが"));
        Assert.Equal("2025年", TextChars.NormalizeHalfWidth("２０２５年", "2025ねん"));
        Assert.Equal("ＡＢＣ", TextChars.NormalizeHalfWidth("ＡＢＣ", "えーびーしー"), "読みに英数字が無ければ全角のまま");
    }

    /// <summary>
    /// 読みの記号を別の文字にして返す変換エンジン (？ → ? や 「 → ［ を覚えた Mozc、？ を ? にする Zenzai の代わり)。
    /// split なら記号を 1 つずつの文節に区切り、そうでなければ読み全体を 1 つの文節で返す。
    /// </summary>
    private sealed class SymbolChangingConverter(bool split) : TestConverter
    {
        public override string? Convert(string hiragana, string? before) => Map(hiragana);

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) =>
            split ? System.Text.RegularExpressions.Regex.Matches(hiragana, @"[\p{L}\p{N}]+|.").Select(m => new ConversionClause(m.Value, Map(m.Value))).ToList() : [new(hiragana, Map(hiragana))];

        private static string Map(string reading) => reading
            .Replace("れいぶん", "例文").Replace("にほんご", "日本語").Replace("おもしろいね", "面白いね").Replace("きょうは", "今日は").Replace("わたしは", "私は")
            .Replace('？', '?').Replace('！', '!').Replace('(', '（').Replace('「', '［').Replace('」', '】').Replace('、', '・');
    }

    [Test]
    public static void TypedSymbols_StayAfterConversion()
    {
        // #519 #1669: 変換エンジン・Mozc の学習が記号を別の文字にしても、かなと一緒に打った記号は打ったときの文字 (CompositionText.Symbol) のまま
        foreach (var split in new[] { true, false })
        {
            foreach (var (keys, expected) in new[]
            {
                ("reibunn?", "例文？"), ("omosiroine!nihongo?", "面白いね！日本語？"), ("nihongo(reibunn)", "日本語(例文)"),
                ("[kyouha", "「今日は"), ("kyouha]", "今日は」"), ("watasiha,", "私は、"),
            })
            {
                var name = $"{keys} (split: {split})";
                var live = new Keyboard(live: true, converter: new SymbolChangingConverter(split));
                live.Type(keys);
                Assert.Equal(expected, live.Showing, name + ": ライブ変換");
                live.Type("\n");
                Assert.Equal(expected, live.Host.Document, name + ": ライブ変換のまま確定");

                var space = new Keyboard(converter: new SymbolChangingConverter(split));
                space.Type(keys + " ");
                Assert.Equal(expected, space.Showing, name + ": Space で変換");
            }
        }
    }

    [Test]
    public static void TypedSymbols_LearnedChoiceStaysOnSymbolOnlyInput()
    {
        // 記号だけを変換して選んだ候補 (？ → ⁉) は覚えて、次に記号だけを変換したときに出す (#672)。かなと一緒に打った ？ は ？ のまま (#519)
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history, moreCandidates: reading => reading == "？" ? ["？", "⁉"] : []);
        k.Type("? ");
        for (var i = 0; i < 10 && k.Host.View!.Candidates[k.Host.View.SelectedIndex] != "⁉"; i++) k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal("⁉", k.Host.Document);
        Assert.Equal("⁉", history.Get("？"), "記号だけの文節で選んだ候補は覚える");

        var again = new Keyboard(history: history);
        again.Type("? ");
        Assert.Equal("⁉", again.Showing, "記号だけを変換すると覚えた候補");

        var live = new Keyboard(live: true, history: history, converter: new SymbolChangingConverter(split: true));
        live.Type("reibunn?");
        Assert.Equal("例文？", live.Showing, "かなと一緒に打った記号は覚えた候補にしない");
    }

    [Test]
    public static void TypedSymbols_StayAfterResizingClauses()
    {
        // 文節の区切りを変えて変換し直した文節でも、かなと一緒に打った記号は打ったときの文字のまま (変換エンジン・変換履歴が 、 を ・ にしても)。
        // 打った 、。 は、いつもそれだけの文節 (文節を伸ばしても入らず、縮めた分は 、 の前に新しい文節を作る)
        var history = new ConversionHistory(null);
        history.Remember("、", "・");
        history.Remember("？", "⁉");
        var k = new Keyboard(history: history, converter: new SymbolChangingConverter(split: true));
        k.Type("watasiha,kyouha? ");
        Assert.Equal("私は|、|今日は|？", string.Join("|", k.Host.View!.Clauses!));
        ShiftArrow(k, VirtualKeys.Right);
        Assert.Equal("私は|、|今日は|？", string.Join("|", k.Host.View!.Clauses!), "、 は前の文節に入らない");
        k.Press(VirtualKeys.Right);
        ShiftArrow(k, VirtualKeys.Right);
        Assert.Equal("私は|、|今日は|？", string.Join("|", k.Host.View!.Clauses!), "、 は伸ばせない");
        k.Press(VirtualKeys.Right);
        ShiftArrow(k, VirtualKeys.Right);
        Assert.Equal("私は|、|今日は？", string.Join("|", k.Host.View!.Clauses!), "伸ばした文節");
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Left);
        ShiftArrow(k, VirtualKeys.Left);
        Assert.Equal("、", k.Host.View!.Clauses![2], "縮めた分は 、 の前の文節: " + string.Join("|", k.Host.View.Clauses));

        var end = new Keyboard(history: history, converter: new SymbolChangingConverter(split: true));
        end.Type("kyouha? ");
        end.Press(VirtualKeys.Left);
        ShiftArrow(end, VirtualKeys.Right);
        ShiftArrow(end, VirtualKeys.Left);
        Assert.Equal("今日は|？", string.Join("|", end.Host.View!.Clauses!), "縮めてできた ？ だけの文節も、覚えた ⁉ にしない");
    }

    [Test]
    public static void MathWordSymbolFromEngine_IsNotLearned()
    {
        // #293: 3かける4 の × は変換エンジン (Mozc) の候補から選ぶ。選んでも覚えない (覚えると 電話をかける まで × になる)
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history, moreCandidates: reading => reading == "かける" ? ["×"] : []);
        k.Type("kakeru ");
        for (var i = 0; i < 20 && k.Host.View!.Candidates[k.Host.View.SelectedIndex] != "×"; i++) k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal("×", k.Host.Document);
        Assert.True(history.Get("かける") is null, "記号を選んでも学習しない");
    }

    /// <summary>覚えさせた文節を記録する変換エンジン (Mozc の代わり)。</summary>
    private sealed class LearningConverter : TestConverter
    {
        private readonly FakeConverter _inner = new();
        public override string? Convert(string hiragana, string? before) => _inner.Convert(hiragana, before);
        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) => _inner.ConvertClauses(hiragana, before);

    }

    /// <summary>しけんし を 試験し、しけんしたい を 試験したい にする変換エンジン (issue #1276)。</summary>
    private sealed class ShikenshiConverter : TestConverter
    {
        public override string? Convert(string hiragana, string? before) => hiragana switch
        {
            "しけんし" => "試験し",
            "しけんしたい" => "試験したい",
            _ => null,
        };

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) =>
            Convert(hiragana, before) is { } text ? [new(hiragana, text)] : null;
    }

    [Test]
    public static void Issue1276_EnglishInTheSameCompositionIsAContextCue()
    {
        // #1276: ph|しけんし の しけんし は、同じ変換ボックスの英字 (ph) を手がかりに 試験紙 にする
        foreach (var live in new[] { false, true })
        {
            var k = new Keyboard(live: live, converter: new ShikenshiConverter());
            k.Type("phsikennsi ");
            Assert.Equal("pH試験紙", k.Host.View?.Text, $"Space (ライブ変換 {live})");
        }
        // 手がかりが無ければ、変換エンジンのまま
        var plain = new Keyboard(converter: new ShikenshiConverter());
        plain.Type("sikennsi ");
        Assert.Equal("試験し", plain.Host.View?.Text);
        // ph を含む英単語は手がかりにしない (php・iphone・phone・graph・alpha の ph)
        foreach (var live in new[] { false, true })
        {
            foreach (var (typed, expected) in new[] { ("phpsikennsitai ", "php試験したい"), ("iphonesikennsitai ", "iPhone試験したい"), ("phonesikennsi ", "phone試験し") })
            {
                var k = new Keyboard(live: live, converter: new ShikenshiConverter());
                k.Type(typed);
                Assert.Equal(expected, k.Host.View?.Text, $"{typed}(ライブ変換 {live})");
            }
        }
        var rules = ContextRules.Load(null);
        Assert.Equal(null, ChooseKana(rules, "しけんし", "graphと alphaの"), "確定済みの英単語の中の ph も手がかりにしない");
        Assert.Equal("試験紙", ChooseKana(rules, "しけんし", "pHの"), "英単語の境界にある pH は手がかり");
    }

    /// <summary>さいき を 再起 にする変換エンジン (issue #1681)。</summary>
    private sealed class SaikiConverter : TestConverter
    {
        public override string? Convert(string hiragana, string? before) => hiragana == "さいき" ? "再起" : null;

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) =>
            hiragana == "さいき" ? [new("さいき", "再起")] : null;
    }

    [Test]
    public static void Issue1681_DictionaryWordsComeBeforeTheEngineCandidates()
    {
        // #1681: 変換エンジンが知っている読みでは、同梱の辞書の語 (PSYQUI) が変換エンジンの候補の後ろ (58 件中 51 番目) に並んでいた。
        // 最初の候補 (変換エンジンの答え) は変えず、その次に辞書の語、そのあとに変換エンジンの候補を並べる
        string[] engine = ["再帰", "最期", "才気", "佐伯", "催奇", "祭器", "才木", "再期", "細期", "斎木"];
        var k = new Keyboard(converter: new SaikiConverter(), moreCandidates: reading => reading == "さいき" ? engine : []);
        k.Type("saiki ");
        var candidates = k.Host.View!.Candidates;
        Assert.Equal("再起", candidates[0], "最初の候補は変換エンジンの答え");
        Assert.Equal("PSYQUI", candidates[1], string.Join(" ", candidates));
        Assert.Equal("再帰", candidates[2], "変換エンジンの候補は辞書の語の次から");
    }

    [Test]
    public static void Issue1636_BackspaceAfterADoubledConsonant_LeavesTheConsonant()
    {
        // #1636: ff (っf) の f・tch (っch) の ch を消したら、っ ではなく打ちかけの子音に戻す (残った原文を打ち直す)
        foreach (var (keys, expected) in new[]
        {
            ("ff\buronto\n", "ふろんと"),
            ("kk\bakkou\n", "かっこう"),
            ("tt\betoka\n", "てとか"),
            ("tch\b\ba\n", "た"),
            // っ に限らず、後ろのキーを見て決めた単位は、そのキーを消したら決め直す (nk の ん)
            ("nk\ba\n", "な"),
            ("kank\bi\n", "かに"),
            ("matcha\n", "まっちゃ"),
            // 子音の後ろを打ってからの っ は、今までどおり 1 音ずつ消す
            ("kitte\b\b\n", "き"),
            ("kitte\b\n", "きっ"),
        })
        {
            var k = new Keyboard();
            k.Type(keys);
            Assert.Equal(expected, k.Host.Document, keys.Replace("\b", "⌫").Replace("\n", "⏎"));
        }
    }

    [Test]
    public static void Backspace_RetypesTheRemainingRaw()
    {
        // BackSpace で消した後の状態は、残った原文を打ち直して作る (#1636)。消す量は見えている 1 音 (打ちかけの子音があればその 1 文字)
        foreach (var (keys, expected) in new[]
        {
            ("reibunn\b", "れいぶ"),     // 基本: 最後の 1 音 (ん)
            ("reibun\b", "れいぶ"),      // 打ちかけの n
            ("akya\b", "あ"),            // 2 文字で 1 音 (きゃ)
            ("akyak\b", "あきゃ"),       // 打ちかけの子音だけ消す
            ("matcha\b", "まっ"),        // ちゃ を消す。子音の後ろまで打った っ は残す (kitte の っ と同じ)
            ("ff\b", "f"),               // 後ろのキーで決めた っ は決め直す
            ("nk\b", "ん"),              // 後ろのキーで決めた ん も決め直す (打ちかけの n は確定される形で見せる)
            ("kyouhagoogle\b", "きょうはgoogl"), // 英語の区間は 1 文字
            ("/k\b", "・"),               // 打ち始めの / を ・ にした単位は残す (打ち直すと / になる)
            ("/kk\b", "・k"),
            ("/\bka/", "か/"),                // 入力が空になったら、次の / は打ち始めの / ではない (かなの後ろの /)
        })
        {
            var k = new Keyboard();
            k.Type(keys);
            Assert.Equal(expected, k.Host.View?.Text, keys.Replace("\b", "⌫"));
        }
    }

    [Test]
    public static void Ki_OffersOldKanji()
    {
        // #371: き の候補に 気 の旧字体 氣 も出す
        var k = new Keyboard();
        k.Type("ki ");
        Assert.True(k.Host.View!.Candidates.Contains("氣"), string.Join(" ", k.Host.View!.Candidates));
    }

    [Test]
    public static void Keigo_IsOfferedAfterEngineCandidates()
    {
        // #352: よく使う動詞の尊敬語・謙譲語も候補に出す。最初の候補 (変換エンジンの結果) は変えない
        foreach (var (keys, keigo) in new[] { ("iu ", "おっしゃる"), ("iku ", "伺う"), ("taberu ", "召し上がる"), ("miru ", "拝見する"), ("suru ", "いたす") })
        {
            var k = new Keyboard();
            k.Type(keys);
            var candidates = k.Host.View!.Candidates;
            Assert.True(candidates.Contains(keigo), keys + ": " + string.Join(" ", candidates));
            Assert.True(candidates[0] != keigo, keys + ": 最初の候補は変えない");
        }
    }

    [Test]
    public static void WiWe_OfferOldKana()
    {
        var k = new Keyboard();
        k.Type("wisuki- ");
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("ゐすきー") && candidates.Contains("ヰスキー"), string.Join(",", candidates));
        k = new Keyboard();
        k.Type("uisuki- ");
        Assert.True(!k.Host.View!.Candidates.Any(c => c.Contains('ゐ') || c.Contains('ヰ')), "ui で打ったら出さない");
        foreach (var (typed, old) in new[] { ("kowi ", "こゐ") })
        {
            k = new Keyboard();
            k.Type(typed);
            Assert.True(k.Host.View?.Candidates.Contains(old) == true, typed + ": " + string.Join(",", k.Host.View?.Candidates ?? []));
        }
    }

    [Test]
    public static void Translations_AreOfferedAfterJapanese()
    {
        // アイデア: 「ふくざつな」を変換したら complex / complicated も候補に。な が付くなら な形容詞 の訳を先に。
        var dictionary = TranslationDictionary.Parse("川\tn\triver,stream\n複雑\tn\tcomplexity\n複雑\tna\tcomplex,complicated");
        Assert.Equal("complex,complicated,complexity", string.Join(",", dictionary.Lookup("複雑な", "ふくざつな")));
        Assert.Equal("complexity,complex,complicated", string.Join(",", dictionary.Lookup("複雑", "ふくざつ")));
        Assert.Equal(0, dictionary.Lookup("川べり", "かわべり").Count, "語の後ろが助詞などでなければ出さない");

        var history = new TranslationHistory(null);
        IReadOnlyList<string> Convert()
        {
            var k = new Keyboard(translations: dictionary, translationHistory: history);
            k.Type("kawa ");
            var view = k.Host.View!;
            var index = view.Candidates.ToList().IndexOf("river");
            Assert.True(index >= 0, "英訳の候補がある");
            Assert.Equal(CandidateNote.Translation, view.Notes![index], "並べ替え後も英訳の注記が追従する");
            return view.Candidates;
        }
        var k = new Keyboard(translations: dictionary, translationHistory: history);
        k.Type("kawa ");
        var view = k.Host.View!;
        var index = view.Candidates.ToList().IndexOf("river");
        Assert.True(index > 0 && view.Candidates[0] == "川", "英訳は日本語の候補の後ろ: " + string.Join(",", view.Candidates));
        Assert.Equal(CandidateNote.Translation, view.Notes![index]);
        for (var i = 0; i < index; i++) k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal("river", k.Host.Output.Single());

        // 学習は弱め: 1 回選んでも 1 番目にはしない。2 回選んだら 2 番目。
        Assert.Equal("川", Convert()[0], "1 回では 1 番目にしない");
        history.Remember("かわ", "river");
        Assert.Equal("川,river", string.Join(",", Convert().Take(2)), "2 回選んだら 2 番目");
    }

    [Test]
    public static void CandidateNotes_SessionJsonKeepsDisplayText()
    {
        // Core 内は Enum で保持し、Native 向け JSON だけで表示文字列にする。
        var view = new CompositionView("@", ["@", "＠", "river", "川"], 0, true, "",
            Notes: [CandidateNote.HalfWidth, CandidateNote.FullWidth, CandidateNote.Translation, CandidateNote.None]);
        using var json = System.Text.Json.JsonDocument.Parse(new SessionResult(true, [], view).ToJson());
        var notes = json.RootElement.GetProperty("view").GetProperty("candidates").GetProperty("notes");
        Assert.Equal(4, notes.GetArrayLength());
        Assert.Equal("半角", notes[0].GetString());
        Assert.Equal("全角", notes[1].GetString());
        Assert.Equal("英訳", notes[2].GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, notes[3].ValueKind);

        using var unlabelled = System.Text.Json.JsonDocument.Parse(new SessionResult(true, [], view with { Notes = null }).ToJson());
        var emptyNotes = unlabelled.RootElement.GetProperty("view").GetProperty("candidates").GetProperty("notes");
        Assert.Equal(4, emptyNotes.GetArrayLength());
        Assert.True(emptyNotes.EnumerateArray().All(n => n.ValueKind == System.Text.Json.JsonValueKind.Null), "注記リストが無ければ各候補の注記は null");
    }

    [Test]
    public static void CandidateNotes_InvalidValueIsRejected()
    {
        try
        {
            ((CandidateNote)(-1)).ToDisplayText();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        Assert.True(false, "未定義の注記を表示時に受け入れない");
    }


    [Test]
    public static void UserDictionary_ReadingThatStaysAscii_IsUsed()
    {
        // zh → ← のように、ローマ字として読めずに英字のまま残る読みを登録しても、変換で使われていなかった
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("zh", "←");
        var k = new Keyboard(userDictionary: dictionary);
        k.Type("zh ");
        Assert.Equal("←", k.Host.View!.Clauses![0]);

        k = new Keyboard(live: true, userDictionary: dictionary);
        k.Type("zh ");
        Assert.Equal("←", k.Host.View!.Clauses![0], "ライブ変換でも使う");
    }

    [Test]
    public static void UserDictionary_WholeAsciiReading_IsOneClause()
    {
        // 打った英字全体を読みに登録した語は、英字が区間 (hello|world) に分かれても候補に出す
        foreach (var reading in new[] { "helloworld", "githubpython" })
        {
            var dictionary = new UserDictionary(null, builtIn: false);
            dictionary.Add(reading, "登録候補");
            var k = new Keyboard(userDictionary: dictionary);
            k.Type(reading + " ");
            Assert.Equal("登録候補", string.Join("|", k.Host.View!.Clauses!), reading);
        }

        // ローマ字として読める読み (go) は、登録した語の後ろに、その読みの漢字も出す (#1141)
        var go = new UserDictionary(null, builtIn: false);
        go.Add("go", "登録候補");
        foreach (var key in new[] { VirtualKeys.Space, VirtualKeys.Convert })
        {
            var k = new Keyboard(userDictionary: go, moreCandidates: reading => reading == "ご" ? ["後", "語", "碁"] : []);
            k.Type("go");
            k.Press(key);
            var candidates = k.Host.View!.Candidates;
            Assert.Equal("登録候補", candidates[0], $"{key}");
            Assert.True(candidates.Contains("go") && candidates.Contains("碁"), $"{key}: " + string.Join(",", candidates));
        }
    }

    [Test]
    public static void UserDictionary_AsciiReading_TabConverts()
    {
        // 英語と判定する読み (hello) を登録したら、Tab でも Space と同じく変換する
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("hello", "登録候補");
        var k = new Keyboard(userDictionary: dictionary, tabConversion: true);
        k.Type("hello");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("登録候補", k.Host.View!.Clauses![0]);
    }

    [Test]
    public static void UserDictionary_SavesAndLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-userdict-{Guid.NewGuid():N}.txt");
        try
        {
            var dictionary = new UserDictionary(path);
            dictionary.Add("きごうとう", "記号等");
            dictionary.Add("おーとあいえむいー", "Meltype");
            var loaded = new UserDictionary(path);
            Assert.Equal("記号等", loaded.Lookup("きごうとう").Single());
            Assert.Equal("Meltype", loaded.Lookup("おーとあいえむいー").Single());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Learning_SkipsSingleKana()
    {
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history);
        k.Type("ki  \n"); // き を変換して候補を選び直す
        Assert.Equal(0, history.Count, "1 文字の読みは覚えない (き → 記 が きごうとう まで巻き込むため)");
    }

    [Test]
    public static void Issue1737_SegmentationAlternativesAreSentenceCandidates()
    {
        // botto は bot|と とも ぼっと とも読める。英日の区切りの案ごとの文を、文全体の 1 つの文節の候補にする
        var k = new Keyboard();
        k.Type("botto ");
        Assert.Equal("botと", string.Join("|", k.Host.View!.Clauses!));
        Assert.True(k.Host.View.Candidates.Contains("ぼっと"), string.Join(" ", k.Host.View.Candidates));
        while (string.Concat(k.Host.View!.Clauses!) != "ぼっと") k.Type(" ");
        k.Type("\n");
        Assert.Equal("ぼっと", k.Host.Output.Single());
    }

    [Test]
    public static void Issue1737_ArrowSplitsSelectedAlternativeIntoClauses()
    {
        // → / ← で、選んでいる案の文節に分けて、文節ごとに選び直せる
        var k = new Keyboard();
        k.Type("botto ");
        k.Press(VirtualKeys.Right);
        Assert.Equal("bot|と", string.Join("|", k.Host.View!.Clauses!));
        Assert.Equal(1, k.Host.View.SelectedClause);

        var j = new Keyboard();
        j.Type("botto ");
        while (string.Concat(j.Host.View!.Clauses!) != "ぼっと") j.Type(" ");
        j.Press(VirtualKeys.Left);
        Assert.Equal("ぼっと", string.Join("|", j.Host.View!.Clauses!));
        j.Type("\n");
        Assert.Equal("ぼっと", j.Host.Output.Single());
    }

    [Test]
    public static void Issue1737_ShiftArrowResizesSelectedAlternative()
    {
        // Shift + ← → も、選んでいる案の文節に分けてから区切りを動かす
        var k = new Keyboard();
        k.Type("botto ");
        while (string.Concat(k.Host.View!.Clauses!) != "ぼっと") k.Type(" ");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("ぼっ|と", string.Join("|", k.Host.View!.Clauses!));
    }

    [Test]
    public static void Issue1737_EnglishEndingShowsAlternativesWithShiftSpace()
    {
        // 英単語で終わる入力は、Space では今までどおり確定して空白を入れる。Shift+Space で変換すると区切りの案を出す
        var space = new Keyboard();
        space.Type("kyouhaMake ");
        Assert.Equal("きょうはMake ", space.Host.Document);

        var k = new Keyboard();
        k.Type("kyouhaMake");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.True(k.Host.View!.Candidates.Contains("今日は負け"), string.Join(" ", k.Host.View.Candidates));
    }

    private static void ShiftSpace(Keyboard k)
    {
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void EnglishWord_KatakanaSpellingIsACandidate()
    {
        // 英語と判定した語の変換の候補に、カタカナ表記を出す。英字が最初の候補のまま (Space では今までどおり英字で確定する)
        var space = new Keyboard();
        space.Type("export ");
        Assert.Equal("export ", space.Host.Document);

        var k = new Keyboard { SeparateKeyRoles = true };
        k.Type("application ");
        Assert.Equal("application", k.Host.View!.Candidates[0]);
        Assert.True(k.Host.View.Candidates.Contains("アプリケーション"), string.Join(" ", k.Host.View.Candidates));

        // Shift+Space (日本語を先に) でも、ローマ字として読めない語は英字が先で、カタカナ表記は最後
        var shift = new Keyboard();
        shift.Type("export");
        ShiftSpace(shift);
        Assert.Equal("export", shift.Host.View!.Candidates[0]);
        Assert.Equal("エクスポート", shift.Host.View.Candidates[^1]);
    }

    [Test]
    public static void EnglishWord_KatakanaSpellingKeepsRomajiCandidatesFirst()
    {
        // ローマ字として読める語 (game → がめ) は、読んだ候補を今までどおり出し、カタカナ表記 (ゲーム) はその後ろ
        var k = new Keyboard { SeparateKeyRoles = true };
        k.Type("game ");
        var candidates = k.Host.View!.Candidates.ToList();
        Assert.Equal("game", candidates[0]);
        Assert.True(candidates.IndexOf("がめ") >= 0 && candidates.IndexOf("がめ") < candidates.IndexOf("ゲーム"), string.Join(" ", candidates));
    }

    [Test]
    public static void EnglishWord_ChoosingKatakanaSpellingDoesNotMakeItJapanese()
    {
        // カタカナ表記を選んで確定しても、その英単語を日本語として覚えない (覚えると次から export が えxぽrt になる)
        var languages = new LanguageMemory(null);
        var k = new Keyboard(languages: languages);
        k.Type("export");
        ShiftSpace(k);
        var katakana = k.Host.View!.Candidates.ToList().IndexOf("エクスポート");
        for (var i = 0; i < katakana; i++) k.Press(VirtualKeys.Space);
        k.Type("\n");
        Assert.Equal("エクスポート", k.Host.Output.Single());
        Assert.Equal(0, languages.Entries().Count);
        k.Type("export\n");
        Assert.Equal("export", k.Host.Output.Last());

        // ローマ字として読んだ候補 (camera → カメラ) を選んだら、今までどおり日本語として覚える
        var camera = new Keyboard(languages: languages);
        camera.Type("camera");
        ShiftSpace(camera);
        var index = camera.Host.View!.Candidates.ToList().IndexOf("カメラ");
        for (var i = 0; i < index; i++) camera.Press(VirtualKeys.Space);
        camera.Type("\n");
        Assert.Equal("カメラ", camera.Host.Output.Single());
        Assert.Equal(1, languages.Entries().Count);
    }

    [Test]
    public static void Issue1737_SpaceAfterF6ConvertsEverythingAsJapanese()
    {
        // F6 でかなにして見せた入力は、Space でも英語の区間に戻さず、すべてローマ字として読んで変換する
        var k = new Keyboard();
        k.Type("kyouhaMake");
        k.Press(VirtualKeys.F6);
        k.Type(" ");
        Assert.Equal("今日は|負け", string.Join("|", k.Host.View!.Clauses!));

        var j = new Keyboard();
        j.Type("botto");
        j.Press(VirtualKeys.F6);
        j.Type(" \n");
        Assert.Equal("ぼっと", j.Host.Output.Single());

        // F6 を押さなければ、今までどおり英語の区間は英語のまま
        var auto = new Keyboard();
        auto.Type("kyouhaMake");
        auto.Press(VirtualKeys.Right);
        Assert.Equal("今日は|Make", string.Join("|", auto.Host.View!.Clauses!));
    }

    /// <summary>Zenzai を使える変換エンジンの代わり。Zenzai にも頼む依頼と頼まない依頼 (<see cref="ConversionRequest.Zenzai"/>) の読みを記録する。</summary>
    private sealed class RecordingStandaloneConverter : IKanjiConverter
    {
        private readonly FakeConverter _inner = new();
        public List<string> WithZenzai { get; } = [];
        public List<string> WithoutZenzai { get; } = [];

        public IReadOnlyList<ConversionClause>? Convert(ConversionRequest request)
        {
            (request.Zenzai ? WithZenzai : WithoutZenzai).Add(request.Hiragana);
            return _inner.Convert(request);
        }
    }

    [Test]
    public static void Issue1737_OtherAlternativesDoNotAskZenzai()
    {
        // Zenzai は最後に頼まれた 1 件だけを裏で処理するので、2 番目以降の区切りの案 (きょうはぼっと) は Zenzai に頼まない。
        // いちばん良い案 (今日は|bot|と) の日本語は、今までどおり Zenzai に頼む
        var converter = new RecordingStandaloneConverter();
        var k = new Keyboard(converter: converter);
        k.Type("kyouhabotto ");
        Assert.True(k.Host.View!.Candidates.Contains("きょうはぼっと"), string.Join(" ", k.Host.View.Candidates));
        Assert.True(converter.WithZenzai.Contains("きょうは"), string.Join(" ", converter.WithZenzai));
        Assert.True(!converter.WithZenzai.Contains("きょうはぼっと"), string.Join(" ", converter.WithZenzai));
        Assert.True(converter.WithoutZenzai.Contains("きょうはぼっと"), string.Join(" ", converter.WithoutZenzai));
    }

    [Test]
    public static void Issue1737_NoKanaAlternativeWithUnreadableLetters()
    {
        // ローマ字として読めない英字が残る読み (google → ごおgぇ) は日本語の案にならないので、すべてかなの案は出さない
        var k = new Keyboard();
        k.Type("kyouhagooglede ");
        Assert.True(!k.Host.View!.Candidates.Any(c => c.Contains("ごお", StringComparison.Ordinal)), string.Join(" ", k.Host.View.Candidates));
    }

    [Test]
    public static void Issue1737_F7OnWholeSentenceCommitsKatakana()
    {
        // 文全体の文節で F7 を押すと、変換をやめて入力全体をカタカナにする (文節に分けずに確定する)
        var k = new Keyboard();
        k.Type("botto ");
        Assert.Equal("botと", string.Join("|", k.Host.View!.Clauses!));
        k.Press(VirtualKeys.F7);
        k.Type("\n");
        Assert.Equal("ボット", k.Host.Output.Single());
    }

    [Test]
    public static void Issue1737_SingleClauseAlternativeKeepsItsCandidates()
    {
        // sushi は 英語 / すし の 1 文節ずつの案。文全体の候補の後ろに、それぞれの文節の候補 (スシ) も並べて、選んで確定できる
        var k = new Keyboard();
        k.Type("sushi ");
        Assert.True(k.Host.View!.Candidates.Contains("sushi"), string.Join(" ", k.Host.View.Candidates));
        Assert.True(k.Host.View.Candidates.Contains("スシ"), string.Join(" ", k.Host.View.Candidates));
        while (string.Concat(k.Host.View!.Clauses!) != "スシ") k.Type(" ");
        k.Type("\n");
        Assert.Equal("スシ", k.Host.Output.Single());
    }

    [Test]
    public static void Clauses_ShiftArrowResizes()
    {
        var k = new Keyboard();
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        Assert.Equal("単位|をとる", string.Join("|", k.Host.View!.Clauses!), "Shift+← で文節を 1 文字縮め、残りは次の文節へ");
        k.Press(VirtualKeys.Right);
        Assert.Equal("たんいを|とる", string.Join("|", k.Host.View.Clauses!), "Shift+→ で 1 文字伸ばす");
        k.Key(VirtualKeys.LShift, up: true);
        k.Type("\n");
        Assert.Equal("たんいをとる", k.Host.Output.Single());
    }

    [Test]
    public static void Clauses_ShiftArrowResize_ExpandsWithWindowsCandidatesAgain()
    {
        // 区切りを変えた文節も、新しい読みで変換エンジンの候補の一覧を取り直す。
        var k = new Keyboard(moreCandidates: r => [r + "★"]);
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Left);
        Assert.True(k.Host.View!.Candidates.Contains("たんいを★"), string.Join(",", k.Host.View.Candidates));
        k.Press(VirtualKeys.Right);
        Assert.True(k.Host.View.Candidates.Contains("とる★"), string.Join(",", k.Host.View.Candidates));
        k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.True(k.Host.View.Candidates.Contains("たんいをと★"), "選んでいる文節: " + string.Join(",", k.Host.View.Candidates));
        k.Press(VirtualKeys.Right);
        Assert.True(k.Host.View.Candidates.Contains("る★"), "前に選んでいた次の文節: " + string.Join(",", k.Host.View.Candidates));
    }

    private sealed class ContextRecordingConverter : TestConverter
    {
        private readonly FakeConverter _inner = new();
        public List<(string Hiragana, string? Before)> Converted { get; } = [];

        public override string? Convert(string hiragana, string? before)
        {
            Converted.Add((hiragana, before));
            return _inner.Convert(hiragana, before);
        }

        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) => _inner.ConvertClauses(hiragana, before);
    }

    [Test]
    public static void Clauses_ResizedClauseUsesClausesBeforeItAsContext()
    {
        // 区切りを動かして読みだけで変換し直す文節には、キャレットの前に続けて、変換ボックスの中でその文節より前にある文字も文脈にする
        var converter = new ContextRecordingConverter();
        var k = new Keyboard(converter: converter);
        k.Host.PrecedingText = "これは";
        k.Type("nihongowokaku ");
        Assert.Equal("日本語を|書く", string.Join("|", k.Host.View!.Clauses!));
        k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("日本語|をかく", string.Join("|", k.Host.View!.Clauses!));
        Assert.True(converter.Converted.Contains(("にほんご", "これは")), string.Join(",", converter.Converted));
        Assert.True(converter.Converted.Contains(("をかく", "これは日本語")), string.Join(",", converter.Converted));
    }

    [Test]
    public static void Clauses_ShiftArrowResizesAcrossEnglish()
    {
        // 報告 (#144): 英語の文節が混ざると Shift+← → で区切りを動かせない (みー|thin|ぐ、disco|で)。
        // (mi-thingu は #153 の対応で最初から みーてぃんぐ と読むようになったので、英語の文節が残る例で確かめる)
        var k = new Keyboard();
        k.Type("konohoteldeyaru ");
        Assert.Equal("この|hotel|でやる", string.Join("|", k.Host.View!.Clauses!));
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        Assert.Equal("このほ|てl|でやる", string.Join("|", k.Host.View.Clauses!), "後ろの英語の文節は、打ったローマ字のかなで読み直して区切りを動かす");
        k.Press(VirtualKeys.Right);
        Assert.Equal("このほて|l|でやる", string.Join("|", k.Host.View.Clauses!));
        k.Key(VirtualKeys.LShift, up: true);

        k = new Keyboard();
        k.Type("konohoteldeyaru ");
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        Assert.Equal("この|ほて|lでやる", string.Join("|", k.Host.View!.Clauses!), "選んだ英語の文節も読み直して縮め、外れたかなは次の文節へ");
        k.Press(VirtualKeys.Left);
        Assert.Equal("この|ほ|てlでやる", string.Join("|", k.Host.View.Clauses!));
        k.Key(VirtualKeys.LShift, up: true);

        k = new Keyboard();
        k.Type("nihongoGithubde ");
        Assert.Equal("日本語|Github|で", string.Join("|", k.Host.View!.Clauses!));
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        Assert.Equal("にほん|ご|Github|で", string.Join("|", k.Host.View.Clauses!), "縮めて空いた分は、後ろの英語の文節を変えずに新しい文節にする");
        k.Press(VirtualKeys.Right);
        Assert.Equal("日本語|Github|で", string.Join("|", k.Host.View.Clauses!), "伸ばして戻す");
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void Clauses_ShiftArrowKeepsEnglishWhenItCannotMove()
    {
        // 区切りを動かせないときは、選んだ英語の文節を日本語に読み直さない (日本語の文節と同じく何も変えない)
        var k = new Keyboard();
        k.Type("konohotel");
        k.Press(VirtualKeys.Convert);
        Assert.Equal("この|hotel", string.Join("|", k.Host.View!.Clauses!));
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        Assert.Equal("この|hotel", string.Join("|", k.Host.View.Clauses!), "最後の文節は伸ばせない");
        k.Key(VirtualKeys.LShift, up: true);

        k = new Keyboard();
        k.Type("konohotelXyz");
        k.Press(VirtualKeys.Convert);
        Assert.Equal("この|hotel|Xyz", string.Join("|", k.Host.View!.Clauses!));
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        Assert.Equal("この|hotel|Xyz", string.Join("|", k.Host.View.Clauses!), "次の文節がかなに読めないので伸ばせない");
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void AmbiguousWord_FollowsEnglishContext()
    {
        var k = new Keyboard();
        k.Type("hello ");
        k.Type("sushi");
        Assert.Equal("sushi", k.Showing, "英語の後なら sushi は英語");
        k.Type(" ");
        Assert.Equal("hello |sushi ", string.Join("|", k.Host.Output), "英語なので Space は空白");
    }

    [Test]
    public static void AmbiguousWord_FollowsJapaneseContext()
    {
        var k = new Keyboard();
        k.Type("kyouha\n");
        k.Type("sushi");
        Assert.Equal("すし", k.Showing, "日本語の後なら sushi は日本語");

        k = new Keyboard();
        k.Type("hello ");
        k.Type("sushiga");
        Assert.Equal("すしが", k.Showing, "英語の後でも、後ろに日本語が続けば日本語");
    }

    [Test]
    public static void Space_AfterEnglishWord_InsertsSpace()
    {
        var k = new Keyboard(live: true);
        k.Type("kyouhagoogle ");
        Assert.Equal("今日はgoogle ", k.Host.Output.Single(), "英単語で終わっていれば、変換ではなく確定して空白");
        // 報告: ライブ変換を OFF にしても漢字になる。OFF なら見えているかなのまま確定する。
        k = new Keyboard();
        k.Type("kyouhagoogle ");
        Assert.Equal("きょうはgoogle ", k.Host.Output.Single(), "ライブ変換 OFF なら かなのまま");
    }

    [Test]
    public static void JapaneseSentences_StayJapanese()
    {
        foreach (var sentence in new[]
        {
            "watashihagakuseidesu", "ashitahaamedesu", "kyouhaiitenkidesune", "sumimasenkakuninshimasu",
            "kanojohasushigasuki", "nihongonobenkyou", "arigatougozaimasu", "kaishaniikimasu", "itsumoarigatou",
            "tomodachitoasobu", "shiryouwookurimasu", "mondaihaarimasen",
        })
        {
            var k = new Keyboard();
            k.Type(sentence + "\n"); // Enter で確定 (末尾の n も ん になる)
            Assert.True(k.Host.Output.Single().All(c => !char.IsAsciiLetter(c)), $"「{sentence}」に英字が混ざった: {k.Host.Output.Single()}");
        }
    }

    [Test]
    public static void LiveConversion_ConvertsWhileTyping()
    {
        var k = new Keyboard(live: true);
        k.Type("ka");
        Assert.Equal("か", k.Showing, "1 拍はかなのまま (漢字を推測できない)");
        k.Type("wa");
        Assert.Equal("川", k.Showing, "2 拍以上になったら Space を押さなくても漢字で表示");

        k = new Keyboard(live: true);
        k.Type("kyo");
        Assert.Equal("きょ", k.Showing, "拗音は前のかなと合わせて 1 拍 (虚 にしない)");
        k.Type("u");
        Assert.Equal("今日", k.Showing);

        k = new Keyboard(live: true);
        k.Type("pushshita");
        Assert.Equal("pushした", k.Showing, "英単語 + する の活用は、Space の変換と同じくかなのまま");
        k = new Keyboard(live: true);
        k.Type("pythonnara");
        Assert.Equal("pythonなら", k.Showing, "英単語 + 助詞も、Space の変換と同じくかなのまま");

        k = new Keyboard(live: true);
        k.Type("kyouha");
        Assert.Equal("今日は", k.Showing);
        k.Type("google");
        Assert.Equal("今日はgoogle", k.Showing, "英語の部分は変換しない");
        k.Type("dekensaku\n");
        Assert.Equal("今日はgoogleで検索", k.Host.Output.Single());
    }

    [Test]
    public static void LiveConversion_SpaceShowsAlternatives()
    {
        var k = new Keyboard(live: true);
        k.Type("kyou ");
        Assert.True(k.Host.View!.Converting, "Space で候補一覧");
        k.Type(" ");
        Assert.Equal("きょう", k.Showing, "次の候補はかなのまま");
        k.Type("\n");
        Assert.Equal("きょう", k.Host.Output.Single());
    }

    [Test]
    public static void Backspace_DeletesOneKanaAtATime()
    {
        var k = new Keyboard();
        k.Type("kyou\b");
        Assert.Equal("きょ", k.Showing, "ローマ字 1 文字ではなく、かな 1 音ずつ消える");
        k.Type("\b");
        Assert.True(k.Showing is null, "きょ も 1 回で消える");

        k.Type("kitte\b");
        Assert.Equal("きっ", k.Showing);

        k = new Keyboard();
        k.Type("ky\b");
        Assert.Equal("k", k.Showing, "まだ音になっていない子音は 1 文字ずつ");

        k = new Keyboard();
        k.Type("google\b");
        Assert.Equal("googl", k.Showing, "英字は 1 文字ずつ");
    }

    [Test]
    public static void EnglishWords_AreShownAsEnglish()
    {
        foreach (var (word, expected) in new[] { ("github", "GitHub"), ("hello", "hello"), ("typescript", "TypeScript"), ("npm", "npm"), ("localhost", "localhost"), ("zoom", "zoom"), ("git", "git"), ("test", "test"), ("iphone", "iPhone"), ("stackoverflow", "stackoverflow") })
        {
            var k = new Keyboard();
            k.Type(word);
            Assert.Equal(expected, k.Showing, $"「{word}」");
        }
    }

    [Test]
    public static void JapaneseWords_AreShownAsKana()
    {
        var expected = new Dictionary<string, string>
        {
            ["watashi"] = "わたし", ["arigatou"] = "ありがとう", ["kana"] = "かな", ["sushi"] = "すし", ["tesuto"] = "てすと",
            // 打ちかけの n は、確定される形 (ん) で見せる
            ["make"] = "まけ", ["ra-men"] = "らーめん", ["nihon"] = "にほん", ["konnichiwq"] = "こんいちwq", ["tanni"] = "たんい",
        };
        foreach (var (typed, kana) in expected)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(kana, k.Showing, $"「{typed}」");
        }
    }

    [Test]
    public static void FinalN_BecomesN_OnCommit()
    {
        var k = new Keyboard();
        k.Type("nihon\n");
        Assert.Equal("にほん", k.Host.Output.Single());
    }

    [Test]
    public static void Capitalized_IsEnglish()
    {
        var k = new Keyboard();
        k.Type("Tokyo");
        Assert.Equal("Tokyo", k.Showing);
    }

    [Test]
    public static void Space_ConvertsThenCyclesCandidates()
    {
        var k = new Keyboard();
        k.Type("kyou ");
        Assert.Equal("今日", k.Showing);
        Assert.True(k.Host.View!.Converting, "変換中");
        k.Type(" ");
        Assert.Equal("きょう", k.Showing, "もう一度 Space で次の候補");
        k.Type(" ");
        Assert.Equal("キョウ", k.Showing);
        k.Type(" ");
        Assert.Equal("ｷｮｳ", k.Showing, "半角カタカナも候補に出す");
        k.Type("\n");
        Assert.Equal("ｷｮｳ", k.Host.Output.Single());
    }

    [Test]
    public static void HalfWidthKatakana_HandlesDakutenAndSmallKana()
    {
        Assert.Equal("ｶﾞｯﾂﾎﾟｰｽﾞ", CompositionText.ToHalfWidthKatakana("ガッツポーズ"));
        Assert.Equal("ｷｮｳ", CompositionText.ToHalfWidthKatakana("キョウ"));
    }

    [Test]
    public static void TypingAfterConversion_CommitsAndStartsNew()
    {
        var k = new Keyboard();
        k.Type("nihongo wo");
        Assert.Equal("日本語", k.Host.Output.Single(), "変換中に次の文字を打つと確定");
        Assert.Equal("を", k.Showing);
    }

    [Test]
    public static void Space_OnEnglish_CommitsWithSpace()
    {
        var k = new Keyboard();
        k.Type("hello world\n");
        Assert.Equal("hello |world", string.Join("|", k.Host.Output));
    }

    [Test]
    public static void Backspace_EditsAndEscapeCancels()
    {
        var k = new Keyboard();
        k.Type("kak\b");
        Assert.Equal("か", k.Showing);
        k.Type("\b\b");
        Assert.True(k.Showing is null && !k.Captured, "空になったら閉じる");
        Assert.Equal(0, k.Host.Output.Count);

        k.Type("abc");
        k.Press(VirtualKeys.Escape);
        Assert.True(k.Showing is null, "Esc で取り消し");
        Assert.Equal(0, k.Host.Output.Count);
    }

    [Test]
    public static void FunctionKeys_ChangeDisplay()
    {
        var k = new Keyboard();
        k.Type("tesuto");
        k.Press(VirtualKeys.F7);
        Assert.Equal("テスト", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("tesuto", k.Showing);
        k.Press(VirtualKeys.OemAuto); // 半角/全角 で日本語⇔英字
        Assert.Equal("てすと", k.Showing);
    }

    [Test]
    public static void FunctionKeys_TypingAfterCommits()
    {
        // F7 でカタカナにしたあと続けて打つと、カタカナを確定して新しく入力する (続きまでカタカナにしない)
        var k = new Keyboard();
        k.Type("tesuto");
        k.Press(VirtualKeys.F7);
        k.Type("desu");
        Assert.Equal("テスト", string.Concat(k.Host.Output));
        Assert.Equal("です", k.Showing);

        // F10 で大文字にしたあとも同じ (続きまで大文字にしない)
        k = new Keyboard();
        k.Type("tesuto");
        k.Press(VirtualKeys.F10);
        k.Press(VirtualKeys.F10);
        Assert.Equal("TESUTO", k.Showing);
        k.Type("desu");
        Assert.Equal("TESUTO", string.Concat(k.Host.Output));
        Assert.Equal("です", k.Showing);
    }

    [Test]
    public static void F8_HalfWidthKatakana()
    {
        // Issue #74: F8 で半角カタカナ (Microsoft IME と同じ)。濁点・半濁点は ﾞ ﾟ に分ける
        var k = new Keyboard();
        k.Type("aiueo");
        k.Press(VirtualKeys.F8);
        Assert.Equal("ｱｲｳｴｵ", k.Showing);
        k.Press(VirtualKeys.Return);
        Assert.Equal("ｱｲｳｴｵ", k.Host.Document);

        k = new Keyboard();
        k.Type("gakkoupa-ri-");
        k.Press(VirtualKeys.F8);
        Assert.Equal("ｶﾞｯｺｳﾊﾟｰﾘｰ", k.Showing);
        k.Press(VirtualKeys.F6);
        Assert.Equal("がっこうぱーりー", k.Showing);

        // 変換中に押しても半角カタカナにする
        k = new Keyboard();
        k.Type("tesuto ");
        k.Press(VirtualKeys.F8);
        k.Press(VirtualKeys.Return);
        Assert.Equal("ﾃｽﾄ", k.Host.Document);
    }

    [Test]
    public static void OtherKeys_CommitFirstThenPassThroughInOrder()
    {
        var k = new Keyboard();
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("text:かな|down:09", string.Join("|", k.Host.Events), "確定してから Tab を通す");
    }

    private static void ShiftTab(Keyboard k)
    {
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Tab);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
    }

    /// <summary>今日は を 今日|は に区切る変換エンジン (変換したあとに続けて打つテスト用)。</summary>
    private sealed class KyouhaConverter : TestConverter
    {
        private readonly FakeConverter _inner = new();
        public override string? Convert(string hiragana, string? before) => _inner.Convert(hiragana, before);
        public override IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? before) => hiragana switch
        {
            "きょう" => [new("きょう", "今日")],
            "きょうは" => [new("きょう", "今日"), new("は", "は")],
            _ => _inner.ConvertClauses(hiragana, before),
        };
    }

    [Test]
    public static void ContinueAfterConversion_KeepsTheConvertedPartComposing()
    {
        // #123: 設定を ON にすると、Space で変換したあとに続けて打っても確定せず、続けて打った文字と一緒に変換ボックスに残る
        var k = new Keyboard(converter: new KyouhaConverter()) { ContinueAfterConversion = true };
        k.Type("kyou ");
        Assert.Equal("今日", k.Showing);
        k.Type("ha");
        Assert.Equal(0, k.Host.Output.Count, "確定しない");
        Assert.Equal("きょうは", k.Showing);
        k.Type(" \n");
        Assert.Equal("今日は", k.Host.Document);

        // 選んでいた候補 (キョウ) は、続けて打ったあとにもう一度変換しても使う
        k = new Keyboard(converter: new KyouhaConverter()) { ContinueAfterConversion = true };
        k.Type("kyou ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("キョウ");
        Assert.True(index > 0, string.Join(" ", k.Host.View!.Candidates));
        for (var i = 0; i < index; i++) k.Press(VirtualKeys.Space);
        Assert.Equal("キョウ", k.Showing);
        k.Type("ha ");
        Assert.Equal("キョウは", k.Showing);
        k.Press(VirtualKeys.Return);
        Assert.Equal("キョウは", k.Host.Document);

        // 既定 (OFF) は今までどおり、変換した部分を確定してから続きを打つ
        var normal = new Keyboard(converter: new KyouhaConverter());
        Assert.True(!new Meltype.Config.Settings().ContinueAfterConversion, "既定で OFF");
        normal.Type("kyou ha");
        Assert.Equal("今日", string.Concat(normal.Host.Output));
    }

    [Test]
    public static void TabConversion_IsOnByDefault()
    {
        // #219: 設定「Tab で変換」は既定で ON
        Assert.True(new Meltype.Config.Settings().TabConversion, "既定で ON");
    }

    [Test]
    public static void TabConversion_StartsConversionLikeSpace()
    {
        // #219: 設定 ON なら変換前の Tab で、Space と同じ表示・候補の変換が始まり、Tab はアプリに渡らない
        var space = new Keyboard();
        space.Type("kana ");
        var k = new Keyboard(tabConversion: true);
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting == true, "変換が始まる");
        Assert.Equal(space.Showing, k.Showing);
        Assert.Equal(string.Join(",", space.Host.View!.Candidates), string.Join(",", k.Host.View!.Candidates));
        Assert.True(!k.Host.Events.Any(e => e == "down:09"), "Tab は渡さない: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void TabConversion_TabMovesToNextAndShiftTabToPreviousCandidate()
    {
        // #219: Tab で始めた変換の中では Tab が次の候補 (Space と同じ)、Shift+Tab が前の候補
        var space = new Keyboard();
        space.Type("kana  ");
        var k = new Keyboard(tabConversion: true);
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        var first = k.Showing;
        k.Press(VirtualKeys.Tab);
        Assert.Equal(space.Showing, k.Showing);
        Assert.True(k.Showing != first, "候補が変わる");
        ShiftTab(k);
        Assert.Equal(first, k.Showing, "前の候補に戻る");
    }

    [Test]
    public static void TabConversion_EnterCommitsFirstCandidateWithoutPassingTab()
    {
        // #219: Tab で始めた変換は Enter で 1 番目の候補を確定し、Tab はアプリに渡らない
        var space = new Keyboard();
        space.Type("kana ");
        space.Press(VirtualKeys.Return);
        var k = new Keyboard(tabConversion: true);
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Return);
        Assert.Equal(space.Host.Document, k.Host.Document);
        Assert.True(!k.Host.Events.Any(e => e == "down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void TabConversion_TabInsideSpaceConversion_CommitsAndPassesTab()
    {
        // #219: Space で始めた変換の中の Tab は、今までどおり確定して Tab を渡す
        var off = new Keyboard();
        off.Type("kana ");
        off.Press(VirtualKeys.Tab);
        var k = new Keyboard(tabConversion: true);
        k.Type("kana ");
        k.Press(VirtualKeys.Tab);
        Assert.Equal(string.Join("|", off.Host.Events), string.Join("|", k.Host.Events));
        Assert.True(k.Host.Events.Contains("down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void TabConversion_EndingInEnglish_CommitsAndPassesTabWithoutSpace()
    {
        // #219: 英語で終わっているときは変換せず、確定して Tab を渡す (空白は入れない)
        var k = new Keyboard(tabConversion: true);
        k.Type("hello");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("text:hello|down:09", string.Join("|", k.Host.Events));
    }

    [Test]
    public static void TabConversion_PredictionTakesTabFirst()
    {
        // #219: 予測変換の候補が出ているときの Tab は、今までどおり予測を選ぶ
        var k = new Keyboard(predictor: new Predictor(new PhraseHistory(null), null, null), tabConversion: true);
        k.Type("kyou ");
        k.Press(VirtualKeys.Return);
        k.Type("kyo");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting != true, "変換にはならない");
        Assert.Equal("今日", k.Showing);
        Assert.Equal(0, k.Host.View!.SelectedPrediction);
    }

    [Test]
    public static void TabConversion_MisspellingTakesTabFirst()
    {
        // #219: もしかしての提案が出ているときの Tab は、今までどおり書き間違いを直す
        var k = new Keyboard(tabConversion: true);
        k.Type("buresureddo");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("ぶれすれっと", k.Showing);
        Assert.True(k.Host.View?.Converting != true, "変換にはならない");
        var converter = new SplitConverter(new Dictionary<string, ConversionClause[]>
        {
            ["ぶれすれっど"] = [new("ぶれす", "ブレス"), new("れっど", "レッド")],
            ["ぶれすれっと"] = [new("ぶれす", "ブレス"), new("れっと", "レット")],
        });
        k = new Keyboard(converter: converter);
        k.Type("buresureddo ");
        Assert.True(k.Host.View?.Suggestion?.Contains("ブレスレット") == true, "変換中に書き間違いの案内を出す");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting == true, "書き間違いを直して変換を続ける");
        Assert.Equal("ブレス|レット", string.Join("|", k.Host.View!.Clauses!));
        Assert.Equal(0, k.Host.View.SelectedClause, "先頭の文節を選ぶ");
    }

    [Test]
    public static void TabConversion_EscThenTab_StartsConversionAgain()
    {
        // #219: Esc で変換を取り消してかなに戻ったあとの Tab は、また変換を始める
        var k = new Keyboard(tabConversion: true);
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Escape);
        Assert.True(k.Host.View?.Converting != true, "かなに戻る");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting == true, "再び変換が始まる");
        Assert.True(!k.Host.Events.Any(e => e == "down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void TabConversion_ShiftTabWithoutPrediction_CommitsAndPassesTab()
    {
        // #219: Shift+Tab は変換を始めず、今までどおり確定して通す
        var k = new Keyboard(tabConversion: true);
        k.Type("kana");
        ShiftTab(k);
        Assert.True(k.Host.View?.Converting != true, "変換にはならない");
        Assert.True(k.Host.Events.Contains("down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void CtrlShortcut_CommitsFirst()
    {
        var k = new Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        k.Press('C');
        k.Key(VirtualKeys.LControl, up: true);
        // 入力中の Ctrl + キーは、Ctrl+U/I/O/P ならかな・英字の切り替え。ほかのキーなら確定してからそのキーを通す
        Assert.Equal("text:かな|down:43", string.Join("|", k.Host.Events), "確定 → Ctrl+C を通す");
        Assert.True(!k.Captured, "ショートカットの後は受け取らない");
    }

    [Test]
    public static void AfterShortcut_NextWordStillComposes()
    {
        var k = new Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        k.Press('S');
        k.Key(VirtualKeys.LControl, up: true);
        k.Type("kyou");
        Assert.Equal("きょう", k.Showing, "ショートカットの後も普通に変換ボックスが使える");
    }

    [Test]
    public static void ShiftArrow_KeepsShift()
    {
        var k = new Keyboard();
        k.Type("hello"); // 英字だけのときの矢印はキャレット移動 (日本語なら文節の選択になる)
        k.Key(VirtualKeys.LShift);
        k.Press(0x25); // ←
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("text:hello|down:25", string.Join("|", k.Host.Events), "確定してから Shift+← (範囲選択) を通す");
    }

    [Test]
    public static void KeysBeforeCapture_AreNotSwallowed()
    {
        var k = new Keyboard();
        Assert.True(!k.Key(VirtualKeys.Space), "変換ボックスが無いときの Space は素通し");
        Assert.True(!k.Key(VirtualKeys.Tab), "Tab など文字を生まないキーは素通し");
        Assert.True(k.Key(0x31), "数字は変換ボックスに入る (数字だけなら半角のまま、Space で空白)");
    }

    [Test]
    public static void EnglishVerb_PlusSuru()
    {
        // Twitter の報告: 「commitしてpushして」が こっみつぃてぷっして になる
        // (英単語の最後の t + s が つ になる / 末尾の pushsite は site も英単語なので全部英字になる)
        AssertCommitted(
            ("commitsitepushsite", "commitしてpushして"),
            ("commitsuru", "commitする"),
            ("commitsitekara", "commitしてから"),
            ("pushsite", "pushして"),
            ("gitpushsite", "gitpushして"),
            // #886: 知らない短い英字 (dl) + する の活用も、英字 + して (全体が英字の dlshite になっていた)
            ("dlshite", "dlして"),
            ("dlsuru", "dlする"),
            ("website", "website"),
            ("websitewomiru", "websiteをみる"),
            ("tetsudou", "てつどう"),
            // #422: declutter が辞書に無いと、助詞 de + clutter に割れて「むだをでclutterする」になる
            ("mudawodecluttersuru", "むだをdeclutterする"));
    }

    [Test]
    public static void DLsite_IsVerbOnlyAfterJapanese()
    {
        // #886: dl + して は shite と打てば dlして。dlsite はサイト名 (DLsite) のまま。
        foreach (var (before, typed, expected) in new[]
        {
            ("ファイルを", "dlshite", "ファイルをdlして"),
            ("", "fairuwodlshite", "ふぁいるをdlして"),
            ("", "DLshite", "DLして"),
            ("", "dlsite", "DLsite"),
            ("", "dlsitede", "DLsiteで"),
            ("", "dlsitenokaiin", "DLsiteのかいいん"),
        })
        {
            var k = new Keyboard();
            if (before.Length > 0) k.Host.PrecedingText = before;
            k.Type(typed + "\n");
            Assert.Equal(expected, before + k.Host.Document, typed);
        }
    }

    [Test]
    public static void Issue1899_TechnicalWordBeforeSuru_StaysEnglish()
    {
        // #1899: share は しゃれ とも読めるが、しゃれする・まけします は日本語として成り立たない。
        // 技術用語の綴りのすぐ後ろが する の活用なら、その区間は英単語として読む (文頭でも同じ)。
        AssertCommitted(
            ("fairusharesite", "ふぁいるshareして"),
            ("fairuwosharesite", "ふぁいるをshareして"),
            ("fairushareshite", "ふぁいるshareして"),
            ("fairusharesuru", "ふぁいるshareする"),
            ("fairushareshita", "ふぁいるshareした"),
            ("koresharesite", "これshareして"),
            ("kyounosharesite", "きょうのshareして"),
            ("mainnisharesite", "mainにshareして"),
            // share 以外の技術用語も同じ (make は まけ とも読める)
            ("fairumakeshite", "ふぁいるmakeして"),
            ("koremakeshimasu", "これmakeします"),
            ("korecaseshimasu", "これcaseします"),
            // おしゃれ・おまけ は 1 語の日本語として読む (お + share / お + make に割らない)
            ("osharesuru", "おしゃれする"),
            ("oshareshite", "おしゃれして"),
            ("omakesuru", "おまけする"),
            ("omakeshite", "おまけして"),
            // 4 文字以上という条件は無い (uri・sha も技術用語なら英語)
            ("koreurisuru", "これuriする"),
            ("koreurishite", "これuriして"),
            ("koreshasuru", "これshaする"),
            ("koreshashite", "これshaして"),
            // ごとする は日本語として成り立つので、goto は日本語のまま
            ("koregotosuru", "これごとする"),
            // 日本語由来の語 (ninja) は日本語の読みのまま
            ("ninjashite", "にんじゃして"),
            ("ninjasuru", "にんじゃする"),
            // 逆向き: する が続かない share 単体は今までどおり日本語 (しゃれ)
            ("share", "しゃれ"),
            // 文頭でも、する の活用が続けば英語
            ("sharesuru", "shareする"),
            ("sharesite", "shareして"),
            ("shareshitara", "shareしたら"),
            // する の活用は しない・します なども同じ
            ("shareshinai", "shareしない"),
            ("shareshimasu", "shareします"),
            ("shareshiteiru", "shareしている"),
            ("sharesareru", "shareされる"),
            ("sharesarenai", "shareされない"),
            ("makeshite", "makeして"),
            // させ は一段動詞の使役形 (負けさせた・褒めさせて) とも読む。読みが一段動詞の語幹なら日本語のまま
            ("wazatomakesaseta", "わざとまけさせた"),
            ("wazatomakesaserareta", "わざとまけさせられた"),
            ("aitewomakesasenaiyounisuru", "あいてをまけさせないようにする"),
            ("mottohomesasete", "もっとほめさせて"),
            ("homesaseru", "ほめさせる"),
            ("sharesaseru", "しゃれさせる"),
            ("sharesaseta", "しゃれさせた"),
            // され・せず は一段動詞の使役形ではないので、する の活用として英語のまま
            ("makesareta", "makeされた"),
            ("makesezu", "makeせず"),
            // させず は使役形なので、一段動詞の語幹なら日本語のまま
            ("makesasezu", "まけさせず"),
            ("wazatomakesasezu", "わざとまけさせず"),
            // 一段動詞の語幹でない技術用語は させ でも英語
            ("casesaseru", "caseさせる"),
            ("casesasezu", "caseさせず"),
            ("urisaseru", "uriさせる"),
            // 技術用語でない英単語 (memo・site) と付属語 (ため・ので) は今までどおり日本語
            ("memoshite", "めもして"),
            ("korememoshite", "これめもして"),
            ("fairumemoshite", "ふぁいるめもして"),
            ("tameshite", "ためして"),
            ("korewotameshite", "これをためして"),
            ("sounanodeshite", "そうなのでして"),
            ("korekarasiteshite", "これからしてして"));
    }

    [Test]
    public static void EnglishVerb_PlusSuru_PendingConsonantIsShownOnce()
    {
        // 英単語 + する の活用の打ちかけの子音が 2 回出ていた (googleしまss)。Space で変換する読みも同じ
        foreach (var (typed, expected, converted) in new[]
        {
            ("googleshimasen", "googleしません", "googleしません"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, typed);
            k.Type(" ");
            Assert.Equal(converted, k.Showing, typed + " (Space)");
        }
    }

    [Test]
    public static void SpaceAroundEnglish_AddsHalfWidthSpaces()
    {
        // Twitter の要望: 半角英語の前後に半角スペース (設定、最初は OFF)
        foreach (var (text, before, after, expected) in new (string, string?, string?, string)[]
        {
            ("今日はGitHubにpushした", null, null, "今日は GitHub に push した"),
            ("iPhone15を買った", null, null, "iPhone15 を買った"),
            ("3時に行く", null, null, "3時に行く"),                  // 数字だけには入れない
            ("GitHubで、pushした。", null, null, "GitHub で、push した。"), // 記号の隣には入れない
            ("C++の本", null, null, "C++ の本"),
            ("Hello.今日は", null, null, "Hello.今日は"),            // 語の端の記号の後ろには入れない
            ("に", "GitHub", null, " に"),                           // 前に確定した英単語に続ける
            ("GitHub", "今日は", "に", " GitHub "),                  // キャレットの前後が日本語
            ("I want to go", null, null, "I want to go"),          // 英文はそのまま
        })
        {
            Assert.Equal(expected, EnglishSpacing.Spaced(text, before, after), text);
        }
    }

    [Test]
    public static void Issue958_SpaceAroundNumbers()
    {
        // #958: 設定「半角数字の前後に半角スペース」。英単語の設定とは別に切り替える
        foreach (var (text, before, after, english, expected) in new (string, string?, string?, bool, string)[]
        {
            ("この処理を3回実行します", null, null, false, "この処理を 3 回実行します"),
            ("ファイルを5個選択します", null, null, false, "ファイルを 5 個選択します"),
            ("3,000円です", null, null, false, "3,000 円です"),
            ("３回", null, null, false, "３回"),                         // 全角数字には入れない
            ("GitHubに3回push", null, null, false, "GitHubに 3 回push"), // 英単語の設定が OFF なら英単語には入れない
            ("GitHubに3回push", null, null, true, "GitHub に 3 回 push"),
            ("回", "3", null, false, " 回"),                             // 前に確定した数字に続ける
            ("3", "今日は", "回", false, " 3 "),
            ("を", "iPhone15", null, false, "を"),                     // 前の語に英字があれば英単語の設定に従う (5 だけを見ない: #1072 のレビュー)
            ("を", "iPhone15", null, true, " を"),
            ("買う", null, "15Pro", false, "買う"),                     // キャレットの後ろの語も同じ
        })
        {
            Assert.Equal(expected, EnglishSpacing.Spaced(text, before, after, english, numbers: true), text);
        }
    }

    [Test]
    public static void SpaceAroundEnglish_WhenTypingInTheCompositionBox()
    {
        // #248: 英数を使わず、変換ボックスの中で英語と日本語を判定して打っても、設定「英単語の前後に半角スペース」が効く
        foreach (var (typed, expected) in new[]
        {
            ("kyouhaGooglenipushshita\n", "きょうは Google に push した"),
            ("pingwoutsu\n", "ping をうつ"),
            ("meetingga\n", "meeting が"),
            ("kyouhashoppingniiku\n", "きょうは shopping にいく"),
            // 別々に確定しても、前に確定した英単語との間に入れる
            ("GitHub\nni\n", "GitHub に"),
        })
        {
            var k = new Keyboard { SpaceAroundEnglish = true };
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void SigilWord_AtStartPassesNameToTheApp()
    {
        // #193: AI エージェントの /command・$skill・@ファイル名 は、変換ボックスに溜めずに打つたびにアプリへ渡す (補完を選べるように)。
        foreach (var word in new[] { "/review", "$skill-name", "@src/app.ts", "/kensaku" })
        {
            // 先頭の / をそのまま渡すのはコードエディターのコードの中だけ (文章の入力欄では ・ にする)。
            var k = new Keyboard { Code = word[0] == '/' };
            k.Type(word);
            Assert.True(!k.Captured && k.Showing is null, $"{word}: 変換ボックスを開かない");
            Assert.Equal(0, k.Host.Output.Count, word);
            Assert.True(k.InSigilWord, $"{word}: 名前の途中");
        }
    }

    [Test]
    public static void SlashAtStart_IsMiddleDotInText()
    {
        // 文章の入力欄の打ち始めの / は ・ (Microsoft IME と同じ)。続けて打てば日本語のまま。
        var k = new Keyboard();
        k.Type("/");
        Assert.Equal("・", k.Showing);
        k.Type("kyou");
        Assert.Equal("・きょう", k.Showing);
        // Space で / にしてから名前を打つと、/ を確定して、名前は半角のままアプリへ渡す。名前の後の空白で日本語に戻る。
        k = new Keyboard();
        k.Type("/ ");
        Assert.Equal("/", k.Showing);
        k.Type("help");
        Assert.Equal("/", string.Concat(k.Host.Output));
        Assert.True(!k.Captured && k.InSigilWord, "/help の help はアプリへ");
        Assert.Equal("/help", k.Host.Document, "help はアプリへ");
        k.Type(" kyou");
        Assert.Equal("きょう", k.Showing);
        // 改行した後の行の先頭の / も ・ (1 回目の Enter は確定だけ)。
        k = new Keyboard();
        k.Type("kyouha\n\n/");
        Assert.Equal("・", k.Showing);
        // コードエディターのコードの中は今までどおり、そのままアプリへ渡す。
        k = new Keyboard { Code = true };
        k.Type("/help");
        Assert.True(!k.Captured && k.InSigilWord, "コードの /help はアプリへ");
    }

    [Test]
    public static void SigilWord_SpaceAfterNameReturnsToNormal()
    {
        // ターミナルの AI の入力行 (Claude Code の「> 」の後)
        var k = new Keyboard { Code = true };
        k.Type("> /review kyouha");
        Assert.Equal("きょうは", k.Showing);
        k.Press(VirtualKeys.Return);
        Assert.Equal("きょうは", k.Host.Output.Single());
        Assert.Equal("> /review きょうは", k.Host.Document, "/ と空白はアプリへ");
    }

    [Test]
    public static void SigilWord_AfterCommittedEnglishWordAndSpace()
    {
        // ターミナルの AI の入力行で、google を Space で確定した (google + 空白) 後の /help も名前として通す。
        var k = new Keyboard { Code = true };
        k.Type("> google /help");
        Assert.Equal("google ", string.Concat(k.Host.Output));
        Assert.True(!k.Captured, "/help は変換ボックスに入れない");
        // Enter (送信・改行) の後の行の先頭も。
        k.Press(VirtualKeys.Return);
        k.Type("$deploy");
        Assert.True(!k.Captured && k.InSigilWord, "Enter の後の $deploy も通す");
    }

    [Test]
    public static void SigilWord_NotAfterLetters()
    {
        // メールアドレスの @ (前が英字) は対象外。変換ボックスの中で打った @ もそのまま変換ボックスで扱う。
        var k = new Keyboard();
        k.Type("taro@example.com");
        Assert.True(k.Captured && k.Showing!.Contains('@'), "taro@ の @ は変換ボックスの中");
        Assert.True(!k.InSigilWord, "名前として扱わない");
        // 日本語を確定した直後 (空白なし) の / も対象外。
        k = new Keyboard();
        k.Type("kyouha\n/");
        Assert.True(k.Captured, "今日は/ の / は変換ボックスに入れる");
        // ホストが前の文字を教えてくれれば、それで決める (打ち始めでも前が英字なら対象外)。
        k = new Keyboard();
        k.Host.PrecedingText = "taro";
        k.Type("@");
        Assert.True(k.Captured, "前が taro なら @ は変換ボックスに入れる");
        k = new Keyboard { Code = true };
        k.Host.PrecedingText = "> ";
        k.Press(VirtualKeys.Left);
        k.Type("/");
        Assert.True(!k.Captured, "前の文字が空白と分かれば、キャレットを動かした後でも / を通す");
    }

    [Test]
    public static void SigilWord_DirectModeDoesNotDetectRomaji()
    {
        // 英数状態でも、/ の後ろの名前はローマ字として判定しない (/kensaku を日本語にしない)。
        var k = new Keyboard(direct: true);
        k.Type("/kensaku");
        Assert.True(k.Direct, "英数状態のまま");
        Assert.True(!k.Captured && k.Host.Output.Count == 0, "打ったキーはそのままアプリへ");
    }

    [Test]
    public static void SigilWord_SettingOff()
    {
        var k = new Keyboard { SigilWords = false };
        k.Type("/review");
        Assert.True(k.Captured, "OFF なら今までどおり変換ボックスに入れる");
    }

    [Test]
    public static void Issue455_OemNoPasokon()
    {
        // Issue #455: oem は 3 文字の略語。oemnopasokonn を おえもの と読まず、oem + の + ぱそこん。
        // 漢字変換のないランナーでは、打った小文字のまま oemのぱそこん。おえむ (oemu) は日本語のまま。
        var k = new Keyboard();
        k.Type("oemnopasokonn\n");
        Assert.Equal("oemのぱそこん", k.Host.Document);
        k = new Keyboard();
        k.Type("oemu\n");
        Assert.Equal("おえむ", k.Host.Document);
    }

    [Test]
    public static void Issue407_TsuideNiFetch()
    {
        // Issue #407: ついでに の途中の ide を英単語にしない。見出しが先頭の ideにfetch は英字のまま。
        AssertCommitted(
            ("tuidenifetch upstream", "ついでにfetch upstream"),
            ("idenifetch", "ideにfetch"));
    }

    [Test]
    public static void Issue338_ZArrowKeys()
    {
        // Issue #338: zh / zj / zk / zl は Microsoft IME と同じ矢印。z + 記号は今までどおり。zz の次の l (puzzle) は矢印にしない。
        AssertCommitted(
            ("zh", "←"), ("zj", "↓"), ("zk", "↑"), ("zl", "→"),
            ("z/", "・"), ("zu", "ず"), ("puzzle", "puzzle"));
    }

    [Test]
    public static void Issue2013_ZArrowAfterKanaIsNotTypo()
    {
        // Issue #2013: かなの後ろの zl (→) を、確定のときに前のローマ字と合わせて打ち間違い (hogezo) として ぞ に直さない。
        // z + 母音 (hogezo) は今までどおり ぞ。
        AssertCommitted(
            ("hogezl", "ほげ→"), ("hogezlfuga", "ほげ→ふが"), ("hogezk", "ほげ↑"), ("truezlfalse", "true→false"),
            ("hogezo", "ほげぞ"));
    }

    [Test]
    public static void Issue1008_ZSymbolsCanBeTurnedOff()
    {
        // Issue #1008: 設定「z + 記号・hjkl で記号・矢印を入れる」を OFF にすると、z はほかの英字と同じ (1000Hz. の z. を … にしない)
        foreach (var (typed, expected) in new[] { ("z.", "z."), ("zh", "zh"), ("zkai", "zかい"), ("zu", "ず") })
        {
            var k = new Keyboard { ZSymbols = false };
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
        var on = new Keyboard();
        on.Type("z.\n");
        Assert.Equal("…", on.Host.Document, "既定 (ON) は今までどおり");
    }

    [Test]
    public static void Issue989_NumberKeysCommitConversion()
    {
        // #989: 設定を OFF にすると、変換中の上の段の数字は、今の候補で確定してから数字を入力する (zu Space 1 → 図1)
        var off = new Keyboard { NumberKeysSelectCandidates = false };
        off.Type("kyouha ");
        Assert.True(off.Host.View is { Converting: true }, "変換中");
        off.Type("10\n");
        Assert.Equal("今日は10", off.Host.Document);
        // 既定 (ON) は今までどおり番号で選ぶ (数字は入らない)
        var on = new Keyboard();
        on.Type("kyouha ");
        on.Type("1");
        Assert.True(!on.Host.Document.Contains('1'), "番号で選ぶ: " + on.Host.Document);
    }

    [Test]
    public static void Issue885_SuitYoku()
    {
        // Issue #885: suit は英単語だが、t + yo を ちょ と読むと すいちょく (垂直) になるので、全体を日本語にする。
        // 英単語の後ろの助詞 (githubya → githubや) と、日本語の読みにならない英単語 + yo (editよ) は今までどおり英字を残す。
        AssertCommitted(
            ("suityoku", "すいちょく"),
            ("githubya", "GitHubや"),
            ("edityo", "editよ"));
        var text = new Composition.CompositionTextState();
        var env = TestSupport.Env(Detector);
        foreach (var c in "suityoku") CompositionText.Append(text, env, c);
        Assert.Equal("JA:suityoku", string.Join(" | ", CompositionText.Segments(text, env).Select(s => (s.IsEnglish ? "EN:" : "JA:") + s.Raw)));
    }

    [Test]
    public static void CustomRomajiTable_PendingDisplayShowsTypedLetters()
    {
        // issue #979 レビュー: 打ちかけの表示が んg から ng に変わり、続けて打つと登録した読みになることを、
        // Core の変換ボックス (CompositionText) で確かめる (Core の変更なので、どの OS でも同じ動き)。
        var dir = Path.Combine(Path.GetTempPath(), $"meltype-romaji-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "romaji.txt"), "ngh\tにゅう\n");
            var detector = CompositionDetector.CreateDefault(dir);
            var text = new Composition.CompositionTextState();
            var env = TestSupport.Env(detector);
            CompositionText.Append(text, env, 'n');
            Assert.Equal("n", CompositionText.Display(text, env, final: false), "n だけなら打ちかけのまま");
            CompositionText.Append(text, env, 'g');
            Assert.Equal("ng", CompositionText.Display(text, env, final: false), "表の綴りの打ちかけは んg ではなく ng と表示する");
            CompositionText.Append(text, env, 'h');
            Assert.Equal("にゅう", CompositionText.Display(text, env, final: false), "続けて打つと登録した読みになる");
            // 表を登録していない場合 (main 側の判定に従う)
            var plain = new Composition.CompositionTextState();
            var plainEnv = TestSupport.Env(Detector);
            foreach (var c in "ng") CompositionText.Append(plain, plainEnv, c);
            // main の判定では、語頭の ng は表の有無に関係なく英字の ng として表示される (main だけの状態でも同じ結果を確認済み)。
            // 表の登録で変わるのは、続けて h を打ったときに にゅう になるかどうか。
            Assert.Equal("ng", CompositionText.Display(plain, plainEnv, final: false), "表が無いときの ng は main と同じ表示");
            CompositionText.Append(plain, plainEnv, 'h');
            Assert.True(CompositionText.Display(plain, plainEnv, final: false) != "にゅう", "表が無ければ ngh は にゅう にならない");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public static void Issue1945_DirectModeLogHidesTypedKeyUnlessRecordTextIsOn()
    {
        // 英数状態の判定を数字で打ち切ったログに、打った数字のキーコード (kana1 の 1 で 0x31) が設定に関係なく残っていた。
        var recordText = Meltype.Diagnostics.Log.RecordText;
        try
        {
            // 数字のほか、記号 (, は 0xBC)  で打ち切ったときも同じ。
            // 逆向き: OFF でもログ自体は残し (判定を打ち切ったことは調べるときに要る)、ON ならキーを残す。
            foreach (var (keys, code) in new[] { ("kana1", "0x31"), ("kana,", "0xBC") })
            {
                foreach (var withText in new[] { false, true })
                {
                    Meltype.Diagnostics.Log.RecordText = withText;
                    var version = Meltype.Diagnostics.Log.Version;
                    new Keyboard(direct: true).Type(keys);
                    var logged = Meltype.Diagnostics.Log.Snapshot().TakeLast((int)Math.Min(Meltype.Diagnostics.Log.Version - version, 1000)).Select(e => e.Message).ToList();
                    var entry = logged.LastOrDefault(m => m.StartsWith("英数状態の判定を打ち切り", StringComparison.Ordinal));
                    Assert.True(entry is not null, $"{keys}: 設定に関係なく打ち切りのログは残す");
                    Assert.Equal(withText, entry!.Contains(code), $"{keys}: 打ったキーは設定どおり: " + entry);
                    // OFF なら、この打鍵で出たどのログにも打った英字が出ない
                    if (!withText) Assert.True(!logged.Any(m => m.Contains("kana")), $"{keys}: 打った英字を残さない: " + string.Join(" / ", logged));
                }
            }
        }
        finally
        {
            Meltype.Diagnostics.Log.RecordText = recordText;
        }
    }

    [Test]
    public static void Issue1644_CommitLogsSegmentReason()
    {
        // 確定のたびに、区切りの重みと次点をログに残す。既定では打った文字を出さず、区間の言語と文字数だけ
        var recordText = Meltype.Diagnostics.Log.RecordText;
        try
        {
            foreach (var withText in new[] { false, true })
            {
                Meltype.Diagnostics.Log.RecordText = withText;
                var entry = CommitLog(new Keyboard(), "kyouhagoogle\n");
                Assert.True(entry.Contains("区切り ") && entry.Contains("次点 "), "区切りと次点: " + entry);
                Assert.Equal(withText, entry.Contains("google"), "打った文字は設定どおり: " + entry);
                if (!withText) Assert.True(entry.Contains("EN(6)"), "文字の代わりに文字数: " + entry);
            }

            // 保護区間を含む入力も、保護区間以外の区切りの重みを出す
            var text = new Composition.CompositionTextState();
            var env = TestSupport.Env(Detector);
            foreach (var c in "@kuraido kyouha") CompositionText.Append(text, env, c);
            var mention = CompositionText.ExplainSegments(text, env);
            Assert.True(mention.Contains("保護区間") && mention.Contains("区切り "), "保護区間と、ほかの区間の区切り: " + mention);
        }
        finally
        {
            Meltype.Diagnostics.Log.RecordText = recordText;
        }

        static string CommitLog(Keyboard k, string keys)
        {
            k.Type(keys);
            return Meltype.Diagnostics.Log.Snapshot().Last(e => e.Level == Meltype.Diagnostics.LogLevel.Decision && e.Message.StartsWith("確定:", StringComparison.Ordinal)).Message;
        }
    }

    [Test]
    public static void KeigoMode_RealtimeConversion()
    {
        foreach (var (typed, expected) in new[]
        {
            ("ryoukai", "承知いたしました"),
            ("arigatou", "ありがとうございます"),
            ("sumimasen", "申し訳ございません"),
            ("kyouhaiku", "本日は伺います"),
            ("sakkiokutta", "先ほどお送りいたしました"),
            ("ohayo", "おはようございます"),
            ("makasete", "お任せください"),
            ("osakini", "お先に失礼いたします"),
            ("daijoubudesu", "問題ございません"),
            ("susunda", "進みました"),
            ("ryoukai.", "承知いたしました。"),
            ("ryoukai,", "承知いたしました、"),
            ("ryoukai!", "承知いたしました！"),
            ("kyouhaiku.", "本日は伺います。"),
            ("kyouhaiku!", "本日は伺います！"),
            ("kyouhaiku?", "本日は行かれますでしょうか？"),
            ("daijoubu?", "問題ございませんでしょうか？"),
            ("arigatou!", "ありがとうございます！"),
            ("oshiete!", "ご教示ください！"),
        })
        {
            var k = new Keyboard(live: true) { KeigoMode = true };
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"ライブ変換: {typed}");
            Assert.True(k.Host.View?.ContextHint?.StartsWith("リアルタイム敬語変換 ON", StringComparison.Ordinal) == true, "入力中にリアルタイム敬語変換 ON を表示");
            k.Type("\n");
            Assert.Equal(expected, k.Host.Document, $"確定: {typed}");
        }

        // 二重敬語・疑問文の精度確認と純粋関数性の検証 (KeigoConverter.Convert 直接)
        var doubleKeigo = KeigoConverter.Convert([new("ありがとう", false, ["ありがとう"]), new("ございます", false, ["ございます"])]);
        Assert.Equal("ありがとうございます", string.Concat(doubleKeigo.Select(c => c.Text)), "二重敬語にしない");

        var pureInput = new List<Clause> { new("りょうかい", false, ["了解"]) };
        var pureOutput = KeigoConverter.Convert(pureInput);
        Assert.Equal("了解", pureInput[0].Text, "純粋関数として入力の Clause を書き換えない");
        Assert.Equal("承知いたしました", pureOutput[0].Text, "戻り値の新しい Clause 列が敬語になる");

        var question = KeigoConverter.Convert([new("みた？", false, ["見た？"])]);
        Assert.Equal("ご覧になりましたでしょうか？", question[0].Text, "疑問文は相手への尊敬・丁寧形にする");

        var pastDa = KeigoConverter.Convert([new("よんだ", false, ["読んだ"])]);
        Assert.Equal("拝読いたしました", pastDa[0].Text, "動詞の過去形を「読んです」にしない");

        var nominalDa = KeigoConverter.Convert([new("よていだ", false, ["予定だ"])]);
        Assert.Equal("予定です", nominalDa[0].Text, "名詞+だ は です にする");

        var adj = KeigoConverter.Convert([new("むずかしい", false, ["難しい"])]);
        Assert.Equal("難しいです", adj[0].Text, "い形容詞の文末は です を付ける");

        var politeAlready = KeigoConverter.Convert([new("かくにんいたします", false, ["確認いたします"])]);
        Assert.Equal("確認いたします", politeAlready[0].Text, "すでに敬語の文末は変えない");

        var noFalseKyou = KeigoConverter.Convert([new("いまにほん", false, ["今日本"])]);
        Assert.Equal("今日本", noFalseKyou[0].Text, "「今日本」の途中を「本日」に誤置換しない");

        // 句読点・感嘆符・疑問符が別文節に分かれても元の文にリセットされない
        var splitPeriod = KeigoConverter.Convert([new("きょうは", false, ["今日は"]), new("いく", false, ["行く"]), new("。", false, ["。"])]);
        Assert.Equal("本日は伺います。", string.Concat(splitPeriod.Select(c => c.Text)), "別文節の句点でも敬語を保つ");

        var splitComma = KeigoConverter.Convert([new("りょうかい", false, ["了解"]), new("、", false, ["、"])]);
        Assert.Equal("承知いたしました、", string.Concat(splitComma.Select(c => c.Text)), "別文節の読点でも敬語を保つ");

        var splitExcl = KeigoConverter.Convert([new("おしえて", false, ["教えて"]), new("！", false, ["！"])]);
        Assert.Equal("ご教示ください！", string.Concat(splitExcl.Select(c => c.Text)), "別文節の感嘆符でも敬語を保つ");

        var splitQuest = KeigoConverter.Convert([new("みた", false, ["見た"]), new("？", false, ["？"])]);
        Assert.Equal("ご覧になりましたでしょうか？", string.Concat(splitQuest.Select(c => c.Text)), "別文節の疑問符でも尊敬・丁寧疑問形にする");

        // ですます調からの敬語変換
        var masu = KeigoConverter.Convert([new("たべます", false, ["食べます"])]);
        Assert.Equal("いただきます", masu[0].Text, "ですます調 (食べます) も敬語にする");

        var masuTeiru = KeigoConverter.Convert([new("かくにんしています", false, ["確認しています"])]);
        Assert.Equal("確認しております", masuTeiru[0].Text, "ですます調 (確認しています) も敬語にする");

        // 主語の手がかりによる尊敬語 / 謙譲語の自動判別
        var sonkeigo = KeigoConverter.Convert([new("しゃちょうが", false, ["社長が"]), new("みます", false, ["見ます"])]);
        Assert.Equal("社長がご覧になります", string.Concat(sonkeigo.Select(c => c.Text)), "目上が主語のときは尊敬語");

        var sonkeigoBefore = KeigoConverter.Convert([new("きます", false, ["来ます"])], before: "お客様は");
        Assert.Equal("お越しになります", sonkeigoBefore[0].Text, "直前の文脈に相手主語があるときも尊敬語");

        var kenjougo = KeigoConverter.Convert([new("わたしが", false, ["私が"]), new("みます", false, ["見ます"])]);
        Assert.Equal("私が拝見いたします", string.Concat(kenjougo.Select(c => c.Text)), "自分が主語のときは謙譲語");

        var afterSentenceBreak = KeigoConverter.Convert([new("いきます", false, ["行きます"])], before: "お客様が来ました。");
        Assert.Equal("伺います", afterSentenceBreak[0].Text, "句点で文が切れたら前の文の相手主語を引きずらない");

        var teHoshii = KeigoConverter.Convert([new("みてほしい", false, ["見てほしい"])]);
        Assert.Equal("ご覧いただけますでしょうか", teHoshii[0].Text, "てほしい系も正しく敬語になる");

        // 途中の読点 (、) で文末敬語に化けないこと
        var midComma = KeigoConverter.Convert([new("みて、", false, ["見て、"]), new("れんらくする", false, ["連絡する"])]);
        Assert.Equal("見て、ご連絡いたします", string.Concat(midComma.Select(c => c.Text)), "途中の読点を文末の『ご覧ください、』にしない");

        // 五段サ行動詞 (押した・思い出した) を『押いたしました』にせず『押しました』にし、サ変動詞 (完了した) は『完了いたしました』にする
        var godanSa = KeigoConverter.Convert([new("おした", false, ["押した"])]);
        Assert.Equal("押しました", godanSa[0].Text, "五段サ行は『押いたしました』にせず『押しました』");

        var suruNoun = KeigoConverter.Convert([new("かんりょうした", false, ["完了した"])]);
        Assert.Equal("完了いたしました", suruNoun[0].Text, "サ変動詞は『完了いたしました』");

        // 疑問文の誤変換防止 (食べました？・危ない？・可愛い？)
        var politeQuest = KeigoConverter.Convert([new("たべました？", false, ["食べました？"])]);
        Assert.Equal("食べましたでしょうか？", politeQuest[0].Text, "『食べまされましたでしょうか？』にしない");

        var adjQuest = KeigoConverter.Convert([new("あぶない？", false, ["危ない？"])]);
        Assert.Equal("危ないでしょうか？", adjQuest[0].Text, "『危ございませんでしょうか？』にしない");

        // 会社・ビジネス実戦 (複文接続・二重敬語回避・メール定型)
        var compound = KeigoConverter.Convert([new("しゅうせいしたから、", false, ["修正したから、"]), new("みてほしい", false, ["見てほしい"])]);
        Assert.Equal("修正いたしましたので、ご覧いただけますでしょうか", string.Concat(compound.Select(c => c.Text)), "複文の『〜したから』もビジネス敬語にする");

        var compoundKedo = KeigoConverter.Convert([new("よていだけど、", false, ["予定だけど、"]), new("れんらくする", false, ["連絡する"])]);
        Assert.Equal("予定ですが、ご連絡いたします", string.Concat(compoundKedo.Select(c => c.Text)), "名詞+だけど → ですが");

        var bizQuest = KeigoConverter.Convert([new("かくにんした？", false, ["確認した？"])]);
        Assert.Equal("ご確認いただけましたでしょうか？", bizQuest[0].Text, "二重敬語『ご確認されましたでしょうか』を避ける");

        var doneQuest = KeigoConverter.Convert([new("おわった？", false, ["終わった？"])]);
        Assert.Equal("お済みでしょうか？", doneQuest[0].Text, "ビジネスで自然な完了確認");

        var bizIdiom = KeigoConverter.Convert([new("おせわになります", false, ["お世話になります"])]);
        Assert.Equal("お世話になっております", bizIdiom[0].Text, "メール書き出し定型");

        // 複数文節に分かれた定型句マージ・ごご重複防止・否定形・相手主語の確認系
        var splitCompanyOriginal = new List<Clause> { new("うちの", false, ["うちの"]), new("かいしゃ", false, ["会社"]), new("から", false, ["から"]) };
        var splitCompany = KeigoConverter.Convert(splitCompanyOriginal);
        Assert.Equal(3, splitCompanyOriginal.Count, "定型句マージ時も入力リストは変更しない");
        Assert.Equal("弊社から", string.Concat(splitCompany.Select(c => c.Text)), "文中の複数文節定型句もマージして敬語化");

        var noDupGo = KeigoConverter.Convert([new("ごれんらくする", false, ["ご連絡する"])]);
        Assert.Equal("ご連絡いたします", noDupGo[0].Text, "『ごご連絡いたします』にならない");

        var negTeiru = KeigoConverter.Convert([new("かくにんしてない", false, ["確認してない"])]);
        Assert.Equal("確認しておりません", negTeiru[0].Text, "『〜てない』を『〜ておりません』にする");

        var bossCheck = KeigoConverter.Convert([new("ぶちょうが", false, ["部長が"]), new("かくにんする", false, ["確認する"])]);
        Assert.Equal("部長がご確認になります", string.Concat(bossCheck.Select(c => c.Text)), "目上主語の確認する → ご確認になります");

        var askNegative = KeigoConverter.Convert([new("おしえてくれない？", false, ["教えてくれない？"])]);
        Assert.Equal("ご教示いただけませんでしょうか？", askNegative[0].Text, "〜てくれない？ → いただけませんでしょうか？");

        // 読点直後の即時複文変換・過去否定・勧誘形・割合記号+だ
        var commaImmediate = KeigoConverter.Convert([new("しゅうせいしたから、", false, ["修正したから、"])]);
        Assert.Equal("修正いたしましたので、", commaImmediate[0].Text, "読点直後でまだ続きを打っていなくても複文接続を即時変換");

        var pastNeg = KeigoConverter.Convert([new("おわってなかった", false, ["終わってなかった"])]);
        Assert.Equal("終わっておりませんでした", pastNeg[0].Text, "〜てなかった → 〜ておりませんでした");

        var volitional = KeigoConverter.Convert([new("そうだんしよう", false, ["相談しよう"])]);
        Assert.Equal("ご相談いたしましょう", volitional[0].Text, "〜しよう → 〜いたしましょう");

        var percentDa = KeigoConverter.Convert([new("100%だ", false, ["100%だ"])]);
        Assert.Equal("100%です", percentDa[0].Text, "記号+だ → です");

        // 尊敬語と謙譲語のさらなる判別 (進行形・自分主語の疑問文・〜てくれた)
        var bossProgressive = KeigoConverter.Convert([new("ぶちょうが", false, ["部長が"]), new("みている", false, ["見ている"])]);
        Assert.Equal("部長がご覧になっています", string.Concat(bossProgressive.Select(c => c.Text)), "目上主語の進行形 → ご覧になっています");

        var selfQuestion = KeigoConverter.Convert([new("わたしが", false, ["私が"]), new("おくった？", false, ["送った？"])]);
        Assert.Equal("私がお送りいたしましたでしょうか？", string.Concat(selfQuestion.Select(c => c.Text)), "自分主語の疑問文は謙譲語");

        var clientGave = KeigoConverter.Convert([new("おきゃくさまが", false, ["お客様が"]), new("おくってくれた", false, ["送ってくれた"])]);
        Assert.Equal("お客様がお送りくださいました", string.Concat(clientGave.Select(c => c.Text)), "〜てくれた → お送りくださいました");

        // 口語 → ビジネス書き言葉の言い換え (後で → 後ほど 等)
        var atode = KeigoConverter.Convert([new("あとで", false, ["後で"]), new("おくる", false, ["送る"])]);
        Assert.Equal("後ほどお送りいたします", string.Concat(atode.Select(c => c.Text)), "後で → 後ほど");

        var konaida = KeigoConverter.Convert([new("こないだの", false, ["こないだの"]), new("けん", false, ["件"])]);
        Assert.Equal("先日の件", string.Concat(konaida.Select(c => c.Text)), "こないだの → 先日の");

        var ikaga = KeigoConverter.Convert([new("どうだった？", false, ["どうだった？"])]);
        Assert.Equal("いかがでしたでしょうか？", ikaga[0].Text, "どうだった？ → いかがでしたでしょうか？");

        // 誤爆防止 (午後で・早急に・ものすごく) とビジネス感謝・謝罪表現
        var gogo = KeigoConverter.Convert([new("ごごで", false, ["午後で"]), new("おねがい", false, ["お願い"])]);
        Assert.Equal("午後でお願いいたします", string.Concat(gogo.Select(c => c.Text)), "「午後で」を「午後ほど」に誤爆しない");

        var soukyuu = KeigoConverter.Convert([new("そうきゅうに", false, ["早急に"]), new("たいおうする", false, ["対応する"])]);
        Assert.Equal("早急に対応いたします", string.Concat(soukyuu.Select(c => c.Text)), "「早急に」を「早急遽」に誤爆しない");

        var thanksBiz = KeigoConverter.Convert([new("かくにんしてくれてありがとう", false, ["確認してくれてありがとう"])]);
        Assert.Equal("ご確認いただきありがとうございます", thanksBiz[0].Text, "〜してくれてありがとう → ご〜いただきありがとうございます");

        // 客 → お客様、客室誤爆防止、来るのでの自他判別 (読点が別文節・確定済み文脈のときも主語を引きずらない)
        var kyakuKuru = KeigoConverter.Convert([new("きゃくが", false, ["客が"]), new("くるので、", false, ["来るので、"]), new("じゅんびする", false, ["準備する"])]);
        Assert.Equal("お客様がお越しになりますので、準備いたします", string.Concat(kyakuKuru.Select(c => c.Text)), "客→お客様 & 相手主語の来るので→お越しになりますので");

        var kyakuKuruSplitComma = KeigoConverter.Convert([new("きゃくが", false, ["客が"]), new("くるので", false, ["来るので"]), new("、", false, ["、"]), new("じゅんびする", false, ["準備する"])]);
        Assert.Equal("お客様がお越しになりますので、準備いたします", string.Concat(kyakuKuruSplitComma.Select(c => c.Text)), "読点が別文節でも『準備なさいます』にしない");

        var kyakuKuruAfterBefore = KeigoConverter.Convert([new("じゅんびする", false, ["準備する"])], before: "お客様がお越しになりますので、");
        Assert.Equal("準備いたします", kyakuKuruAfterBefore[0].Text, "読点まで確定済みでも前の節の主語を引きずらない");

        var subjectWithComma = KeigoConverter.Convert([new("おきゃくさまは", false, ["お客様は"]), new("、", false, ["、"]), new("みます", false, ["見ます"])]);
        Assert.Equal("お客様は、ご覧になります", string.Concat(subjectWithComma.Select(c => c.Text)), "主語直後の読点では尊敬語を維持する");

        var selfKuru = KeigoConverter.Convert([new("くるので、", false, ["来るので、"]), new("まって", false, ["待って"])]);
        Assert.Equal("参りますので、少々お待ちください", string.Concat(selfKuru.Select(c => c.Text)), "自分主語の来るので→参りますので");

        var kyakushitsu = KeigoConverter.Convert([new("きゃくしつを", false, ["客室を"]), new("かくにんする", false, ["確認する"])]);
        Assert.Equal("客室を確認いたします", string.Concat(kyakushitsu.Select(c => c.Text)), "「客室」を「お客様室」に誤爆しない");

        // 〜てくれて の文脈振り分け (感謝が続く → いただき / 相手主語の経過 → くださり)
        var tekureteThanks = KeigoConverter.Convert([new("おしえてくれて", false, ["教えてくれて"]), new("たすかった", false, ["助かった"])]);
        Assert.Equal("ご教示いただき大変助かりました", string.Concat(tekureteThanks.Select(c => c.Text)), "感謝・恩恵が続くときは『〜いただき』");

        var tekureteBoss = KeigoConverter.Convert([new("ぶちょうが", false, ["部長が"]), new("おくってくれて、", false, ["送ってくれて、"]), new("たすかった", false, ["助かった"])]);
        Assert.Equal("部長がお送りいただき大変助かりました", string.Concat(tekureteBoss.Select(c => c.Text)).Replace("いただき、", "いただき"), "助かった直前は『〜いただき』");

        var tekureteAction = KeigoConverter.Convert([new("ぶちょうが", false, ["部長が"]), new("おくってくれて、", false, ["送ってくれて、"]), new("かくにんした", false, ["確認した"])]);
        Assert.Equal("部長がお送りくださり、確認いたしました", string.Concat(tekureteAction.Select(c => c.Text)), "相手主語＋一般動作が続くときは『〜くださり』");

        // 拡充した語彙・ビジネス表現・依頼・疑問・尊敬語
        var hisashiburi = KeigoConverter.Convert([new("ひさしぶり", false, ["ひさしぶり"])]);
        Assert.Equal("ご無沙汰しております", hisashiburi[0].Text, "ひさしぶり → ご無沙汰しております");

        var tsuideni = KeigoConverter.Convert([new("ついでに", false, ["ついでに"]), new("せつめいする", false, ["説明する"])]);
        Assert.Equal("併せてご説明いたします", string.Concat(tsuideni.Select(c => c.Text)), "ついでに → 併せて / 説明する → ご説明いたします");

        var received = KeigoConverter.Convert([new("うけとった", false, ["受け取った"])]);
        Assert.Equal("拝受いたしました", received[0].Text, "受け取った → 拝受いたしました");

        var bossApproved = KeigoConverter.Convert([new("ぶちょうが", false, ["部長が"]), new("しょうにんした", false, ["承認した"])]);
        Assert.Equal("部長がご承認なさいました", string.Concat(bossApproved.Select(c => c.Text)), "目上主語の承認した → ご承認なさいました");

        var fixRequest = KeigoConverter.Convert([new("しゅうせいしてほしい？", false, ["修正してほしい？"])]);
        Assert.Equal("ご修正いただけますでしょうか？", fixRequest[0].Text, "修正してほしい？ → ご修正いただけますでしょうか？");

        var reachableQuest = KeigoConverter.Convert([new("とどいた？", false, ["届いた？"])]);
        Assert.Equal("届きましたでしょうか？", reachableQuest[0].Text, "届いた？ → 届きましたでしょうか？");

        var tekureteExplain = KeigoConverter.Convert([new("せつめいしてくれて", false, ["説明してくれて"]), new("ありがとう", false, ["ありがとう"])]);
        Assert.Equal("ご説明いただきありがとうございます", string.Concat(tekureteExplain.Select(c => c.Text)), "説明してくれて＋ありがとう → ご説明いただきありがとうございます");

        // 基本動詞の敬語表現 (着る/寝る/座る/尋ねる/待つ)
        var bossWear = KeigoConverter.Convert([new("しゃちょうが", false, ["社長が"]), new("きた", false, ["着た"])]);
        Assert.Equal("社長がお召しになりました", string.Concat(bossWear.Select(c => c.Text)), "社長が着た → お召しになりました");

        var selfWait = KeigoConverter.Convert([new("まっている", false, ["待っている"])]);
        Assert.Equal("お待ちしております", selfWait[0].Text, "待っている → お待ちしております");

        var selfAsk = KeigoConverter.Convert([new("たずねた", false, ["尋ねた"])]);
        Assert.Equal("伺いました", selfAsk[0].Text, "尋ねた → 伺いました");

        var sitDown = KeigoConverter.Convert([new("すわって", false, ["座って"])]);
        Assert.Equal("お掛けください", sitDown[0].Text, "座って → お掛けください");

        // どこに行くの / どこに行くの？ (どこ→どちら & 〜の疑問・確認)
        var dokoNiIkuno = KeigoConverter.Convert([new("どこに", false, ["どこに"]), new("いくの", false, ["行くの"])]);
        Assert.Equal("どちらに行かれますか", string.Concat(dokoNiIkuno.Select(c => c.Text)), "どこに行くの → どちらに行かれますか");

        var dokoNiIkunoQuest = KeigoConverter.Convert([new("どこに", false, ["どこに"]), new("いくの？", false, ["行くの？"])]);
        Assert.Equal("どちらに行かれますでしょうか？", string.Concat(dokoNiIkunoQuest.Select(c => c.Text)), "どこに行くの？ → どちらに行かれますでしょうか？");

        // あったら・あれば ＝ ございましたら (1文節/2文節 & 誤爆防止)
        var mondaiAreba = KeigoConverter.Convert([new("もんだいがあれば、", false, ["問題があれば、"]), new("れんらくして", false, ["連絡して"])]);
        Assert.Equal("問題がございましたら、ご連絡ください", string.Concat(mondaiAreba.Select(c => c.Text)), "問題があれば → 問題がございましたら");

        var splitAttara = KeigoConverter.Convert([new("しつもんが", false, ["質問が"]), new("あったら、", false, ["あったら、"]), new("おしえて", false, ["教えて"])]);
        Assert.Equal("質問がございましたら、ご教示ください", string.Concat(splitAttara.Select(c => c.Text)), "2文節に分かれた『あったら、』→ ございましたら、");

        var kaiteAttara = KeigoConverter.Convert([new("かいて", false, ["書いて"]), new("あったら", false, ["あったら"])]);
        Assert.Equal("書いてあったら", string.Concat(kaiteAttara.Select(c => c.Text)), "補助動詞の『書いてあったら』は『書いてございましたら』に誤爆しない");

        // 推量表現 (はずだ/みたいだ/らしい/そうだ/に違いない/じゃないか/大丈夫+推量)
        var hazuda = KeigoConverter.Convert([new("とどくはずだ", false, ["届くはずだ"])]);
        Assert.Equal("届くはずです", hazuda[0].Text, "届くはずだ → 届くはずです");

        var mitaida = KeigoConverter.Convert([new("おわるみたいだ", false, ["終わるみたいだ"])]);
        Assert.Equal("終わるみたいです", mitaida[0].Text, "終わるみたいだ → 終わるみたいです");

        var daijoubuKamo = KeigoConverter.Convert([new("だいじょうぶかも", false, ["大丈夫かも"])]);
        Assert.Equal("問題ないかもしれません", daijoubuKamo[0].Text, "大丈夫かも → 問題ないかもしれません (問題ございませんかもしれませんにしない)");

        var daijoubuToOmou = KeigoConverter.Convert([new("だいじょうぶだとおもう", false, ["大丈夫だと思う"])]);
        Assert.Equal("問題ないかと存じます", daijoubuToOmou[0].Text, "大丈夫だと思う → 問題ないかと存じます");

        var chigainai = KeigoConverter.Convert([new("まちがいに", false, ["間違いに"]), new("ちがいない", false, ["違いない"])]);
        Assert.Equal("間違いに違いありません", string.Concat(chigainai.Select(c => c.Text)), "〜に違いない → 〜に違いありません");

        var janaika = KeigoConverter.Convert([new("まにあうんじゃないか", false, ["間に合うんじゃないか"])]);
        Assert.Equal("間に合うのではないでしょうか", janaika[0].Text, "〜んじゃないか → 〜のではないでしょうか");

        var kamoMid = KeigoConverter.Convert([new("おくれるかもだけど、", false, ["遅れるかもだけど、"]), new("いく", false, ["行く"])]);
        Assert.Equal("遅れるかもしれませんが、伺います", string.Concat(kamoMid.Select(c => c.Text)), "〜かもだけど、 → 〜かもしれませんが、");

        var kamoQuest = KeigoConverter.Convert([new("とどくかも？", false, ["届くかも？"])]);
        Assert.Equal("届くでしょうか？", kamoQuest[0].Text, "届くかも？ → 届くでしょうか？");

        // OFF (既定) なら通常のまま
        var off = new Keyboard(live: true);
        off.Type("ryoukai");
        Assert.True(off.Showing != "承知いたしました", "OFF は通常のまま: " + off.Showing);

        // LateResults = true (実アプリ相当) のライブ変換→確定 (CommittedDisplay) でも敬語が適用されること
        var lateSettings = new Settings { LiveConversion = true, KeigoMode = true };
        var lateSession = new TestSession(() => lateSettings, lateResults: true);
        foreach (var ch in "ryoukai") lateSession.HandleKey(0, ch, false, false, false, false);
        var lateCommit = lateSession.HandleKey(VirtualKeys.Return, null, false, false, false, false);
        Assert.Equal("承知いたしました", lateCommit.Commits.Single().Text, "LateResults=true の確定でも敬語が消えない");

        // 名詞+助詞「で」や副詞・接続の「〜て」に「ください」を誤爆しないこと
        foreach (var (reading, text) in new[]
        {
            ("かいぎで", "会議で"),
            ("めーるで", "メールで"),
            ("すべて", "全て"),
            ("はじめて", "初めて"),
            ("あらためて", "改めて"),
            ("にかんして", "に関して"),
            ("として", "として"),
        })
        {
            var kept = KeigoConverter.Convert([new(reading, false, [text])]);
            Assert.Equal(text, kept[0].Text, $"非依頼の『{text}』に『ください』を誤爆しない");
        }
        Assert.Equal("結んでください", KeigoConverter.Convert([new("むすんで", false, ["結んで"])])[0].Text, "動詞の『結んで』には『ください』が付く");
        Assert.Equal("繋いでください", KeigoConverter.Convert([new("つないで", false, ["繋いで"])])[0].Text, "動詞の『繋いで』には『ください』が付く");

        // 追加語彙の確認 (〜しとく / 〜してみる / 対応できない / 〜してもらえる？)
        Assert.Equal("確認しておきます", KeigoConverter.Convert([new("かくにんしとく", false, ["確認しとく"])])[0].Text);
        Assert.Equal("調べてみます", KeigoConverter.Convert([new("しらべてみる", false, ["調べてみる"])])[0].Text);
        Assert.Equal("対応いたしかねます", KeigoConverter.Convert([new("たいおうできない", false, ["対応できない"])])[0].Text);
        Assert.Equal("ご確認いただけますでしょうか？", KeigoConverter.Convert([new("かくにんしてもらえる？", false, ["確認してもらえる？"])])[0].Text);

        // 動詞の尊敬語・謙譲語の言い換え網羅確認
        Assert.Equal("召し上がります", KeigoConverter.Convert([new("しゃちょうが", false, ["社長が"]), new("たべる", false, ["食べる"])])[1].Text);
        Assert.Equal("お亡くなりになりました", KeigoConverter.Convert([new("せんせいが", false, ["先生が"]), new("しんだ", false, ["死んだ"])])[1].Text);
        Assert.Equal("お話しになります", KeigoConverter.Convert([new("ぶちょうが", false, ["部長が"]), new("はなす", false, ["話す"])])[1].Text);
        Assert.Equal("お預かりになります", KeigoConverter.Convert([new("おきゃくさまが", false, ["お客様が"]), new("あずかる", false, ["預かる"])])[1].Text);
        Assert.Equal("差し上げます", KeigoConverter.Convert([new("あげる", false, ["あげる"])])[0].Text);
        Assert.Equal("おります", KeigoConverter.Convert([new("いる", false, ["いる"])])[0].Text);
        Assert.Equal("拝見しております", KeigoConverter.Convert([new("みている", false, ["見ている"])])[0].Text);
        Assert.Equal("拝読いたしました", KeigoConverter.Convert([new("よみました", false, ["読みました"])])[0].Text);
        Assert.Equal("申し上げております", KeigoConverter.Convert([new("いっている", false, ["言っている"])])[0].Text);
        Assert.Equal("お預かりいたします", KeigoConverter.Convert([new("あずかる", false, ["預かる"])])[0].Text);
        Assert.Equal("お届けいたしました", KeigoConverter.Convert([new("とどけた", false, ["届けた"])])[0].Text);

        // 複合五段サ行動詞を「〜いたしました」と誤爆しないこと + ビジネスクッション言葉
        Assert.Equal("取り消しました", KeigoConverter.Convert([new("とりけした", false, ["取り消した"])])[0].Text);
        Assert.Equal("見直しました", KeigoConverter.Convert([new("みなおした", false, ["見直した"])])[0].Text);
        Assert.Equal("折り返します", KeigoConverter.Convert([new("おりかえす", false, ["折り返す"])])[0].Text);
        Assert.Equal("思い出しました", KeigoConverter.Convert([new("おもいだした", false, ["思い出した"])])[0].Text);
        Assert.Equal("恐れ入りますが", KeigoConverter.Convert([new("おそれいりますが", false, ["恐れ入りますが"])])[0].Text);
        Assert.Equal("差し支えなければ", KeigoConverter.Convert([new("さしつかえなければ", false, ["差し支えなければ"])])[0].Text);

        // 2 文節に分かれた既敬語の重複防止 (お世話+になっております / 失礼+いたします / お待たせ+いたしました / お先に+失礼します)
        Assert.Equal("お世話になっております", string.Concat(KeigoConverter.Convert([new("おせわ", false, ["お世話"]), new("になっております", false, ["になっております"])]).Select(c => c.Text)));
        Assert.Equal("失礼いたします", string.Concat(KeigoConverter.Convert([new("しつれい", false, ["失礼"]), new("いたします", false, ["いたします"])]).Select(c => c.Text)));
        Assert.Equal("お待たせいたしました", string.Concat(KeigoConverter.Convert([new("おまたせ", false, ["お待たせ"]), new("いたしました", false, ["いたしました"])]).Select(c => c.Text)));
        Assert.Equal("お先に失礼いたします", string.Concat(KeigoConverter.Convert([new("おさきに", false, ["お先に"]), new("しつれいします", false, ["失礼します"])]).Select(c => c.Text)));

        // 2 文節に分かれた「〜て / 〜で + 補助動詞」の精度確認 (みる/ない/ある/おく/いく/いい？)
        Assert.Equal("調べてみます", string.Concat(KeigoConverter.Convert([new("しらべて", false, ["調べて"]), new("みる", false, ["見る"])]).Select(c => c.Text)));
        Assert.Equal("書いてみましたでしょうか？", string.Concat(KeigoConverter.Convert([new("かいて", false, ["書いて"]), new("みた？", false, ["見た？"])]).Select(c => c.Text)));
        Assert.Equal("確認しておりません", string.Concat(KeigoConverter.Convert([new("かくにんして", false, ["確認して"]), new("ない", false, ["ない"])]).Select(c => c.Text)));
        Assert.Equal("届いておりませんでした", string.Concat(KeigoConverter.Convert([new("とどいて", false, ["届いて"]), new("いなかった", false, ["いなかった"])]).Select(c => c.Text)));
        Assert.Equal("書いてあります", string.Concat(KeigoConverter.Convert([new("かいて", false, ["書いて"]), new("ある", false, ["ある"])]).Select(c => c.Text)));
        Assert.Equal("確認しておきます", string.Concat(KeigoConverter.Convert([new("かくにんして", false, ["確認して"]), new("おく", false, ["おく"])]).Select(c => c.Text)));
        Assert.Equal("進めてまいります", string.Concat(KeigoConverter.Convert([new("すすめて", false, ["進めて"]), new("いく", false, ["いく"])]).Select(c => c.Text)));
        Assert.Equal("確認してよろしいでしょうか？", string.Concat(KeigoConverter.Convert([new("かくにんして", false, ["確認して"]), new("いい？", false, ["いい？"])]).Select(c => c.Text)));
        Assert.Equal("確認していただけますでしょうか？", string.Concat(KeigoConverter.Convert([new("かくにんして", false, ["確認して"]), new("もらえますか？", false, ["もらえますか？"])]).Select(c => c.Text)));

        // 常体敬語動詞の丁寧化・二重敬語防止・業務定番表現
        Assert.Equal("おっしゃいました", KeigoConverter.Convert([new("おっしゃった", false, ["おっしゃった"])])[0].Text);
        Assert.Equal("くださいました", KeigoConverter.Convert([new("くださった", false, ["くださった"])])[0].Text);
        Assert.Equal("いただきました", KeigoConverter.Convert([new("いただいた", false, ["いただいた"])])[0].Text);
        Assert.Equal("ご提出ください", KeigoConverter.Convert([new("ていしゅつして", false, ["提出して"])])[0].Text);
        Assert.Equal("ご利用ください", KeigoConverter.Convert([new("りようして", false, ["利用して"])])[0].Text);
        Assert.Equal("拝見できかねます", KeigoConverter.Convert([new("みられない", false, ["見られない"])])[0].Text);
        Assert.Equal("チームがご覧になります", string.Concat(KeigoConverter.Convert([new("ちーむが", false, ["チームが"]), new("みます", false, ["見ます"])]).Select(c => c.Text)));
        Assert.Equal("当方が拝見いたします", string.Concat(KeigoConverter.Convert([new("とうほうが", false, ["当方が"]), new("みます", false, ["見ます"])]).Select(c => c.Text)));

        // 「いいですか？ / 良いですか？ / 〜ていいですか？」→「よろしいでしょうか？」
        Assert.Equal("よろしいでしょうか？", KeigoConverter.Convert([new("いいですか？", false, ["いいですか？"])])[0].Text);
        Assert.Equal("よろしいでしょうか？", KeigoConverter.Convert([new("よいですか？", false, ["良いですか？"])])[0].Text);
        Assert.Equal("確認してもよろしいでしょうか？", KeigoConverter.Convert([new("かくにんしていいですか？", false, ["確認していいですか？"])])[0].Text);
        Assert.Equal("確認してよろしいでしょうか？", string.Concat(KeigoConverter.Convert([new("かくにんして", false, ["確認して"]), new("いいですか？", false, ["いいですか？"])]).Select(c => c.Text)));
        Assert.Equal("これでよろしいでしょうか？", KeigoConverter.Convert([new("これでいいですか？", false, ["これでいいですか？"])])[0].Text);
        Assert.Equal("明日がよろしいでしょうか？", KeigoConverter.Convert([new("あしたがいいですか？", false, ["明日がいいですか？"])])[0].Text);

        // 変換後に Space を押すと元の常体候補が出て戻せること
        var revertLive = new Keyboard(live: true) { KeigoMode = true };
        revertLive.Type("ryoukai");
        Assert.Equal("承知いたしました", revertLive.Showing);
        revertLive.Press(VirtualKeys.Space);
        Assert.True(revertLive.Host.View!.Candidates.Contains("了解"), "Space で元の表現 (了解) が候補に出る: " + string.Join(",", revertLive.Host.View.Candidates));
        var originalIndex = revertLive.Host.View.Candidates.ToList().IndexOf("了解");
        revertLive.SelectCandidate(originalIndex);
        revertLive.Press(VirtualKeys.Return);
        Assert.Equal("了解", revertLive.Host.Document, "Space から元の表現を選んで確定できる");

        var revertManual = new Keyboard(live: false) { KeigoMode = true };
        revertManual.Type("ryoukai ");
        Assert.Equal("承知いたしました", revertManual.Showing);
        Assert.True(revertManual.Host.View!.Candidates.Contains("了解"), "変換候補に元の表現 (了解) が含まれる: " + string.Join(",", revertManual.Host.View.Candidates));
        while (revertManual.Showing != "了解")
            revertManual.Press(VirtualKeys.Space);
        revertManual.Press(VirtualKeys.Return);
        Assert.Equal("了解", revertManual.Host.Document, "Space で候補を送って元の常体 (了解) に戻せる");

        // 逆向きの例: ON でも変えてはいけない入力 (引用・固有名詞・客観・後でを含む熟語・英字混在)
        Assert.Equal("「了解」", string.Concat(KeigoConverter.Convert([new("「", false, ["「"]), new("りょうかい", false, ["了解"]), new("」", false, ["」"])]).Select(c => c.Text)), "カギ括弧で囲まれた引用は変えない");
        Assert.Equal("「了解」", KeigoConverter.Convert([new("「りょうかい」", false, ["「了解」"])])[0].Text, "単一文節の引用は変えない");
        Assert.Equal("「後で」と申し上げました", string.Concat(KeigoConverter.Convert([new("「あとで」と", false, ["「後で」と"]), new("いった", false, ["言った"])]).Select(c => c.Text)), "引用内の語は変えず、外の地の文だけ敬語にする");
        Assert.Equal("株式会社山田", KeigoConverter.Convert([new("かぶしきがいしゃやまだ", false, ["株式会社山田"])])[0].Text, "固有名詞は変えない");
        Assert.Equal("東日本", KeigoConverter.Convert([new("ひがしにほん", false, ["東日本"])])[0].Text, "固有名詞の途中を変えない");
        Assert.Equal("客観", KeigoConverter.Convert([new("きゃっかん", false, ["客観"])])[0].Text, "客観の「客」をお客様にしない");
        Assert.Equal("客室", KeigoConverter.Convert([new("きゃくしつ", false, ["客室"])])[0].Text, "客室の「客」をお客様にしない");
        Assert.Equal("観客", KeigoConverter.Convert([new("かんきゃく", false, ["観客"])])[0].Text, "観客の「客」をお客様にしない");
        Assert.Equal("客観的に拝見いたします", string.Concat(KeigoConverter.Convert([new("きゃっかんてきに", false, ["客観的に"]), new("みる", false, ["見る"])]).Select(c => c.Text)), "客観の「客」で尊敬語 (ご覧になります) に誤爆しない");
        Assert.Equal("前後で確認", KeigoConverter.Convert([new("ぜんごでかくにん", false, ["前後で確認"])])[0].Text, "「前後で」の途中を後ほどにしない");
        Assert.Equal("午後で調整", KeigoConverter.Convert([new("ごごでちょうせい", false, ["午後で調整"])])[0].Text, "「午後で」の途中を後ほどにしない");
        Assert.Equal("食後で構いません", KeigoConverter.Convert([new("しょくごでかまいません", false, ["食後で構いません"])])[0].Text, "「食後で」の途中を後ほどにしない");
        Assert.Equal("githubに上げた", string.Concat(KeigoConverter.Convert([new("github", true, ["github"]), new("に", false, ["に"]), new("あげた", false, ["上げた"])]).Select(c => c.Text)), "英字混在の「上げた」を「差し上げました」に誤爆しない");

        var mixedEnglish = new Keyboard(live: true) { KeigoMode = true };
        mixedEnglish.Type("githubniageta");
        var mixedShowing = mixedEnglish.Showing ?? "";
        Assert.True(mixedShowing.StartsWith("github", StringComparison.OrdinalIgnoreCase), "英字混在入力の英字部分を壊さない: " + mixedShowing);
        Assert.True(!mixedShowing.Contains("差し上げ", StringComparison.Ordinal), "githubniageta を差し上げに誤爆しない: " + mixedShowing);
    }
}
