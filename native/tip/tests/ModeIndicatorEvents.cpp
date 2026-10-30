// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 cet-t

// 実際の TSF コンパートメントと入力欄を使う。パイプだけを記録用に置き換え、
// 通常のユーザーの Meltype・辞書・設定には接続しない。
#include "TextService.h"
#include <atomic>
#include <cstdio>
#include <cwchar>

namespace meltype {
HINSTANCE g_module = nullptr;
LONG g_locks = 0;
int indicatorRequests = 0;
std::atomic<bool> indicatorEnabled = false;
int helloApplied = 0;
void TipLog(const wchar_t* format, ...) {
    // ApplyHello が設定を反映した後に出す接続状態のログを待機条件に使う。
    if (std::wcscmp(format, L"Meltype.exe: %s") == 0) ++helloApplied;
}
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
bool PipeClient::Transact(const std::string& request, std::string& response, DWORD, bool) {
    if (request.find("\"op\":\"indicator\"") != std::string::npos) ++indicatorRequests;
    response = std::string("{\"active\":true,\"code\":false,\"modeIndicator\":") +
               (indicatorEnabled.load() ? "true" : "false") +
               ",\"consumed\":false,\"commits\":[],\"view\":null}";
    return true;
}
}

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
        for (bool enabled : {false, true}) {
            indicatorEnabled = enabled;
            auto* service = new TextService();
            const int appliedBefore = helloApplied;
            if (FAILED(service->ActivateEx(manager.Get(), client, 0))) return 1;
            // hello は別スレッドで取得し、タイマーで所有スレッドへ反映する。
            // 通信が返っただけで切り替えを始めず、設定の反映完了までメッセージを処理する。
            const ULONGLONG deadline = GetTickCount64() + 3000;
            while (helloApplied == appliedBefore && GetTickCount64() < deadline) {
                Pump();
                Sleep(10);
            }
            check(helloApplied > appliedBefore, "hello settings are applied before testing mode switches");
            std::printf("modeIndicator=%s\n", enabled ? "ON" : "OFF");
            auto automatic = [&](bool open) {
                VARIANT value;
                VariantInit(&value);
                value.vt = VT_I4;
                value.lVal = open ? 1 : 0;
                openClose->SetValue(client, &value);
                Pump();
                check(service->IsOpen() == open, "automatic change reaches the real OnChange callback");
            };
            indicatorRequests = 0;
            automatic(false);
            automatic(true);
            check(indicatorRequests == 0, "application-driven close/open does not request an indicator");
            SetFocus(password);
            automatic(false);
            check(indicatorRequests == 0, "password-field entry and automatic close do not request an indicator");
            SetFocus(normal);
            automatic(true);
            check(indicatorRequests == 0, "password-field exit and automatic open do not request an indicator");
            for (int i = 1; i <= 5; ++i) {
                const bool wasOpen = service->IsOpen();
                service->ToggleInputMode(nullptr);
                Pump();
                check(service->IsOpen() != wasOpen, "explicit toggle changes input mode even when indicator is disabled");
                check(indicatorRequests == (enabled ? i : 0), enabled
                    ? "enabled indicator requests exactly one final indicator per toggle"
                    : "disabled indicator sends no request on explicit toggles");
            }
            service->Deactivate();
            service->Release();
        }
        manager->Deactivate();
    }
    DestroyWindow(window);
    CoUninitialize();
    return failed == 0 ? 0 : 1;
}
