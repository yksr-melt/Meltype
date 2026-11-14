// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 cet-t

// 実際の TSF コンパートメントと入力欄を使う。パイプだけを記録用に置き換え、
// 通常のユーザーの Meltype・辞書・設定には接続しない。
#include "TextService.h"
#include <cstdio>
#include <cwchar>

namespace meltype {
HINSTANCE g_module = nullptr;
int toggleRequests = 0;
double revision = 0;
void TipLog(const wchar_t*, ...) {}
std::string ToUtf8(const std::wstring& text) {
    if (text.empty()) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), size, nullptr, nullptr);
    return result;
}
std::wstring FromUtf8(const std::string& text) {
    if (text.empty()) return {};
    int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring result(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), size);
    return result;
}
void PipeClient::Close() {}
// Meltype.exe の代わり: 開閉の事実をそのまま返し、toggle なら開閉を入れ替えて、入力モードの表示を求める
bool PipeClient::Transact(const std::string& request, std::string& response, DWORD, bool) {
    const bool toggle = request.find("\"type\":\"ModeToggleRequested\"") != std::string::npos;
    if (toggle) ++toggleRequests;
    const bool open = (request.find("\"imeOpen\":true") != std::string::npos) != toggle;
    const char* shown = open ? "true" : "false";
    response = std::string("{\"version\":1,\"state\":{\"handlesInput\":true,\"imeOpen\":") + shown + ",\"japanese\":" + shown +
               ",\"modeIndicatorMs\":" + (toggle ? "1200" : "0") +
               ",\"capture\":{\"printableAscii\":true,\"printableAsciiExcept\":\"\",\"shortcuts\":[]},\"revision\":" +
               std::to_string(++revision) + "},\"result\":{\"consumed\":true,\"commits\":[],\"view\":null}}";
    return true;
}
}  // namespace meltype

static void Pump() {
    MSG message;
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
}

int main() {
    using Microsoft::WRL::ComPtr;
    using namespace meltype;
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 1;
    g_module = GetModuleHandleW(nullptr);
    HWND window = CreateWindowExW(WS_EX_TOPMOST, L"STATIC", L"Meltype IME mode event test", WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                                  100, 100, 420, 180, nullptr, nullptr, g_module, nullptr);
    HWND normal = CreateWindowExW(0, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_BORDER, 20, 20, 360, 30, window, nullptr, g_module, nullptr);
    HWND password = CreateWindowExW(0, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_BORDER | ES_PASSWORD, 20, 70, 360, 30, window, nullptr, g_module, nullptr);
    SetForegroundWindow(window);
    SetFocus(normal);
    Pump();
    if (GetForegroundWindow() != window) {
        std::puts("SKIP: test window could not become foreground; no event assertions ran");
        DestroyWindow(window);
        CoUninitialize();
        return 77;
    }
    int failed = 0;
    auto check = [&](bool condition, const char* name) {
        std::printf("%s %s\n", condition ? "PASS" : "FAIL", name);
        if (!condition) ++failed;
    };
    {
        ComPtr<ITfThreadMgr> manager;
        TfClientId client = TF_CLIENTID_NULL;
        if (FAILED(CoCreateInstance(CLSID_TF_ThreadMgr, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&manager))) ||
            FAILED(manager->Activate(&client))) return 1;
        ComPtr<ITfCompartmentMgr> compartments;
        ComPtr<ITfCompartment> openClose;
        manager.As(&compartments);
        compartments->GetCompartment(GUID_COMPARTMENT_KEYBOARD_OPENCLOSE, &openClose);
        auto* service = new TextService(nullptr);
        if (FAILED(service->ActivateEx(manager.Get(), client, 0))) return 1;
        auto automatic = [&](bool open) {
            VARIANT value;
            VariantInit(&value);
            value.vt = VT_I4;
            value.lVal = open ? 1 : 0;
            openClose->SetValue(client, &value);
            Pump();
            check(service->IsOpen() == open, "automatic change reaches the real OnChange callback and Meltype.exe");
        };
        automatic(false);
        automatic(true);
        check(toggleRequests == 0, "application-driven close/open is not sent as an explicit toggle");
        SetFocus(password);
        automatic(false);
        check(toggleRequests == 0, "password-field entry and automatic close are not explicit toggles");
        SetFocus(normal);
        automatic(true);
        check(toggleRequests == 0, "password-field exit and automatic open are not explicit toggles");
        for (int i = 1; i <= 5; ++i) {
            const bool wasOpen = service->IsOpen();
            service->ToggleInputMode(nullptr);
            Pump();
            check(service->IsOpen() != wasOpen, "explicit toggle changes the input mode");
            check(toggleRequests == i, "each explicit toggle is sent once (Meltype.exe shows the indicator)");
        }
        service->Deactivate();
        service->Release();
        manager->Deactivate();
    }
    DestroyWindow(window);
    CoUninitialize();
    return failed == 0 ? 0 : 1;
}
