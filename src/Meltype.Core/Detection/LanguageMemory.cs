// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Config;

namespace Meltype.Detection;

/// <summary>英字で打った語を英語・日本語のどちらで出すかを学習したもの (トレイの「学習した語」の一覧に出す)。Active は今有効になっているか (2 回目を待っている語は false)。</summary>
public sealed record LearnedLanguage(string Word, bool English, int Count, DateTime Used, bool Active, string? Key = null, string? Context = null)
{
    /// <summary>忘れるときに渡す名前 (文脈ごとに分けて覚えた語は、印 @e / @j 付き)。</summary>
    public string EntryKey => Key ?? Word;
}

/// <summary>
/// 英語とも日本語とも読める語 (api, sushi …) を、ユーザーが自分で英字 / かなに直したときに覚えておく。
/// 次からその語は、自動の判定より覚えた方を優先する (api と打って F10 で英字にして確定 → 次から api は英字)。
/// %LOCALAPPDATA%\Meltype\languages.json に「打った英字 → 英語か」だけを保存する。
/// </summary>
internal sealed class LanguageMemory
{
    private const int MaxEntries = 3000;
    private readonly string? _path;
    // 覚える・忘れるは中身を書き換えるので、読むときも _gate を取る (設定・トレイの一覧は入力と別のスレッドから読む: issue #2271)
    private Dictionary<string, Entry> _entries;
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;
    private readonly FileStamp _stamp;

    internal sealed class Entry
    {
        public bool English { get; set; }
        public DateTime Used { get; set; }

        /// <summary>同じ向きに直した回数 (前の版で保存したものは 0 = 1 回)。</summary>
        public int Count { get; set; }

        /// <summary>F10 / F6 などで、はっきり英字 / かなに直したか (変換の候補から選んだだけなら false)。</summary>
        public bool Explicit { get; set; }
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

    /// <summary><see cref="IsReadableRomaji"/> の決め方: 途中までではなく、全体がローマ字として読める。</summary>
    internal static Func<string, bool> ReadableBy(RomajiDetector romaji) => word => romaji.AnalyzeFragment(word) is { IsValid: true, Partial: "" };

    /// <summary>英語として覚えるのに 2 回要る語か。</summary>
    private bool NeedsTwice(string word, Entry entry) =>
        entry.English && Math.Max(1, entry.Count) < 2 &&
        (IsCommonJapanese?.Invoke(word) == true || (!entry.Explicit && word.Length <= 2 && IsReadableRomaji?.Invoke(word) == true));

    public LanguageMemory(string? path)
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
        var read = Config.DataFile.Load(path, "英語 / 日本語の学習データ", content =>
        {
            var loaded = JsonSerializer.Deserialize(content, Config.LearningJsonContext.Default.Languages);
            // 値が null の語は捨てる (手で書き換えたファイル・細工したバックアップ。System.Text.Json は辞書の値の null を拒まない)
            if (loaded is not null)
                foreach (var (word, entry) in loaded)
                    if (entry is not null) entries[word] = entry;
        }, stamp);
        return read ? entries : null;
    }

    /// <summary>ほかのプロセス (設定アプリ) が languages.json を書き換えていたら読み直す (<see cref="FileStamp"/>)。</summary>
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

    public int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>
    /// 文脈ごとに分けるときの、語の後ろにつける印 (分けないときは空)。context は今の入力の前が英語か (true)・日本語か (false) で、
    /// 学習を前の文脈ごとに分けない (設定「英語 / 日本語の学習を前の文脈ごとに分ける」が OFF: issue #491)・前が分からないなら null。
    /// </summary>
    private static string ContextSuffix(bool? context) => context is { } english ? (english ? "@e" : "@j") : "";

    /// <summary>覚えている内容が変わるたびに増える (判定の結果を使い回してよいかを見るのに使う)。</summary>
    public int Version { get; private set; }

    /// <summary>覚えている語なら英語か (true) 日本語か (false)。覚えていなければ null。word は小文字の英字。</summary>
    public bool? Get(string word, bool? context = null)
    {
        lock (_gate) return GetLocked(word, context);
    }

    private bool? GetLocked(string word, bool? context)
    {
        if (ContextSuffix(context) is { Length: > 0 } suffix && _entries.TryGetValue(word + suffix, out var contextual))
            return NeedsTwice(word, contextual) ? null : contextual.English;
        if (!_entries.TryGetValue(word, out var entry)) return null;
        if (NeedsTwice(word, entry)) return null;
        return entry.English;
    }

    internal bool? Get(ReadOnlySpan<char> word, bool? context = null)
    {
        if (context is not null) return Get(word.ToString(), context);
        lock (_gate)
        {
            if (!_entries.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(word, out var entry)) return null;
            if (NeedsTwice(word.ToString(), entry)) return null;
            return entry.English;
        }
    }

    /// <summary>
    /// ユーザーが英字 / かなに直した語を覚える (2 文字以上の英字だけ)。
    /// explicitChoice は F10 / F6 / Tab ではっきり直したとき (変換の候補から選んだだけなら false)。context は前の文脈 (<see cref="ContextSuffix"/>)。
    /// </summary>
    public void Remember(string word, bool english, bool explicitChoice = false, bool? context = null)
    {
        word = word.ToLowerInvariant();
        if (word.Length < 2 || !word.All(char.IsAsciiLetterLower)) return;
        lock (_gate) RememberLocked(word, english, explicitChoice, context);
        _saver.Request();
    }

    private void RememberLocked(string word, bool english, bool explicitChoice, bool? context)
    {
        Version++;
        var entryKey = word + ContextSuffix(context);
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
    public IReadOnlyList<LearnedLanguage> Entries()
    {
        lock (_gate)
        {
            // 2 回目を待っているかは、Get と同じく印を外した語で見る (印付きの語はローマ字として読めないので、待っていても有効と出てしまう: issue #2268)
            return _entries.OrderByDescending(e => e.Value.Used)
                .Select(e => new LearnedLanguage(WithoutContext(e.Key), e.Value.English, Math.Max(1, e.Value.Count), e.Value.Used, !NeedsTwice(WithoutContext(e.Key), e.Value), e.Key,
                    e.Key.EndsWith("@e", StringComparison.Ordinal) ? "英語の後" : e.Key.EndsWith("@j", StringComparison.Ordinal) ? "日本語の後" : null))
                .ToList();
        }
    }

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
                if (_stamp.Changed)
                {
                    Reload();
                    return true;
                }
                string json;
                lock (_gate) json = JsonSerializer.Serialize(_entries, Config.LearningJsonContext.Default.Languages);
                Config.DataFile.Write(_path, temp => File.WriteAllText(temp, json));
                _stamp.Synced();
            }
            return true;
        }
        catch (Exception ex) when (Config.DataFile.IsFileError(ex))
        {
            Diagnostics.Log.Warn($"英語 / 日本語の学習データを保存できませんでした: {ex.Message}");
            return false;
        }
    }
}
