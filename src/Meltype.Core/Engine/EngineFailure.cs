// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Text.Json;

namespace Meltype.Engine;

/// <summary>
/// 変換エンジン (Mozc の変換ヘルパー・Zenzai の llama-server) とのやり取りで、相手のせいで起きる例外。
/// 起動できない (Win32Exception)・終了した (InvalidOperationException・ObjectDisposedException)・パイプや通信が切れた (IOException・HttpRequestException)・
/// 時間切れ (TimeoutException・OperationCanceledException)・答えが壊れている (JsonException・FormatException)。
/// これ以外の例外はコードの誤りなので、捕まえずに背景の処理の一番外 (<see cref="LatestRequestWorker"/>) でログに書く。
/// </summary>
internal static class EngineFailure
{
    public static bool IsExpected(Exception ex) =>
        ex is IOException or Win32Exception or InvalidOperationException or ObjectDisposedException or TimeoutException or OperationCanceledException
            or HttpRequestException or JsonException or FormatException;
}
