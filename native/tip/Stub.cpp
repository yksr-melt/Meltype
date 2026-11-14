// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
//
// ロードする impl は、stub と同じフォルダーの MeltypeTipImpl.dll.sha256 (impl の SHA-256) で決まり、
// impl は impl\<SHA-256>\MeltypeTipImpl.dll にある (どちらも Register-Tip.ps1 が配置する)。Meltype.exe からは指定させない
// (パイプのサーバーを偽装したプロセスによる、古い impl へのダウングレードを防ぐ)。
// impl の中身の SHA-256 は照合しない。改竄は、置き場所 (Program Files) の ACL で防ぐ。管理者以外が書けるフォルダーに置くなら、中身を照合する。
// TextService を生成した impl は FreeLibrary しない。

#include "Stub.h"

#include <new>

namespace meltype {
namespace {

std::wstring StubFolder() {
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(g_module, path, MAX_PATH);
    std::wstring folder = path;
    return folder.substr(0, folder.find_last_of(L'\\'));
}

// MeltypeTipImpl.dll.sha256 の内容 (16 進 64 文字)。読めない・形式が違うなら空
std::wstring CurrentImpl() {
    HANDLE file = CreateFileW((StubFolder() + L"\\MeltypeTipImpl.dll.sha256").c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                              nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return {};
    char buffer[128] = {};
    DWORD read = 0;
    ReadFile(file, buffer, sizeof(buffer), &read, nullptr);
    CloseHandle(file);
    std::wstring hash;
    for (DWORD i = 0; i < read; i++) {
        const char c = buffer[i];
        if (c == ' ' || c == '\r' || c == '\n') continue;
        if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))) return {};
        hash += static_cast<wchar_t>(c);
    }
    return hash.size() == 64 ? hash : std::wstring();
}

ITfTextInputProcessorEx* CreateInner(const std::wstring& hash, HWND window) {
    if (hash.empty()) {
        TipLog(L"stub: MeltypeTipImpl.dll.sha256 を読めません");
        return nullptr;
    }
    // SHA-256 はファイル名にしない。Gecko (Firefox など) は、ファイル名の . より前が 16 桁以上の 16 進数の DLL のロードを拒否する
    // (browser/app/winlauncher/freestanding/DllBlocklist.cpp の DetermineBlockAction)。フォルダー名は判定しない
    std::wstring path = StubFolder() + L"\\impl\\" + hash + L"\\MeltypeTipImpl.dll";
    HMODULE module = LoadLibraryExW(path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (module == nullptr) {
        TipLog(L"stub: %s をロードできません: %lu", path.c_str(), GetLastError());
        return nullptr;
    }
    auto abi = reinterpret_cast<MeltypeTipAbiFn>(reinterpret_cast<void*>(GetProcAddress(module, "MeltypeTipAbi")));
    if (abi == nullptr || abi() != kStubImplAbi) {
        TipLog(L"stub: %s は ABI が違います", path.c_str());
        FreeLibrary(module);
        return nullptr;
    }
    auto create = reinterpret_cast<MeltypeTipCreateFn>(reinterpret_cast<void*>(GetProcAddress(module, "MeltypeTipCreate")));
    ITfTextInputProcessorEx* inner = nullptr;
    if (create == nullptr || FAILED(create(window, &inner))) {
        TipLog(L"stub: %s の TextService を生成できません", path.c_str());
        FreeLibrary(module);
        return nullptr;
    }
    return inner;
}

constexpr wchar_t kWindowClass[] = L"Meltype.Tip.Stub";

class StubService final : public ITfTextInputProcessorEx, public ITfDisplayAttributeProvider {
public:
    StubService() {
        static const ATOM registered = [] {
            WNDCLASSW cls{};
            cls.lpfnWndProc = WindowProc;
            cls.hInstance = g_module;
            cls.lpszClassName = kWindowClass;
            return RegisterClassW(&cls);
        }();
        (void)registered;
        window_ = CreateWindowExW(0, kWindowClass, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, g_module, nullptr);
        if (window_) SetWindowLongPtrW(window_, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
        loaded_ = CurrentImpl();
        inner_ = CreateInner(loaded_, window_);
    }

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfTextInputProcessor) || IsEqualIID(riid, IID_ITfTextInputProcessorEx)) {
            *ppv = static_cast<ITfTextInputProcessorEx*>(this);
        } else if (IsEqualIID(riid, IID_ITfDisplayAttributeProvider)) {
            *ppv = static_cast<ITfDisplayAttributeProvider*>(this);
        }
        if (*ppv == nullptr) return E_NOINTERFACE;
        AddRef();
        return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        ULONG count = InterlockedDecrement(&refs_);
        if (count == 0) delete this;
        return count;
    }

    // ITfTextInputProcessorEx
    STDMETHODIMP Activate(ITfThreadMgr* threadMgr, TfClientId clientId) override { return ActivateEx(threadMgr, clientId, 0); }
    STDMETHODIMP ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD flags) override {
        if (inner_ == nullptr) {
            // Register-Tip.ps1 が impl を配置している途中に生成された
            loaded_ = CurrentImpl();
            inner_ = CreateInner(loaded_, window_);
        }
        if (inner_ == nullptr) return E_FAIL;
        threadMgr_ = threadMgr;
        threadMgr_->AddRef();
        clientId_ = clientId;
        flags_ = flags;
        return inner_->ActivateEx(threadMgr_, clientId_, flags_);
    }
    STDMETHODIMP Deactivate() override {
        HRESULT hr = inner_ ? inner_->Deactivate() : S_OK;
        if (threadMgr_) {
            threadMgr_->Release();
            threadMgr_ = nullptr;
        }
        return hr;
    }

    // ITfDisplayAttributeProvider
    STDMETHODIMP EnumDisplayAttributeInfo(IEnumTfDisplayAttributeInfo** ppEnum) override {
        ITfDisplayAttributeProvider* provider = Provider();
        if (provider == nullptr) return E_FAIL;
        HRESULT hr = provider->EnumDisplayAttributeInfo(ppEnum);
        provider->Release();
        return hr;
    }
    STDMETHODIMP GetDisplayAttributeInfo(REFGUID guid, ITfDisplayAttributeInfo** ppInfo) override {
        ITfDisplayAttributeProvider* provider = Provider();
        if (provider == nullptr) return E_FAIL;
        HRESULT hr = provider->GetDisplayAttributeInfo(guid, ppInfo);
        provider->Release();
        return hr;
    }

private:
    ~StubService() {
        if (window_) DestroyWindow(window_);
        if (inner_) inner_->Release();
    }

    ITfDisplayAttributeProvider* Provider() {
        ITfDisplayAttributeProvider* provider = nullptr;
        if (inner_) inner_->QueryInterface(IID_ITfDisplayAttributeProvider, reinterpret_cast<void**>(&provider));
        return provider;
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
        auto* self = reinterpret_cast<StubService*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == kStubImplStale && self != nullptr) {
            self->Reload();
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    // 未確定文字列は、前の impl の Deactivate が確定する
    void Reload() {
        // 差し替える impl が無い間 (Meltype.exe だけが別ビルド) は、impl が要求のたびに kStubImplStale を送ってくる
        const ULONGLONG now = GetTickCount64();
        if (now - lastReload_ < 1000) return;
        lastReload_ = now;
        const std::wstring hash = CurrentImpl();
        if (hash.empty() || hash == loaded_ || hash == failed_) return;
        ITfTextInputProcessorEx* next = CreateInner(hash, window_);
        if (next == nullptr) {
            failed_ = hash;
            return;
        }
        TipLog(L"stub: impl を差し替えます: %s → %s", loaded_.c_str(), hash.c_str());
        // 差し替えの途中で TSF が Release しても解放されないようにする
        AddRef();
        if (inner_) {
            if (threadMgr_) inner_->Deactivate();
            inner_->Release();
        }
        inner_ = next;
        loaded_ = hash;
        if (threadMgr_) {
            HRESULT hr = inner_->ActivateEx(threadMgr_, clientId_, flags_);
            if (FAILED(hr)) TipLog(L"stub: 新しい impl を Activate できません: 0x%08lX", hr);
        }
        Release();
    }

    LONG refs_ = 1;
    HWND window_ = nullptr;        // kStubImplStale の送信先
    std::wstring loaded_;          // inner_ を生成した impl の SHA-256
    std::wstring failed_;          // CreateInner が失敗した impl の SHA-256 (ABI が違う impl を、kStubImplStale のたびにロードし直さない)
    ULONGLONG lastReload_ = 0;
    ITfTextInputProcessorEx* inner_ = nullptr;
    ITfThreadMgr* threadMgr_ = nullptr;  // Activate から Deactivate までの間だけ保持する
    TfClientId clientId_ = TF_CLIENTID_NULL;
    DWORD flags_ = 0;
};

}  // namespace

HRESULT CreateStubService(REFIID riid, void** ppv) {
    auto* service = new (std::nothrow) StubService();
    if (service == nullptr) return E_OUTOFMEMORY;
    HRESULT hr = service->QueryInterface(riid, ppv);
    service->Release();
    return hr;
}

}  // namespace meltype
