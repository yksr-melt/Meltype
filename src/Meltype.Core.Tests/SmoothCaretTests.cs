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
    public static void SmoothCaretMotion_GlidesSmoothlyAndHandsOffWithoutDestinationLinger()
    {
        var motion = new SmoothCaretMotion();
        Assert.True(!motion.SetTarget(100, 200, 20), "初回位置では飛ばさずに現在位置だけ記録する");
        Assert.Equal(100f, motion.CurrentX);

        Assert.True(motion.SetTarget(124, 200, 20), "近くの移動ではアニメーションを開始する");
        Assert.True(motion.IsActive, "アニメーション中になる");
        Assert.True(motion.CurrentX > 100f && motion.CurrentX < 124f, $"初回フレームで遅延防止の初期前進が入る: {motion.CurrentX}");
        Assert.True(motion.Opacity > 0.5f, "移動開始時は鮮明な不透明度になる");

        motion.Step(8.33f);
        Assert.True(motion.CurrentX > 104f && motion.CurrentX < 124f, $"1 フレーム後は中間に位置する: {motion.CurrentX}");

        // ターゲット近傍に達した時点で残り時間待ちをせず即座に透明・非アクティブへ引き継ぐ (二重表示防止)
        var steps = 0;
        while (motion.Step(8.33f) && steps < 30)
            steps++;

        Assert.True(steps < 15, $"約 120ms 以内に素早くネイティブキャレットへ引き継ぐ: steps={steps}");
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
