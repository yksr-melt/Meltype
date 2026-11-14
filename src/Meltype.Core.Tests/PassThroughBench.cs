// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.RegularExpressions;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// 素通し入力 (#1911) の spike: コードの英字の語と日本語のローマ字を、打った語だけで判定したときと、前後の文字も見て判定したときで比べる。
///   dotnet run --project src/Meltype.Core.Tests -- --pass-through-bench リポジトリのフォルダー
/// </summary>
internal static partial class PassThroughBench
{
    private sealed record Token(string Text, string Before, string After, string Source);

    private static readonly string[] CodeExtensions = [".cs", ".mjs", ".js", ".ts", ".py", ".sh", ".swift", ".cpp", ".h", ".ps1", ".rb", ".yml", ".nix"];

    // 直後に識別子が来るキーワード・コマンド
    private static readonly HashSet<string> Keywords =
    [
        "fn", "let", "const", "var", "def", "class", "new", "return", "import", "from", "use", "pub", "func", "struct", "type", "enum", "interface",
        "int", "string", "bool", "void", "long", "char", "float", "double", "static", "private", "public", "internal", "readonly", "async", "await",
        "if", "elif", "else", "for", "foreach", "while", "case", "in", "of", "is", "as", "not", "and", "or", "local", "export", "function", "echo",
        "sudo", "git", "cd", "ls", "cat", "rm", "cp", "mv", "mkdir", "npm", "npx", "node", "dotnet", "cargo", "nix", "python", "python3", "pip", "gh",
        "grep", "sed", "awk", "find", "make", "docker", "ssh", "systemctl", "set", "unset", "source", "then", "do", "done", "fi", "test",
    ];

    public static void Run(string root)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-bench-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var runtime = new MeltypeRuntime(directory);
            var settings = new Settings();
            Verdict Classify(string letters, bool final) => runtime.ClassifyDirect(letters, final, settings);

            var code = CodeTokens(root).ToList();
            var romaji = Impact.Readings().Select(Impact.ToRomaji).OfType<string>().Where(w => w.Length >= 2).Distinct()
                .Select(w => new Token(w, "", "", "readings.txt")).ToList();

            Report("今: 打ち始めから 1 文字ずつ判定", code, romaji, t => Incremental(t, Classify));
            Report("語の形だけ: 語を打ち終えてから判定 (_・数字・大文字を含む語は英字)", code, romaji, t => Shape(t, Classify));
            Report("語の形 + 直前の記号", code, romaji, t => !SymbolBefore(t) && Shape(t, Classify));
            Report("語の形 + 直前の記号・キーワード", code, romaji, t => Before(t, Classify));
            Report("語の形 + 直前の記号・キーワード + 直後の記号 (Space の後まで待つ)", code, romaji, t => Context(t, Classify));
            Report("案: 語の形 + 語に接した前の記号 (. $ - / \\ @) + 語を打ち終えた記号", code, romaji, t => Proposal(t, Classify));

            // 英語の文の語 (スペルチェッカーと同じ一覧なので、4 文字以上はスペルチェッカーが止める。止まらない 3 文字以下が要)
            var english = Impact.EnglishWords().Distinct().Select(w => new Token(w, "", "", "english-words.txt")).ToList();
            foreach (var (name, japanese) in new (string, Func<Token, bool>)[] { ("今", t => Incremental(t, Classify)), ("案", t => Proposal(t, Classify)) })
            {
                var wrong = english.Where(japanese).ToList();
                Console.WriteLine($"## 英単語を日本語にした ({name}): {wrong.Count} / {english.Count} ({Percent(wrong.Count, english.Count)})" +
                    $" うち 3 文字以下: {wrong.Count(t => t.Text.Length <= 3)}");
                Console.WriteLine("  " + string.Join(", ", wrong.Take(60).Select(t => t.Text)));
            }

            // 直前の文脈を付けたローマ字。日本語を書く場所 (コメント・文字列・プロンプト・日本語の文の続き) と、書かない場所
            Console.WriteLine("## 直前の文脈ごとの、ローマ字を英字のままにした割合 (語の形 + 直前の記号・キーワード)");
            foreach (var before in new[] { "", "// ", "# ", "\"", "x = \"", "> ", "今日は", "google ", "x = ", "(", "foo." })
            {
                var missed = romaji.Count(t => !Before(t with { Before = before }, Classify));
                var missedProposal = romaji.Count(t => !Proposal(t with { Before = before }, Classify));
                Console.WriteLine($"  {before,-10} {Percent(missed, romaji.Count)} 案: {Percent(missedProposal, romaji.Count)}");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static bool SymbolBefore(Token t) => t.Before.Length > 0 && ".(=,:<[{/\\$@-&|!*+>%?".Contains(t.Before[^1]);

    private static bool Before(Token t, Func<string, bool, Verdict> classify) =>
        !SymbolBefore(t) && !(PreviousWord().Match(t.Before) is { Success: true } previous && Keywords.Contains(previous.Groups[1].Value)) &&
        Shape(t, classify);

    private static void Report(string name, List<Token> code, List<Token> romaji, Func<Token, bool> japanese)
    {
        var wrongCode = code.Where(japanese).ToList();
        var missedRomaji = romaji.Where(t => !japanese(t)).ToList();
        // 文字列の中 (前に引用符が奇数個) の語は、識別子でなく文字列の中身 (日本語を書く場所でもある)
        var inString = wrongCode.Where(t => t.Before.Count(c => c is '"' or '\'' or '`') % 2 == 1).ToList();
        var identifiers = wrongCode.Except(inString).ToList();
        Console.WriteLine($"## {name}");
        Console.WriteLine($"コードの語を日本語にした: {wrongCode.Count} / {code.Count} ({Percent(wrongCode.Count, code.Count)})" +
            $" うち文字列の外: {identifiers.Count} ({Percent(identifiers.Count, code.Count)})");
        Console.WriteLine("  文字列の外の例: " + string.Join(" | ", identifiers.GroupBy(t => t.Text).OrderByDescending(g => g.Count()).Take(25)
            .Select(g => $"{g.First().Before.TrimStart()}[{g.Key}]{g.First().After}×{g.Count()}")));
        Console.WriteLine($"ローマ字を英字のままにした: {missedRomaji.Count} / {romaji.Count} ({Percent(missedRomaji.Count, romaji.Count)})");
        Console.WriteLine("日本語にしたコードの語 (多い順): " + string.Join(", ", wrongCode.GroupBy(t => t.Text).OrderByDescending(g => g.Count()).Take(40)
            .Select(g => $"{g.Key}×{g.Count()}")));
        Console.WriteLine("  例: " + string.Join(" | ", wrongCode.Take(15).Select(t => $"{t.Before}[{t.Text}]{t.After}")));
        Console.WriteLine("英字のままにしたローマ字: " + string.Join(", ", missedRomaji.Take(30).Select(t => t.Text)));
        Console.WriteLine();
    }

    private static string Percent(int count, int total) => total == 0 ? "-" : $"{100.0 * count / total:0.00}%";

    /// <summary>今の英数状態: 大文字で始まる語は英字。小文字の英字の続きを 1 文字ずつ判定し、決まらなければ打ち終わりで判定する。</summary>
    private static bool Incremental(Token t, Func<string, bool, Verdict> classify)
    {
        if (!char.IsAsciiLetterLower(t.Text[0])) return false;
        var letters = new string(t.Text.TakeWhile(char.IsAsciiLetterLower).ToArray());
        for (var i = 1; i <= letters.Length; i++)
        {
            var verdict = classify(letters[..i], i == letters.Length);
            if (verdict != Verdict.Undecided) return verdict == Verdict.Japanese;
        }
        return false;
    }

    private static bool Shape(Token t, Func<string, bool, Verdict> classify) =>
        t.Text.All(char.IsAsciiLetterLower) && classify(t.Text, true) == Verdict.Japanese;

    /// <summary>
    /// 判定は語を打ち終えたキーで決めるので、そのキー (After の先頭) までは使える。Space の後ろは使えない (Space は日本語なら変換に使う)。
    /// 前の記号は、文章には出てこないもの (メンバー・変数・オプション・パス) だけ。( や引用符の後には日本語を書く。
    /// </summary>
    private static bool Proposal(Token t, Func<string, bool, Verdict> classify) =>
        !(t.Before.Length > 0 && ".$-/\\@".Contains(t.Before[^1])) && !(t.After.Length > 0 && "(._=:[<{;,".Contains(t.After[0])) && Shape(t, classify);

    private static bool Context(Token t, Func<string, bool, Verdict> classify)
    {
        if (t.After.Length > 0 && "(._=:[<-;,)]{'\"`/".Contains(t.After[0])) return false;
        if (t.After.TrimStart().StartsWith('-') || t.After.TrimStart().StartsWith('=')) return false;
        return Before(t, classify);
    }

    private static IEnumerable<Token> CodeTokens(string root)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => CodeExtensions.Contains(Path.GetExtension(f)) && !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("/.git/") &&
                !f.Contains("/node_modules/"));
        foreach (var file in files)
        {
            foreach (var raw in File.ReadLines(file))
            {
                // コメント (日本語の文) は除く。コードの部分だけ
                var line = raw.Split("//")[0];
                if (line.TrimStart().StartsWith('#') && !file.EndsWith(".cs")) continue;
                foreach (Match m in Identifier().Matches(line))
                {
                    // 文字列の中の日本語の文の続きは除く (前に ASCII 以外がある行)
                    if (line.Any(c => c > 0x7F)) break;
                    yield return new Token(m.Value, line[..m.Index], line[(m.Index + m.Length)..], file);
                }
            }
        }
    }

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"([A-Za-z_][A-Za-z0-9_]*)\s+$")]
    private static partial Regex PreviousWord();
}
