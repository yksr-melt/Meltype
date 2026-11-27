// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Config;

/// <summary>
/// 設定アプリから、動いている Meltype.exe に出してもらう画面・操作 (Meltype.exe --open &lt;名前&gt;)。
/// ログ・直した誤判定・打鍵数・更新の状態は Meltype.exe のメモリにあり、別のプロセスの設定アプリからは読めない。
/// </summary>
public enum AppWindow
{
    Guide,
    Report,
    Corrections,
    Log,
    KeyCount,
    ReleaseNotes,
    CheckForUpdate,
    RegisterIme,
}
