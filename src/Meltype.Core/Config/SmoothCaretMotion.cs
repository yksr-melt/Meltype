// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

namespace Meltype.Config;

/// <summary>
/// スムースカーソルの位置補間とフェードアウトを計算する純粋な状態マシン。
/// 指数減衰イージングでターゲット位置へ滑らかに追従し、遠距離ジャンプ (ウィンドウ切替など) は即時スナップ、
/// 到着後は元のアプリのカーソルと二重に見えないよう自然にフェードアウトする。
/// </summary>
public sealed class SmoothCaretMotion
{
    private const float TauMs = 34f;
    private const float SnapThresholdPx = 0.5f;
    private const float MaxGlideDistancePx = 300f;
    private const float ActiveOpacity = 0.85f;
    private const float IdleBeforeFadeMs = 110f;
    private const float FadeDurationMs = 160f;

    private bool _initialized;
    private float _targetX;
    private float _targetY;
    private float _targetHeight;
    private float _settledMs;

    public float CurrentX { get; private set; }
    public float CurrentY { get; private set; }
    public float CurrentHeight { get; private set; } = 18f;
    public float Opacity { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>新しいターゲット矩形を設定する。アニメーションが必要なら true を返す。</summary>
    public bool SetTarget(float x, float y, float height)
    {
        height = Math.Clamp(height, 10f, 64f);
        if (!_initialized)
        {
            _initialized = true;
            CurrentX = _targetX = x;
            CurrentY = _targetY = y;
            CurrentHeight = _targetHeight = height;
            Opacity = 0f;
            IsActive = false;
            return false;
        }

        var dx = x - _targetX;
        var dy = y - _targetY;
        if (MathF.Abs(dx) < SnapThresholdPx && MathF.Abs(dy) < SnapThresholdPx && MathF.Abs(height - _targetHeight) < SnapThresholdPx)
            return IsActive;

        _targetX = x;
        _targetY = y;
        _targetHeight = height;

        var distance = Distance(x - CurrentX, y - CurrentY);
        if (distance > MaxGlideDistancePx)
        {
            // 別ウィンドウや大きく離れた行へのジャンプは画面を横切らせず即時スナップする
            CurrentX = x;
            CurrentY = y;
            CurrentHeight = height;
            Opacity = 0f;
            IsActive = false;
            _settledMs = 0f;
            return false;
        }

        _settledMs = 0f;
        Opacity = ActiveOpacity;
        IsActive = true;
        return true;
    }

    /// <summary>dtMs ミリ秒ぶん補間を進める。まだ描画・タイマー継続が必要なら true を返す。</summary>
    public bool Step(float dtMs)
    {
        if (!IsActive || dtMs <= 0f) return false;

        var alpha = 1f - MathF.Exp(-dtMs / TauMs);
        CurrentX += (_targetX - CurrentX) * alpha;
        CurrentY += (_targetY - CurrentY) * alpha;
        CurrentHeight += (_targetHeight - CurrentHeight) * alpha;

        var remaining = Distance(_targetX - CurrentX, _targetY - CurrentY);
        if (remaining < SnapThresholdPx && MathF.Abs(_targetHeight - CurrentHeight) < SnapThresholdPx)
        {
            CurrentX = _targetX;
            CurrentY = _targetY;
            CurrentHeight = _targetHeight;
            _settledMs += dtMs;

            if (_settledMs <= IdleBeforeFadeMs)
            {
                Opacity = ActiveOpacity;
                return true;
            }

            var fadeElapsed = _settledMs - IdleBeforeFadeMs;
            if (fadeElapsed >= FadeDurationMs)
            {
                Opacity = 0f;
                IsActive = false;
                return false;
            }

            Opacity = ActiveOpacity * (1f - fadeElapsed / FadeDurationMs);
            return true;
        }

        _settledMs = 0f;
        Opacity = ActiveOpacity;
        return true;
    }

    public void Reset()
    {
        _initialized = false;
        Opacity = 0f;
        IsActive = false;
        _settledMs = 0f;
    }

    private static float Distance(float dx, float dy) => MathF.Sqrt(dx * dx + dy * dy);
}
