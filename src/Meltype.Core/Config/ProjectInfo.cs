// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Reflection;
using System.Runtime.InteropServices;

namespace Meltype.Config;

/// <summary>Windows 版・Mac 版・Linux 版で共通の、プロジェクトの情報と不具合報告の URL。</summary>
public static class ProjectInfo
{
    public const string SourceUrl = "https://github.com/yksr-melt/Meltype";

    /// <summary>Meltype.Core の版 (Mac 版・Linux 版の版)。</summary>
    public static string CoreVersion =>
        typeof(ProjectInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    /// <summary>
    /// タグの無い自己ビルドのコミット ID (先頭 7 文字)。リリースのビルドは版にコミット ID を付けない (src/Directory.Build.props) ので null。
    /// </summary>
    public static string? BuildCommit =>
        typeof(ProjectInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+') is [_, var commit, ..] ? commit[..Math.Min(7, commit.Length)] : null;

    /// <summary>.NET SDK が付けた版 (1.5.1+<コミット ID 全体>) のコミット ID を、先頭 7 文字にする (1.5.1+abc1234)。コミット ID が無ければそのまま。</summary>
    public static string ShortCommit(string version) =>
        version.Split('+') is [var number, var commit, ..] ? $"{number}+{commit[..Math.Min(7, commit.Length)]}" : version;

    /// <summary>版にタグの無い自己ビルドのコミット ID を付けたもの (1.5.1+abc1234)。不具合の報告と「ばーじょん」の候補で同じ版の文字列にする (issue #1868)。</summary>
    public static string WithCommit(string version) => BuildCommit is { } commit ? $"{version}+{commit}" : version;

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
    /// 初めて起動したとき (新しく入れたとき) は、更新ではないので見せない。autoShow が OFF なら見せない。
    /// </summary>
    public static bool ShouldShowReleaseNotes(string? shownVersion, string currentVersion, bool firstRun, bool autoShow = true) =>
        autoShow && !firstRun && currentVersion != "?" && shownVersion != currentVersion;

    /// <summary>
    /// GitHub の Issue の作成画面を、OS・版・実行環境を入れた状態で開く URL。
    /// template は .github/ISSUE_TEMPLATE のファイル名 (1-bug.yml / 2-misdetection.yml / 4-idea.yml)。欄は id で入れる。
    /// ひな形を選ぶ画面 (issues/new/choose) を開くと、選んだあとの画面に URL の値が引き継がれず空欄になる (issue #877)。
    /// </summary>
    public static string GitHubReportUrl(string template, string os, string version, string environment) =>
        $"{SourceUrl}/issues/new?template={Uri.EscapeDataString(template)}&os={Uri.EscapeDataString(os)}" +
        $"&version={Uri.EscapeDataString(version)}&environment={Uri.EscapeDataString(environment)}";

    /// <summary>
    /// 自分で直した誤判定を、「変換・判定の間違い」のひな形 (2-misdetection.yml) に入れた状態で開く URL (issue #284)。
    /// 欄の id (typed・actual・expected・key・zenzai・context) は、ひな形と合わせる。
    /// Zenzai の欄は、直したときの「Zenzai を使う」の設定 (Correction.Zenzai) で入れる。分からなければ空のまま (issue #1197)。
    /// </summary>
    public static string MisdetectionReportUrl(Meltype.Diagnostics.Correction correction, string os, string version, string environment) =>
        GitHubReportUrl("2-misdetection.yml", os, version, environment) +
        $"&title={Uri.EscapeDataString("[誤判定] " + correction.Typed)}" +
        $"&typed={Uri.EscapeDataString(correction.Typed)}" +
        $"&actual={Uri.EscapeDataString(correction.Shown)}" +
        $"&expected={Uri.EscapeDataString(correction.Corrected)}" +
        $"&key={Uri.EscapeDataString(correction.LastKey)}" +
        (correction.Zenzai is { } on ? $"&zenzai={Uri.EscapeDataString(on ? "使っている" : "使っていない")}" : "") +
        $"&context={Uri.EscapeDataString(correction.Before)}";

    /// <summary>
    /// 実行環境 (不具合報告に入れる。1 行ずつ「項目: 値」。入力した文字は入れない)。version は「Meltype:」の値、os は「OS:」の値。
    /// osLines は、その OS でだけ分かる項目 (Windows の動作モード・画面など)。
    /// </summary>
    public static string Environment(Settings settings, string version, string os, IEnumerable<string>? osLines = null) => string.Join("\n",
    [
        $"Meltype: {version}",
        $"OS: {os}",
        $".NET: {System.Environment.Version}",
        .. osLines ?? [],
        $"Zenzai: {(settings.UseZenzai ? "ON" : "OFF")}",
        $"自動判定: {(settings.AutoDetection ? "ON" : "OFF")}",
        $"入力方式: {settings.InputStyle}",
        $"ライブ変換: {(settings.LiveConversion ? "ON" : "OFF")}",
        $"言語: {System.Globalization.CultureInfo.CurrentUICulture.Name}",
    ]);
}
