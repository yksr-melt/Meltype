// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using System.Text.Json.Serialization;
using Meltype.Detection;

namespace Meltype.Learning;

public sealed class PrefixStats
{
    [JsonPropertyName("japanese")] public int Japanese { get; set; }
    [JsonPropertyName("english")] public int English { get; set; }
    [JsonPropertyName("lastUsed")] public DateTime LastUsed { get; set; }
}

/// <summary>1 セッションの結末。UserModel に渡して学習させる。</summary>
public enum SessionOutcome
{
    /// <summary>日本語に切り替え、そのまま使われた。</summary>
    JapaneseAccepted,
    /// <summary>日本語に切り替えたが、直後に IME を切り替え直された (誤爆)。</summary>
    JapaneseRejected,
    /// <summary>切り替えず (英語/不明)、そのまま使われた。</summary>
    StayedAccepted,
    /// <summary>切り替えなかったが、直後に IME を ON にされた (見逃し)。</summary>
    StayedRejected,
}

/// <summary>
/// ユーザー固有の入力傾向 (設計書 §19, §20)。入力内容そのものは保存せず、
/// 判定に使った先頭数文字 (最大 <see cref="MaxPrefixLength"/> 文字) ごとの回数だけを持つ。
/// </summary>
public sealed class UserModel
{
    public const int MaxPrefixLength = 6;
    // 2 文字の prefix ("ka" など) を学習すると無関係な語まで巻き込むので 3 文字以上だけを扱う。
    public const int MinPrefixLength = 3;
    private const int MaxEntries = 5000;


    private readonly object _gate = new();
    private readonly string? _path;
    private Dictionary<string, PrefixStats> _prefixes = new(StringComparer.Ordinal);
    private bool _dirty;

    public UserModel(string? path)
    {
        _path = path;
        Load();
    }

    public int Count { get { lock (_gate) return _prefixes.Count; } }

    public static string? PrefixKey(string letters)
    {
        if (letters.Length < MinPrefixLength || letters.Any(c => c is < 'a' or > 'z')) return null;
        return letters.Length > MaxPrefixLength ? letters[..MaxPrefixLength] : letters;
    }

    public PrefixStats? Get(string prefix)
    {
        lock (_gate) return _prefixes.TryGetValue(prefix, out var stats) ? Copy(stats) : null;
    }

    /// <summary>
    /// 入力中の文字列の先頭と一致する学習済み prefix のうち、最も長いものからスコアを出す。
    /// 誤爆 (英語なのに日本語にした) の記録は日本語側の記録より重く扱う。
    /// </summary>
    public void Evaluate(string letters, List<Contribution> output)
    {
        PrefixStats? stats = null;
        string? matched = null;
        lock (_gate)
        {
            for (var length = Math.Min(letters.Length, MaxPrefixLength); length >= MinPrefixLength; length--)
            {
                if (_prefixes.TryGetValue(letters[..length], out var found))
                {
                    stats = found;
                    matched = letters[..length];
                    break;
                }
            }
        }
        if (stats is null) return;

        var japanese = 2 * stats.Japanese - 3 * stats.English;
        var english = 2 * stats.English - stats.Japanese;
        if (japanese >= 4)
        {
            output.Add(new Contribution("User", Math.Min(4, japanese / 2 + 2), 0, $"「{matched}」は日本語として使われてきた ({stats.Japanese}/{stats.English})"));
        }
        else if (english >= 2)
        {
            // 英語だった・誤爆だったという記録は拒否権として強く効かせる (誤爆防止が最優先)。
            output.Add(new Contribution("User", 0, Math.Min(10, english * 3), $"「{matched}」は英語として使われてきた ({stats.Japanese}/{stats.English})"));
        }
    }

    public void Learn(string letters, SessionOutcome outcome, bool decidedEnglish)
    {
        var key = PrefixKey(letters);
        if (key is null) return;
        lock (_gate)
        {
            if (!_prefixes.TryGetValue(key, out var stats))
            {
                // 「判定不能のまま使われた」だけでは記録を増やさない (学習データを肥大化させない)。
                if (outcome == SessionOutcome.StayedAccepted && !decidedEnglish) return;
                _prefixes[key] = stats = new PrefixStats();
            }
            switch (outcome)
            {
                case SessionOutcome.JapaneseAccepted:
                    stats.Japanese++;
                    break;
                case SessionOutcome.JapaneseRejected:
                    // 誤判定した場合は該当パターンの重みを下げる (設計書 §19)。
                    stats.English += 2;
                    stats.Japanese = Math.Max(0, stats.Japanese - 1);
                    break;
                case SessionOutcome.StayedAccepted:
                    stats.English++;
                    break;
                case SessionOutcome.StayedRejected:
                    stats.Japanese += 2;
                    stats.English = Math.Max(0, stats.English - 1);
                    break;
            }
            stats.Japanese = Math.Min(stats.Japanese, 1000);
            stats.English = Math.Min(stats.English, 1000);
            stats.LastUsed = DateTime.UtcNow;
            _dirty = true;
            TrimIfNeeded();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _prefixes.Clear();
            _dirty = true;
        }
        Save();
    }

    public void Save()
    {
        if (_path is null) return;
        Dictionary<string, PrefixStats> snapshot;
        lock (_gate)
        {
            if (!_dirty) return;
            snapshot = _prefixes.ToDictionary(p => p.Key, p => Copy(p.Value), StringComparer.Ordinal);
            _dirty = false;
        }
        try
        {
            Config.DataFile.Write(_path, temp => File.WriteAllText(temp, JsonSerializer.Serialize(new ModelFile { Version = 1, Prefixes = snapshot }, Config.IndentedLearningJsonContext.Default.ModelFile)));
        }
        catch (Exception ex)
        {
            lock (_gate) _dirty = true;
            Diagnostics.Log.Warn($"model.json を保存できませんでした: {ex.Message}");
        }
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(_path), Config.LearningJsonContext.Default.ModelFile);
            if (file?.Prefixes is null) return;
            foreach (var (key, stats) in file.Prefixes)
            {
                if (PrefixKey(key) == key && stats is not null) _prefixes[key] = stats;
            }
        }
        catch (Exception ex)
        {
            try { File.Copy(_path, _path + ".broken", overwrite: true); }
            catch (Exception copyEx) { Diagnostics.Log.Warn($"{Path.GetFileName(_path)} を .broken に残せませんでした: {copyEx.Message}"); }
            Diagnostics.Log.Warn($"model.json を読み込めなかったため学習データなしで開始します: {ex.Message}");
        }
    }

    private void TrimIfNeeded()
    {
        if (_prefixes.Count <= MaxEntries) return;
        foreach (var key in _prefixes.OrderBy(p => p.Value.LastUsed).Take(_prefixes.Count - MaxEntries * 9 / 10).Select(p => p.Key).ToList())
        {
            _prefixes.Remove(key);
        }
    }

    private static PrefixStats Copy(PrefixStats s) => new() { Japanese = s.Japanese, English = s.English, LastUsed = s.LastUsed };

    internal sealed class ModelFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("prefixes")] public Dictionary<string, PrefixStats>? Prefixes { get; set; }
    }
}
