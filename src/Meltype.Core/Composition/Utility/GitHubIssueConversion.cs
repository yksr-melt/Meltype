// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Globalization;
using Meltype.Composition;

namespace Meltype.Composition.Utility;

/// <summary>
/// #123 の形は、# と数字を別々の文節にしない (issue #865)。
/// 設定「#番号で Issue・PR のタイトルを出す GitHub のリポジトリ」が書いてあれば、タイトルも出す (issue #291)。
/// </summary>
internal sealed class GitHubIssueConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input)
    {
        var raw = CompositionText.Raw(input.Text);
        if (input.Text.KanaInput || input.Text.Mode != DisplayMode.Auto || raw is not ['#', .. { Length: > 0 and <= 7 } digits] || !digits.All(char.IsAsciiDigit)) return null;
        var settings = input.Context.Settings;
        var repository = GitHubIssueTitles.Normalize(settings.GitHubRepository);
        List<string> candidates = [raw];
        if (input.Context.GitHubTitles?.Lookup(repository, int.Parse(digits, CultureInfo.InvariantCulture)) is { Length: > 0 } title)
            candidates.AddRange([$"{raw} {title}", $"{title} ({raw})", $"{repository}{raw}"]);
        candidates.Add(CompositionText.ToFullWidth(raw));
        return new(candidates, UtilityPlacement.Replace);
    }
}
