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

    /// <summary>要求 (<see cref="TipRequest"/> の JSON) を送ってきた impl が、同梱の impl と同じビルドか。変換の結果を待つ要求 (wait) は照合しない。</summary>
    public bool Matches(string request)
    {
        if (_hashes.Count == 0) return true;
        try
        {
            using var document = JsonDocument.Parse(request);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (root.TryGetProperty("wait", out _)) return true;
            return root.TryGetProperty("impl", out var impl) && impl.ValueKind == JsonValueKind.String &&
                _hashes.Values.Contains(impl.GetString(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
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
