// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text.Json;
using Meltype.Config;

namespace Meltype.Learning;

/// <summary>
/// 読みごとに選んだ語の回数。普通の変換の学習より弱く効かせる候補の記録に使う:
/// 選んだ英訳 (translations.json) と、ユーティリティ変換で選んだ書式 (utility-formats.json。読みの代わりにユーティリティ変換の id)。
/// </summary>
internal sealed class ChoiceCounts
{
    private readonly string? _path;
    private readonly string _name;
    // 読み直すときは、中身を書き換えずに丸ごと差し替える (Get は入力のスレッドから lock を取らずに読む)
    private Dictionary<string, Dictionary<string, int>> _counts;
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;
    private readonly FileStamp _stamp;

    /// <param name="name">ログに出すデータの名前 (英訳の学習データ)。</param>
    public ChoiceCounts(string? path, string name)
    {
        _name = name;
        _saver = new DeferredSaver(Write);
        _path = path;
        _stamp = new FileStamp(path);
        _counts = new(StringComparer.Ordinal);
        Reload();
    }

    private static Dictionary<string, Dictionary<string, int>>? Load(string? path, string name, Config.FileStamp stamp)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var read = Config.DataFile.Load(path, name, content =>
        {
            var loaded = JsonSerializer.Deserialize(content, Config.LearningJsonContext.Default.Translations);
            // 値が null の読みは捨てる (手で書き換えたファイル・細工したバックアップ。System.Text.Json は辞書の値の null を拒まない)
            if (loaded is not null)
                foreach (var (key, value) in loaded)
                    if (value is not null) counts[key] = value;
        }, stamp);
        return read ? counts : null;
    }

    /// <summary>ほかのプロセス (設定アプリ) がファイルを書き換えていたら読み直す (<see cref="FileStamp"/>)。</summary>
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
        if (Load(_path, _name, _stamp) is not { } counts) return;
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

    /// <summary>記録をすべて消す (設定アプリの「学習」の「すべて (リセット)」)。</summary>
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
            Diagnostics.Log.Warn($"{_name}を保存できませんでした: {ex.Message}");
            return false;
        }
    }
}
