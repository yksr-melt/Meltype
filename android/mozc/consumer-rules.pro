# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 ninjin0802
# libmozc.so は、このクラスとメソッドを名前で探す（initialize は記号の名前、残りは RegisterNatives）。
-keep class com.google.android.apps.inputmethod.libs.mozc.session.MozcJni { *; }

# protobuf-javalite は、生成されたクラスのフィールドを実行時に名前で読む。
-keep class org.mozc.android.inputmethod.japanese.protobuf.** { *; }
-keepclassmembers class * extends com.google.protobuf.GeneratedMessageLite { <fields>; }
