// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// DLL の入口 (COM のクラスファクトリー) と、IME としての登録 (regsvr32)。

#include <shlobj.h>
#include <strsafe.h>

#include <cstdarg>

#include "CandidateWindow.h"
#include "Globals.h"
#include "TextService.h"

namespace meltype {

HINSTANCE g_module = nullptr;
LONG g_locks = 0;

std::string ToUtf8(const std::wstring& text) {
    if (text.empty()) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string out(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), size, nullptr, nullptr);
    return out;
}

std::wstring FromUtf8(const std::string& text) {
    if (text.empty()) return {};
    int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring out(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), size);
    return out;
}

// 自分 (IME を読み込んだアプリ) の整合性レベルが Medium 以下か。読めなければ false
// (管理者として動くアプリは、ふつうの権限のプログラムがジャンクションで向けた先にログを作ってしまうので書かない)
static bool SelfIsMediumOrLower() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
    DWORD level = 0;
    DWORD size = 0;
    GetTokenInformation(token, TokenIntegrityLevel, nullptr, 0, &size);
    std::vector<BYTE> buffer(size);
    if (size > 0 && GetTokenInformation(token, TokenIntegrityLevel, buffer.data(), size, &size)) {
        PSID sid = reinterpret_cast<TOKEN_MANDATORY_LABEL*>(buffer.data())->Label.Sid;
        level = *GetSidSubAuthority(sid, *GetSidSubAuthorityCount(sid) - 1);
    }
    CloseHandle(token);
    return level != 0 && level <= SECURITY_MANDATORY_MEDIUM_RID;
}

void TipLog(const wchar_t* format, ...) {
    static int enabled = -1;
    if (enabled < 0 && !SelfIsMediumOrLower()) enabled = 0;
    if (enabled < 0) {
        wchar_t value[8] = {};
        enabled = GetEnvironmentVariableW(L"MELTYPE_TIP_LOG", value, 8) > 0 && value[0] == L'1' ? 1 : 0;
        // 環境変数が無ければ、データフォルダーに tip-log という空のファイルがあるときだけ
        if (!enabled) {
            wchar_t path[MAX_PATH] = {};
            if (GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH) > 0) {
                StringCchCatW(path, MAX_PATH, L"\\Meltype\\tip-log");
                enabled = GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES ? 1 : 0;
            }
        }
    }
    if (!enabled) return;
    wchar_t message[1024];
    va_list args;
    va_start(args, format);
    StringCchVPrintfW(message, ARRAYSIZE(message), format, args);
    va_end(args);
    wchar_t path[MAX_PATH] = {};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH) == 0) return;
    StringCchCatW(path, MAX_PATH, L"\\Meltype\\tip.log");
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME now;
    GetLocalTime(&now);
    wchar_t exe[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exe, MAX_PATH);
    const wchar_t* name = wcsrchr(exe, L'\\');
    wchar_t line[1400];
    StringCchPrintfW(line, ARRAYSIZE(line), L"%02d:%02d:%02d.%03d [%s %lu] %s\r\n", now.wHour, now.wMinute, now.wSecond, now.wMilliseconds,
                     name ? name + 1 : exe, GetCurrentThreadId(), message);
    std::string utf8 = ToUtf8(line);
    DWORD written = 0;
    WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
    CloseHandle(file);
}

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
    STDMETHODIMP_(ULONG) AddRef() override {
        DllAddRef();
        return 2;
    }
    STDMETHODIMP_(ULONG) Release() override {
        DllRelease();
        return 1;
    }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        *ppv = nullptr;
        if (outer != nullptr) return CLASS_E_NOAGGREGATION;
        auto* service = new (std::nothrow) TextService();
        if (service == nullptr) return E_OUTOFMEMORY;
        HRESULT hr = service->QueryInterface(riid, ppv);
        service->Release();
        return hr;
    }
    STDMETHODIMP LockServer(BOOL lock) override {
        if (lock) {
            DllAddRef();
        } else {
            DllRelease();
        }
        return S_OK;
    }
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
            CandidateWindow::RegisterClasses();
            break;
        case DLL_PROCESS_DETACH:
            CandidateWindow::UnregisterClasses();
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

STDAPI DllCanUnloadNow() {
    if (g_locks > 0) return S_FALSE;
    // このあと DLL が外れると静的変数ごと消えるので、ファクトリーはここで解放する
    CandidateWindow::ReleaseFactories();
    return S_OK;
}

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
