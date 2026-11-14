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
            Report("前後も見る: 語の形 + 直前のキーワード・記号 + 直後の記号", code, romaji, t => Context(t, Classify));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Report(string name, List<Token> code, List<Token> romaji, Func<Token, bool> japanese)
    {
        var wrongCode = code.Where(japanese).ToList();
        var missedRomaji = romaji.Where(t => !japanese(t)).ToList();
        Console.WriteLine($"## {name}");
        Console.WriteLine($"コードの語を日本語にした: {wrongCode.Count} / {code.Count} ({Percent(wrongCode.Count, code.Count)})");
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

    private static bool Context(Token t, Func<string, bool, Verdict> classify)
    {
        // 記号は語に接しているときだけ見る (「// 」「# 」の後のコメント・「"」で始まる文字列には日本語を書く)
        var before = t.Before.TrimEnd();
        if (t.Before.Length > 0 && ".(=,:<[{/\\$@-&|!*+>%?".Contains(t.Before[^1])) return false;
        if (PreviousWord().Match(before) is { Success: true } previous && Keywords.Contains(previous.Groups[1].Value)) return false;
        if (t.After.Length > 0 && "(._=:[<-;,)]{'\"`/".Contains(t.After[0])) return false;
        if (t.After.TrimStart().StartsWith('-') || t.After.TrimStart().StartsWith('=')) return false;
        return Shape(t, classify);
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
