// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Reflection;
using System.Runtime.InteropServices;

namespace Meltype.Config;

/// <summary>Windows 版・Mac 版・Linux 版で共通の、プロジェクトの情報と不具合報告の URL。</summary>
public static class ProjectInfo
{
    public const string SourceUrl = "https://github.com/yksr-melt/Meltype";

    /// <summary>
    /// 不具合報告のフォーム (Google フォームの「事前入力した URL」。OS に Windows 11、版に 0.0.0、実行環境に ENV を入れて作ったもの)。
    /// 空なら GitHub の Issue の画面を開く。作り方は tools/report-form/README.md。
    /// </summary>
    public const string ReportForm = "";

    /// <summary>Meltype.Core の版 (Mac 版・Linux 版の版)。</summary>
    public static string CoreVersion =>
        typeof(ProjectInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    /// <summary>公開した版 (1.0.0 以降) か。それまではリポジトリが非公開なので、GitHub で報告する道は出さない。</summary>
    public static bool IsPublicRelease(string version) => Version.TryParse(version, out var parsed) && parsed.Major >= 1;

    /// <summary>
    /// 不具合報告を開く URL。os はフォームの OS の選択肢 (Windows 11 / Mac (プレビュー版) / Linux (プレビュー版))。
    /// フォームがまだ無ければ GitHub の Issue の画面 (公開前はリポジトリが非公開なので、協力者だけが開ける)。
    /// </summary>
    public static string ReportUrl(string os, string version, string environment)
    {
        if (ReportForm.Length == 0) return $"{SourceUrl}/issues/new/choose";
        return ReportForm
            .Replace("=Windows+11", "=" + Uri.EscapeDataString(os))
            .Replace("=0.0.0", "=" + Uri.EscapeDataString(version))
            .Replace("=ENV", "=" + Uri.EscapeDataString(environment));
    }

    /// <summary>
    /// GitHub の Issue の作成画面を、OS・版・実行環境を入れた状態で開く URL。
    /// template は .github/ISSUE_TEMPLATE のファイル名 (1-bug.yml / 2-misdetection.yml / 4-idea.yml)。欄は id で入れる。
    /// </summary>
    public static string GitHubReportUrl(string template, string os, string version, string environment) =>
        $"{SourceUrl}/issues/new?template={Uri.EscapeDataString(template)}&os={Uri.EscapeDataString(os)}" +
        $"&version={Uri.EscapeDataString(version)}&environment={Uri.EscapeDataString(environment)}";

    /// <summary>
    /// 自分で直した誤判定を、「変換・判定の間違い」のひな形 (2-misdetection.yml) に入れた状態で開く URL (issue #284)。
    /// 欄の id (kind・typed・actual・expected・key・context) は、ひな形と合わせる。
    /// </summary>
    public static string MisdetectionReportUrl(Composition.Correction correction, string os, string version, string environment) =>
        GitHubReportUrl("2-misdetection.yml", os, version, environment) +
        $"&title={Uri.EscapeDataString("[誤判定] " + correction.Typed)}" +
        $"&kind={Uri.EscapeDataString(correction.Kind)}" +
        $"&typed={Uri.EscapeDataString(correction.Typed)}" +
        $"&actual={Uri.EscapeDataString(correction.Shown)}" +
        $"&expected={Uri.EscapeDataString(correction.Corrected)}" +
        $"&key={Uri.EscapeDataString(correction.LastKey)}" +
        $"&context={Uri.EscapeDataString(correction.Before)}";

    /// <summary>Mac 版・Linux 版の実行環境 (不具合報告に入れる。入力した文字は入れない)。</summary>
    public static string Environment(Settings settings, string platform) => string.Join("\n",
    [
        $"Meltype: {CoreVersion} ({platform}、プレビュー版)",
        $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
        $".NET: {System.Environment.Version}",
        $"自動判定の強さ: {settings.DetectionLevel}",
        $"入力方式: {settings.InputStyle}",
        $"ライブ変換: {(settings.LiveConversion ? "ON" : "OFF")}",
        $"言語: {System.Globalization.CultureInfo.CurrentUICulture.Name}",
    ]);
}
