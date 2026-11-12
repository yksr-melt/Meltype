// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.Json;

namespace Meltype.Tip;

/// <summary>
/// Meltype IME の impl (native/tip の MeltypeTipImpl.dll) を、この Meltype.exe に同梱した impl と照合する。
/// impl は、署名前のバイナリの SHA-256 (MeltypeTipImpl.dll.sha256) を要求の impl に付ける。
/// 別ビルドの impl とは通信しないので、Meltype.exe と impl のあいだのプロトコルに後方互換性は要らない。
/// </summary>
/// <param name="folder">この Meltype.exe に同梱した tip フォルダー (x64・x86 の下に MeltypeTipImpl.dll.sha256)。</param>
internal sealed class TipBuild(string folder)
{
    private static readonly string[] Architectures = ["x64", "x86"];

    public static TipBuild Shipped { get; } = new(Path.Combine(AppContext.BaseDirectory, "tip"));

    // アーキテクチャ → impl の SHA-256。tip フォルダーが無い Meltype.exe (dotnet run・テスト・ポータブル版) では空で、照合しない
    private readonly Dictionary<string, string> _hashes = Architectures
        .Select(arch => (arch, hash: ReadHash(Path.Combine(folder, arch))))
        .Where(pair => pair.hash is not null)
        .ToDictionary(pair => pair.arch, pair => pair.hash!);

    /// <summary>要求 (<see cref="TipRequest"/> の JSON) を送ってきた DLL を、同梱の impl と照合する。変換の結果を待つ要求 (wait) は照合しない。</summary>
    public TipClient Check(string request)
    {
        if (_hashes.Count == 0) return TipClient.Same;
        try
        {
            using var document = JsonDocument.Parse(request);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return TipClient.Other;
            if (root.TryGetProperty("wait", out _)) return TipClient.Same;
            // ---- 後方互換レイヤー (#1670) ここから。削除するときは、ここまでを消す (TipClient.LegacyBeforeSplit) ----
            if (!root.TryGetProperty("impl", out _)) return TipClient.LegacyBeforeSplit;
            // ---- 後方互換レイヤー (#1670) ここまで ----
            return root.TryGetProperty("impl", out var impl) && impl.ValueKind == JsonValueKind.String &&
                _hashes.Values.Contains(impl.GetString(), StringComparer.OrdinalIgnoreCase) ? TipClient.Same : TipClient.Other;
        }
        catch (JsonException)
        {
            return TipClient.Other;
        }
    }

    /// <summary>登録済みの stub (MeltypeTip.dll) がこれからロードする impl が、同梱の impl と同じビルドか。</summary>
    /// <param name="installed">登録済みの tip フォルダー (x64・x86 の下に MeltypeTip.dll と MeltypeTipImpl.dll.sha256)。</param>
    public bool IsInstalled(string installed)
    {
        return _hashes.All(pair => string.Equals(ReadHash(Path.Combine(installed, pair.Key)), pair.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadHash(string folder)
    {
        try
        {
            return File.ReadAllText(Path.Combine(folder, "MeltypeTipImpl.dll.sha256")).Trim() is { Length: > 0 } hash ? hash : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>要求を送ってきた DLL (<see cref="TipBuild.Check"/>)。</summary>
internal enum TipClient
{
    /// <summary>同梱の impl と同じビルドの impl。</summary>
    Same,

    /// <summary>別ビルドの impl。</summary>
    Other,

    /// <summary>
    /// 後方互換レイヤー (#1670)。削除する。
    /// stub と impl に分割する前の DLL (要求に impl が無い)。プロセスを再起動するまで差し替わらないので、今までどおり応答し、再起動を通知する。
    /// 応答しないと、分割前の版から更新したとき、更新前から起動しているアプリが再起動まで英字しか入力できない。
    /// 削除するときは、この値を消し、「後方互換レイヤー (#1670) ここから」から「ここまで」を消す (この値を使う塊はコンパイルエラーになる)。docs/dev/TSF_DESIGN.md の同じ目印の行も消す。
    /// 削除した後は、分割前の DLL には {"stale":true} を返し、そのアプリは再起動まで英字のまま入る (再起動の通知は出る)。
    /// </summary>
    LegacyBeforeSplit,
}
