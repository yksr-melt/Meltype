// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Text;
using Meltype.Config;
using Meltype.Libraries.Lexicon;

namespace Meltype.Learning;

/// <summary>
/// 確定した語句の記録 (読み → 確定した文字列と回数)。予測変換で、打ちかけの読みから続きを出すのに使う
/// (きょうは まで打つと、前に確定した「今日は天気がいい」を出す)。
/// 1 行に「読み<Tab>語句<Tab>回数<Tab>最後に使った日時 (UTC の Ticks)」のテキストで保存する。
/// </summary>
internal sealed class PhraseHistory
{
    private const int MaxEntries = 3000;
    private readonly string? _path;
    private Dictionary<(string Reading, string Text), (int Count, long Used)> _entries;
    // 裏のスレッドからも消す (MeltypeRuntime.ForgetZenzaiHallucinations) ので、読むときも取る
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;
    private readonly FileStamp _stamp;
    private int _version;
    private (int Version, (string Reading, string Text, int Count, long Used, int Order)[] Items)? _index;

    public PhraseHistory(string? path)
    {
        _saver = new DeferredSaver(Write);
        _path = path;
        _stamp = new FileStamp(path);
        _entries = [];
        Reload();
    }

    private static Dictionary<(string Reading, string Text), (int Count, long Used)>? Load(string? path, Config.FileStamp stamp)
    {
        var entries = new Dictionary<(string Reading, string Text), (int Count, long Used)>();
        var read = Config.DataFile.Load(path, "予測変換の学習データ", content =>
        {
            foreach (var line in content.ReplaceLineEndings("\n").Split('\n'))
            {
                var parts = line.Split('\t');
                if (parts.Length < 4 || parts[0].Length == 0 || parts[1].Length == 0) continue;
                if (!int.TryParse(parts[2], out var count) || !long.TryParse(parts[3], out var used)) continue;
                // 日時・版から作る語は、以前の版で覚えてしまったものを使わない
                if (StaleWords.IsStale(parts[0], parts[1])) continue;
                entries[(parts[0], parts[1])] = (Math.Max(1, count), used);
            }
        }, stamp);
        return read ? entries : null;
    }

    /// <summary>ほかのプロセス (設定アプリ) が phrases.txt を書き換えていたら読み直す (<see cref="FileStamp"/>)。</summary>
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
            _version++;
        }
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

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
        (int Version, (string Reading, string Text, int Count, long Used, int Order)[] Items) index;
        lock (_gate)
        {
            if (_index is not { } built || built.Version != _version)
            {
                var order = 0;
                var items = _entries.Select(e => (e.Key.Reading, e.Key.Text, e.Value.Count, e.Value.Used, order++)).ToArray();
                Array.Sort(items, (a, b) => string.CompareOrdinal(a.Reading, b.Reading));
                _index = built = (_version, items);
            }
            index = built;
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

    /// <summary>覚えている語句 (読みと語句) の一覧。</summary>
    public IReadOnlyList<(string Reading, string Text)> Entries()
    {
        lock (_gate) return _entries.Keys.ToList();
    }

    /// <summary>覚えている語句の読みのうち、typed で始まるもの (予測の候補を確定したときに、同じ読みで覚え直すため)。</summary>
    public string? ReadingOf(string text, string typed)
    {
        lock (_gate) return _entries.Keys.Where(k => k.Text == text && k.Reading.StartsWith(typed, StringComparison.Ordinal)).Select(k => k.Reading).FirstOrDefault();
    }

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

    /// <summary>読み reading で覚えた語句 text だけを忘れる。忘れたかを返す。</summary>
    public bool Forget(string reading, string text)
    {
        lock (_gate)
        {
            if (!_entries.Remove((reading, text))) return false;
            _version++;
        }
        _saver.WriteNow();
        return true;
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
                if (_stamp.Changed)
                {
                    Reload();
                    return true;
                }
                List<string> lines;
                lock (_gate) lines = _entries.Select(e => $"{e.Key.Reading}\t{e.Key.Text}\t{e.Value.Count}\t{e.Value.Used}").ToList();
                Config.DataFile.Write(_path, temp => File.WriteAllLines(temp, lines, Encoding.UTF8));
                _stamp.Synced();
            }
            return true;
        }
        catch (Exception ex) when (Config.DataFile.IsFileError(ex))
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
internal sealed class Predictor
{
    /// <summary>出す候補の数。</summary>
    public const int MaxPredictions = 5;

    /// <summary>この文字数から予測する (短いと候補が多すぎて当たらない)。</summary>
    public const int MinKanaLength = 2, MinEnglishLength = 3;

    /// <summary>英字の名前を読みから予測する文字数 (2 文字だと あい → IIS, IaaS … のように当たらない候補が多すぎる)。</summary>
    public const int MinEnglishNameKanaLength = 3;

    private readonly PhraseHistory? _phrases;
    private readonly Func<IEnumerable<(string Reading, string Word)>>? _userWords;
    private readonly ConversionHistory? _history;

    // 同梱辞書は入力ホストによらないので、最初に使うときだけ索引を作り、プロセス内で共有する。
    private static readonly Lazy<PackedWordSet> CommonEnglish = new(() => PackedWordSet.FromWords(DictionarySource.Load("english.txt", null)));
    private static readonly Lazy<EnglishName[]> EnglishNames = new(LoadEnglishNames);
    private static readonly Lazy<(string Reading, string Phrase, string Start)[]> Greetings = new(LoadGreetings);

    /// <summary>英字の名前の読みと語。Brand は社名・サービス名の辞書 (brands.txt) のもの、Order は辞書に書いた順。</summary>
    private readonly record struct EnglishName(string Reading, string Word, bool Brand, int Order);

    /// <param name="userWords">ユーザー辞書の語 (読み, 語)。呼ぶたびに今の語を返す。</param>
    internal Predictor(PhraseHistory? phrases, Func<IEnumerable<(string Reading, string Word)>>? userWords, ConversionHistory? history)
    {
        _phrases = phrases;
        _userWords = userWords;
        _history = history;
    }

    /// <summary>学習データを使わない予測 (ユーザー辞書・同梱の辞書だけ)。</summary>
    internal Predictor WithoutLearning() => new(null, _userWords, null);

    internal PhraseHistory? Phrases => _phrases;

    /// <summary>
    /// 版番号 (Meltype-1.4.0・Mozc-3.34.6239.102+24.11.oss) を含むか。
    /// 予測候補・学習には使わない (更新後は古い版になる。issue #1488)。
    /// </summary>
    internal static bool IsVersionText(string text)
    {
        foreach (var name in (ReadOnlySpan<string>)["Meltype-", "Mozc-"])
        {
            var i = text.IndexOf(name, StringComparison.Ordinal);
            if (i >= 0 && i + name.Length < text.Length && char.IsAsciiDigit(text[i + name.Length])) return true;
        }
        return false;
    }

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
                if (word.Length > 0 && !IsVersionText(word) && !result.Contains(word)) result.Add(word);
            }
        }
        if (_phrases is not null) Add(_phrases.StartingWith(kana));
        if (_userWords is not null)
            Add(_userWords().Where(w => w.Reading.Length > kana.Length && w.Reading.StartsWith(kana, StringComparison.Ordinal)).Select(w => w.Word));
        if (_history is not null)
            Add(_history.TextsStartingWith(kana));
        // 学習がなくても出す定型句 (おはよ → おはようございます)。出し始める読みまで打ったら出す (ほかの語の打ち始めでは出さない)。
        // 打ち終えた読みと同じ語句 (こんにちは) は、続きではないので出さない
        if (result.Count < MaxPredictions) Add(Greetings.Value.Where(g => g.Reading.Length > kana.Length && g.Reading.StartsWith(kana, StringComparison.Ordinal)
            && kana.StartsWith(g.Start, StringComparison.Ordinal)).Select(g => g.Phrase));
        return result;
    }

    /// <summary>打ちかけの英単語の続きの候補 (decis → decision, decisions …)。打った大文字 (先頭・すべて) に合わせる。</summary>
    public static IReadOnlyList<string> PredictEnglish(string raw)
    {
        if (raw.Length < MinEnglishLength || !raw.All(char.IsAsciiLetter)) return [];
        // 綴り順の一覧を小文字どうしで二分探索するので、打ったものの小文字を先に作る (候補は小文字だけ)
        Span<char> buffer = raw.Length <= 64 ? stackalloc char[64] : new char[raw.Length];
        var lower = buffer[..raw.Length];
        for (var i = 0; i < raw.Length; i++) lower[i] = char.ToLowerInvariant(raw[i]);
        var common = CommonEnglish.Value;
        // よく使う英単語の一覧のものを先に、それぞれ短い順 (同じ長さは一覧の順)。全部を並べ替えずに上位だけを取る
        var result = new List<string>(MaxPredictions);
        TakeMatching(result, common, lower, MaxPredictions, skip: null);
        if (result.Count < MaxPredictions)
            TakeMatching(result, DictionarySource.EnglishWords, lower, MaxPredictions - result.Count, skip: common);
        for (var i = 0; i < result.Count; i++) result[i] = MatchCase(result[i], raw);
        return result;
    }

    /// <summary>
    /// 打ちかけの読み (ひらがな) から、英字で書く社名・サービス名・語の候補 (しゃお → Xiaomi、ぎっとは → GitHub)。
    /// 社名・サービス名を先に、それぞれ読みの短い順。読みと同じ長さ (読みを打ち終わったもの) も出す。
    /// 読みで並べた一覧を二分探索するので、打つたびに全部を見ることはしない。
    /// </summary>
    public static IReadOnlyList<string> PredictEnglishNames(string kana)
    {
        if (kana.Length < MinEnglishNameKanaLength) return [];
        var names = EnglishNames.Value;
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
            foreach (var line in Libraries.Lexicon.DictionarySource.LinesWithoutComments(Libraries.Lexicon.DictionarySource.ReadEmbedded(file)))
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
        Libraries.Lexicon.DictionarySource.LinesWithoutComments(Libraries.Lexicon.DictionarySource.ReadEmbedded("greetings.txt"))
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(parts => parts.Length == 3 && parts.All(p => p.Length > 0))
            .Select(parts => (parts[0], parts[1], parts[2])).ToArray();

    /// <summary>prefix より長い一致を短い順 (同じ長さは一覧の順) に足す。skip の語は足さない。</summary>
    private static void TakeMatching(List<string> result, PackedWordSet sorted, ReadOnlySpan<char> prefix, int max, PackedWordSet? skip)
    {
        // 候補に残らなかった語を文字列にしないよう、索引の番号だけで並べる。
        Span<int> best = stackalloc int[MaxPredictions];
        var count = 0;
        for (var i = sorted.LowerBound(prefix); i < sorted.Count; i++)
        {
            var word = sorted.Word(i);
            if (!word.StartsWith(prefix, StringComparison.Ordinal)) break;
            if (word.Length <= prefix.Length || (count == max && word.Length >= sorted.Word(best[count - 1]).Length) ||
                word.ContainsAnyExceptInRange('a', 'z') || skip?.Contains(word) == true) continue;
            var at = Math.Min(count, max - 1);
            while (at > 0 && word.Length < sorted.Word(best[at - 1]).Length)
            {
                best[at] = best[at - 1];
                at--;
            }
            best[at] = i;
            count = Math.Min(count + 1, max);
        }
        foreach (var index in best[..count]) result.Add(sorted.Word(index).ToString());
    }

    private static string MatchCase(string word, string typed)
    {
        if (typed.Length > 1 && typed.All(char.IsAsciiLetterUpper)) return word.ToUpperInvariant();
        return char.IsAsciiLetterUpper(typed[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : word;
    }
}
