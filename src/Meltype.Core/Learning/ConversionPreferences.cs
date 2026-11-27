// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Config;

namespace Meltype.Learning;

/// <summary>
/// 文脈の手がかりで変換を選ぶ規則 (dictionaries/contexts.txt)。
/// 形式は 1 行に「読み 候補 : 手がかり 手がかり …」。前後の文字列に手がかりが含まれていれば、その候補を最初にする
/// (気温 が近くにあれば あつい → 暑い、財布 なら かわ → 革)。変換エンジンだけでは文脈を読み切れない語を補う。
/// </summary>
internal sealed class ContextRules
{
    private readonly Dictionary<string, List<(string Candidate, string[] Cues)>> _rules = new(StringComparer.Ordinal);

    // 語の後ろに続く助詞・助動詞・活用語尾の頭の文字 (はし|を、あつい|ので、かく|けど、せんたく|してる)。
    // る・い・き・り などで始まる残りは語の続き (はし|る → 走る、かわ|いい、いし|き) なので補正しない。
    private const string EndingHeads = "をがはにでともへのやかなだけよねさしすせまたてぞぜわっじば";

    public int Count => _rules.Sum(r => r.Value.Count);

    public static ContextRules Load(string? userDirectory)
    {
        var rules = new ContextRules();
        rules.AddText(Libraries.Lexicon.DictionarySource.ReadEmbedded("contexts.txt"));
        Config.DataFile.LoadUserText(userDirectory, "contexts.txt", "ユーザーの文脈辞書", rules.AddText);
        return rules;
    }

    public void AddText(string text)
    {
        foreach (var line in Libraries.Lexicon.DictionarySource.LinesWithoutComments(text))
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

/// <summary>
/// ユーザーが選び直した変換の記録 (Microsoft IME の学習と同じ)。次に同じ読みを変換したとき最初の候補にする。
/// %LOCALAPPDATA%\Meltype\conversions.json に「文節の読み → 選んだ文字列」だけを保存する。
/// </summary>
internal sealed class ConversionHistory
{
    private const int MaxEntries = 5000;
    private readonly string? _path;
    private Dictionary<string, Entry> _entries;
    // 裏のスレッドからも消す (MeltypeRuntime.ForgetZenzaiHallucinations) ので、読むときも取る
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;
    private readonly FileStamp _stamp;
    private (int Version, (string Reading, string Text, DateTime Used, int Order)[] Items)? _index;

    internal sealed class Entry
    {
        public string Text { get; set; } = "";
        public DateTime Used { get; set; }
    }

    /// <summary>
    /// 覚える読みか。1 文字のかな・英字 (き → 記) を覚えると、関係ない変換 (き + ごうとう) まで巻き込むので 2 文字以上だけ。
    /// ただし数字・記号の 1 文字 (1 → ①、( → （) は、かなの途中に巻き込まれないので覚える (issue #672)。
    /// 読み込むときも同じ条件で選ぶ (違うと、覚えた 1 → ① が再起動で消える: issue #2267)。
    /// </summary>
    internal static bool IsLearnableReading(string reading) =>
        reading.Length >= 2 || reading.Length == 1 && reading[0] is < 'ぁ' or > 'ヿ' && !char.IsAsciiLetter(reading[0]);

    public ConversionHistory(string? path)
    {
        _saver = new DeferredSaver(Write);
        _path = path;
        _stamp = new FileStamp(path);
        _entries = new(StringComparer.Ordinal);
        Reload();
    }

    private static Dictionary<string, Entry>? Load(string? path, Config.FileStamp stamp)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var read = Config.DataFile.Load(path, "変換の学習データ", content =>
        {
            var loaded = JsonSerializer.Deserialize(content, Config.ConversionJsonContext.Default.Conversions);
            // 覚えない読み・日時や版から作る語 (StaleWords.IsStale) は、以前の版で覚えてしまったものも使わない。
            // 値・Text が null の読みは捨てる (手で書き換えたファイル・細工したバックアップ。System.Text.Json は辞書の値の null を拒まない)
            if (loaded is not null)
                foreach (var (reading, entry) in loaded)
                    if (entry?.Text is not null && IsLearnableReading(reading) && !StaleWords.IsStale(reading, entry.Text)) entries[reading] = entry;
        }, stamp);
        return read ? entries : null;
    }

    /// <summary>ほかのプロセス (設定アプリ) が conversions.json を書き換えていたら読み直す (<see cref="FileStamp"/>)。</summary>
    internal void Refresh()
    {
        lock (_fileGate)
        {
            if (_stamp.Changed) Reload();
        }
    }

    // _fileGate の中で呼ぶ。読む前に更新時刻を覚える (読んでいる間に書かれても、次にまた読み直す)
    private void Reload()
    {
        _stamp.Synced();
        // 読めなかったら前の中身のまま (Config.DataFile.Load)
        if (Load(_path, _stamp) is not { } entries) return;
        lock (_gate)
        {
            _entries = entries;
            Version++;
        }
    }

    /// <summary>学習した内容が変わるたびに増える (変換結果のキャッシュを捨てるため)。</summary>
    public int Version { get; private set; }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public string? Get(string reading)
    {
        lock (_gate) return _entries.TryGetValue(reading, out var entry) ? entry.Text : null;
    }

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
        (int Version, (string Reading, string Text, DateTime Used, int Order)[] Items) index;
        lock (_gate)
        {
            if (_index is not { } built || built.Version != Version)
            {
                var order = 0;
                var items = _entries.Select(e => (e.Key, e.Value.Text, e.Value.Used, order++)).ToArray();
                Array.Sort(items, (a, b) => string.CompareOrdinal(a.Key, b.Key));
                _index = built = (Version, items);
            }
            index = built;
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
    public IReadOnlyList<(string Reading, string Text, DateTime Used)> Entries()
    {
        lock (_gate) return _entries.OrderByDescending(e => e.Value.Used).Select(e => (e.Key, e.Value.Text, e.Value.Used)).ToList();
    }

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
                if (_stamp.Changed)
                {
                    Reload();
                    return true;
                }
                string json;
                lock (_gate) json = JsonSerializer.Serialize(_entries, Config.ConversionJsonContext.Default.Conversions);
                Config.DataFile.Write(_path, temp => File.WriteAllText(temp, json));
                _stamp.Synced();
            }
            return true;
        }
        catch (Exception ex) when (Config.DataFile.IsFileError(ex))
        {
            Diagnostics.Log.Warn($"変換の学習データを保存できませんでした: {ex.Message}");
            return false;
        }
    }
}
