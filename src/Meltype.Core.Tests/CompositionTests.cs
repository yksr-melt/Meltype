// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>Meltype キーボード (変換ボックス)。</summary>
internal static class CompositionTests
{
    internal static readonly CompositionDetector Detector = CompositionDetector.CreateDefault();

    internal sealed class FakeConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana switch
        {
            "きょう" => "今日",
            "にほんご" => "日本語",
            "こんにちは" => "今日は",
            "きょうは" => "今日は",
            "でけんさく" => "で検索",
            "たんい" => "単位",
            "な～～～" => "名～～～",
            "かんたん" => "簡単",
            _ => null,
        };

        /// <summary>直近の ConvertClauses に渡された文脈 (文脈なしの呼び出しは数えない)。</summary>
        public string? LastContext { get; private set; }

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            if (context is not null) LastContext = context;
            return hiragana switch
            {
                "たんいをとる" => [new("たんいを", "単位を"), new("とる", "取る")],
                "ひるか" => [new("ひる", "昼"), new("か", "化")],
                // 本物の変換エンジンは、前に同じ語があると区切りを変える (「記号等」含め、 + きごうとう → 気強盗)。
                "きごうとう" when context?.Contains("記号等") == true => [new("き", "気"), new("ごうとう", "強盗")],
                "きごうとう" => [new("きごう", "記号"), new("とう", "等")],
                "あつい" => [new("あつい", "熱い")],
                "かわ" => [new("かわ", "川")],
                "すぱいだーまっ" => [new("すぱいだーま", "スパイダーマ"), new("っ", "っ")],
                "はしを" => [new("はしを", "橋を")],
                // 本物の変換エンジン (Mozc) の苦手なもの: え、 → 得、、がちで → 勝ちで
                "え、しらん" => [new("え", "得"), new("、", "、"), new("しらん", "知らん")],
                "えかく" => [new("え", "絵"), new("かく", "描く")],
                "がちでやばい" => [new("がちで", "勝ちで"), new("やばい", "ヤバい")],
                // 英単語の後ろの する の活用: した → 下、したい → 死体
                "した" => [new("した", "下")],
                "したい" => [new("したい", "死体")],
                _ => null,
            };
        }
    }

    internal sealed class FakeHost : ICompositionHost
    {
        public List<string> Output { get; } = [];
        public List<string> Events { get; } = [];
        public CompositionView? View { get; private set; }
        public bool PhysicalShift { get; set; }
        public bool CanDeleteBackward { get; set; } = true;
        public ReconversionSelection? Selection { get; set; }

        public ReconversionSelection? GetReconversionSelection() => Selection;

        public bool TryReplaceSelection(ReconversionSelection selection, string text)
        {
            if (!ReferenceEquals(Selection, selection)) return false;
            Events.Add($"replace:{selection.Text}:{text}");
            Selection = null;
            Document = text;
            return true;
        }

        /// <summary>入力欄のキャレットの直前にある (と見なす) 確定済みの文字列。</summary>
        public string? PrecedingText { get; set; }

        /// <summary>入力欄のキャレットの後ろにある (と見なす) 文字列。</summary>
        public string? FollowingText { get; set; }

        public void RequestSurroundingText(Action<string?, string?> callback) => callback(PrecedingText ?? (Document.Length > 0 ? Document : null), FollowingText);

        /// <summary>入力欄の中身 (確定した文字列と、確定し直すときの削除を反映したもの)。</summary>
        public string Document { get; private set; } = "";

        public void DeleteBackward(int count)
        {
            Events.Add($"bs:{count}");
            Document = Document[..Math.Max(0, Document.Length - count)];
        }

        public void CommitText(string text)
        {
            Output.Add(text);
            Events.Add($"text:{text}");
            Document += text;
        }

        /// <summary>アプリに送り直したキー (英数状態で判定した語など)。</summary>
        public event Action<KeyEvent>? Replayed;

        public void Replay(KeyEvent e)
        {
            Events.Add($"{(e.IsUp ? "up" : "down")}:{e.Vk:X2}");
            Replayed?.Invoke(e);
        }
        public void Replay(MouseButtonEvent e) => Events.Add($"mouse:{e.Message:X}");

        public char? CharFromKey(KeyEvent e, bool shift)
        {
            if (VirtualKeys.IsLetter(e.Vk)) return shift || PhysicalShift ? (char)e.Vk : char.ToLowerInvariant((char)e.Vk);
            var shifted = shift || PhysicalShift;
            foreach (var (c, key) in JisKeys)
            {
                if (key.Vk == e.Vk && key.Shift == shifted) return c;
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

        public bool IsShiftDown() => PhysicalShift;

        /// <summary>入力欄の文字を、キーを処理しているその場で読めているか (Mac と同じ環境にするとき true)。</summary>
        public bool SurroundingTextIsCurrent { get; set; }
        public string? ClipboardText { get; set; }

        public void Show(CompositionView view) => View = view;
        public void Hide() => View = null;
    }

    internal sealed class Keyboard
    {
        private long _now = 1000;
        private bool _ctrlHeld;
        public CaptureGate Gate { get; } = new(() => { });
        public FakeHost Host { get; } = new();
        public CompositionController Controller { get; }

        private static readonly Detection.ScoreEngine DirectEngine = TestSupport.CreateEngine();
        private static readonly CandidateDictionary Candidates = CandidateDictionary.Load(null);
        private static readonly ContextRules Rules = ContextRules.Load(null);
        private static readonly MisspellingDictionary Misspellings = MisspellingDictionary.Load(null);
        private static readonly Lazy<RomajiTypoCorrector> SharedTypos = new(() => RomajiTypoCorrector.Load(Detector.Romaji));
        private static RomajiTypoCorrector Typos => SharedTypos.Value;
        private bool _directEnglishWord;

        /// <summary>自動判定の強さ。</summary>
        public Meltype.Config.DetectionLevel Level { get; set; } = Meltype.Config.DetectionLevel.Balanced;

        private bool _shiftHeld;

        /// <summary>かな入力 (JIS) か。</summary>
        public bool Kana { get; set; }
        public bool CorrectTypos { get; set; } = true;
        public bool ZSymbols { get; set; } = true;
        public bool NumberKeysSelectCandidates { get; set; } = true;
        public bool SpaceAroundEnglish { get; set; }
        public bool ShiftSpaceHalfWidth { get; set; }
        public bool EnterCommitsAsShown { get; set; }
        public bool AlphanumericKeyStartsAsTyped { get; set; }
        public bool FullWidthParentheses { get; set; }
        public bool FullWidthSlash { get; set; }
        public bool SquareBrackets { get; set; }
        public bool EscStepsBack { get; set; }
        public bool EscRevertsSelectedClause { get; set; }
        public bool FunctionKeysAffectSelectedClause { get; set; }
        public bool CompatibleShortcuts { get; set; }
        public bool PartialCommit { get; set; }
        public bool MacControlKeys { get; set; }
        public bool CursorIntoBrackets { get; set; }
        public string ControlKeyBindings { get; set; } = "";
        public bool CalculatorCandidates { get; set; }
        public bool LogKeyIntervals { get; set; }
        public bool LaughterAfterKana { get; set; }
        public bool ContextualLearning { get; set; }
        public bool FullWidthSpaceByDefault { get; set; }
        public bool ShowCandidatesWhileTyping { get; set; }
        public bool GitHubIssueCandidates { get; set; }
        public string GitHubRepository { get; set; } = "";
        public GitHubIssueTitles? GitHubTitles { get; set; }
        public bool ClipboardCandidates { get; set; }
        public bool ContinueAfterConversion { get; set; }
        public bool Learning { get; set; } = true;
        public bool SeparateKeyRoles { get; set; }
        public bool EnglishFirstInput { get; set; }
        public bool ConsonantAbbreviations { get; set; }

        /// <summary>句読点の組み合わせ (設定)。</summary>
        public Meltype.Config.PunctuationStyle Punctuation { get; set; }

        /// <summary>かな入力で、仮想キーを順に打つ (shift: その打鍵で Shift を押す)。</summary>
        public void TypeKeys(params (int Vk, bool Shift)[] keys)
        {
            foreach (var (vk, shift) in keys)
            {
                if (shift)
                {
                    Host.PhysicalShift = true;
                    Key(VirtualKeys.LShift);
                }
                Press(vk);
                if (shift)
                {
                    Key(VirtualKeys.LShift, up: true);
                    Host.PhysicalShift = false;
                }
            }
        }

        /// <summary>かな入力で、英字キー (と , . / - の記号キー) を打つ。大文字は Shift を押して打つ。</summary>
        public void TypeKanaKeys(string keys) =>
            TypeKeys(keys.Select(c => (c switch { ',' => 0xBC, '.' => 0xBE, '/' => 0xBF, '-' => 0xBD, '@' => 0xC0, '[' => 0xDB, _ => (int)char.ToUpperInvariant(c) }, char.IsAsciiLetterUpper(c))).ToArray());

        /// <summary>英数 (直接入力) 状態か。</summary>
        public bool Direct { get; set; }

        /// <summary>先頭か空白の直後の / $ @ で始まる語をそのまま入力するか (設定の SigilWordsDirect)。</summary>
        public bool SigilWords { get; set; } = true;

        /// <summary>MeltypeEngine と同じく、アプリに届いた文字を追いかける。</summary>
        public SigilWord Sigil { get; } = new();

        /// <summary>コードエディターのコードを打っているか (先頭の / を ・ にせず、そのまま渡す)。</summary>
        public bool Code { get; set; }

        public long Now => _now;

        public FakeConverter Converter { get; } = new();

        public Keyboard(bool live = false, bool direct = false, ConversionHistory? history = null, IKanjiConverter? converter = null,
            Func<string, IReadOnlyList<string>>? moreCandidates = null, UserDictionary? userDictionary = null, LanguageMemory? languages = null,
            TranslationDictionary? translations = null, TranslationHistory? translationHistory = null, bool slashAsMiddleDot = false, Predictor? predictor = null,
            Func<DateTime>? now = null, bool showTypedKeys = false, bool tabConversion = false, bool englishNamePredictions = false)
        {
            Direct = direct;
            Controller = new CompositionController(Gate, Detector, converter ?? Converter, Host, new CompositionOptions
            {
                LiveConversion = () => live,
                DirectMode = () => Direct,
                ClassifyDirect = (letters, final) => DirectEngine.Evaluate(new Detection.DetectionInput(letters, letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), final)).Verdict,
                DirectDecided = japanese => { if (japanese) Direct = false; else _directEnglishWord = true; },
                Candidates = Candidates,
                ContextRules = Rules,
                History = history ?? new ConversionHistory(null),
                MoreCandidates = moreCandidates,
                UserDictionary = userDictionary,
                Level = () => Level,
                KanaInput = () => Kana,
                Misspellings = Misspellings,
                Languages = languages,
                Translations = translations,
                TranslationHistory = translationHistory,
                RomajiTypos = Typos,
                CorrectTypos = () => CorrectTypos,
                ZSymbols = () => ZSymbols,
                NumberKeysSelectCandidates = () => NumberKeysSelectCandidates,
                SpaceAroundEnglish = () => SpaceAroundEnglish,
                ShiftSpaceHalfWidth = () => ShiftSpaceHalfWidth,
                EnterCommitsAsShown = () => EnterCommitsAsShown,
                AlphanumericKeyStartsAsTyped = () => AlphanumericKeyStartsAsTyped,
                FullWidthParentheses = () => FullWidthParentheses,
                FullWidthSlash = () => FullWidthSlash,
                SquareBrackets = () => SquareBrackets,
                EscStepsBack = () => EscStepsBack,
                EscRevertsSelectedClause = () => EscRevertsSelectedClause,
                FunctionKeysAffectSelectedClause = () => FunctionKeysAffectSelectedClause,
                CompatibleShortcuts = () => CompatibleShortcuts,
                PartialCommit = () => PartialCommit,
                MacControlKeys = () => MacControlKeys,
                CursorIntoBrackets = () => CursorIntoBrackets,
                ControlKeyBindings = () => ControlKeyBindings,
                CalculatorCandidates = () => CalculatorCandidates,
                LogKeyIntervals = () => LogKeyIntervals,
                LaughterAfterKana = () => LaughterAfterKana,
                ContextualLearning = () => ContextualLearning,
                FullWidthSpaceByDefault = () => FullWidthSpaceByDefault,
                ShowCandidatesWhileTyping = () => ShowCandidatesWhileTyping,
                GitHubIssueCandidates = () => GitHubIssueCandidates,
                GitHubRepository = () => GitHubRepository,
                GitHubTitles = () => GitHubTitles,
                ClipboardCandidates = () => ClipboardCandidates,
                Predictor = predictor,
                Predictions = () => predictor is not null,
                EnglishNamePredictions = () => englishNamePredictions,
                Punctuation = () => Punctuation,
                SlashAsMiddleDot = () => slashAsMiddleDot,
                Now = now ?? (() => DateTime.Now),
                ShowTypedKeys = () => showTypedKeys,
                TabConversion = () => tabConversion,
                ContinueAfterConversion = () => ContinueAfterConversion,
                Learning = () => Learning,
                SeparateKeyRoles = () => SeparateKeyRoles,
                EnglishFirstInput = () => EnglishFirstInput,
                ConsonantAbbreviations = () => ConsonantAbbreviations,
            });
            Controller.Committed += Sigil.Append;
            Host.Replayed += e =>
            {
                if (!e.IsUp) Sigil.OnKey(e.Vk, Host.CharFromKey(e, _shiftHeld));
            };
        }

        /// <summary>MeltypeEngine.StartsComposition と同じ条件。</summary>
        private bool Starts(KeyEvent k)
        {
            if (!k.IsDown || _ctrlHeld) return false;
            if (k.Vk == VirtualKeys.Convert) return true;
            if (SigilWords && (Direct || !Kana) && Host.CharFromKey(k, _shiftHeld) is { } typed && Sigil.PassesThrough(typed, Host.PrecedingText))
            {
                if (typed != '/' || Direct || Sigil.IsActive || Code) return false;
                Controller.MarkSlashAtStart();
                return true;
            }
            var letter = VirtualKeys.IsLetter(k.Vk);
            if (Direct) return letter && !_directEnglishWord && Level != Meltype.Config.DetectionLevel.Manual;
            if (Kana && Detection.KanaDetector.IsKanaKey(k.Vk)) return true;
            if (k.Vk == VirtualKeys.Space && (_shiftHeld || FullWidthSpaceByDefault && !Direct)) return true;
            return letter || k.Vk is >= 0x30 and <= 0x39 or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;
        }

        /// <summary>フックと同じく、関所が閉じていれば英字キーで変換ボックスを開く。</summary>
        public bool Key(int vk, bool up = false)
        {
            var e = new KeyEvent(vk, 0, false, up, false, _now += 30);
            // 実際のフックと同じく、Ctrl が押されている間は変換ボックスを開かない。
            if (vk == VirtualKeys.LControl) _ctrlHeld = !up;
            if (vk == VirtualKeys.LShift) _shiftHeld = !up;
            if (Direct && !up && !VirtualKeys.IsLetter(vk) && !VirtualKeys.IsModifier(vk)) _directEnglishWord = false;
            var swallowed = Gate.OnKey(e, Starts);
            if (!swallowed) Host.Events.Add($"{(up ? "passed-up" : "passed")}:{vk:X2}");
            if (!swallowed && !up)
            {
                if (_ctrlHeld) Sigil.Lose();
                else Sigil.OnKey(vk, Host.CharFromKey(e, _shiftHeld));
            }
            Controller.Pump();
            return swallowed;
        }

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
                    Host.PhysicalShift = !Gate.IsCaptured;
                    Key(VirtualKeys.LShift);
                    Press(c);
                    Key(VirtualKeys.LShift, up: true);
                    Host.PhysicalShift = false;
                }
                else if (c == ' ') Press(VirtualKeys.Space);
                else if (c == '\n') Press(VirtualKeys.Return);
                else if (c == '\b') Press(VirtualKeys.Back);
                else if (FakeHost.JisKeys.TryGetValue(c, out var key) && key.Shift)
                {
                    Host.PhysicalShift = !Gate.IsCaptured;
                    Key(VirtualKeys.LShift);
                    Press(key.Vk);
                    Key(VirtualKeys.LShift, up: true);
                    Host.PhysicalShift = false;
                }
                else if (FakeHost.JisKeys.TryGetValue(c, out key)) Press(key.Vk);
                else Press(char.ToUpperInvariant(c));
            }
        }

        public string? Showing => Host.View?.Text;
    }

    private sealed class WideContextConverter : IKanjiConverter, IWideContextConverter
    {
        public string? WideContext { get; set; }
        public string? Convert(string hiragana) => null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => null;
    }

    [Test]
    public static void WideContext_IsPassedToConvertersThatCanUseIt()
    {
        var converter = new WideContextConverter();
        var k = new Keyboard(converter: converter);
        k.Host.PrecedingText = "昨日は雨でしたが、\n今日は";
        k.Type("kyou");
        Assert.Equal("昨日は雨でしたが、今日は", converter.WideContext ?? "(なし)", "改行を除いて文をまたいで渡す");
        var longText = string.Concat(Enumerable.Repeat("あ", 150)) + "終";
        var l = new Keyboard(converter: converter);
        l.Host.PrecedingText = longText;
        l.Type("kyou");
        Assert.Equal(100, converter.WideContext?.Length ?? 0, "最大 100 文字");
        Assert.True(converter.WideContext!.EndsWith('終'), "キャレットに近い方を残す");
        var e = new Keyboard(converter: converter);
        e.Host.PrecedingText = "Hello, this is a pen. ";
        e.Type("kyou");
        Assert.True(converter.WideContext is null, "英文のときは渡さない");
    }

    [Test]
    public static void Issue666_ClickingATypingCandidate_SelectsThatWord()
    {
        // 入力中の候補は読み全体の候補。クリックしたとき、文節に分かれて一覧が変わっても、見えていた語が選ばれる
        var k = new Keyboard(live: true) { ShowCandidatesWhileTyping = true };
        k.Type("tanniwotoru");
        var shown = k.Host.View!.Candidates.ToList();
        Assert.True(shown.Count >= 2, "候補が出ている: " + string.Join(",", shown));
        k.Controller.SelectCandidate(1);
        Assert.Equal(shown[1], k.Showing, "クリックした語が選ばれる");
        k.Type("\n");
        Assert.Equal(shown[1], k.Host.Document, "その語で確定");
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
        }

        // 設定が OFF なら今までどおり (kyou は日本語)
        var off = new Keyboard();
        off.Type("kyou");
        Assert.Equal("きょう", off.Showing, "OFF は自動判定");
    }

    [Test]
    public static void Issue674_ExtendedYoonSpellings()
    {
        foreach (var (typed, expected) in new[] { ("kye", "きぇ"), ("gwi", "ぐぃ"), ("zwa", "ずぁ"), ("fya", "ふゃ"), ("mye", "みぇ") })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, typed);
                // 英単語の中の綴りは、かなにしない (trying・swan・highway・hyena・babying)
        foreach (var word in new[] { "trying", "swan", "swine", "highway", "pathway", "hyena", "babying", "studying" })
        {
            var e = new Keyboard();
            e.Type(word);
            Assert.Equal(word, e.Showing, word);
        }
}
    }

    [Test]
    public static void Reconversion_RomajiTextBecomesJapanese()
    {
        // #288: ローマ字のまま入った文を選んで変換キー → Meltype の判定で日本語の所だけを日本語にして候補に出す。Enter で置き換える
        Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (selected, expected) in new[]
            {
                ("kyouha iitenki desu ne", "きょうはいいてんきですね"),
                ("asita no meeting de review shimasu", "あしたの meeting で review します"),
                ("PR no review onegai shimasu", "PR の review おねがいします"),
            })
            {
                var k = new Keyboard(direct: true);
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
            // 日本語にする所が無い英文は、今までの再変換と同じく扱う (英文を勝手に日本語にしない)
            var english = new Keyboard(direct: true);
            english.Host.Selection = new ReconversionSelection("this is a pen", "this is a pen");
            english.Press(VirtualKeys.Convert);
            Assert.True(english.Showing is null || !english.Showing.Any(c => c is >= 'ぁ' and <= 'ゖ'), "英文を日本語にしない: " + english.Showing);
        }
        finally
        {
            Detector.SpellChecker = null;
        }
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
    public static void Reconversion_ReplacesSelectedTextOnlyOnCommit()
    {
        var k = new Keyboard(direct: true);
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
        Assert.True(!k.Gate.IsCaptured, "確定後はキーを解放する");
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
        Assert.True(!k.Gate.IsCaptured, "取消後はキーを解放する");
        k.Type("kana");
        k.Press(VirtualKeys.Return);
        Assert.True(k.Host.Output.Count > 0, "取消後も通常入力できる");
    }

    [Test]
    public static void Reconversion_ChangedSelectionIsNotReplaced()
    {
        var k = new Keyboard();
        k.Host.Selection = new ReconversionSelection("今日", "きょう");
        k.Press(VirtualKeys.Convert);
        k.Host.Selection = new ReconversionSelection("橋", "はし");
        k.Press(VirtualKeys.Return);
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:", StringComparison.Ordinal)), "別の選択範囲を置換しない");
        Assert.Equal(0, k.Host.Output.Count);
        Assert.True(!k.Gate.IsCaptured, "置換失敗後もキーを解放する");
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
        Assert.True(!k.Gate.IsCaptured, "全角スペースの後は横取りしない");
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
        Assert.True(predictor.PredictEnglishNames("しゃお").Contains("Xiaomi"), string.Join(",", predictor.PredictEnglishNames("しゃお")));
        Assert.True(predictor.PredictEnglishNames("ぎっと") is { } git && git.Contains("GitHub") && git.Contains("GitLab"), string.Join(",", predictor.PredictEnglishNames("ぎっと")));
        Assert.True(predictor.PredictEnglishNames("しゃおみ").Contains("Xiaomi"), "読みを打ち終わっても出す");
        Assert.Equal(1, predictor.PredictEnglishNames("しゃおみ").Count(w => w == "Xiaomi"), "同じ語は 1 つだけ (しゃおみ・しゃおみー)");
        // #1040・#959: めるたいぷ → Meltype (候補と予測の両方)
        Assert.True(predictor.PredictEnglishNames("めるた").Contains("Meltype"), string.Join(",", predictor.PredictEnglishNames("めるた")));
        Assert.True(CandidateDictionary.Load(null).Lookup("めるたいぷ").Contains("Meltype"), "めるたいぷ の候補に Meltype");
        Assert.Equal(0, predictor.PredictEnglishNames("しゃ").Count, "2 文字では出さない");
        Assert.True(predictor.PredictEnglishNames("あいあ").Count <= Predictor.MaxPredictions, "多すぎない");
        Assert.Equal(0, predictor.PredictEnglishNames("ゐゐゐ").Count, "無い読みは出さない");

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
        var k = new Keyboard(showTypedKeys: true);
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
        foreach (var expected in new[] { "2026年10月8日", "2026年10月8日(木)", "10月8日(木)", "2026/10/08", "2026/10/08(木)", "10/8(木)", "2026-10-08", "木曜日" })
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
    private sealed class SplitConverter(Dictionary<string, ConversionClause[]> clauses) : IKanjiConverter
    {
        private readonly FakeConverter _inner = new();
        public string? Convert(string hiragana) => _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            clauses.TryGetValue(hiragana, out var result) ? result : _inner.ConvertClauses(hiragana, context);
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
        // Ctrl+Shift+Z: 確定してから Ctrl → Shift → Z の順に送り、離したことも送る
        var k = new Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        k.Key(VirtualKeys.LShift);
        k.Press('Z');
        k.Key(VirtualKeys.LShift, up: true);
        k.Key(VirtualKeys.LControl, up: true);
        var events = string.Join("|", k.Host.Events);
        Assert.True(events.StartsWith("text:かな|down:A2|down:A0|down:5A", StringComparison.Ordinal), events);
        Assert.True(events.Contains("up:A0") || events.Contains("passed-up:A0"), "Shift を離したことも届く: " + events);
        Assert.True(events.Contains("up:A2") || events.Contains("passed-up:A2"), "Ctrl を離したことも届く: " + events);
        Assert.True(!k.Gate.IsCaptured, "ショートカットの後は横取りをやめる");
    }

    [Test]
    public static void CtrlClick_CommitsThenSendsCtrlWithClick()
    {
        // Ctrl を押したままクリック: 確定してから、Ctrl とクリックを送る
        var k = new Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        k.Gate.OnMouseButton(new MouseButtonEvent(0x201, 10, 20, 0));
        k.Controller.Pump();
        var events = string.Join("|", k.Host.Events);
        Assert.True(events.StartsWith("text:かな|down:A2|mouse:201", StringComparison.Ordinal), events);
        k.Key(VirtualKeys.LControl, up: true);
        Assert.True(string.Join("|", k.Host.Events).Contains("A2", StringComparison.Ordinal) && k.Host.Events.Count(e => e.Contains(":A2")) == 2, "Ctrl の上げ下げがそろう: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void CaptureGate_StartCheckDoesNotBlockQueue()
    {
        // 打ち始めの判定 (フォーカスを調べ終わるのを待つことがある) の間も、UI スレッドはキューを読める
        var gate = new CaptureGate(() => { });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var hook = Task.Run(() => gate.OnKey(new KeyEvent('A', 0, false, false, false, 0), _ =>
        {
            entered.Set();
            release.Wait(5000);
            return true;
        }));
        Assert.True(entered.Wait(5000), "判定が始まる");
        var ui = Task.Run(() => gate.TryDequeue(out _) || gate.IsCaptured || !gate.TryRelease());
        var finished = ui.Wait(2000);
        release.Set();
        Assert.True(finished && !ui.Result, "判定の間も、キューの出し入れと関所の開け閉めは待たされない");
        Assert.True(hook.Result && gate.IsCaptured, "判定が済んだら横取りを始める");
        Assert.True(gate.TryDequeue(out var input) && input.Key?.Vk == 'A' && !gate.TryDequeue(out _), "判定した打鍵を 1 回だけ積む");
    }

    [Test]
    public static void ShiftClick_CommitsThenSendsShiftWithClick()
    {
        // Shift を押したままクリック (範囲選択): 確定してから、Shift とクリックを送る
        var k = new Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LShift);
        k.Gate.OnMouseButton(new MouseButtonEvent(0x201, 10, 20, 0));
        k.Controller.Pump();
        var events = string.Join("|", k.Host.Events);
        Assert.True(events.StartsWith("text:かな|down:A0|mouse:201", StringComparison.Ordinal), events);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.True(k.Host.Events.Count(e => e.Contains(":A0")) == 2, "Shift の上げ下げがそろう: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void CtrlOtherShortcut_CommitsThenPasses()
    {
        var k = new Keyboard();
        k.Type("aiueo");
        CtrlPress(k, 'C');
        Assert.Equal("あいうえお", k.Host.Document);
        var down = k.Host.Events.IndexOf("down:A2");
        Assert.True(down >= 0 && k.Host.Events.IndexOf("down:43") > down, "確定してから Ctrl+C を送る: " + string.Join(" ", k.Host.Events));
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
        Assert.True(!k.Gate.IsCaptured, "最後の文節を番号で選んだら確定する");
    }

    [Test]
    public static void DigitKey_MovesToNextClause()
    {
        var k = new Keyboard();
        k.Type("tanniwotoru ");
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
        Assert.True(!k.Gate.IsCaptured, "最後の文節を番号で選んだら確定する");
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
            var k = new Keyboard(slashAsMiddleDot: on) { SigilWords = false }; // 先頭の /help は #193 でそのまま入力になるので切って比べる
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, $"{typed} ({(on ? "ON" : "OFF")})");
        }
    }

    [Test]
    public static void Reconversion_WithoutSelectionDoesNothing()
    {
        var k = new Keyboard();
        k.Press(VirtualKeys.Convert);
        Assert.True(!k.Gate.IsCaptured, "選択文字なしでは変換を開始しない");
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
        Assert.True(!k.Gate.IsCaptured, "読みを消し切ったら再変換を終了する");
        k.Type("kana");
        k.Press(VirtualKeys.Return);
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:", StringComparison.Ordinal)), "次の入力に再変換の状態を持ち越さない");
        Assert.True(k.Host.Output.Count > 0, "次の入力は普通に確定する");
    }

    [Test]
    public static void Reconversion_FocusLossAbandonsWithoutReplacement()
    {
        var k = new Keyboard();
        k.Host.Selection = new ReconversionSelection("今日", "きょう");
        k.Press(VirtualKeys.Convert);
        Assert.True(k.Controller.Abandon("フォーカスが変わった"), "再変換を破棄する");
        k.Controller.Pump();
        Assert.True(!k.Gate.IsCaptured, "フォーカス喪失後はキーを解放する");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:", StringComparison.Ordinal)), "フォーカス喪失時は置換しない");
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
        Assert.True(!k.Gate.IsCaptured, "確定後はキーを横取りしない");
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
            ["githubnipush"] = "githubにpush",
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
        foreach (var (typed, expected) in new[] { ("e,shiran ", "え|、|知らん"), ("ekaku ", "絵|描く"), ("gachideyabai ", "ガチで|ヤバい"),
            // 英単語 + する: push|下、commit|死体 になっていた。英単語の無いところ (した = 下) は変換エンジンのまま
            ("pushshita ", "push|した"), ("commitshitai ", "commit|したい"), ("shita ", "下") })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, string.Join("|", k.Host.View!.Clauses!), typed);
        }
    }

    public static void Clauses_SelectWithArrowsAndConvertEach()
    {
        var k = new Keyboard();
        k.Type("tanniwotoru ");
        Assert.Equal("単位を|取る", string.Join("|", k.Host.View!.Clauses!), "Space で文節に区切って変換");
        Assert.Equal(0, k.Host.View.SelectedClause, "最初は先頭の文節を選択");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View.SelectedClause, "→ で次の文節");
        k.Type(" ");
        Assert.Equal("単位を|とる", string.Join("|", k.Host.View.Clauses!), "Space で選択中の文節だけ次の候補");
        k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View.SelectedClause, "← で前の文節");
        k.Type("\n");
        Assert.Equal("単位をとる", k.Host.Output.Single());
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
    public static void CorpusContextRules_PreferChatFormsWhenCued()
    {
        var rules = ContextRules.Load(null);
        Assert.Equal("ガチで", rules.Choose("かちで", "終わってる")!);
        Assert.Equal("垢", rules.Choose("あか", "Twitterのアカウント")!);
        Assert.Equal("鯖", rules.Choose("さば", "Discordコミュ")!);
        Assert.Equal("めるちゃん", rules.Choose("めるちゃん", "先輩")!);
        Assert.Equal("ガチで", rules.Choose("かちで", "オンゲーしかしてないから知らん")!);
        Assert.Equal("音ゲーしか", rules.Choose("おんげーしか", "ACの曲を漁ろう")!);
        Assert.Equal("淫夢", rules.Choose("いんゆめ", "R18画像")!);
        Assert.Equal("すこん部", rules.Choose("すこんぶ", "鯖のオーナー")!);
        Assert.Equal("音ゲーしか", rules.Choose("おんげーしか", "してないから")!);
        Assert.Equal("淫", rules.Choose("いん", "夢のr－18画像")!);
        Assert.Equal("鯖", rules.Choose("さば", "すこん部のオーナ人")!);
        Assert.Equal("え", rules.Choose("え", "ほんと")!);
        Assert.Equal("うちの", rules.Choose("うちの", "家族は")!);
        Assert.Equal("ねむ", rules.Choose("ねむ", "先輩に聞いて")!);
        Assert.Equal("いま", rules.Choose("いま", "やるなら")!);
        Assert.Equal("垢", rules.Choose("あか", "ログインできなくなって")!);
        Assert.Equal("なに", rules.Choose("なに", "って")!);
        Assert.Equal("めるちゃん", rules.Choose("めるちゃん", "後輩")!);
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
        foreach (var (typed, expected) in new[]
        {
            ("e-mail", "e-mail"), ("co-op", "co-op"), ("bio-break", "bio-break"), ("e-maildeokuru", "e-mailでおくる"), ("x-ray", "x-ray"), ("lo-fi", "lo-fi"), ("Lo-fi", "Lo-fi"), ("sci-fi", "sci-fi"), ("lo-fiwokiku", "lo-fiをきく"), ("r-18", "r-18"), ("sub-6", "sub-6"), ("GPT-6.7", "GPT-6.7"), ("Blu-ray", "Blu-ray"),
            ("e-to", "えーと"), ("su-pa-", "すーぱー"), ("o-bun", "おーぶん"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
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
    public static void Issue409_TanomeOffersBirthdaySlang()
    {
        var dictionary = CandidateDictionary.Load(null);
        Assert.True(dictionary.Lookup("たんおめ").Contains("誕おめ"), "たんおめ → 誕おめ");
    }

    [Test]
    public static void Issue404_Gemma4StaysEnglish()
    {
        var k = new Keyboard();
        k.Type("Gemma4\n");
        Assert.Equal("Gemma4", k.Host.Document);
    }

    [Test]
    public static void Issue469_HoloXStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("holoX\n");
        Assert.Equal("holoX", k.Host.Document);
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
    public static void Issue346_FusionStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("fusion\n");
        Assert.Equal("fusion", k.Host.Document);
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
        var spellChecker = Detector.SpellChecker;
        try
        {
            foreach (var spell in new[] { false, true })
            {
                foreach (var live in new[] { false, true })
                {
                    Detector.SpellChecker = spell ? Detection.BuiltInWordChecker.Shared : null;
                    foreach (var (typed, expected) in new[]
                    {
                ("usbtunaide", "usbつないで"), ("usbtsunaide", "usbつないで"),
                ("USBtunaide", "USBつないで"), ("pctunaide", "pcつないで"),
                ("usbsuru", "usbする"), ("usbtsukatte", "usbつかって"),
                ("usbnuite", "usbぬいて"), ("usbwotunaide", "usbをつないで"),
                ("hdmitunaide", "hdmiつないで"),
            })
                    {
                        var message = $"{typed} (スペル: {spell}, ライブ変換: {live})";
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
        }
        finally
        {
            Detector.SpellChecker = spellChecker;
        }
    }

    [Test]
    public static void Issue1132_ShortEnglishThenPendingJapanese()
    {
        var spellChecker = Detector.SpellChecker;
        try
        {
            foreach (var spell in new[] { false, true })
            {
                foreach (var live in new[] { false, true })
                {
                    Detector.SpellChecker = spell ? Detection.BuiltInWordChecker.Shared : null;
                    foreach (var (typed, showing, committed) in new[]
                    {
                ("usbtun", "usbつn", "usbつん"),
                ("usbtsukatt", "usbつかっt", "usbつかっt"),
            })
                    {
                        var message = $"{typed} (スペル: {spell}, ライブ変換: {live})";
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
        }
        finally
        {
            Detector.SpellChecker = spellChecker;
        }
    }

    [Test]
    public static void Issue1132_KnownLimitation_ReadableEnglishAfterShortPrefix()
    {
        // 既知の問題: 本来は pronate のまま英字にしたいが、pro + なて と読めるため日本語との区別がつかない。
        var spellChecker = Detector.SpellChecker;
        try
        {
            foreach (var spell in new[] { false, true })
            {
                Detector.SpellChecker = spell ? Detection.BuiltInWordChecker.Shared : null;
                var k = new Keyboard();
                k.Type("pronate");
                Assert.Equal("proなて", k.Showing, "既知の問題の現在の表示");
                k.Type("\n");
                Assert.Equal("proなて", k.Host.Document, "本来の期待は pronate。判定を改善したときに更新する");
            }
        }
        finally
        {
            Detector.SpellChecker = spellChecker;
        }
    }

    [Test]
    public static void Issue506_EnumerateStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("enumerate\n");
        Assert.Equal("enumerate", k.Host.Document);
    }

    [Test]
    public static void Issue536_FatigueStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("fatigue\n");
        Assert.Equal("fatigue", k.Host.Document);
    }

    [Test]
    public static void Issue538_InfobesityStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("infobesity\n");
        Assert.Equal("infobesity", k.Host.Document);
    }
  
    [Test]
    public static void Issue505_LiveConvertIgnoresSymbolsInLength()
    {
        var k = new Keyboard(live: true);
        k.Type("na~~~\n");
        Assert.Equal("な～～～", k.Host.Document);
    }

    [Test]
    public static void Issue616_WhoisStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("whois\n");
        Assert.Equal("whois", k.Host.Document);
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
    public static void Issue656_SiriHyphenIsLongVowel()
    {
        var k = new Keyboard();
        k.Type("siri-zu\n");
        Assert.Equal("しりーず", k.Host.Document);
    }

    [Test]
    public static void Issue669_OpenStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("imaopenshita\n");
        Assert.Equal("いまopenした", k.Host.Document);
    }

    [Test]
    public static void Issue674_FyuIsFuxyu()
    {
        var k = new Keyboard();
        k.Type("fyu\n");
        Assert.Equal("ふゅ", k.Host.Document);
    }

    [Test]
    public static void Issue683_GuesstimationStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("guesstimation\n");
        Assert.Equal("guesstimation", k.Host.Document);
    }

    [Test]
    public static void Issue686_EnshittificationStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("enshittification\n");
        Assert.Equal("enshittification", k.Host.Document);
    }

    [Test]
    public static void Issue689_FinfluencerStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("Finfluencer\n");
        Assert.Equal("Finfluencer", k.Host.Document);
    }

    [Test]
    public static void Issue690_DatamationStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("datamation\n");
        Assert.Equal("datamation", k.Host.Document);
    }

    [Test]
    public static void Issue691_AdwareStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("adware\n");
        Assert.Equal("adware", k.Host.Document);
    }

    [Test]
    public static void Issue692_GreedflationStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("greedflation\n");
        Assert.Equal("greedflation", k.Host.Document);
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
        Assert.Equal("お父さん", dictionary.Split("お")?.Single().Word, "その 1 文字だけを変換するときは登録した単語");
        Assert.True(dictionary.Split("おかね") is null, "ほかの語の途中の 1 文字は巻き込まない");
    }

    [Test]
    public static void Issue712_JapaneseStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("englishtojapanesewomazeteuteru\n");
        Assert.Equal("englishとjapaneseをまぜてうてる", k.Host.Document);
    }

    [Test]
    public static void Issue724_BingeWatchStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("binge-watch\n");
        Assert.Equal("binge-watch", k.Host.Document);
    }

    [Test]
    public static void Issue725_OpenStreetMapStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("openstreetmap\n");
        Assert.Equal("openstreetmap", k.Host.Document);
    }

    [Test]
    public static void Issue726_MalvertisingStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("malvertising\n");
        Assert.Equal("malvertising", k.Host.Document);
    }

    [Test]
    public static void Issue727_AdBlockerStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("ad-blocker\n");
        Assert.Equal("ad-blocker", k.Host.Document);
    }

    [Test]
    public static void Issue730_AdtechStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("adtech\n");
        Assert.Equal("adtech", k.Host.Document);
    }

    [Test]
    public static void Issue731_RobonomicsStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("robonomics\n");
        Assert.Equal("robonomics", k.Host.Document);
    }

    [Test]
    public static void Issue732_MobisodeStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("mobisode\n");
        Assert.Equal("mobisode", k.Host.Document);
    }

    [Test]
    public static void Issue733_NetizenryStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("netizenry\n");
        Assert.Equal("netizenry", k.Host.Document);
    }

    [Test]
    public static void Issue735_TverStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("tver\n");
        Assert.Equal("tver", k.Host.Document);
    }

    [Test]
    public static void Issue344_Issue341_NumbersOfferKanjiAndEra()
    {
        Assert.Equal("二〇二六|二千二十六|弐千弐拾六|令和8年", string.Join("|", NumberCandidates.For("2026")), "2026");
        Assert.Equal("一二三四五|一万二千三百四十五|壱万弐千参百四拾五", string.Join("|", NumberCandidates.For("12345")), "12345");
        Assert.Equal("一〇〇|百|壱百", string.Join("|", NumberCandidates.For("100")), "100");
        Assert.Equal("一〇〇〇〇|一万|壱万", string.Join("|", NumberCandidates.For("10000")), "10000");
        Assert.Equal("〇〇一", string.Join("|", NumberCandidates.For("001")), "先頭が 0 の数字は位取りしない");
        Assert.Equal("一九八九|千九百八十九|壱千九百八拾九|平成元年", string.Join("|", NumberCandidates.For("1989")), "1989 = 平成元年");
        Assert.Equal(0, NumberCandidates.For("abc").Count, "数字でなければ候補なし");
    }

    [Test]
    public static void NumberAndEraCandidates_DoNotDependOnCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.True(NumberCandidates.For("２０２６").Contains("令和8年"), "全角数字をいつも同じ元号にする");
            Assert.Equal("2026年|2026", string.Join("|", CompositionController.EraToYear("れいわ８ねん")!));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public static void Issue344_DigitsOfferKanjiWhenConverting()
    {
        var k = new Keyboard();
        k.Type("2026");
        k.Host.PhysicalShift = true;
        k.Press(VirtualKeys.Space);
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("二〇二六") && candidates.Contains("令和8年"), "数字の候補に漢数字・元号: " + string.Join(",", candidates));
    }

    [Test]
    public static void Issue411_ChangeStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("changesuru\n");
        Assert.Equal("changeする", k.Host.Document);
    }

    [Test]
    public static void Issue485_OpenNoStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("imaopenno\n");
        Assert.Equal("いまopenの", k.Host.Document);
    }

    [Test]
    public static void Issue708_ShiftSpaceCanBeHalfWidth()
    {
        var k = new Keyboard { ShiftSpaceHalfWidth = true };
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
    public static void Issue677_EnterCanCommitAsShown()
    {
        // 打ち間違い (onegaishimsu) は、既定では確定するときに直す。設定 ON なら見えているとおりに確定する
        var normal = new Keyboard();
        normal.Type("onegaishimsu\n");
        var shownKeyboard = new Keyboard { EnterCommitsAsShown = true };
        shownKeyboard.Type("onegaishimsu");
        var shown = shownKeyboard.Showing;
        shownKeyboard.Type("\n");
        Assert.Equal(shown, shownKeyboard.Host.Document, "見えているとおりに確定");
        Assert.True(normal.Host.Document != shown, "既定は確定するとき直す: " + normal.Host.Document + " / " + shown);
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
        var k = new Keyboard { SquareBrackets = true, FullWidthSlash = true };
        k.Type("[nihongo]aiueo/kakikukeko\n");
        Assert.Equal("［にほんご］あいうえお／かきくけこ", k.Host.Document);
        var normal = new Keyboard();
        normal.Type("[nihongo]\n");
        Assert.Equal("「にほんご」", normal.Host.Document, "OFF は今までどおり");
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
            k.Type("tanniwogoogleto ");
            k.Press(VirtualKeys.Right);
            Assert.Equal("たんいを|google|と", string.Join("|", k.Host.View!.Clauses!), "2 番目の英語の文節を選ぶ");
            k.Press(vk);
            Assert.True(k.Host.View!.Converting, "変換は続く");
            Assert.Equal($"たんいを|{expected}|と", string.Join("|", k.Host.View!.Clauses!), "選んだ英語の文節だけが変わる");
        }
    }

    [Test]
    public static void Issue599_CtrlKLResizeTheClause()
    {
        var k = new Keyboard { CompatibleShortcuts = true };
        k.Type("tanniwotoru ");
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
    public static void Issue341_EraYearOffersGregorianYear()
    {
        var k = new Keyboard();
        k.Type("reiwagannnenn ");
        Assert.True(k.Host.View!.Candidates.Contains("2019年"), "令和元年: " + string.Join(",", k.Host.View.Candidates));
        Assert.Equal("2026年", CompositionController.EraToYear("れいわ8ねん")!.First(), "令和8年");
        Assert.Equal("1989年", CompositionController.EraToYear("へいせいがんねん")!.First(), "平成元年");
        Assert.Equal("1926年", CompositionController.EraToYear("しょうわ1ねん")!.First(), "昭和1年");
        Assert.True(CompositionController.EraToYear("へいせい40ねん") is null, "平成は 31 年まで");
    }

    [Test]
    public static void Issue340_TraditionalUnitsOfferMetricSize()
    {
        Assert.Equal("3尺|約90.91cm", string.Join("|", CompositionController.TypedUnitCandidates("3しゃく")!), "3 尺");
        Assert.Equal("1坪|約3.31㎡", string.Join("|", CompositionController.TypedUnitCandidates("1つぼ")!), "1 坪");
        Assert.Equal("2貫|約7.5kg", string.Join("|", CompositionController.TypedUnitCandidates("2かん")!), "2 貫");
        Assert.True(CompositionController.TypedUnitCandidates("かん") is null, "数字が無ければ対象外");
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
        var k = new Keyboard { MacControlKeys = true };
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
    public static void Issue249_MozcThenJapanese()
    {
        var k = new Keyboard();
        k.Type("mozcgamiteirutohaie\n");
        Assert.Equal("mozcがみているとはいえ", k.Host.Document);
    }

    [Test]
    public static void Issue268_DeVitoThenParticle()
    {
        var k = new Keyboard();
        k.Type("DeVitode\n");
        Assert.Equal("DeVitoで", k.Host.Document);
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
    public static void Issue286_KeyIntervalsAreLoggedWithoutText()
    {
        var k = new Keyboard { LogKeyIntervals = true };
        var before = Diagnostics.Log.Snapshot().Length;
        var version = Diagnostics.Log.Version;
        k.Type("hello");
        var added = Diagnostics.Log.Snapshot().Select(x => x.Message).Where(x => x.Contains("打鍵の間隔")).ToList();
        Assert.True(added.Count >= 3, "間隔を残す: " + added.Count);
        Assert.True(added.All(x => !x.Contains("hello")), "打った文字は残さない");
    }

    [Test]
    public static void Issue713_SingleKanaThenWIsLaughter()
    {
        var k = new Keyboard { LaughterAfterKana = true };
        k.Type("naruhodonew\n");
        Assert.Equal("なるほどねw", k.Host.Document, "なるほどnew → なるほどねw");
        var saw = new Keyboard { LaughterAfterKana = true };
        saw.Type("saw\n");
        Assert.Equal("さw", saw.Host.Document, "saw → さw");
        // 1 回の入力に かな + w が 2 つ以上あっても、それぞれ分ける (2 つ目が別の区間を書き換えていた)
        var twice = new Keyboard { LaughterAfterKana = true };
        twice.Type("newtosaw\n");
        Assert.Equal("ねwとさw", twice.Host.Document, "2 つとも笑いの w (2 つ目が別の区間を書き換えて ねwさsaww になっていた)");
        var spread = new Keyboard { LaughterAfterKana = true };
        spread.Type("ganbattenewdesunesaw\n");
        Assert.Equal("がんばってねwですねさw", spread.Host.Document, "間に日本語があっても");
        var normal = new Keyboard();
        normal.Type("naruhodonew\n");
        Assert.Equal("なるほどnew", normal.Host.Document, "OFF は今までどおり");
    }

    [Test]
    public static void Issue491_LearningCanBeSeparatedByContext()
    {
        var memory = new LanguageMemory(null) { ContextSensitive = true };
        memory.Context = false;
        memory.Remember("nw", english: false, explicitChoice: true);
        memory.Context = true;
        memory.Remember("nw", english: true, explicitChoice: true);
        memory.Context = false;
        Assert.Equal(false, memory.Get("nw"), "日本語の後では日本語");
        memory.Context = true;
        Assert.Equal(true, memory.Get("nw"), "英語の後では英語");
        // 学習した語の一覧には、印 (@e / @j) を出さず、語そのものと文脈を分けて返す。消すときは印付きの名前
        var listed = memory.Entries().OrderBy(e => e.Context).ToList();
        Assert.True(listed.All(e => e.Word == "nw"), "語に印が付かない: " + string.Join(",", listed.Select(e => e.Word)));
        Assert.True(listed.Select(e => e.Context).SequenceEqual(["日本語の後", "英語の後"]) || listed.Select(e => e.Context).SequenceEqual(["英語の後", "日本語の後"]), "文脈が分かる");
        memory.Remove([listed.First(e => e.Context == "英語の後").EntryKey]);
        Assert.Equal(1, memory.Entries().Count, "文脈ごとに消せる");
        memory.Remember("nw", english: true, explicitChoice: true);
        memory.Context = null;
        Assert.True(memory.Get("nw") is null, "文脈が分からなければ、文脈ごとの学習は使わない");
        var plain = new LanguageMemory(null);
        plain.Remember("nw", english: true, explicitChoice: true);
        plain.Context = false;
        Assert.Equal(true, plain.Get("nw"), "OFF は今までどおり 1 つだけ");
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
        var k = new Keyboard { FullWidthSpaceByDefault = true };
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
    public static void Issue666_CandidatesCanBeShownWhileTyping()
    {
        var k = new Keyboard { ShowCandidatesWhileTyping = true };
        k.Type("kyouha");
        Assert.True(!k.Host.View!.Converting, "変換はまだ始めていない");
        Assert.True(k.Host.View.Candidates.Count >= 2, "入力中も候補を出す: " + string.Join(",", k.Host.View.Candidates));
        var off = new Keyboard();
        off.Type("kyouha");
        Assert.Equal(0, off.Host.View!.Candidates.Count, "OFF は出さない");
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
        var k = new Keyboard { GitHubIssueCandidates = true, GitHubRepository = "yksr-melt/Meltype", GitHubTitles = titles };
        k.Type("#123 ");
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("#123 変換ボックスが隠れる") && candidates.Contains("yksr-melt/Meltype#123"), string.Join(",", candidates));
        var off = new Keyboard { GitHubRepository = "yksr-melt/Meltype", GitHubTitles = titles };
        off.Type("#123 ");
        Assert.True(off.Host.View is null || !off.Host.View.Candidates.Contains("#123 変換ボックスが隠れる"), "OFF は問い合わせない");
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
        // #123 は # と 123 を別々の文節に変換しない (タイトルが無い・設定 OFF でも)
        var k = new Keyboard();
        k.Type("#123 ");
        Assert.True(k.Host.View is { } view && view.Candidates.Contains("#123") && view.Candidates.Contains("＃１２３"), string.Join(",", k.Host.View?.Candidates ?? []));
        var none = new Keyboard { GitHubIssueCandidates = true, GitHubRepository = "a/b", GitHubTitles = new GitHubIssueTitles((_, _) => Task.FromResult<string?>(null)) };
        none.Type("#45 ");
        Assert.True(none.Host.View is { } v && v.Candidates.Contains("#45"), string.Join(",", none.Host.View?.Candidates ?? []));
    }

    [Test]
    public static void Issue866_GitHubRepositorySettingIsNormalized()
    {
        // 設定のリポジトリは、空白・URL・.git が付いていても「所有者/名前」として問い合わせる
        foreach (var setting in (string[])[" yksr-melt/Meltype ", "https://github.com/yksr-melt/Meltype", "github.com/yksr-melt/Meltype.git", "https://github.com/yksr-melt/Meltype/issues/1"])
        {
            Assert.Equal("yksr-melt/Meltype", GitHubIssueTitles.Normalize(setting), setting);
            var titles = new GitHubIssueTitles((repository, number) => Task.FromResult<string?>(repository == "yksr-melt/Meltype" && number == 866 ? "候補に出ない" : null));
            var k = new Keyboard { GitHubIssueCandidates = true, GitHubRepository = setting, GitHubTitles = titles };
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
        Assert.True(CompositionController.ClipboardLine("a\nb") is null && CompositionController.ClipboardLine(new string('あ', 201)) is null && CompositionController.ClipboardLine(" ") is null, "複数行・長い・空は出さない");
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
    public static void Issue677_EnterAsShown_KeepsKanaBeforeFinalN()
    {
        // かんたn と見えていた入力は、Enter でかんたんに確定する (語末の n が ん になったところで漢字にしない)
        var shown = new Keyboard(live: true) { EnterCommitsAsShown = true };
        shown.Type("kantan");
        shown.Type("\n");
        Assert.Equal("かんたん", shown.Host.Document, "設定 ON: 見えていたかなのまま");
        var normal = new Keyboard(live: true);
        normal.Type("kantan\n");
        Assert.Equal("簡単", normal.Host.Document, "既定は今までどおり");
        var done = new Keyboard(live: true) { EnterCommitsAsShown = true };
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
    public static void Issue421_DefineStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("kachiwodefinesuru\n");
        Assert.Equal("かちをdefineする", k.Host.Document);
    }

    [Test]
    public static void Issue419_DefuseStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("mondaiwodefusesuru\n");
        Assert.Equal("もんだいをdefuseする", k.Host.Document);
    }

    [Test]
    public static void Issue435_BluRayStaysEnglish()
    {
        var k = new Keyboard();
        k.Type("Blu-ray\n");
        Assert.Equal("Blu-ray", k.Host.Document);
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
    public static void LaterLongerEnglishWord_WinsOverShorterOne()
    {
        // motteschoolhe が mottes (英単語) + ちょおl + へ になっていた。持って + school + へ。
        var k = new Keyboard();
        k.Type("kameramotteschoolheiku\n");
        Assert.Equal("かめらもってschoolへいく", k.Host.Document);
    }

    [Test]
    public static void SingleCapital_ThenParticle_IsSplit()
    {
        // Anisiyouka が英字のままになっていた (A にしようか)。名前 (Tanaka) は区切らない。
        foreach (var (typed, expected) in new[] { ("Anisiyouka", "Aにしようか"), ("Xgawakaru", "Xがわかる"), ("Tanaka", "Tanaka") })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void CommaAfterDigit_IsTouten_UnlessDigitFollows()
    {
        // x64,arm64 の , は読点 (x64、arm64)。1,000 の , は桁区切りのまま。
        foreach (var (typed, expected) in new[] { ("x64,arm64", "x64、arm64"), ("1,000en", "1,000えん") })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
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

    [Test]
    public static void Punctuation_AfterEnglish_FollowsSetting()
    {
        // #416: 英字のすぐ後ろの , . も句読点にする設定。OFF なら今までどおり半角
        string Type(string keys, bool on, string? before = null, Meltype.Config.PunctuationStyle style = default)
        {
            Detector.UseJapanesePunctuationAfterEnglish = () => on;
            try
            {
                var k = new Keyboard { Punctuation = style };
                k.Host.PrecedingText = before;
                k.Type(keys);
                return k.Host.Document;
            }
            finally
            {
                Detector.UseJapanesePunctuationAfterEnglish = null;
            }
        }
        Assert.Equal("IME。", Type("IME.\n", on: true), "略語の後ろも句点");
        Assert.Equal("Google.", Type("Google.\n", on: false), "OFF は半角のまま");
        Assert.Equal("Google。", Type("Google.\n", on: true), "ON は句点");
        Assert.Equal("Google、", Type("Google,\n", on: true), "ON は読点");
        Assert.Equal("Google．", Type("Google.\n", on: true, style: Meltype.Config.PunctuationStyle.FullWidthCommaPeriod), "設定「句読点」の形");
        Assert.Equal("world.", Type("world.\n", on: true, before: "Hello "), "キャレットの前が英語 (Hello ) なら英文の続き");
        Assert.Equal("tetr.io", Type("tetr.io\n", on: true), "英単語の中の . は半角");
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
        Assert.True(k.Gate.IsCaptured, "判定中は打鍵を受け取る");
        k.Controller.Tick(k.Now + 1000);
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:", StringComparison.Ordinal)), "しばらく打たなければ英語とみなす (送り直さない)");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), "消さない");
        Assert.True(!k.Gate.IsCaptured, "判定をやめたら横取りもやめる");
    }

    [Test]
    public static void DirectMode_ShiftAfterHeldWordReachesApp()
    {
        // 英数状態で英字を打った直後に押した Shift がアプリに届かず、(bow) の ) が Shift なしの 0 になっていた。
        var k = new Keyboard(direct: true);
        k.Type("bow");
        Assert.True(k.Gate.IsCaptured, "bow はまだ判定中");
        k.Host.Events.Clear();
        k.Key(VirtualKeys.LShift);
        k.Press(0x30);
        k.Key(VirtualKeys.LShift, up: true);
        var shift = k.Host.Events.IndexOf("down:A0");
        Assert.True(shift >= 0 && shift < k.Host.Events.IndexOf("passed:30"), $"Shift を 0 より先にアプリへ送る ({string.Join(",", k.Host.Events)})");
        Assert.True(!k.Gate.IsCaptured, "判定をやめたら横取りもやめる");
    }

    [Test]
    public static void ShiftRepeatWhileComposing_ReleaseReachesApp()
    {
        // キーボードが 2 台だと、文字を打っても Shift のリピートが止まらず、離しても Shift が押されたままになっていた (issue #617)。
        var k = new Keyboard();
        k.Host.PhysicalShift = true; // Shift の押下は、変換ボックスが開く前にアプリへ届いている
        k.Press(0x41);
        Assert.True(k.Gate.IsCaptured, "A で変換ボックスが開く");
        k.Key(VirtualKeys.RShift); // リピート
        k.Press(0x41);
        k.Key(VirtualKeys.RShift);
        k.Host.Events.Clear();
        k.Key(VirtualKeys.RShift, up: true);
        Assert.True(k.Host.Events.Contains("up:A1"), $"Shift を離したことをアプリに伝える ({string.Join(",", k.Host.Events)})");
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
        k.Type("iphonede");
        k.Press(VirtualKeys.Left); // 最後の文節 (で)
        k.Press(VirtualKeys.Left); // iphone
        k.Type(" ");
        Assert.Equal("iPhone", k.Host.View!.Clauses![0], "候補に正しい大文字小文字の形がある");
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
        Assert.Equal("行け", rules.Choose("いけ", "ねむ学校"), "学校いけ → 行け (池にしない)");
        Assert.Equal("行けよ", rules.Choose("いけよ", "早く"), "助詞付きでも 行け");
        Assert.Equal("池", rules.Choose("いけ", "公園の"), "公園なら 池");
        Assert.Equal(null, rules.Choose("いけん", "学校の"), "意見 を 行けん にしない");
        Assert.Equal(null, rules.Choose("いけない", "学校で"), "いけない を 行けない にしない");
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
        k.Controller.ForgetLastCommit(); // Meltype を通らないキー (矢印など) が押された
        k.Type("ant ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), "キャレットが動いたかもしれないので消さない");
    }

    [Test]
    public static void AutoCorrect_NotWhenCaretMovedOnMac()
    {
        // 報告 (#137): 確定した後に別の場所をクリックしてから英語の語を確定すると、前の語ではなく
        // 「今のキャレットの前の文字」が消される。Mac はクリックを知らされない (ForgetLastCommit が呼ばれない) ので、
        // 消す前に「直前に確定した語が今のキャレットの位置にあるか」を入力欄に確かめる。
        var k = new Keyboard();
        k.Host.SurroundingTextIsCurrent = true;
        k.Type("i ");
        k.Host.PrecedingText = "ほかのところ"; // 別の場所をクリックした (Mac なので知らされない)
        k.Type("want ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), $"前の語が今のキャレットの位置に無いので消さない: {string.Join("|", k.Host.Events)}");
    }

    [Test]
    public static void AutoCorrect_StillCorrectsWhenCaretStaysAtTheWord()
    {
        // Mac (入力欄の文字をその場で読める) でも、キャレットの直前に前の語があるなら今までどおり直す。
        var k = new Keyboard();
        k.Host.SurroundingTextIsCurrent = true;
        k.Type("i want ");
        Assert.Equal("I want ", k.Host.Document, string.Join("|", k.Host.Events));
    }

    [Test]
    public static void AutoCorrect_StillCorrectsWhenSurroundingTextIsNotCurrent()
    {
        // Windows は入力欄の文字を別のスレッドで後から読むので、届いた値が古いことがある (キャレットが動いたことは
        // ForgetLastCommit で知らされる)。読めていない環境では今までどおり直す。
        var k = new Keyboard();
        k.Type("i ");
        k.Host.PrecedingText = "ほかのところ";
        k.Type("want ");
        Assert.True(k.Host.Events.Any(e => e.StartsWith("bs:", StringComparison.Ordinal)), $"読めていない環境では直す: {string.Join("|", k.Host.Events)}");
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
        var cases = new Dictionary<string, string> { ["[kagi]"] = "「かぎ」", ["-"] = "ー", ["/"] = "/", ["z/"] = "・", ["#"] = "#", ["("] = "(", [")"] = ")", ["]"] = "」", ["@"] = "@", [",,,"] = "...", ["\\"] = "￥", [","] = "、",
            // 報告: Shift で打つ記号が全角で打てない、/ が打てない。英語の中では半角のまま。
            ["$%&"] = "＄％＆", ["kyouha(tenki)"] = "きょうは(てんき)", ["hello@example"] = "hello@example",
            ["tetr.io"] = "tetr.io", ["Wakatte.TV"] = "Wakatte.TV", ["J-core"] = "J-core", ["p-hub"] = "p-hub", ["talk-admin"] = "talk-admin",
            // 報告: ca / cu / co で か く こ
            ["cacuco"] = "かくこ", ["iijane"] = "いいじゃね", ["shoucanshi"] = "しょうかんし", ["vanpaia"] = "ゔぁんぱいあ", ["wyiwye"] = "ゐゑ", ["GPL3.0"] = "GPL3.0", ["3.14desu"] = "3.14です", ["oknotasuku"] = "okのたすく",
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
        Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
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
        finally
        {
            Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void BuiltInPhrases_AreSplitOut()
    {
        // 報告: 白馬の王子様 → ハクバノ王子サマ、ばらまいてた愛 → ばらまいて他愛
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Split("はくばのおうじさま")?.Any(p => p.Word == "白馬の王子様") == true, "白馬の王子様");
        Assert.True(dictionary.Split("ばらまいてたあい")?.First().Word == "ばらまいてた", "ばらまいてた|あい");
        // 報告 (#196): しょたこん → ショタこん
        Assert.True(dictionary.Split("しょたこん")?.Any(p => p.Word == "ショタコン") == true, "しょたこん → ショタコン");
        // 報告 (#1028): todorisuto → トドリスト
        Assert.True(dictionary.Split("とどりすと")?.Any(p => p.Word == "todoリスト") == true, "とどりすと → todoリスト");
        Assert.True(dictionary.Split("りょかい")?.Any(p => p.Word == "りょかい") == true, "りょかい → りょかい (#1041)");
        Assert.True(dictionary.Split("いちがっき")?.Any(p => p.Word == "一学期") == true, "いちがっき → 一学期 (#1037)");
        Assert.True(dictionary.Split("にがっき")?.Any(p => p.Word == "二学期") == true, "にがっき → 二学期 (#1037)");
        Assert.True(dictionary.Split("さんがっき")?.Any(p => p.Word == "三学期") == true, "さんがっき → 三学期 (#1037)");
        // 数字と 学期 は別の文節にする (いち|がっき → 一|学期)
        Assert.Equal("いち:一|がっき:学期", string.Join("|", dictionary.Clauses("いちがっき", "一学期").Select(c => $"{c.Reading}:{c.Word}")), "一|学期");
        Assert.Equal("しょせん:所詮", string.Join("|", dictionary.Clauses("しょせん", "所詮").Select(c => $"{c.Reading}:{c.Word}")), "区切りの無い語句は 1 つの文節");
        Assert.True(dictionary.Split("どしたの")?.Any(p => p.Word == "どしたの") == true, "どしたの → どしたの (#1014)");
        Assert.True(dictionary.Split("したので")?.Any(p => p.Word == "したので") == true, "したので → したので (#1019)");
        Assert.True(dictionary.Split("けんりつ")?.Any(p => p.Word == "県立") == true, "けんりつ → 県立 (#1004)");
        Assert.True(dictionary.Split("しりーず")?.Any(p => p.Word == "シリーズ") == true, "しりーず → シリーズ (#853)");
        // 報告 (#872): きょうひさしぶり → 共久しぶり。今日|久しぶり の 2 つの文節にする
        var hisashiburi = dictionary.Split("きょうひさしぶり");
        Assert.True(hisashiburi is [("きょう", null), ("ひさしぶり", "久しぶり")], "きょう|ひさしぶり → きょう|久しぶり (#872)");
        Assert.True(dictionary.Split("なさそう")?.Any(p => p.Word == "なさそう") == true, "なさそう → なさそう (#825)");
        Assert.True(dictionary.Split("になった")?.Any(p => p.Word == "になった") == true, "になった → になった (#917)");
        Assert.True(dictionary.Split("ふぉろばく")?.Any(p => p.Word == "フォロ爆") == true, "ふぉろばく → フォロ爆 (#1268)");
        Assert.True(dictionary.Split("きゅうくらりん")?.Any(p => p.Word == "きゅうくらりん") == true, "きゅうくらりん → きゅうくらりん (#1278)");
        Assert.True(dictionary.Split("ぶいすぽ")?.Any(p => p.Word == "ぶいすぽ") == true, "ぶいすぽ → ぶいすぽ (#1286)");
        Assert.Equal(0, dictionary.Count, "同梱の語句はユーザー辞書の一覧に出さない");
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
        // # は最初から半角 (Discord のチャンネル名・ハッシュタグ)。Space で全角の ＃ にできる。
        k = new Keyboard();
        k.Type("# ");
        Assert.Equal("#", k.Host.View!.Candidates[0]);
        Assert.True(k.Host.View.Candidates.Contains("＃"), "# の候補に ＃ がある: " + string.Join(",", k.Host.View.Candidates));
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
        Assert.Equal("2025年10月", CompositionController.NormalizeHalfWidth("２０２５年１０月", "2025ねん10がつ"), "変換エンジンが全角にした数字は半角に戻す");
        Assert.Equal("１つ", CompositionController.NormalizeHalfWidth("１つ", "ひとつ"), "読みに半角数字が無ければそのまま");
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
        Assert.Equal("sだけが", CompositionController.NormalizeHalfWidth("ｓだけが", "sだけが"));
        Assert.Equal("2025年", CompositionController.NormalizeHalfWidth("２０２５年", "2025ねん"));
        Assert.Equal("ＡＢＣ", CompositionController.NormalizeHalfWidth("ＡＢＣ", "えーびーしー"), "読みに英数字が無ければ全角のまま");
    }

    /// <summary>決めた文節に区切る変換エンジン (計算の言葉のテスト用)。</summary>
    private sealed class ClauseConverter(Dictionary<string, ConversionClause[]> clauses) : IKanjiConverter
    {
        private readonly FakeConverter _inner = new();
        public string? Convert(string hiragana) => hiragana switch { "かける" => "掛ける", "わる" => "割る", "たす" => "足す", _ => _inner.Convert(hiragana) };
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            clauses.TryGetValue(hiragana, out var result) ? result : _inner.ConvertClauses(hiragana, context);
    }

    [Test]
    public static void MathWordsAfterDigits_OfferSymbols()
    {
        // #293: 数字のすぐ後ろの かける・わる は × ÷ を最初の候補に。変換エンジンが数字と同じ文節にしても (3かける) 別の文節にしても
        var converter = new ClauseConverter(new()
        {
            ["3かける4"] = [new("3", "3"), new("かける", "掛ける"), new("4", "4")],
            ["10わる2"] = [new("10わる", "10割る"), new("2", "2")],
            ["1たす1は2"] = [new("1", "1"), new("たす", "足す"), new("1は2", "1は2")],
            ["5ふんかける"] = [new("5ふん", "5分"), new("かける", "かける")],
            ["でんわをかける"] = [new("でんわを", "電話を"), new("かける", "かける")],
        });
        foreach (var (typed, expected) in new[]
        {
            ("3kakeru4 \n", "3×4"), ("10waru2 \n", "10÷2"), ("1tasu1ha2 \n", "1足す1は2"),
            ("5hunkakeru \n", "5分かける"), ("denwawokakeru \n", "電話をかける"),
        })
        {
            var k = new Keyboard(converter: converter);
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed);
        }

        // たす は記号を 2 番目の候補に足す (動詞のことも多いので最初にはしない)。選んでも覚えない
        var history = new ConversionHistory(null);
        var tasu = new Keyboard(converter: converter, history: history);
        tasu.Type("1tasu1ha2 ");
        tasu.Press(VirtualKeys.Right);
        Assert.Equal("+", tasu.Host.View!.Candidates[1], string.Join(",", tasu.Host.View!.Candidates));
        tasu.Press(VirtualKeys.Space);
        tasu.Press(VirtualKeys.Return);
        Assert.Equal("1+1は2", tasu.Host.Document);
        Assert.True(history.Get("たす") is null, "記号を選んでも学習しない");
    }

    /// <summary>覚えさせた文節を記録する変換エンジン (Mozc の代わり)。</summary>
    private sealed class LearningConverter : IKanjiConverter, ILearningConverter
    {
        private readonly FakeConverter _inner = new();
        public List<string> Learned { get; } = [];
        public string? Convert(string hiragana) => _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => _inner.ConvertClauses(hiragana, context);

        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
        {
            lock (Learned) Learned.Add(string.Join("|", clauses.Select(c => $"{c.Reading}={c.Text}")));
        }
    }

    /// <summary>しゃーなし を 西なし にする変換エンジン (Mozc の苦手な語の代わり)。</summary>
    private sealed class NishinashiConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana.Replace("しゃーなし", "西なし");
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => [new(hiragana, Convert(hiragana)!)];
    }

    [Test]
    public static void Shanashi_IsNotConvertedToPlaceName()
    {
        // #368: sha-nashi (しゃーなし) を、変換エンジンが 西なし にしていた。同梱の語句でかなのまま最初の候補にする
        var k = new Keyboard(converter: new NishinashiConverter(), userDictionary: new UserDictionary(null));
        k.Type("sha-nashi ");
        Assert.Equal("しゃーなし", k.Host.View!.Candidates[0], string.Join(" ", k.Host.View!.Candidates));
        k.Press(VirtualKeys.Return);
        Assert.Equal("しゃーなし", k.Host.Document);
    }

    /// <summary>いちがっき を 一楽器 にする変換エンジン (issue #1037)。</summary>
    private sealed class IchigakkiConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana.Replace("いちがっき", "一楽器").Replace("いち", "一").Replace("がっき", "楽器");
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => [new(hiragana, Convert(hiragana)!)];
    }

    [Test]
    public static void Issue1037_GakkiIsSplitFromNumber()
    {
        // #1037: いちがっき が 一楽器 になっていた。同梱の語句で 一|学期 の 2 つの文節にする (数字と 学期 を別に選び直せる)
        var k = new Keyboard(converter: new IchigakkiConverter(), userDictionary: new UserDictionary(null));
        k.Type("itigakki ");
        Assert.Equal("一|学期", string.Join("|", k.Host.View!.Clauses!));
        k.Press(VirtualKeys.Return);
        Assert.Equal("一学期", k.Host.Document);
    }

    [Test]
    public static void Issue1004_Kenritsu_IsNotUsedInsideALongerWord()
    {
        // #1072 のレビュー: 同梱の語句 ^けんりつ は、変換エンジンの文節の途中 (はっけんりつ = 発見率) では使わない
        var converter = new ClauseConverter(new()
        {
            ["はっけんりつ"] = [new("はっけんりつ", "発見率")],
            ["けんりつ"] = [new("けんりつ", "県率")],
            ["あいちけんりつ"] = [new("あいち", "愛知"), new("けんりつ", "県率")],
            ["あいち"] = [new("あいち", "愛知")],
        });
        var k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("hakkenritu ");
        Assert.Equal("発見率", string.Join("|", k.Host.View!.Clauses!));
        k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("aitikenritu ");
        Assert.Equal("愛知|県立", string.Join("|", k.Host.View!.Clauses!), "文節の頭から始まるなら使う (#1004)");
    }

    [Test]
    public static void Issue1019_Sitanode_IsNotUsedInsideALongerWord()
    {
        // #1072 のレビュー: 同梱の語句 ^したので は、変換エンジンの文節の途中 (はなしたので = 話したので) では使わない
        var converter = new ClauseConverter(new()
        {
            ["はなしたので"] = [new("はなしたので", "話したので")],
            ["したので"] = [new("したので", "下ので")],
        });
        var k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("hanasitanode ");
        Assert.Equal("話したので", string.Join("|", k.Host.View!.Clauses!));
        k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("sitanode ");
        Assert.Equal("したので", string.Join("|", k.Host.View!.Clauses!), "文節の頭から始まるなら使う (#1019)");
    }

    [Test]
    public static void Issue1014_Doshitano_IsNotUsedInsideALongerWord()
    {
        // #1072 のレビュー: 同梱の語句 ^どしたの は、変換エンジンの文節の途中 (なんどしたの = 何度したの) では使わない
        var converter = new ClauseConverter(new()
        {
            ["なんどしたの"] = [new("なんどしたの", "何度したの")],
            ["どしたの"] = [new("どしたの", "土下の")],
        });
        var k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("nanndositano ");
        Assert.Equal("何度したの", string.Join("|", k.Host.View!.Clauses!));
        k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("dositano ");
        Assert.Equal("どしたの", string.Join("|", k.Host.View!.Clauses!), "文節の頭から始まるなら使う (#1014)");
    }

    [Test]
    public static void Issue825_Nasasou_IsNotUsedInsideALongerWord()
    {
        // #1072 のレビュー: 同梱の語句 ^なさそう は、変換エンジンの文節の途中 (あぶなさそう = 危なさそう) では使わない
        var converter = new ClauseConverter(new()
        {
            ["あぶなさそう"] = [new("あぶなさそう", "危なさそう")],
            ["なさそう"] = [new("なさそう", "無點そう")],
        });
        var k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("abunasasou ");
        Assert.Equal("危なさそう", string.Join("|", k.Host.View!.Clauses!));
        k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("nasasou ");
        Assert.Equal("なさそう", string.Join("|", k.Host.View!.Clauses!), "文節の頭から始まるなら使う (#825)");
    }

    [Test]
    public static void Issue917_Ninatta_IsNotUsedInsideALongerWord()
    {
        // #1072 のレビュー: 同梱の語句 ^になった は、変換エンジンの文節の途中 (きになった = 気に|なった の に) では使わない
        var converter = new ClauseConverter(new()
        {
            ["きになった"] = [new("きに", "気に"), new("なった", "なった")],
            ["になった"] = [new("になった", "担った")],
        });
        var k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("kininatta ");
        Assert.Equal("気に|なった", string.Join("|", k.Host.View!.Clauses!));
        k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("ninatta ");
        Assert.Equal("になった", string.Join("|", k.Host.View!.Clauses!), "文節の頭から始まるなら使う (#917)");
    }

    [Test]
    public static void Issue1037_Gakki_IsNotUsedInsideALongerClause()
    {
        // #1072 のレビュー: に|がっき (二|学期) は、変換エンジンの文節の途中 (ほんとうに|がっきを の に) では使わない
        var converter = new ClauseConverter(new()
        {
            ["ほんとうにがっきをひく"] = [new("ほんとうに", "本当に"), new("がっきを", "楽器を"), new("ひく", "弾く")],
        });
        var k = new Keyboard(converter: converter, userDictionary: new UserDictionary(null));
        k.Type("honntounigakkiwohiku ");
        Assert.Equal("本当に|楽器を|弾く", string.Join("|", k.Host.View!.Clauses!));
        // 区切りで分けた文節は学習で選び直さない (いち で 市 を選んだことがあっても 一|学期 のまま)
        var history = new ConversionHistory(null);
        history.Remember("いち", "市");
        k = new Keyboard(converter: new IchigakkiConverter(), userDictionary: new UserDictionary(null), history: history);
        k.Type("itigakki ");
        Assert.Equal("一|学期", string.Join("|", k.Host.View!.Clauses!));
    }

    /// <summary>やほやほ を 谷保谷保 にする変換エンジン (issue #424)。</summary>
    private sealed class YahoConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana == "やほやほ" ? "谷保谷保" : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            hiragana == "やほやほ" ? [new("やほ", "谷保"), new("やほ", "谷保")] : null;
    }

    [Test]
    public static void Issue424_YahoyahoStaysHiragana()
    {
        // #424: yahoyaho を Enter すると 谷保谷保 になっていた。繰り返しはかなのまま。
        var k = new Keyboard(live: true, converter: new YahoConverter(), userDictionary: new UserDictionary(null));
        k.Type("yahoyaho\n");
        Assert.Equal("やほやほ", k.Host.Document);
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
            return k.Host.View!.Candidates;
        }
        var k = new Keyboard(translations: dictionary, translationHistory: history);
        k.Type("kawa ");
        var view = k.Host.View!;
        var index = view.Candidates.ToList().IndexOf("river");
        Assert.True(index > 0 && view.Candidates[0] == "川", "英訳は日本語の候補の後ろ: " + string.Join(",", view.Candidates));
        Assert.Equal("英訳", view.Notes?[index] ?? "(なし)");
        for (var i = 0; i < index; i++) k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal("river", k.Host.Output.Single());

        // 学習は弱め: 1 回選んでも 1 番目にはしない。2 回選んだら 2 番目。
        Assert.Equal("川", Convert()[0], "1 回では 1 番目にしない");
        history.Remember("かわ", "river");
        Assert.Equal("川,river", string.Join(",", Convert().Take(2)), "2 回選んだら 2 番目");
    }

    [Test]
    public static void Commit_TeachesTheConverter()
    {
        // Mozc の学習: 確定した文節 (区切りと文字列) を変換エンジンに覚えさせる。
        var converter = new LearningConverter();
        var k = new Keyboard(converter: converter);
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Return);
        // 覚えさせるのは裏で行うので、少し待つ。
        for (var i = 0; i < 100 && converter.Learned.Count == 0; i++) Thread.Sleep(10);
        Assert.Equal("たんいを=単位を|とる=取る", converter.Learned.SingleOrDefault() ?? "(なし)");
    }

    /// <summary>わたしのことがすき・しのがきた を区切る変換エンジン (ユーザー辞書の語が文節の途中に入るテスト用)。</summary>
    private sealed class WatashiConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => hiragana switch
        {
            "わたしのことがすき" => [new("わたしの", "私の"), new("ことが", "事が"), new("すき", "好き")],
            "わた" => [new("わた", "綿")],
            "ことがすき" => [new("ことが", "事が"), new("すき", "好き")],
            "しのがきた" => [new("しの", "篠"), new("が", "が"), new("きた", "来た")],
            "がきた" => [new("が", "が"), new("きた", "来た")],
            _ => null,
        };
    }

    [Test]
    public static void UserDictionary_WordInsideAnEngineClause_IsNotSplitOut()
    {
        // #374: しの → 紫乃 を登録すると、わたしのことがすき が ワタ紫乃ことが好き になっていた。
        // 変換エンジンの文節 (わたしの) の途中から始まる語では区切らない。文節の頭から始まるなら今までどおり使う
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("しの", "紫乃");
        var k = new Keyboard(converter: new WatashiConverter(), userDictionary: dictionary);
        k.Type("watashinokotogasuki ");
        Assert.Equal("私の事が好き", string.Concat(k.Host.View!.Clauses!));

        k = new Keyboard(converter: new WatashiConverter(), userDictionary: dictionary);
        k.Type("shinogakita ");
        Assert.Equal("紫乃", k.Host.View!.Clauses![0], "文節の頭から始まる登録した語は使う");

        k = new Keyboard(live: true, converter: new WatashiConverter(), userDictionary: dictionary);
        k.Type("watashinokotogasuki");
        Assert.True(!k.Showing!.Contains("紫乃"), "ライブ変換でも割り込まない: " + k.Showing);
    }

    private sealed class AgeashiConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => hiragana switch
        {
            "あげあしとらないで" => [new("あげあし", "揚げ足"), new("とらないで", "取らないで")],
            "ないで" => [new("ないで", "内で")],
            "かいしょうないひと" => [new("かいしょう", "解消"), new("ないひと", "ない人")],
            "ひと" => [new("ひと", "人")],
            "これほんあかだね" => [new("これ", "コレ"), new("ほんあかだね", "本垢だね")],
            "これ" => [new("これ", "これ")],
            "きょうはやないのだね" => [new("きょうはや", "教派や"), new("ないのだね", "ないのだね")],
            "きょうは" => [new("きょうは", "今日は")],
            "だね" => [new("だね", "だね")],
            _ => null,
        };
    }

    [Test]
    public static void Phrase_EngineAlreadyConvertsIt_IsNotSplitOut()
    {
        // 同梱の語句 あげあしとら → 揚げ足取ら で区切ると、残りの ないで を単独で変換して 内で になっていた。
        // 変換エンジンが文の中で同じ文字 (揚げ足取らないで) にできているなら、語句では区切らない
        var k = new Keyboard(converter: new AgeashiConverter(), userDictionary: new UserDictionary(null));
        k.Type("ageashitoranaide ");
        Assert.Equal("揚げ足取らないで", string.Concat(k.Host.View!.Clauses!));

        // 変換エンジンが間違える語句 (解消ない人) は、今までどおり語句で区切る
        k = new Keyboard(converter: new AgeashiConverter(), userDictionary: new UserDictionary(null));
        k.Type("kaishounaihito ");
        Assert.Equal("甲斐性ない人", string.Concat(k.Host.View!.Clauses!));

        // 語句より前は今までどおり別に変換する (文全体の変換エンジンの結果にすると コレ本垢だね になる)
        k = new Keyboard(converter: new AgeashiConverter(), userDictionary: new UserDictionary(null));
        k.Type("korehonnakadane ");
        Assert.Equal("これ本垢だね", string.Concat(k.Host.View!.Clauses!));

        // 始まりが変換エンジンの文節の途中にある語句は、今までどおり語句で区切る (教派や|ないのだね にしない)
        k = new Keyboard(converter: new AgeashiConverter(), userDictionary: new UserDictionary(null));
        k.Type("kyouhayanainodane ");
        Assert.Equal("今日はやないのだね", string.Concat(k.Host.View!.Clauses!));
    }

    [Test]
    public static void UserDictionary_WinsOverEngine()
    {
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Add("きごうとう", "記号等") is null, "登録できる");
        Assert.True(dictionary.Add("", "記") is not null, "空の読みは登録できない");

        var k = new Keyboard(userDictionary: dictionary);
        k.Host.PrecedingText = "文章を「記号等」含め、"; // 変換エンジンが き|ごうとう と区切ってしまう文脈
        k.Type("kigoutoufukume ");
        Assert.Equal("記号等", k.Host.View!.Clauses![0], "登録した読みの部分は、変換エンジンの区切りに関係なく登録した単語");
        Assert.Equal(2, k.Host.View.Clauses.Count, "残り (ふくめ) は別の文節");

        k = new Keyboard(live: true, userDictionary: dictionary);
        k.Type("kigoutou");
        Assert.Equal("記号等", k.Showing, "ライブ変換でも使う");
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
    public static void Context_DoesNotChangeClauseBoundaries()
    {
        // 報告: 記号等 がどうしても 機強盗 になる (前に「記号等」があると変換エンジンが き|ごうとう と区切る)。
        var k = new Keyboard();
        k.Host.PrecedingText = "文章を「記号等」含め、";
        k.Type("kigoutou ");
        Assert.Equal("記号|等", string.Join("|", k.Host.View!.Clauses!), "文脈で区切りが変わるなら文脈なしの結果を使う");
    }

    [Test]
    public static void ParticleStart_GetsContext()
    {
        // 報告: ○○になってしまう → 「になってしまう」が「担ってしまう」になる。
        var k = new Keyboard();
        k.Type("ninatteshimau ");
        Assert.Equal("これ", k.Converter.LastContext, "前の文脈が無いときは仮の文脈で「に」を助詞として読ませる");

        k = new Keyboard();
        k.Host.PrecedingText = "〇〇";
        k.Type("ninatteshimau ");
        Assert.Equal("〇〇", k.Converter.LastContext, "○ などの記号も日本語の文脈として渡す");

        k = new Keyboard();
        k.Type("hashiru ");
        Assert.True(k.Converter.LastContext is null, "に 以外で始まる読みには仮の文脈を付けない (はしる → は知る を防ぐ)");
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
    public static void Clauses_ShiftArrowResizes()
    {
        var k = new Keyboard();
        k.Type("tanniwotoru ");
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

    [Test]
    public static void Clauses_ShiftArrowResizesAcrossEnglish()
    {
        // 報告 (#144): 英語の文節が混ざると Shift+← → で区切りを動かせない (みー|thin|ぐ、disco|で)。
        // (mi-thingu は #153 の対応で最初から みーてぃんぐ と読むようになったので、英語の文節が残る例で確かめる)
        var k = new Keyboard();
        k.Type("konoteamdeyaru ");
        Assert.Equal("この|team|でやる", string.Join("|", k.Host.View!.Clauses!));
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        Assert.Equal("このて|あm|でやる", string.Join("|", k.Host.View.Clauses!), "後ろの英語の文節は、打ったローマ字のかなで読み直して区切りを動かす");
        k.Press(VirtualKeys.Right);
        Assert.Equal("このてあ|m|でやる", string.Join("|", k.Host.View.Clauses!));
        k.Key(VirtualKeys.LShift, up: true);

        k = new Keyboard();
        k.Type("konoteamdeyaru ");
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        Assert.Equal("この|てあ|mでやる", string.Join("|", k.Host.View!.Clauses!), "選んだ英語の文節も読み直して縮め、外れたかなは次の文節へ");
        k.Press(VirtualKeys.Left);
        Assert.Equal("この|て|あmでやる", string.Join("|", k.Host.View.Clauses!));
        k.Key(VirtualKeys.LShift, up: true);

        k = new Keyboard();
        k.Type("tanniGithubde ");
        Assert.Equal("単位|Github|で", string.Join("|", k.Host.View!.Clauses!));
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        Assert.Equal("たん|い|Github|で", string.Join("|", k.Host.View.Clauses!), "縮めて空いた分は、後ろの英語の文節を変えずに新しい文節にする");
        k.Press(VirtualKeys.Right);
        Assert.Equal("単位|Github|で", string.Join("|", k.Host.View.Clauses!), "伸ばして戻す");
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void Clauses_ShiftArrowKeepsEnglishWhenItCannotMove()
    {
        // 区切りを動かせないときは、選んだ英語の文節を日本語に読み直さない (日本語の文節と同じく何も変えない)
        var k = new Keyboard();
        k.Type("konoteam");
        k.Press(VirtualKeys.Convert);
        Assert.Equal("この|team", string.Join("|", k.Host.View!.Clauses!));
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        Assert.Equal("この|team", string.Join("|", k.Host.View.Clauses!), "最後の文節は伸ばせない");
        k.Key(VirtualKeys.LShift, up: true);

        k = new Keyboard();
        k.Type("konoteamXyz");
        k.Press(VirtualKeys.Convert);
        Assert.Equal("この|team|Xyz", string.Join("|", k.Host.View!.Clauses!));
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        Assert.Equal("この|team|Xyz", string.Join("|", k.Host.View.Clauses!), "次の文節がかなに読めないので伸ばせない");
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
        k.Type("kyou");
        Assert.Equal("きょう", k.Showing, "短いうちはかなのまま (的外れな漢字を出さない)");
        k.Type("ha");
        Assert.Equal("今日は", k.Showing, "4 文字以上になったら Space を押さなくても漢字で表示");
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
        foreach (var word in new[] { "github", "hello", "typescript", "npm", "localhost", "zoom", "git", "test", "iphone", "stackoverflow" })
        {
            var k = new Keyboard();
            k.Type(word);
            Assert.Equal(word, k.Showing, $"「{word}」");
        }
    }

    [Test]
    public static void JapaneseWords_AreShownAsKana()
    {
        var expected = new Dictionary<string, string>
        {
            ["watashi"] = "わたし", ["arigatou"] = "ありがとう", ["kana"] = "かな", ["sushi"] = "すし", ["tesuto"] = "てすと",
            ["make"] = "まけ", ["ra-men"] = "らーめn", ["nihon"] = "にほn", ["konnichiwq"] = "こんいちwq", ["tanni"] = "たんい",
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
        Assert.True(k.Showing is null && !k.Gate.IsCaptured, "空になったら閉じる");
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
        Assert.Equal("text:かな|down:09|passed-up:09", string.Join("|", k.Host.Events), "確定後の キーアップ は関所を通らず直接届く");
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
    private sealed class KyouhaConverter : IKanjiConverter
    {
        private readonly FakeConverter _inner = new();
        public string? Convert(string hiragana) => _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => hiragana switch
        {
            "きょう" => [new("きょう", "今日")],
            "きょうは" => [new("きょう", "今日"), new("は", "は")],
            _ => _inner.ConvertClauses(hiragana, context),
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
        Assert.Equal("text:hello|down:09|passed-up:09", string.Join("|", k.Host.Events));
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
        // 入力中の Ctrl は次のキーを見るまで送らない (Ctrl+U/I/O/P はかな・英字の切り替え)。ほかのキーなら確定 → Ctrl → そのキーの順に送る
        Assert.Equal("text:かな|down:A2|down:43|passed-up:43|passed-up:A2", string.Join("|", k.Host.Events), "確定 → Ctrl → C を送る → 以降は直接アプリへ");
        Assert.True(!k.Gate.IsCaptured, "ショートカットの後は横取りをやめる");
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
        Assert.Equal("text:hello|down:A0|down:25|passed-up:25|passed-up:A0", string.Join("|", k.Host.Events), "範囲選択のための Shift はアプリに届く");
    }

    [Test]
    public static void MouseClick_CommitsBeforeClicking()
    {
        var k = new Keyboard();
        k.Type("kana");
        Assert.True(k.Gate.OnMouseButton(new MouseButtonEvent(0x201, 10, 20, 0)), "変換中のクリックは一旦止める");
        k.Controller.Pump();
        Assert.Equal("text:かな|mouse:201", string.Join("|", k.Host.Events), "確定してからクリックを再生");
        Assert.True(!k.Gate.OnMouseButton(new MouseButtonEvent(0x202, 10, 20, 0)), "確定後のクリックは素通し");
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
    public static void KeyUpOfKeyPressedBeforeCapture_IsReplayed()
    {
        // Shift を押したまま大文字で打ち始めた場合、Shift の押下は関所の前にアプリへ届いている。
        var k = new Keyboard();
        k.Key('T');
        k.Key(VirtualKeys.LShift, up: true);
        Assert.True(k.Host.Events.Contains("up:A0"), "離したことを伝えないと Shift が押しっぱなしになる");
    }

    [Test]
    public static void EnglishVerb_PlusSuru()
    {
        // Twitter の報告: 「commitしてpushして」が こっみつぃてぷっして になる
        // (英単語の最後の t + s が つ になる / 末尾の pushsite は site も英単語なので全部英字になる)
        foreach (var (typed, expected) in new[]
        {
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
            ("mudawodecluttersuru", "むだをdeclutterする"),
        })
        {
            // 実際と同じく、辞書にない英単語 (website) はスペルチェッカーで見る
            Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
            try
            {
                var k = new Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
            finally
            {
                Detector.SpellChecker = null;
            }
        }
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
            ("", "dlsite", "dlsite"),
            ("", "dlsitede", "dlsiteで"),
            ("", "dlsitenokaiin", "dlsiteのかいいん"),
        })
        {
            var k = new Keyboard();
            if (before.Length > 0) k.Host.PrecedingText = before;
            k.Type(typed + "\n");
            Assert.Equal(expected, before + k.Host.Document, typed);
        }
    }

    [Test]
    public static void EnglishVerb_PlusSuru_PendingConsonantIsShownOnce()
    {
        // 英単語 + する の活用の打ちかけの子音が 2 回出ていた (googleしまss)。Space で変換する読みも同じ
        foreach (var (typed, expected, converted) in new[]
        {
            ("googleshimasen", "googleしませn", "googleしません"),
        })
        {
            Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
            try
            {
                var k = new Keyboard();
                k.Type(typed);
                Assert.Equal(expected, k.Showing, typed);
                k.Type(" ");
                Assert.Equal(converted, k.Showing, typed + " (Space)");
            }
            finally
            {
                Detector.SpellChecker = null;
            }
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
            Assert.Equal(expected, CompositionController.AddSpacesAroundEnglish(text, before, after), text);
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
            Assert.Equal(expected, CompositionController.AddSpacesAroundEnglish(text, before, after, english, numbers: true), text);
        }
    }

    [Test]
    public static void SpaceAroundEnglish_WhenTypingInTheCompositionBox()
    {
        // #248: 英数を使わず、変換ボックスの中で英語と日本語を判定して打っても、設定「英単語の前後に半角スペース」が効く
        Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
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
        finally
        {
            Detector.SpellChecker = null;
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
            Assert.True(!k.Gate.IsCaptured && k.Showing is null, $"{word}: 変換ボックスを開かない");
            Assert.Equal(0, k.Host.Output.Count, word);
            Assert.True(k.Sigil.IsActive, $"{word}: 名前の途中");
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
        Assert.True(!k.Gate.IsCaptured && k.Sigil.IsActive, "/help の help はアプリへ");
        Assert.True(k.Host.Events.Contains("down:48") && k.Host.Events.Contains("passed:50"), "help はアプリへ");
        k.Type(" kyou");
        Assert.Equal("きょう", k.Showing);
        // 改行した後の行の先頭の / も ・ (1 回目の Enter は確定だけ)。
        k = new Keyboard();
        k.Type("kyouha\n\n/");
        Assert.Equal("・", k.Showing);
        // コードエディターのコードの中は今までどおり、そのままアプリへ渡す。
        k = new Keyboard { Code = true };
        k.Type("/help");
        Assert.True(!k.Gate.IsCaptured && k.Sigil.IsActive, "コードの /help はアプリへ");
    }

    [Test]
    public static void SigilWord_SpaceAfterNameReturnsToNormal()
    {
        var k = new Keyboard { Code = true };
        k.Type("/review kyouha");
        Assert.Equal("きょうは", k.Showing);
        k.Press(VirtualKeys.Return);
        Assert.Equal("きょうは", k.Host.Output.Single());
        Assert.True(k.Host.Events.Contains("passed:BF") && k.Host.Events.Contains("passed:20"), "/ と空白はアプリへ");
    }

    [Test]
    public static void SigilWord_AfterCommittedEnglishWordAndSpace()
    {
        // google を Space で確定した (google + 空白) 後の /help も名前として通す。
        var k = new Keyboard { Code = true };
        k.Type("google /help");
        Assert.Equal("google ", string.Concat(k.Host.Output));
        Assert.True(!k.Gate.IsCaptured, "/help は変換ボックスに入れない");
        // Enter (送信・改行) の後の行の先頭も。
        k.Press(VirtualKeys.Return);
        k.Type("$deploy");
        Assert.True(!k.Gate.IsCaptured && k.Sigil.IsActive, "Enter の後の $deploy も通す");
    }

    [Test]
    public static void SigilWord_NotAfterLetters()
    {
        // メールアドレスの @ (前が英字) は対象外。変換ボックスの中で打った @ もそのまま変換ボックスで扱う。
        var k = new Keyboard();
        k.Type("taro@example.com");
        Assert.True(k.Gate.IsCaptured && k.Showing!.Contains('@'), "taro@ の @ は変換ボックスの中");
        Assert.True(!k.Sigil.IsActive, "名前として扱わない");
        // 日本語を確定した直後 (空白なし) の / も対象外。
        k = new Keyboard();
        k.Type("kyouha\n/");
        Assert.True(k.Gate.IsCaptured, "今日は/ の / は変換ボックスに入れる");
        // ホストが前の文字を教えてくれれば、それで決める (打ち始めでも前が英字なら対象外)。
        k = new Keyboard();
        k.Host.PrecedingText = "taro";
        k.Type("@");
        Assert.True(k.Gate.IsCaptured, "前が taro なら @ は変換ボックスに入れる");
        k = new Keyboard { Code = true };
        k.Host.PrecedingText = "> ";
        k.Press(VirtualKeys.Left);
        k.Type("/");
        Assert.True(!k.Gate.IsCaptured, "前の文字が空白と分かれば、キャレットを動かした後でも / を通す");
    }

    [Test]
    public static void SigilWord_UnknownPositionAndBackspace()
    {
        // 矢印でキャレットを動かした後は前の文字が分からないので、普通に扱う。
        var k = new Keyboard();
        k.Press(VirtualKeys.Left);
        k.Type("/");
        Assert.True(k.Gate.IsCaptured, "前の文字が分からなければ / は変換ボックスに入れる");
        // 記号まで消したら、記号の前 (先頭) に戻る。
        k = new Keyboard { Code = true };
        k.Type("/re\b\b\b");
        Assert.True(!k.Sigil.IsActive, "/ を消したら名前ではない");
        k.Type("kyou");
        Assert.Equal("きょう", k.Showing);
        k.Press(VirtualKeys.Escape);
        k.Type("@file");
        Assert.True(!k.Gate.IsCaptured, "消した後の先頭の @ も通す");
    }

    [Test]
    public static void SigilWord_DirectModeDoesNotDetectRomaji()
    {
        // 英数状態でも、/ の後ろの名前はローマ字として判定しない (/kensaku を日本語にしない)。
        var k = new Keyboard(direct: true);
        k.Type("/kensaku");
        Assert.True(k.Direct, "英数状態のまま");
        Assert.True(!k.Gate.IsCaptured && k.Host.Output.Count == 0, "打ったキーはそのままアプリへ");
    }

    [Test]
    public static void SigilWord_FocusMoved()
    {
        // 補完の候補の一覧にフォーカスが移ったように見えても、名前の途中なら続ける。
        var k = new Keyboard { Code = true };
        k.Type("/re");
        k.Sigil.FocusMoved();
        k.Type("view");
        Assert.True(!k.Gate.IsCaptured && k.Sigil.IsActive, "/review の続きもアプリへ");
        // 名前の途中でなければ、次の文字は先頭とみなす (日本語の後でも)。
        k = new Keyboard();
        k.Type("kyouha\n");
        k.Sigil.FocusMoved();
        k.Type("@file");
        Assert.True(!k.Gate.IsCaptured, "別の入力欄に移った後の @file はアプリへ");
    }

    [Test]
    public static void SigilWord_SettingOff()
    {
        var k = new Keyboard { SigilWords = false };
        k.Type("/review");
        Assert.True(k.Gate.IsCaptured, "OFF なら今までどおり変換ボックスに入れる");
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
    public static void Issue455_KatakanaExpectIgnoresAsciiCase()
    {
        var tsv = Path.Combine(Path.GetTempPath(), $"meltype-expect-455-{Guid.NewGuid():N}.tsv");
        var json = Path.Combine(Path.GetTempPath(), $"meltype-expect-455-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tsv, "oemnopasokonn\tOEMのパソコン\noemnopasokonn\tCPUのパソコン\n");
            Checks.Expect(tsv, json);
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
            var rows = doc.RootElement.EnumerateArray().ToArray();
            Assert.Equal(2, rows.Length);
            Assert.True(rows[0].GetProperty("ok").GetBoolean(), "OEMのパソコン は oemのぱそこん と一致する");
            Assert.Equal("oemのぱそこん", rows[0].GetProperty("actual").GetString());
            Assert.Equal(false, rows[1].GetProperty("ok").GetBoolean());
        }
        finally
        {
            if (File.Exists(tsv)) File.Delete(tsv);
            if (File.Exists(json)) File.Delete(json);
        }
    }

    [Test]
    public static void Issue407_TsuideNiFetch()
    {
        // Issue #407: ついでに の途中の ide を英単語にしない。見出しが先頭の ideにfetch は英字のまま。
        foreach (var (typed, expected) in new[]
        {
            ("tuidenifetch upstream", "ついでにfetch upstream"),
            ("idenifetch", "ideにfetch"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Issue154_AbcToDef()
    {
        // Issue #154: 英字のあいだの「と」は助詞。全体を 1 つの英単語にしない。
        var k = new Keyboard();
        k.Type("abctodef\n");
        Assert.Equal("abcとdef", k.Host.Document);
    }

    [Test]
    public static void Issue338_ZArrowKeys()
    {
        // Issue #338: zh / zj / zk / zl は Microsoft IME と同じ矢印。z + 記号は今までどおり。zz の次の l (puzzle) は矢印にしない。
        foreach (var (typed, expected) in new[]
        {
            ("zh", "←"), ("zj", "↓"), ("zk", "↑"), ("zl", "→"),
            ("z/", "・"), ("zu", "ず"), ("puzzle", "ぷっぞえ"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
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
        foreach (var (typed, expected) in new[]
        {
            ("suityoku", "すいちょく"),
            ("githubya", "githubや"),
            ("edityo", "editよ"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
        var text = new CompositionText(Detector);
        foreach (var c in "suityoku") text.Append(c);
        Assert.Equal("JA:suityoku", string.Join(" | ", text.Segments().Select(s => (s.IsEnglish ? "EN:" : "JA:") + s.Raw)));
    }
}
