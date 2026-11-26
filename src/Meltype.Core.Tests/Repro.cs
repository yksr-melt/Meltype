// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Tests;

/// <summary>
/// 報告された「打ったもの」を Meltype キーボードで打ってみる (GitHub の bot が誤判定の報告を再現するのに使う)。
/// MELTYPE_MOZC に変換ヘルパーがあればアプリと同じく Mozc で漢字に変換する。無ければ日本語 / 英語の判定だけ (漢字にしない)。
/// Mozc に加えて MELTYPE_ZENZAI_SERVER・MELTYPE_ZENZAI_MODEL があれば、Zenzai も使って打った結果を zenzai に出す。
///   --repro 26niti space --learn "niti → （日）"
/// --learn の手順は、先に別の入力欄で打って確定し、Meltype に学習させる (学習した後で起きる誤変換を再現する。Mozc には覚えさせない: Henkan.Keyboard)。
/// </summary>
internal static class Repro
{
    // bot (meltype-bot.mjs) は、Mozc だけの結果を一番上の committed・converted などで、Zenzai の結果を zenzai の下の同じ名前で読む
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    /// <summary>打つ文字の上限 (bot に長い文を渡されても時間がかからないように)。</summary>
    private const int MaxKeys = 300;
    /// <summary>学習させる手順の上限 (bot に長い手順を渡されても時間がかからないように)。</summary>
    private const int MaxLessons = 5;

    /// <param name="Showing">最後のキーの前に変換ボックスに出ていたもの。</param>
    /// <param name="Committed">最後のキーを押した後に、入力欄に入ったもの。</param>
    /// <param name="Composing">最後のキーを押した後に、まだ変換ボックスにあるもの。</param>
    /// <param name="Converted">Space で変換した結果 (漢字対応の確認用。変換エンジンが無ければ null)。</param>
    internal sealed record Result(string? Showing, string Committed, string? Composing, string? Converted, IReadOnlyList<string>? Clauses, IReadOnlyList<string>? Candidates);

    /// <summary>学習させる手順。Keys を打ち、Text があれば Space で変換して候補から Text を選び、Enter で確定する。</summary>
    internal sealed record Lesson(string Keys, string? Text)
    {
        /// <summary>「打ったキー」か「打ったキー → 確定した語」。</summary>
        public static Lesson Parse(string text)
        {
            var parts = text.Split('→', 2);
            var word = parts.ElementAtOrDefault(1)?.Trim();
            return new(Repro.Keys(parts[0]), string.IsNullOrEmpty(word) ? null : word);
        }
    }

    /// <summary>--repro の後ろの引数: 打ったキー [space|enter|none] [--learn 手順]…</summary>
    public static void Type(IReadOnlyList<string> args)
    {
        var lessons = args.Select((arg, i) => (arg, i)).Where(a => a.arg == "--learn" && a.i + 1 < args.Count)
            .Select(a => Lesson.Parse(args[a.i + 1])).Take(MaxLessons).ToList();
        Type(args.ElementAtOrDefault(0) ?? "", args.ElementAtOrDefault(1) is { } last && last != "--learn" ? last : "enter", lessons);
    }

    // 英字・数字・記号と空白だけ (それ以外の文字はキーとして打てない)
    private static string Keys(string text) => new string(text.Where(c => c is >= ' ' and <= '~').Take(MaxKeys).ToArray()).Trim();

    private static void Type(string keys, string last, IReadOnlyList<Lesson> lessons)
    {
        // bot が結果を読むので、ほかの環境の文字コードにせず UTF-8 で出す
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var typed = Keys(keys);

        var hasMozc = Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } path && File.Exists(path);
        // 学習データは、手順で覚えたものをどの入力欄でも読む
        var history = new Meltype.Learning.ConversionHistory(null);
        var languages = new Meltype.Detection.LanguageMemory(null);
        // Mozc があればライブ変換付きキーボード、無ければ判定だけの偽物。手順のほかは覚えない (Henkan.Keyboard と同じ)
        CompositionTests.Keyboard Create(bool zenzai) => hasMozc ? Henkan.Keyboard(zenzai, history, languages)
            : new(history: history, languages: languages) { Private = true };
        var learned = Learn(() => Create(zenzai: false), lessons);
        var mozc = Run(() => Create(zenzai: false), typed, last, hasMozc);
        // Zenzai が起動しない・途中で答えなくなったときは、Mozc に任せた結果を Zenzai の結果として出さず、理由を出す
        object? zenzai = null;
        if (hasMozc && Henkan.HasZenzai)
        {
            if (Henkan.StartZenzai() is { } notStarted) zenzai = new { error = notStarted };
            else
            {
                var result = Run(() => Create(zenzai: true), typed, last, convert: true);
                zenzai = Henkan.ZenzaiFailure is { } failure ? new { error = failure } : result;
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            typed,
            last,
            engine = hasMozc ? Henkan.EngineName : "なし (判定だけ)",
            learned,
            mozc.Showing,
            mozc.Committed,
            mozc.Composing,
            mozc.Converted,
            mozc.Clauses,
            mozc.Candidates,
            zenzai,
        }, Json));

        if (hasMozc) Henkan.Shutdown();
    }

    internal static Result Run(Func<CompositionTests.Keyboard> create, string typed, string last, bool convert)
    {
        var k = create();
        k.Type(typed);
        var showing = k.Showing;
        switch (last)
        {
            case "space": k.Type(" "); break;
            case "enter": k.Type("\n"); break;
        }
        if (!convert) return new(showing, k.Host.Document, k.Showing, null, null, null);
        // last が space なら、打った結果がそのまま Space で変換した結果
        var host = k.Host;
        if (last != "space")
        {
            var s = create();
            s.Type(typed + " ");
            host = s.Host;
        }
        var view = host.View;
        return new(showing, k.Host.Document, k.Showing,
            view is { Converting: true } ? string.Concat(view.Clauses ?? [view.Text]) : host.Document,
            view is { Converting: true, Clauses: { } c } ? c : null,
            view is { Converting: true } ? view.Candidates.Take(9).ToList() : null);
    }

    /// <summary>
    /// 手順を 1 つずつ、覚える入力欄で打って確定する。手順ごとに確定したものを返す (手順が無ければ null)。
    /// 確定したものが次に打つものの前の文にならないように、手順ごとに入力欄を分ける。
    /// </summary>
    internal static IReadOnlyList<string>? Learn(Func<CompositionTests.Keyboard> create, IReadOnlyList<Lesson> lessons)
    {
        if (lessons.Count == 0) return null;
        return lessons.Select(lesson =>
        {
            var k = create();
            k.Private = false;
            k.Type(lesson.Keys);
            if (lesson.Text is { } text && k.Showing != text)
            {
                // Space で変換して、候補の Text まで Space で進める (候補に無ければ最初の候補で確定する)
                k.Type(" ");
                var index = k.Host.View?.Candidates.ToList().IndexOf(text) ?? -1;
                for (var i = 0; i < index; i++) k.Type(" ");
            }
            k.Type("\n");
            return k.Host.Document;
        }).ToList();
    }
}

/// <summary>Pull Request のチェック (.github/workflows/core.yml・pr-expectations.yml) で使う結果の出力。</summary>
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
    /// 「入力 → 期待」の一覧 (1 行に「入力&lt;Tab&gt;期待」) を、アプリと同じ Meltype キーボードと Mozc で確かめて JSON で出す (MELTYPE_MOZC が要る)。
    ///   入力がかな (しゃおみ) … 変換の候補に期待した語 (Xiaomi) が出るか
    ///   入力が英字 (nihongowohanasu) … 打って Enter で確定した結果か Space で変換した結果が期待と同じか
    /// </summary>
    public static void Expect(string input, string output)
    {
        var mozc = Henkan.Engine ?? throw new InvalidOperationException("MELTYPE_MOZC に Mozc の変換ヘルパーの場所を入れてください");
        var candidates = Dictionary.CandidateDictionary.Load(null);
        var results = new List<object>();
        foreach (var line in File.ReadAllLines(input))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2) throw new FormatException($"Expected exactly two tab-separated columns in {input}: {line}");
            var (typed, expected) = (parts[0].Trim(), parts[1].Trim());
            if (typed.Length == 0 || expected.Length == 0) continue;
            if (typed.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー' or >= 'ァ' and <= 'ヺ'))
            {
                var reading = new string(typed.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
                var list = candidates.Lookup(reading).Concat(mozc.Candidates(reading, long.MaxValue)).Distinct().ToList();
                results.Add(new { typed, expected, kind = "候補", actual = string.Join(" ", list.Take(12)), ok = list.Contains(expected) });
                Henkan.Reset();
                continue;
            }
            var keys = new string(typed.Where(c => c is >= ' ' and <= '~').Take(300).ToArray());
            var k = Henkan.Keyboard();
            k.Type(keys + "\n");
            var entered = k.Host.Document;
            Henkan.Reset();
            var s = Henkan.Keyboard();
            s.Type(keys + " ");
            var view = s.Host.View;
            var converted = view is { Converting: true } ? s.Host.Document + string.Concat(view.Clauses ?? [view.Text]) : s.Host.Document.TrimEnd();
            Henkan.Reset();
            var actual = entered == converted ? entered : $"Enter: {entered} / Space: {converted}";
            results.Add(new { typed, expected, kind = "入力", actual, ok = entered == expected || converted == expected });
        }
        // MozcConverter は失敗を null や空の候補で返すので、Mozc なしの結果で通っていないことを確かめる
        if (mozc.ConvertText("にほんご", null) is null) throw new InvalidOperationException("Mozc を起動できないか、Mozc が応答しませんでした");
        File.WriteAllText(output, JsonSerializer.Serialize(results, Json));
    }

    /// <summary>
    /// 日本語の文の中に英単語を入れて打ち (sorede + alcoholic + wotsukau)、確定した結果が「かな + 英単語 + かな」になるかを数える。
    /// 英単語は、同梱の英単語の一覧のうち、ローマ字としては読めない (英語としか読めない) 4〜10 文字の語から決まった間隔で選ぶ。
    /// 前後の日本語は、英単語とくっつきやすい形 (te・ni で終わる、母音で始まる助詞など) を混ぜてある。
    /// </summary>
    public static void MixedBench(string output)
    {
        var romaji = CompositionTests.Detector.Romaji;
        // MELTYPE_BENCH_OFFSET で選ぶ語をずらす (調整に使っていない語で確かめる)
        var offset = int.TryParse(Environment.GetEnvironmentVariable("MELTYPE_BENCH_OFFSET"), out var o) ? o % 97 : 0;
        string[] prefixes = ["sorede", "kinouha", "chotto", "atode", "kyoumo", "sugoku", "mottekita", "tsukatte", "watashino", "minnani", "zenbu", "korekara"];
        string[] suffixes = ["wotsukau", "gasuki", "dekensaku", "nitsuite", "toiu", "mademiru", "kamo", "nanode", "ha", "wo", "ga", "ni", "de"];
        var words = Libraries.Lexicon.DictionarySource.ReadEmbedded("english-words.txt").Split('\n').Select(w => w.Trim())
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

    /// <summary>カタカナをひらがなに直す (テストの変換エンジンはカタカナにしないため)。</summary>
    private static string Hiragana(string text) =>
        new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

}

/// <summary>
/// henkan-test: 打ったキーを Meltype キーボードで打ち、変換ボックスの表示・Enter で確定した結果・Space で変換した結果を JSON で返す。
/// 環境変数 MELTYPE_MOZC に Mozc の変換ヘルパーの場所があれば、アプリと同じく Mozc で漢字に変換する。無ければ日本語 / 英語の判定だけ (漢字にしない)。
///   dotnet run --project src/Meltype.Core.Tests -- --henkan kyouhagoogledekensaku
/// </summary>
internal static class Henkan
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const int MaxKeys = 300;

    /// <summary>Mozc の学習データの場所。bot が同時にいくつも動かすので、プロセスごとに分ける (終わったら消す)。</summary>
    private static readonly string ProfileDirectory = Path.Combine(Path.GetTempPath(), $"meltype-henkan-mozc-{Environment.ProcessId}");

    /// <summary>終わるときに、Mozc の変換ヘルパーを止めて、学習データの場所を消す。</summary>
    public static void Shutdown()
    {
        if (Mozc.IsValueCreated) Mozc.Value?.Dispose();
        if (Zenzai.IsValueCreated) Zenzai.Value?.Dispose();
        try { if (Directory.Exists(ProfileDirectory)) Directory.Delete(ProfileDirectory, recursive: true); } catch { }
    }

    /// <summary>続けて試すとき (--jht-batch)、前の文の控えを次の文に持ち越さない (1 文ずつ起動したときと同じ結果にする)。</summary>
    public static void Reset()
    {
        if (Mozc.IsValueCreated) Mozc.Value?.ClearCache();
    }

    private static readonly Lazy<Engine.MozcConverter?> Mozc = new(() =>
        Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } helper && File.Exists(helper)
            ? WithBuiltInPhrases(new Engine.MozcConverter(new Engine.MozcHelperProcess(helper, ProfileDirectory)))
            : null);

    /// <summary>アプリ (MeltypeRuntime) と同じく、同梱の語句を Mozc のユーザー辞書に渡す。</summary>
    private static Engine.MozcConverter WithBuiltInPhrases(Engine.MozcConverter mozc)
    {
        if (!mozc.SetUserDictionary([.. new Meltype.Dictionary.UserDictionary(null).EngineWords().Words.Select(w => (w.Reading, w.Word, w.Pos))]))
            throw new InvalidOperationException("Mozc の変換ヘルパーが同梱の語句を受け取りませんでした");
        return mozc;
    }

    private static readonly Lazy<Dictionary.TranslationDictionary> Translations = new(Dictionary.TranslationDictionary.Load);

    // bot の runner は CPU だけで計算するので、アプリより長く待つ (待ちきれなかった答えを ZenzaiConverter は捨てて、しばらく休む)
    private const int ZenzaiTimeoutMs = 30000;
    private static readonly Lazy<Engine.ZenzaiConverter?> Zenzai = new(() =>
        Environment.GetEnvironmentVariable("MELTYPE_ZENZAI_SERVER") is { Length: > 0 } server &&
        Environment.GetEnvironmentVariable("MELTYPE_ZENZAI_MODEL") is { Length: > 0 } model
            ? new Engine.ZenzaiConverter(server, model, ZenzaiTimeoutMs)
            : null);

    /// <summary>MELTYPE_ZENZAI_SERVER と MELTYPE_ZENZAI_MODEL があるか (ファイルがあるかは <see cref="StartZenzai"/> で確かめる)。</summary>
    public static bool HasZenzai => Zenzai.Value is not null;

    /// <summary>Zenzai の llama-server を起動して、答えられるまで待つ。使えなければその理由、使えれば null。</summary>
    public static string? StartZenzai()
    {
        if (Zenzai.Value is not { IsInstalled: true } zenzai) return "llama-server かモデルのファイルがありません";
        zenzai.WarmUp();
        for (var i = 0; i < 200 && !zenzai.IsReady && !zenzai.GaveUp; i++) Thread.Sleep(300);
        return zenzai.IsReady ? null : "llama-server が起動しませんでした";
    }

    /// <summary>打った後に、Zenzai が途中で答えなくなっていた理由 (答えなかった変換は Mozc の結果になる)。答えていれば null。</summary>
    public static string? ZenzaiFailure => Zenzai.Value switch
    {
        { GaveUp: true } => "Zenzai が続けて答えませんでした",
        { IsResting: true } => $"Zenzai の答えが {ZenzaiTimeoutMs / 1000} 秒以内に届きませんでした",
        _ => null,
    };

    /// <summary>MELTYPE_MOZC の Mozc (無ければ null)。性能の計測 (--perf) が控えの使い回しの回数を読むのに使う。</summary>
    public static Engine.MozcConverter? Engine => Mozc.Value;

    public static Dictionary.TranslationDictionary TranslationDictionary => Translations.Value;

    public static string EngineName => Mozc.Value is not null ? "Mozc" : "なし (判定だけ)";

    /// <summary>漢字の読み (Mozc の逆変換。--jht が出てほしい文の読みを求める)。Mozc が無ければ null。</summary>
    public static Func<string, string?>? ReadingOf => Mozc.Value is { } mozc ? mozc.Reading : null;

    /// <summary>アプリと同じ設定の Meltype キーボード (ライブ変換 ON、Mozc があれば Mozc で変換)。</summary>
    public static CompositionTests.Keyboard Keyboard() => Keyboard(zenzai: false);

    /// <summary>
    /// <see cref="Keyboard()"/> の、zenzai なら Zenzai の答えも使うもの。history・languages は学習データ (無ければ新しく作る)。
    /// 確定しても Meltype に覚えさせない (覚えないように求める入力欄で打つ。覚えたものは読む)。覚えさせると、前の打ち方・前の文で確定した
    /// 変換が次の打ち方の結果を変えてしまう (確かめたいのは、まだ何も覚えていない人か、--repro の --learn の手順だけを覚えた人の変換)。
    /// </summary>
    public static CompositionTests.Keyboard Keyboard(bool zenzai, Learning.ConversionHistory? history = null, Detection.LanguageMemory? languages = null)
    {
        // アプリと同じく Mozc と Zenzai を合わせた変換エンジンを使う。答えが届くまで待つ (LateResults にしない)
        var converter = Mozc.Value is { } engine ? new Engine.HybridConverter(engine, zenzai ? () => Zenzai.Value : null) : null;
        return new(live: true, history: history, converter: converter is null ? null : converter,
            moreCandidates: converter is null ? null : reading => converter.Candidates(reading, long.MaxValue),
            userDictionary: new Meltype.Dictionary.UserDictionary(null), languages: languages, translations: Translations.Value)
        { Private = true };
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
        }, Json));
    }
}
