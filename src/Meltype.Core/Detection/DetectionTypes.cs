// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Detection;

public enum Verdict
{
    /// <summary>まだ判断材料が足りない。保留を続ける。</summary>
    Undecided,
    Japanese,
    English,
    /// <summary>判断できない。何もしない (設計書 §17)。</summary>
    Unknown,
}

/// <summary>
/// 判定対象。Letters は打鍵を英小文字に直したもの (英字以外のキーは '#')、
/// Keys は仮想キーコード列 (かな入力判定用)。
/// </summary>
internal sealed record DetectionInput(string Letters, IReadOnlyList<int> Keys, bool IsFinal);

/// <summary>各 Detector の加点。1 つの判定器が直接 Japanese/English を決めることはない (設計書 §12)。</summary>
public readonly record struct Contribution(string Source, int Japanese, int English, string Reason)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Japanese != 0) parts.Add($"JP{Japanese:+#;-#}");
        if (English != 0) parts.Add($"EN{English:+#;-#}");
        return $"{Source}({string.Join(' ', parts)}: {Reason})";
    }
}

public sealed record DetectionResult(
    Verdict Verdict,
    string Text,
    int JapaneseScore,
    int EnglishScore,
    IReadOnlyList<Contribution> Contributions,
    string Summary)
{
    /// <summary>
    /// ログ用の説明。入力した文字をログに出さない設定 (既定) なら、打った文字と、理由の中の打った文字 (「kyou」と一致 など) は出さない。
    /// </summary>
    public string Describe() =>
        $"{Verdict} {Diagnostics.Log.Text(Text)} JP={JapaneseScore} EN={EnglishScore} {Summary}" +
        (Contributions.Count > 0 ? " | " + string.Join(", ", Contributions.Select(c => Diagnostics.Log.RecordText ? c.ToString() : $"{c.Source}(JP{c.Japanese:+#;-#;0} EN{c.English:+#;-#;0})")) : "");
}
