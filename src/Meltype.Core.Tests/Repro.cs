// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Tests;

/// <summary>
/// 報告された「打ったもの」を Meltype キーボードで打ってみる (GitHub の bot が誤判定の報告を再現するのに使う)。
/// MELTYPE_MOZC に変換ヘルパーがあればアプリと同じく Mozc で漢字に変換する。無ければ日本語 / 英語の判定だけ (漢字にしない)。
/// </summary>
internal static class Repro
{
    /// <summary>打つ文字の上限 (bot に長い文を渡されても時間がかからないように)。</summary>
    private const int MaxKeys = 300;

    public static void Type(string keys, string last)
    {
        // 英字・数字・記号と空白だけ (それ以外の文字はキーとして打てない)
        // bot が結果を読むので、ほかの環境の文字コードにせず UTF-8 で出す
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var typed = new string(keys.Where(c => c is >= ' ' and <= '~').Take(MaxKeys).ToArray()).Trim();
        // Windows のテストランナーなら Windows のスペルチェッカー、ほかの環境では同梱の英単語の一覧 (アプリと同じ)
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;

        var hasMozc = Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } path && File.Exists(path);
        // Mozc があればライブ変換付きキーボード、無ければ判定だけの偽物
        CompositionTests.Keyboard Create() => hasMozc ? Henkan.Keyboard() : new CompositionTests.Keyboard();

        var k = Create();
        k.Type(typed);
        var showing = k.Showing;
        switch (last)
        {
            case "space": k.Type(" "); break;
            case "enter": k.Type("\n"); break;
        }
        var committed = k.Host.Document;
        var composing = k.Showing;

        // Space で変換した結果も返す (漢字対応の確認用。last が space なら committed と同じ経路)
        string? converted = null;
        IReadOnlyList<string>? clauses = null;
        IReadOnlyList<string>? candidates = null;
        if (hasMozc && last != "space")
        {
            var s = Create();
            s.Type(typed + " ");
            var view = s.Host.View;
            converted = view is { Converting: true } ? string.Concat(view.Clauses ?? [view.Text]) : s.Host.Document;
            clauses = view is { Converting: true, Clauses: { } c } ? c : null;
            candidates = view is { Converting: true } ? view.Candidates.Take(9).ToList() : null;
        }
        else if (hasMozc && last == "space")
        {
            var view = k.Host.View;
            converted = view is { Converting: true } ? string.Concat(view.Clauses ?? [view.Text]) : committed;
            clauses = view is { Converting: true, Clauses: { } c } ? c : null;
            candidates = view is { Converting: true } ? view.Candidates.Take(9).ToList() : null;
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            typed,
            last,
            engine = hasMozc ? Henkan.EngineName : "なし (判定だけ)",
            // 最後のキーの前に変換ボックスに出ていたもの
            showing,
            // 最後のキーを押した後: 入力欄に入ったもの + まだ変換ボックスにあるもの
            committed,
            composing,
            converted,
            clauses,
            candidates,
            // 英語のスペルチェッカーを使ったか (Windows では使う。使わないと英単語の判定が実際のアプリと違うことがある)
            spellChecker = CompositionTests.Detector.SpellChecker is not null,
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        if (hasMozc) Henkan.Shutdown();
    }
}

/// <summary>Pull Request のチェック (.github/workflows/pr-checks.yml) で使う結果の出力。</summary>
internal static class Checks
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    /// <summary>品質テストの例ごとの結果を JSON で出す (main と Pull Request の結果を比べて、新しく外れた例を見つける)。</summary>
    public static void EvalJson(string path)
    {
        var result = Quality.Run();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            pass = result.Pass,
            total = result.Total,
            cases = result.Cases.Select(c => new { key = c.Key, ok = c.Ok, detail = c.Detail }),
        }, Json));
    }

    /// <summary>
    /// 「入力 → 期待」の一覧 (1 行に「入力&lt;Tab&gt;期待」) を確かめて JSON で出す。
    ///   入力がかな (しゃおみ) … 変換の候補に期待した語 (Xiaomi) が出るか
    ///   入力が英字 (nihongowohanasu) … 打って Enter した結果が期待どおりか。期待に漢字が入っていれば、日本語 / 英語の分かれ方だけを比べる
    ///   (テストでは変換エンジンを使わず漢字にしないため)。カタカナも同じ理由で、ひらがなに直して比べる (ホスティング = ほすてぃんぐ)。そのとき英字の大小は無視する (OEM = oem)。
    /// </summary>
    public static void Expect(string input, string output)
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var candidates = Composition.CandidateDictionary.Load(null);
        var results = new List<object>();
        foreach (var line in File.ReadAllLines(input))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2) continue;
            var (typed, expected) = (parts[0].Trim(), parts[1].Trim());
            if (typed.Length == 0 || expected.Length == 0) continue;
            if (typed.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー' or >= 'ァ' and <= 'ヺ'))
            {
                var reading = new string(typed.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
                var list = candidates.Lookup(reading);
                results.Add(new { typed, expected, kind = "候補", actual = string.Join(" ", list.Take(12)), ok = list.Contains(expected) });
                continue;
            }
            var k = new CompositionTests.Keyboard();
            k.Type(new string(typed.Where(c => c is >= ' ' and <= '~').Take(300).ToArray()) + "\n");
            var actual = k.Host.Document;
            var kanji = expected.Any(c => c is >= '㐀' and <= '鿿');
            var katakana = !kanji && expected.Any(c => c is >= 'ァ' and <= 'ヶ');
            var ok = kanji
                ? Words(actual) == Words(expected)
                : katakana
                    ? AsciiLower(Hiragana(actual)) == AsciiLower(Hiragana(expected))
                    : Hiragana(actual) == Hiragana(expected);
            results.Add(new { typed, expected, kind = kanji ? "判定 (英字の部分だけ比べる)" : katakana ? "入力 (カタカナはかなとして比べる)" : "入力", actual, ok });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(results, Json));
    }

    /// <summary>
    /// 日本語の文の中に英単語を入れて打ち (sorede + alcoholic + wotsukau)、確定した結果が「かな + 英単語 + かな」になるかを数える。
    /// 英単語は、同梱の英単語の一覧のうち、ローマ字としては読めない (英語としか読めない) 4〜10 文字の語から決まった間隔で選ぶ。
    /// 前後の日本語は、英単語とくっつきやすい形 (te・ni で終わる、母音で始まる助詞など) を混ぜてある。
    /// </summary>
    public static void MixedBench(string output)
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var romaji = CompositionTests.Detector.Romaji;
        // MELTYPE_BENCH_OFFSET で選ぶ語をずらす (調整に使っていない語で確かめる)
        var offset = int.TryParse(Environment.GetEnvironmentVariable("MELTYPE_BENCH_OFFSET"), out var o) ? o % 97 : 0;
        string[] prefixes = ["sorede", "kinouha", "chotto", "atode", "kyoumo", "sugoku", "mottekita", "tsukatte", "watashino", "minnani", "zenbu", "korekara"];
        string[] suffixes = ["wotsukau", "gasuki", "dekensaku", "nitsuite", "toiu", "mademiru", "kamo", "nanode", "ha", "wo", "ga", "ni", "de"];
        var words = Detection.DictionarySource.ReadEmbedded("english-words.txt").Split('\n').Select(w => w.Trim())
            .Where(w => w.Length is >= 4 and <= 10 && w.All(char.IsAsciiLetterLower) && !romaji.Analyze(w).IsValid)
            .Where((_, index) => index % 97 == offset).ToList();
        var results = new List<object>();
        var pass = 0;
        for (var i = 0; i < words.Count; i++)
        {
            for (var frame = 0; frame < 2; frame++)
            {
                var prefix = prefixes[(i * 2 + frame) % prefixes.Length];
                var suffix = suffixes[(i * 3 + frame * 5) % suffixes.Length];
                var typed = prefix + words[i] + suffix;
                var expected = romaji.ConvertLenient(prefix, final: true) + words[i] + romaji.ConvertLenient(suffix, final: true);
                var k = new CompositionTests.Keyboard();
                k.Type(typed + "\n");
                var ok = Hiragana(k.Host.Document) == expected;
                if (ok) pass++;
                results.Add(new { typed, expected, actual = k.Host.Document, ok });
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { pass, total = results.Count, cases = results }, Json));
        Console.WriteLine($"{pass}/{results.Count}");
    }

    /// <summary>
    /// 人手で期待を書いた日英混在入力を、Enter まで実際のキー経路で打って採点する。
    /// 漢字変換・IMK上のカーソル操作は評価対象外。既定スコアを CI の合否に使わない。
    /// </summary>
    public static void MixedCorpus(string input, string output)
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var rows = new List<(string Category, string Typed, string Expected)>();
        foreach (var (line, index) in File.ReadLines(input).Select((line, index) => (line, index + 1)))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var columns = line.Split('\t');
            if (columns.Length != 3 || columns[0].Length == 0 || columns[1].Length == 0 ||
                columns[2].Length == 0 || columns[1].Length > 300 ||
                columns[1].Any(c => c is < ' ' or > '~'))
                throw new FormatException($"{input}:{index}: category, ASCII keys, expected をタブで3列指定してください");
            rows.Add((columns[0], columns[1], columns[2]));
        }
        if (rows.Count == 0) throw new FormatException("比較する入力がありません");

        var cases = rows.Select(row =>
        {
            var keyboard = new CompositionTests.Keyboard();
            keyboard.Type(row.Typed + "\n");
            var actual = Hiragana(keyboard.Host.Document);
            return new { category = row.Category, typed = row.Typed, expected = row.Expected,
                         actual, ok = actual == row.Expected };
        }).ToList();
        var groups = cases.GroupBy(c => c.category).OrderBy(g => g.Key)
            .Select(g => new { category = g.Key, pass = g.Count(c => c.ok), total = g.Count() }).ToList();
        var pass = cases.Count(c => c.ok);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            mode = "Core key-to-Enter, no kanji conversion, not GUI",
            corpus = Path.GetFileName(input),
            pass, total = cases.Count, groups, cases,
        }, Json));
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        foreach (var group in groups)
            Console.WriteLine($"{group.category}: {group.pass}/{group.total}");
        Console.WriteLine($"Total: {pass}/{cases.Count} (diagnostic only)");
    }

    /// <summary>カタカナをひらがなに直す (テストの変換エンジンはカタカナにしないため)。</summary>
    private static string Hiragana(string text) =>
        new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

    /// <summary>カタカナの期待では、英字の大小だけを無視する (OEM = oem)。</summary>
    private static string AsciiLower(string text) =>
        new(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c).ToArray());

    /// <summary>英字の語だけを取り出す (日本語 / 英語の分かれ方を比べる)。</summary>
    private static string Words(string text) =>
        string.Join(" ", System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z][A-Za-z'’-]*").Select(m => m.Value.ToLowerInvariant()));
}

/// <summary>
/// henkan-test: 打ったキーを Meltype キーボードで打ち、変換ボックスの表示・Enter で確定した結果・Space で変換した結果を JSON で返す。
/// 環境変数 MELTYPE_MOZC に Mozc の変換ヘルパーの場所があれば、アプリと同じく Mozc で漢字に変換する。無ければ日本語 / 英語の判定だけ (漢字にしない)。
///   dotnet run --project src/Meltype.Core.Tests -- --henkan kyouhagoogledekensaku
/// </summary>
internal static class Henkan
{
    private const int MaxKeys = 300;

    /// <summary>Mozc の学習データの場所。bot が同時にいくつも動かすので、プロセスごとに分ける (終わったら消す)。</summary>
    private static readonly string ProfileDirectory = Path.Combine(Path.GetTempPath(), $"meltype-henkan-mozc-{Environment.ProcessId}");

    /// <summary>終わるときに、Mozc の変換ヘルパーを止めて、学習データの場所を消す。</summary>
    public static void Shutdown()
    {
        if (Mozc.IsValueCreated) Mozc.Value?.Dispose();
        try { if (Directory.Exists(ProfileDirectory)) Directory.Delete(ProfileDirectory, recursive: true); } catch { }
    }

    /// <summary>続けて試すとき (--jht-batch)、前の文の控えを次の文に持ち越さない (1 文ずつ起動したときと同じ結果にする)。</summary>
    public static void Reset()
    {
        if (Mozc.IsValueCreated) Mozc.Value?.ClearCache();
    }

    private static readonly Lazy<Composition.MozcConverter?> Mozc = new(() =>
        Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } helper && File.Exists(helper)
            ? new Composition.MozcConverter(helper, ProfileDirectory)
            : null);
    private static readonly Lazy<Composition.TranslationDictionary> Translations = new(Composition.TranslationDictionary.Load);

    /// <summary>MELTYPE_MOZC の Mozc (無ければ null)。性能の計測 (--perf) が控えの使い回しの回数を読むのに使う。</summary>
    public static Composition.MozcConverter? Engine => Mozc.Value;

    public static Composition.TranslationDictionary TranslationDictionary => Translations.Value;

    /// <summary>Windows のテストランナーが Microsoft IME を変換エンジンにするとき (Mozc が無いとき) に入れる。</summary>
    public static Composition.IKanjiConverter? FallbackConverter { get; set; }

    public static string EngineName => Mozc.Value is not null ? "Mozc" : FallbackConverter is not null ? "Microsoft IME" : "なし (判定だけ)";

    /// <summary>アプリと同じ設定の Meltype キーボード (ライブ変換 ON、Mozc があれば Mozc で変換)。</summary>
    public static CompositionTests.Keyboard Keyboard()
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        // Mozc には覚えさせない (覚えさせると、前の打ち方・前の文で確定した変換が次の打ち方の結果を変えてしまう。
        // 確かめたいのは、まだ何も覚えていない人の最初の変換)
        var converter = Mozc.Value is { } engine ? new WithoutLearning(engine) : FallbackConverter;
        return new(live: true, converter: converter, moreCandidates: Mozc.Value is { } mozc ? mozc.Candidates : null,
            userDictionary: new Composition.UserDictionary(null), translations: Translations.Value);
    }

    /// <summary>変換だけを頼み、覚えさせない変換エンジン (ILearningConverter を持たない)。</summary>
    private sealed class WithoutLearning(Composition.IKanjiConverter inner) : Composition.IKanjiConverter
    {
        public string? Convert(string hiragana) => inner.Convert(hiragana);
        public IReadOnlyList<Composition.ConversionClause>? ConvertClauses(string hiragana, string? context = null) => inner.ConvertClauses(hiragana, context);
    }

    public static void Run(string keys)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var typed = new string(keys.Where(c => c is >= ' ' and <= '~').Take(MaxKeys).ToArray()).Trim();
        CompositionTests.Keyboard Create() => Keyboard();

        // そのまま打った表示と、Enter で確定した結果
        var k = Create();
        k.Type(typed);
        var showing = k.Showing ?? "";
        k.Type("\n");
        var entered = k.Host.Document;

        // Space で変換した結果 (英語で終わっていれば確定して空白)
        var s = Create();
        s.Type(typed + " ");
        var view = s.Host.View;
        var converted = view is { Converting: true } ? string.Concat(view.Clauses ?? [view.Text]) : s.Host.Document;
        var clauses = view is { Converting: true, Clauses: { } c } ? c : [];
        var candidates = view is { Converting: true } ? view.Candidates.Take(9).ToList() : [];

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            typed,
            engine = EngineName,
            showing,
            entered,
            converted,
            clauses,
            candidates,
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}

/// <summary>
/// 英単語の混じった文の変換を 2 通りで比べる (MELTYPE_MOZC に Mozc の変換ヘルパーの場所が要る)。
///   今の変換: Meltype キーボードで打って Space で変換した結果 (英単語のところで文を切って、日本語の部分を別々に Mozc に渡す)
///   丸ごと:   英単語を仮の名詞 (えっくす) に置き換えた読みを、文ごと 1 回で Mozc に渡し、エックス を英単語に戻す
/// 入力ファイルは 1 行に「打ったもの&lt;Tab&gt;期待」。
/// </summary>
internal static class MozcCompare
{
    private const string Placeholder = "えっくす";
    private const string PlaceholderText = "エックス";

    public static void Run(string input, string output)
    {
        var helper = Environment.GetEnvironmentVariable("MELTYPE_MOZC");
        if (helper is null || !File.Exists(helper)) throw new InvalidOperationException("MELTYPE_MOZC に Mozc の変換ヘルパーの場所を入れてください");
        var profile = Path.Combine(Path.GetTempPath(), $"meltype-mozc-compare-{Environment.ProcessId}");
        using var mozc = new Composition.MozcConverter(helper, profile);
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var results = new List<object>();
        int currentOk = 0, wholeOk = 0;
        foreach (var line in File.ReadAllLines(input))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2 || parts[0].StartsWith('#')) continue;
            var (typed, expected) = (parts[0].Trim(), parts[1].Trim());

            // 今の変換
            var s = Henkan.Keyboard();
            s.Type(typed + " ");
            var view = s.Host.View;
            var current = view is { Converting: true } ? string.Concat(view.Clauses ?? [view.Text]) : s.Host.Document.TrimEnd();
            Henkan.Reset();

            // 丸ごと: 変換エンジン無しのキーボードで、英語と日本語 (かな) の分かれ方を見る
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            var shown = k.Showing ?? "";
            var english = System.Text.RegularExpressions.Regex.Matches(shown, "[A-Za-z0-9][A-Za-z0-9._'+#-]*").Select(m => m.Value).ToList();
            var reading = System.Text.RegularExpressions.Regex.Replace(shown, "[A-Za-z0-9][A-Za-z0-9._'+#-]*", Placeholder);
            var clauses = mozc.ConvertClauses(reading) ?? [];
            var text = string.Concat(clauses.Select(c => c.Text));
            var placeholders = System.Text.RegularExpressions.Regex.Matches(text, PlaceholderText).Count;
            string whole;
            if (placeholders == english.Count)
            {
                var index = 0;
                whole = System.Text.RegularExpressions.Regex.Replace(text, PlaceholderText, _ => english[index++]);
            }
            else whole = $"(仮の名詞が {english.Count} 個 → {placeholders} 個) {text}";
            mozc.ClearCache();

            var ok1 = Normalize(current) == Normalize(expected);
            var ok2 = Normalize(whole) == Normalize(expected);
            if (ok1) currentOk++;
            if (ok2) wholeOk++;
            results.Add(new { typed, expected, shown, current, whole, wholeClauses = clauses.Select(c => c.Reading + ":" + c.Text), currentOk = ok1, wholeOk = ok2 });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { currentOk, wholeOk, total = results.Count, cases = results },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"今の変換 {currentOk}/{results.Count}、丸ごと {wholeOk}/{results.Count}");
    }

    /// <summary>比べるときは空白と、英字の大文字・小文字を無視する (GitHub と github は同じとみなす)。</summary>
    private static string Normalize(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());
}
