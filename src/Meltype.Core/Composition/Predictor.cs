// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Composition;

/// <summary>
/// 確定した語句の記録 (読み → 確定した文字列と回数)。予測変換で、打ちかけの読みから続きを出すのに使う
/// (きょうは まで打つと、前に確定した「今日は天気がいい」を出す)。
/// 1 行に「読み<Tab>語句<Tab>回数<Tab>最後に使った日時 (UTC の Ticks)」のテキストで保存する。
/// </summary>
public sealed class PhraseHistory
{
    private const int MaxEntries = 3000;
    private readonly string? _path;
    private readonly Dictionary<(string Reading, string Text), (int Count, long Used)> _entries = new();

    public PhraseHistory(string? path)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var parts = line.Split('\t');
                if (parts.Length < 4 || parts[0].Length == 0 || parts[1].Length == 0) continue;
                if (!int.TryParse(parts[2], out var count) || !long.TryParse(parts[3], out var used)) continue;
                _entries[(parts[0], parts[1])] = (Math.Max(1, count), used);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"予測変換の学習データを読めませんでした: {ex.Message}");
        }
    }

    public int Count => _entries.Count;

    /// <summary>確定した語句を覚える。読みが 3 文字未満・読みのまま (かなのまま) 確定したものは覚えない。</summary>
    public void Remember(string reading, string text)
    {
        if (reading.Length < 3 || text.Length == 0 || text == reading || text.Contains('\t') || text.Contains('\n')) return;
        var key = (reading, text);
        var count = _entries.TryGetValue(key, out var old) ? old.Count + 1 : 1;
        _entries[key] = (count, DateTime.UtcNow.Ticks);
        if (_entries.Count > MaxEntries)
        {
            foreach (var stale in _entries.OrderBy(e => e.Value.Used).Take(_entries.Count - MaxEntries * 9 / 10).Select(e => e.Key).ToList())
            {
                _entries.Remove(stale);
            }
        }
        Save();
    }

    /// <summary>読みが prefix で始まり、prefix より長い語句 (よく使う順 → 新しい順)。</summary>
    public IEnumerable<string> StartingWith(string prefix) =>
        _entries.Where(e => e.Key.Reading.Length > prefix.Length && e.Key.Reading.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(e => e.Value.Count).ThenByDescending(e => e.Value.Used)
            .Select(e => e.Key.Text);

    /// <summary>覚えている語句の読みのうち、typed で始まるもの (予測の候補を確定したときに、同じ読みで覚え直すため)。</summary>
    public string? ReadingOf(string text, string typed) =>
        _entries.Keys.Where(k => k.Text == text && k.Reading.StartsWith(typed, StringComparison.Ordinal)).Select(k => k.Reading).FirstOrDefault();

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    /// <summary>sinceUtc より後に覚えた (確定した) 語句を忘れる (トレイの「最近の学習を消す」)。忘れた数を返す。</summary>
    public int ForgetSince(DateTime sinceUtc)
    {
        var recent = _entries.Where(e => e.Value.Used >= sinceUtc.Ticks).Select(e => e.Key).ToList();
        if (recent.Count == 0) return 0;
        foreach (var key in recent) _entries.Remove(key);
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
            File.WriteAllLines(temp, _entries.Select(e => $"{e.Key.Reading}\t{e.Key.Text}\t{e.Value.Count}\t{e.Value.Used}"), Encoding.UTF8);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"予測変換の学習データを保存できませんでした: {ex.Message}");
        }
    }
}

/// <summary>
/// 予測変換の候補 (issue #38)。打っている途中の読み・英字から、続きの候補を出す。
///   日本語: 前に確定した語句 (PhraseHistory) → ユーザー辞書 → 選び直した変換の学習 の、読みが前方一致する語
///   英語: よく使う英単語 (english.txt) → 同梱の英単語の一覧 (english-words.txt) の、前方一致する語 (短い順)
/// </summary>
public sealed class Predictor
{
    /// <summary>出す候補の数。</summary>
    public const int MaxPredictions = 5;

    /// <summary>この文字数から予測する (短いと候補が多すぎて当たらない)。</summary>
    public const int MinKanaLength = 2, MinEnglishLength = 3;

    private readonly PhraseHistory? _phrases;
    private readonly UserDictionary? _userDictionary;
    private readonly ConversionHistory? _history;
    private readonly Lazy<(string[] Common, string[] All)> _english;

    public Predictor(PhraseHistory? phrases, UserDictionary? userDictionary, ConversionHistory? history)
    {
        _phrases = phrases;
        _userDictionary = userDictionary;
        _history = history;
        _english = new(() =>
        {
            static string[] Sorted(IEnumerable<string> words) =>
                words.Select(w => w.Trim()).Where(w => w.Length > 0 && w.All(char.IsAsciiLetterLower)).Distinct().Order(StringComparer.Ordinal).ToArray();
            var common = Sorted(Detection.DictionarySource.Load("english.txt", null));
            var all = Sorted(Detection.DictionarySource.ReadEmbedded("english-words.txt").Split('\n').Where(l => !l.StartsWith('#')));
            return (common, all);
        });
    }

    public PhraseHistory? Phrases => _phrases;

    /// <summary>打ちかけの読み (ひらがな) の続きの候補。</summary>
    public IReadOnlyList<string> PredictJapanese(string kana)
    {
        if (kana.Length < MinKanaLength) return [];
        var result = new List<string>();
        void Add(IEnumerable<string> words)
        {
            foreach (var word in words)
            {
                if (result.Count >= MaxPredictions) return;
                if (word.Length > 0 && !result.Contains(word)) result.Add(word);
            }
        }
        if (_phrases is not null) Add(_phrases.StartingWith(kana));
        if (_userDictionary is not null)
            Add(_userDictionary.Words.Where(w => w.Reading.Length > kana.Length && w.Reading.StartsWith(kana, StringComparison.Ordinal)).Select(w => w.Word));
        if (_history is not null)
            Add(_history.Entries().Where(e => e.Reading.Length > kana.Length && e.Reading.StartsWith(kana, StringComparison.Ordinal)).Select(e => e.Text));
        return result;
    }

    /// <summary>打ちかけの英単語の続きの候補 (decis → decision, decisions …)。打った大文字 (先頭・すべて) に合わせる。</summary>
    public IReadOnlyList<string> PredictEnglish(string raw)
    {
        if (raw.Length < MinEnglishLength || !raw.All(char.IsAsciiLetter)) return [];
        var lower = raw.ToLowerInvariant();
        var (common, all) = _english.Value;
        var words = StartingWith(common, lower).Concat(StartingWith(all, lower))
            .Where(w => w.Length > lower.Length).Distinct()
            // よく使う英単語の一覧のものを先に、それぞれ短い順
            .Select((w, i) => (Word: w, Common: Array.BinarySearch(common, w, StringComparer.Ordinal) >= 0, Index: i))
            .OrderByDescending(w => w.Common).ThenBy(w => w.Word.Length).ThenBy(w => w.Index)
            .Take(MaxPredictions)
            .Select(w => MatchCase(w.Word, raw));
        return words.ToList();
    }

    private static IEnumerable<string> StartingWith(string[] sorted, string prefix)
    {
        var index = Array.BinarySearch(sorted, prefix, StringComparer.Ordinal);
        if (index < 0) index = ~index;
        for (var i = index; i < sorted.Length && sorted[i].StartsWith(prefix, StringComparison.Ordinal); i++) yield return sorted[i];
    }

    private static string MatchCase(string word, string typed)
    {
        if (typed.Length > 1 && typed.All(char.IsAsciiLetterUpper)) return word.ToUpperInvariant();
        return char.IsAsciiLetterUpper(typed[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : word;
    }
}
