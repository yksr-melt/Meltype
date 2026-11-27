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
    public static void SmoothCaretMotion_GlidesSmoothlyAndFadesOutAfterSettling()
    {
        var motion = new SmoothCaretMotion();
        Assert.True(!motion.SetTarget(100, 200, 20), "初回位置では飛ばさずに現在位置だけ記録する");
        Assert.Equal(100f, motion.CurrentX);

        Assert.True(motion.SetTarget(120, 200, 20), "近くの移動ではアニメーションを開始する");
        Assert.True(motion.IsActive, "アニメーション中になる");
        Assert.True(motion.Opacity > 0.5f, "移動開始時は不透明になる");

        motion.Step(16f);
        Assert.True(motion.CurrentX > 100f && motion.CurrentX < 120f, $"1 フレーム後は中間に位置する: {motion.CurrentX}");

        // 十分時間が経つとターゲットへ収束し、フェードアウトして非アクティブになる
        for (var i = 0; i < 40; i++) motion.Step(16f);
        Assert.Equal(120f, motion.CurrentX);
        Assert.True(!motion.IsActive, "収束＋フェードアウト後は非アクティブになる");
        Assert.Equal(0f, motion.Opacity);
    }

    [Test]
    public static void SmoothCaretMotion_SnapsOnLargeJump()
    {
        var motion = new SmoothCaretMotion();
        motion.SetTarget(100, 100, 20);

        Assert.True(!motion.SetTarget(800, 500, 20), "300px 超の遠距離ジャンプは即時スナップして飛ばさない");
        Assert.Equal(800f, motion.CurrentX);
        Assert.Equal(500f, motion.CurrentY);
        Assert.True(!motion.IsActive, "即時スナップ時はアニメーションを開始しない");

        Assert.True(!motion.SetTarget(802, 500, 20), "微小な揺れ (3.5px 未満) は被り防止のためアニメーションしない");
        Assert.True(!motion.IsActive, "微小な揺れ時は非アクティブのまま");
    }
}
