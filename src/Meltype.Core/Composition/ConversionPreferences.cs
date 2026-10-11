// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Composition;

/// <summary>
/// 文脈の手がかりで変換を選ぶ規則 (dictionaries/contexts.txt)。
/// 形式は 1 行に「読み 候補 : 手がかり 手がかり …」。前後の文字列に手がかりが含まれていれば、その候補を最初にする
/// (気温 が近くにあれば あつい → 暑い、財布 なら かわ → 革)。変換エンジンだけでは文脈を読み切れない語を補う。
/// </summary>
public sealed class ContextRules
{
    private readonly Dictionary<string, List<(string Candidate, string[] Cues)>> _rules = new(StringComparer.Ordinal);

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
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
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
    /// 文節の読み (はしを) に対して、前後の文字列 (surrounding) の手がかりに合う候補 (箸を) を返す。無ければ null。
    /// 辞書にある最長の先頭部分で探し、残り (を) はそのまま付ける。手がかりが多く当たった規則を優先する。
    /// </summary>
    public string? Choose(string reading, string surrounding)
    {
        if (surrounding.Length == 0) return null;
        for (var length = reading.Length; length >= 1; length--)
        {
            if (!_rules.TryGetValue(reading[..length], out var list)) continue;
            var best = list
                .Select(rule => (rule.Candidate, Hits: rule.Cues.Count(cue => surrounding.Contains(cue, StringComparison.Ordinal))))
                .Where(r => r.Hits > 0)
                .OrderByDescending(r => r.Hits)
                .FirstOrDefault();
            return best.Hits > 0 ? best.Candidate + reading[length..] : null;
        }
        return null;
    }
}

/// <summary>
/// ユーザーが選び直した変換の記録 (Microsoft IME の学習と同じ)。次に同じ読みを変換したとき最初の候補にする。
/// %LOCALAPPDATA%\Meltype\conversions.json に「文節の読み → 選んだ文字列」だけを保存する。
/// </summary>
public sealed class ConversionHistory
{
    private const int MaxEntries = 5000;
    private readonly string? _path;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal sealed class Entry
    {
        public string Text { get; set; } = "";
        public DateTime Used { get; set; }
    }

    public ConversionHistory(string? path)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), Config.ConversionJsonContext.Default.Conversions);
            // 1 文字の読み (き → 記) は、以前の版で覚えてしまったものも使わない (関係ない変換を巻き込むため)。
            if (loaded is not null) foreach (var (reading, entry) in loaded) if (reading.Length >= 2) _entries[reading] = entry;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"変換の学習データを読めませんでした: {ex.Message}");
        }
    }

    /// <summary>学習した内容が変わるたびに増える (変換結果のキャッシュを捨てるため)。</summary>
    public int Version { get; private set; }

    public int Count => _entries.Count;

    public string? Get(string reading) => _entries.TryGetValue(reading, out var entry) ? entry.Text : null;

    public void Remember(string reading, string text)
    {
        if (reading.Length == 0 || text.Length == 0) return;
        _entries[reading] = new Entry { Text = text, Used = DateTime.UtcNow };
        if (_entries.Count > MaxEntries)
        {
            foreach (var old in _entries.OrderBy(e => e.Value.Used).Take(_entries.Count - MaxEntries * 9 / 10).Select(e => e.Key).ToList())
            {
                _entries.Remove(old);
            }
        }
        Version++;
        Save();
    }

    public void Forget(string reading)
    {
        if (!_entries.Remove(reading)) return;
        Version++;
        Save();
    }

    /// <summary>学習した変換 (読み → 選んだ語) の一覧 (新しく使ったものから)。設定の「学習した語」に出す。</summary>
    public IReadOnlyList<(string Reading, string Text, DateTime Used)> Entries() =>
        _entries.OrderByDescending(e => e.Value.Used).Select(e => (e.Key, e.Value.Text, e.Value.Used)).ToList();

    public void Clear()
    {
        _entries.Clear();
        Version++;
        Save();
    }

    /// <summary>sinceUtc より後に覚えた (使った) 変換を忘れる (トレイの「最近の学習を消す」)。忘れた数を返す。</summary>
    public int ForgetSince(DateTime sinceUtc)
    {
        var recent = _entries.Where(e => e.Value.Used >= sinceUtc).Select(e => e.Key).ToList();
        if (recent.Count == 0) return 0;
        foreach (var reading in recent) _entries.Remove(reading);
        Version++;
        Save();
        return recent.Count;
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Config.ConversionJsonContext.Default.Conversions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"変換の学習データを保存できませんでした: {ex.Message}");
        }
    }
}
