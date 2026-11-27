// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// 変換中の文字の下線の種類 (打っている途中は点線、変換した文節は細線、選んでいる文節は太線)。

#pragma once

#include "Globals.h"

namespace meltype {

ITfDisplayAttributeInfo* CreateDisplayAttributeInfo(REFGUID guid);
IEnumTfDisplayAttributeInfo* CreateDisplayAttributeEnum();

}  // namespace meltype
