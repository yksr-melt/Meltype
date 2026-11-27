// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Config;

namespace Meltype.Composition.Utility;

/// <summary>
/// ばーじょん に、実行中の Meltype の版・フロント・Zenzai の設定 (Meltype-1.5.0 (Windows / Meltype IME、Zenzai: ON)) を出す (issue #959・#1868)。
/// 不具合の報告にそのまま貼れるように、タグの無い自己ビルドではコミット ID も付け (Meltype-1.5.0+abc1234)、
/// Windows に登録されている Meltype IME が別のビルドなら、そのことと、その Meltype IME を同梱した Meltype の版も付ける。
/// </summary>
internal sealed class VersionConversion : IUtilityConversion
{
    public UtilityCandidates? Convert(UtilityInput input) =>
        input.Reading == "ばーじょん" ? UtilityCandidates.Rankable("version", [Candidate(input.Context.Settings, input.Context.OtherTipVersion())]) : null;

    internal static string Candidate(Settings settings, string? otherTipVersion) =>
        Text(ProjectInfo.WithCommit(ProjectInfo.CoreVersion), Front(settings), settings.UseZenzai, otherTipVersion);

    internal static string Text(string version, string front, bool zenzai, string? otherTipVersion) =>
        $"Meltype-{version} ({front}、Zenzai: {(zenzai ? "ON" : "OFF")}{(otherTipVersion is { } tip ? $"、Meltype IME は別の版: {tip}" : "")})";

    /// <summary>
    /// OS と入力のフロント。Windows は動かしている設定の動作モードで決まる (Meltype IME を使えなければ Meltype キーボードになっている)。
    /// Linux はプロセスのコマンドラインで決まる。macOS はフロントが 1 つなので OS だけにする。
    /// </summary>
    private static string Front(Settings settings) =>
        OperatingSystem.IsWindows() ? $"Windows / {(settings.Mode == InputMode.Tsf ? "Meltype IME" : "Meltype キーボード")}"
        : OperatingSystem.IsMacOS() ? "macOS"
        : OperatingSystem.IsLinux() ? (LinuxFront.Value is { } front ? $"Linux / {front}" : "Linux")
        : "?";

    /// <summary>
    /// Linux のフロント (IBus・fcitx5・端末)。どのフロントも Core を自分のプロセスに読み込むので、プロセスのコマンドライン
    /// (fcitx5 は fcitx5 の本体、IBus と端末は python3 で動かすスクリプト) の名前で分かる。プロセスの間は変わらないので、最初に 1 回だけ読む。
    /// 分からなければ (テストなど) null。
    /// </summary>
    private static readonly Lazy<string?> LinuxFront = new(() => LinuxFrontOf(File.ReadAllText("/proc/self/cmdline").Split('\0')));

    // コマンドラインに出るプログラムの名前 → フロントの名前
    private static readonly Dictionary<string, string> LinuxFronts = new()
    {
        ["fcitx5"] = "fcitx5",
        ["ibus-engine-meltype"] = "IBus",
        ["meltype-tty"] = "端末",
    };

    internal static string? LinuxFrontOf(IEnumerable<string> commandLine) =>
        commandLine.Select(argument => LinuxFronts.GetValueOrDefault(Path.GetFileName(argument))).FirstOrDefault(front => front is not null);
}
