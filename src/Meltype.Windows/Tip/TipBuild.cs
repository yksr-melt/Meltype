// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.Json;

namespace Meltype.Tip;

/// <summary>
/// Meltype IME の impl (native/tip の MeltypeTipImpl.dll) が、この Meltype.exe と一緒にビルドしたものかを見分ける。
/// impl は、署名する前の中身の SHA-256 (MeltypeTipImpl.dll.sha256) を要求の impl に付ける。
/// 違うビルドの impl とはやり取りしないので、Meltype.exe と impl のあいだのプロトコルは、前の版と互換にしなくてよい。
/// </summary>
/// <param name="folder">この Meltype.exe と一緒に配った tip フォルダー (x64・x86 の下に MeltypeTipImpl.dll.sha256)。</param>
internal sealed class TipBuild(string folder)
{
    private static readonly string[] Architectures = ["x64", "x86"];

    public static TipBuild Shipped { get; } = new(Path.Combine(AppContext.BaseDirectory, "tip"));

    // アーキテクチャ → impl の SHA-256。tip フォルダーが無い Meltype.exe (dotnet run・テスト・ポータブル版) では空で、何も見分けない
    private readonly Dictionary<string, string> _hashes = Architectures
        .Select(arch => (arch, hash: ReadHash(Path.Combine(folder, arch))))
        .Where(pair => pair.hash is not null)
        .ToDictionary(pair => pair.arch, pair => pair.hash!);

    /// <summary>要求 (<see cref="TipRequest"/> の JSON) を送ってきた impl が、同じビルドか。変換の結果を待つ要求 (wait) は見ない。</summary>
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

    /// <summary>stub (登録した MeltypeTip.dll) がこれから読み込む impl が、同じビルドか。</summary>
    /// <param name="stub">64 ビットの stub のパス (…\tip\x64\MeltypeTip.dll)。</param>
    public bool IsInstalled(string stub)
    {
        if (Path.GetDirectoryName(Path.GetDirectoryName(stub)) is not { } installed) return false;
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
