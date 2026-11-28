// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

internal static class SmoothCaretTests
{
    [Test]
    public static void SmoothCaret_DefaultSettingIsOffAndOnlyActiveInTsfWhenEnabled()
    {
        Assert.True(!new Settings().SmoothCaret, "スムーズカーソルは既定で OFF");
        Assert.True(!new Settings().UsesSmoothCaret, "既定では無効");

        var tsf = new Settings { SmoothCaret = true, Mode = InputMode.Tsf, Enabled = true };
        Assert.True(tsf.UsesSmoothCaret, "Meltype IME 有効時のみ反映される");

        var paused = new Settings { SmoothCaret = true, Mode = InputMode.Tsf, Enabled = false };
        Assert.True(!paused.UsesSmoothCaret, "一時停止中は反映されない");

        var keyboard = new Settings { SmoothCaret = true, Mode = InputMode.Keyboard, Enabled = true };
        Assert.True(!keyboard.UsesSmoothCaret, "Meltype キーボード使用時は反映されない");
    }

    [Test]
    public static void SmoothCaretMotion_GlidesSoftlyAndHandsOffWithoutOverlap()
    {
        var motion = new SmoothCaretMotion();
        Assert.True(!motion.SetTarget(100, 200, 20), "初回位置では飛ばさずに現在位置だけ記録する");
        Assert.Equal(100f, motion.CurrentX);

        motion.NotifyInput();
        Assert.True(motion.SetTarget(124, 200, 20), "近くの移動ではアニメーションを開始する");
        Assert.True(motion.IsActive, "アニメーション中になる");
        Assert.Equal(100f, motion.CurrentX);
        Assert.True(motion.Opacity is > 0.2f and < 0.6f, $"出発直後はソフトフェードインする: {motion.Opacity}");

        motion.Step(16f);
        Assert.True(motion.CurrentX > 105f && motion.CurrentX < 115f, $"1 フレーム後はゆったり中間に位置する: {motion.CurrentX}");
        Assert.True(motion.Opacity > 0.8f, $"フェードイン後は鮮明な不透明度になる: {motion.Opacity}");

        var steps = 0;
        var maxX = motion.CurrentX;
        while (motion.Step(8.33f) && steps < 50)
        {
            maxX = MathF.Max(maxX, motion.CurrentX);
            steps++;
        }

        Assert.True(maxX <= 124.01f, $"指数減衰なのでオーバーシュートしない: maxX={maxX}");
        Assert.True(steps is >= 15 and < 40, $"ゆったりふわっとした余韻 (~150〜250ms) で収束する: steps={steps}");
        Assert.Equal(124f, motion.CurrentX);
        Assert.True(!motion.IsActive, "到着ハンドオフ後は即座に非アクティブになる");
        Assert.Equal(0f, motion.Opacity);
    }

    [Test]
    public static void SmoothCaretMotion_IgnoresRevertSpikeAndKeepsOpacityWhenMasked()
    {
        var motion = new SmoothCaretMotion();
        motion.SetTarget(100, 200, 20);
        motion.NotifyInput();
        Assert.True(motion.SetTarget(120, 200, 20, 2f, nativeCaretMasked: true), "マスク有効でアニメーション開始");

        motion.Step(20f, nativeCaretMasked: true);
        // 40ms 以内に直前の元の座標 (100) へ戻るキャッシュ遅延スパイクは無視し、ターゲット 120 を維持する
        Assert.True(motion.SetTarget(100, 200, 20, 2f, nativeCaretMasked: true), "逆戻りスパイクでも進行中を維持する");
        Assert.Equal(120f, motion.TargetX);

        while (motion.TargetX - motion.CurrentX > 1.2f)
            motion.Step(8.33f, nativeCaretMasked: true);
        Assert.True(motion.IsActive && motion.Opacity > 0.85f, $"マスク有効時は到着直前まで鮮明な不透明度を維持: {motion.Opacity}");
    }

    [Test]
    public static void SmoothCaretMotion_DetectsNativeInterpolatingCaretAndRecoversOnDiscreteJumps()
    {
        var motion = new SmoothCaretMotion();
        motion.Snap(100, 200, 20);

        // 1 回の入力 (NotifyInput) の後にアプリのキャレットが短時間に小刻みに 4 回以上補間移動したとき (Word・Excel 等) は自動抑止する
        motion.NotifyInput();
        Assert.True(motion.SetTarget(104, 200, 20, timestampMs: 100), "1 ステップ目は通常の移動として開始");
        Assert.True(motion.SetTarget(108, 200, 20, timestampMs: 116), "2 ステップ目");
        Assert.True(motion.SetTarget(112, 200, 20, timestampMs: 132), "3 ステップ目");
        Assert.True(!motion.SetTarget(116, 200, 20, timestampMs: 148), "4 ステップ目でアプリ自前のスムーズカーソルと判定して抑止する");
        Assert.True(motion.NativeAnimationDetected, "自前補間の検知フラグが立つ");
        Assert.True(!motion.IsActive, "二重表示を防ぐためオーバーレイを即座に止める");

        // 抑止中は次の入力でも飛ばさない
        motion.NotifyInput();
        Assert.True(!motion.SetTarget(128, 200, 20, timestampMs: 250), "抑止中は離散移動 1 回目ではまだ再開しない");

        // 1 入力 1 ステップの離散移動が続いたとき (VS Code 既定 OFF 等) は自動復帰する
        motion.NotifyInput();
        motion.SetTarget(140, 200, 20, timestampMs: 350);
        motion.NotifyInput();
        motion.SetTarget(152, 200, 20, timestampMs: 450);
        motion.NotifyInput();
        Assert.True(!motion.NativeAnimationDetected, "離散ジャンプが 3 回続いたら抑止を解除する");
        Assert.True(motion.SetTarget(164, 200, 20, timestampMs: 550), "解除後は再びスムーズカーソルが動く");
    }

    [Test]
    public static void SmoothCaretMotion_DoesNotSuppressBackspaceRepeatOrImmediateBackspaceAfterType()
    {
        var motion = new SmoothCaretMotion();
        motion.Snap(200, 200, 20);

        // 確定後の文字を BackSpace 連打で削除 (全角 14px ずつ、NotifyInput が遅れても誤抑止しない)
        Assert.True(motion.SetTarget(186, 200, 20, timestampMs: 100), "BackSpace 1 文字目でアニメーションする");
        Assert.True(motion.SetTarget(172, 200, 20, timestampMs: 116), "BackSpace 2 文字目");
        Assert.True(motion.SetTarget(158, 200, 20, timestampMs: 132), "BackSpace 3 文字目");
        Assert.True(motion.SetTarget(144, 200, 20, timestampMs: 148), "BackSpace 4 文字目でも誤抑止しない");
        Assert.True(!motion.NativeAnimationDetected, "文字幅の連続削除はアプリ自前補間と誤認しない");

        // 半角幅 (7px) のキーリピート (33ms 間隔) でも誤抑止しない
        Assert.True(motion.SetTarget(137, 200, 20, timestampMs: 200), "半角 BackSpace 1");
        Assert.True(motion.SetTarget(130, 200, 20, timestampMs: 233), "半角 BackSpace 2");
        Assert.True(motion.SetTarget(123, 200, 20, timestampMs: 266), "半角 BackSpace 3");
        Assert.True(motion.SetTarget(116, 200, 20, timestampMs: 299), "半角 BackSpace 4");
        Assert.True(!motion.NativeAnimationDetected, "キーリピート間隔の削除は誤抑止しない");

        // 入力直後 (40ms 以内) に BackSpace で元の位置へ戻る場合は逆戻りスパイクとして無視しない
        motion.Snap(100, 200, 20);
        motion.NotifyInput();
        Assert.True(motion.SetTarget(116, 200, 20, timestampMs: 500), "1 文字入力");
        motion.Step(20f);
        motion.NotifyInput();
        Assert.True(motion.SetTarget(100, 200, 20, timestampMs: 520), "直後の BackSpace は元の位置へ滑らかに戻る");
        Assert.Equal(100f, motion.TargetX);
    }

    [Test]
    public static void SmoothCaretMotion_StabilizesSameLineVerticalJitterAndSnapsLargeJump()
    {
        var motion = new SmoothCaretMotion();
        motion.SetTarget(100, 100, 18, 1f);

        motion.NotifyInput();
        Assert.True(motion.SetTarget(118, 102, 19, 1f), "横移動でアニメーション開始");
        Assert.Equal(102f, motion.CurrentY);
        Assert.Equal(19f, motion.CurrentHeight);
        Assert.Equal(1f, motion.CurrentWidth);

        motion.Reset();
        motion.SetTarget(100, 100, 20);

        motion.NotifyInput();
        Assert.True(!motion.SetTarget(800, 500, 20), "300px 超の遠距離ジャンプは即時スナップして飛ばさない");
        Assert.Equal(800f, motion.CurrentX);
        Assert.Equal(500f, motion.CurrentY);
        Assert.True(!motion.IsActive, "即時スナップ時はアニメーションを開始しない");

        motion.NotifyInput();
        Assert.True(!motion.SetTarget(802, 503, 20), "同一箇所の微小なメトリクス揺れは被り防止のためアニメーションしない");
        Assert.True(!motion.IsActive, "微小な揺れ時は非アクティブのまま");
    }
}
