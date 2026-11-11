// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
//
// stub (MeltypeTip.dll) が TSF に渡すテキストサービス。impl (MeltypeTipImpl.dll) の TextService に転送し、impl を入れ替える。

#pragma once

#include "Globals.h"

namespace meltype {

HRESULT CreateStubService(REFIID riid, void** ppv);

}  // namespace meltype
