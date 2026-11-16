// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.NowPlaying;

/// <summary>今再生している曲 (曲名・アーティスト。「なう」の候補: issue #1400) を OS から読むもの。覚えない (今の 1 曲だけ持つ)・送らない。</summary>
public interface INowPlayingSource : IDisposable
{
    /// <summary>今再生している曲。再生していない・読めないなら null。キーを処理するスレッドで呼ぶので待たない。</summary>
    (string Title, string Artist)? Read();
}

public static class NowPlayingSource
{
    /// <summary>この OS の読み方。読めない OS なら null。enabled は設定「なうで再生中の曲を候補に出す」(OFF の間は読まない)。</summary>
    public static INowPlayingSource? Create(Func<bool> enabled) =>
        OperatingSystem.IsWindows() ? new WindowsMediaSession(enabled) : null;
}
