// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 cet-t

// 実際の TSF コンパートメントと入力欄で、自動開閉・明示的な切り替え・タスクバーのメニューへのキーの受け渡しを確かめる。
// タスクバーはテストのプロセスに同じクラス名のウィンドウを作って再現する。パイプだけを記録用に置き換え、
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
    response = std::string("{\"version\":1,\"state\":{\"handlesInput\":true,\"imeOpen\":") + shown +
               ",\"languageMode\":\"" + (open ? "Japanese" : "Alphanumeric") + "\"" +
               ",\"modeIndicatorMs\":" + (toggle ? "1200" : "0") +
               ",\"capture\":{\"printableAscii\":true,\"printableAsciiExcept\":\"\",\"shortcuts\":[{\"vk\":32,\"modifiers\":0}]},\"revision\":" +
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
    HWND window = CreateWindowExW(WS_EX_TOPMOST, L"STATIC", L"Meltype IME input event test", WS_OVERLAPPEDWINDOW | WS_VISIBLE,
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

        ComPtr<ITfDocumentMgr> document;
        ComPtr<ITfContext> context;
        TfEditCookie edit = TF_INVALID_EDIT_COOKIE;
        if (FAILED(manager->CreateDocumentMgr(&document)) ||
            FAILED(document->CreateContext(client, 0, nullptr, &context, &edit)) || FAILED(document->Push(context.Get()))) return 1;
        auto takesKey = [&](UINT vk) {
            BOOL eaten = FALSE;
            check(SUCCEEDED(service->OnTestKeyDown(context.Get(), vk, 1, &eaten)), "TSF key test succeeds");
            return eaten != FALSE;
        };
        check(takesKey('U'), "ordinary input receives U");
        check(takesKey(VK_SPACE), "ordinary input captures Space as an input-mode shortcut");
        // Win+X のメニューと Windows 10 のアドレス ツールバーの代わり。クラス名はテストのプロセスの中だけで使われる
        WNDCLASSW taskbarClass = {};
        taskbarClass.lpfnWndProc = DefWindowProcW;
        taskbarClass.hInstance = g_module;
        taskbarClass.lpszClassName = L"Shell_TrayWnd";
        if (!RegisterClassW(&taskbarClass)) return 1;
        HWND taskbar = CreateWindowExW(WS_EX_TOPMOST, L"Shell_TrayWnd", L"", WS_POPUP | WS_VISIBLE, 100, 300, 420, 48, nullptr, nullptr, g_module, nullptr);
        HWND menu = CreateWindowExW(0, L"STATIC", L"", WS_CHILD | WS_VISIBLE, 0, 0, 200, 48, taskbar, nullptr, g_module, nullptr);
        HWND address = CreateWindowExW(0, L"EDIT", L"", WS_CHILD | WS_VISIBLE, 200, 0, 220, 48, taskbar, nullptr, g_module, nullptr);
        if (!taskbar || !menu || !address) return 1;
        SetForegroundWindow(taskbar);
        SetFocus(menu);
        // 通知を待たないので、フォーカスが移った直後の 1 打目からメニューへ渡る
        check(!takesKey('U'), "the first letter after the taskbar menu takes focus goes to the menu");
        check(!takesKey('U'), "the next submenu access key also goes to the menu");
        check(!takesKey('1'), "a digit goes to the taskbar menu");
        check(!takesKey(VK_SPACE), "Space goes to the taskbar menu even when configured as a shortcut");
        BOOL eaten = TRUE;
        service->OnKeyDown(context.Get(), VK_SPACE, 1, &eaten);
        check(!eaten && toggleRequests == 0, "the actual Space key does not toggle the input mode in a menu");
        SetFocus(address);
        check(takesKey('U'), "the address toolbar of the taskbar receives U as an input field");
        SetForegroundWindow(window);
        SetFocus(normal);
        check(takesKey('U'), "ordinary input receives U again after leaving the taskbar");
        check(takesKey(VK_SPACE), "ordinary input captures Space again after leaving the taskbar");
        DestroyWindow(taskbar);
        document->Pop(TF_POPF_ALL);

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
