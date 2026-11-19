// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Tests;

/// <summary>
/// 報告された「打ったもの」を Meltype キーボードで打ってみる (GitHub の bot が誤判定の報告を再現するのに使う)。
/// MELTYPE_MOZC に変換ヘルパーがあればアプリと同じく Mozc で漢字に変換する。無ければ日本語 / 英語の判定だけ (漢字にしない)。
/// Mozc に加えて MELTYPE_ZENZAI_SERVER・MELTYPE_ZENZAI_MODEL があれば、Zenzai も使って打った結果を zenzai に出す。
/// </summary>
internal static class Repro
{
    // bot (meltype-bot.mjs) は、Mozc だけの結果を一番上の committed・converted などで、Zenzai の結果を zenzai の下の同じ名前で読む
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    /// <summary>打つ文字の上限 (bot に長い文を渡されても時間がかからないように)。</summary>
    private const int MaxKeys = 300;

    /// <param name="Showing">最後のキーの前に変換ボックスに出ていたもの。</param>
    /// <param name="Committed">最後のキーを押した後に、入力欄に入ったもの。</param>
    /// <param name="Composing">最後のキーを押した後に、まだ変換ボックスにあるもの。</param>
    /// <param name="Converted">Space で変換した結果 (漢字対応の確認用。変換エンジンが無ければ null)。</param>
    private sealed record Result(string? Showing, string Committed, string? Composing, string? Converted, IReadOnlyList<string>? Clauses, IReadOnlyList<string>? Candidates);

    public static void Type(string keys, string last)
    {
        // 英字・数字・記号と空白だけ (それ以外の文字はキーとして打てない)
        // bot が結果を読むので、ほかの環境の文字コードにせず UTF-8 で出す
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var typed = new string(keys.Where(c => c is >= ' ' and <= '~').Take(MaxKeys).ToArray()).Trim();

        var hasMozc = Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } path && File.Exists(path);
        // Mozc があればライブ変換付きキーボード、無ければ判定だけの偽物
        var mozc = Run(() => hasMozc ? Henkan.Keyboard() : new CompositionTests.Keyboard(), typed, last, hasMozc);
        // Zenzai が起動しない・途中で答えなくなったときは、Mozc に任せた結果を Zenzai の結果として出さず、理由を出す
        object? zenzai = null;
        if (hasMozc && Henkan.HasZenzai)
        {
            if (Henkan.StartZenzai() is { } notStarted) zenzai = new { error = notStarted };
            else
            {
                var result = Run(() => Henkan.Keyboard(zenzai: true), typed, last, convert: true);
                zenzai = Henkan.ZenzaiFailure is { } failure ? new { error = failure } : result;
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            typed,
            last,
            engine = hasMozc ? Henkan.EngineName : "なし (判定だけ)",
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

    private static Result Run(Func<CompositionTests.Keyboard> create, string typed, string last, bool convert)
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
        var candidates = Composition.CandidateDictionary.Load(null);
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
                var list = candidates.Lookup(reading).Concat(mozc.Candidates(reading)).Distinct().ToList();
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
        if (mozc.Convert("にほんご") is null) throw new InvalidOperationException("Mozc を起動できないか、Mozc が応答しませんでした");
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

    private static readonly Lazy<Composition.MozcConverter?> Mozc = new(() =>
        Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } helper && File.Exists(helper)
            ? new Composition.MozcConverter(new WithoutLearning(new Composition.MozcHelperProcess(helper, ProfileDirectory)))
            : null);
    private static readonly Lazy<Composition.TranslationDictionary> Translations = new(Composition.TranslationDictionary.Load);

    // bot の Windows の runner は CPU だけで計算するので、アプリより長く待つ (待ちきれなかった答えを ZenzaiConverter は捨てて、しばらく休む)
    private const int ZenzaiTimeoutMs = 30000;
    private static readonly Lazy<Composition.ZenzaiConverter?> Zenzai = new(() =>
        Environment.GetEnvironmentVariable("MELTYPE_ZENZAI_SERVER") is { Length: > 0 } server &&
        Environment.GetEnvironmentVariable("MELTYPE_ZENZAI_MODEL") is { Length: > 0 } model
            ? new Composition.ZenzaiConverter(server, model, ZenzaiTimeoutMs)
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
    public static Composition.MozcConverter? Engine => Mozc.Value;

    public static Composition.TranslationDictionary TranslationDictionary => Translations.Value;

    public static string EngineName => Mozc.Value is not null ? "Mozc" : "なし (判定だけ)";

    /// <summary>漢字の読み (Mozc の逆変換。--jht が出てほしい文の読みを求める)。Mozc が無ければ null。</summary>
    public static Func<string, string?>? ReadingOf => Mozc.Value is { } mozc ? mozc.Reading : null;

    /// <summary>アプリと同じ設定の Meltype キーボード (ライブ変換 ON、Mozc があれば Mozc で変換)。</summary>
    public static CompositionTests.Keyboard Keyboard() => Keyboard(zenzai: false);

    /// <summary><see cref="Keyboard()"/> の、zenzai なら Zenzai の答えも使うもの。</summary>
    public static CompositionTests.Keyboard Keyboard(bool zenzai)
    {
        // アプリと同じく Mozc と Zenzai を合わせた変換エンジンを使う。答えが届くまで待つ (Budget を Enable しない)
        var converter = Mozc.Value is { } engine ? new Composition.HybridConverter(engine, zenzai ? () => Zenzai.Value : null) : null;
        return new(live: true, converter: converter, moreCandidates: converter is null ? null : converter.Candidates,
            userDictionary: new Meltype.Composition.UserDictionary(null), translations: Translations.Value);
    }

    /// <summary>
    /// 学習の要求 (L) に答えるだけで、Mozc に覚えさせないヘルパー。覚えさせると、前の打ち方・前の文で確定した変換が次の打ち方の結果を変えてしまう
    /// (確かめたいのは、まだ何も覚えていない人の最初の変換)。
    /// </summary>
    private sealed class WithoutLearning(Composition.IMozcHelper inner) : Composition.IMozcHelper
    {
        public bool IsInstalled => inner.IsInstalled;
        public void Start() => inner.Start();
        public string? Send(string request) => request.StartsWith("L\t", StringComparison.Ordinal) ? "OK" : inner.Send(request);
        public void Reset() => inner.Reset();
        public long WorkingSet => inner.WorkingSet;
        public void Dispose() => inner.Dispose();
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

/// <summary>
/// 英単語の混じった文の変換を 2 通りで比べる (MELTYPE_MOZC に Mozc の変換ヘルパーの場所が要る)。
///   今の変換: Meltype キーボードで打って Space で変換した結果 (英単語のところで文を切って、日本語の部分を別々に Mozc に渡す)
///   丸ごと:   英単語を仮の名詞 (えっくす) に置き換えた読みを、文ごと 1 回で Mozc に渡し、エックス を英単語に戻す
/// 入力ファイルは 1 行に「打ったもの&lt;Tab&gt;期待」。
/// </summary>
internal static class MozcCompare
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const string Placeholder = "えっくす";
    private const string PlaceholderText = "エックス";

    public static void Run(string input, string output)
    {
        var helper = Environment.GetEnvironmentVariable("MELTYPE_MOZC");
        if (helper is null || !File.Exists(helper)) throw new InvalidOperationException("MELTYPE_MOZC に Mozc の変換ヘルパーの場所を入れてください");
        var profile = Path.Combine(Path.GetTempPath(), $"meltype-mozc-compare-{Environment.ProcessId}");
        using var mozc = new Composition.MozcConverter(new Composition.MozcHelperProcess(helper, profile));
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
            var placeholders = System.Text.RegularExpressions.Regex.Count(text, PlaceholderText);
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
            Json));
        Console.WriteLine($"今の変換 {currentOk}/{results.Count}、丸ごと {wholeOk}/{results.Count}");
    }

    /// <summary>比べるときは空白と、英字の大文字・小文字を無視する (GitHub と github は同じとみなす)。</summary>
    private static string Normalize(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());
}
