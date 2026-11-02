// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Composition;

/// <summary>
/// ふだんの文章では使わない漢字 (JIS 第 1・第 2 水準と Windows の機種依存文字 = CP932 に無い漢字: 禸)。
/// 変換エンジンがそれを含む候補を一番に出しても、ほかの候補を先にする (juubu → 禸部 にしない: issue #1315)。
/// </summary>
internal static class RareKanji
{
    private static readonly Lazy<Encoding?> Cp932 = new(() =>
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception)
        {
            return null;
        }
    });

    // 文字ごとの結果 (0 = まだ調べていない、1 = ふつう、2 = まれ)
    private static readonly byte[] Known = new byte[0x10000];

    /// <summary>まれな漢字か (CJK 統合漢字・拡張 A のうち CP932 で書けないもの)。調べられなければ false。</summary>
    public static bool IsRare(char c)
    {
        if (c is not ((>= '㐀' and <= '䶿') or (>= '一' and <= '鿿'))) return false;
        if (Known[c] != 0) return Known[c] == 2;
        if (Cp932.Value is not { } encoding) return false;
        bool rare;
        try
        {
            encoding.GetByteCount([c]);
            rare = false;
        }
        catch (EncoderFallbackException)
        {
            rare = true;
        }
        Known[c] = rare ? (byte)2 : (byte)1;
        return rare;
    }

    public static bool ContainsRare(string text) => text.Any(IsRare);

    /// <summary>一番目の候補がまれな漢字を含むときだけ、まれな漢字を含む候補をほかの候補の後ろに回す (順番はそれぞれのまま)。</summary>
    public static List<string> Demote(List<string> candidates)
    {
        if (candidates.Count < 2 || !ContainsRare(candidates[0])) return candidates;
        var common = candidates.Where(c => !ContainsRare(c)).ToList();
        if (common.Count == 0) return candidates;
        return [.. common, .. candidates.Where(ContainsRare)];
    }
}
