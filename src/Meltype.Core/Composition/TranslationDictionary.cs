// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Composition;

/// <summary>
/// 英訳の候補 (dictionaries/translations.txt。JMdict のよく使う語から作った表、CC BY-SA 4.0)。
/// 変換した文節 (複雑な) の英訳 (complex, complicated …) を、変換の候補の後ろに出すのに使う。
/// </summary>
public sealed class TranslationDictionary
{
    // 書き方 (複雑) → 品詞ごとの英訳
    private readonly Dictionary<string, List<(string Kind, string[] Words)>> _entries = new(StringComparer.Ordinal);
    private int _maxLength;

    public int Count => _entries.Count;

    public static TranslationDictionary Load() => Parse(Detection.DictionarySource.ReadEmbedded("translations.txt"));

    public static TranslationDictionary Parse(string text)
    {
        var dictionary = new TranslationDictionary();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            var fields = line.Split('\t');
            if (fields.Length < 3) continue;
            if (!dictionary._entries.TryGetValue(fields[0], out var list)) dictionary._entries[fields[0]] = list = [];
            list.Add((fields[1], fields[2].Split(',', StringSplitOptions.RemoveEmptyEntries)));
            dictionary._maxLength = Math.Max(dictionary._maxLength, fields[0].Length);
        }
        return dictionary;
    }

    // 語の後ろに付いていてよい、助詞・「だ」など (複雑|な、会議|に、食べる|の)。これ以外が続くなら (食べ|ます) 英訳は出さない。
    private static readonly HashSet<string> Endings =
        ["", "な", "に", "だ", "です", "の", "を", "が", "は", "も", "と", "で", "へ", "や", "から", "まで", "より", "って", "とか", "さ"];

    /// <summary>
    /// 文節の英訳。text は変換した文字列 (複雑な)、reading はその読み (かなだけの語 ゆっくり を引くのに使う)。
    /// 後ろに「な」が付くなら な形容詞 の訳を先に、「に」なら副詞・な形容詞の訳を先にする。
    /// </summary>
    public IReadOnlyList<string> Lookup(string text, string reading)
    {
        foreach (var key in new[] { text, reading })
        {
            for (var length = Math.Min(_maxLength, key.Length); length >= 1; length--)
            {
                if (!_entries.TryGetValue(key[..length], out var list)) continue;
                var ending = key[length..];
                if (!Endings.Contains(ending)) break;
                string[] order = ending switch
                {
                    "な" => ["na", "i", "n", "adv", "v"],
                    "に" => ["adv", "na", "n", "i", "v"],
                    _ => ["n", "na", "i", "v", "adv"],
                };
                var words = new List<string>();
                foreach (var kind in order)
                {
                    foreach (var (entryKind, entryWords) in list)
                    {
                        if (entryKind != kind) continue;
                        // 助詞は付けたまま (カメラを → cameraを、川に → riverに)。な形容詞の な・副詞の に は語の一部なので外す (複雑な → complex)。
                        var suffix = ending == "な" || (ending == "に" && kind is "adv" or "na") ? "" : ending;
                        foreach (var word in entryWords) if (!words.Contains(word + suffix) && words.Count < 5) words.Add(word + suffix);
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
        if (!text.Any(c => IsKanji(c) || c is >= 'ァ' and <= 'ヶ')) return null;
        for (var length = Math.Min(_maxLength, text.Length); length >= 1; length--)
        {
            if (!_entries.TryGetValue(text[..length], out var list)) continue;
            if (text[length..].Any(c => IsKanji(c) || c is >= 'ァ' and <= 'ヶ')) return null;
            var words = new List<string>();
            foreach (var (_, entryWords) in list)
            {
                foreach (var word in entryWords) if (!words.Contains(word) && words.Count < 4) words.Add(word);
            }
            return words.Count == 0 ? null : string.Join(", ", words);
        }
        return null;
    }

    private static bool IsKanji(char c) => c is >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or '々';
}

/// <summary>
/// 選んだ英訳の記録 (%LOCALAPPDATA%\Meltype\translations.json)。普通の変換の学習より弱く効かせる:
/// 1 回選んだら英訳の中で先頭に、2 回以上なら変換エンジンの 1 番目の候補のすぐ後ろ (2 番目) に出す。1 番目にはしない。
/// </summary>
public sealed class TranslationHistory
{
    private readonly string? _path;
    private readonly Dictionary<string, Dictionary<string, int>> _counts = new(StringComparer.Ordinal);

    public TranslationHistory(string? path)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), Config.LearningJsonContext.Default.Translations);
            if (loaded is not null) foreach (var (key, value) in loaded) _counts[key] = value;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"英訳の学習データを読めませんでした: {ex.Message}");
        }
    }

    /// <summary>読みに対して選んだ回数 (多い順)。</summary>
    public IReadOnlyList<(string Word, int Count)> Get(string reading) =>
        _counts.TryGetValue(reading, out var words) ? words.OrderByDescending(w => w.Value).Select(w => (w.Key, w.Value)).ToList() : [];

    public void Remember(string reading, string word)
    {
        if (!_counts.TryGetValue(reading, out var words)) _counts[reading] = words = new(StringComparer.Ordinal);
        words[word] = words.GetValueOrDefault(word) + 1;
        Save();
    }

    /// <summary>選んだ英訳の記録をすべて消す (トレイの「学習データをリセット」)。</summary>
    public void Clear()
    {
        _counts.Clear();
        Save();
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_counts, Config.LearningJsonContext.Default.Translations));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"英訳の学習データを保存できませんでした: {ex.Message}");
        }
    }
}
