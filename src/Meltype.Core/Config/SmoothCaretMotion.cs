// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

namespace Meltype.Config;

/// <summary>
/// スムースカーソルの位置補間と距離連動ハンドオフを計算する純粋な状態マシン。
/// ゆったりとした指数減衰イージングでふわっと滑らかに追従し、
/// 到着直前の数 px で smoothstep により透明へ溶け込むことで元のキャレットとの二重表示やぼやけを防ぐ。
/// </summary>
public sealed class SmoothCaretMotion
{
    // ゆったりふわっと減速する指数減衰の時定数 (1 文字移動で約 160ms、長距離移動で約 200ms かけて柔らかく吸い付く)
    private const float TauMs = 46f;
    private const float ArriveSnapPx = 0.35f;
    private const float FadeStartDistancePx = 2.0f;
    private const float MinGlideDistancePx = 3.0f;
    private const float SameLineTolerancePx = 3.0f;
    private const float MinLineJumpPx = 6.0f;
    private const float MaxGlideDistancePx = 300f;
    private const float ActiveOpacity = 0.92f;

    private bool _initialized;

    public float CurrentX { get; private set; }
    public float CurrentY { get; private set; }
    public float CurrentHeight { get; private set; } = 18f;
    public float CurrentWidth { get; private set; } = 2f;
    public float TargetX { get; private set; }
    public float TargetY { get; private set; }
    public float TargetHeight { get; private set; } = 18f;
    public float TargetWidth { get; private set; } = 2f;
    public float Opacity { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>新しいターゲット矩形を設定する。アニメーションが必要なら true を返す。</summary>
    public bool SetTarget(float x, float y, float height, float width = 2f)
    {
        height = Math.Clamp(height, 10f, 64f);
        width = Math.Clamp(width, 1f, 3f);
        if (!_initialized)
        {
            _initialized = true;
            SnapTo(x, y, height, width);
            return false;
        }

        var targetDx = MathF.Abs(x - TargetX);
        var targetDy = MathF.Abs(y - TargetY);
        if (targetDx < 0.5f && targetDy < 0.5f && MathF.Abs(height - TargetHeight) < 0.5f)
            return IsActive;

        var dx = x - CurrentX;
        var dy = y - CurrentY;
        var absDx = MathF.Abs(dx);
        var absDy = MathF.Abs(dy);
        var distance = Distance(dx, dy);

        // 同一箇所での UIA / フォントメトリクス由来の微小な揺れや、別ウィンドウ等への遠距離ジャンプは即時スナップする
        var isJitter = absDx < MinGlideDistancePx && absDy < MathF.Max(MinLineJumpPx, height * 0.45f);
        if (isJitter || distance < MinGlideDistancePx || distance > MaxGlideDistancePx)
        {
            SnapTo(x, y, height, width);
            return false;
        }

        TargetX = x;
        TargetY = y;
        TargetHeight = height;
        TargetWidth = width;
        CurrentWidth = width;

        // 同じ行の横移動で上下 1〜3px だけメトリクスが揺れる場合は、縦揺れを防ぐため Y と高さを即座に揃える
        if (absDy <= SameLineTolerancePx)
        {
            CurrentY = y;
            CurrentHeight = height;
        }

        Opacity = ComputeOpacity(distance);
        IsActive = true;
        return true;
    }

    /// <summary>dtMs ミリ秒ぶん補間を進める。まだ描画・タイマー継続が必要なら true を返す。</summary>
    public bool Step(float dtMs)
    {
        if (!IsActive || dtMs <= 0f) return false;

        var alpha = 1f - MathF.Exp(-dtMs / TauMs);
        CurrentX += (TargetX - CurrentX) * alpha;
        CurrentY += (TargetY - CurrentY) * alpha;
        CurrentHeight += (TargetHeight - CurrentHeight) * alpha;

        var remaining = Distance(TargetX - CurrentX, TargetY - CurrentY);
        if (remaining <= ArriveSnapPx)
        {
            // ネイティブキャレットが既に TargetX に描画されているため、重なって太くぼやける手前で完全に消して引き継ぐ
            SnapTo(TargetX, TargetY, TargetHeight, TargetWidth);
            return false;
        }

        Opacity = ComputeOpacity(remaining);
        return true;
    }

    public void Reset()
    {
        _initialized = false;
        Opacity = 0f;
        IsActive = false;
    }

    private void SnapTo(float x, float y, float height, float width)
    {
        CurrentX = TargetX = x;
        CurrentY = TargetY = y;
        CurrentHeight = TargetHeight = height;
        CurrentWidth = TargetWidth = width;
        Opacity = 0f;
        IsActive = false;
    }

    private static float ComputeOpacity(float remainingPx)
    {
        if (remainingPx >= FadeStartDistancePx) return ActiveOpacity;
        if (remainingPx <= ArriveSnapPx) return 0f;
        var t = (remainingPx - ArriveSnapPx) / (FadeStartDistancePx - ArriveSnapPx);
        // smoothstep で到着直前をふわっと自然に溶け込ませる
        return ActiveOpacity * t * t * (3f - 2f * t);
    }

    private static float Distance(float dx, float dy) => MathF.Sqrt(dx * dx + dy * dy);
}
