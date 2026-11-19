// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Composition.Utility;

/// <summary>
/// ばーじょん に、実行中の Meltype の版・フロント・Zenzai の設定 (Meltype-1.5.0 (Windows / Meltype IME、Zenzai: ON)) を出す (issue #959・#1868)。
/// 不具合の報告にそのまま貼れるように、タグの無い自己ビルドではコミット ID も付け (Meltype-1.5.0+abc1234)、
/// Windows に登録されている Meltype IME が別のビルドなら、そのことと Meltype IME のビルドも付ける。
/// </summary>
internal sealed class VersionConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading == "ばーじょん" ? new([Candidate(input.Context.Settings, input.Context.Front())], UtilityPlacement.Append) : null;

    internal static string Candidate(Settings settings, VersionFront front) =>
        Text(ProjectInfo.WithCommit(ProjectInfo.CoreVersion), Os, front, settings.UseZenzai);

    internal static string Text(string version, string os, VersionFront front, bool zenzai) => string.Concat(
        $"Meltype-{version} ({os}",
        front.Name is { } name ? $" / {name}" : "",
        $"、Zenzai: {(zenzai ? "ON" : "OFF")}",
        // Meltype IME の DLL の版は Meltype の版と別で、DLL を変えたときだけ上がる (native/tip/MeltypeTip.rc) ので、どのビルドかは impl のハッシュで示す
        front.OtherTipBuild is { } tip ? $"、Meltype IME は別のビルド: {tip[..Math.Min(7, tip.Length)]}" : "",
        ")");

    private static string Os =>
        OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsMacOS() ? "macOS"
        : OperatingSystem.IsLinux() ? "Linux"
        : "?";
}

/// <summary>
/// 「ばーじょん」の候補に出す、フロントのこと。Name はフロントの名前 (OS にフロントが 1 つしかなければ null)、
/// OtherTipBuild は Windows に登録されている Meltype IME の別のビルド (<see cref="Ime.IImeEnvironment.OtherTipBuild"/>)。
/// </summary>
internal sealed record VersionFront(string? Name, string? OtherTipBuild);
