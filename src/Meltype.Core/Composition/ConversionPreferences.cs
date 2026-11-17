// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 文脈の手がかりで変換を選ぶ規則 (dictionaries/contexts.txt)。
/// 形式は 1 行に「読み 候補 : 手がかり 手がかり …」。前後の文字列に手がかりが含まれていれば、その候補を最初にする
/// (気温 が近くにあれば あつい → 暑い、財布 なら かわ → 革)。変換エンジンだけでは文脈を読み切れない語を補う。
/// </summary>
public sealed class ContextRules
{
    private readonly Dictionary<string, List<(string Candidate, string[] Cues)>> _rules = new(StringComparer.Ordinal);

    // 語の後ろに続く助詞・助動詞・活用語尾の頭の文字 (はし|を、あつい|ので、かく|けど、せんたく|してる)。
    // る・い・き・り などで始まる残りは語の続き (はし|る → 走る、かわ|いい、いし|き) なので補正しない。
    private const string EndingHeads = "をがはにでともへのやかなだけよねさしすせまたてぞぜわっじば";

    public int Count => _rules.Sum(r => r.Value.Count);

    public static ContextRules Load(string? userDirectory)
    {
        var rules = new ContextRules();
        rules.AddText(Detection.DictionarySource.ReadEmbedded("contexts.txt"));
        if (userDirectory is not null)
        {
            var path = Path.Combine(userDirectory, "contexts.txt");
            try
            {
                if (File.Exists(path)) rules.AddText(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"ユーザーの文脈辞書を読めませんでした: {ex.Message}");
            }
        }
        return rules;
    }

    public void AddText(string text)
    {
        foreach (var line in Detection.DictionarySource.LinesWithoutComments(text))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var head = line[..colon].Split([' ', '\t', '　'], StringSplitOptions.RemoveEmptyEntries);
            var cues = line[(colon + 1)..].Split([' ', '\t', '\r', '　'], StringSplitOptions.RemoveEmptyEntries);
            if (head.Length != 2 || cues.Length == 0) continue;
            if (!_rules.TryGetValue(head[0], out var list)) _rules[head[0]] = list = [];
            list.Add((head[1], cues));
        }
    }

    /// <summary>
    /// 文節の読み (はしを) と変換エンジンの結果 (engineText: 橋を) に対して、前後の文字列 (surrounding) の手がかりに合う候補 (箸を) を返す。無ければ null。
    /// 辞書にある最長の先頭部分で探し、残り (を) はそのまま付ける。手がかりが多く当たった規則を優先する。
    /// 残りがあるときは、語の境界と活用を Meltype では判定せず、変換エンジンに確かめる (issue #348)。
    /// 変換エンジンの結果が「先頭部分の語 + 残りのかな」になっていて、その語が先頭部分の読みの変換 (同じ規則の候補か、
    /// convertStem で先頭部分だけを変換した結果) であるときだけ置き換える。
    /// 隠して は かく の変換ではないので 書くして に、変わった は かわ の変換ではないので 川った に、確認 は にん で終わらないので 書くにん にしない。
    /// </summary>
    public string? Choose(string reading, string surrounding, string engineText, Func<string, string?> convertStem)
    {
        if (surrounding.Length == 0) return null;
        for (var length = reading.Length; length >= 1; length--)
        {
            var head = reading[..length];
            if (!_rules.TryGetValue(head, out var list)) continue;
            var ending = reading[length..];
            if (ending.Length > 0)
            {
                if (!EndingHeads.Contains(ending[0]) || !engineText.EndsWith(ending, StringComparison.Ordinal)) return null;
                var stem = engineText[..^ending.Length];
                if (stem != head && !list.Any(rule => rule.Candidate == stem) && convertStem(head) != stem) return null;
            }
            var best = list
                .Select(rule => (rule.Candidate, Hits: rule.Cues.Count(cue => HasCue(surrounding, cue))))
                .Where(r => r.Hits > 0)
                .OrderByDescending(r => r.Hits)
                .FirstOrDefault();
            return best.Hits > 0 ? best.Candidate + ending : null;
        }
        return null;
    }

    /// <summary>
    /// 手がかりの端が英字なら、その端は英単語の境界でだけ当てる (ph を graph・php・iphone に当てない、AC を ACCESS に当てない)。
    /// かなや漢字の手がかりは部分一致のまま (蒸し → 蒸し暑い)。
    /// </summary>
    private static bool HasCue(string surrounding, string cue)
    {
        for (var at = surrounding.IndexOf(cue, StringComparison.Ordinal); at >= 0; at = surrounding.IndexOf(cue, at + 1, StringComparison.Ordinal))
        {
            var end = at + cue.Length;
            if (char.IsAsciiLetter(cue[0]) && at > 0 && char.IsAsciiLetter(surrounding[at - 1])) continue;
            if (char.IsAsciiLetter(cue[^1]) && end < surrounding.Length && char.IsAsciiLetter(surrounding[end])) continue;
            return true;
        }
        return false;
    }
}
