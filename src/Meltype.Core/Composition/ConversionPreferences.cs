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
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;
    private (int Version, (string Reading, string Text, DateTime Used, int Order)[] Items)? _index;

    internal sealed class Entry
    {
        public string Text { get; set; } = "";
        public DateTime Used { get; set; }
    }

    public ConversionHistory(string? path)
    {
        _saver = new DeferredSaver(Write);
        _path = path;
        Config.DataFile.Load(path, "変換の学習データ", content =>
        {
            var loaded = JsonSerializer.Deserialize(content, Config.ConversionJsonContext.Default.Conversions);
            // 1 文字の読み (き → 記) は、以前の版で覚えてしまったものも使わない (関係ない変換を巻き込むため)。
            if (loaded is not null) foreach (var (reading, entry) in loaded) if (reading.Length >= 2) _entries[reading] = entry;
        });
    }

    /// <summary>学習した内容が変わるたびに増える (変換結果のキャッシュを捨てるため)。</summary>
    public int Version { get; private set; }

    public int Count => _entries.Count;

    public string? Get(string reading) => _entries.TryGetValue(reading, out var entry) ? entry.Text : null;

    public void Remember(string reading, string text)
    {
        if (reading.Length == 0 || text.Length == 0) return;
        lock (_gate)
        {
            _entries[reading] = new Entry { Text = text, Used = DateTime.UtcNow };
            if (_entries.Count > MaxEntries)
            {
                foreach (var old in _entries.OrderBy(e => e.Value.Used).Take(_entries.Count - MaxEntries * 9 / 10).Select(e => e.Key).ToList())
                {
                    _entries.Remove(old);
                }
            }
            Version++;
        }
        _saver.Request();
    }

    public void Forget(string reading)
    {
        lock (_gate)
        {
            if (!_entries.Remove(reading)) return;
            Version++;
        }
        _saver.WriteNow();
    }

    /// <summary>読みが prefix で始まり prefix より長い、学習した変換の語 (新しく使ったものから。Entries() から探すのと同じ順)。</summary>
    public IEnumerable<string> TextsStartingWith(string prefix)
    {
        if (_index is not { } index || index.Version != Version)
        {
            var order = 0;
            var items = _entries.Select(e => (e.Key, e.Value.Text, e.Value.Used, order++)).ToArray();
            Array.Sort(items, (a, b) => string.CompareOrdinal(a.Key, b.Key));
            _index = index = (Version, items);
        }
        var matches = new List<(string Text, DateTime Used, int Order)>();
        for (var i = LowerBound(index.Items, prefix); i < index.Items.Length && index.Items[i].Reading.StartsWith(prefix, StringComparison.Ordinal); i++)
        {
            if (index.Items[i].Reading.Length > prefix.Length) matches.Add((index.Items[i].Text, index.Items[i].Used, index.Items[i].Order));
        }
        return matches.OrderByDescending(m => m.Used).ThenBy(m => m.Order).Select(m => m.Text);
    }

    private static int LowerBound((string Reading, string Text, DateTime Used, int Order)[] items, string prefix)
    {
        int low = 0, high = items.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (string.CompareOrdinal(items[middle].Reading, prefix) < 0) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    /// <summary>学習した変換 (読み → 選んだ語) の一覧 (新しく使ったものから)。設定の「学習した語」に出す。</summary>
    public IReadOnlyList<(string Reading, string Text, DateTime Used)> Entries() =>
        _entries.OrderByDescending(e => e.Value.Used).Select(e => (e.Key, e.Value.Text, e.Value.Used)).ToList();

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Version++;
        }
        _saver.WriteNow();
    }

    /// <summary>sinceUtc より後に覚えた (使った) 変換を忘れる (トレイの「最近の学習を消す」)。忘れた数を返す。</summary>
    public int ForgetSince(DateTime sinceUtc)
    {
        List<string> recent;
        lock (_gate)
        {
            recent = _entries.Where(e => e.Value.Used >= sinceUtc).Select(e => e.Key).ToList();
            if (recent.Count == 0) return 0;
            foreach (var reading in recent) _entries.Remove(reading);
            Version++;
        }
        _saver.WriteNow();
        return recent.Count;
    }

    private bool Write()
    {
        if (_path is null) return true;
        try
        {
            lock (_fileGate)
            {
                string json;
                lock (_gate) json = JsonSerializer.Serialize(_entries, Config.ConversionJsonContext.Default.Conversions);
                Config.DataFile.Write(_path, temp => File.WriteAllText(temp, json));
            }
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"変換の学習データを保存できませんでした: {ex.Message}");
            return false;
        }
    }
}
