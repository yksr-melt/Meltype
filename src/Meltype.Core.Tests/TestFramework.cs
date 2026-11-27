// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Reflection;

namespace Meltype.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestAttribute : Attribute;

internal sealed class AssertionException(string message) : Exception(message);

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new AssertionException(message);
    }

    public static void Equal<T>(T expected, T actual, string message = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{message} 期待値: {expected} / 実際: {actual}".Trim());
    }
}

/// <summary>[Test] の付いた static メソッドをすべて実行する最小限のテストランナー (NuGet が使えないので xUnit の代わり)。</summary>
internal static class TestHost
{
    public static int Run(IEnumerable<Assembly> assemblies, string? filter)
    {
        // MozcConverterTests が、この実行ファイルを偽の Mozc の変換ヘルパーとして起動したとき
        if (filter is not null && Path.GetFileName(filter) == MozcConverterTests.FakeHelperProfile) return MozcConverterTests.RunFakeHelper();
        var tests = assemblies.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .Where(m => filter is null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();

        var failed = 0;
        foreach (var test in tests)
        {
            var name = $"{test.DeclaringType!.Name}.{test.Name}";
            try
            {
                test.Invoke(null, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}\n        {ex.InnerException.Message.Replace("\n", "\n        ")}");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>1 文字ずつの判定とその理由を表示する (辞書・閾値の調整用)。</summary>
    public static void Explain(IEnumerable<string> words)
    {
        var engine = TestSupport.CreateEngine();
        foreach (var word in words)
        {
            for (var i = 1; i <= word.Length; i++)
            {
                var prefix = word[..i];
                var result = engine.Evaluate(new Detection.DetectionInput(prefix, prefix.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), i == word.Length));
                Console.WriteLine(result.Describe());
                if (result.Verdict != Detection.Verdict.Undecided) break;
            }
            Console.WriteLine();
        }
    }
}
