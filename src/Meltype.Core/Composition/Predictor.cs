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
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;
    private int _version;
    private (int Version, (string Reading, string Text, int Count, long Used, int Order)[] Items)? _index;

    public PhraseHistory(string? path)
    {
        _saver = new DeferredSaver(Write);
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
        lock (_gate)
        {
            var count = _entries.TryGetValue(key, out var old) ? old.Count + 1 : 1;
            _entries[key] = (count, DateTime.UtcNow.Ticks);
            if (_entries.Count > MaxEntries)
            {
                foreach (var stale in _entries.OrderBy(e => e.Value.Used).Take(_entries.Count - MaxEntries * 9 / 10).Select(e => e.Key).ToList())
                {
                    _entries.Remove(stale);
                }
            }
            _version++;
        }
        _saver.Request();
    }

    /// <summary>読みが prefix で始まり、prefix より長い語句 (よく使う順 → 新しい順)。</summary>
    public IEnumerable<string> StartingWith(string prefix)
    {
        // 読みで並べた索引から、前方一致する範囲だけを見る (並びは、全部を順に見て安定に並べ替えたときと同じ)
        if (_index is not { } index || index.Version != _version)
        {
            var order = 0;
            var items = _entries.Select(e => (e.Key.Reading, e.Key.Text, e.Value.Count, e.Value.Used, order++)).ToArray();
            Array.Sort(items, (a, b) => string.CompareOrdinal(a.Reading, b.Reading));
            _index = index = (_version, items);
        }
        int low = 0, high = index.Items.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (string.CompareOrdinal(index.Items[middle].Reading, prefix) < 0) low = middle + 1;
            else high = middle;
        }
        var matches = new List<(string Text, int Count, long Used, int Order)>();
        for (var i = low; i < index.Items.Length && index.Items[i].Reading.StartsWith(prefix, StringComparison.Ordinal); i++)
        {
            if (index.Items[i].Reading.Length > prefix.Length) matches.Add((index.Items[i].Text, index.Items[i].Count, index.Items[i].Used, index.Items[i].Order));
        }
        return matches.OrderByDescending(m => m.Count).ThenByDescending(m => m.Used).ThenBy(m => m.Order).Select(m => m.Text);
    }

    /// <summary>覚えている語句の読みのうち、typed で始まるもの (予測の候補を確定したときに、同じ読みで覚え直すため)。</summary>
    public string? ReadingOf(string text, string typed) =>
        _entries.Keys.Where(k => k.Text == text && k.Reading.StartsWith(typed, StringComparison.Ordinal)).Select(k => k.Reading).FirstOrDefault();

    /// <summary>覚えている語句 text を、読みにかかわらずすべて忘れる (予測変換の候補を Delete で消す)。忘れた数を返す。</summary>
    public int Forget(string text)
    {
        List<(string Reading, string Text)> keys;
        lock (_gate)
        {
            keys = _entries.Keys.Where(k => k.Text == text).ToList();
            if (keys.Count == 0) return 0;
            foreach (var key in keys) _entries.Remove(key);
            _version++;
        }
        _saver.WriteNow();
        return keys.Count;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _version++;
        }
        _saver.WriteNow();
    }

    /// <summary>sinceUtc より後に覚えた (確定した) 語句を忘れる (トレイの「最近の学習を消す」)。忘れた数を返す。</summary>
    public int ForgetSince(DateTime sinceUtc)
    {
        List<(string Reading, string Text)> recent;
        lock (_gate)
        {
            recent = _entries.Where(e => e.Value.Used >= sinceUtc.Ticks).Select(e => e.Key).ToList();
            if (recent.Count == 0) return 0;
            foreach (var key in recent) _entries.Remove(key);
            _version++;
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
                List<string> lines;
                lock (_gate) lines = _entries.Select(e => $"{e.Key.Reading}\t{e.Key.Text}\t{e.Value.Count}\t{e.Value.Used}").ToList();
                Config.DataFile.Write(_path, temp => File.WriteAllLines(temp, lines, Encoding.UTF8));
            }
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"予測変換の学習データを保存できませんでした: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// 予測変換の候補 (issue #38)。打っている途中の読み・英字から、続きの候補を出す。
///   日本語: 前に確定した語句 (PhraseHistory) → ユーザー辞書 → 選び直した変換の学習 → 同梱の定型句 (greetings.txt、issue #1213) の、読みが前方一致する語
///   英語: よく使う英単語 (english.txt) → 同梱の英単語の一覧 (english-words.txt) の、前方一致する語 (短い順)
///   英字の名前 (設定が ON のとき、issue #890): 社名・サービス名 (brands.txt) → 英字で書く語 (loanwords.txt) の、読みが前方一致する語 (しゃお → Xiaomi)
/// </summary>
public sealed class Predictor
{
    /// <summary>出す候補の数。</summary>
    public const int MaxPredictions = 5;

    /// <summary>この文字数から予測する (短いと候補が多すぎて当たらない)。</summary>
    public const int MinKanaLength = 2, MinEnglishLength = 3;

    /// <summary>英字の名前を読みから予測する文字数 (2 文字だと あい → IIS, IaaS … のように当たらない候補が多すぎる)。</summary>
    public const int MinEnglishNameKanaLength = 3;

    private readonly PhraseHistory? _phrases;
    private readonly UserDictionary? _userDictionary;
    private readonly ConversionHistory? _history;
    private readonly Lazy<(string[] Common, string[] All)> _english;
    private readonly Lazy<EnglishName[]> _englishNames = new(LoadEnglishNames);
    private readonly Lazy<(string Reading, string Phrase, string Start)[]> _greetings = new(LoadGreetings);

    /// <summary>英字の名前の読みと語。Brand は社名・サービス名の辞書 (brands.txt) のもの、Order は辞書に書いた順。</summary>
    private readonly record struct EnglishName(string Reading, string Word, bool Brand, int Order);

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
            Add(_history.TextsStartingWith(kana));
        // 学習がなくても出す定型句 (おはよ → おはようございます)。出し始める読みまで打ったら出す (ほかの語の打ち始めでは出さない)。
        // 打ち終えた読みと同じ語句 (こんにちは) は、続きではないので出さない
        Add(_greetings.Value.Where(g => g.Reading.Length > kana.Length && g.Reading.StartsWith(kana, StringComparison.Ordinal)
            && kana.StartsWith(g.Start, StringComparison.Ordinal)).Select(g => g.Phrase));
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

    /// <summary>
    /// 打ちかけの読み (ひらがな) から、英字で書く社名・サービス名・語の候補 (しゃお → Xiaomi、ぎっとは → GitHub)。
    /// 社名・サービス名を先に、それぞれ読みの短い順。読みと同じ長さ (読みを打ち終わったもの) も出す。
    /// 読みで並べた一覧を二分探索するので、打つたびに全部を見ることはしない。
    /// </summary>
    public IReadOnlyList<string> PredictEnglishNames(string kana)
    {
        if (kana.Length < MinEnglishNameKanaLength) return [];
        var names = _englishNames.Value;
        var matches = new List<EnglishName>();
        for (var i = FirstReadingAtOrAfter(names, kana); i < names.Length && names[i].Reading.StartsWith(kana, StringComparison.Ordinal); i++) matches.Add(names[i]);
        return matches.OrderByDescending(n => n.Brand).ThenBy(n => n.Reading.Length).ThenBy(n => n.Order)
            .Select(n => n.Word).Distinct().Take(MaxPredictions).ToList();
    }

    /// <summary>読みが prefix 以上になる最初の位置 (読みは序数順に並べてある)。</summary>
    private static int FirstReadingAtOrAfter(EnglishName[] names, string prefix)
    {
        int low = 0, high = names.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (string.CompareOrdinal(names[mid].Reading, prefix) < 0) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    /// <summary>brands.txt・loanwords.txt (読み 英字 英字 …) から、英字を含む語だけを読みの順に並べる。</summary>
    private static EnglishName[] LoadEnglishNames()
    {
        var names = new List<EnglishName>();
        void Add(string file, bool brand)
        {
            foreach (var line in Detection.DictionarySource.LinesWithoutComments(Detection.DictionarySource.ReadEmbedded(file)))
            {
                var parts = line.Split([' ', '\t', '\r', '　'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                foreach (var word in parts.Skip(1))
                {
                    if (word.Any(char.IsAsciiLetter)) names.Add(new(parts[0], word, brand, names.Count));
                }
            }
        }
        Add("brands.txt", brand: true);
        Add("loanwords.txt", brand: false);
        return names.OrderBy(n => n.Reading, StringComparer.Ordinal).ThenBy(n => n.Order).ToArray();
    }

    /// <summary>greetings.txt (読み<Tab>語句<Tab>出し始める読み)。</summary>
    private static (string Reading, string Phrase, string Start)[] LoadGreetings() =>
        Detection.DictionarySource.LinesWithoutComments(Detection.DictionarySource.ReadEmbedded("greetings.txt"))
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(parts => parts.Length == 3 && parts.All(p => p.Length > 0))
            .Select(parts => (parts[0], parts[1], parts[2])).ToArray();

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
