// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai

#include "TextService.h"

#include <InputScope.h>
#include <wrl/client.h>

#include <algorithm>
#include <cstdlib>
#include <cwctype>

#include "DisplayAttributes.h"
#include "LangBar.h"

namespace meltype {
namespace {

// Meltype.exe のプロトコルの版 (ImeHost.Version)
constexpr int kImeVersion = 1;

class EditSession final : public ITfEditSession {
public:
    explicit EditSession(std::function<HRESULT(TfEditCookie)> action) : action_(std::move(action)) {}

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfEditSession)) {
            *ppv = static_cast<ITfEditSession*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        ULONG count = InterlockedDecrement(&refs_);
        if (count == 0) delete this;
        return count;
    }
    STDMETHODIMP DoEditSession(TfEditCookie ec) override { return action_(ec); }

private:
    LONG refs_ = 1;
    std::function<HRESULT(TfEditCookie)> action_;
};

// 入力欄の種類 (InputScope) のプロパティ。{1713DD5A-68E7-4A5B-9AF6-592A595C778D}
constexpr GUID kPropInputScope = {0x1713dd5a, 0x68e7, 0x4a5b, {0x9a, 0xf6, 0x59, 0x2a, 0x59, 0x5c, 0x77, 0x8d}};

// パスワード・暗証番号の入力欄か (入力欄の種類で知らせてくるアプリ向け。ストアアプリの PasswordBox など)
bool IsSecretScope(InputScope scope) {
    switch (scope) {
        case IS_PASSWORD: case IS_NUMERIC_PASSWORD: case IS_NUMERIC_PIN: case IS_ALPHANUMERIC_PIN: case IS_ALPHANUMERIC_PIN_SET:
            return true;
        default:
            return false;
    }
}

// 送り直したキーに付ける印 (SendInput の dwExtraInfo)。'MLTP' (Meltype.exe のキーボードフックが送る印 'MELT' とは別にする)
constexpr ULONG_PTR kReinjectMarker = 0x4D4C5450;

// 修飾キーだけのキー (それ自体では入力にならない)
bool IsModifier(UINT vk) {
    return ToggleShortcut::IsModifier(vk) || vk == VK_CAPITAL;
}


bool KeyDown(int vk) { return (GetKeyState(vk) & 0x8000) != 0; }

// このスレッドでメニューを開いている (Win+X のメニュー・右クリックのメニュー・メニューバーなど: issue #862)。
// メニューの間に打つキーはメニューのアクセスキー (Win+X の U など) なので、入力欄の IME として受け取らない
bool InMenuMode() {
    GUITHREADINFO info = {sizeof(info)};
    return GetGUIThreadInfo(GetCurrentThreadId(), &info) &&
           (info.flags & (GUI_INMENUMODE | GUI_POPUPMENUMODE | GUI_SYSTEMMENUMODE)) != 0;
}

// US 配列の Alt + ` (半角/全角キー相当)。Alt はキーイベントの lParam も確認する。
UINT ShortcutKey(UINT vk, LPARAM lParam) {
    const auto bits = static_cast<ULONG_PTR>(lParam);
    const UINT scan = static_cast<UINT>((bits >> 16) & 0xFF);
    return ToggleShortcut::NormalizeShortcutKey(vk, scan, (bits & (static_cast<ULONG_PTR>(1) << 24)) != 0,
        scan == 0x29 ? MapVirtualKeyExW(0x29, MAPVK_VSC_TO_VK_EX, GetKeyboardLayout(0)) : 0);
}

UINT ShortcutSlot(UINT vk, LPARAM lParam = 0) {
    // JIS 半角/全角の仮想キー変化だけを同じスロットで照合する。
    return ShortcutKey(vk, lParam);
}

// Alt/Win を押したまま主キーだけを飲み込むと、アプリ側で修飾キー単独押しと判定される。
// 割り当てのない 0xE8 を送り、Alt の単独押しによるメニュー移動を防ぐ。
void MaskSystemModifier() {
    if (!KeyDown(VK_MENU) && !KeyDown(VK_LWIN) && !KeyDown(VK_RWIN)) return;
    INPUT mask[2] = {};
    mask[0].type = mask[1].type = INPUT_KEYBOARD;
    mask[0].ki.wVk = mask[1].ki.wVk = 0xE8;
    mask[0].ki.dwExtraInfo = mask[1].ki.dwExtraInfo = kReinjectMarker;
    mask[1].ki.dwFlags = KEYEVENTF_KEYUP;
    SendInput(2, mask, sizeof(INPUT));
}

// そのキーで入力される文字 (無ければ 0)
wchar_t CharOf(UINT vk, LPARAM lParam) {
    BYTE state[256] = {};
    if (!GetKeyboardState(state)) return 0;
    // Ctrl を押していても、文字としては Ctrl なしの文字を見る
    state[VK_CONTROL] = state[VK_LCONTROL] = state[VK_RCONTROL] = 0;
    if (!KeyDown(VK_MENU)) state[VK_MENU] = state[VK_LMENU] = state[VK_RMENU] = 0;
    wchar_t buffer[8] = {};
    UINT scan = (lParam >> 16) & 0xFF;
    // 4 = キーボードの状態 (デッドキー) を変えない (Windows 10 1607 以降)
    int n = ToUnicode(vk, scan, state, buffer, 8, 4);
    return n == 1 ? buffer[0] : 0;
}

std::string RandomId() {
    char buffer[40];
    sprintf_s(buffer, "%lu-%lu-%llu", GetCurrentProcessId(), GetCurrentThreadId(), GetTickCount64());
    return buffer;
}

// 同じ COM のオブジェクトか (インターフェースのポインターは、取り出し方で違うことがあるので IUnknown で比べる)
bool SameObject(IUnknown* a, IUnknown* b) {
    if (a == b) return true;
    if (a == nullptr || b == nullptr) return false;
    Microsoft::WRL::ComPtr<IUnknown> ua, ub;
    if (FAILED(a->QueryInterface(IID_PPV_ARGS(&ua))) || FAILED(b->QueryInterface(IID_PPV_ARGS(&ub)))) return false;
    return ua.Get() == ub.Get();
}

// 変換中の文字の下線を消す (その変換中の文字がある入力欄で)
void ClearAttributeOf(TfEditCookie ec, ITfComposition* composition) {
    if (composition == nullptr) return;
    Microsoft::WRL::ComPtr<ITfRange> range;
    Microsoft::WRL::ComPtr<ITfContext> context;
    Microsoft::WRL::ComPtr<ITfProperty> property;
    if (SUCCEEDED(composition->GetRange(&range)) && SUCCEEDED(range->GetContext(&context)) &&
        SUCCEEDED(context->GetProperty(GUID_PROP_ATTRIBUTE, &property))) {
        property->Clear(ec, range.Get());
    }
}

std::wstring ProcessName() {
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    const wchar_t* name = wcsrchr(path, L'\\');
    std::wstring result = name ? name + 1 : path;
    if (result.size() > 4 && _wcsicmp(result.c_str() + result.size() - 4, L".exe") == 0) result.resize(result.size() - 4);
    return result;
}

// Chromium 系のブラウザーか。本物のパスワード欄では、Chromium は IME を無効にした入力欄 (空の文書) に切り替える (ContextDisabled で分かる)。
// 一方で、ふつうの入力欄 (Twitch のコメント欄・U-NEXT の検索欄など) を、アプリが知らせる入力欄の種類でパスワードと言ってくることがある (issue #878)
bool IsChromiumBrowser() {
    static const bool chromium = [] {
        std::wstring name = ProcessName();
        for (const wchar_t* browser : {L"chrome", L"msedge", L"brave", L"vivaldi", L"opera", L"chromium"}) {
            if (_wcsicmp(name.c_str(), browser) == 0) return true;
        }
        return false;
    }();
    return chromium;
}

// この impl があるフォルダーの名前 (Register-Tip.ps1 が impl の SHA-256 にする) の JSON の文字列。
// Meltype.exe が、同梱の impl の SHA-256 と照合する
const std::string& ImplId() {
    static const std::string id = [] {
        wchar_t path[MAX_PATH] = {};
        GetModuleFileNameW(g_module, path, MAX_PATH);
        std::wstring folder = path;
        folder = folder.substr(0, folder.find_last_of(L'\\'));
        return JsonString(folder.substr(folder.find_last_of(L'\\') + 1));
    }();
    return id;
}

}  // namespace

HRESULT RunEditSession(ITfContext* context, TfClientId clientId, DWORD flags, std::function<HRESULT(TfEditCookie)> action) {
    if (context == nullptr) return E_INVALIDARG;
    auto* session = new EditSession(std::move(action));
    HRESULT result = E_FAIL;
    HRESULT hr = context->RequestEditSession(clientId, session, flags, &result);
    session->Release();
    return FAILED(hr) ? hr : result;
}

TextService::TextService(HWND stub) : stub_(stub) {}

TextService::~TextService() { DestroyMessageWindow(); }

STDMETHODIMP TextService::QueryInterface(REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfTextInputProcessor) || IsEqualIID(riid, IID_ITfTextInputProcessorEx)) {
        *ppv = static_cast<ITfTextInputProcessorEx*>(this);
    } else if (IsEqualIID(riid, IID_ITfThreadMgrEventSink)) {
        *ppv = static_cast<ITfThreadMgrEventSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfKeyEventSink)) {
        *ppv = static_cast<ITfKeyEventSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfCompositionSink)) {
        *ppv = static_cast<ITfCompositionSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfDisplayAttributeProvider)) {
        *ppv = static_cast<ITfDisplayAttributeProvider*>(this);
    } else if (IsEqualIID(riid, IID_ITfCompartmentEventSink)) {
        *ppv = static_cast<ITfCompartmentEventSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfTextEditSink)) {
        *ppv = static_cast<ITfTextEditSink*>(this);
    }
    if (*ppv == nullptr) return E_NOINTERFACE;
    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) TextService::AddRef() { return InterlockedIncrement(&refs_); }

STDMETHODIMP_(ULONG) TextService::Release() {
    ULONG count = InterlockedDecrement(&refs_);
    if (count == 0) delete this;
    return count;
}


// ---- 起動・終了 ----

STDMETHODIMP TextService::Activate(ITfThreadMgr* threadMgr, TfClientId clientId) { return ActivateEx(threadMgr, clientId, 0); }

STDMETHODIMP TextService::ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD flags) {
    threadMgr_ = threadMgr;
    threadMgr_->AddRef();
    clientId_ = clientId;
    // Windows は、同じアプリの中で一度終えた IME を、同じオブジェクトのまま起動し直すことがある。前の状態を初期化する
    // (終わったときの印 deactivated_ が残ると、二度と Meltype.exe とやり取りしなくなる)
    deactivated_ = false;
    state_ = ServerState{};
    pendingOpen_ = -1;
    ownEdit_ = false;
    secretChecked_ = false;
    ForgetSecretField();
    markerSeen_ = false;
    resumeRange_.Reset();
    resumeContext_.Reset();
    resumeText_.clear();
    lastConverting_ = false;
    reinjectGuardVk_ = 0;
    reinjectGuardCount_ = 0;
    abandoned_.clear();
    staleCommitTries_ = 0;
    staleVk_ = 0;
    for (Reinjected& pending : reinjected_) pending = {};
    passVk_ = 0;
    passReason_ = nullptr;
    for (bool& eaten : eatenDown_) eaten = false;
    // サインインや UAC の画面では何もしない。
    // Meltype.exe より高い権限で動いているアプリ (管理者として動くアプリなど) は、つなぐときに断る (Pipe.cpp の IsTrustedServer)
    disabled_ = (flags & TF_TMAE_SECUREMODE) != 0;
    instance_ = RandomId();
    TipLog(L"Activate (%s)%s", ProcessName().c_str(), disabled_ ? L" 無効: セキュリティ保護された画面" : L"");

    ITfSource* source = nullptr;
    if (SUCCEEDED(threadMgr_->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
        source->AdviseSink(IID_ITfThreadMgrEventSink, static_cast<ITfThreadMgrEventSink*>(this), &threadMgrCookie_);
        source->Release();
    }
    ITfKeystrokeMgr* keystroke = nullptr;
    if (SUCCEEDED(threadMgr_->QueryInterface(IID_ITfKeystrokeMgr, reinterpret_cast<void**>(&keystroke)))) {
        keystroke->AdviseKeyEventSink(clientId_, static_cast<ITfKeyEventSink*>(this), TRUE);
        keystroke->Release();
    }

    // 下線の種類を GUID → 番号 (atom) にしておく
    ITfCategoryMgr* category = nullptr;
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_ITfCategoryMgr, reinterpret_cast<void**>(&category)))) {
        category->RegisterGUID(GUID_AttrInput, &atomInput_);
        category->RegisterGUID(GUID_AttrConverted, &atomConverted_);
        category->RegisterGUID(GUID_AttrTarget, &atomTarget_);
        category->Release();
    }

    // 日本語 / 英数 (IME の ON/OFF)。Meltype は日本語から始める (半角/全角 を押さなくてよいのが Meltype なので)
    // 何もしないプロセス (サインインの画面) では、入力モードを変えず、タスクバーの「あ」も出さない (キーはそのまま通るので)
    ITfCompartmentMgr* compartments = nullptr;
    if (!disabled_ && SUCCEEDED(threadMgr_->QueryInterface(IID_ITfCompartmentMgr, reinterpret_cast<void**>(&compartments)))) {
        ITfCompartment* openClose = nullptr;
        if (SUCCEEDED(compartments->GetCompartment(GUID_COMPARTMENT_KEYBOARD_OPENCLOSE, &openClose))) {
            VARIANT value;
            VariantInit(&value);
            value.vt = VT_I4;
            value.lVal = 1;
            openClose->SetValue(clientId_, &value);
            ITfSource* compartmentSource = nullptr;
            if (SUCCEEDED(openClose->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&compartmentSource)))) {
                compartmentSource->AdviseSink(IID_ITfCompartmentEventSink, static_cast<ITfCompartmentEventSink*>(this), &compartmentCookie_);
                compartmentSource->Release();
            }
            openClose->Release();
        }
        compartments->Release();
    }
    open_ = !disabled_;

    candidates_ = std::make_unique<CandidateWindow>([this](int index) { SelectCandidate(index); });

    if (!disabled_) {
        langBar_ = new LangBarButton(this);
        ITfLangBarItemMgr* items = nullptr;
        if (SUCCEEDED(threadMgr_->QueryInterface(IID_ITfLangBarItemMgr, reinterpret_cast<void**>(&items)))) {
            items->AddItem(langBar_);
            items->Release();
        }
    }

    ITfDocumentMgr* focus = nullptr;
    if (SUCCEEDED(threadMgr_->GetFocus(&focus)) && focus != nullptr) {
        AdviseTextEditSink(focus);
        focus->Release();
    }
    if (!disabled_) {
        CreateMessageWindow();
        RefreshPreservedKeys();
        Sync();
    }
    return S_OK;
}

STDMETHODIMP TextService::Deactivate() {
    DestroyMessageWindow();
    UnpreserveKeys();
    TipLog(L"Deactivate");
    if (composition_ != nullptr && compositionContext_ != nullptr) {
        // 確定してから終わる
        ITfContext* context = compositionContext_;
        context->AddRef();
        RunEditSession(context, clientId_, TF_ES_SYNC | TF_ES_READWRITE, [this, context](TfEditCookie ec) {
            CommitInSession(ec, context, true, true);
            EndComposition(ec);
            return S_OK;
        });
        context->Release();
    }
    if (!disabled_) {
        JsonValue reply;
        Send("InstanceDestroyed", "", "", reply, 100, true);
    }
    // ここから後 (アプリが変換中の文字を確定した通知・あとから動く非同期の編集セッション) は、Meltype.exe につなぎ直さない
    deactivated_ = true;
    resumeRange_.Reset();
    resumeContext_.Reset();
    ForgetSecretField();
    // 手放した変換中の文字は、終われるならここで終える (下線を残さない)。終えられなかったものは、手放したときに頼んだ
    // 非同期の編集セッションに任せて、ここでは持たない (入力欄を持ち続けて、この IME が解放されなくなるのを防ぐ)
    EndAbandonedNow();
    abandoned_.clear();
    pipe_.Close();
    candidates_.reset();
    UnadviseTextEditSink();

    if (langBar_ != nullptr) {
        ITfLangBarItemMgr* items = nullptr;
        if (SUCCEEDED(threadMgr_->QueryInterface(IID_ITfLangBarItemMgr, reinterpret_cast<void**>(&items)))) {
            items->RemoveItem(langBar_);
            items->Release();
        }
        langBar_->Detach();
        langBar_->Release();
        langBar_ = nullptr;
    }

    ITfCompartmentMgr* compartments = nullptr;
    if (compartmentCookie_ != TF_INVALID_COOKIE && SUCCEEDED(threadMgr_->QueryInterface(IID_ITfCompartmentMgr, reinterpret_cast<void**>(&compartments)))) {
        ITfCompartment* openClose = nullptr;
        if (SUCCEEDED(compartments->GetCompartment(GUID_COMPARTMENT_KEYBOARD_OPENCLOSE, &openClose))) {
            ITfSource* source = nullptr;
            if (SUCCEEDED(openClose->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
                source->UnadviseSink(compartmentCookie_);
                source->Release();
            }
            openClose->Release();
        }
        compartments->Release();
    }
    compartmentCookie_ = TF_INVALID_COOKIE;

    ITfKeystrokeMgr* keystroke = nullptr;
    if (SUCCEEDED(threadMgr_->QueryInterface(IID_ITfKeystrokeMgr, reinterpret_cast<void**>(&keystroke)))) {
        keystroke->UnadviseKeyEventSink(clientId_);
        keystroke->Release();
    }
    ITfSource* source = nullptr;
    if (threadMgrCookie_ != TF_INVALID_COOKIE && SUCCEEDED(threadMgr_->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
        source->UnadviseSink(threadMgrCookie_);
        source->Release();
    }
    threadMgrCookie_ = TF_INVALID_COOKIE;

    if (composition_ != nullptr) {
        composition_->Release();
        composition_ = nullptr;
    }
    if (compositionContext_ != nullptr) {
        compositionContext_->Release();
        compositionContext_ = nullptr;
    }
    threadMgr_->Release();
    threadMgr_ = nullptr;
    clientId_ = TF_CLIENTID_NULL;
    return S_OK;
}

// ---- フォーカス ----

STDMETHODIMP TextService::OnSetFocus(ITfDocumentMgr* focus, ITfDocumentMgr*) {
    // 別の入力欄に移ったら、変換中の内容は確定する (前の入力欄のキャレットは動かさない)。
    // できればその場で確定する (あとで確定すると、それまでに新しい入力欄で打ったキーが、前の入力欄の変換中の文字に入る)
    if (composition_ != nullptr) {
        if (!CommitNow(true, true)) CommitAsync(true, true);
    } else if (!disabled_) {
        JsonValue reply;
        if (Send("CaretMoved", "", CaretMoved(true), reply, 100, true)) ApplyState(reply[L"state"]);
    }
    // アプリが確定した打ちかけの文字は、別の入力欄に移ったので確定したことにする
    ResumeOrCommit(nullptr);
    ForgetSecretField();
    if (candidates_) candidates_->Hide();
    AdviseTextEditSink(focus);
    if (focus != nullptr && !disabled_) Sync(true);
    return S_OK;
}

STDMETHODIMP TextService::OnSetFocus(BOOL foreground) {
    if (!foreground && candidates_) candidates_->Hide();
    // 別アプリ (Meltype の設定画面など) から戻ったとき、キー入力の前に状態と設定を受け取り直す
    if (foreground && !disabled_) Sync(true);
    // キーを受け取らなくなった (スレッドが前面でなくなった・ほかの IME に移った): 受け取ったキーを離したことは届かないかもしれないので忘れる
    // (このスレッドで次に押したキーの、離したことまで受け取らないように)
    if (!foreground) {
        for (bool& eaten : eatenDown_) eaten = false;
    }
    return S_OK;
}

void TextService::AdviseTextEditSink(ITfDocumentMgr* documentMgr) {
    UnadviseTextEditSink();
    if (documentMgr == nullptr) return;
    ITfContext* context = nullptr;
    if (FAILED(documentMgr->GetTop(&context)) || context == nullptr) return;
    ITfSource* source = nullptr;
    if (SUCCEEDED(context->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
        if (SUCCEEDED(source->AdviseSink(IID_ITfTextEditSink, static_cast<ITfTextEditSink*>(this), &editSinkCookie_))) {
            editSinkContext_ = context;
            editSinkContext_->AddRef();
        }
        source->Release();
    }
    ownEdit_ = false;
    context->Release();
}

void TextService::UnadviseTextEditSink() {
    if (editSinkContext_ == nullptr) return;
    ITfSource* source = nullptr;
    if (SUCCEEDED(editSinkContext_->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
        source->UnadviseSink(editSinkCookie_);
        source->Release();
    }
    editSinkContext_->Release();
    editSinkContext_ = nullptr;
    editSinkCookie_ = TF_INVALID_COOKIE;
}

STDMETHODIMP TextService::OnEndEdit(ITfContext* context, TfEditCookie ec, ITfEditRecord* record) {
    if (record == nullptr) return S_OK;
    // 自分の編集 (確定・変換中の文字) が終わった通知は、キャレットが動いたことにしない
    if (ownEdit_) {
        ownEdit_ = false;
        return S_OK;
    }
    BOOL changed = FALSE;
    if (FAILED(record->GetSelectionStatus(&changed))) return S_OK;
    if (composition_ == nullptr || context != compositionContext_) {
        // 変換していないとき (変換中の文字が別の入力欄に残っているときも) に、キャレットが動いた・文字が変わった。
        // コードの行の判定・前の語の確定し直しは、Meltype.exe が今のキャレットの前の文字で決める
        SendCaret(ec, context, changed != FALSE);
        return S_OK;
    }
    if (!changed) return S_OK;
    // クリック・矢印などで、キャレットが変換中の文字の外に出たら確定する
    TF_SELECTION selection = {};
    ULONG fetched = 0;
    if (FAILED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) || fetched == 0) return S_OK;
    ITfRange* range = nullptr;
    bool outside = false;
    if (SUCCEEDED(composition_->GetRange(&range))) {
        LONG startVsStart = 0, endVsEnd = 0;
        selection.range->CompareStart(ec, range, TF_ANCHOR_START, &startVsStart);
        selection.range->CompareEnd(ec, range, TF_ANCHOR_END, &endVsEnd);
        outside = startVsStart < 0 || endVsEnd > 0;
        range->Release();
    }
    selection.range->Release();
    if (outside) {
        TipLog(L"キャレットが変換中の文字の外に出たので確定します");
        // 確定したあとで、キャレットを確定した文字の後ろに戻さない (クリックした所に打てるように)
        CommitAsync(true);
    }
    return S_OK;
}

void TextService::SendCaret(TfEditCookie ec, ITfContext* context, bool selectionChanged) {
    if (disabled_ || !state_.known || !state_.active) return;
    std::string field;
    if (!ReadField(ec, context, field)) return;
    // passed: キャレットを動かしたキー (少し前にアプリに通したキー)。無ければ、キャレットが動いたなら 0 (クリックなど)、文字が変わっただけなら無し。
    // 改行の後の自動のインデントなど、キーに続くアプリの編集でも通知が来るので、通したキーは少しの間覚えておく。
    // ただし、マウスのボタンを押しているあいだに選択が変わったら、キーの直後でもクリックとして伝える
    const bool clicked = selectionChanged && ((GetAsyncKeyState(VK_LBUTTON) | GetAsyncKeyState(VK_RBUTTON)) & 0x8000) != 0;
    const bool recent = !clicked && GetTickCount64() - passedTime_ < 500;
    std::string caret = std::string(",\"caret\":{\"moved\":") + (selectionChanged || recent ? "true" : "false");
    if (recent) caret += ",\"passedKey\":" + std::to_string(passedVk_) + (passedControl_ ? ",\"ctrl\":true" : "");
    else if (selectionChanged) caret += ",\"passedKey\":0";
    caret += "}";
    JsonValue reply;
    // キャレットを動かすたびに送るので、応答が遅ければ待たない (表示が次の出来事まで古いだけ)
    if (Send("CaretMoved", field, caret, reply, 100, true)) {
        ApplyState(reply[L"state"]);
        UpdateLangBar();
    }
}

bool TextService::IsSecretField(TfEditCookie ec, ITfContext* context) {
    // 入力欄の種類 (InputScope) がパスワード・暗証番号か。
    // (入力欄が開いたときに非同期の編集セッションで読むと、それが終わるまでキーが届かなくなるアプリがあるので、キーの編集セッションの中で読む)
    // 種類は 2 か所にある: Windows が持つもの (SetInputScope などで決めたもの) と、アプリが自分で知らせるもの (自前で TSF に対応したアプリ。
    // ブラウザーなど)。どちらかがパスワードと言えばパスワード欄とする。どちらにも無ければ、種類を決めていない入力欄
    // (ふつうのアプリの多く。ブラウザーはパスワード欄では IME を無効にもするので、それは ContextDisabled で見る)。
    // 種類の値はあるのに中身を読めないときは、パスワード欄かもしれないので、パスワード欄として扱う (打った文字を Meltype.exe に送らない)。
    // 問い合わせ自体ができないとき (対応していないアプリ・選択が無い) は、種類を決めていない入力欄と同じにする
    // (止めると、そのアプリでは Meltype IME がまったく使えなくなるため)
    // 入力を覚えないように求める入力欄 (IS_PRIVATE。ブラウザーのシークレット / InPrivate ウィンドウ) かも、ここで読んで privateField_ に残す
    privateField_ = false;
    auto unreadable = [](const wchar_t* step) {
        TipLog(L"入力欄の種類を読めないので、パスワード欄として扱います (%s)", step);
        return true;
    };
    Microsoft::WRL::ComPtr<ITfProperty> windowsProperty;
    Microsoft::WRL::ComPtr<ITfReadOnlyProperty> appProperty;
    if (FAILED(context->GetProperty(kPropInputScope, &windowsProperty))) windowsProperty.Reset();
    if (FAILED(context->GetAppProperty(kPropInputScope, &appProperty))) appProperty.Reset();
    // 調べるとき用: どこから読めるか (変わったときだけ書く)
    const wchar_t* source = windowsProperty && appProperty ? L"Windows とアプリ" : windowsProperty ? L"Windows" : appProperty ? L"アプリ" : L"無い";
    if (scopeSource_ != source) TipLog(L"入力欄の種類を読むところ: %s", source);
    scopeSource_ = source;
    if (!windowsProperty && !appProperty) return false;
    TF_SELECTION selection = {};
    ULONG fetched = 0;
    if (FAILED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) || fetched == 0) return false;
    const wchar_t* failed = nullptr;
    bool secret = false;
    bool appSaysSecret = false;
    std::wstring secretScopes;  // 調べるとき用: パスワードとみなした入力欄の種類 (数値)
    for (ITfReadOnlyProperty* property : {static_cast<ITfReadOnlyProperty*>(windowsProperty.Get()), appProperty.Get()}) {
        if (property == nullptr) continue;
        bool fromApp = property == appProperty.Get();
        VARIANT value;
        VariantInit(&value);
        if (FAILED(property->GetValue(ec, selection.range, &value))) {
            // 問い合わせられない: 種類を決めていないのと同じ
        } else if (value.vt == VT_UNKNOWN && value.punkVal != nullptr) {
            Microsoft::WRL::ComPtr<ITfInputScope> scope;
            InputScope* scopes = nullptr;
            UINT count = 0;
            if (FAILED(value.punkVal->QueryInterface(IID_ITfInputScope, reinterpret_cast<void**>(scope.GetAddressOf())))) {
                failed = L"ITfInputScope";
            } else if (FAILED(scope->GetInputScopes(&scopes, &count)) || (scopes == nullptr && count != 0)) {
                failed = L"GetInputScopes";
            } else {
                for (UINT i = 0; i < count; i++) {
                    if (IsSecretScope(scopes[i])) {
                        if (fromApp) appSaysSecret = true;
                        else secret = true;
                        secretScopes += (secretScopes.empty() ? L"" : L", ") + std::wstring(fromApp ? L"アプリ " : L"Windows ") + std::to_wstring(static_cast<int>(scopes[i]));
                    }
                    if (scopes[i] == IS_PRIVATE) privateField_ = true;
                }
            }
            if (scopes != nullptr) CoTaskMemFree(scopes);
        } else if (value.vt != VT_EMPTY && value.vt != VT_NULL && value.vt != VT_UNKNOWN) {
            // 種類を決めていない入力欄は VT_EMPTY・VT_NULL か空の VT_UNKNOWN。それ以外の形は知らないので読めなかったとする
            failed = L"VARIANT";
        }
        VariantClear(&value);
    }
    selection.range->Release();
    if (failed != nullptr) return unreadable(failed);
    if (appSaysSecret) {
        // Chromium 系のブラウザーで、アプリだけがパスワードと言い、IME も無効にしていない入力欄は、パスワード欄とみなさない (issue #878)
        if (IsChromiumBrowser() && !secret && !ContextDisabled(context)) {
            if (lastSecretScopes_ != secretScopes) TipLog(L"アプリがパスワード欄と知らせてきましたが、IME を無効にしていないので入力欄として扱います (%s)", secretScopes.c_str());
        } else {
            secret = true;
        }
    }
    if (secret && lastSecretScopes_ != secretScopes) TipLog(L"パスワードの入力欄の種類: %s", secretScopes.c_str());
    lastSecretScopes_ = secretScopes;
    return secret;
}

int TextService::ReadSecretField(ITfContext* context) {
    // キーごとに読み直す (同じ入力欄のまま、アプリが入力欄の種類をパスワードに変えることがあるので)。
    // キーを受け取る前に分かれば、パスワード欄のキーは受け取らずにアプリへそのまま渡せる (送り直さずに済む)
    bool secret = false;
    HRESULT hr = RunEditSession(context, clientId_, TF_ES_SYNC | TF_ES_READ, [&](TfEditCookie ec) {
        secret = IsSecretField(ec, context);
        return S_OK;
    });
    return FAILED(hr) ? -1 : secret ? 1 : 0;
}

// ---- 日本語 / 英数 ----

STDMETHODIMP TextService::OnChange(REFGUID guid) {
    if (!IsEqualGUID(guid, GUID_COMPARTMENT_KEYBOARD_OPENCLOSE) || threadMgr_ == nullptr) return S_OK;
    ITfCompartmentMgr* compartments = nullptr;
    if (FAILED(threadMgr_->QueryInterface(IID_ITfCompartmentMgr, reinterpret_cast<void**>(&compartments)))) return S_OK;
    ITfCompartment* openClose = nullptr;
    if (SUCCEEDED(compartments->GetCompartment(GUID_COMPARTMENT_KEYBOARD_OPENCLOSE, &openClose))) {
        VARIANT value;
        VariantInit(&value);
        if (SUCCEEDED(openClose->GetValue(&value))) {
            open_ = value.vt == VT_I4 && value.lVal != 0;
            VariantClear(&value);
        }
        openClose->Release();
    }
    compartments->Release();
    TipLog(L"入力モード: %s", open_ ? L"日本語" : L"英数");
    if (!open_ && composition_ != nullptr) CommitAsync(false);
    // アプリ・Windows・Meltype.exe のキーボードフック (左右の Alt) が開閉を変えた: 次のキーを受け取るかが変わるので、すぐに伝える
    if (!settingOpen_ && !disabled_) Sync();
    UpdateLangBar();
    return S_OK;
}

void TextService::SetOpen(bool open) {
    if (threadMgr_ == nullptr || open == open_) return;
    ITfCompartmentMgr* compartments = nullptr;
    if (FAILED(threadMgr_->QueryInterface(IID_ITfCompartmentMgr, reinterpret_cast<void**>(&compartments)))) return;
    ITfCompartment* openClose = nullptr;
    if (SUCCEEDED(compartments->GetCompartment(GUID_COMPARTMENT_KEYBOARD_OPENCLOSE, &openClose))) {
        VARIANT value;
        VariantInit(&value);
        value.vt = VT_I4;
        value.lVal = open ? 1 : 0;
        settingOpen_ = true;
        openClose->SetValue(clientId_, &value);  // OnChange が呼ばれる
        settingOpen_ = false;
        openClose->Release();
    }
    compartments->Release();
}

void TextService::FlushOpen() {
    const int open = pendingOpen_;
    pendingOpen_ = -1;
    if (open >= 0) SetOpen(open == 1);
    UpdateLangBar();
}

void TextService::UpdateLangBar() {
    if (langBar_ != nullptr) langBar_->Update();
}

void TextService::ToggleInputMode(ITfContext* context) {
    if (disabled_) return;
    Microsoft::WRL::ComPtr<ITfContext> focused;
    if (context == nullptr && threadMgr_ != nullptr) {
        Microsoft::WRL::ComPtr<ITfDocumentMgr> documentMgr;
        if (SUCCEEDED(threadMgr_->GetFocus(&documentMgr)) && documentMgr) documentMgr->GetTop(&focused);
        context = focused.Get();
    }
    bool sent = false;
    if (context != nullptr) {
        RunEditSession(context, clientId_, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie ec) {
            std::string field;
            if (!ReadField(ec, context, field)) return S_OK;
            const RECT anchor = CaretRect(ec, context);
            ownEdit_ = true;
            JsonValue reply;
            if (Send("ModeToggleRequested", field, "", reply, 800, false, &anchor)) {
                ApplyOutput(ec, context, reply);
                sent = true;
            }
            return S_OK;
        });
        ownEdit_ = false;
    }
    // Meltype.exe につながらない (管理者として動くアプリなど): 開閉だけを切り替える
    if (!sent) pendingOpen_ = open_ ? 0 : 1;
    FlushOpen();
}

// ---- Meltype.exe とのやり取り ----

bool TextService::Send(const char* type, const std::string& field, const std::string& fields, JsonValue& reply, DWORD timeoutMs, bool optional,
                       const RECT* anchor) {
    if (deactivated_) return false;
    std::string request = "{\"impl\":" + ImplId() + ",\"thread\":" + std::to_string(GetCurrentThreadId());
    if (anchor != nullptr && anchor->bottom > anchor->top && anchor->bottom - anchor->top < 200)
        request += ",\"x\":" + std::to_string(anchor->left) + ",\"y\":" + std::to_string(anchor->bottom + 4);
    request += std::string(",\"event\":{\"version\":") + std::to_string(kImeVersion) + ",\"type\":\"" + type + "\",\"instance\":\"" + instance_ +
               "\",\"imeOpen\":" + (open_ ? "true" : "false") + (field.empty() ? "" : ",\"field\":" + field) + fields + "}}";
    std::string response;
    if (!pipe_.Transact(request, response, timeoutMs, optional)) return false;
    const bool parsed = ParseJson(response, reply);
    if (parsed && reply[L"stale"].Bool()) {
        // この impl は Meltype.exe と別ビルド: stub に差し替えを要求する (差し替わるまでは、タイムアウトと同じ扱い)
        if (stub_) PostMessageW(stub_, kStubImplStale, 0, 0);
        return false;
    }
    if (!parsed || reply[L"state"].IsNull() || reply[L"result"].IsNull()) {
        // 版の違う Meltype.exe (更新の途中など)
        TipLog(L"応答を読めません");
        return false;
    }
    return true;
}

bool TextService::ReadField(TfEditCookie ec, ITfContext* context, std::string& field) {
    if (IsSecretField(ec, context)) return false;
    std::wstring before, after, selection;
    // 読めなければ before を送らない (空の文字列だと、本体は行の頭とみなす)
    const bool read = SurroundingText(ec, context, before, after, selection);
    field = std::string("{") + (read ? "\"before\":" + JsonString(before) + ",\"after\":" + JsonString(after) + "," : "") + "\"composing\":" +
            (composition_ != nullptr ? "true" : "false") + (selection.empty() ? "" : ",\"selection\":" + JsonString(selection)) +
            (privateField_ ? ",\"traits\":[\"Private\"]" : "") + "}";
    return true;
}

void TextService::ApplyState(const JsonValue& state) {
    if (state.IsNull() || state[L"revision"].number < state_.rev) return;
    ServerState next;
    next.known = true;
    next.active = state[L"handlesInput"].Bool();
    next.open = state[L"imeOpen"].Bool();
    next.japanese = state[L"languageMode"].Str() == L"Japanese";
    next.rev = state[L"revision"].number;
    const JsonValue& capture = state[L"capture"];
    next.text = capture[L"printableAscii"].Bool();
    next.except = capture[L"printableAsciiExcept"].Str();
    next.keys.clear();
    for (const JsonValue& key : capture[L"shortcuts"].array) {
        const int vk = key[L"vk"].Int(), mods = key[L"modifiers"].Int();
        if (vk > 0 && vk < 256 && mods >= 0 && mods <= 15) next.keys.push_back({static_cast<unsigned>(vk), mods});
    }
    for (const JsonValue& key : capture[L"ctrlKeysWhileComposing"].array) {
        const int vk = key[L"vk"].Int();
        if (vk > 0 && vk < 256) next.composing.push_back(static_cast<UINT>(vk));
    }
    if (next.active != state_.active || !state_.known) TipLog(L"Meltype.exe: %s", next.active ? L"受け持ちます" : L"受け持ちません");
    state_ = std::move(next);
    if (state_.keys != preservedFrom_) RefreshPreservedKeys();
    if (state_.active && state_.open != open_) pendingOpen_ = state_.open ? 1 : 0;
}

std::string TextService::CaretMoved(bool left) {
    return std::string(",\"caret\":{\"moved\":true,\"passedKey\":0") + (left ? ",\"focusLeft\":true" : "") + "}";
}

void TextService::Sync(bool entered) {
    JsonValue reply;
    // 状態を受け取るだけ。応答が遅ければ待たない (次のキーで受け取る)。
    // フォーカスが入ったときは入力欄の事実も送る (アプリ別設定・コードの行で、受け持つか・入力モードを表示するかが変わる)
    std::string field;
    RECT anchor = {};
    Microsoft::WRL::ComPtr<ITfContext> context;
    if (entered && threadMgr_ != nullptr) {
        Microsoft::WRL::ComPtr<ITfDocumentMgr> documentMgr;
        if (SUCCEEDED(threadMgr_->GetFocus(&documentMgr)) && documentMgr) documentMgr->GetTop(&context);
    }
    if (context) {
        RunEditSession(context.Get(), clientId_, TF_ES_SYNC | TF_ES_READ, [&](TfEditCookie ec) {
            // パスワード欄の文字は読まずに、パスワード欄であることだけを伝える
            if (!ReadField(ec, context.Get(), field)) field = "{\"traits\":[\"Password\"]}";
            anchor = CaretRect(ec, context.Get());
            return S_OK;
        });
    }
    if (Send(entered ? "CaretMoved" : "StateRequested", field, entered ? ",\"caret\":{\"moved\":true,\"passedKey\":0,\"focusEntered\":true}" : "", reply, 100, true,
             field.empty() ? nullptr : &anchor))
        ApplyState(reply[L"state"]);
    FlushOpen();
}

// 通信の結果を、IME のスレッドで反映する
LRESULT CALLBACK TextService::MessageWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_NCCREATE) {
        auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(create->lpCreateParams));
    }
    auto self = reinterpret_cast<TextService*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == kConversionWaitDone && self) {
        self->FinishConversionWait();
        return 0;
    }
    if (message == kAltKey && self) {
        self->HandleAltKey(static_cast<UINT>(wParam), static_cast<UINT>(lParam));
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

void TextService::CreateMessageWindow() {
    WNDCLASSW cls{};
    cls.lpfnWndProc = MessageWindowProc;
    cls.hInstance = g_module;
    cls.lpszClassName = L"Meltype.Tip.Messages";
    RegisterClassW(&cls);
    window_ = CreateWindowExW(0, cls.lpszClassName, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, g_module, this);
}

void TextService::DestroyMessageWindow() {
    if (window_) {
        DestroyWindow(window_);
        window_ = nullptr;
    }
    // worker は this や TSF に触れない。DLL を解放する前に通信の終わりを待つ
    if (conversionWait_.valid()) {
        conversionWait_.wait();
        conversionWait_.get();
    }
    conversionPipe_.reset();
    // クラスはプロセス内で共有する。他スレッドのウィンドウが残る間の解除失敗は許容し、最後のインスタンスがウィンドウを破棄したときに解除する
    UnregisterClassW(L"Meltype.Tip.Messages", g_module);
}

void TextService::StartConversionWait() {
    if (composition_ == nullptr || conversionWait_.valid() || !window_ || deactivated_) return;
    if (!conversionPipe_) conversionPipe_ = std::make_shared<PipeClient>();
    auto pipe = conversionPipe_;
    HWND window = window_;
    auto request = "{\"wait\":" + std::to_string(conversionGeneration_) + "}";
    try {
        conversionWait_ = std::async(std::launch::async, [pipe, window, request]() {
            std::string response;
            JsonValue reply;
            int generation = -1;
            try {
                // optional: wait は Meltype.exe が最大 250ms 持つので、時間切れは Meltype.exe が止まっているときだけ
                if (pipe->Transact(request, response, 1000, true) && ParseJson(response, reply) && reply[L"generation"].type == JsonValue::Type::Number)
                    generation = reply[L"generation"].Int();
            } catch (...) {
            }
            PostMessageW(window, kConversionWaitDone, 0, 0);
            return generation;
        });
    } catch (...) {
        TipLog(L"変換結果を待つスレッドを開始できません");
    }
}

void TextService::FinishConversionWait() {
    if (!conversionWait_.valid()) return;
    const int generation = conversionWait_.get();
    // つながらないときは次の Apply までやり直さない (Meltype.exe が無いときに空回りしない)
    if (generation < 0) return;
    // 最初の wait (世代 -1) も、受け付ける前に届いた結果を反映するので changed にする
    const bool changed = generation != conversionGeneration_;
    conversionGeneration_ = generation;
    if (composition_ == nullptr) {
        // 入力していない間はつながりを持たない (全アプリの全スレッドの分がつながり続ける)
        if (conversionPipe_) conversionPipe_->Close();
        return;
    }
    if (changed) {
        Microsoft::WRL::ComPtr<ITfContext> context = compositionContext_;
        Microsoft::WRL::ComPtr<ITfComposition> target = composition_;
        Microsoft::WRL::ComPtr<TextService> self = this;
        RunEditSession(context.Get(), clientId_, TF_ES_ASYNCDONTCARE | TF_ES_READWRITE, [self, context, target](TfEditCookie ec) {
            if (self->composition_ != target.Get() || self->compositionContext_ != context.Get()) return S_OK;
            JsonValue reply;
            // consumed でなければ、Meltype.exe に入力の続きが無い。ここでは何もせず、次のキーで扱う
            if (self->Send("ConversionFinished", "", "", reply) && reply[L"result"][L"consumed"].Bool()) {
                self->ownEdit_ = true;
                self->ApplyOutput(ec, context.Get(), reply);
            }
            return S_OK;
        });
    }
    StartConversionWait();
}

// ---- キー ----

bool TextService::ContextDisabled(ITfContext* context) {
    // IME を使わない入力欄 (パスワード欄は ReadSecretField で見る)
    if (context == nullptr) return true;
    ITfCompartmentMgr* compartments = nullptr;
    if (FAILED(context->QueryInterface(IID_ITfCompartmentMgr, reinterpret_cast<void**>(&compartments)))) return false;
    bool disabled = false;
    for (const GUID* guid : {&GUID_COMPARTMENT_KEYBOARD_DISABLED, &GUID_COMPARTMENT_EMPTYCONTEXT}) {
        ITfCompartment* compartment = nullptr;
        if (SUCCEEDED(compartments->GetCompartment(*guid, &compartment))) {
            VARIANT value;
            VariantInit(&value);
            if (SUCCEEDED(compartment->GetValue(&value)) && value.vt == VT_I4 && value.lVal != 0) disabled = true;
            VariantClear(&value);
            compartment->Release();
        }
    }
    compartments->Release();
    return disabled;
}

void TextService::UnpreserveKeys() {
    Microsoft::WRL::ComPtr<ITfKeystrokeMgr> manager;
    if (threadMgr_ && SUCCEEDED(threadMgr_->QueryInterface(IID_PPV_ARGS(&manager))))
        for (const auto& binding : preservedKeys_) manager->UnpreserveKey(binding.guid, &binding.key);
    preservedKeys_.clear();
}

void TextService::RefreshPreservedKeys() {
    if (!threadMgr_) return;
    Microsoft::WRL::ComPtr<ITfKeystrokeMgr> manager;
    if (FAILED(threadMgr_->QueryInterface(IID_PPV_ARGS(&manager)))) return;
    UnpreserveKeys();
    preservedFrom_ = state_.keys;
    if (disabled_) return;
    // 修飾キー付きのキー (Ctrl+Space など) は、TSF に登録しないと IME に届かない
    std::vector<ToggleShortcut> withModifiers;
    for (const auto& key : state_.keys)
        if (key.mods != 0) withModifiers.push_back(key);
    for (const auto& b : PreservedShortcutKeys(withModifiers, MapVirtualKeyExW(0x29, MAPVK_VSC_TO_VK_EX, GetKeyboardLayout(0)) == ToggleShortcut::Grave)) {
        PreservedKey entry{{b.key, b.flags}, {0x7118DA00u | b.key, static_cast<USHORT>(b.flags), 0x4AC2, {0xAA, 0x4D, 0xC5, 0x51, 0x17, 0x39, 0x61, 0xC3}}};
        const HRESULT hr = manager->PreserveKey(clientId_, entry.guid, &entry.key, nullptr, 0);
        if (SUCCEEDED(hr)) preservedKeys_.push_back(entry);
        else TipLog(L"ショートカットを登録できません: 0x%02X (0x%08X)", b.key, hr);
    }
}

STDMETHODIMP TextService::OnPreservedKey(ITfContext* context, REFGUID guid, BOOL* eaten) {
    *eaten = FALSE;
    for (const auto& binding : preservedKeys_) {
        if (!IsEqualGUID(binding.guid, guid)) continue;
        const UINT scan = ToggleShortcut::NormalizeHankakuKey(binding.key.uVKey) == ToggleShortcut::Hankaku || binding.key.uVKey == ToggleShortcut::Grave
            ? 0x29 : MapVirtualKeyExW(binding.key.uVKey, MAPVK_VK_TO_VSC, GetKeyboardLayout(0));
        const LPARAM keyParam = static_cast<LPARAM>(scan) << 16;
        const UINT slot = ShortcutSlot(binding.key.uVKey, keyParam);
        if (binding.key.uModifiers & TF_MOD_ON_KEYUP) {
            *eaten = eatenDown_[slot] ? TRUE : FALSE;
            eatenDown_[slot] = false;
            return S_OK;
        }
        if (disabled_ || ContextDisabled(context) || KeyDown(VK_LWIN) || KeyDown(VK_RWIN)) return S_OK;
        if (!CapturesKey(binding.key.uVKey, keyParam)) return S_OK;
        *eaten = TRUE;
        const bool repeat = eatenDown_[slot];
        eatenDown_[slot] = true;
        HandleKey(context, ShortcutKey(binding.key.uVKey, keyParam), 0, repeat, true);
        MaskSystemModifier();
        return S_OK;
    }
    return S_OK;
}

bool TextService::CapturesKey(UINT vk, LPARAM lParam) const {
    const auto bits = static_cast<ULONG_PTR>(lParam);
    const int mods = (KeyDown(VK_SHIFT) ? 1 : 0) | (KeyDown(VK_CONTROL) ? 2 : 0) |
        ((KeyDown(VK_MENU) || (bits & (static_cast<ULONG_PTR>(1) << 29))) ? 4 : 0) |
        (KeyDown(VK_LWIN) || KeyDown(VK_RWIN) ? 8 : 0);
    for (const auto& key : state_.keys)
        if (key.Matches(ShortcutKey(vk, lParam), mods)) return true;
    return false;
}

void TextService::ForgetSecretField() {
    secretVk_ = 0;
    secretContext_.Reset();
}

bool TextService::WouldEat(ITfContext* context, UINT vk, LPARAM lParam, wchar_t& ch, bool test) {
    ch = 0;
    secretChecked_ = false;
    secretWhileComposing_ = false;
    if (disabled_ || vk >= 256) return false;
    const bool listed = CapturesKey(vk, lParam);
    if (IsModifier(vk) && !listed) return false;
    // アプリが確定した打ちかけの文字があれば、このキーの前に、変換中に戻すか確定したことにする
    ResumeOrCommit(context);
    // VK_PACKET: ほかのソフト (パスワード管理・AutoHotkey など) が送り込んだ文字。送り直せないので受け取らずに通す。
    // 変換中なら、変換中の文字の中に入って食い違わないように、先に確定する
    // (同期で確定できなければ、あとで確定する。キャレットは動かさず、前の語も確定し直さない)
    if (vk == VK_PACKET || vk == VK_PROCESSKEY) {
        if (vk == VK_PACKET && composition_ != nullptr && !CommitNow(true)) CommitAsync(true);
        return false;
    }
    if (composition_ != nullptr && compositionContext_ != context) {
        // 変換中の文字が前の入力欄に残っている (フォーカスが移ったときに確定できなかった): 先に確定する。
        // できなければ、このキーは受け取らない (前の入力欄の変換中の文字に入れないように)。
        // 前の入力欄が編集を受け付けなくなっている (閉じかけているなど) と、いつまでも確定できないので、2 つ目のキーからは手放す。
        // 打鍵ごとに 1 回だけ数える (OnTestKeyDown で数えたキーを、続く OnKeyDown で数え直さない。OnKeyDown しか呼ばないアプリもある)
        if (!CommitNow(true)) {
            ULONGLONG now = GetTickCount64();
            bool counted = !test && vk == staleVk_ && now - staleTime_ < 500;
            if (!counted) {
                staleCommitTries_++;
                staleVk_ = vk;
                staleTime_ = now;
            }
            if (staleCommitTries_ <= 1) {
                if (!counted) CommitAsync(true);
                return false;
            }
            AbandonComposition();
        }
    }
    // 受け取るように言われたキー (Space・半角/全角・切り替えキーなど) は、変換中でもそうでなくても、意味は Meltype.exe が決める
    if (listed) return true;
    if (composition_ != nullptr) {
        // 変換中にアプリが入力欄の IME を無効にした (ブラウザーはパスワード欄に変えるとそうする): 変換中の文字を確定して、
        // このキーからは受け取らない。ここで確定できなければ受け取り、キーの処理の中で確定してから送り直す
        // (キーが先に届いて、確定が後になるのを防ぐ)。
        // 入力欄の種類がパスワードに変わったかは、キーの編集セッションの中で確かめる (HandleKey。secretChecked_ は false のまま)
        if (ContextDisabled(context)) {
            if (CommitNow(true)) {
                TipLog(L"変換中に入力欄がパスワード欄になったので、確定してキーを通します");
                return false;
            }
            secretWhileComposing_ = true;
            return true;
        }
        // Ctrl・Alt・Windows キーとの組み合わせ (Ctrl + S など): Meltype.exe も確定してアプリに渡すだけなので、ここで確定して
        // 受け取らずに通す (受け取ってから送り直すと、送り直しが届かないアプリでキーが消える)。
        // ここで確定できなければ受け取り、キーの処理の中で確定してから送り直す (キーが先に届いて、確定が後になるのを防ぐ)
        // ただし変換ボックスの中で使う Ctrl+英字 (本体が capture.composing で知らせる) は、確定せずに Meltype.exe に送る
        const bool used = KeyDown(VK_CONTROL) && !KeyDown(VK_MENU) && !KeyDown(VK_LWIN) && !KeyDown(VK_RWIN) &&
            std::find(state_.composing.begin(), state_.composing.end(), vk) != state_.composing.end();
        if ((KeyDown(VK_CONTROL) || KeyDown(VK_MENU) || KeyDown(VK_LWIN) || KeyDown(VK_RWIN)) && !used && CommitNow()) return false;
        return true;  // 変換中はほかのキーを全部受け取る (つながらなければ確定して通す)
    }
    // 調べるとき用: 文字を生むキーを通した理由 (どのキーかは書かない)
    auto pass = [&](const wchar_t* reason) {
        if (passReason_ != reason) TipLog(L"キーを通します: %s", reason);
        passReason_ = reason;
        return false;
    };
    if (state_.known && !state_.active) return pass(L"Meltype.exe が受け持たない");
    if (InMenuMode()) return pass(L"メニューを開いている");
    if (KeyDown(VK_CONTROL) || KeyDown(VK_MENU) || KeyDown(VK_LWIN) || KeyDown(VK_RWIN)) return pass(L"Ctrl・Alt・Windows キーを押している");
    if (ContextDisabled(context)) return pass(L"この入力欄では使わない");
    ch = CharOf(vk, 0);
    // 受け取る文字は Meltype.exe が決める (英数では打っている英語の語の続きを通すなど)
    if (!state_.text || ch < L'!' || ch > L'~' || state_.except.find(ch) != std::wstring::npos) return pass(L"受け取らない文字");
    // パスワード欄か。OnTestKeyDown で読んだ結果は、続く同じキー・同じ入力欄の OnKeyDown で 1 回だけ使う (ほかのキー・入力欄には使わない)
    ULONGLONG now = GetTickCount64();
    int secret;
    if (!test && vk == secretVk_ && context == secretContext_.Get() && now - secretTime_ < 200) {
        secret = secretResult_;
        privateField_ = secretPrivate_;
    } else {
        secret = ReadSecretField(context);
    }
    ForgetSecretField();
    if (test) {
        secretVk_ = vk;
        secretContext_ = context;
        secretTime_ = now;
        secretResult_ = secret;
        secretPrivate_ = privateField_;
    }
    if (secret == 1) return pass(L"パスワードの入力欄");
    // 読めなかったら、キーの編集セッションの中で確かめる (HandleKey)
    secretChecked_ = secret == 0;
    passReason_ = nullptr;
    return true;
}

bool TextService::IsReinjected(UINT vk) {
    // 送り直したキーは 1 回だけ素通しする。送り直すときに付けた印 (dwExtraInfo) で見分ける。
    // 印は前のメッセージのものが残っていることがあるので、送り直してまだ届いていないキーと同じキーのときだけ見る。
    // 印が届かないアプリのために、時刻でも見分ける (少し経っても届かなければ忘れる。押しっぱなしの次のキーを素通ししないように)
    // 印が届くアプリだと分かったら、時刻では見分けない (送り直しが届く前に同じキーを打ったとき、そのキーを素通ししないように)
    ULONGLONG now = GetTickCount64();
    bool marked = GetMessageExtraInfo() == static_cast<LPARAM>(kReinjectMarker);
    for (Reinjected& pending : reinjected_) {
        if (pending.vk != 0 && now - pending.time > 2000) {
            // 届かなかった (印の付かない経路で届いたのかもしれない): 時刻でも見分けるのに戻す
            pending.vk = 0;
            markerSeen_ = false;
        }
        if (pending.vk == 0 || pending.vk != vk) continue;
        if (marked) markerSeen_ = true;
        else if (markerSeen_) continue;
        bool ours = marked || now - pending.time < 500;
        pending.vk = 0;
        return ours;
    }
    return false;
}

STDMETHODIMP TextService::OnTestKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    UINT vk = static_cast<UINT>(wParam);
    if (IsReinjected(vk)) {
        passVk_ = vk;  // アプリによっては、このあと OnKeyDown も呼ぶ
        passTime_ = GetTickCount64();
        *eaten = FALSE;
        return S_OK;
    }
    passVk_ = 0;
    wchar_t ch = 0;
    *eaten = WouldEat(context, vk, lParam, ch, true) ? TRUE : FALSE;
    // アプリにそのまま渡すキー (BackSpace・矢印・Enter・Space など) はキャレットを動かす (編集の通知で Meltype.exe に伝える)
    if (!*eaten && !IsModifier(vk)) RememberPassed(vk);
    return S_OK;
}

STDMETHODIMP TextService::OnKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    UINT vk = static_cast<UINT>(wParam);
    if ((vk == passVk_ && GetTickCount64() - passTime_ < 500) || IsReinjected(vk)) {
        passVk_ = 0;
        *eaten = FALSE;
        return S_OK;
    }
    passVk_ = 0;
    wchar_t ch = 0;
    if (!WouldEat(context, vk, lParam, ch, false)) {
        if (!IsModifier(vk)) RememberPassed(vk);
        *eaten = FALSE;
        return S_OK;
    }
    *eaten = TRUE;
    const UINT slot = ShortcutSlot(vk, lParam);
    const bool repeat = eatenDown_[slot];
    eatenDown_[slot] = true;
    if (CapturesKey(vk, lParam)) {
        HandleKey(context, ShortcutKey(vk, lParam), vk == VK_SPACE ? L' ' : 0, repeat, true);
        MaskSystemModifier();
        return S_OK;
    }
    HandleKey(context, vk, CharOf(vk, lParam), repeat, false);
    return S_OK;
}

STDMETHODIMP TextService::OnTestKeyUp(ITfContext*, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    // VK_KANJI は解放通知が来ない環境があるので、Alt を離したら次の操作を受け付ける。
    if (wParam == VK_MENU || wParam == VK_LMENU || wParam == VK_RMENU) {
        eatenDown_[ToggleShortcut::Hankaku] = false;
    }
    *eaten = wParam < 256 && eatenDown_[ShortcutSlot(static_cast<UINT>(wParam), lParam)] ? TRUE : FALSE;
    return S_OK;
}

STDMETHODIMP TextService::OnKeyUp(ITfContext*, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    // VK_KANJI は解放通知が来ない環境があるので、Alt を離したら次の操作を受け付ける。
    if (wParam == VK_MENU || wParam == VK_LMENU || wParam == VK_RMENU) {
        eatenDown_[ToggleShortcut::Hankaku] = false;
    }
    const UINT slot = ShortcutSlot(static_cast<UINT>(wParam), lParam);
    *eaten = wParam < 256 && eatenDown_[slot] ? TRUE : FALSE;
    if (wParam < 256) eatenDown_[slot] = false;
    return S_OK;
}

void TextService::HandleKey(ITfContext* context, UINT vk, wchar_t ch, bool repeat, bool listed) {
    // 8 = Windows キー (Meltype.exe は Mac の Command と同じく、確定してアプリに渡す)
    const bool shift = KeyDown(VK_SHIFT), control = KeyDown(VK_CONTROL), alt = KeyDown(VK_MENU), command = KeyDown(VK_LWIN) || KeyDown(VK_RWIN);
    bool failed = false;
    bool consumed = true;
    bool inserted = false;
    HRESULT hr = RunEditSession(context, clientId_, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie ec) {
        // キーを受け取る前に入力欄の種類を読めなかった: ここで確かめ、パスワード欄なら Meltype.exe に送らずにアプリへ返す
        // (変換中なら、変換中の文字を確定してから。変換中にパスワード欄になったのに、受け取る前に確定できなかったときも)
        if (secretWhileComposing_ || (!secretChecked_ && IsSecretField(ec, context))) {
            if (composition_ != nullptr) CommitInSession(ec, context, true);
            consumed = false;
            TipLog(L"パスワードの入力欄なので何もしません");
            return S_OK;
        }
        std::string field;
        ReadField(ec, context, field);
        // 受け取るように言われたキーは、入力モードを表示する位置も送る (切り替えたかは Meltype.exe が決める)
        const RECT anchor = listed ? CaretRect(ec, context) : RECT{};
        ownEdit_ = true;
        const std::string key = std::string(",\"key\":{\"vk\":") + std::to_string(vk) + ",\"ch\":" + std::to_string(ch) +
                                ",\"shift\":" + (shift ? "true" : "false") + ",\"ctrl\":" + (control ? "true" : "false") +
                                ",\"alt\":" + (alt ? "true" : "false") + ",\"command\":" + (command ? "true" : "false") +
                                (repeat ? ",\"repeat\":true" : "") + "}";
        JsonValue reply;
        if (!Send("KeyPressed", field, key, reply, 800, false, listed ? &anchor : nullptr)) {
            // Meltype.exe が応答しない: 変換中の文字はそのまま確定する。打った文字はここで入れる
            // (送り直すと、その間に打った次のキーより後に届いて順番が入れ替わる)
            failed = true;
            state_.known = false;
            if (candidates_) candidates_->Hide();
            EndComposition(ec);
            if (ch >= 0x20 && !control && !alt && !command) {
                TF_SELECTION selection = {};
                ULONG fetched = 0;
                if (SUCCEEDED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) && fetched > 0) {
                    inserted = WriteCommit(ec, context, selection.range, std::wstring(1, ch));
                    selection.range->Release();
                }
            }
            return S_OK;
        }
        consumed = ApplyOutput(ec, context, reply);
        // 英数で使わなかった文字は、送り直さずにここで入れる (送り直しが届かないアプリがある)
        const JsonValue& result = reply[L"result"];
        if (!consumed && !open_ && ch >= L' ' && ch <= L'~' && vk != VK_SPACE && !control && !alt && !command &&
            result[L"commits"].array.empty() && result[L"view"].IsNull()) {
            TF_SELECTION selection = {};
            ULONG fetched = 0;
            if (SUCCEEDED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) && fetched > 0) {
                inserted = WriteCommit(ec, context, selection.range, std::wstring(1, ch));
                selection.range->Release();
            }
            if (!inserted) TipLog(L"英数文字を直接入力できませんでした。キーをアプリへ送り直します");
        }
        return S_OK;
    });
    // 同期の編集セッションの終わりの通知 (OnEndEdit) はもう来ている。何も変えなかったときに印が残らないように戻す
    ownEdit_ = false;
    if (FAILED(hr)) {
        TipLog(L"編集セッションを始められません: 0x%08X", hr);
        failed = true;
        // キーを送り直す前に、変換中の文字を確定する (キャレットは動かさない)
        if (composition_ != nullptr) CommitAsync(true);
    } else if (failed) {
        TipLog(L"Meltype.exe から応答が来ないので、打った文字をそのまま入れました");
    }
    // Meltype.exe につながらない (管理者として動くアプリなど) ときの 半角/全角・英数・かな: 開閉だけを切り替え、アプリには通さない
    // (アプリに通すと、PowerShell (PSReadLine) は文字の無いキーを @ として入れる)
    const UINT key = ToggleShortcut::NormalizeHankakuKey(vk);
    const bool modeKey = key == ToggleShortcut::Hankaku || key == ToggleShortcut::Grave || key == 0xF0 || key == 0x15;
    if (failed && modeKey && !repeat) pendingOpen_ = key == 0xF0 ? 0 : key == 0x15 ? 1 : open_ ? 0 : 1;
    FlushOpen();
    // Meltype.exe が使わなかったキーは、アプリに送り直す
    if (!inserted && (failed ? !modeKey : !consumed)) Reinject(vk);
}

void TextService::HandleAltKey(UINT vk, UINT flags) {
    const bool released = (flags & kAltReleased) != 0;
    std::string key = std::string(",\"key\":{\"vk\":") + std::to_string(vk);
    if (released) {
        key += std::string(",\"interrupted\":") + ((flags & kAltInterrupted) ? "true" : "false");
    } else {
        key += std::string(",\"alt\":true,\"shift\":") + ((flags & kAltShift) ? "true" : "false") + ",\"ctrl\":" + ((flags & kAltCtrl) ? "true" : "false") +
               ",\"command\":" + ((flags & kAltCommand) ? "true" : "false");
    }
    key += "}";
    const char* type = released ? "KeyReleased" : "KeyPressed";
    Microsoft::WRL::ComPtr<ITfContext> context;
    if (threadMgr_ != nullptr) {
        Microsoft::WRL::ComPtr<ITfDocumentMgr> documentMgr;
        if (SUCCEEDED(threadMgr_->GetFocus(&documentMgr)) && documentMgr) documentMgr->GetTop(&context);
    }
    // 単独押しで切り替えるときは変換中の文字を確定するので、入力欄を書き換えられる編集で送る
    bool sent = false;
    if (context) {
        const HRESULT hr = RunEditSession(context.Get(), clientId_, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie ec) {
            // パスワード欄の文字は読まずに、パスワード欄であることだけを伝える
            std::string field;
            if (!ReadField(ec, context.Get(), field)) field = "{\"traits\":[\"Password\"]}";
            const RECT anchor = CaretRect(ec, context.Get());
            ownEdit_ = true;
            JsonValue reply;
            sent = Send(type, field, key, reply, 800, false, &anchor);
            if (sent) ApplyOutput(ec, context.Get(), reply);
            return S_OK;
        });
        ownEdit_ = false;
        if (FAILED(hr)) TipLog(L"編集セッションを始められません: 0x%08X", hr);
    }
    // 入力欄が無い・編集できない: 本体には知らせる (押下と解放が対にならないと、次の単独押しを見分けられない)
    if (!sent) {
        JsonValue reply;
        if (Send(type, "", key, reply, 800, true)) ApplyState(reply[L"state"]);
    }
    FlushOpen();
}

void TextService::RememberPassed(UINT vk) {
    passedVk_ = vk;
    passedControl_ = KeyDown(VK_CONTROL);
    passedTime_ = GetTickCount64();
}

bool TextService::Reinject(UINT vk) {
    // 受け取ったキーを使わなかった: アプリに送り直す (Ctrl などは押されたままなので、組み合わせもそのまま届く)
    eatenDown_[vk] = false;
    // 前に送り直した同じキーがまだ届いていない (見分けられずに受け取ってしまった) のに、また送り直そうとしている:
    // 自分の送り直しを受け取っては送り直すのを繰り返しているかもしれない。続くなら、アプリが固まらないように送り直すのをやめる。
    // (人が同じキーを続けて打ったときは、前の送り直しは届いて見分けられているので数えない)
    ULONGLONG now = GetTickCount64();
    bool previousPending = false;
    for (const Reinjected& pending : reinjected_) {
        if (pending.vk == vk && now - pending.time < 2000) previousPending = true;
    }
    // 数えるのは 1 秒ごと (人がキーを押し続けたときに、ずっと止めたままにならないように)
    if (!previousPending || vk != reinjectGuardVk_ || now - reinjectGuardTime_ > 1000) {
        reinjectGuardVk_ = vk;
        reinjectGuardTime_ = now;
        reinjectGuardCount_ = 0;
    }
    if (previousPending && ++reinjectGuardCount_ > 3) {
        TipLog(L"同じキーの送り直しが続くので、やめます");
        return false;
    }
    // まだ届いていない送り直しの続きに覚える (いっぱいなら一番古いものを捨てる)
    Reinjected* slot = &reinjected_[0];
    for (Reinjected& pending : reinjected_) {
        if (pending.vk == 0) {
            slot = &pending;
            break;
        }
        if (pending.time < slot->time) slot = &pending;
    }
    slot->vk = vk;
    slot->time = GetTickCount64();
    INPUT inputs[2] = {};
    for (int i = 0; i < 2; i++) {
        inputs[i].type = INPUT_KEYBOARD;
        inputs[i].ki.wVk = static_cast<WORD>(vk);
        inputs[i].ki.wScan = static_cast<WORD>(MapVirtualKeyW(vk, MAPVK_VK_TO_VSC));
        inputs[i].ki.dwFlags = i == 1 ? KEYEVENTF_KEYUP : 0;
        inputs[i].ki.dwExtraInfo = kReinjectMarker;
        if (vk == VK_LEFT || vk == VK_RIGHT || vk == VK_UP || vk == VK_DOWN || vk == VK_HOME || vk == VK_END || vk == VK_PRIOR ||
            vk == VK_NEXT || vk == VK_INSERT || vk == VK_DELETE) {
            inputs[i].ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
        }
    }
    // 送り込みを許されていないとき (UIPI) は、失敗しても分からないことがある。分かったときだけログに書く
    if (SendInput(2, inputs, sizeof(INPUT)) != 2) {
        slot->vk = 0;
        TipLog(L"キーをアプリに送り直せません: %lu", GetLastError());
        return false;
    }
    return true;
}

bool TextService::SurroundingText(TfEditCookie ec, ITfContext* context, std::wstring& before, std::wstring& after, std::wstring& selected) {
    auto unreadable = [this](const wchar_t* where, HRESULT hr) {
        if (!beforeUnreadable_) TipLog(L"キャレットの前の文字を読めません (%s: 0x%08X)", where, static_cast<unsigned>(hr));
        beforeUnreadable_ = true;
        return false;
    };
    // 変換中なら、変換中の文字より前と後ろ。そうでなければキャレットの前と後ろ
    Microsoft::WRL::ComPtr<ITfRange> anchor;
    if (composition_ != nullptr && context == compositionContext_) {
        const HRESULT hr = composition_->GetRange(&anchor);
        if (!anchor) return unreadable(L"変換中の文字の範囲", hr);
    } else {
        TF_SELECTION selection = {};
        ULONG fetched = 0;
        const HRESULT hr = context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched);
        if (FAILED(hr) || fetched == 0) return unreadable(L"選択範囲", hr);
        anchor.Attach(selection.range);
    }
    // 選んでいる文字 (変換キーで再変換する)
    constexpr LONG kSelection = 1000;
    if (composition_ == nullptr) {
        std::wstring text(kSelection, L'\0');
        ULONG length = 0;
        if (SUCCEEDED(anchor->GetText(ec, 0, text.data(), kSelection, &length))) selected.assign(text.data(), length);
    }
    // 前は最大 4000 文字 (コードエディターで、複数行のコメント・文字列の中かを見分けるため。コードでなければ Meltype.exe が 20 文字にする)
    constexpr LONG kBefore = 4000;
    constexpr LONG kAfter = 20;
    std::wstring buffer(kBefore, L'\0');
    Microsoft::WRL::ComPtr<ITfRange> range;
    HRESULT hr = anchor->Clone(&range);
    if (FAILED(hr)) return unreadable(L"範囲の複製", hr);
    LONG moved = 0;
    range->Collapse(ec, TF_ANCHOR_START);
    range->ShiftStart(ec, -kBefore, &moved, nullptr);
    ULONG length = 0;
    hr = range->GetText(ec, 0, buffer.data(), kBefore, &length);
    if (FAILED(hr)) return unreadable(L"GetText", hr);
    before.assign(buffer.data(), length);
    if (beforeUnreadable_) TipLog(L"キャレットの前の文字を読めるようになりました");
    beforeUnreadable_ = false;
    range.Reset();
    if (SUCCEEDED(anchor->Clone(&range))) {
        LONG moved = 0;
        range->Collapse(ec, TF_ANCHOR_END);
        range->ShiftEnd(ec, kAfter, &moved, nullptr);
        ULONG length = 0;
        if (SUCCEEDED(range->GetText(ec, 0, buffer.data(), kAfter, &length))) after.assign(buffer.data(), length);
    }
    return true;
}

RECT TextService::CaretRect(TfEditCookie ec, ITfContext* context) {
    RECT rect = {};
    TF_SELECTION selection = {};
    ULONG fetched = 0;
    if (FAILED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) || fetched == 0 || selection.range == nullptr) return rect;
    Microsoft::WRL::ComPtr<ITfRange> range;
    range.Attach(selection.range);
    range->Collapse(ec, selection.style.ase == TF_AE_START ? TF_ANCHOR_START : TF_ANCHOR_END);
    Microsoft::WRL::ComPtr<ITfContextView> view;
    BOOL clipped = FALSE;
    // Terminal の UI Automation の選択範囲は実際のカーソル位置とは限らないので、TSF の入力コンテキストから取る
    if (SUCCEEDED(context->GetActiveView(&view)) && (FAILED(view->GetTextExt(ec, range.Get(), &rect, &clipped)) || clipped)) rect = {};
    return rect;
}

// ---- 入力欄への反映 ----

bool TextService::ApplyOutput(TfEditCookie ec, ITfContext* context, const JsonValue& reply) {
    ApplyState(reply[L"state"]);
    const JsonValue& result = reply[L"result"];
    Apply(ec, context, result);
    return result[L"consumed"].Bool();
}

void TextService::CommittedOutside() {
    if (disabled_) return;
    JsonValue reply;
    if (Send("CommittedByApp", "", CaretMoved(false), reply)) ApplyState(reply[L"state"]);
}

void TextService::Apply(TfEditCookie ec, ITfContext* context, const JsonValue& result) {
    // 1 つの応答の中の確定は、前の確定の後ろに続けて入れる (クリックで確定したときは、キャレットがもうクリックした所にあるので、
    // キャレットの位置に入れると、クリックした所に入ったり、選んでいた文字を上書きしたりする)
    Microsoft::WRL::ComPtr<ITfRange> next;
    const auto& commits = result[L"commits"].array;
    const JsonValue& view = result[L"view"];
    for (const JsonValue& edit : commits) {
        const std::wstring& text = edit[L"text"].Str();
        // 確定し直すときの、キャレットの前の消す文字 (本体が照合済み)
        const JsonValue& remove = edit[L"removeBefore"];
        const LONG deleteBefore = remove.type == JsonValue::Type::String ? static_cast<LONG>(remove.Str().size()) : 0;
        int caretBack = edit[L"moveCaretBack"].Int();
        int caretAfterCommit = 0;
        if (caretBack > 0) {
            TF_STATUS status = {};
            const HRESULT got = context->GetStatus(&status);
            const bool continues = &edit != &commits.back() || !view[L"text"].Str().empty();
            if (SUCCEEDED(got) && (status.dwStaticFlags & TF_SS_TRANSITORY) != 0) {
                // Chromium は次の変換の開始位置を確定範囲の境界に使う。続きがあるときは閉じ括弧の後ろから入力する。
                if (!continues && deleteBefore == 0 && !keepAppCaret_ && result[L"consumed"].Bool()) {
                    caretAfterCommit = caretBack;
                }
                caretBack = 0;
            }
        }
        const JsonValue& current = edit[L"currentWord"];
        Microsoft::WRL::ComPtr<ITfRange> range;
        if (composition_ != nullptr) {
            composition_->GetRange(&range);
        } else if (next) {
            next->Clone(&range);
        } else {
            TF_SELECTION selection = {};
            ULONG fetched = 0;
            if (SUCCEEDED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) && fetched > 0) range.Attach(selection.range);
        }
        if (!range) continue;
        Microsoft::WRL::ComPtr<ITfRange> movedCaret;
        if (deleteBefore > 0 && current.type == JsonValue::Type::String) {
            // 確定し直し (今確定した語つき): 今の語をふつうに確定してから、次の編集セッションで、前に確定した文字ごと書き換える。
            // 同じ編集セッションで変換中の文字の外も書き換えると、Chrome はページに変換の終わり (compositionend) を伝えない。
            // 変換中の範囲を前に広げて書き換えると、Chrome では TF_E_RANGE_NOT_COVERED で失敗する (#262)
            if (!WriteCommit(ec, context, range.Get(), current.Str(), caretBack, &movedCaret)) continue;
            // 今の語の後ろの位置を覚えておき、書き換えるときにキャレットがそこにあるかを確かめる (後ろに文字が入っても動かないように、両端とも前に寄せる)
            Microsoft::WRL::ComPtr<ITfRange> end;
            if (SUCCEEDED(range->Clone(&end)) && SUCCEEDED(end->Collapse(ec, TF_ANCHOR_END)) &&
                SUCCEEDED(end->SetGravity(ec, TF_GRAVITY_BACKWARD, TF_GRAVITY_BACKWARD)))
                RewriteBeforeCaret(context, end.Get(), deleteBefore + static_cast<LONG>(current.Str().size()), remove.Str() + current.Str(), text);
        } else {
            // 確定し直し: 前に確定した文字を消して入れ直す。前の文字が確定したときのままでなければ (アプリが書き換えた・前の文字を読めない)、
            // 関係ない文字を消したり、古い文字に重ねて入れたりしないように、確定し直さない (変換中の文字は、次の確定でそのまま確定する)
            if (deleteBefore > 0 && !ExtendOverPrevious(ec, range.Get(), deleteBefore, remove)) continue;
            if (!WriteCommit(ec, context, range.Get(), text, caretBack, &movedCaret)) continue;
            if (caretAfterCommit > 0) MoveCaretAfterCommit(ec, context, range.Get(), text, caretAfterCommit);
        }
        next.Reset();
        if (movedCaret) next = std::move(movedCaret);
        else if (SUCCEEDED(range->Clone(&next))) next->Collapse(ec, TF_ANCHOR_END);
    }

    lastConverting_ = !view.IsNull() && view[L"converting"].Bool();
    if (!view.IsNull() && !view[L"text"].Str().empty()) {
        ApplyComposition(ec, context, view);
        ShowCandidates(ec, context, view);
    } else {
        if (composition_ != nullptr) {
            // 変換中の文字を消した (BackSpace で全部消した・Esc で取り消した)。
            // Meltype.exe がこの入力を受け持っていない (一時停止した・つなぎ直して入力の続きが無い) ときは、消さずにそのまま確定する
            ITfRange* range = nullptr;
            if (state_.active && SUCCEEDED(composition_->GetRange(&range))) {
                range->SetText(ec, 0, L"", 0);
                range->Release();
            }
            EndComposition(ec);
        }
        if (candidates_) candidates_->Hide();
    }
    StartConversionWait();
}

void TextService::MoveCaretAfterCommit(TfEditCookie ec, ITfContext* context, ITfRange* range, const std::wstring& text, int count) {
    const HWND window = GetFocus();
    Microsoft::WRL::ComPtr<ITfRange> committed;
    if (window == nullptr || static_cast<size_t>(count) > text.size() || FAILED(range->Clone(&committed)) ||
        FAILED(committed->SetGravity(ec, TF_GRAVITY_BACKWARD, TF_GRAVITY_BACKWARD))) {
        return;
    }
    Microsoft::WRL::ComPtr<TextService> self = this;
    Microsoft::WRL::ComPtr<ITfContext> target = context;
    const auto revision = state_.rev;
    const auto passed = passedTime_;
    RunEditSession(context, clientId_, TF_ES_ASYNC | TF_ES_READ, [self, target, committed, text, count, window, revision, passed](TfEditCookie cookie) {
        Microsoft::WRL::ComPtr<ITfDocumentMgr> document;
        Microsoft::WRL::ComPtr<ITfContext> focused;
        if (self->deactivated_ || self->threadMgr_ == nullptr || self->composition_ != nullptr ||
            self->state_.rev != revision || self->passedTime_ != passed ||
            FAILED(self->threadMgr_->GetFocus(&document)) || !document ||
            FAILED(document->GetTop(&focused)) || !SameObject(focused.Get(), target.Get())) {
            return S_OK;
        }
        TF_SELECTION selection = {};
        ULONG fetched = 0;
        if (FAILED(target->GetSelection(cookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) || fetched == 0 || selection.range == nullptr) {
            return S_OK;
        }
        Microsoft::WRL::ComPtr<ITfRange> current;
        current.Attach(selection.range);
        BOOL empty = FALSE, same = FALSE;
        if (FAILED(current->IsEmpty(cookie, &empty)) || !empty ||
            FAILED(current->IsEqualEnd(cookie, committed.Get(), TF_ANCHOR_END, &same)) || !same) {
            return S_OK;
        }
        std::wstring actual(text.size() + 1, L'\0');
        ULONG length = 0;
        if (FAILED(committed->GetText(cookie, 0, actual.data(), static_cast<ULONG>(actual.size()), &length)) ||
            length != text.size() || actual.compare(0, length, text) != 0) {
            return S_OK;
        }
        GUITHREADINFO foreground = {sizeof(foreground)};
        if (!GetGUIThreadInfo(0, &foreground) || foreground.hwndFocus != window ||
            ((GetAsyncKeyState(VK_SHIFT) | GetAsyncKeyState(VK_CONTROL) | GetAsyncKeyState(VK_MENU) |
              GetAsyncKeyState(VK_LWIN) | GetAsyncKeyState(VK_RWIN) |
              GetAsyncKeyState(VK_LBUTTON) | GetAsyncKeyState(VK_RBUTTON)) & 0x8000) != 0) {
            return S_OK;
        }
        for (int i = 0; i < count; ++i) {
            if (!self->Reinject(VK_LEFT)) return S_OK;
        }
        return S_OK;
    });
}

bool TextService::ExtendOverPrevious(TfEditCookie ec, ITfRange* range, LONG count, const JsonValue& expect) {
    Microsoft::WRL::ComPtr<ITfRange> previous;
    if (FAILED(range->Clone(&previous))) return false;
    previous->Collapse(ec, TF_ANCHOR_START);
    LONG moved = 0;
    previous->ShiftStart(ec, -count, &moved, nullptr);
    // 戻れた文字数 (向きの符号は気にしない)
    // 消す文字が分からなければ消さない (確かめられないまま、関係ない文字を消さないように)
    bool ok = labs(moved) == count && expect.type == JsonValue::Type::String;
    if (ok) {
        std::wstring text(static_cast<size_t>(count) + 1, L'\0');
        ULONG length = 0;
        ok = SUCCEEDED(previous->GetText(ec, 0, text.data(), static_cast<ULONG>(text.size()), &length)) && length == static_cast<ULONG>(count) &&
             text.compare(0, length, expect.Str()) == 0;
    }
    if (!ok) {
        TipLog(L"前の文字が確定したときと違うので、確定し直しません");
        return false;
    }
    return SUCCEEDED(range->ShiftStart(ec, -count, &moved, nullptr));
}

void TextService::RewriteBeforeCaret(ITfContext* context, ITfRange* end, LONG count, const std::wstring& before, const std::wstring& text) {
    Microsoft::WRL::ComPtr<TextService> self = this;
    Microsoft::WRL::ComPtr<ITfContext> target = context;
    Microsoft::WRL::ComPtr<ITfRange> committedEnd = end;
    JsonValue expect;
    expect.type = JsonValue::Type::String;
    expect.string = before;
    RunEditSession(context, clientId_, TF_ES_ASYNC | TF_ES_READWRITE, [self, target, committedEnd, count, expect, text](TfEditCookie ec) {
        // 動くまでに次の語を打ち始めていたら書き換えない
        if (self->composition_ != nullptr) return S_OK;
        TF_SELECTION selection = {};
        ULONG fetched = 0;
        if (FAILED(target->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) || fetched == 0) return S_OK;
        Microsoft::WRL::ComPtr<ITfRange> range;
        range.Attach(selection.range);
        // キャレットが今の語の後ろのまま (選んでいない) で、その前が before のままのときだけ書き換える。
        // キャレットが動いた (同じ文字が並んだ別の所へ動いたときも)・前の文字を読めないときは、今の語を確定したままにする
        BOOL empty = FALSE, same = FALSE;
        if (FAILED(range->IsEmpty(ec, &empty)) || !empty || FAILED(range->IsEqualEnd(ec, committedEnd.Get(), TF_ANCHOR_END, &same)) || !same) return S_OK;
        if (!self->ExtendOverPrevious(ec, range.Get(), count, expect)) return S_OK;
        if (target.Get() == self->editSinkContext_) self->ownEdit_ = true;
        if (!self->WriteCommit(ec, target.Get(), range.Get(), text)) self->ownEdit_ = false;
        return S_OK;
    });
}

bool TextService::WriteCommit(TfEditCookie ec, ITfContext* context, ITfRange* range, const std::wstring& text, int caretBack, ITfRange** movedCaret) {
    if (composition_ == nullptr) {
        ITfContextComposition* contextComposition = nullptr;
        if (SUCCEEDED(context->QueryInterface(IID_ITfContextComposition, reinterpret_cast<void**>(&contextComposition)))) {
            if (SUCCEEDED(contextComposition->StartComposition(ec, range, static_cast<ITfCompositionSink*>(this), &composition_)) && composition_ != nullptr) {
                compositionContext_ = context;
                compositionContext_->AddRef();
            }
            contextComposition->Release();
        }
    }
    ITfProperty* attribute = nullptr;
    if (SUCCEEDED(context->GetProperty(GUID_PROP_ATTRIBUTE, &attribute))) {
        attribute->Clear(ec, range);
        attribute->Release();
    }
    const bool written = SUCCEEDED(range->SetText(ec, 0, text.c_str(), static_cast<LONG>(text.size())));
    // 同じ編集セッションで確定範囲を基準に選択位置を設定する。
    // クリック・別の入力欄で確定したときは、アプリが動かした所のままにする。
    if (written && !keepAppCaret_) {
        Microsoft::WRL::ComPtr<ITfRange> caretRange;
        if (SUCCEEDED(range->Clone(&caretRange))) {
            caretRange->Collapse(ec, TF_ANCHOR_END);
            Microsoft::WRL::ComPtr<ITfRange> target;
            bool inside = false;
            if (caretBack > 0 && static_cast<size_t>(caretBack) <= text.size()) {
                const LONG offset = static_cast<LONG>(text.size()) - caretBack;
                LONG moved = 0;
                inside = SUCCEEDED(range->Clone(&target)) && SUCCEEDED(target->Collapse(ec, TF_ANCHOR_START)) &&
                    SUCCEEDED(target->ShiftEnd(ec, offset, &moved, nullptr)) && moved == offset &&
                    SUCCEEDED(target->Collapse(ec, TF_ANCHOR_END));
            }
            TF_SELECTION caret = {};
            caret.range = inside ? target.Get() : caretRange.Get();
            caret.style.ase = inside ? TF_AE_END : TF_AE_NONE;
            caret.style.fInterimChar = FALSE;
            const HRESULT result = context->SetSelection(ec, 1, &caret);
            if (inside && SUCCEEDED(result)) {
                if (movedCaret != nullptr) *movedCaret = target.Detach();
            } else if (inside) {
                // 内側を指定できない場合も、通常の確定位置から続きの入力を反映する。
                caret.range = caretRange.Get();
                caret.style.ase = TF_AE_NONE;
                context->SetSelection(ec, 1, &caret);
            }
        }
    }
    EndComposition(ec);
    return written;
}

void TextService::ApplyComposition(TfEditCookie ec, ITfContext* context, const JsonValue& view) {
    const std::wstring& text = view[L"text"].Str();
    if (composition_ == nullptr) {
        TF_SELECTION selection = {};
        ULONG fetched = 0;
        if (FAILED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) || fetched == 0) return;
        ITfContextComposition* contextComposition = nullptr;
        if (SUCCEEDED(context->QueryInterface(IID_ITfContextComposition, reinterpret_cast<void**>(&contextComposition)))) {
            if (SUCCEEDED(contextComposition->StartComposition(ec, selection.range, static_cast<ITfCompositionSink*>(this), &composition_)) &&
                composition_ != nullptr) {
                compositionContext_ = context;
                compositionContext_->AddRef();
            }
            contextComposition->Release();
        }
        selection.range->Release();
        if (composition_ == nullptr) {
            TipLog(L"変換中の文字を置けません");
            return;
        }
    }

    ITfRange* range = nullptr;
    if (FAILED(composition_->GetRange(&range))) return;
    range->SetText(ec, 0, text.c_str(), static_cast<LONG>(text.size()));

    // 下線: 変換中は文節ごと (選んでいる文節は太線)、打っている途中は点線
    const auto& clauses = view[L"clauses"].array;
    if (view[L"converting"].Bool() && !clauses.empty()) {
        int selected = view[L"selectedClause"].Int();
        LONG offset = 0;
        for (size_t i = 0; i < clauses.size(); i++) {
            LONG length = static_cast<LONG>(clauses[i].Str().size());
            ITfRange* clause = nullptr;
            if (SUCCEEDED(range->Clone(&clause))) {
                LONG moved = 0;
                clause->Collapse(ec, TF_ANCHOR_START);
                clause->ShiftEnd(ec, offset + length, &moved, nullptr);
                clause->ShiftStart(ec, offset, &moved, nullptr);
                SetAttribute(ec, context, clause, static_cast<int>(i) == selected ? atomTarget_ : atomConverted_);
                clause->Release();
            }
            offset += length;
        }
    } else {
        SetAttribute(ec, context, range, atomInput_);
    }

    // キャレットは変換中の文字の後ろ
    ITfRange* caretRange = nullptr;
    if (SUCCEEDED(range->Clone(&caretRange))) {
        caretRange->Collapse(ec, TF_ANCHOR_END);
        TF_SELECTION caret = {};
        caret.range = caretRange;
        caret.style.ase = TF_AE_NONE;
        caret.style.fInterimChar = FALSE;
        context->SetSelection(ec, 1, &caret);
        caretRange->Release();
    }
    range->Release();
}

void TextService::SetAttribute(TfEditCookie ec, ITfContext* context, ITfRange* range, TfGuidAtom atom) {
    if (atom == TF_INVALID_GUIDATOM) return;
    ITfProperty* property = nullptr;
    if (FAILED(context->GetProperty(GUID_PROP_ATTRIBUTE, &property))) return;
    VARIANT value;
    VariantInit(&value);
    value.vt = VT_I4;
    value.lVal = static_cast<LONG>(atom);
    property->SetValue(ec, range, &value);
    property->Release();
}

void TextService::EndComposition(TfEditCookie ec) {
    if (composition_ == nullptr) return;
    ITfRange* range = nullptr;
    if (compositionContext_ != nullptr && SUCCEEDED(composition_->GetRange(&range))) {
        ITfProperty* property = nullptr;
        if (SUCCEEDED(compositionContext_->GetProperty(GUID_PROP_ATTRIBUTE, &property))) {
            property->Clear(ec, range);
            property->Release();
        }
        range->Release();
    }
    composition_->EndComposition(ec);
    composition_->Release();
    composition_ = nullptr;
    staleCommitTries_ = 0;
    staleVk_ = 0;
    if (compositionContext_ != nullptr) {
        compositionContext_->Release();
        compositionContext_ = nullptr;
    }
}

STDMETHODIMP TextService::OnCompositionTerminated(TfEditCookie ec, ITfComposition* composition) {
    // アプリの側で変換中の文字を確定した (入力欄の外をクリックしたなど)。文字は入力欄に残っているので、
    // Meltype.exe の側も確定したことにする (返ってきた確定の文字は入れない)
    TipLog(L"アプリが変換中の文字を確定しました");
    // 下線が残らないように (その変換中の文字がある入力欄で消す)
    ClearAttributeOf(ec, composition);
    if (composition_ == nullptr || !SameObject(composition, composition_)) {
        // 今の変換中の文字ではない (前の入力欄で手放した変換中の文字): 今の入力の状態には触らない
        ForgetAbandoned(composition);
        return S_OK;
    }
    // 打っている途中 (変換していない) なら、Meltype.exe の側はまだ確定しない。空のノートに最初の文字を入れたときなどに、
    // アプリが入力欄を作り直して確定してしまうことがある (そのまま確定すると、ka が「kあ」になる)。
    // キャレットがその文字の後ろのままなら、次のキーで変換中に戻して続ける (ResumeOrCommit)
    resumeRange_.Reset();
    resumeContext_.Reset();
    resumeText_.clear();
    if (!disabled_ && !deactivated_ && !lastConverting_ && compositionContext_ != nullptr) {
        Microsoft::WRL::ComPtr<ITfRange> range;
        if (SUCCEEDED(composition->GetRange(&range))) {
            wchar_t buffer[65];
            ULONG length = 0;
            if (SUCCEEDED(range->GetText(ec, 0, buffer, 64, &length)) && length > 0 && length < 64) {
                resumeRange_ = range;
                resumeContext_ = compositionContext_;
                resumeText_.assign(buffer, length);
            }
        }
    }
    if (composition_ != nullptr) {
        composition_->Release();
        composition_ = nullptr;
    }
    if (compositionContext_ != nullptr) {
        compositionContext_->Release();
        compositionContext_ = nullptr;
    }
    if (candidates_) candidates_->Hide();
    staleCommitTries_ = 0;
    staleVk_ = 0;
    lastConverting_ = false;
    if (resumeRange_) return S_OK;
    // 入力欄の文字は、アプリが書き換えたかもしれない: 前に確定した語を、次のキーで確定し直さない (関係ない文字を消さないように)
    CommittedOutside();
    return S_OK;
}

void TextService::ResumeOrCommit(ITfContext* context) {
    if (!resumeRange_) return;
    Microsoft::WRL::ComPtr<ITfRange> range = resumeRange_;
    Microsoft::WRL::ComPtr<ITfContext> resumeContext = resumeContext_;
    std::wstring text = resumeText_;
    resumeRange_.Reset();
    resumeContext_.Reset();
    resumeText_.clear();
    bool resumed = false;
    if (context != nullptr && resumeContext.Get() == context && composition_ == nullptr) {
        RunEditSession(context, clientId_, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie ec) {
            // 文字がアプリに確定されたときのままで、キャレットがその後ろにある (選んでいない) ときだけ
            wchar_t buffer[65];
            ULONG length = 0;
            if (FAILED(range->GetText(ec, 0, buffer, 64, &length)) || text.compare(0, std::wstring::npos, buffer, length) != 0) return S_OK;
            TF_SELECTION selection = {};
            ULONG fetched = 0;
            if (FAILED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) || fetched == 0) return S_OK;
            LONG start = 1, end = 1;
            selection.range->CompareStart(ec, range.Get(), TF_ANCHOR_END, &start);
            selection.range->CompareEnd(ec, range.Get(), TF_ANCHOR_END, &end);
            selection.range->Release();
            if (start != 0 || end != 0) return S_OK;
            ITfContextComposition* contextComposition = nullptr;
            if (FAILED(context->QueryInterface(IID_ITfContextComposition, reinterpret_cast<void**>(&contextComposition)))) return S_OK;
            if (context == editSinkContext_) ownEdit_ = true;
            if (SUCCEEDED(contextComposition->StartComposition(ec, range.Get(), static_cast<ITfCompositionSink*>(this), &composition_)) && composition_ != nullptr) {
                compositionContext_ = context;
                compositionContext_->AddRef();
                SetAttribute(ec, context, range.Get(), atomInput_);
                resumed = true;
            }
            contextComposition->Release();
            return S_OK;
        });
        ownEdit_ = false;
    }
    if (resumed) {
        TipLog(L"アプリが確定した打ちかけの文字を、変換中に戻しました");
        return;
    }
    // 戻せない (キャレットが動いた・文字が変わった・別の入力欄): 今までどおり確定したことにする
    CommittedOutside();
}

void TextService::CommitAsync(bool keepAppCaret, bool left) {
    if (composition_ == nullptr || compositionContext_ == nullptr) return;
    // 非同期の編集セッションは、実行されずに捨てられることもある。参照は ComPtr に持たせ、セッションと一緒に解放する
    // 動くまでに変換中の文字が変わっていたら (確定し終えた・別の入力欄で新しく始めた) 何もしない
    Microsoft::WRL::ComPtr<ITfContext> context = compositionContext_;
    Microsoft::WRL::ComPtr<ITfComposition> target = composition_;
    Microsoft::WRL::ComPtr<TextService> self = this;
    RunEditSession(context.Get(), clientId_, TF_ES_ASYNCDONTCARE | TF_ES_READWRITE, [self, context, target, keepAppCaret, left](TfEditCookie ec) {
        if (self->composition_ == target.Get()) self->CommitInSession(ec, context.Get(), keepAppCaret, left);
        return S_OK;
    });
}

void TextService::AbandonComposition() {
    // 前の入力欄の変換中の文字を、入力欄には触らずに手放す (文字はそのまま残る)。Meltype.exe の側も確定したことにする
    TipLog(L"前の入力欄の変換中の文字を確定できないので、手放します");
    Microsoft::WRL::ComPtr<ITfComposition> old;
    old.Attach(composition_);
    composition_ = nullptr;
    Microsoft::WRL::ComPtr<ITfContext> oldContext;
    oldContext.Attach(compositionContext_);
    compositionContext_ = nullptr;
    // 前の入力欄が編集を受け付けるようになったら、下線を消して終える (それまでにアプリの側が終えたら OnCompositionTerminated で消す)
    if (oldContext && old) {
        // いくつも溜まらないように (古いものは、アプリが終えたときに下線だけ消える)
        if (abandoned_.size() >= 8) abandoned_.erase(abandoned_.begin());
        abandoned_.push_back({old, oldContext});
        Microsoft::WRL::ComPtr<TextService> self = this;
        RunEditSession(oldContext.Get(), clientId_, TF_ES_ASYNCDONTCARE | TF_ES_READWRITE, [self, old](TfEditCookie ec) {
            // 終わった後 (Deactivate) は一覧を空にしているので、そのときも終える
            if (self->ForgetAbandoned(old.Get()) || self->deactivated_) {
                ClearAttributeOf(ec, old.Get());
                old->EndComposition(ec);
            }
            return S_OK;
        });
    }
    staleCommitTries_ = 0;
    staleVk_ = 0;
    if (candidates_) candidates_->Hide();
    CommittedOutside();
}

bool TextService::ForgetAbandoned(ITfComposition* composition) {
    for (auto it = abandoned_.begin(); it != abandoned_.end(); ++it) {
        if (SameObject(composition, it->composition.Get())) {
            abandoned_.erase(it);
            return true;
        }
    }
    return false;
}

void TextService::EndAbandonedNow() {
    std::vector<Abandoned> pending;
    pending.swap(abandoned_);
    for (Abandoned& item : pending) {
        Microsoft::WRL::ComPtr<ITfComposition> composition = item.composition;
        HRESULT hr = RunEditSession(item.context.Get(), clientId_, TF_ES_SYNC | TF_ES_READWRITE, [composition](TfEditCookie ec) {
            ClearAttributeOf(ec, composition.Get());
            composition->EndComposition(ec);
            return S_OK;
        });
        if (FAILED(hr)) TipLog(L"手放した変換中の文字を終えられませんでした: 0x%08X", hr);
    }
}

bool TextService::CommitNow(bool keepAppCaret, bool left) {
    // 同期で確定する (キーをアプリに通す前・別の入力欄に移ったときに、変換中の文字を確定しておくため)
    if (composition_ == nullptr || compositionContext_ == nullptr) return true;
    Microsoft::WRL::ComPtr<ITfContext> context = compositionContext_;
    HRESULT hr = RunEditSession(context.Get(), clientId_, TF_ES_SYNC | TF_ES_READWRITE, [this, context, keepAppCaret, left](TfEditCookie ec) {
        CommitInSession(ec, context.Get(), keepAppCaret, left);
        return S_OK;
    });
    ownEdit_ = false;
    return SUCCEEDED(hr);
}

void TextService::CommitInSession(TfEditCookie ec, ITfContext* context, bool keepAppCaret, bool left) {
    // 編集できるのは、この編集セッションの入力欄にある変換中の文字だけ
    if (composition_ != nullptr && context == compositionContext_) {
        // 終わりの通知 (OnEndEdit) が来るのは、今見ている入力欄だけ (フォーカスが移った後の前の入力欄では印を付けない。残ると次のクリックを見逃す)
        if (context == editSinkContext_) ownEdit_ = true;
        keepAppCaret_ = keepAppCaret;
        JsonValue reply;
        std::string field;
        ReadField(ec, context, field);
        // クリック・別の入力欄で確定する (keepAppCaret) ときは、キャレットが動いたので前の語を確定し直さない
        if (Send("CommitRequested", field, keepAppCaret ? CaretMoved(left) : "", reply)) ApplyOutput(ec, context, reply);
        keepAppCaret_ = false;
        EndComposition(ec);
    }
    if (candidates_) candidates_->Hide();
}

void TextService::SelectCandidate(int index) {
    if (compositionContext_ == nullptr) return;
    Microsoft::WRL::ComPtr<ITfContext> context = compositionContext_;
    Microsoft::WRL::ComPtr<ITfComposition> target = composition_;
    Microsoft::WRL::ComPtr<TextService> self = this;
    RunEditSession(context.Get(), clientId_, TF_ES_ASYNCDONTCARE | TF_ES_READWRITE, [self, context, target, index](TfEditCookie ec) {
        // 動くまでに変換中の文字が変わっていたら選ばない
        if (self->composition_ != target.Get() || self->compositionContext_ != context.Get()) return S_OK;
        self->ownEdit_ = true;
        JsonValue reply;
        if (self->Send("CandidateClicked", "", ",\"index\":" + std::to_string(index), reply)) {
            self->ApplyOutput(ec, context.Get(), reply);
        }
        return S_OK;
    });
}

// ---- 候補 ----

void TextService::ShowCandidates(TfEditCookie ec, ITfContext* context, const JsonValue& view) {
    if (!candidates_) return;
    // 出すもの (候補の一覧・もしかして・打ったキー・案内) は Meltype.exe が決める。どれも無ければ出さない
    CandidateView data;
    const JsonValue& list = view[L"candidates"];
    for (const JsonValue& item : list[L"items"].array) data.candidates.push_back(item.Str());
    for (const JsonValue& item : list[L"notes"].array) data.notes.push_back(item.Str());
    data.selected = list.IsNull() ? -1 : list[L"selected"].Int();
    data.numbered = list[L"numbered"].Bool();
    data.meaning = list[L"selectedMeaning"].Str();
    data.meaningMs = static_cast<UINT>(list[L"meaningDelayMs"].Int());
    data.suggestion = view[L"spellingSuggestion"].Str();
    data.typed = view[L"typedKeys"].Str();
    data.hint = view[L"hint"].Str();
    const bool converting = view[L"converting"].Bool();
    if (data.candidates.empty() && data.suggestion.empty() && data.typed.empty() && data.hint.empty()) {
        candidates_->Hide();
        return;
    }

    // 選んでいる文節の画面上の位置
    RECT anchor = {};
    ITfRange* range = nullptr;
    if (composition_ != nullptr && SUCCEEDED(composition_->GetRange(&range))) {
        ITfRange* target = nullptr;
        if (SUCCEEDED(range->Clone(&target))) {
            const auto& clauses = view[L"clauses"].array;
            int selected = view[L"selectedClause"].Int();
            if (converting && selected >= 0 && selected < static_cast<int>(clauses.size())) {
                LONG offset = 0;
                for (int i = 0; i < selected; i++) offset += static_cast<LONG>(clauses[i].Str().size());
                LONG moved = 0;
                target->Collapse(ec, TF_ANCHOR_START);
                target->ShiftEnd(ec, offset + static_cast<LONG>(clauses[selected].Str().size()), &moved, nullptr);
                target->ShiftStart(ec, offset, &moved, nullptr);
            }
            ITfContextView* contextView = nullptr;
            if (SUCCEEDED(context->GetActiveView(&contextView))) {
                BOOL clipped = FALSE;
                if (FAILED(contextView->GetTextExt(ec, target, &anchor, &clipped))) anchor = {};
                contextView->Release();
            }
            target->Release();
        }
        range->Release();
    }
    if (anchor.left == 0 && anchor.top == 0 && anchor.right == 0 && anchor.bottom == 0) {
        // 位置が分からないアプリ: キャレット → マウスカーソルの近く
        GUITHREADINFO info = {sizeof(info)};
        if (GetGUIThreadInfo(GetCurrentThreadId(), &info) && info.hwndCaret != nullptr) {
            POINT point = {info.rcCaret.left, info.rcCaret.top};
            ClientToScreen(info.hwndCaret, &point);
            anchor = {point.x, point.y, point.x + 1, point.y + (info.rcCaret.bottom - info.rcCaret.top)};
        } else {
            POINT point = {};
            GetCursorPos(&point);
            anchor = {point.x, point.y, point.x + 1, point.y + 16};
        }
    }
    candidates_->Show(data, anchor);
}

// ---- 下線の種類 ----

STDMETHODIMP TextService::EnumDisplayAttributeInfo(IEnumTfDisplayAttributeInfo** ppEnum) {
    if (ppEnum == nullptr) return E_INVALIDARG;
    *ppEnum = CreateDisplayAttributeEnum();
    return *ppEnum ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP TextService::GetDisplayAttributeInfo(REFGUID guid, ITfDisplayAttributeInfo** ppInfo) {
    if (ppInfo == nullptr) return E_INVALIDARG;
    *ppInfo = CreateDisplayAttributeInfo(guid);
    return *ppInfo ? S_OK : E_INVALIDARG;
}

}  // namespace meltype
