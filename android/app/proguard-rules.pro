# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 KoheiTakada
# JNI looks up these callback methods by name; R8 cannot see those references.
-keep class io.github.ninjin0802.keyboard.app.core.CoreMozcCallbacks { *; }
