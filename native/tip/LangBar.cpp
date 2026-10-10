// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai

#include "LangBar.h"

#include "TextService.h"

namespace meltype {
namespace {

constexpr DWORD kSinkCookie = 0x4D454C54;  // 'MELT'

// タスクバーが暗い (ダークモード) か
bool TaskbarIsDark() {
    DWORD value = 0, size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"SystemUsesLightTheme",
                     RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS) {
        return value == 0;
    }
    return true;
}

// 「あ」「A」の文字だけのアイコンを作る (背景は透明、文字はタスクバーの色に合わせて白か黒)
HICON CreateTextIcon(const wchar_t* text) {
    int size = GetSystemMetrics(SM_CXSMICON);
    if (size <= 0) size = 16;
    BITMAPINFO info = {};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = size;
    info.bmiHeader.biHeight = -size;
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HDC screen = GetDC(nullptr);
    HDC dc = CreateCompatibleDC(screen);
    HBITMAP color = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
    ReleaseDC(nullptr, screen);
    if (color == nullptr) {
        DeleteDC(dc);
        return nullptr;
    }
    HGDIOBJ oldBitmap = SelectObject(dc, color);
    // 白で描いて、明るさをそのまま不透明度にする
    memset(bits, 0, static_cast<size_t>(size) * size * 4);
    HFONT font = CreateFontW(-MulDiv(size, 15, 16), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                             ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Yu Gothic UI");
    HGDIOBJ oldFont = SelectObject(dc, font);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(255, 255, 255));
    RECT rect = {0, 0, size, size};
    DrawTextW(dc, text, -1, &rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    GdiFlush();
    BYTE ink = TaskbarIsDark() ? 255 : 0;
    auto* pixels = static_cast<BYTE*>(bits);
    for (int i = 0; i < size * size; i++) {
        BYTE alpha = pixels[i * 4 + 1];  // 緑の明るさ
        // 乗算済みアルファ
        BYTE value = static_cast<BYTE>(ink * alpha / 255);
        pixels[i * 4 + 0] = value;
        pixels[i * 4 + 1] = value;
        pixels[i * 4 + 2] = value;
        pixels[i * 4 + 3] = alpha;
    }
    SelectObject(dc, oldFont);
    DeleteObject(font);
    SelectObject(dc, oldBitmap);
    DeleteDC(dc);

    HBITMAP mask = CreateBitmap(size, size, 1, 1, nullptr);
    ICONINFO iconInfo = {};
    iconInfo.fIcon = TRUE;
    iconInfo.hbmColor = color;
    iconInfo.hbmMask = mask;
    HICON icon = CreateIconIndirect(&iconInfo);
    DeleteObject(color);
    DeleteObject(mask);
    return icon;
}

}  // namespace

LangBarButton::LangBarButton(TextService* service) : service_(service) { DllAddRef(); }

LangBarButton::~LangBarButton() {
    if (sink_ != nullptr) sink_->Release();
    DllRelease();
}

STDMETHODIMP LangBarButton::QueryInterface(REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfLangBarItem) || IsEqualIID(riid, IID_ITfLangBarItemButton)) {
        *ppv = static_cast<ITfLangBarItemButton*>(this);
    } else if (IsEqualIID(riid, IID_ITfSource)) {
        *ppv = static_cast<ITfSource*>(this);
    }
    if (*ppv == nullptr) return E_NOINTERFACE;
    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) LangBarButton::AddRef() { return InterlockedIncrement(&refs_); }

STDMETHODIMP_(ULONG) LangBarButton::Release() {
    ULONG count = InterlockedDecrement(&refs_);
    if (count == 0) delete this;
    return count;
}

STDMETHODIMP LangBarButton::GetInfo(TF_LANGBARITEMINFO* info) {
    if (info == nullptr) return E_INVALIDARG;
    info->clsidService = CLSID_TextService;
    // Windows 8 以降のタスクバーの入力モードの表示は GUID_LBI_INPUTMODE の項目を見る
    info->guidItem = GUID_LBI_INPUTMODE;
    info->dwStyle = TF_LBI_STYLE_BTN_BUTTON | TF_LBI_STYLE_SHOWNINTRAY;
    info->ulSort = 0;
    wcscpy_s(info->szDescription, L"Meltype の入力モード");
    return S_OK;
}

STDMETHODIMP LangBarButton::GetStatus(DWORD* status) {
    if (status == nullptr) return E_INVALIDARG;
    *status = 0;
    return S_OK;
}

STDMETHODIMP LangBarButton::GetTooltipString(BSTR* tooltip) {
    if (tooltip == nullptr) return E_INVALIDARG;
    bool open = service_ == nullptr || service_->IsOpen();
    *tooltip = SysAllocString(open ? L"Meltype: 日本語 (クリックで英数)" : L"Meltype: 英数 (クリックで日本語)");
    return *tooltip ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP LangBarButton::OnClick(TfLBIClick, POINT, const RECT*) {
    if (service_ != nullptr) service_->ToggleInputMode(nullptr);
    return S_OK;
}

STDMETHODIMP LangBarButton::GetIcon(HICON* icon) {
    if (icon == nullptr) return E_INVALIDARG;
    *icon = CreateTextIcon(service_ == nullptr || service_->IsOpen() ? L"あ" : L"A");
    return *icon ? S_OK : E_FAIL;
}

STDMETHODIMP LangBarButton::GetText(BSTR* text) {
    if (text == nullptr) return E_INVALIDARG;
    *text = SysAllocString(service_ == nullptr || service_->IsOpen() ? L"あ" : L"A");
    return *text ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP LangBarButton::AdviseSink(REFIID riid, IUnknown* sink, DWORD* cookie) {
    if (!IsEqualIID(riid, IID_ITfLangBarItemSink)) return CONNECT_E_CANNOTCONNECT;
    if (sink_ != nullptr) return CONNECT_E_ADVISELIMIT;
    if (sink == nullptr || cookie == nullptr) return E_INVALIDARG;
    if (FAILED(sink->QueryInterface(IID_ITfLangBarItemSink, reinterpret_cast<void**>(&sink_)))) {
        sink_ = nullptr;
        return E_NOINTERFACE;
    }
    *cookie = kSinkCookie;
    return S_OK;
}

STDMETHODIMP LangBarButton::UnadviseSink(DWORD cookie) {
    if (cookie != kSinkCookie || sink_ == nullptr) return CONNECT_E_NOCONNECTION;
    sink_->Release();
    sink_ = nullptr;
    return S_OK;
}

void LangBarButton::Update() {
    if (sink_ != nullptr) sink_->OnUpdate(TF_LBI_ICON | TF_LBI_TEXT | TF_LBI_TOOLTIP);
}

}  // namespace meltype
