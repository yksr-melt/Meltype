// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using Meltype.Config;

namespace Meltype.Tests;

internal static class SmoothCaretTests
{
    [Test]
    public static void SmoothCaret_DefaultSettingIsOff()
    {
        Assert.True(!new Settings().SmoothCaret, "スムースカーソルは既定で OFF");
    }

    [Test]
    public static void SmoothCaretMotion_GlidesSoftlyAndHandsOffWithoutOverlap()
    {
        var motion = new SmoothCaretMotion();
        Assert.True(!motion.SetTarget(100, 200, 20), "初回位置では飛ばさずに現在位置だけ記録する");
        Assert.Equal(100f, motion.CurrentX);

        Assert.True(motion.SetTarget(124, 200, 20), "近くの移動ではアニメーションを開始する");
        Assert.True(motion.IsActive, "アニメーション中になる");
        Assert.Equal(100f, motion.CurrentX);
        Assert.True(motion.Opacity > 0.5f, "移動開始時は鮮明な不透明度になる");

        motion.Step(16f);
        Assert.True(motion.CurrentX > 105f && motion.CurrentX < 115f, $"1 フレーム後はゆったり中間に位置する: {motion.CurrentX}");

        // ゆったり減速しながらターゲット近傍へ達し、オーバーシュートせずにネイティブキャレットへ引き継ぐ
        var steps = 0;
        var maxX = motion.CurrentX;
        while (motion.Step(8.33f) && steps < 50)
        {
            maxX = MathF.Max(maxX, motion.CurrentX);
            steps++;
        }

        Assert.True(maxX <= 124.01f, $"指数減衰なのでオーバーシュートしない: maxX={maxX}");
        Assert.True(steps is >= 15 and < 40, $"さらにゆったりふわっとした余韻 (~150〜250ms) で収束する: steps={steps}");
        Assert.Equal(124f, motion.CurrentX);
        Assert.True(!motion.IsActive, "到着ハンドオフ後は即座に非アクティブになる");
        Assert.Equal(0f, motion.Opacity);
    }

    [Test]
    public static void SmoothCaretMotion_StabilizesSameLineVerticalJitterAndSnapsLargeJump()
    {
        var motion = new SmoothCaretMotion();
        motion.SetTarget(100, 100, 18, 1f);

        // 同一行の横移動で UIA の Y 座標が 2px 揺れても縦には揺らさず新しい行高へ即座に揃える
        Assert.True(motion.SetTarget(118, 102, 19, 1f), "横移動でアニメーション開始");
        Assert.Equal(102f, motion.CurrentY);
        Assert.Equal(19f, motion.CurrentHeight);
        Assert.Equal(1f, motion.CurrentWidth);

        motion.Reset();
        motion.SetTarget(100, 100, 20);

        Assert.True(!motion.SetTarget(800, 500, 20), "300px 超の遠距離ジャンプは即時スナップして飛ばさない");
        Assert.Equal(800f, motion.CurrentX);
        Assert.Equal(500f, motion.CurrentY);
        Assert.True(!motion.IsActive, "即時スナップ時はアニメーションを開始しない");

        Assert.True(!motion.SetTarget(802, 503, 20), "同一箇所の微小なメトリクス揺れは被り防止のためアニメーションしない");
        Assert.True(!motion.IsActive, "微小な揺れ時は非アクティブのまま");
    }
}
