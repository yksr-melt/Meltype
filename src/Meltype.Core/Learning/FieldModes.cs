// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Config;

namespace Meltype.Learning;

/// <summary>
/// 入力欄ごとの入力モードの学習 (fieldmodes.json。入力欄 → 日本語なら true)。
/// 入力欄に入ってまだ何も打たないうちに切り替えたモードを覚え、次にその入力欄に入ったらそのモードにする。
/// 覚えるのは直したことだけで、何も覚えていない入力欄では今までどおり (前のモードのまま)。
/// </summary>
internal sealed class FieldModes
{
    private readonly string? _path;
    // 読み直すときは、中身を書き換えずに丸ごと差し替える (Get は入力のスレッドから lock を取らずに読む)
    private Dictionary<string, bool> _modes = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly DeferredSaver _saver;
    private readonly FileStamp _stamp;

    public FieldModes(string? path)
    {
        _saver = new DeferredSaver(Write);
        _path = path;
        _stamp = new FileStamp(path);
        Reload();
    }

    /// <summary>
    /// 同じ入力欄とみなす名前。アプリと要素のクラス名で決める (要素は Windows の UI Automation。ほかの OS はアプリごと)。
    /// 要素の名前は使わない: チャット欄などはチャンネル名・相手の名前が入って入力欄ごとに増え、その名前がファイルに残る。
    /// アプリが分からなければ null (覚えない)。
    /// </summary>
    public static string? KeyOf(string? app, string? className) =>
        app is null ? null : $"{app}\t{className}";

    // 覚える入力欄の数の上限 (超えたら古いものから忘れる)
    private const int MaxEntries = 300;

    /// <summary>覚えたモード (日本語なら true)。覚えていなければ null。</summary>
    public bool? Get(string key) => _modes.TryGetValue(key, out var japanese) ? japanese : null;

    public void Remember(string key, bool japanese)
    {
        lock (_gate)
        {
            if (_modes.TryGetValue(key, out var known) && known == japanese) return;
            // 覚え直した入力欄は新しいものとして後ろに置く
            // (毎回作り直すので、Dictionary は足した順に並ぶ)
            var rest = _modes.Where(e => e.Key != key).ToList();
            if (rest.Count >= MaxEntries) rest = rest[(rest.Count - MaxEntries + 1)..];
            _modes = new(rest, StringComparer.Ordinal) { [key] = japanese };
        }
        _saver.Request();
    }

    /// <summary>覚えた入力欄の数。</summary>
    public int Count => _modes.Count;

    /// <summary>すべて消す (「学習データを消す」のリセット)。</summary>
    public void Clear()
    {
        lock (_gate) _modes = new(StringComparer.Ordinal);
        _saver.WriteNow();
    }

    /// <summary>ほかのプロセス (設定アプリ) が fieldmodes.json を書き換えていたら読み直す (<see cref="FileStamp"/>)。</summary>
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
        var modes = new Dictionary<string, bool>(StringComparer.Ordinal);
        // 読めなかったら前の中身のまま (Config.DataFile.Load)
        if (!DataFile.Load(_path, "入力欄ごとの入力モードの学習データ", content =>
            {
                if (JsonSerializer.Deserialize(content, LearningJsonContext.Default.FieldModes) is { } loaded)
                    foreach (var (key, value) in loaded) modes[key] = value;
            }, _stamp)) return;
        lock (_gate) _modes = modes;
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
                lock (_gate) json = JsonSerializer.Serialize(_modes, LearningJsonContext.Default.FieldModes);
                DataFile.Write(_path, temp => File.WriteAllText(temp, json));
                _stamp.Synced();
            }
            return true;
        }
        catch (Exception ex) when (DataFile.IsFileError(ex))
        {
            Diagnostics.Log.Warn($"入力欄ごとの入力モードの学習データを保存できませんでした: {ex.Message}");
            return false;
        }
    }
}
