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

    /// <summary>公開した版 (1.0.0 以降。1.3.1-pre.3 などのプレリリースも) か。それまではリポジトリが非公開なので、GitHub で報告する道は出さない。</summary>
    public static bool IsPublicRelease(string version) => Version.TryParse(version.Split('-', 2)[0], out var parsed) && parsed.Major >= 1;

    /// <summary>
    /// 版 (1.3.1・1.3.1-pre.3) を比べる (Semantic Versioning の順。プレリリースは同じ数字の正式版より前)。
    /// left が新しければ正、同じなら 0、古ければ負。読めない版があれば null。packaging/update.ps1 の Compare-MeltypeVersion と同じ順。
    /// </summary>
    public static int? CompareVersions(string left, string right)
    {
        var a = left.Split('+')[0].Split('-', 2);
        var b = right.Split('+')[0].Split('-', 2);
        if (!Version.TryParse(a[0], out var x) || !Version.TryParse(b[0], out var y)) return null;
        var core = x.CompareTo(y);
        if (core != 0 || (a.Length == 1 && b.Length == 1)) return Math.Sign(core);
        if (a.Length == 1) return 1;
        if (b.Length == 1) return -1;
        var p = a[1].Split('.');
        var q = b[1].Split('.');
        for (var i = 0; i < Math.Min(p.Length, q.Length); i++)
        {
            var pn = long.TryParse(p[i], out var pv) && p[i].All(char.IsAsciiDigit);
            var qn = long.TryParse(q[i], out var qv) && q[i].All(char.IsAsciiDigit);
            var c = pn && qn ? pv.CompareTo(qv) : pn ? -1 : qn ? 1 : string.CompareOrdinal(p[i], q[i]);
            if (c != 0) return Math.Sign(c);
        }
        return Math.Sign(p.Length - q.Length);
    }

    /// <summary>プレリリースの版 (1.3.1-pre.3) か。</summary>
    public static bool IsPrerelease(string version) => version.Split('+')[0].Contains('-');

    /// <summary>その版のリリースのページ (リリースノート)。</summary>
    public static string ReleaseUrl(string version) => $"{SourceUrl}/releases/tag/v{Uri.EscapeDataString(version)}";

    /// <summary>すべてのリリースの一覧 (過去のリリースノート)。</summary>
    public const string ReleasesUrl = SourceUrl + "/releases";

    /// <summary>
    /// 更新したあとの初めての起動で、リリースノートを見せるか (issue #1207)。最後に見せた版と今の版が違えば見せる。
    /// 初めて起動したとき (新しく入れたとき) は、更新ではないので見せない。
    /// </summary>
    public static bool ShouldShowReleaseNotes(string? shownVersion, string currentVersion, bool firstRun) =>
        !firstRun && currentVersion != "?" && shownVersion != currentVersion;

    /// <summary>不具合報告のフォーム (ReportForm) があるか。無ければ GitHub の Issue の画面で報告してもらう。</summary>
    public static bool HasReportForm => ReportForm.Length > 0;

    /// <summary>
    /// 不具合報告を開く URL。os はフォームの OS の選択肢 (Windows 11 / Mac (プレビュー版) / Linux (プレビュー版))。
    /// フォームがまだ無ければ GitHub の「不具合の報告」のひな形 (1-bug.yml) を、OS・版・実行環境を入れた状態で開く。
    /// ひな形を選ぶ画面 (issues/new/choose) を開くと、選んだあとの画面に URL の値が引き継がれず空欄になる (issue #877)。
    /// </summary>
    public static string ReportUrl(string os, string version, string environment)
    {
        if (!HasReportForm) return GitHubReportUrl("1-bug.yml", os, version, environment);
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
    /// 欄の id (kind・typed・actual・expected・key・zenzai・context) は、ひな形と合わせる。
    /// Zenzai の欄は、直したときの「Zenzai を使う」の設定 (Correction.Zenzai) で入れる。分からなければ空のまま (issue #1197)。
    /// </summary>
    public static string MisdetectionReportUrl(Composition.Correction correction, string os, string version, string environment) =>
        GitHubReportUrl("2-misdetection.yml", os, version, environment) +
        $"&title={Uri.EscapeDataString("[誤判定] " + correction.Typed)}" +
        $"&kind={Uri.EscapeDataString(correction.Kind)}" +
        $"&typed={Uri.EscapeDataString(correction.Typed)}" +
        $"&actual={Uri.EscapeDataString(correction.Shown)}" +
        $"&expected={Uri.EscapeDataString(correction.Corrected)}" +
        $"&key={Uri.EscapeDataString(correction.LastKey)}" +
        (correction.Zenzai is { } on ? $"&zenzai={Uri.EscapeDataString(on ? "使っている" : "使っていない")}" : "") +
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
