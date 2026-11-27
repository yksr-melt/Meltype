// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Config;
using Meltype.Libraries.Chars;
using Meltype.Libraries.Lexicon;

namespace Meltype.Dictionary;

/// <summary>
/// 英訳の候補 (dictionaries/translations.txt。JMdict のよく使う語から作った表、CC BY-SA 4.0)。
/// 変換した文節 (複雑な) の英訳 (complex, complicated …) を、変換の候補の後ろに出すのに使う。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "日本語入力の辞書を表す既存の公開名。コレクション型を表すものではない。")]
internal sealed class TranslationDictionary
{
    // 書き方 [TAB] 品詞 [TAB] 英訳,英訳…
    private readonly Libraries.Lexicon.PackedLines _lines;

    private TranslationDictionary(Libraries.Lexicon.PackedLines lines) => _lines = lines;

    public int Count => _lines.Count;

    public static TranslationDictionary Load() => Parse(Libraries.Lexicon.DictionarySource.ReadEmbedded("translations.txt"));

    public static TranslationDictionary Parse(string text) => new(Libraries.Lexicon.PackedLines.FromText(text));

    // 語の後ろに付いていてよい、助詞・「だ」など (複雑|な、会議|に、食べる|の)。これ以外が続くなら (食べ|ます) 英訳は出さない。
    private static readonly HashSet<string> Endings =
        ["", "な", "に", "だ", "です", "の", "を", "が", "は", "も", "と", "で", "へ", "や", "から", "まで", "より", "って", "とか", "さ"];
    private static readonly int MaxEndingLength = Endings.Max(ending => ending.Length);

    /// <summary>
    /// 文節の英訳。text は変換した文字列 (複雑な)、reading はその読み (かなだけの語 ゆっくり を引くのに使う)。
    /// 後ろに「な」が付くなら な形容詞 の訳を先に、「に」なら副詞・な形容詞の訳を先にする。
    /// </summary>
    public IReadOnlyList<string> Lookup(string text, string reading)
    {
        foreach (var key in new[] { text, reading })
        {
            var minLength = Math.Max(1, key.Length - MaxEndingLength);
            for (var length = Math.Min(_lines.MaxKeyLength, key.Length); length >= minLength; length--)
            {
                var (first, count) = _lines.Find(key.AsSpan(0, length));
                if (count == 0) continue;
                var ending = key[length..];
                if (!Endings.Contains(ending)) break;
                string[] order = ending switch
                {
                    "な" => ["na", "i", "n", "adv", "v"],
                    "に" => ["adv", "na", "n", "i", "v"],
                    _ => ["n", "na", "i", "v", "adv"],
                };
                var words = new List<string>();
                var left = count; // まだ英訳を取り出していない行の数。全部取り出したら、残りの品詞は見なくてよい
                foreach (var kind in order)
                {
                    // 助詞は付けたまま (カメラを → cameraを、川に → riverに)。な形容詞の な・副詞の に は語の一部なので外す (複雑な → complex)。
                    var suffix = ending == "な" || (ending == "に" && kind is "adv" or "na") ? "" : ending;
                    for (var k = first; k < first + count; k++)
                    {
                        if (!_lines.Field(k, 1).SequenceEqual(kind)) continue;
                        var translations = _lines.Field(k, 2);
                        foreach (var range in translations.Split(','))
                        {
                            var translation = translations[range];
                            if (translation.IsEmpty) continue;
                            var word = string.Concat(translation, suffix.AsSpan());
                            if (!words.Contains(word)) words.Add(word);
                            if (words.Count == 5) return words;
                        }
                        if (--left == 0) return words;
                    }
                }
                return words;
            }
        }
        return [];
    }

    /// <summary>
    /// 変換の候補の意味 (橋 → bridge、箸 → chopsticks)。候補の先頭から一番長く辞書に一致する語を引く (橋を → 橋、持って → 持つ は引けない)。
    /// 残りに漢字・カタカナが入る (語の一部しか一致しない: 今日は → 今) なら null。かなだけの候補・英字の候補も null。
    /// </summary>
    public string? Meaning(string text)
    {
        var minLength = text.Length;
        while (minLength > 0 && !KanaChars.IsKanjiOrKatakana(text[minLength - 1])) minLength--;
        if (minLength == 0) return null;
        for (var length = Math.Min(_lines.MaxKeyLength, text.Length); length >= minLength; length--)
        {
            var (first, count) = _lines.Find(text.AsSpan(0, length));
            if (count == 0) continue;
            var words = new List<string>();
            for (var k = first; k < first + count && words.Count < 4; k++)
            {
                var translations = _lines.Field(k, 2);
                foreach (var range in translations.Split(','))
                {
                    var translation = translations[range];
                    if (translation.IsEmpty) continue;
                    var word = translation.ToString();
                    if (!words.Contains(word)) words.Add(word);
                    if (words.Count == 4) break;
                }
            }
            return words.Count == 0 ? null : string.Join(", ", words);
        }
        return null;
    }
}

/// <summary>
/// 選んだ英訳の記録 (%LOCALAPPDATA%\Meltype\translations.json)。普通の変換の学習より弱く効かせる:
/// 1 回選んだら英訳の中で先頭に、2 回以上なら変換エンジンの 1 番目の候補のすぐ後ろ (2 番目) に出す。1 番目にはしない。
/// </summary>
internal sealed class TranslationHistory
{
    private readonly string? _path;
    // 読み直すときは、中身を書き換えずに丸ごと差し替える (Get は入力のスレッドから lock を取らずに読む)
    private Dictionary<string, Dictionary<string, int>> _counts;
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;
    private readonly FileStamp _stamp;

    public TranslationHistory(string? path)
    {
        _saver = new DeferredSaver(Write);
        _path = path;
        _stamp = new FileStamp(path);
        _counts = new(StringComparer.Ordinal);
        Reload();
    }

    private static Dictionary<string, Dictionary<string, int>>? Load(string? path, Config.FileStamp stamp)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var read = Config.DataFile.Load(path, "英訳の学習データ", content =>
        {
            var loaded = JsonSerializer.Deserialize(content, Config.LearningJsonContext.Default.Translations);
            // 値が null の読みは捨てる (手で書き換えたファイル・細工したバックアップ。System.Text.Json は辞書の値の null を拒まない)
            if (loaded is not null)
                foreach (var (key, value) in loaded)
                    if (value is not null) counts[key] = value;
        }, stamp);
        return read ? counts : null;
    }

    /// <summary>ほかのプロセス (設定アプリ) が translations.json を書き換えていたら読み直す (<see cref="FileStamp"/>)。</summary>
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
        if (Load(_path, _stamp) is not { } counts) return;
        lock (_gate) _counts = counts;
    }

    /// <summary>読みに対して選んだ回数 (多い順)。</summary>
    public IReadOnlyList<(string Word, int Count)> Get(string reading) =>
        _counts.TryGetValue(reading, out var words) ? words.OrderByDescending(w => w.Value).Select(w => (w.Key, w.Value)).ToList() : [];

    public void Remember(string reading, string word)
    {
        lock (_gate)
        {
            if (!_counts.TryGetValue(reading, out var words)) _counts[reading] = words = new(StringComparer.Ordinal);
            words[word] = words.GetValueOrDefault(word) + 1;
        }
        _saver.Request();
    }

    /// <summary>選んだ英訳の記録をすべて消す (設定アプリの「学習データをすべて消す」)。</summary>
    public void Clear()
    {
        lock (_gate) _counts.Clear();
        _saver.WriteNow();
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
                lock (_gate) json = JsonSerializer.Serialize(_counts, Config.LearningJsonContext.Default.Translations);
                Config.DataFile.Write(_path, temp => File.WriteAllText(temp, json));
                _stamp.Synced();
            }
            return true;
        }
        catch (Exception ex) when (Config.DataFile.IsFileError(ex))
        {
            Diagnostics.Log.Warn($"英訳の学習データを保存できませんでした: {ex.Message}");
            return false;
        }
    }
}
