// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Serugu

namespace Meltype.Composition;

/// <summary>
/// Zenzai (llama-server + zenz の GGUF) の持ち主。設定で ON のときだけ作り、指定の場所・GPU の設定が変わるたびに作り直す。
/// 使えなくなった (作れない・変換で例外になった <see cref="Failed"/>・何度も失敗して ZenzaiConverter が諦めた) ときは止めて、
/// しばらくたってから作り直す (打つたびに例外にしない。一時的な失敗で、ずっと止めたままにもしない。#973)。
/// 続けて使えないときは、待つ時間を <see cref="RetryAfter"/> から倍にしていく (<see cref="MaxRetryAfter"/> まで)。
/// 止める (Dispose) のはここだけで、使う側 (HybridConverter) は止めない。
/// </summary>
/// <param name="create">llama-server と GGUF の場所と使う GPU から作る (テストでは差し替える)。</param>
/// <param name="now">今の時刻 (ミリ秒。テストでは差し替える)。</param>
public sealed class ZenzaiHolder(Func<string, string, string, ZenzaiConverter>? create = null, Func<long>? now = null) : IDisposable
{
    private readonly Func<string, string, string, ZenzaiConverter> _create = create ?? CreateAndWarmUp;
    private readonly Func<long> _now = now ?? (() => Environment.TickCount64);
    private ZenzaiConverter? _zenzai;
    private string _key = "";
    private long _retryAt;
    // 続けて使えなかった回数 (待つ時間を延ばす)。場所が変わったとき、一度でも答えたものが失敗したときは 0 から
    private int _failures;
    // 作れなかったときの例外の中身を、この場所でもう書いたか (2 回目からは要点だけ書く)
    private bool _loggedCreateError;

    /// <summary>使えなくなってから、もう一度作るまでの最初の時間。</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>続けて使えないときに、待つ時間の上限。</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromHours(1);

    /// <summary>今の Zenzai。場所が空 (OFF) なら止めて null。作れない・止めて待っている間も null (変換は Mozc などで続ける)。</summary>
    public ZenzaiConverter? Get(string serverPath, string modelPath, string device = "")
    {
        var server = serverPath.Trim();
        var model = modelPath.Trim();
        if (server.Length == 0 || model.Length == 0)
        {
            // OFF にした・場所を空にしたら、llama-server を止める (メモリと CPU を使い続けないように)
            Stop();
            _key = "";
            return null;
        }
        device = device.Trim();
        var key = server + "|" + model + "|" + device;
        if (key != _key)
        {
            Stop();
            _key = key;
            _retryAt = 0;
            _failures = 0;
            _loggedCreateError = false;
        }
        // 何度も失敗して諦めた (llama-server が起動しない・答えない・空の答えが続く。例外にはならない) ときも、止めて待ってから作り直す。
        // 場所にファイルが無いときは諦めない (ZenzaiConverter が使えないと答えるだけ) ので、場所を直すまでそのまま
        if (_zenzai is { GaveUp: true }) StopAndWait("Zenzai が何度も変換できなかったので止めました");
        if (_zenzai is null && _now() >= _retryAt)
        {
            try
            {
                _zenzai = _create(server, model, device);
            }
            catch (Exception ex)
            {
                // 作れないとき (#973: 同梱の .NET に System.Net.Http が無い) は、変換は Mozc などで続ける
                // (HybridConverter は Zenzai が無ければ今までどおり変換する)。例外の中身を書くのは、この場所で 1 回目だけ
                var wait = WaitBeforeRetry();
                var detail = _loggedCreateError ? ex.Message : ex.ToString();
                _loggedCreateError = true;
                Diagnostics.Log.Error($"Zenzai を使えないので、今までの変換エンジンで変換します ({wait.TotalMinutes} 分後にもう一度試します): {detail}");
            }
        }
        return _zenzai;
    }

    /// <summary>変換で例外になった Zenzai を止める (HybridConverter から)。しばらくたってから作り直す。</summary>
    public void Failed(ZenzaiConverter failed)
    {
        if (ReferenceEquals(failed, _zenzai)) StopAndWait("変換で例外になったので Zenzai を止めました");
    }

    public void Dispose() => Stop();

    /// <summary>Zenzai の今の状態 (トレイのメニューに出す。issue #1152)。OFF・まだ一度も変換していなければ null。</summary>
    public string? Status => _key.Length == 0 ? null : _zenzai switch
    {
        null => "使えません (しばらくしてもう一度試します。ログに理由があります)",
        { IsInstalled: false } => "llama-server かモデルが見つかりません",
        { IsAvailable: false } => "使えません (llama-server が答えません。しばらくしてもう一度試します)",
        { IsReady: false } => "起動中",
        { IsResting: true } => "応答が遅いので、少し休んでいます",
        _ => "使えます",
    };

    private void StopAndWait(string reason)
    {
        // 一度でも答えたものの失敗は、一時的なものとして最初の待ち時間から
        if (_zenzai is { HasAnswered: true }) _failures = 0;
        Stop();
        var wait = WaitBeforeRetry();
        Diagnostics.Log.Info($"{reason}。{wait.TotalMinutes} 分後にもう一度使います");
    }

    private void Stop()
    {
        _zenzai?.Dispose();
        _zenzai = null;
    }

    private TimeSpan WaitBeforeRetry()
    {
        _failures++;
        var wait = TimeSpan.FromTicks(Math.Min(MaxRetryAfter.Ticks, RetryAfter.Ticks << Math.Min(_failures - 1, 20)));
        _retryAt = _now() + (long)wait.TotalMilliseconds;
        return wait;
    }

    private static ZenzaiConverter CreateAndWarmUp(string server, string model, string device)
    {
        var zenzai = new ZenzaiConverter(server, model, device: device);
        try
        {
            if (zenzai.IsInstalled) zenzai.WarmUp();
            return zenzai;
        }
        catch
        {
            zenzai.Dispose();
            throw;
        }
    }
}
