// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Reflection;

namespace Meltype;

/// <summary>Meltype.exe の版と、報告・更新の URL。</summary>
internal static class AppInfo
{
    /// <summary>公開した版 (1.0.0 以降) か。それまではリポジトリが非公開なので、GitHub で報告する道は出さない。</summary>
    public static bool IsPublicRelease => Config.ProjectInfo.IsPublicRelease(Version);

    /// <summary>GitHub の Issue の作成画面を、OS・版・実行環境を入れた状態で開く URL (template は 1-bug.yml など)。</summary>
    public static string GitHubReportUrl(string template, string environment) =>
        // OS の選択肢は雛形ごとに違う (不具合は Windows 11 / 10、誤判定は Windows)
        Config.ProjectInfo.GitHubReportUrl(template, template.StartsWith("1-", StringComparison.Ordinal) ? Diagnostics.ReportInfo.OsName : "Windows", Version, environment);

    /// <summary>自分で直した誤判定を、誤判定のひな形に入れた状態で開く URL (issue #284)。</summary>
    public static string MisdetectionReportUrl(Meltype.Diagnostics.Correction correction, string environment) =>
        Config.ProjectInfo.MisdetectionReportUrl(correction, "Windows", Version, environment);

    /// <summary>自動更新で最新のリリースを見に行く GitHub のリポジトリ (公開されている必要がある)。</summary>
    public const string UpdateRepository = "yksr-melt/Meltype";

    public static string Version =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";
}
