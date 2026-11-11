// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// stub (MeltypeTip.dll) のエントリーポイント (COM のクラスファクトリー) と、IME としての登録 (regsvr32)。

#include "Globals.h"
#include "Stub.h"

namespace meltype {

namespace {

class ClassFactory final : public IClassFactory {
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory)) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return 2; }
    STDMETHODIMP_(ULONG) Release() override { return 1; }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        *ppv = nullptr;
        if (outer != nullptr) return CLASS_E_NOAGGREGATION;
        return CreateStubService(riid, ppv);
    }
    STDMETHODIMP LockServer(BOOL) override { return S_OK; }
};

ClassFactory g_factory;

std::wstring ClsidString() {
    wchar_t text[64] = {};
    StringFromGUID2(CLSID_TextService, text, ARRAYSIZE(text));
    return text;
}

std::wstring ModulePath() {
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(g_module, path, MAX_PATH);
    return path;
}

bool SetString(HKEY key, const wchar_t* name, const std::wstring& value) {
    return RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))) ==
           ERROR_SUCCESS;
}

HRESULT RegisterComServer() {
    std::wstring key = L"Software\\Classes\\CLSID\\" + ClsidString();
    HKEY clsid = nullptr;
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &clsid, nullptr) != ERROR_SUCCESS) return SELFREG_E_CLASS;
    SetString(clsid, nullptr, kDisplayName);
    HKEY server = nullptr;
    bool ok = RegCreateKeyExW(clsid, L"InprocServer32", 0, nullptr, 0, KEY_WRITE, nullptr, &server, nullptr) == ERROR_SUCCESS;
    if (ok) {
        ok = SetString(server, nullptr, ModulePath()) && SetString(server, L"ThreadingModel", L"Apartment");
        RegCloseKey(server);
    }
    RegCloseKey(clsid);
    return ok ? S_OK : SELFREG_E_CLASS;
}

void UnregisterComServer() {
    std::wstring key = L"Software\\Classes\\CLSID\\" + ClsidString();
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, key.c_str());
}

const GUID* const kCategories[] = {
    &GUID_TFCAT_TIP_KEYBOARD,
    &GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER,
    &GUID_TFCAT_TIPCAP_INPUTMODECOMPARTMENT,
    &GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT,
    &GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT,
};

HRESULT RegisterProfile() {
    ITfInputProcessorProfileMgr* profiles = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_ITfInputProcessorProfileMgr,
                                  reinterpret_cast<void**>(&profiles));
    if (FAILED(hr)) return hr;
    std::wstring icon = ModulePath();
    // アイコンはこの DLL のリソースの 1 つ目 (MeltypeTip.rc)
    hr = profiles->RegisterProfile(CLSID_TextService, kLangId, GUID_Profile, kDisplayName, static_cast<ULONG>(wcslen(kDisplayName)), icon.c_str(),
                                   static_cast<ULONG>(icon.size()), 0, nullptr, 0, TRUE, 0);
    profiles->Release();
    return hr;
}

void UnregisterProfile() {
    ITfInputProcessorProfileMgr* profiles = nullptr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_ITfInputProcessorProfileMgr,
                                   reinterpret_cast<void**>(&profiles)))) {
        profiles->UnregisterProfile(CLSID_TextService, kLangId, GUID_Profile, 0);
        profiles->Release();
    }
}

HRESULT RegisterCategories(bool add) {
    ITfCategoryMgr* category = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_ITfCategoryMgr, reinterpret_cast<void**>(&category));
    if (FAILED(hr)) return hr;
    for (const GUID* guid : kCategories) {
        if (add) {
            HRESULT one = category->RegisterCategory(CLSID_TextService, *guid, CLSID_TextService);
            if (FAILED(one)) hr = one;
        } else {
            category->UnregisterCategory(CLSID_TextService, *guid, CLSID_TextService);
        }
    }
    category->Release();
    return hr;
}

}  // namespace
}  // namespace meltype

using namespace meltype;

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            g_module = instance;
            DisableThreadLibraryCalls(instance);
            break;
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;
    if (!IsEqualCLSID(clsid, CLSID_TextService)) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, ppv);
}

// stub も impl も、プロセスの終了までアンロードしない
STDAPI DllCanUnloadNow() { return S_FALSE; }

STDAPI DllRegisterServer() {
    HRESULT hr = RegisterComServer();
    if (SUCCEEDED(hr)) hr = RegisterProfile();
    if (SUCCEEDED(hr)) hr = RegisterCategories(true);
    if (FAILED(hr)) {
        RegisterCategories(false);
        UnregisterProfile();
        UnregisterComServer();
    }
    return hr;
}

STDAPI DllUnregisterServer() {
    RegisterCategories(false);
    UnregisterProfile();
    UnregisterComServer();
    return S_OK;
}
