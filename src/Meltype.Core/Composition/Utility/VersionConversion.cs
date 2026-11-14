// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Composition.Utility;

/// <summary>
/// ばーじょん に、実行中の Meltype の版とフロント (Meltype-1.5.0 (Windows / Meltype IME)) を出す (issue #959・#1868)。
/// 不具合の報告にそのまま貼れるように、タグの無い自己ビルドではコミット ID も付ける (Meltype-1.5.0+abc1234)。
/// </summary>
internal sealed class VersionConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading == "ばーじょん" ? new([Candidate(input.Context.Settings)], UtilityPlacement.Append) : null;

    internal static string Candidate(Settings settings) => Text(ProjectInfo.WithCommit(ProjectInfo.CoreVersion), Front(settings));

    internal static string Text(string version, string front) => $"Meltype-{version} ({front})";

    /// <summary>
    /// OS と入力のフロント。Windows は設定の動作モードで決まる (フロントは 2 つあり、使うのは設定のほう)。
    /// Linux の IBus・fcitx5 は Core からは区別できないので OS だけにする。
    /// </summary>
    private static string Front(Settings settings) =>
        OperatingSystem.IsWindows() ? $"Windows / {(settings.Mode == InputMode.Tsf ? "Meltype IME" : "Meltype キーボード")}"
        : OperatingSystem.IsMacOS() ? "macOS"
        : OperatingSystem.IsLinux() ? "Linux"
        : "?";
}
