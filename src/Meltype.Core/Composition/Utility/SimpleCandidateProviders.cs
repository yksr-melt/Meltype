// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text.RegularExpressions;
using Clause = Meltype.Composition.CompositionController.Clause;

namespace Meltype.Composition;

/// <summary>
/// じこう / じこうのあいさつ の文節に、今の月に合う時候の挨拶 (秋冷の候 / 拝啓　秋冷の候、…) を候補として出す (issue #339)。
/// 今の月を先に、続けて前後の月のものも出す。
/// </summary>
internal sealed class SeasonalGreetingProvider(CompositionOptions options) : ReadingCandidateMiddleware
{
    /// <summary>月ごとの時候の挨拶 (「〜の候」の〜)。手紙・メールの書き出しに使う。</summary>
    private static readonly string[][] SeasonalTerms =
    [
        ["新春", "初春", "厳寒", "大寒"],
        ["余寒", "立春", "春寒", "梅花"],
        ["早春", "春分", "春暖", "浅春"],
        ["桜花", "陽春", "春暖", "晩春"],
        ["新緑", "薫風", "若葉", "立夏"],
        ["入梅", "梅雨", "向暑", "初夏"],
        ["盛夏", "猛暑", "大暑", "酷暑"],
        ["晩夏", "残暑", "立秋", "暮夏"],
        ["初秋", "新秋", "秋晴", "野分"],
        ["秋冷", "紅葉", "秋涼", "寒露"],
        ["晩秋", "霜降", "向寒", "初霜"],
        ["初冬", "師走", "寒冷", "歳晩"],
    ];

    protected override IEnumerable<string> Candidates(string reading) =>
        reading is "じこう" or "じこうのあいさつ" ? SeasonalGreetings(options.Now().Month) : [];

    internal static IEnumerable<string> SeasonalGreetings(int month)
    {
        var months = new[] { month, (month + 10) % 12 + 1, month % 12 + 1 };
        var terms = months.SelectMany(m => SeasonalTerms[m - 1]).Distinct().ToList();
        return terms.Select(t => $"{t}の候").Concat([$"拝啓　{terms[0]}の候、"]);
    }
}

/// <summary>元号の年 (れいわ8ねん・へいせいがんねん) を西暦にした候補 (2026年 / 2026)。元号の範囲 (明治 〜 令和) の外なら null (issue #341)。</summary>
internal sealed class EraYearProvider : ReadingCandidateMiddleware
{
    private static readonly Regex EraReading =
        new("^(?<e>れいわ|へいせい|しょうわ|たいしょう|めいじ)(?<n>[0-9０-９]{1,2}|がん)ねん$");

    protected override IEnumerable<string> Candidates(string reading) => EraToYear(reading) ?? [];

    internal static IEnumerable<string>? EraToYear(string reading)
    {
        if (EraReading.Match(reading) is not { Success: true } match) return null;
        var number = match.Groups["n"].Value;
        var n = number == "がん" ? 1 : int.Parse(new string(number.Select(c => c is >= '０' and <= '９' ? (char)(c - '０' + '0') : c).ToArray()), CultureInfo.InvariantCulture);
        (int Start, int Last) range = match.Groups["e"].Value switch
        {
            "れいわ" => (2019, 2100),
            "へいせい" => (1989, 2019),
            "しょうわ" => (1926, 1989),
            "たいしょう" => (1912, 1926),
            _ => (1868, 1912),
        };
        var year = range.Start + n - 1;
        if (n < 1 || year > range.Last) return null;
        return [$"{year}年", year.ToString(CultureInfo.InvariantCulture)];
    }
}

/// <summary>数字 + 尺貫法の単位の読み (3しゃく) の文節に、単位の字 (3尺) と、メートル法に直した大きさ (約90.9cm) を出す (issue #340)。尺貫法でなければ null。</summary>
internal sealed class TraditionalUnitProvider : ReadingCandidateMiddleware
{
    /// <summary>尺貫法の単位 (読み → 単位の字、メートル法での大きさ、メートル法の単位)。</summary>
    private static readonly (string Reading, string Unit, double Size, string Metric)[] TraditionalUnits =
    [
        ("しゃく", "尺", 100.0 / 3.3, "cm"), ("すん", "寸", 10.0 / 3.3, "cm"), ("けん", "間", 200.0 / 110.0, "m"),
        ("じょう", "丈", 100.0 / 33.0, "m"), ("つぼ", "坪", 400.0 / 121.0, "㎡"),
        ("ごう", "合", 180.39, "mL"), ("しょう", "升", 1.8039, "L"), ("と", "斗", 18.039, "L"),
        ("かん", "貫", 3.75, "kg"), ("もんめ", "匁", 3.75, "g"), ("きん", "斤", 600, "g"), ("りょう", "両", 37.5, "g"),
    ];

    private static readonly Regex UnitReading =
        new("^(?<n>[0-9０-９]{1,6}(?:[.．][0-9０-９]{1,3})?)(?<u>しゃく|すん|けん|じょう|つぼ|ごう|しょう|と|かん|もんめ|きん|りょう)$");

    protected override IEnumerable<string> Candidates(string reading) => TypedUnitCandidates(reading) ?? [];

    internal static IEnumerable<string>? TypedUnitCandidates(string reading)
    {
        if (UnitReading.Match(reading) is not { Success: true } match) return null;
        var digits = new string(match.Groups["n"].Value.Select(c => c switch { >= '０' and <= '９' => (char)(c - '０' + '0'), '．' => '.', _ => c }).ToArray());
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        var unit = TraditionalUnits.First(u => u.Reading == match.Groups["u"].Value);
        var size = value * unit.Size;
        var text = size >= 100 ? Math.Round(size).ToString("0", CultureInfo.InvariantCulture)
            : Math.Round(size, 2).ToString("0.##", CultureInfo.InvariantCulture);
        return [$"{digits}{unit.Unit}", $"約{text}{unit.Metric}"];
    }
}

/// <summary>くりっぷぼーど・ぺーすと の文節に、クリップボードの文字を出す (設定で ON のときだけ)。</summary>
internal sealed class ClipboardCandidateProvider(CompositionOptions options, ICompositionHost host) : ReadingCandidateMiddleware
{
    private static readonly HashSet<string> Readings = ["くりっぷぼーど", "ぺーすと"];

    protected override IEnumerable<string> Candidates(string reading) =>
        Readings.Contains(reading) && options.ClipboardCandidates() && ClipboardLine(host.ClipboardText) is { } clip ? [clip] : [];

    /// <summary>クリップボードの文字が、候補に出してよい 1 行 (200 文字まで・制御文字なし) なら、その文字。前後の空白は除く。</summary>
    internal static string? ClipboardLine(string? text)
    {
        var line = text?.Trim();
        return line is { Length: > 0 and <= 200 } && !line.Any(char.IsControl) ? line : null;
    }
}

/// <summary>
/// 入力全体が計算式 (issue #295) か 月/日 (10/20、issue #935) なら、変換エンジンを使わずに、打ったままと結果を候補にする 1 つの文節を返す。
/// 月/日 は分数・割り算 (1/2) かもしれないので、打ったままと計算の結果より後ろに。全角 (１０／２０) は日付のときだけ最後に出す。
/// </summary>
internal sealed class RawExpressionProvider(CompositionOptions options) : IConversionMiddleware
{
    private static readonly Regex SlashReading = new("^(?<m>[0-9]{1,2})/(?<d>[0-9]{1,2})$");

    public List<Clause> Convert(ConversionRequest request, ConversionStep next)
    {
        var raw = request.Raw;
        if (raw.Length == 0) return next(request);
        string[] calculated = options.CalculatorCandidates() && Calculator.Evaluate(raw) is { } result
            ? [result, $"{raw.TrimEnd('=')}={result}"]
            : [];
        var dates = SlashReading.Match(raw) is { Success: true } date
            ? TypedDateCandidateProvider.DateCandidates(options, int.Parse(date.Groups["m"].Value, CultureInfo.InvariantCulture), int.Parse(date.Groups["d"].Value, CultureInfo.InvariantCulture)) ?? []
            : [];
        if (calculated.Length == 0 && dates.Length == 0) return next(request);
        string[] fullWidth = dates.Length > 0 ? [CompositionText.ToFullWidth(raw)] : [];
        var clause = new Clause(raw, true, [.. new[] { raw }.Concat(calculated).Concat(dates).Concat(fullWidth).Distinct()]) { Raw = raw };
        foreach (var text in dates) clause.Generated.Add(text);
        return [clause];
    }
}
