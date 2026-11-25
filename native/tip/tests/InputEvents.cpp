// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 cet-t

// 実際の TSF コンパートメントと入力欄で、自動開閉・明示的な切り替え・キーの受け渡し・キャレット通知を確かめる。
// タスクバーはテストのプロセスに同じクラス名のウィンドウを作って再現する。パイプだけを記録用に置き換え、
// 通常のユーザーの Meltype・辞書・設定には接続しない。
#include "TextService.h"
#include <cstdio>
#include <cwchar>
#include <functional>

namespace meltype {
HINSTANCE g_module = nullptr;
int toggleRequests = 0;
double revision = 0;
std::vector<JsonValue> caretEvents;
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
    JsonValue message;
    if (ParseJson(request, message) && message[L"event"][L"type"].Str() == L"CaretMoved") {
        caretEvents.push_back(message[L"event"][L"caret"]);
    }
    const bool open = (request.find("\"imeOpen\":true") != std::string::npos) != toggle;
    const char* shown = open ? "true" : "false";
    // l の打鍵は、アプリの編集からキャレット通知を送るテストのためにアプリへ通す。
    response = std::string("{\"version\":1,\"state\":{\"handlesInput\":true,\"imeOpen\":") + shown +
               ",\"languageMode\":\"" + (open ? "Japanese" : "Alphanumeric") + "\"" +
               ",\"modeIndicatorMs\":" + (toggle ? "1200" : "0") +
               ",\"capture\":{\"printableAscii\":true,\"printableAsciiExcept\":\"l\",\"shortcuts\":[{\"vk\":32,\"modifiers\":0}]},\"revision\":" +
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

static void CheckCaretNotifications(ITfContext* context, TfClientId client, meltype::TextService& service,
                                    const std::function<void(bool, const char*)>& check) {
    using Microsoft::WRL::ComPtr;
    using namespace meltype;
    const auto edit = [&](const wchar_t* text, LONG position) {
        return RunEditSession(context, client, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie cookie) {
            ComPtr<ITfRange> range;
            HRESULT hr;
            if (text) {
                // アプリが文字を追加し、同じ編集セッションでキャレットをその直後へ進める。
                if (FAILED(hr = context->GetEnd(cookie, &range))) return hr;
                if (FAILED(hr = range->SetText(cookie, 0, text, static_cast<LONG>(std::wcslen(text))))) return hr;
                if (FAILED(hr = context->GetEnd(cookie, &range))) return hr;
            } else {
                if (FAILED(hr = context->GetStart(cookie, &range))) return hr;
                LONG shifted = 0;
                if (FAILED(hr = range->ShiftStart(cookie, position, &shifted, nullptr))) return hr;
                if (shifted != position) return E_FAIL;
                if (FAILED(hr = range->Collapse(cookie, TF_ANCHOR_START))) return hr;
            }
            TF_SELECTION selection{};
            selection.range = range.Get();
            selection.style.ase = TF_AE_NONE;
            return context->SetSelection(cookie, 1, &selection);
        });
    };
    const auto expect = [&](UINT key, const wchar_t* text, LONG position, UINT expectedKey, wchar_t expectedChar, const char* label) {
        caretEvents.clear();
        if (key != 0) {
            // 小文字の l を打つ条件を固定する。Caps Lock や修飾キーの状態はこのスレッドのキー判定の間だけ変える。
            BYTE savedKeys[256]{};
            BYTE unmodifiedKeys[256]{};
            if (!GetKeyboardState(savedKeys) || !SetKeyboardState(unmodifiedKeys)) {
                check(false, "set unmodified keyboard state for the test key");
                return;
            }
            BOOL eaten = TRUE;
            const HRESULT keyResult = service.OnTestKeyDown(context, key, 0, &eaten);
            const BOOL restored = SetKeyboardState(savedKeys);
            if (FAILED(keyResult) || eaten || !restored) {
                check(false, label);
                return;
            }
        }
        const HRESULT edited = edit(text, position);
        if (FAILED(edited) || caretEvents.size() != 1) {
            std::printf("  edit: 0x%08lX, caret notifications: %zu\n", static_cast<unsigned long>(edited), caretEvents.size());
            check(false, label);
            return;
        }
        const auto& caret = caretEvents.front();
        const auto& passedKey = caret[L"passedKey"];
        const auto& passedChar = caret[L"passedChar"];
        const bool charMatches = expectedChar != 0 ? passedChar.type == JsonValue::Type::Number && passedChar.Int() == expectedChar
                                                  : caret.object.count(L"passedChar") == 0;
        check(caret[L"moved"].Bool() && passedKey.type == JsonValue::Type::Number && passedKey.number == expectedKey && charMatches, label);
    };

    const SHORT letter = VkKeyScanW(L'l');
    const bool ready = letter != -1 && HIBYTE(letter) == 0 && SUCCEEDED(edit(L"sample", 0));
    check(ready, "caret test context accepts text and an unmodified l key");
    if (!ready) return;
    const UINT letterKey = LOBYTE(letter);

    expect(0, nullptr, 1, 0, 0, "selection without a key sends only the caret movement");
    expect(VK_ESCAPE, nullptr, 2, VK_ESCAPE, 0, "Esc keeps its key number without a text edit");
    expect(VK_RETURN, nullptr, 3, VK_RETURN, 0, "Enter keeps its key number without a text edit");
    expect(letterKey, nullptr, 4, 0, 0, "selection after a printable key does not report text input");
    expect(letterKey, L"l", 0, letterKey, L'l', "insertion before the caret sends the printable key and character");
    expect(0, nullptr, 5, 0, 0, "selection after insertion does not repeat the printable key or character");
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

        for (int i = 1; i <= 5; ++i) {
            const bool wasOpen = service->IsOpen();
            service->ToggleInputMode(nullptr);
            Pump();
            check(service->IsOpen() != wasOpen, "explicit toggle changes the input mode");
            check(toggleRequests == i, "each explicit toggle is sent once (Meltype.exe shows the indicator)");
        }

        service->Deactivate();
        service->Release();

        // 先行テストで通したキーを持ち込まないよう、別のテキストサービスで通知を確かめる。
        // 所有者のない TSF コンテキストを、文字を入力できる欄として扱う。
        ComPtr<ITfCompartmentMgr> contextCompartments;
        if (FAILED(context.As(&contextCompartments))) return 1;
        for (const GUID* id : {&GUID_COMPARTMENT_KEYBOARD_DISABLED, &GUID_COMPARTMENT_EMPTYCONTEXT}) {
            ComPtr<ITfCompartment> compartment;
            VARIANT enabled{};
            enabled.vt = VT_I4;
            if (FAILED(contextCompartments->GetCompartment(*id, &compartment)) ||
                FAILED(compartment->SetValue(client, &enabled))) return 1;
        }
        ComPtr<TextService> caretService;
        caretService.Attach(new TextService(nullptr));
        if (FAILED(caretService->ActivateEx(manager.Get(), client, 0)) ||
            FAILED(manager->SetFocus(document.Get()))) return 1;
        CheckCaretNotifications(context.Get(), client, *caretService.Get(), check);
        caretService->Deactivate();
        document->Pop(TF_POPF_ALL);
        manager->Deactivate();
    }
    DestroyWindow(window);
    CoUninitialize();
    return failed == 0 ? 0 : 1;
}
