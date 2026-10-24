// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package com.google.android.apps.inputmethod.libs.mozc.session;

/**
 * libmozc.so の入り口。パッケージ、クラス名、メソッドの名前と型は Mozc の
 * src/android/jni/mozcjni.cc が決めているので、変えない。
 *
 * ライブラリは JNI_OnLoad を持たない。名前で見つかるのは initialize() だけで、
 * 残りの3つは initialize() がこのクラスに登録する。だから initialize() を最初に呼ぶ。
 */
public final class MozcJni {
    static {
        System.loadLibrary("mozc");
    }

    private MozcJni() {}

    public static native boolean initialize();

    public static native boolean onPostLoad(String profileDirectory, String dataFile);

    public static native byte[] evalCommand(byte[] command);

    public static native String getDataVersion();
}
