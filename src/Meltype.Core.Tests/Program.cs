// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
namespace Meltype.Tests;

/// <summary>
/// OS に依存しない部分 (Meltype.Core) のテスト。Windows 以外 (Mac・Linux) でも動く。
///   dotnet run --project src/Meltype.Core.Tests                 … すべてのテスト
///   dotnet run --project src/Meltype.Core.Tests -- Composition  … 名前に一致するテストだけ
///   dotnet run --project src/Meltype.Core.Tests -- --eval       … 品質テストの正解率と外れた例
///   dotnet run --project src/Meltype.Core.Tests -- --version    … 版 (Meltype.Core)
///   dotnet run --project src/Meltype.Core.Tests -- --explain konnichiwa hello … 1 文字ずつの判定理由
/// Windows では src/Meltype.Tests から実行すると、Windows のスペルチェッカーを使い、Windows 専用のテストもまとめて流す。
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "--version":
                Console.WriteLine($"Meltype {Meltype.Config.ProjectInfo.CoreVersion}");
                return 0;
            case "--eval":
                Quality.Print(Quality.Run());
                return 0;
            case "--repro":
                // 打ったキーをそのまま打ってみて、結果を JSON で返す (GitHub の bot が報告を再現するのに使う)。
                //   --repro nihongowohanasu [enter|space|none]
                Repro.Type(args.ElementAtOrDefault(1) ?? "", args.ElementAtOrDefault(2) ?? "enter");
                return 0;
            case "--segments":
                // 調べる用: 打った文字の単位と、英語 / 日本語の区間分け (Enter の前) と、区間分けのラティスの道と重み
                {
                    var text = new Meltype.Composition.CompositionText(CompositionTests.Detector);
                    foreach (var c in args.ElementAtOrDefault(1) ?? "") text.Append(c);
                    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
                    Console.WriteLine("units: " + string.Join(" ", text.Units.Select(u => $"{u.Raw}={u.Kana}")) + (text.Pending.Length > 0 ? $" +{text.Pending}" : ""));
                    CompositionTests.Detector.LatticeTrace = Console.WriteLine;
                    var segments = text.Segments();
                    CompositionTests.Detector.LatticeTrace = null;
                    Console.WriteLine("segments: " + string.Join(" | ", segments.Select(s => (s.IsEnglish ? "EN:" : "JA:") + s.Raw)));
                }
                return 0;
            case "--eval-json":
                Checks.EvalJson(args[1]);
                return 0;
            case "--judge-corpus":
                // --judge-corpus 出力ファイル [ほかの一覧 …]: よく使う語の判定の一覧 (Pull Request のチェックで main と比べる。issue #1196)
                Impact.JudgeCorpus(args[1], args.Skip(2));
                return 0;
            case "--mixed-bench":
                // --mixed-bench 出力ファイル: 日本語の文の中に英単語を入れて打ち、英単語と日本語に正しく分かれるかを数える (区切りの点数化の試作の比較用)
                Checks.MixedBench(args[1]);
                return 0;
            case "--mozc-compare":
                // --mozc-compare 入力ファイル 出力ファイル: 今の変換と、文を丸ごと Mozc に渡す変換を比べる (MELTYPE_MOZC が要る)
                MozcCompare.Run(args[1], args[2]);
                Henkan.Shutdown();
                return 0;
            case "--perf":
                // --perf 出力ファイル [例文のファイル] [回数]: 変換エンジンの遅延・段階ごとの時間・候補の数・控えの使い回し・メモリ・正解率 (docs/PERFORMANCE.md)
                Performance.Measure(args[1], args.ElementAtOrDefault(2), args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : Performance.DefaultSamples);
                Henkan.Shutdown();
                return 0;
            case "--perf-compare":
                // --perf-compare 前.json 後.json: --perf の結果を 2 つ比べる
                Performance.Compare(args[1], args[2]);
                return 0;
            case "--expect":
                // --expect 入力ファイル 出力ファイル (Pull Request のチェック用)
                Checks.Expect(args[1], args[2]);
                return 0;
            case "--henkan":
                Henkan.Run(string.Join(" ", args.Skip(1)));
                Henkan.Shutdown();
                return 0;
            case "--jht":
                // --jht 出てほしい文 [/ 読み] (japanese-henkan-test。精度の調べもの用)
                {
                    var text = string.Join(" ", args.Skip(1));
                    var slash = text.IndexOf(" / ", StringComparison.Ordinal);
                    Jht.Run(slash >= 0 ? text[..slash] : text, slash >= 0 ? text[(slash + 3)..] : null, null, Henkan.Keyboard, Henkan.EngineName);
                    Henkan.Shutdown();
                }
                return 0;
            case "--jht-batch":
                // 標準入力から 1 行に 1 つ「出てほしい文 [/ 読み]」を受け取り、1 行ずつ結果を返す (多くの文を続けて試すとき。起動の待ちを省く)
                Jht.Batch(null, Henkan.Keyboard, () => Henkan.EngineName);
                Henkan.Shutdown();
                return 0;
            case "--dictionary-memory":
                // 組み込み辞書ごとの、読み込む前と後のマネージドヒープの差と読み込みの時間
                DictionaryMemory.Measure();
                return 0;
            case "--dictionary-dump":
                // --dictionary-dump 出力ファイル: 組み込み辞書の引き方の答えを書き出す (辞書の持ち方を変える前と後で diff する)
                DictionaryMemory.Dump(args[1]);
                return 0;
            case "--dictionary-bench":
                // --dictionary-bench [回数]: 1 打鍵あたりの時間と割り当て
                DictionaryMemory.Bench(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 15);
                return 0;
            case "--dictionary-lookup-bench":
                // --dictionary-lookup-bench [回数]: 意味・英訳を直接引いたときの 1 検索あたりの時間と割り当て
                DictionaryMemory.LookupBench(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 7);
                return 0;
            case "--explain":
                if (Environment.GetEnvironmentVariable("MELTYPE_UTF8") == "1") Console.OutputEncoding = new System.Text.UTF8Encoding(false);
                TestHost.Explain(args.Skip(1));
                return 0;
            default:
                return TestHost.Run([typeof(Program).Assembly], args.FirstOrDefault());
        }
    }
}
