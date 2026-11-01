// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Eisenferik

using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// 組み込み辞書の保持メモリ・検索結果・入力処理を変更前後で比較する。両方で実行できるよう、辞書の持ち方には依存しない。
///   --dictionary-memory           … 辞書ごとに、読み込む前と後のマネージドヒープの差と、読み込みにかかった時間
///   --dictionary-dump 出力ファイル … 辞書の全部の見出し・読み・英単語に対する引き方の答え (前と後で書き出して diff で比べる)
///   --dictionary-bench [回数]     … 1 打鍵あたりの時間と割り当て (日本語の文の中に英単語を入れた文と、ローマ字の打ち間違い入りの文を打つ)
///   --dictionary-lookup-bench [回数] … 意味・英訳を直接引いたときの 1 検索あたりの時間と割り当て (--dictionary-bench は変換器が読みをそのまま返すので、漢字の候補で意味・英訳を引くことがない)
/// 時間を出す 3 つ (--dictionary-dump 以外) は -c Release で実行する。
/// </summary>
internal static class DictionaryMemory
{
    private static readonly string[] CandidateFiles = ["candidates.txt", "loanwords.txt", "brands.txt"];
    private static readonly string[] EmojiFiles = ["emoji.txt", "emoji-cldr.txt"];
    private static readonly string[] LookupSuffixes = ["", "を", "字"];
    /// <summary>ヒープを詰めてから、使っている大きさ (断片化した分を除く) を返す。</summary>
    private static long Heap()
    {
        for (var i = 0; i < 2; i++)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        return info.HeapSizeBytes - info.FragmentedBytes;
    }

    // 読み込み中の一時オブジェクトの参照が、計測側のスタックに残るのを避ける
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Load(Func<object> load) => load();

    // 辞書の内部表現に依存せず、変更前後で同じかな入力の判定を呼ぶ
    private static bool ContainsJapaneseReading(string kana, string after) =>
        (bool)typeof(CompositionDetector).GetMethod("ContainsJapaneseReading", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [kana, after])!;

    public static void Measure()
    {
        RequireRelease();
        Console.OutputEncoding = new UTF8Encoding(false);
        // 型の初期化と、辞書とは別の小さな静的なもの (ローマ字の表など) を先に済ませておく
        new RomajiDetector(null).AnalyzeFragment("konnichiwa");
        _ = DictionarySource.ReadEmbedded("brands.txt");

        var keep = new List<object>();
        var rows = new List<(string Name, long Bytes, double Milliseconds)>();
        void Row(string name, Func<object> load)
        {
            var before = Heap();
            var watch = Stopwatch.StartNew();
            var loaded = Load(load);
            watch.Stop();
            rows.Add((name, Heap() - before, watch.Elapsed.TotalMilliseconds));
            keep.Add(loaded);
        }
        CompositionDetector? detector = null;
        Row("判定の本体 (CompositionDetector.CreateDefault)", () => detector = CompositionDetector.CreateDefault());
        // 共有される読みの集合は、先に初期化するかな入力の行で数える
        Row("readings (かな入力の判定)", () => ContainsJapaneseReading("あいう", ""));
        Row("readings (RomajiTypoCorrector)", () => RomajiTypoCorrector.Load(detector!.Romaji));
        Row("英訳の候補 (TranslationDictionary)", TranslationDictionary.Load);
        Row("候補の意味 (MeaningDictionary)", MeaningDictionary.Load);
        Row("スペルチェッカー (BuiltInWordChecker)", () => BuiltInWordChecker.Shared);
        Row("変換の候補 (CandidateDictionary)", () => CandidateDictionary.Load(null));

        Console.WriteLine("辞書\tマネージドヒープ\t読み込み");
        foreach (var (name, bytes, milliseconds) in rows) Console.WriteLine($"{name}\t{bytes / 1024.0 / 1024.0:F2} MB\t{milliseconds:F0} ms");
        Console.WriteLine($"合計\t{rows.Sum(r => r.Bytes) / 1024.0 / 1024.0:F2} MB\t{rows.Sum(r => r.Milliseconds):F0} ms");
        GC.KeepAlive(keep);
    }

    private static IEnumerable<string[]> Fields(string name) =>
        DictionarySource.DataLines(DictionarySource.ReadEmbedded(name)).Select(line => line.Split('\t'));

    public static void Dump(string output)
    {
        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append('\n');
        static string Join(IEnumerable<string>? words) => words is null ? "null" : string.Join("|", words);

        // 英訳: 書き方そのもの・後ろに な・に・を・ます が付いたもの・意味として
        var translations = TranslationDictionary.Load();
        Line($"英訳 {translations.Count}");
        foreach (var key in Fields("translations.txt").Select(f => f[0]).Distinct())
        {
            Line($"英訳 {key} {Join(translations.Lookup(key, key))} / {Join(translations.Lookup(key + "な", ""))} / {Join(translations.Lookup(key + "に", "x"))} / " +
                $"{Join(translations.Lookup(key + "を", ""))} / {Join(translations.Lookup(key + "ます", ""))} / {translations.Meaning(key)} / {translations.Meaning(key + "を")} / {translations.Meaning(key + "語")}");
        }

        // 意味: 読みの印あり・なし、語幹で引く形 (持って・美しかった)
        var meanings = MeaningDictionary.Load();
        Line($"意味 {meanings.Count}");
        foreach (var fields in Fields("meanings.txt"))
        {
            var key = fields[0];
            var stem = key.Length >= 2 ? key[..^1] : key;
            Line($"意味 {key} {meanings.Lookup(key, null)} / {meanings.Lookup(key, fields.ElementAtOrDefault(1))} / {meanings.Lookup(key + "を", "x")} / " +
                $"{meanings.Lookup(stem + "って", null)} / {meanings.Lookup(stem + "かった", null)} / {meanings.Lookup(stem, null)} / {meanings.Lookup(key + "字", null)}");
        }

        // 変換の候補: 読みそのもの・助詞が付いたもの・絵文字だけ
        var candidates = CandidateDictionary.Load(null);
        Line($"候補 {candidates.Count}");
        var readings = CandidateFiles
            .SelectMany(name => DictionarySource.LinesWithoutComments(DictionarySource.ReadEmbedded(name)))
            .Select(line => line.Split([' ', '\t', '\r', '　'], StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length > 0).Select(parts => parts[0])
            .Concat(EmojiFiles.SelectMany(Fields).Select(f => f[0].Trim())).Distinct();
        foreach (var reading in readings)
        {
            Line($"候補 {reading} {Join(candidates.Lookup(reading))} / {Join(candidates.Lookup(reading + "を"))} / {Join(candidates.LookupWords(reading + "から"))} / " +
                $"{Join(candidates.LookupEmoji(reading))} / {candidates.Contains(reading)} / {Join(candidates.Lookup(reading + "ふ"))}");
        }

        // スペルチェッカー: 一覧の語と、少し変えた語
        var spell = BuiltInWordChecker.Shared;
        foreach (var word in DictionarySource.ReadEmbedded("english-words.txt").Split('\n').Select(w => w.Trim()))
        {
            Line($"英単語 {word} {spell.IsWord(word)} {spell.IsWord(word + "s")} {spell.IsWord(word.Length > 1 ? word[..^1] : "")} {spell.IsWord(word.ToUpperInvariant())}");
        }

        // 読み: ローマ字の打ち間違い直しの語 (活用した形・付属語が続く形) と、かな入力の判定
        var detector = CompositionDetector.CreateDefault();
        var typos = RomajiTypoCorrector.Load(detector.Romaji);
        Line($"読み {typos.Count}");
        string[] tails = ["", "わ", "い", "え", "お", "か", "き", "け", "こ", "が", "ぎ", "げ", "ご", "さ", "し", "せ", "そ", "た", "ち", "て", "と", "な", "に", "ね", "の",
            "ば", "び", "べ", "ぼ", "ま", "み", "め", "も", "ら", "り", "れ", "ろ", "っ", "ん", "く", "かっ", "けれ", "そう", "すれ", "しろ", "くれ", "よく", "ます", "です"];
        foreach (var fields in Fields("readings.txt"))
        {
            var key = fields[0];
            var stem = key.Length >= 2 ? key[..^1] : key;
            var line = new StringBuilder($"読み {key} ");
            foreach (var tail in tails) line.Append(typos.IsWord(stem + tail) ? '1' : '0');
            foreach (var tail in tails) line.Append(typos.IsWord(key + tail) ? '1' : '0');
            line.Append(' ').Append(ContainsJapaneseReading(key, "")).Append(ContainsJapaneseReading(stem, "る")).Append(ContainsJapaneseReading("あ" + key, ""));
            Line(line.ToString());
        }
        foreach (var letters in (string[])["onegaishimsu", "arigatpu", "sumimasne", "kyouhaiitenki", "hashiwowataru", "kawanimiwoiru", "yoroshikuy", "tabemasita"])
        {
            Line($"打ち間違い {letters} {typos.Fix(letters, true)} {typos.Fix(letters, false)} {typos.IsCommonJapanese(letters)}");
        }

        // 判定の語の一覧: どの語のどの先頭も (語の後ろに 1 文字足したものも)
        var romaji = RomajiDetector.CreateDefault(null);
        var japanese = DictionarySource.Load("japanese.txt", null).ToList();
        var english = DictionarySource.Load("english.txt", null).Concat(ProperNouns.Load(null).LowercaseWords);
        foreach (var (name, list) in new[] { ("日本語", new DictionaryDetector(japanese, romaji).Words), ("英語", new EnglishDetector(english).Words) })
        {
            Line($"語の一覧 {name} {list.Count} {Join(list.Words)}");
            foreach (var word in list.Words)
            {
                var line = new StringBuilder($"語の一覧 {name} {word} ");
                for (var length = 0; length <= word.Length + 1; length++)
                {
                    var head = length <= word.Length ? word[..length] : word + "q";
                    line.Append(list.HasPrefix(head) ? '1' : '0').Append(list.ContainsWord(head) ? '1' : '0');
                }
                line.Append(' ').Append(Join(list.WordsStartingWith(word[0])));
                Line(line.ToString());
            }
        }
        var result = text.ToString();
        File.WriteAllText(output, result);
        Console.WriteLine($"{result.Count(c => c == '\n')} 行");
    }

    /// <summary>外部の漢字変換エンジンの処理時間を含めないため、読みをそのまま返す。</summary>
    private sealed class Identity : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => null;
    }

    public static void Bench(int rounds)
    {
        RequireRelease();
        Console.OutputEncoding = new UTF8Encoding(false);
        var detector = CompositionDetector.CreateDefault();
        detector.SpellChecker = BuiltInWordChecker.Shared;
        var options = new CompositionOptions
        {
            Candidates = CandidateDictionary.Load(null),
            Translations = TranslationDictionary.Load(),
            Meanings = MeaningDictionary.Load(),
            RomajiTypos = RomajiTypoCorrector.Load(detector.Romaji),
        };
        // --mixed-bench と同じ作り方の文 (日本語 + 英単語 + 日本語) を打って Enter
        var romaji = detector.Romaji;
        string[] prefixes = ["sorede", "kinouha", "chotto", "atode", "kyoumo", "sugoku", "mottekita", "tsukatte", "watashino", "minnani", "zenbu", "korekara"];
        string[] suffixes = ["wotsukau", "gasuki", "dekensaku", "nitsuite", "toiu", "mademiru", "kamo", "nanode", "ha", "wo", "ga", "ni", "de"];
        var words = DictionarySource.ReadEmbedded("english-words.txt").Split('\n').Select(w => w.Trim())
            .Where(w => w.Length is >= 4 and <= 10 && w.All(char.IsAsciiLetterLower) && !romaji.Analyze(w).IsValid)
            .Where((_, index) => index % 97 == 3).ToList();
        var inputs = words.Select((word, i) => prefixes[i % prefixes.Length] + word + suffixes[i % suffixes.Length] + "\n").ToList();
        // 打ち間違いを含む入力を、変換して確定する場合と直接確定する場合で測る
        foreach (var sentence in (string[])["onegaishimsu", "arigatpu", "sumimasne", "kyouhaiitenkidesune", "hashiwowataru", "fukuzatsuna", "kaiginiderimasu", "ashitahayasumidesu", "kawanimiwoiru", "nihongowohanasu"])
        {
            inputs.Add(sentence + " \n");
            inputs.Add(sentence + "\n");
        }
        var keys = inputs.Sum(input => input.Length);
        var times = new List<double>();
        var allocations = new List<double>();
        for (var round = 0; round < rounds; round++)
        {
            var session = new MeltypeSession(detector, new Identity(), options, () => new Settings());
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            foreach (var input in inputs)
            {
                foreach (var c in input)
                {
                    var vk = c == ' ' ? Input.VirtualKeys.Space : c == '\n' ? Input.VirtualKeys.Return : char.ToUpperInvariant(c);
                    session.HandleKey(vk, c is ' ' or '\n' ? null : c, false, false, false, false);
                }
            }
            watch.Stop();
            times.Add(watch.Elapsed.TotalMicroseconds / keys);
            allocations.Add((GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)keys);
        }
        times.Sort();
        allocations.Sort();
        Console.WriteLine($"{keys} 打鍵 × {rounds} 回。1 打鍵あたり: 時間 最小 {times[0]:F1} μs・中央 {times[rounds / 2]:F1} μs、割り当て 最小 {allocations[0] / 1024:F1} KB・中央 {allocations[rounds / 2] / 1024:F1} KB");
    }

    private sealed record LookupInput(string Text, string? Reading);

    public static void LookupBench(int rounds)
    {
        RequireRelease();
        Console.OutputEncoding = new UTF8Encoding(false);
        const int iterations = 200000;
        var meaningsText = DictionarySource.ReadEmbedded("meanings.txt");
        var translationsText = DictionarySource.ReadEmbedded("translations.txt");
        var meanings = MeaningDictionary.Parse(meaningsText);
        var translations = TranslationDictionary.Parse(translationsText);
        LookupInput[] fixedInputs =
        [
            new("橋", "はし"), new("箸を", "はしを"), new("持って", "もって"),
            new("美しかった", "うつくしかった"), new("複雑な", "ふくざつな"), new("川に", "かわに"),
            new("会議", "かいぎ"), new("学校", "がっこう"), new("存在しない語", "そんざいしないご"),
        ];
        var japanese = Corpus(meaningsText, withReading: true);
        var english = Corpus(translationsText, withReading: false);
        Console.WriteLine($"各検索を {iterations} 回慣らした後、{iterations} 回 × {rounds} 回計測 (辞書の読み込みは含めない)。");
        foreach (var (set, ja, en) in new[] { ("fixed", fixedInputs, fixedInputs), ("corpus", japanese, english) })
        {
            Run(set, "Japanese meaning", ja, i => meanings.Lookup(i.Text, i.Reading));
            Run(set, "English meaning", en, i => translations.Meaning(i.Text));
            Run(set, "Translations", en, i => translations.Lookup(i.Text, i.Reading ?? ""));
        }

        void Run(string set, string operation, LookupInput[] inputs, Action<LookupInput> lookup)
        {
            void Loop()
            {
                for (var i = 0; i < iterations; i++) lookup(inputs[i % inputs.Length]);
            }
            Loop();
            var times = new List<double>();
            var allocations = new List<double>();
            for (var round = 0; round < rounds; round++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                Loop();
                watch.Stop();
                times.Add(watch.Elapsed.TotalMicroseconds / iterations);
                allocations.Add((GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations);
            }
            times.Sort();
            allocations.Sort();
            Console.WriteLine($"{set} ({inputs.Length} 入力) / {operation}: 1 検索あたり: 時間 最小 {times[0]:F3} μs・中央 {times[rounds / 2]:F3} μs、割り当て 最小 {allocations[0]:F1} B・中央 {allocations[rounds / 2]:F1} B");
        }
    }

    // 3 欄そろった行を 23 行おきに 512 行選び、見出し・助詞付き・別の漢字が続く形を引く。
    private static LookupInput[] Corpus(string text, bool withReading) => text.Split('\n')
        .Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0 && line[0] != '#')
        .Select(line => line.Split('\t')).Where(fields => fields.Length >= 3)
        .Where((_, index) => index % 23 == 0).Take(512)
        .SelectMany(fields => LookupSuffixes.Select(suffix =>
            new LookupInput(fields[0] + suffix, withReading && fields[1].Length > 0 ? fields[1] : null))).ToArray();

    // 時間を測る道具は、最適化していない Debug の時間を出さないよう Release でしか動かさない
    private static void RequireRelease()
    {
#if DEBUG
        throw new InvalidOperationException("時間の計測は -c Release で実行してください。");
#endif
    }
}
