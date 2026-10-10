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
        public bool SpaceAroundEnglish { get; set; }
        public bool Learning { get; set; } = true;

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

        public long Now => _now;

        public FakeConverter Converter { get; } = new();

        public Keyboard(bool live = false, bool direct = false, ConversionHistory? history = null, IKanjiConverter? converter = null,
            Func<string, IReadOnlyList<string>>? moreCandidates = null, UserDictionary? userDictionary = null, LanguageMemory? languages = null,
            TranslationDictionary? translations = null, TranslationHistory? translationHistory = null, bool slashAsMiddleDot = false, Predictor? predictor = null,
            Func<DateTime>? now = null, bool showTypedKeys = false, bool tabConversion = false)
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
                SpaceAroundEnglish = () => SpaceAroundEnglish,
                Predictor = predictor,
                Predictions = () => predictor is not null,
                Punctuation = () => Punctuation,
                SlashAsMiddleDot = () => slashAsMiddleDot,
                Now = now ?? (() => DateTime.Now),
                ShowTypedKeys = () => showTypedKeys,
                TabConversion = () => tabConversion,
                Learning = () => Learning,
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
            if (SigilWords && (Direct || !Kana) && Host.CharFromKey(k, _shiftHeld) is { } typed && Sigil.PassesThrough(typed, Host.PrecedingText)) return false;
            var letter = VirtualKeys.IsLetter(k.Vk);
            if (Direct) return letter && !_directEnglishWord && Level != Meltype.Config.DetectionLevel.Manual;
            if (Kana && Detection.KanaDetector.IsKanaKey(k.Vk)) return true;
            if (k.Vk == VirtualKeys.Space && _shiftHeld) return true;
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
        Assert.True(k.Host.Events.Any(e => e.StartsWith("replace:今日:")), "選択範囲を置き換える");
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
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:")), "取消時は置換しない");
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
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:")), "別の選択範囲を置換しない");
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
    public static void Prediction_OffWithoutPredictor()
    {
        var k = new Keyboard();
        k.Type("decis");
        Assert.True(k.Host.View!.Predictions is null, "予測の元が無ければ出さない");
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
            Assert.True(candidates.Any(c => c.StartsWith("GitHub")), keys + ": " + string.Join(" ", candidates));
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
        foreach (var expected in new[] { "2026年10月8日", "2026年10月8日(木)", "10月8日(木)", "2026/10/08", "2026-10-08", "木曜日" })
            Assert.True(today.Host.View!.Candidates.Contains(expected), string.Join(" ", today.Host.View!.Candidates));
        var other = new Keyboard(now: () => new DateTime(2026, 10, 8, 17, 22, 0));
        other.Type("imada ");
        Assert.True(!other.Host.View!.Candidates.Contains("17:22"), "いま だけの文節のとき");
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
        // 英字に数字・記号が混ざっていても、変わるのは英字だけ (api2.0 → API2.0 → Api2.0)
        var k = new Keyboard();
        k.Type("api2.0");
        var shown = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            k.Press(VirtualKeys.F10);
            shown.Add(k.Showing);
        }
        Assert.Equal("api2.0,API2.0,Api2.0", string.Join(",", shown));
    }

    [Test]
    public static void F9F10_SwitchingResetsCase()
    {
        // F10 で大文字にした後に F9 (全角) にすると、打ったまま (小文字) から。F9 の大文字の後に F10 でも同じ
        var k = new Keyboard();
        k.Type("abc");
        k.Press(VirtualKeys.F10);
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
            Assert.True(!k.Host.Events.Any(e => e.EndsWith($":{control:X2}")), "Ctrl を送らない: " + string.Join(" ", k.Host.Events));
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
        Assert.True(events.StartsWith("text:かな|down:A2|down:A0|down:5A"), events);
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
        Assert.True(events.StartsWith("text:かな|down:A2|mouse:201"), events);
        k.Key(VirtualKeys.LControl, up: true);
        Assert.True(string.Join("|", k.Host.Events).Contains("A2", StringComparison.Ordinal) && k.Host.Events.Count(e => e.Contains(":A2")) == 2, "Ctrl の上げ下げがそろう: " + string.Join("|", k.Host.Events));
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
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:")), "次の入力に再変換の状態を持ち越さない");
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
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("replace:")), "フォーカス喪失時は置換しない");
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
            ["repo"] = "れぽ", // ローマ字としても読める語は日本語のまま (F10 で英字)
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
            ["who"] = "うぉ", ["ulo"] = "うぉ", ["uxo"] = "うぉ",
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
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:")), "- の前の ro だけ送っていた (- は送らない)");
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
            ("e-mail", "e-mail"), ("co-op", "co-op"), ("e-maildeokuru", "e-mailでおくる"), ("x-ray", "x-ray"), ("r-18", "r-18"), ("sub-6", "sub-6"), ("GPT-6.7", "GPT-6.7"),
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
        Assert.True(meanings.Lookup("橋", "きょう")?.StartsWith("中脳") == true, "きょう と読んだ 橋 は脳橋の意味");
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
        var downs = events.Count(e => e.StartsWith("down:"));
        Assert.True(downs > 0, "判定を待たずに英字を送っている");
        var bs = events.FindIndex(e => e.StartsWith("bs:"));
        Assert.Equal($"bs:{downs}", bs < 0 ? "(なし)" : events[bs], "送った英字を消す");
        Assert.True(events.FindLastIndex(e => e.StartsWith("down:")) < bs, "送った後に消す");
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
            Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), $"{word}: 英語なら消さない");
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
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:")), "判定中でも打った英字はすぐ送る");
        Assert.True(k.Gate.IsCaptured, "判定中は打鍵を受け取る");
        k.Controller.Tick(k.Now + 1000);
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:")), "しばらく打たなければ英語とみなす (送り直さない)");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), "消さない");
        Assert.True(!k.Gate.IsCaptured, "判定をやめたら横取りもやめる");
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
            ("I love ", "が好き", "すし"),     // 食い違うときは日本語
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
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), "消さない");
    }

    [Test]
    public static void AutoCorrect_KeepsExplicitlyChosenLanguage()
    {
        // #124: F10 で英字にして確定した語 (api) は、後ろに日本語が続いても かな に確定し直さない
        var k = new Keyboard(languages: new LanguageMemory(null));
        k.Type("api");
        k.Press(VirtualKeys.F10);
        k.Type("\n");
        k.Type("tte\n");
        Assert.Equal("apiって", k.Host.Document, string.Join("|", k.Host.Events));
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
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), "キャレットが動いたかもしれないので消さない");
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
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), $"前の語が今のキャレットの位置に無いので消さない: {string.Join("|", k.Host.Events)}");
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
        Assert.True(k.Host.Events.Any(e => e.StartsWith("bs:")), $"読めていない環境では直す: {string.Join("|", k.Host.Events)}");
    }

    [Test]
    public static void AutoCorrect_NotWhenUserChoseCandidate()
    {
        var k = new Keyboard();
        k.Type("i  "); // 2 回目の Space で候補を選び直した
        k.Type("want ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), "自分で選んだ変換は直さない");
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
            // 長い名前の途中の助詞・ドメインの頭では分けない
            ["Meltype.gitignore"] = "Meltype.gitignore", ["Meltype.gcno"] = "Meltype.gcno", ["Meltype.automount"] = "Meltype.automount",
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
    public static void BuiltInPhrases_AreSplitOut()
    {
        // 報告: 白馬の王子様 → ハクバノ王子サマ、ばらまいてた愛 → ばらまいて他愛
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Split("はくばのおうじさま")?.Any(p => p.Word == "白馬の王子様") == true, "白馬の王子様");
        Assert.True(dictionary.Split("ばらまいてたあい")?.First().Word == "ばらまいてた", "ばらまいてた|あい");
        // 報告 (#196): しょたこん → ショタこん
        Assert.True(dictionary.Split("しょたこん")?.Any(p => p.Word == "ショタコン") == true, "しょたこん → ショタコン");
        Assert.Equal(0, dictionary.Count, "同梱の語句はユーザー辞書の一覧に出さない");
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
        // 日本語のすぐ後ろの we は英単語にしない (こ + we = こうぇ → こゑ)
        foreach (var (typed, old) in new[] { ("kowi ", "こゐ"), ("kowe ", "こゑ") })
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

    [Test]
    public static void UserDictionary_WinsOverEngine()
    {
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Add("きごうとう", "記号等") is null, "登録できる");
        Assert.True(dictionary.Add("き", "記") is not null, "1 文字の読みは登録できない");

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
            ("website", "website"),
            ("websitewomiru", "websiteをみる"),
            ("tetsudou", "てつどう"),
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
    public static void SigilWord_AtStartPassesNameToTheApp()
    {
        // #193: AI エージェントの /command・$skill・@ファイル名 は、変換ボックスに溜めずに打つたびにアプリへ渡す (補完を選べるように)。
        foreach (var word in new[] { "/review", "$skill-name", "@src/app.ts", "/kensaku" })
        {
            var k = new Keyboard();
            k.Type(word);
            Assert.True(!k.Gate.IsCaptured && k.Showing is null, $"{word}: 変換ボックスを開かない");
            Assert.Equal(0, k.Host.Output.Count, word);
            Assert.True(k.Sigil.IsActive, $"{word}: 名前の途中");
        }
    }

    [Test]
    public static void SigilWord_SpaceAfterNameReturnsToNormal()
    {
        var k = new Keyboard();
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
        var k = new Keyboard();
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
        k = new Keyboard();
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
        k = new Keyboard();
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
        var k = new Keyboard();
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
}
