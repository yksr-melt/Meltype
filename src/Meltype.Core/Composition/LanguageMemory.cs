// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Composition;

/// <summary>
/// 英語とも日本語とも読める語 (api, sushi …) を、ユーザーが自分で英字 / かなに直したときに覚えておく。
/// 次からその語は、自動の判定より覚えた方を優先する (api と打って F10 で英字にして確定 → 次から api は英字)。
/// %LOCALAPPDATA%\Meltype\languages.json に「打った英字 → 英語か」だけを保存する。
/// </summary>
public sealed class LanguageMemory
{
    private const int MaxEntries = 3000;
    private readonly string? _path;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;

    internal sealed class Entry
    {
        public bool English { get; set; }
        public DateTime Used { get; set; }

        /// <summary>同じ向きに直した回数 (前の版で保存したものは 0 = 1 回)。</summary>
        public int Count { get; set; }

        /// <summary>F10 / F6 などで、はっきり英字 / かなに直したか (変換の候補から選んだだけなら false)。</summary>
        public bool Explicit { get; set; }
    }

    /// <summary>学習した語 (設定の「学習した語」の一覧に出す)。Active は今有効になっているか (2 回目を待っている語は false)。</summary>
    public sealed record Learned(string Word, bool English, int Count, DateTime Used, bool Active, string? Key = null, string? Context = null)
    {
        /// <summary>Remove に渡す名前 (文脈ごとに分けて覚えた語は、印 @e / @j 付き)。</summary>
        public string EntryKey => Key ?? Word;
    }

    /// <summary>
    /// ローマ字としてよく使う日本語になる語か (kyouha = 今日は、sushi = すし)。こういう語は、一度英字にして確定しただけでは
    /// 英語として覚えない (2 回で覚える)。間違えて一度確定しただけで、ふつうの日本語がずっと英字になってしまうため。
    /// </summary>
    public Func<string, bool>? IsCommonJapanese { get; set; }

    /// <summary>
    /// ローマ字として読み切れる語か (go = ご)。こういう語を、変換の候補から英字を選んだだけで英語として覚えると、
    /// 日本語の中 (nihongo) まで英字になりやすいので、2 文字の語 (go・to・no) は、はっきり直したとき (F10) 以外は 2 回で覚える。
    /// </summary>
    public Func<string, bool>? IsReadableRomaji { get; set; }

    /// <summary>英語として覚えるのに 2 回要る語か。</summary>
    private bool NeedsTwice(string word, Entry entry) =>
        entry.English && Math.Max(1, entry.Count) < 2 &&
        (IsCommonJapanese?.Invoke(word) == true || (!entry.Explicit && word.Length <= 2 && IsReadableRomaji?.Invoke(word) == true));

    public LanguageMemory(string? path)
    {
        _saver = new DeferredSaver(Write);
        _path = path;
        Config.DataFile.Load(path, "英語 / 日本語の学習データ", content =>
        {
            var loaded = JsonSerializer.Deserialize(content, Config.LearningJsonContext.Default.Languages);
            if (loaded is not null) foreach (var (word, entry) in loaded) _entries[word] = entry;
        });
    }

    public int Count => _entries.Count;

    /// <summary>学習を前の文脈 (英語の後か日本語の後か) ごとに分けるか (設定「英語 / 日本語の学習を前の文脈ごとに分ける」: issue #491)。</summary>
    public bool ContextSensitive { get; set; }

    /// <summary>今の入力の前が英語か (true)、日本語か (false)、分からないか (null)。</summary>
    public bool? Context { get; set; }

    /// <summary>文脈ごとに分けるときの、語の後ろにつける印 (分けないときは空)。</summary>
    private string ContextSuffix => ContextSensitive && Context is { } english ? (english ? "@e" : "@j") : "";

    /// <summary>覚えている内容が変わるたびに増える (判定の結果を使い回してよいかを見るのに使う)。</summary>
    public int Version { get; private set; }

    /// <summary>覚えている語なら英語か (true) 日本語か (false)。覚えていなければ null。word は小文字の英字。</summary>
    public bool? Get(string word)
    {
        if (ContextSuffix is { Length: > 0 } suffix && _entries.TryGetValue(word + suffix, out var contextual))
            return NeedsTwice(word, contextual) ? null : contextual.English;
        if (!_entries.TryGetValue(word, out var entry)) return null;
        if (NeedsTwice(word, entry)) return null;
        return entry.English;
    }

    internal bool? Get(ReadOnlySpan<char> word)
    {
        if (ContextSuffix is { Length: > 0 }) return Get(word.ToString());
        if (!_entries.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(word, out var entry)) return null;
        if (NeedsTwice(word.ToString(), entry)) return null;
        return entry.English;
    }

    /// <summary>
    /// ユーザーが英字 / かなに直した語を覚える (2 文字以上の英字だけ)。
    /// explicitChoice は F10 / F6 / Tab ではっきり直したとき (変換の候補から選んだだけなら false)。
    /// </summary>
    public void Remember(string word, bool english, bool explicitChoice = false)
    {
        word = word.ToLowerInvariant();
        if (word.Length < 2 || !word.All(char.IsAsciiLetterLower)) return;
        lock (_gate) RememberLocked(word, english, explicitChoice);
        _saver.Request();
    }

    private void RememberLocked(string word, bool english, bool explicitChoice)
    {
        Version++;
        var entryKey = word + ContextSuffix;
        if (_entries.TryGetValue(entryKey, out var old) && old.English == english)
        {
            old.Used = DateTime.UtcNow;
            var waiting = NeedsTwice(word, old);
            old.Count = Math.Max(1, old.Count) + 1;
            old.Explicit |= explicitChoice;
            if (waiting) Diagnostics.Log.Decision($"{Diagnostics.Log.Text(word)}は次から英語にします (2 回目の学習)。");
        }
        else
        {
            var entry = new Entry { English = english, Used = DateTime.UtcNow, Count = 1, Explicit = explicitChoice };
            _entries[entryKey] = entry;
            Diagnostics.Log.Decision(NeedsTwice(word, entry)
                ? $"{Diagnostics.Log.Text(word)}はローマ字としても読めるので、もう一度英字にして確定したら英語にします (学習)。"
                : $"{Diagnostics.Log.Text(word)}は次から{(english ? "英語" : "日本語")}にします (学習)。");
        }
        if (_entries.Count > MaxEntries)
        {
            foreach (var key in _entries.OrderBy(e => e.Value.Used).Take(_entries.Count - MaxEntries * 9 / 10).Select(e => e.Key).ToList())
            {
                _entries.Remove(key);
            }
        }
    }

    /// <summary>学習した語の一覧 (新しく使ったものから)。</summary>
    public IReadOnlyList<Learned> Entries() =>
        _entries.OrderByDescending(e => e.Value.Used)
            .Select(e => new Learned(WithoutContext(e.Key), e.Value.English, Math.Max(1, e.Value.Count), e.Value.Used, !NeedsTwice(e.Key, e.Value), e.Key,
                e.Key.EndsWith("@e", StringComparison.Ordinal) ? "英語の後" : e.Key.EndsWith("@j", StringComparison.Ordinal) ? "日本語の後" : null))
            .ToList();

    /// <summary>文脈ごとに分けて覚えた語の印 (@e / @j) を外した、語そのもの。</summary>
    private static string WithoutContext(string key) => key.EndsWith("@e", StringComparison.Ordinal) || key.EndsWith("@j", StringComparison.Ordinal) ? key[..^2] : key;

    /// <summary>学習した語を忘れる (設定の「学習した語」から消したとき)。</summary>
    public void Remove(IEnumerable<string> words)
    {
        var removed = false;
        lock (_gate)
        {
            foreach (var word in words) removed |= _entries.Remove(word);
            if (!removed) return;
            Version++;
        }
        _saver.WriteNow();
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

    /// <summary>sinceUtc より後に覚えた (直した) 語を忘れる (トレイの「最近の学習を消す」)。忘れた数を返す。</summary>
    public int ForgetSince(DateTime sinceUtc)
    {
        List<string> recent;
        lock (_gate) recent = _entries.Where(e => e.Value.Used >= sinceUtc).Select(e => e.Key).ToList();
        Remove(recent);
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
                lock (_gate) json = JsonSerializer.Serialize(_entries, Config.LearningJsonContext.Default.Languages);
                Config.DataFile.Write(_path, temp => File.WriteAllText(temp, json));
            }
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"英語 / 日本語の学習データを保存できませんでした: {ex.Message}");
            return false;
        }
    }
}
