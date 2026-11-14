// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
//
// impl (MeltypeTipImpl.dll) のエントリーポイント。stub (MeltypeTip.dll) がロードして MeltypeTipAbi を確かめ、MeltypeTipCreate を呼ぶ。

#include <new>

#include "TextService.h"

using namespace meltype;

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

extern "C" UINT STDAPICALLTYPE MeltypeTipAbi() { return kStubImplAbi; }

extern "C" HRESULT STDAPICALLTYPE MeltypeTipCreate(HWND stub, ITfTextInputProcessorEx** service) {
    if (service == nullptr) return E_INVALIDARG;
    *service = new (std::nothrow) TextService(stub);
    return *service ? S_OK : E_OUTOFMEMORY;
}
