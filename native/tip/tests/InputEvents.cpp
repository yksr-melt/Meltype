// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

// 実際の TSF コンパートメントと入力欄で、自動開閉・明示的な切り替え・タスクバーのメニューへのキーの受け渡しを確かめる。
// タスクバーはテストのプロセスに同じクラス名のウィンドウを作って再現する。パイプだけを記録用に置き換え、
// 文書なしのフォーカス通知もテスト側で固定し、通常のユーザーの Meltype・辞書・設定には接続しない。
#include "TextService.h"
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <textstor.h>

namespace meltype {
HINSTANCE g_module = nullptr;
int toggleRequests = 0;
double revision = 0;
bool composingTest = false;
std::wstring pendingText;
std::vector<std::wstring> committedText;
std::vector<bool> composingKeys;
// 空でなければ、CommitRequested は表示中の文字ではなくこの文字を確定する (変換すると表示と確定が変わる場合)
std::wstring commitResult;
int appCommits = 0;
std::vector<bool> commitFields;  // CommitRequested が欄の事実を送ったか
int eventRequests = 0;
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
    if (request.find("\"wait\"") != std::string::npos) return false;
    ++eventRequests;
    JsonValue message;
    if (!ParseJson(request, message)) return false;
    const auto& event = message[L"event"];
    const auto& type = event[L"type"].Str();
    std::string commits = "[]", view = "null";
    if (composingTest) {
        if (type == L"KeyPressed" && event[L"key"][L"vk"].Int() != 0xF3) {
            composingKeys.push_back(event[L"field"][L"composing"].Bool());
            pendingText += static_cast<wchar_t>(event[L"key"][L"ch"].Int());
        } else if (type == L"CommitRequested") {
            const std::wstring text = commitResult.empty() ? pendingText : commitResult;
            committedText.push_back(text);
            commitFields.push_back(!event[L"field"].IsNull());
            commits = "[{\"text\":\"" + ToUtf8(text) + "\"}]";
            pendingText.clear();
        } else if (type == L"CommittedByApp") {
            ++appCommits;
            pendingText.clear();
        }
        if (!pendingText.empty()) view = "{\"text\":\"" + ToUtf8(pendingText) + "\",\"converting\":false}";
    }
    const bool toggle = type == L"ModeToggleRequested" || (type == L"KeyPressed" && event[L"key"][L"vk"].Int() == 0xF3);
    if (toggle) ++toggleRequests;
    const bool open = (request.find("\"imeOpen\":true") != std::string::npos) != toggle;
    const char* shown = open ? "true" : "false";
    response = std::string("{\"version\":1,\"state\":{\"handlesInput\":true,\"imeOpen\":") + shown +
               ",\"languageMode\":\"" + (open ? "Japanese" : "Alphanumeric") + "\"" +
               ",\"modeIndicatorMs\":" + (toggle ? "1200" : "0") +
               ",\"capture\":{\"printableAscii\":true,\"printableAsciiExcept\":\"\",\"shortcuts\":[{\"vk\":32,\"modifiers\":0}]},\"revision\":" +
               std::to_string(++revision) + "},\"result\":{\"consumed\":true,\"commits\":" + commits + ",\"view\":" + view + "}}";
    return true;
}
}  // namespace meltype

// 文字・選択位置・編集ロックをアプリ側で持つ。実際の TSF が範囲と composition を管理し、パイプだけを上の応答に置き換える。
class TextStore final : public ITextStoreACP {
public:
    explicit TextStore(HWND window) : window_(window) {}
    std::wstring text;
    bool blockWrites = false;
    bool blockReads = false;
    bool failSelection = false;
    bool emptySelection = false;
    LONG CaretStart() const { return selection_.acpStart; }
    LONG CaretEnd() const { return selection_.acpEnd; }
    void MoveCaret(LONG position) {
        selection_ = {position, position, {TS_AE_NONE, FALSE}};
        if (sink_) sink_->OnSelectionChange();
    }
    void GrantPendingLock() {
        const DWORD flags = pendingLock_;
        pendingLock_ = 0;
        if (flags && sink_) sink_->OnLockGranted(flags);
    }
    STDMETHODIMP QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid != IID_IUnknown && iid != IID_ITextStoreACP) return E_NOINTERFACE;
        *result = static_cast<ITextStoreACP*>(this);
        AddRef();
        return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return ++refs_; }
    STDMETHODIMP_(ULONG) Release() override { const auto refs = --refs_; if (!refs) delete this; return refs; }
    STDMETHODIMP AdviseSink(REFIID iid, IUnknown* sink, DWORD) override {
        return sink->QueryInterface(iid, reinterpret_cast<void**>(sink_.ReleaseAndGetAddressOf()));
    }
    STDMETHODIMP UnadviseSink(IUnknown*) override { sink_.Reset(); return S_OK; }
    STDMETHODIMP RequestLock(DWORD flags, HRESULT* result) override {
        if (!sink_) return E_UNEXPECTED;
        const bool write = (flags & TS_LF_READWRITE) == TS_LF_READWRITE;
        if ((blockWrites && write) || (blockReads && !write)) {
            if (flags & TS_LF_SYNC) *result = TS_E_SYNCHRONOUS;
            else { pendingLock_ = flags; *result = TS_S_ASYNC; }
        } else {
            *result = sink_->OnLockGranted(flags);
        }
        return S_OK;
    }
    STDMETHODIMP GetStatus(TS_STATUS* status) override { *status = {0, TS_SS_NOHIDDENTEXT}; return S_OK; }
    STDMETHODIMP QueryInsert(LONG start, LONG end, ULONG, LONG* first, LONG* last) override {
        if (start < 0 || end < start || end > static_cast<LONG>(text.size())) return TS_E_INVALIDPOS;
        *first = start; *last = end; return S_OK;
    }
    STDMETHODIMP GetSelection(ULONG, ULONG count, TS_SELECTION_ACP* selection, ULONG* fetched) override {
        if (failSelection) return E_FAIL;
        *fetched = count && !emptySelection ? 1 : 0;
        if (*fetched) *selection = selection_;
        return S_OK;
    }
    STDMETHODIMP SetSelection(ULONG count, const TS_SELECTION_ACP* selection) override {
        if (count != 1) return E_INVALIDARG;
        selection_ = *selection; return S_OK;
    }
    STDMETHODIMP GetText(LONG start, LONG end, WCHAR* buffer, ULONG capacity, ULONG* copied,
                         TS_RUNINFO* runs, ULONG runCapacity, ULONG* runCount, LONG* next) override {
        if (end == -1) end = static_cast<LONG>(text.size());
        if (start < 0 || end < start || end > static_cast<LONG>(text.size())) return TS_E_INVALIDPOS;
        const auto length = (std::min)(static_cast<ULONG>(end - start), capacity);
        if (buffer && length) std::wmemcpy(buffer, text.data() + start, length);
        *copied = length;
        *runCount = runCapacity && end > start ? 1 : 0;
        if (*runCount) *runs = {capacity ? length : static_cast<ULONG>(end - start), TS_RT_PLAIN};
        *next = start + (capacity ? length : end - start);
        return S_OK;
    }
    STDMETHODIMP SetText(DWORD, LONG start, LONG end, const WCHAR* value, ULONG length, TS_TEXTCHANGE* change) override {
        if (start < 0 || end < start || end > static_cast<LONG>(text.size())) return TS_E_INVALIDPOS;
        text.replace(start, end - start, value, length);
        *change = {start, end, start + static_cast<LONG>(length)};
        // 実際の入力欄と同じく、書き換えた範囲の後ろの選択位置は文字数の差だけずらす
        const LONG delta = static_cast<LONG>(length) - (end - start);
        for (LONG* anchor : {&selection_.acpStart, &selection_.acpEnd}) {
            if (*anchor > end || (*anchor == end && end > start)) *anchor += delta;
        }
        return S_OK;
    }
    STDMETHODIMP InsertTextAtSelection(DWORD flags, const WCHAR* value, ULONG length, LONG* start, LONG* end, TS_TEXTCHANGE* change) override {
        const LONG first = selection_.acpStart, last = selection_.acpEnd;
        if (start) *start = first;
        if (end) *end = last;
        if (flags & TS_IAS_QUERYONLY) return S_OK;
        TS_TEXTCHANGE changed = {};
        const HRESULT hr = SetText(0, first, last, value, length, &changed);
        if (FAILED(hr)) return hr;
        if (change) *change = changed;
        if (end) *end = changed.acpNewEnd;
        selection_ = {changed.acpNewEnd, changed.acpNewEnd, {TS_AE_NONE, FALSE}};
        return S_OK;
    }
    STDMETHODIMP GetFormattedText(LONG, LONG, IDataObject**) override { return E_NOTIMPL; }
    STDMETHODIMP GetEmbedded(LONG, REFGUID, REFIID, IUnknown**) override { return E_NOTIMPL; }
    STDMETHODIMP QueryInsertEmbedded(const GUID*, const FORMATETC*, BOOL* insertable) override { *insertable = FALSE; return S_OK; }
    STDMETHODIMP InsertEmbedded(DWORD, LONG, LONG, IDataObject*, TS_TEXTCHANGE*) override { return E_NOTIMPL; }
    STDMETHODIMP InsertEmbeddedAtSelection(DWORD, IDataObject*, LONG*, LONG*, TS_TEXTCHANGE*) override { return E_NOTIMPL; }
    STDMETHODIMP RequestSupportedAttrs(DWORD, ULONG, const TS_ATTRID*) override { return S_OK; }
    STDMETHODIMP RequestAttrsAtPosition(LONG, ULONG, const TS_ATTRID*, DWORD) override { return S_OK; }
    STDMETHODIMP RequestAttrsTransitioningAtPosition(LONG, ULONG, const TS_ATTRID*, DWORD) override { return S_OK; }
    STDMETHODIMP FindNextAttrTransition(LONG, LONG halt, ULONG, const TS_ATTRID*, DWORD, LONG* next, BOOL* found, LONG* offset) override {
        *next = halt; *found = FALSE; *offset = 0; return S_OK;
    }
    STDMETHODIMP RetrieveRequestedAttrs(ULONG, TS_ATTRVAL*, ULONG* fetched) override { *fetched = 0; return S_OK; }
    STDMETHODIMP GetEndACP(LONG* end) override { *end = static_cast<LONG>(text.size()); return S_OK; }
    STDMETHODIMP GetActiveView(TsViewCookie* view) override { *view = 1; return S_OK; }
    STDMETHODIMP GetACPFromPoint(TsViewCookie, const POINT*, DWORD, LONG*) override { return E_NOTIMPL; }
    STDMETHODIMP GetTextExt(TsViewCookie, LONG, LONG, RECT* rect, BOOL* clipped) override {
        GetWindowRect(window_, rect); *clipped = FALSE; return S_OK;
    }
    STDMETHODIMP GetScreenExt(TsViewCookie, RECT* rect) override { GetWindowRect(window_, rect); return S_OK; }
    STDMETHODIMP GetWnd(TsViewCookie, HWND* window) override { *window = window_; return S_OK; }
private:
    ULONG refs_ = 1;
    HWND window_;
    TS_SELECTION_ACP selection_ = {0, 0, {TS_AE_NONE, FALSE}};
    Microsoft::WRL::ComPtr<ITextStoreACPSink> sink_;
    DWORD pendingLock_ = 0;
};

static void Pump() {
    MSG message;
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
}

// 文書なしの通知は SetFocus(null) では作れないため、GetFocus の結果だけを固定する。
// COM の同一性・購読先・参照数は実際の manager に任せ、入力欄と composition は実際の TSF を使う。
class FocusManager final : public ITfThreadMgr {
public:
    explicit FocusManager(ITfThreadMgr* manager) : manager_(manager) {}
    bool noDocument = false;
    STDMETHODIMP QueryInterface(REFIID iid, void** result) override { return manager_->QueryInterface(iid, result); }
    STDMETHODIMP_(ULONG) AddRef() override { return manager_->AddRef(); }
    STDMETHODIMP_(ULONG) Release() override { return manager_->Release(); }
    STDMETHODIMP Activate(TfClientId* client) override { return manager_->Activate(client); }
    STDMETHODIMP Deactivate() override { return manager_->Deactivate(); }
    STDMETHODIMP CreateDocumentMgr(ITfDocumentMgr** document) override { return manager_->CreateDocumentMgr(document); }
    STDMETHODIMP EnumDocumentMgrs(IEnumTfDocumentMgrs** documents) override { return manager_->EnumDocumentMgrs(documents); }
    STDMETHODIMP GetFocus(ITfDocumentMgr** document) override {
        if (!noDocument) return manager_->GetFocus(document);
        *document = nullptr;
        return S_FALSE;
    }
    STDMETHODIMP SetFocus(ITfDocumentMgr* document) override { return manager_->SetFocus(document); }
    STDMETHODIMP AssociateFocus(HWND window, ITfDocumentMgr* document, ITfDocumentMgr** previous) override {
        return manager_->AssociateFocus(window, document, previous);
    }
    STDMETHODIMP IsThreadFocus(BOOL* focus) override { return manager_->IsThreadFocus(focus); }
    STDMETHODIMP GetFunctionProvider(REFCLSID clsid, ITfFunctionProvider** provider) override { return manager_->GetFunctionProvider(clsid, provider); }
    STDMETHODIMP EnumFunctionProviders(IEnumTfFunctionProviders** providers) override { return manager_->EnumFunctionProviders(providers); }
    STDMETHODIMP GetGlobalCompartment(ITfCompartmentMgr** compartments) override { return manager_->GetGlobalCompartment(compartments); }
private:
    ITfThreadMgr* manager_;
};

int main(int argc, char** argv) {
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
    const bool compositionOnly = argc == 2 && std::strcmp(argv[1], "--composition-only") == 0;
    if (GetForegroundWindow() != window && !compositionOnly) {
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
        FocusManager focusManager(manager.Get());
        if (FAILED(service->ActivateEx(&focusManager, client, 0))) return 1;
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
        ComPtr<TextStore> store;
        store.Attach(new TextStore(normal));
        TfEditCookie edit = TF_INVALID_EDIT_COOKIE;
        if (FAILED(manager->CreateDocumentMgr(&document)) ||
            FAILED(document->CreateContext(client, 0, store.Get(), &context, &edit)) || FAILED(document->Push(context.Get()))) return 1;
        ComPtr<ITfDocumentMgr> otherDocument;
        ComPtr<ITfContext> otherContext;
        ComPtr<TextStore> otherStore;
        otherStore.Attach(new TextStore(normal));
        if (FAILED(manager->CreateDocumentMgr(&otherDocument)) ||
            FAILED(otherDocument->CreateContext(client, 0, otherStore.Get(), &otherContext, &edit)) ||
            FAILED(otherDocument->Push(otherContext.Get()))) return 1;
        manager->SetFocus(document.Get());
        composingTest = true;
        BOOL keyEaten = FALSE;
        service->OnKeyDown(context.Get(), 'T', 1, &keyEaten);
        check(keyEaten && pendingText == L"t", "the first consonant reaches the input service");
        check(store->text == L"t", "the first consonant is visible in the application text store");
        // 実機ログと同じ通知順を渡す。SetFocus(nullptr) はテスト用TSFではキーフォーカス喪失も起こすので、別に検証する。
        service->OnSetFocus(nullptr, document.Get());
        check(committedText.empty(), "a temporary document focus gap does not commit the first consonant");
        service->OnSetFocus(document.Get(), nullptr);
        service->OnKeyDown(context.Get(), 'E', 1, &keyEaten);
        check(composingKeys.size() == 2 && composingKeys.back(), "the next letter continues the same composition after document focus returns");
        service->OnSetFocus(FALSE);
        service->OnKillThreadFocus();
        check(committedText.empty() && pendingText == L"te", "losing keyboard and thread focus keeps input when its document remains focused");
        const int beforeRestore = eventRequests;
        service->OnSetThreadFocus();
        service->OnSetFocus(TRUE);
        check(eventRequests == beforeRestore + 1, "restoring thread and keyboard focus sends only one focus update");
        service->OnSetFocus(otherDocument.Get(), document.Get());
        check(committedText.size() == 1 && committedText.front() == L"te", "moving to a different document commits the complete input once");
        service->OnSetFocus(FALSE);
        service->OnKillThreadFocus();
        check(committedText.size() == 1, "later focus-loss notifications do not commit the input twice");
        auto resetInput = [&](const std::wstring& initial = L"") {
            service->OnSetFocus(otherDocument.Get(), document.Get());
            const HRESULT hr = RunEditSession(context.Get(), client, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie ec) {
                ComPtr<ITfRange> range, end;
                context->GetStart(ec, &range);
                context->GetEnd(ec, &end);
                range->ShiftEndToRange(ec, end.Get(), TF_ANCHOR_END);
                range->SetText(ec, 0, initial.data(), static_cast<LONG>(initial.size()));
                range->Collapse(ec, TF_ANCHOR_END);
                TF_SELECTION selection = {range.Get(), {TF_AE_NONE, FALSE}};
                return context->SetSelection(ec, 1, &selection);
            });
            if (FAILED(hr)) { ++failed; std::puts("FAIL resetting the text store"); }
            pendingText.clear();
            committedText.clear();
            composingKeys.clear();
            commitFields.clear();
            appCommits = 0;
            service->OnSetFocus(document.Get(), nullptr);
        };
        auto clearOther = [&] {
            const HRESULT hr = RunEditSession(otherContext.Get(), client, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie ec) {
                ComPtr<ITfRange> range, end;
                otherContext->GetStart(ec, &range);
                otherContext->GetEnd(ec, &end);
                range->ShiftEndToRange(ec, end.Get(), TF_ANCHOR_END);
                range->SetText(ec, 0, L"", 0);
                range->Collapse(ec, TF_ANCHOR_START);
                TF_SELECTION selection = {range.Get(), {TF_AE_NONE, FALSE}};
                return otherContext->SetSelection(ec, 1, &selection);
            });
            if (FAILED(hr)) { ++failed; std::puts("FAIL resetting the other text store"); }
        };
        auto press = [&](UINT vk) { service->OnKeyDown(context.Get(), vk, 1, &keyEaten); };

        resetInput();
        press('T');
        service->OnSetFocus(nullptr, document.Get());
        store->blockReads = true;
        service->OnSetFocus(document.Get(), nullptr);
        check(committedText.empty() && pendingText == L"t", "an unavailable read lock during focus restoration does not force a commit");
        store->blockReads = false;
        press('E');
        check(composingKeys.back() && store->text == L"te", "the next key validates and continues input after the read lock becomes available");

        resetInput();
        press('T');
        service->OnSetFocus(nullptr, document.Get());
        store->failSelection = true;
        service->OnSetFocus(document.Get(), nullptr);
        press('E');
        check(committedText.empty() && pendingText == L"te" && store->text == L"te" && composingKeys.back(),
              "unreadable selection after a focus gap keeps letters in the same composition");
        store->failSelection = false;

        resetInput();
        press('T');
        store->failSelection = true;
        press('E');
        press('S');
        check(committedText.empty() && pendingText == L"tes" && store->text == L"tes" && composingKeys[1] && composingKeys[2],
              "repeated selection failures without any focus change keep input continuous");
        store->failSelection = false;

        resetInput();
        press('T');
        store->emptySelection = true;
        press('E');
        check(committedText.empty() && pendingText == L"te" && store->text == L"te" && composingKeys.back(),
              "an unavailable selection without an error also keeps input continuous");
        store->emptySelection = false;

        resetInput();
        press('T');
        service->OnSetFocus(nullptr, document.Get());
        ComPtr<ITfContextOwnerCompositionServices> owner;
        context.As(&owner);
        check(SUCCEEDED(owner->TerminateComposition(nullptr)), "the application can terminate the TSF composition");
        service->OnSetFocus(document.Get(), nullptr);
        press('E');
        check(committedText.empty() && composingKeys.back() && store->text == L"te", "application-terminated input resumes across a temporary focus gap");

        resetInput(L"xx");
        press('T');
        service->OnSetFocus(nullptr, document.Get());
        store->MoveCaret(0);
        service->OnSetFocus(document.Get(), nullptr);
        check(committedText.size() == 1 && committedText.front() == L"t", "moving outside the composing range commits input even in the same context");
        press('E');
        check(!composingKeys.back() && pendingText == L"e" && store->text == L"exxt", "input at the new caret does not join the old range");

        // 旧欄を同期編集できない間に離れた: Core の確定は離れたときに 1 回だけ求め、旧欄には表示ではなく Core の確定の文字を入れる。
        // 模擬 Core は表示 (t) と違う文字 (と) を確定する。
        resetInput();
        press('T');
        commitResult = L"と";
        store->blockWrites = true;
        service->OnSetFocus(otherDocument.Get(), document.Get());
        check(committedText.size() == 1 && pendingText.empty() && appCommits == 0,
              "leaving a locked field asks the core to commit once instead of reporting the display as committed");
        check(commitFields.size() == 1 && !commitFields.back(), "the detached commit does not send field facts it could not read");
        service->OnKeyDown(otherContext.Get(), 'E', 1, &keyEaten);
        check(pendingText == L"e" && !composingKeys.back() && otherStore->text == L"e", "the first key in the new field starts new input there");
        store->blockWrites = false;
        store->GrantPendingLock();
        Pump();
        check(store->text == L"と" && committedText.size() == 1 && otherStore->text == L"e" && pendingText == L"e",
              "the old field receives the core's committed text once the lock is released");
        commitResult.clear();
        service->OnSetFocus(document.Get(), otherDocument.Get());
        clearOther();

        // A → B → A と戻り、旧欄の非同期の編集より先に打つ: 打鍵の編集セッションで先に旧範囲を確定し、新しい入力は後ろに続ける。
        resetInput();
        press('T');
        commitResult = L"とう";
        store->blockWrites = true;
        service->OnSetFocus(otherDocument.Get(), document.Get());
        store->blockWrites = false;
        service->OnSetFocus(document.Get(), otherDocument.Get());
        press('E');
        check(store->text == L"とうe" && committedText.size() == 1 && pendingText == L"e" && !composingKeys.back(),
              "returning before the late edit commits only the old input and keeps new keys composing");
        commitResult.clear();
        press('S');
        check(store->text == L"とうes" && pendingText == L"es" && composingKeys.back(), "keys after the return continue the new composition");
        store->GrantPendingLock();
        Pump();
        check(store->text == L"とうes" && store->CaretStart() == 4 && store->CaretEnd() == 4 && committedText.size() == 1,
              "the late callback after the return changes neither the text nor the caret");

        // アプリが旧範囲の変換を先に終えた: 遅れた反映はアプリの文字を上書きせず、Core にも確定を送り直さない。
        resetInput();
        press('T');
        commitResult = L"と";
        store->blockWrites = true;
        service->OnSetFocus(otherDocument.Get(), document.Get());
        // アプリが変換を終えるときは、アプリ自身が編集ロックを与える。旧欄の非同期の編集はまだ待たせたまま
        store->blockWrites = false;
        check(SUCCEEDED(owner->TerminateComposition(nullptr)), "the application can terminate the detached composition");
        store->GrantPendingLock();
        Pump();
        check(store->text == L"t" && committedText.size() == 1 && appCommits == 0,
              "an application-ended old composition is neither overwritten nor committed again");

        // アプリが旧範囲の文字を書き換えた: 遅れた反映はアプリの文字を残す。
        resetInput();
        press('T');
        store->blockWrites = true;
        service->OnSetFocus(otherDocument.Get(), document.Get());
        store->blockWrites = false;
        RunEditSession(context.Get(), client, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie ec) {
            ComPtr<ITfRange> range, end;
            context->GetStart(ec, &range);
            context->GetEnd(ec, &end);
            range->ShiftEndToRange(ec, end.Get(), TF_ANCHOR_END);
            return range->SetText(ec, 0, L"x", 1);
        });
        store->GrantPendingLock();
        Pump();
        check(store->text == L"x" && committedText.size() == 1, "an application-rewritten old range keeps the application's text");
        commitResult.clear();

        resetInput();
        press('T');
        focusManager.noDocument = true;
        service->OnSetFocus(nullptr, document.Get());
        service->OnKillThreadFocus();
        check(pendingText.empty() && store->text == L"t", "leaving the thread resolves retained input while document focus is absent");
        const int afterNullDeparture = eventRequests;
        service->OnSetFocus(FALSE);
        service->OnKillThreadFocus();
        check(eventRequests == afterNullDeparture, "repeated loss notifications do not resend idle caret updates");
        focusManager.noDocument = false;
        service->OnSetFocus(document.Get(), nullptr);

        // フォーカス通知より先に、別の欄のキーが届いた: 同じく旧欄の確定を先に求めてから、新しい欄で始める。
        resetInput();
        press('T');
        commitResult = L"と";
        store->blockWrites = true;
        service->OnKeyDown(otherContext.Get(), 'E', 1, &keyEaten);
        check(pendingText == L"e" && !composingKeys.back() && otherStore->text == L"e" && committedText.size() == 1,
              "a key in a different context commits the locked old field before starting new input");
        store->blockWrites = false;
        store->GrantPendingLock();
        Pump();
        check(store->text == L"と" && otherStore->text == L"e" && committedText.size() == 1,
              "the old field receives the committed text after a key arrives before the focus notification");
        commitResult.clear();
        service->OnSetFocus(document.Get(), otherDocument.Get());
        clearOther();
        resetInput();
        press('T');
        service->OnSetFocus(otherDocument.Get(), document.Get());
        service->OnKeyDown(otherContext.Get(), 'E', 1, &keyEaten);
        check(committedText.size() == 1 && committedText.front() == L"t" && !composingKeys.back() &&
              store->text == L"t" && otherStore->text == L"e", "switching documents commits the old field and starts new input in the new field");

        // 旧欄を編集できないまま、その context が取り外された: 遅れた反映をせず、Core にも 2 回目の確定を送らない。
        commitResult = L"え";
        otherStore->blockWrites = true;
        service->OnSetFocus(document.Get(), otherDocument.Get());
        check(committedText.size() == 2 && committedText.back() == L"え" && pendingText.empty(), "leaving the locked new field commits it once");
        otherDocument->Pop(TF_POPF_ALL);
        otherStore->blockWrites = false;
        otherStore->GrantPendingLock();
        Pump();
        check(committedText.size() == 2 && appCommits == 0 && otherStore->text == L"e",
              "removing the owning context neither writes late text nor commits again");
        commitResult.clear();
        composingTest = false;
        if (!compositionOnly) {
            auto takesKey = [&](UINT vk) {
                BOOL eaten = FALSE;
                check(SUCCEEDED(service->OnTestKeyDown(context.Get(), vk, 1, &eaten)), "TSF key test succeeds");
                return eaten != FALSE;
            };
            check(takesKey('U'), "ordinary input receives U");
            check(takesKey(VK_SPACE), "ordinary input captures Space as an input-mode shortcut");
            check(!takesKey(VK_NUMPAD5), "a numpad digit goes to the application while nothing is typed");
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
        }
        service->Deactivate();
        service->Release();
        manager->Deactivate();
    }
    DestroyWindow(window);
    CoUninitialize();
    return failed == 0 ? 0 : 1;
}
