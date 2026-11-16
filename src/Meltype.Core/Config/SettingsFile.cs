// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Config;

/// <summary>
/// config.json の設定。<see cref="Current"/> を読んだとき、前に確かめてから <see cref="CheckIntervalMs"/> 経っていれば
/// 更新時刻を見て、変わっていれば読み直す (Mac・Linux 版では、設定画面やエディターが入力とは別のプロセスで config.json を書く)。
/// 同じプロセスの設定画面 (Windows) は <see cref="Apply"/> で変える。どのスレッドから読んでもよい。
/// </summary>
/// <param name="loaded">読んだ・読み直した・変えたときに、lock の中で呼ぶ (待つ処理をしない)。</param>
internal sealed class SettingsFile(string path, Action<Settings>? loaded = null, Func<long>? timeMs = null)
{
    private readonly object _gate = new();

    /// <summary>1 打鍵で何度も読むので、更新時刻を見るのはこの間隔 (ミリ秒) に 1 回まで。保存してから効くまで、この分だけ遅れる。</summary>
    public const long CheckIntervalMs = 1000;

    private readonly Func<long> _timeMs = timeMs ?? (() => Environment.TickCount64);
    private Settings? _settings;
    private DateTime _writeTime;
    private long _checkedMs;
    private bool _failed;

    public Settings Current
    {
        get
        {
            lock (_gate) return Read();
        }
    }

    /// <summary>設定を変えて config.json に保存する。保存できなくても、このプロセスでは変えた設定を使う。</summary>
    public Settings Apply(Settings settings)
    {
        var next = settings.Clone().Normalize();
        lock (_gate)
        {
            try
            {
                next.Save(path);
                _writeTime = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"config.json を保存できませんでした: {ex.Message}");
            }
            _settings = next;
            _checkedMs = _timeMs();
            _failed = false;
            // 受け手が前の設定と比べるので、読み直しと同じく lock の中で知らせる
            loaded?.Invoke(next);
            return next;
        }
    }

    private Settings Read()
    {
        var now = _timeMs();
        if (_settings is not null && now - _checkedMs < CheckIntervalMs) return _settings;
        _checkedMs = now;
        // 読む前の時刻を覚える。読んでいる間に書かれても、次に読み直す (Load の移行で保存したときも 1 回読み直す)
        DateTime writeTime;
        try
        {
            writeTime = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (_settings is not null && ex is IOException or UnauthorizedAccessException)
        {
            // 更新時刻を読めなければ、前の設定を使い続けて次の間隔でまた確かめる
            return _settings;
        }
        var changed = writeTime != _writeTime;
        // 読み直せなかったときは、更新時刻が変わらなくても (権限を直したなど) 次の間隔でまた読む
        if (_settings is not null && !changed && !_failed) return _settings;
        _writeTime = writeTime;
        if (_settings is null) _settings = Settings.Load(path);
        else
        {
            // 読み直しで無い・読めないのは、エディターが書いている途中のことがある (元のファイルを別名に移してから書くエディターもある)。
            // 既定値に戻さず (.broken も作らず)、前の設定を使い続ける
            if (!File.Exists(path)) return _settings;
            try
            {
                _settings = Settings.LoadOrThrow(path);
                _failed = false;
            }
            catch (Exception ex)
            {
                // 読み直すたびにログを書かないように、書くのは更新時刻が変わったときだけ
                if (changed) Diagnostics.Log.Warn($"config.json を読み直せなかったので、前の設定を使い続けます: {ex.Message}");
                _failed = true;
                return _settings;
            }
        }
        loaded?.Invoke(_settings);
        return _settings;
    }
}
