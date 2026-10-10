// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai

#include "TextService.h"

#include <InputScope.h>
#include <wrl/client.h>

#include <cstdlib>
#include <cwctype>

#include "DisplayAttributes.h"
#include "LangBar.h"

namespace meltype {
namespace {

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
        case IS_PRIVATE:
            return true;
        default:
            return false;
    }
}

// 送り直したキーに付ける印 (SendInput の dwExtraInfo)。'MLTP' (Meltype.exe のキーボードフックが送る印 'MELT' とは別にする)
constexpr ULONG_PTR kReinjectMarker = 0x4D4C5450;

// 修飾キーだけのキー (それ自体では入力にならない)
bool IsModifier(UINT vk) {
    switch (vk) {
        case VK_SHIFT: case VK_LSHIFT: case VK_RSHIFT:
        case VK_CONTROL: case VK_LCONTROL: case VK_RCONTROL:
        case VK_MENU: case VK_LMENU: case VK_RMENU:
        case VK_LWIN: case VK_RWIN: case VK_CAPITAL:
            return true;
        default:
            return false;
    }
}

// 半角/全角 キー (日本語キーボード)
bool IsHankakuZenkaku(UINT vk) { return vk == VK_KANJI || vk == 0xF3 || vk == 0xF4; }

bool KeyDown(int vk) { return (GetKeyState(vk) & 0x8000) != 0; }

// 変換中に Meltype.exe が受け持つ Ctrl+英字 (CompositionController.ControlShortcut と合わせる)。Alt・Windows キーと一緒なら違う
bool IsCompositionShortcut(UINT vk) {
    if (!KeyDown(VK_CONTROL) || KeyDown(VK_MENU) || KeyDown(VK_LWIN) || KeyDown(VK_RWIN)) return false;
    return vk == 'U' || vk == 'I' || vk == 'O' || vk == 'P';
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

bool IsOwnProcess() {
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    const wchar_t* name = wcsrchr(path, L'\\');
    name = name ? name + 1 : path;
    return _wcsicmp(name, L"Meltype.exe") == 0;
}

std::wstring ProcessName() {
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    const wchar_t* name = wcsrchr(path, L'\\');
    std::wstring result = name ? name + 1 : path;
    if (result.size() > 4 && _wcsicmp(result.c_str() + result.size() - 4, L".exe") == 0) result.resize(result.size() - 4);
    return result;
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

TextService::TextService() { DllAddRef(); }

TextService::~TextService() { DllRelease(); }

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
    // Windows は、同じアプリの中で一度終えた IME を、同じオブジェクトのまま起動し直すことがある。前の状態をすべて初期化する
    // (終わったときの印 deactivated_ が残ると、二度と Meltype.exe とやり取りしなくなる)
    deactivated_ = false;
    serverActive_ = false;
    lastHello_ = 0;
    caretMoved_ = false;
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
    keyFailed_ = false;
    consumed_ = true;
    for (bool& eaten : eatenDown_) eaten = false;
    // サインインや UAC の画面では何もしない。
    // Meltype.exe より高い権限で動いているアプリ (管理者として動くアプリなど) は、つなぐときに断る (Pipe.cpp の IsTrustedServer)
    // (Meltype.exe 自身の画面でも使う。そのときは Meltype.exe が画面のスレッドを通さずに処理する: TipServer の Serve)
    const wchar_t* reason = (flags & TF_TMAE_SECUREMODE) ? L"セキュリティ保護された画面" : nullptr;
    ownProcess_ = IsOwnProcess();
    disabled_ = reason != nullptr;
    sid_ = RandomId();
    TipLog(L"Activate (%s)%s%s", ProcessName().c_str(), disabled_ ? L" 無効: " : L"", disabled_ ? reason : L"");

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
    // 何もしないプロセス (Meltype.exe 自身・サインインの画面) では、入力モードを変えず、タスクバーの「あ」も出さない (キーはそのまま通るので)
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
    if (!disabled_) Hello();
    return S_OK;
}

STDMETHODIMP TextService::Deactivate() {
    TipLog(L"Deactivate");
    if (composition_ != nullptr && compositionContext_ != nullptr) {
        // 確定してから終わる
        ITfContext* context = compositionContext_;
        context->AddRef();
        RunEditSession(context, clientId_, TF_ES_SYNC | TF_ES_READWRITE, [this, context](TfEditCookie ec) {
            JsonValue reply;
            if (Request(CommitRequest(true), reply)) Apply(ec, context, reply);
            EndComposition(ec);
            return S_OK;
        });
        context->Release();
    }
    if (!disabled_) {
        JsonValue reply;
        Request("{\"op\":\"close\",\"sid\":\"" + sid_ + "\"}", reply);
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
    if (composition_ != nullptr && !CommitNow(true)) CommitAsync(true);
    // アプリが確定した打ちかけの文字は、別の入力欄に移ったので確定したことにする
    ResumeOrCommit(nullptr);
    caretMoved_ = true;
    ForgetSecretField();
    if (candidates_) candidates_->Hide();
    AdviseTextEditSink(focus);
    return S_OK;
}

STDMETHODIMP TextService::OnSetFocus(BOOL foreground) {
    if (!foreground && candidates_) candidates_->Hide();
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

bool TextService::IsSecretField(TfEditCookie ec, ITfContext* context) {
    // 入力欄の種類 (InputScope) がパスワード・暗証番号か。
    // (入力欄が開いたときに非同期の編集セッションで読むと、それが終わるまでキーが届かなくなるアプリがあるので、キーの編集セッションの中で読む)
    // 種類は 2 か所にある: Windows が持つもの (SetInputScope などで決めたもの) と、アプリが自分で知らせるもの (自前で TSF に対応したアプリ。
    // ブラウザーなど)。どちらかがパスワードと言えばパスワード欄とする。どちらにも無ければ、種類を決めていない入力欄
    // (ふつうのアプリの多く。ブラウザーはパスワード欄では IME を無効にもするので、それは ContextDisabled で見る)。
    // 種類の値はあるのに中身を読めないときは、パスワード欄かもしれないので、パスワード欄として扱う (打った文字を Meltype.exe に送らない)。
    // 問い合わせ自体ができないとき (対応していないアプリ・選択が無い) は、種類を決めていない入力欄と同じにする
    // (止めると、そのアプリでは Meltype IME がまったく使えなくなるため)
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
    for (ITfReadOnlyProperty* property : {static_cast<ITfReadOnlyProperty*>(windowsProperty.Get()), appProperty.Get()}) {
        if (property == nullptr) continue;
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
                    if (IsSecretScope(scopes[i])) secret = true;
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

std::string TextService::CommitRequest(bool moved) const {
    // moved: キャレットが動いた (クリック・別の入力欄・アプリが確定した) ので、前に確定した語を確定し直さない
    return "{\"op\":\"commit\",\"sid\":\"" + sid_ + "\"" + (moved ? ",\"moved\":true" : "") + "}";
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
    if (FAILED(record->GetSelectionStatus(&changed)) || !changed) return S_OK;
    if (composition_ == nullptr || context != compositionContext_) {
        // 変換していないとき (変換中の文字が別の入力欄に残っているときも) に、クリックなどでキャレットが動いた: 前に確定した語を確定し直さない
        caretMoved_ = true;
        return S_OK;
    }
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
        caretMoved_ = true;
        CommitAsync(true);
    }
    return S_OK;
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
    UpdateLangBar();
    return S_OK;
}

void TextService::SetOpen(bool open) {
    if (threadMgr_ == nullptr) return;
    ITfCompartmentMgr* compartments = nullptr;
    if (FAILED(threadMgr_->QueryInterface(IID_ITfCompartmentMgr, reinterpret_cast<void**>(&compartments)))) return;
    ITfCompartment* openClose = nullptr;
    if (SUCCEEDED(compartments->GetCompartment(GUID_COMPARTMENT_KEYBOARD_OPENCLOSE, &openClose))) {
        VARIANT value;
        VariantInit(&value);
        value.vt = VT_I4;
        value.lVal = open ? 1 : 0;
        openClose->SetValue(clientId_, &value);  // OnChange が呼ばれる
        openClose->Release();
    }
    compartments->Release();
}

void TextService::UpdateLangBar() {
    if (langBar_ != nullptr) langBar_->Update();
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

bool TextService::ServerReady() {
    if (disabled_) return false;
    if (!serverActive_ && GetTickCount64() - lastHello_ > 2000) Hello();
    return serverActive_;
}

void TextService::Hello() {
    lastHello_ = GetTickCount64();
    JsonValue reply;
    serverActive_ = Request("{\"op\":\"hello\",\"process\":" + JsonString(ProcessName()) + "}", reply) && reply[L"active"].Bool();
    TipLog(L"Meltype.exe: %s", serverActive_ ? L"つながりました" : L"使えません (動いていない・モードが Meltype IME でない)");
}

void TextService::ForgetSecretField() {
    secretVk_ = 0;
    secretContext_.Reset();
}

bool TextService::WouldEat(ITfContext* context, UINT vk, wchar_t& ch, bool test) {
    ch = 0;
    secretChecked_ = false;
    secretWhileComposing_ = false;
    if (disabled_ || vk >= 256) return false;
    if (IsModifier(vk)) return false;
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
        // ただし変換ボックスの中で使う Ctrl+英字 (Ctrl+U/I/O/P はかな・英字の切り替え) は、確定せずに Meltype.exe に送る
        if ((KeyDown(VK_CONTROL) || KeyDown(VK_MENU) || KeyDown(VK_LWIN) || KeyDown(VK_RWIN)) && !IsCompositionShortcut(vk) && CommitNow()) return false;
        return true;  // 変換中はほかのキーを全部受け取る (つながらなければ確定して通す)
    }
    // 半角/全角: 日本語 ⇔ 英数 (IME の ON/OFF は IME 自身が切り替える)。Meltype.exe が使えないときはアプリに通す
    if (IsHankakuZenkaku(vk)) return !KeyDown(VK_CONTROL) && !KeyDown(VK_MENU) && ServerReady();
    // 調べるとき用: 文字を生むキーを通した理由 (どのキーかは書かない)
    auto pass = [&](const wchar_t* reason) {
        if (passReason_ != reason) TipLog(L"キーを通します: %s", reason);
        passReason_ = reason;
        return false;
    };
    if (!open_) return pass(L"英数");
    if (KeyDown(VK_CONTROL) || KeyDown(VK_MENU) || KeyDown(VK_LWIN) || KeyDown(VK_RWIN)) return pass(L"Ctrl・Alt・Windows キーを押している");
    if (ContextDisabled(context)) return pass(L"この入力欄では使わない");
    ch = CharOf(vk, 0);
    bool starts = (ch >= L'a' && ch <= L'z') || (ch >= L'A' && ch <= L'Z') || (ch >= L'!' && ch <= L'~');
    if (!starts) return false;
    if (!ServerReady()) return pass(L"Meltype.exe が使えない");
    // パスワード欄か。OnTestKeyDown で読んだ結果は、続く同じキー・同じ入力欄の OnKeyDown で 1 回だけ使う (ほかのキー・入力欄には使わない)
    ULONGLONG now = GetTickCount64();
    int secret;
    if (!test && vk == secretVk_ && context == secretContext_.Get() && now - secretTime_ < 200) {
        secret = secretResult_;
    } else {
        secret = ReadSecretField(context);
    }
    ForgetSecretField();
    if (test) {
        secretVk_ = vk;
        secretContext_ = context;
        secretTime_ = now;
        secretResult_ = secret;
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

STDMETHODIMP TextService::OnTestKeyDown(ITfContext* context, WPARAM wParam, LPARAM, BOOL* eaten) {
    UINT vk = static_cast<UINT>(wParam);
    if (IsReinjected(vk)) {
        passVk_ = vk;  // アプリによっては、このあと OnKeyDown も呼ぶ
        passTime_ = GetTickCount64();
        caretMoved_ = true;
        *eaten = FALSE;
        return S_OK;
    }
    passVk_ = 0;
    wchar_t ch = 0;
    *eaten = WouldEat(context, vk, ch, true) ? TRUE : FALSE;
    // アプリにそのまま渡すキー (BackSpace・矢印・Enter・Space など) はキャレットを動かす: 前に確定した語を確定し直さない
    if (!*eaten && !IsModifier(vk)) caretMoved_ = true;
    return S_OK;
}

STDMETHODIMP TextService::OnKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) {
    UINT vk = static_cast<UINT>(wParam);
    if ((vk == passVk_ && GetTickCount64() - passTime_ < 500) || IsReinjected(vk)) {
        passVk_ = 0;
        caretMoved_ = true;
        *eaten = FALSE;
        return S_OK;
    }
    passVk_ = 0;
    wchar_t ch = 0;
    if (!WouldEat(context, vk, ch, false)) {
        if (!IsModifier(vk)) caretMoved_ = true;
        *eaten = FALSE;
        return S_OK;
    }
    *eaten = TRUE;
    eatenDown_[vk] = true;
    if (IsHankakuZenkaku(vk) && composition_ == nullptr) {
        SetOpen(!open_);
        return S_OK;
    }
    ch = CharOf(vk, lParam);
    HandleKey(context, vk, ch);
    return S_OK;
}

STDMETHODIMP TextService::OnTestKeyUp(ITfContext*, WPARAM wParam, LPARAM, BOOL* eaten) {
    *eaten = wParam < 256 && eatenDown_[wParam] ? TRUE : FALSE;
    return S_OK;
}

STDMETHODIMP TextService::OnKeyUp(ITfContext*, WPARAM wParam, LPARAM, BOOL* eaten) {
    *eaten = wParam < 256 && eatenDown_[wParam] ? TRUE : FALSE;
    if (wParam < 256) eatenDown_[wParam] = false;
    return S_OK;
}

void TextService::HandleKey(ITfContext* context, UINT vk, wchar_t ch) {
    // 8 = Windows キー (Meltype.exe は Mac の Command と同じく、確定してアプリに渡す)
    int mods = (KeyDown(VK_SHIFT) ? 1 : 0) | (KeyDown(VK_CONTROL) ? 2 : 0) | (KeyDown(VK_MENU) ? 4 : 0) | (KeyDown(VK_LWIN) || KeyDown(VK_RWIN) ? 8 : 0);
    keyFailed_ = false;
    consumed_ = true;
    bool insertedOnFailure = false;
    HRESULT hr = RunEditSession(context, clientId_, TF_ES_SYNC | TF_ES_READWRITE, [&](TfEditCookie ec) {
        // キーを受け取る前に入力欄の種類を読めなかった: ここで確かめ、パスワード欄なら Meltype.exe に送らずにアプリへ返す
        // (変換中なら、変換中の文字を確定してから。変換中にパスワード欄になったのに、受け取る前に確定できなかったときも)
        if (secretWhileComposing_ || (!secretChecked_ && IsSecretField(ec, context))) {
            if (composition_ != nullptr) CommitInSession(ec, context, true);
            consumed_ = false;
            TipLog(L"パスワードの入力欄なので何もしません");
            return S_OK;
        }
        ownEdit_ = true;
        std::string request = "{\"op\":\"key\",\"sid\":\"" + sid_ + "\",\"vk\":" + std::to_string(vk) + ",\"ch\":" + std::to_string(ch) +
                              ",\"mods\":" + std::to_string(mods) + ",\"process\":" + JsonString(ProcessName());
        // 前に確定してからキャレットが動いた: Meltype.exe に、前の語を確定し直さないように伝える
        if (caretMoved_) request += ",\"moved\":true";
        caretMoved_ = false;
        if (composition_ == nullptr) {
            std::wstring before, after;
            SurroundingText(ec, context, before, after);
            request += ",\"before\":" + JsonString(before) + ",\"after\":" + JsonString(after);
        } else {
            // 変換中: Meltype.exe に入力の続きが無ければ (つなぎ直した)、新しく始めずに知らせてもらう
            request += ",\"composing\":true";
        }
        request += "}";
        JsonValue reply;
        if (!Request(request, reply)) {
            // Meltype.exe が応答しない: 変換中の文字はそのまま確定する。打った文字はここで入れる
            // (送り直すと、その間に打った次のキーより後に届いて順番が入れ替わる)
            keyFailed_ = true;
            serverActive_ = false;
            if (candidates_) candidates_->Hide();
            EndComposition(ec);
            // Ctrl・Alt・Windows キーとの組み合わせは、文字を入れずにアプリに送り直す
            if (ch >= 0x20 && (mods & (2 | 4 | 8)) == 0) {
                TF_SELECTION selection = {};
                ULONG fetched = 0;
                if (SUCCEEDED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) && fetched > 0) {
                    insertedOnFailure = WriteCommit(ec, context, selection.range, std::wstring(1, ch));
                    selection.range->Release();
                }
            }
            return S_OK;
        }
        serverActive_ = reply[L"active"].Bool();
        consumed_ = reply[L"consumed"].Bool();
        // 入力の続きが無いので受け持たなかっただけ (つなぎ直した) かもしれない: 次のキーで、すぐに問い合わせ直す
        if (!serverActive_ && composition_ != nullptr) lastHello_ = 0;
        Apply(ec, context, reply);
        return S_OK;
    });
    // 同期の編集セッションの終わりの通知 (OnEndEdit) はもう来ている。何も変えなかったときに印が残らないように戻す
    ownEdit_ = false;
    if (FAILED(hr)) {
        TipLog(L"編集セッションを始められません: 0x%08X", hr);
        keyFailed_ = true;
        // キーを送り直す前に、変換中の文字を確定する (キャレットは動かさない)
        if (composition_ != nullptr) CommitAsync(true);
    } else if (keyFailed_) {
        TipLog(L"Meltype.exe から応答が来ないので、打った文字をそのまま入れました");
    }
    if ((keyFailed_ && !insertedOnFailure) || !consumed_) Reinject(vk);
}

void TextService::Reinject(UINT vk) {
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
        return;
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
    }
}

bool TextService::Request(const std::string& json, JsonValue& reply) {
    if (deactivated_) return false;
    std::string response;
    // Meltype.exe 自身の中: どのスレッドからの要求かを付ける (画面のスレッドからなら、Meltype.exe はそのスレッドを待たずに処理する。
    // 待つと、応答を待って止まっているこのスレッドとお互いに待ち合って固まる)
    std::string request = json;
    if (ownProcess_ && !request.empty() && request.back() == '}') {
        request.insert(request.size() - 1, ",\"tid\":" + std::to_string(GetCurrentThreadId()));
    }
    // 初めての変換は Mozc の起動などで時間がかかることがあるので、少し長めに待つ
    if (!pipe_.Transact(request, response, 800)) return false;
    if (!ParseJson(response, reply)) {
        TipLog(L"応答を読めません");
        return false;
    }
    return true;
}

void TextService::SurroundingText(TfEditCookie ec, ITfContext* context, std::wstring& before, std::wstring& after) {
    TF_SELECTION selection = {};
    ULONG fetched = 0;
    if (FAILED(context->GetSelection(ec, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) || fetched == 0) return;
    wchar_t buffer[32];
    ITfRange* range = nullptr;
    if (SUCCEEDED(selection.range->Clone(&range))) {
        LONG moved = 0;
        range->Collapse(ec, TF_ANCHOR_START);
        range->ShiftStart(ec, -20, &moved, nullptr);
        ULONG length = 0;
        if (SUCCEEDED(range->GetText(ec, 0, buffer, 20, &length))) before.assign(buffer, length);
        range->Release();
    }
    if (SUCCEEDED(selection.range->Clone(&range))) {
        LONG moved = 0;
        range->Collapse(ec, TF_ANCHOR_END);
        range->ShiftEnd(ec, 20, &moved, nullptr);
        ULONG length = 0;
        if (SUCCEEDED(range->GetText(ec, 0, buffer, 20, &length))) after.assign(buffer, length);
        range->Release();
    }
    selection.range->Release();
}

// ---- 入力欄への反映 ----

void TextService::Apply(TfEditCookie ec, ITfContext* context, const JsonValue& reply) {
    // 1 つの応答の中の確定は、前の確定の後ろに続けて入れる (クリックで確定したときは、キャレットがもうクリックした所にあるので、
    // キャレットの位置に入れると、クリックした所に入ったり、選んでいた文字を上書きしたりする)
    Microsoft::WRL::ComPtr<ITfRange> next;
    for (const JsonValue& edit : reply[L"commits"].array) {
        const std::wstring& text = edit[L"text"].Str();
        int deleteBefore = edit[L"deleteBefore"].Int();
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
        // 確定し直し: 前に確定した文字を消して入れ直す。前の文字が確定したときのままでなければ (アプリが書き換えた・前の文字を読めない)、
        // 関係ない文字を消したり、古い文字に重ねて入れたりしないように、確定し直さない (変換中の文字は、次の確定でそのまま確定する)
        if (deleteBefore > 0 && !ExtendOverPrevious(ec, range.Get(), deleteBefore, edit[L"expect"])) continue;
        if (!WriteCommit(ec, context, range.Get(), text)) continue;
        next.Reset();
        if (SUCCEEDED(range->Clone(&next))) next->Collapse(ec, TF_ANCHOR_END);
    }

    const JsonValue& view = reply[L"view"];
    lastConverting_ = !view.IsNull() && view[L"converting"].Bool();
    if (!view.IsNull() && !view[L"text"].Str().empty()) {
        ApplyComposition(ec, context, view);
        ShowCandidates(ec, context, view);
    } else {
        if (composition_ != nullptr) {
            // 変換中の文字を消した (BackSpace で全部消した・Esc で取り消した)。
            // Meltype.exe がこの入力を受け持っていない (一時停止した・つなぎ直して入力の続きが無い) ときは、消さずにそのまま確定する
            ITfRange* range = nullptr;
            if (reply[L"active"].Bool() && SUCCEEDED(composition_->GetRange(&range))) {
                range->SetText(ec, 0, L"", 0);
                range->Release();
            }
            EndComposition(ec);
        }
        if (candidates_) candidates_->Hide();
    }
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

bool TextService::WriteCommit(TfEditCookie ec, ITfContext* context, ITfRange* range, const std::wstring& text) {
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
    bool written = SUCCEEDED(range->SetText(ec, 0, text.c_str(), static_cast<LONG>(text.size())));
    // キャレットを確定した文字の後ろへ (クリック・別の入力欄で確定したときは、アプリが動かした所のまま)
    if (written && !keepAppCaret_) {
        Microsoft::WRL::ComPtr<ITfRange> caretRange;
        if (SUCCEEDED(range->Clone(&caretRange))) {
            caretRange->Collapse(ec, TF_ANCHOR_END);
            TF_SELECTION caret = {};
            caret.range = caretRange.Get();
            caret.style.ase = TF_AE_NONE;
            caret.style.fInterimChar = FALSE;
            context->SetSelection(ec, 1, &caret);
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
    caretMoved_ = true;
    if (!disabled_) {
        JsonValue reply;
        Request(CommitRequest(true), reply);
    }
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
    caretMoved_ = true;
    JsonValue reply;
    Request(CommitRequest(true), reply);
}

void TextService::CommitAsync(bool keepAppCaret) {
    if (composition_ == nullptr || compositionContext_ == nullptr) return;
    // 非同期の編集セッションは、実行されずに捨てられることもある。参照は ComPtr に持たせ、セッションと一緒に解放する
    // 動くまでに変換中の文字が変わっていたら (確定し終えた・別の入力欄で新しく始めた) 何もしない
    Microsoft::WRL::ComPtr<ITfContext> context = compositionContext_;
    Microsoft::WRL::ComPtr<ITfComposition> target = composition_;
    Microsoft::WRL::ComPtr<TextService> self = this;
    RunEditSession(context.Get(), clientId_, TF_ES_ASYNCDONTCARE | TF_ES_READWRITE, [self, context, target, keepAppCaret](TfEditCookie ec) {
        if (self->composition_ == target.Get()) self->CommitInSession(ec, context.Get(), keepAppCaret);
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
    caretMoved_ = true;
    JsonValue reply;
    Request(CommitRequest(true), reply);
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

bool TextService::CommitNow(bool keepAppCaret) {
    // 同期で確定する (キーをアプリに通す前・別の入力欄に移ったときに、変換中の文字を確定しておくため)
    if (composition_ == nullptr || compositionContext_ == nullptr) return true;
    Microsoft::WRL::ComPtr<ITfContext> context = compositionContext_;
    HRESULT hr = RunEditSession(context.Get(), clientId_, TF_ES_SYNC | TF_ES_READWRITE, [this, context, keepAppCaret](TfEditCookie ec) {
        CommitInSession(ec, context.Get(), keepAppCaret);
        return S_OK;
    });
    ownEdit_ = false;
    return SUCCEEDED(hr);
}

void TextService::CommitInSession(TfEditCookie ec, ITfContext* context, bool keepAppCaret) {
    // 編集できるのは、この編集セッションの入力欄にある変換中の文字だけ
    if (composition_ != nullptr && context == compositionContext_) {
        // 終わりの通知 (OnEndEdit) が来るのは、今見ている入力欄だけ (フォーカスが移った後の前の入力欄では印を付けない。残ると次のクリックを見逃す)
        if (context == editSinkContext_) ownEdit_ = true;
        keepAppCaret_ = keepAppCaret;
        JsonValue reply;
        // クリック・別の入力欄で確定する (keepAppCaret) ときは、キャレットが動いたので前の語を確定し直さない
        if (Request(CommitRequest(keepAppCaret), reply)) Apply(ec, context, reply);
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
        if (self->Request("{\"op\":\"select\",\"sid\":\"" + self->sid_ + "\",\"index\":" + std::to_string(index) + "}", reply)) {
            self->Apply(ec, context.Get(), reply);
        }
        return S_OK;
    });
}

// ---- 候補 ----

void TextService::ShowCandidates(TfEditCookie ec, ITfContext* context, const JsonValue& view) {
    if (!candidates_) return;
    CandidateView data;
    for (const JsonValue& item : view[L"candidates"].array) data.candidates.push_back(item.Str());
    for (const JsonValue& item : view[L"notes"].array) data.notes.push_back(item.Str());
    data.selected = view[L"selectedIndex"].Int();
    data.suggestion = view[L"suggestion"].Str();
    data.meaning = view[L"meaning"].Str();
    bool converting = view[L"converting"].Bool();
    if (!((converting && data.candidates.size() > 1) || !data.suggestion.empty())) {
        candidates_->Hide();
        return;
    }
    if (!converting) {
        data.candidates.clear();
        data.notes.clear();
        data.selected = -1;
        data.meaning.clear();
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
