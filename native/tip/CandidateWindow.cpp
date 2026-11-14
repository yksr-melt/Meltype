// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// 候補の一覧。Windows 11 の Microsoft IME に寄せた見た目:
//   角丸 (DWM)、ライト / ダークは Windows の設定に合わせる、縦 1 列で番号 + 候補、
//   選んでいる行は背景を薄く塗って左端にアクセントの縦線、9 個ずつのページ。
// 打ったキーと「もしかして」は一覧の上に、候補の意味は少し止まったら一覧の右に出す。

#include "CandidateWindow.h"

#include <d2d1_1.h>
#include <dwmapi.h>
#include <dwrite.h>

#include <algorithm>
#include <mutex>
#include <cmath>

namespace meltype {
namespace {

constexpr wchar_t kClassName[] = L"MeltypeCandidateWindow";
constexpr wchar_t kMeaningClassName[] = L"MeltypeMeaningWindow";
constexpr int kPageSize = 9;
constexpr UINT_PTR kMeaningTimer = 1;

// 大きさ (DIP。96 dpi のときのピクセル)
constexpr float kPadding = 4;        // 窓の縁から行まで
constexpr float kRowHeight = 32;     // 候補の 1 行
constexpr float kNumberWidth = 26;   // 番号の列
constexpr float kTextInset = 10;     // 行の左端から番号まで
constexpr float kNoteGap = 16;       // 候補と注釈の間
constexpr float kFooterHeight = 24;  // 「3 / 27」の行
constexpr float kTypedHeight = 22;   // 打ったキーの行
constexpr float kCandidateFontSize = 15;
constexpr float kSmallFontSize = 12;
constexpr float kMinWidth = 168;
constexpr float kMaxWidth = 520;
constexpr float kMeaningMaxWidth = 320;

// 1 つのアプリの中で、いくつもの UI スレッドが候補の一覧を出すことがある (エクスプローラーなど)。
// ファクトリーはマルチスレッド用を 1 つだけ生成する (impl はアンロードしないので、解放しない)
ID2D1Factory* g_d2d = nullptr;
IDWriteFactory* g_dwrite = nullptr;
std::mutex g_factoryMutex;
std::once_flag g_classOnce;
bool g_registered = false;

bool EnsureFactories() {
    std::lock_guard<std::mutex> lock(g_factoryMutex);
    if (g_d2d == nullptr) {
        D2D1_FACTORY_OPTIONS options = {};
        D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED, __uuidof(ID2D1Factory), &options, reinterpret_cast<void**>(&g_d2d));
    }
    if (g_dwrite == nullptr) {
        DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(&g_dwrite));
    }
    return g_d2d != nullptr && g_dwrite != nullptr;
}

struct Palette {
    D2D1_COLOR_F background;
    D2D1_COLOR_F text;
    D2D1_COLOR_F secondary;
    D2D1_COLOR_F selectedFill;
    D2D1_COLOR_F hoverFill;
    D2D1_COLOR_F accent;
    D2D1_COLOR_F separator;
    D2D1_COLOR_F suggestionFill;
    COLORREF border;
};

D2D1_COLOR_F Rgba(int r, int g, int b, float a = 1.0f) { return D2D1::ColorF(r / 255.0f, g / 255.0f, b / 255.0f, a); }

bool AppsUseDarkTheme() {
    DWORD value = 1, size = sizeof(value);
    RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"AppsUseLightTheme", RRF_RT_REG_DWORD, nullptr,
                 &value, &size);
    return value == 0;
}

// Windows のアクセントの色 (無ければ Windows の既定の青)
COLORREF AccentColor() {
    DWORD value = 0, size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\DWM", L"AccentColor", RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS) {
        // AABBGGRR
        return RGB(value & 0xFF, (value >> 8) & 0xFF, (value >> 16) & 0xFF);
    }
    return RGB(0, 120, 212);
}

Palette CurrentPalette() {
    COLORREF accent = AccentColor();
    int r = GetRValue(accent), g = GetGValue(accent), b = GetBValue(accent);
    Palette p;
    if (AppsUseDarkTheme()) {
        // ダークでは、アクセントの色を少し明るくして読みやすくする
        auto lighten = [](int c) { return c + (255 - c) * 2 / 5; };
        p.background = Rgba(44, 44, 44);
        p.text = Rgba(255, 255, 255);
        p.secondary = Rgba(255, 255, 255, 0.62f);
        p.selectedFill = Rgba(255, 255, 255, 0.08f);
        p.hoverFill = Rgba(255, 255, 255, 0.05f);
        p.accent = Rgba(lighten(r), lighten(g), lighten(b));
        p.separator = Rgba(255, 255, 255, 0.08f);
        p.suggestionFill = Rgba(lighten(r), lighten(g), lighten(b), 0.12f);
        p.border = RGB(70, 70, 70);
    } else {
        p.background = Rgba(249, 249, 249);
        p.text = Rgba(26, 26, 26);
        p.secondary = Rgba(0, 0, 0, 0.6f);
        p.selectedFill = Rgba(0, 0, 0, 0.06f);
        p.hoverFill = Rgba(0, 0, 0, 0.035f);
        p.accent = Rgba(r, g, b);
        p.separator = Rgba(0, 0, 0, 0.08f);
        p.suggestionFill = Rgba(r, g, b, 0.08f);
        p.border = RGB(222, 222, 222);
    }
    return p;
}

IDWriteTextFormat* CreateFormat(float size, DWRITE_FONT_WEIGHT weight = DWRITE_FONT_WEIGHT_NORMAL) {
    IDWriteTextFormat* format = nullptr;
    g_dwrite->CreateTextFormat(L"Yu Gothic UI", nullptr, weight, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, size, L"ja-jp", &format);
    if (format != nullptr) {
        format->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
    }
    return format;
}

float TextWidth(IDWriteTextFormat* format, const std::wstring& text) {
    if (format == nullptr || text.empty()) return 0;
    IDWriteTextLayout* layout = nullptr;
    if (FAILED(g_dwrite->CreateTextLayout(text.c_str(), static_cast<UINT32>(text.size()), format, 4096, 100, &layout))) return 0;
    DWRITE_TEXT_METRICS metrics = {};
    layout->GetMetrics(&metrics);
    layout->Release();
    return metrics.widthIncludingTrailingWhitespace;
}

void DrawText(ID2D1RenderTarget* target, IDWriteTextFormat* format, const std::wstring& text, const D2D1_RECT_F& rect, const D2D1_COLOR_F& color,
              bool wrap = false) {
    if (format == nullptr || text.empty()) return;
    IDWriteTextLayout* layout = nullptr;
    if (FAILED(g_dwrite->CreateTextLayout(text.c_str(), static_cast<UINT32>(text.size()), format, rect.right - rect.left, rect.bottom - rect.top, &layout))) {
        return;
    }
    if (wrap) layout->SetWordWrapping(DWRITE_WORD_WRAPPING_WRAP);
    ID2D1SolidColorBrush* brush = nullptr;
    target->CreateSolidColorBrush(color, &brush);
    if (brush != nullptr) {
        // 絵文字をカラーで描く
        target->DrawTextLayout(D2D1::Point2F(rect.left, rect.top), layout, brush, D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT | D2D1_DRAW_TEXT_OPTIONS_CLIP);
        brush->Release();
    }
    layout->Release();
}

void FillRounded(ID2D1RenderTarget* target, const D2D1_RECT_F& rect, float radius, const D2D1_COLOR_F& color) {
    ID2D1SolidColorBrush* brush = nullptr;
    target->CreateSolidColorBrush(color, &brush);
    if (brush == nullptr) return;
    target->FillRoundedRectangle(D2D1::RoundedRect(rect, radius, radius), brush);
    brush->Release();
}

void PrepareWindow(HWND hwnd, COLORREF border) {
    // Windows 11: 角丸と縁の色 (Windows 10 では何もしない)
    DWORD corner = 2;  // DWMWCP_ROUND
    DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, &corner, sizeof(corner));
    DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, &border, sizeof(border));
}

// 画面からはみ出さない位置にする
void PlaceWithin(int& x, int& y, int width, int height, const RECT& anchor) {
    HMONITOR monitor = MonitorFromRect(&anchor, MONITOR_DEFAULTTONEAREST);
    MONITORINFO info = {sizeof(info)};
    GetMonitorInfoW(monitor, &info);
    const RECT& area = info.rcWork;
    if (y + height > area.bottom) y = anchor.top - height - 4;  // 下に入らなければ上
    if (y < area.top) y = area.top;
    if (x + width > area.right) x = area.right - width;
    if (x < area.left) x = area.left;
}

}  // namespace

struct CandidateWindow::Impl {
    SelectHandler onSelect;
    HWND hwnd = nullptr;
    HWND meaningHwnd = nullptr;
    ID2D1HwndRenderTarget* target = nullptr;
    ID2D1HwndRenderTarget* meaningTarget = nullptr;
    CandidateView view;
    RECT anchor = {};
    int first = 0;
    int hover = -1;
    float scale = 1;
    float width = kMinWidth;  // DIP
    std::wstring meaningKey;

    float SuggestionHeight() const { return view.suggestion.empty() ? 0 : kRowHeight + (view.candidates.empty() ? 0 : 5); }
    // 候補の行より上 (打ったキー・もしかして) の高さ
    float HeaderHeight() const { return (view.typed.empty() ? 0 : kTypedHeight) + SuggestionHeight(); }
    int Rows() const { return std::min(static_cast<int>(view.candidates.size()) - first, kPageSize); }
    bool Paged() const { return view.candidates.size() > kPageSize; }

    float Height() const {
        float height = kPadding * 2 + HeaderHeight();
        // ページを送っても大きさが変わらないように、2 ページ以上あるときは 9 行分
        int rows = Paged() ? kPageSize : static_cast<int>(view.candidates.size());
        height += rows * kRowHeight;
        if (Paged()) height += kFooterHeight;
        if (!view.hint.empty()) height += kTypedHeight;
        return height;
    }

    float Measure() {
        IDWriteTextFormat* candidate = CreateFormat(kCandidateFontSize);
        IDWriteTextFormat* note = CreateFormat(kSmallFontSize);
        float widest = 0;
        for (size_t i = 0; i < view.candidates.size(); i++) {
            float w = TextWidth(candidate, view.candidates[i]);
            if (i < view.notes.size() && !view.notes[i].empty()) w += kNoteGap + TextWidth(note, view.notes[i]);
            widest = std::max(widest, w);
        }
        float result = kPadding * 2 + kTextInset + kNumberWidth + widest + 16;
        if (!view.suggestion.empty()) result = std::max(result, kPadding * 2 + 12 + TextWidth(candidate, view.suggestion) + 16);
        if (!view.typed.empty()) result = std::max(result, kPadding * 2 + 12 + TextWidth(note, view.typed) + 16);
        if (!view.hint.empty()) result = std::max(result, kPadding * 2 + 12 + TextWidth(note, view.hint) + 16);
        if (candidate) candidate->Release();
        if (note) note->Release();
        return std::clamp(result, kMinWidth, kMaxWidth);
    }

    // DIP の y から、候補の番号 (無ければ -1)
    int HitTest(float y) const {
        float top = kPadding + HeaderHeight();
        if (y < top) return -1;
        int row = static_cast<int>((y - top) / kRowHeight);
        if (row < 0 || row >= Rows()) return -1;
        return first + row;
    }

    void Paint() {
        if (target == nullptr) {
            RECT client;
            GetClientRect(hwnd, &client);
            D2D1_RENDER_TARGET_PROPERTIES props = D2D1::RenderTargetProperties();
            props.dpiX = props.dpiY = 96.0f * scale;
            if (FAILED(g_d2d->CreateHwndRenderTarget(props, D2D1::HwndRenderTargetProperties(hwnd, D2D1::SizeU(client.right, client.bottom)), &target))) return;
            target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        }
        Palette p = CurrentPalette();
        IDWriteTextFormat* candidate = CreateFormat(kCandidateFontSize);
        IDWriteTextFormat* note = CreateFormat(kSmallFontSize);
        target->BeginDraw();
        target->Clear(p.background);

        float y = kPadding;
        if (!view.typed.empty()) {
            // 打ったキー (設定「打ったキーを表示」): 打ち間違いに気づけるように、小さく出す
            DrawText(target, note, view.typed, D2D1::RectF(kPadding + 12, y, width - kPadding - 8, y + kTypedHeight), p.secondary);
            y += kTypedHeight;
        }
        if (!view.suggestion.empty()) {
            // もしかして: アクセントの色で薄く塗った行
            D2D1_RECT_F row = D2D1::RectF(kPadding, y, width - kPadding, y + kRowHeight);
            FillRounded(target, row, 4, p.suggestionFill);
            DrawText(target, candidate, view.suggestion, D2D1::RectF(row.left + 12, row.top, row.right - 8, row.bottom), p.accent);
            y += kRowHeight;
            if (!view.candidates.empty()) {
                ID2D1SolidColorBrush* line = nullptr;
                target->CreateSolidColorBrush(p.separator, &line);
                if (line) {
                    target->DrawLine(D2D1::Point2F(kPadding + 4, y + 2.5f), D2D1::Point2F(width - kPadding - 4, y + 2.5f), line, 1.0f);
                    line->Release();
                }
                y += 5;
            }
        }

        for (int i = 0; i < Rows(); i++) {
            int index = first + i;
            D2D1_RECT_F row = D2D1::RectF(kPadding, y, width - kPadding, y + kRowHeight);
            bool selected = index == view.selected;
            if (selected) {
                FillRounded(target, row, 4, p.selectedFill);
                // 左端のアクセントの縦線 (Windows 11 のリストの選択と同じ形)
                float barHeight = 16;
                FillRounded(target, D2D1::RectF(row.left, row.top + (kRowHeight - barHeight) / 2, row.left + 3, row.top + (kRowHeight + barHeight) / 2), 1.5f,
                            p.accent);
            } else if (index == hover) {
                FillRounded(target, row, 4, p.hoverFill);
            }
            if (view.numbered) {
                wchar_t number[4];
                swprintf_s(number, L"%d", i + 1);
                DrawText(target, note, number, D2D1::RectF(row.left + kTextInset, row.top, row.left + kTextInset + kNumberWidth, row.bottom), p.secondary);
            }
            float noteWidth = 0;
            if (index < static_cast<int>(view.notes.size()) && !view.notes[index].empty()) {
                noteWidth = TextWidth(note, view.notes[index]);
                DrawText(target, note, view.notes[index], D2D1::RectF(row.right - 10 - noteWidth, row.top, row.right - 6, row.bottom), p.secondary);
                noteWidth += kNoteGap;
            }
            DrawText(target, candidate, view.candidates[index],
                     D2D1::RectF(row.left + kTextInset + kNumberWidth, row.top, row.right - 8 - noteWidth, row.bottom), p.text);
            y += kRowHeight;
        }

        if (Paged()) {
            y = kPadding + HeaderHeight() + kPageSize * kRowHeight;
            wchar_t page[32];
            swprintf_s(page, L"%d / %d", std::max(0, view.selected) + 1, static_cast<int>(view.candidates.size()));
            float pageWidth = TextWidth(note, page);
            DrawText(target, note, page, D2D1::RectF(width - kPadding - 12 - pageWidth, y, width - kPadding - 8, y + kFooterHeight), p.secondary);
            y += kFooterHeight;
        }
        if (!view.hint.empty()) DrawText(target, note, view.hint, D2D1::RectF(kPadding + 12, y, width - kPadding - 8, y + kTypedHeight), p.secondary);

        HRESULT hr = target->EndDraw();
        if (hr == D2DERR_RECREATE_TARGET) {
            target->Release();
            target = nullptr;
        }
        if (candidate) candidate->Release();
        if (note) note->Release();
    }

    // ---- 意味 ----

    void ScheduleMeaning() {
        std::wstring key = view.meaning.empty() || view.selected < 0 ? L"" : std::to_wstring(view.selected) + L"\n" + view.meaning;
        if (key == meaningKey) {
            if (meaningHwnd != nullptr && IsWindowVisible(meaningHwnd)) PlaceMeaning();
            return;
        }
        meaningKey = key;
        KillTimer(hwnd, kMeaningTimer);
        if (meaningHwnd != nullptr) ShowWindow(meaningHwnd, SW_HIDE);
        if (!key.empty()) SetTimer(hwnd, kMeaningTimer, view.meaningMs, nullptr);
    }

    float MeaningSize(float& height) {
        IDWriteTextFormat* format = CreateFormat(kSmallFontSize + 1);
        if (format == nullptr) return 0;
        format->SetWordWrapping(DWRITE_WORD_WRAPPING_WRAP);
        format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_NEAR);
        IDWriteTextLayout* layout = nullptr;
        float textWidth = 0;
        height = 0;
        if (SUCCEEDED(g_dwrite->CreateTextLayout(view.meaning.c_str(), static_cast<UINT32>(view.meaning.size()), format, kMeaningMaxWidth, 2000, &layout))) {
            DWRITE_TEXT_METRICS metrics = {};
            layout->GetMetrics(&metrics);
            textWidth = metrics.widthIncludingTrailingWhitespace;
            height = metrics.height;
            layout->Release();
        }
        format->Release();
        height += 20;
        return textWidth + 24;
    }

    void ShowMeaning() {
        KillTimer(hwnd, kMeaningTimer);
        if (view.meaning.empty() || !IsWindowVisible(hwnd)) return;
        if (meaningHwnd == nullptr) {
            meaningHwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, kMeaningClassName, L"", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr,
                                          g_module, this);
            if (meaningHwnd == nullptr) return;
            PrepareWindow(meaningHwnd, CurrentPalette().border);
        }
        PlaceMeaning();
        ShowWindow(meaningHwnd, SW_SHOWNOACTIVATE);
        InvalidateRect(meaningHwnd, nullptr, FALSE);
    }

    void PlaceMeaning() {
        float height = 0;
        float w = MeaningSize(height);
        // 切り捨てると幅が足りずに折り返し、最後の行が切れるので切り上げる
        int pw = static_cast<int>(ceilf((w + 2) * scale)), ph = static_cast<int>(ceilf((height + 2) * scale));
        RECT own;
        GetWindowRect(hwnd, &own);
        int row = std::max(0, view.selected - first);
        int x = own.right + static_cast<int>(4 * scale);
        int y = own.top + static_cast<int>((kPadding + HeaderHeight() + row * kRowHeight) * scale);
        HMONITOR monitor = MonitorFromRect(&own, MONITOR_DEFAULTTONEAREST);
        MONITORINFO info = {sizeof(info)};
        GetMonitorInfoW(monitor, &info);
        if (x + pw > info.rcWork.right) x = own.left - pw - static_cast<int>(4 * scale);
        y = std::clamp(y, static_cast<int>(info.rcWork.top), std::max(static_cast<int>(info.rcWork.top), static_cast<int>(info.rcWork.bottom) - ph));
        if (meaningTarget != nullptr) {
            meaningTarget->Release();
            meaningTarget = nullptr;
        }
        SetWindowPos(meaningHwnd, HWND_TOPMOST, x, y, pw, ph, SWP_NOACTIVATE);
    }

    void PaintMeaning() {
        if (meaningTarget == nullptr) {
            RECT client;
            GetClientRect(meaningHwnd, &client);
            D2D1_RENDER_TARGET_PROPERTIES props = D2D1::RenderTargetProperties();
            props.dpiX = props.dpiY = 96.0f * scale;
            if (FAILED(g_d2d->CreateHwndRenderTarget(props, D2D1::HwndRenderTargetProperties(meaningHwnd, D2D1::SizeU(client.right, client.bottom)),
                                                     &meaningTarget))) {
                return;
            }
            meaningTarget->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        }
        Palette p = CurrentPalette();
        IDWriteTextFormat* format = CreateFormat(kSmallFontSize + 1);
        if (format != nullptr) {
            format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_NEAR);
        }
        RECT client;
        GetClientRect(meaningHwnd, &client);
        meaningTarget->BeginDraw();
        meaningTarget->Clear(p.background);
        DrawText(meaningTarget, format, view.meaning, D2D1::RectF(12, 10, client.right / scale - 12, client.bottom / scale - 10), p.text, true);
        if (meaningTarget->EndDraw() == D2DERR_RECREATE_TARGET) {
            meaningTarget->Release();
            meaningTarget = nullptr;
        }
        if (format) format->Release();
    }

    void HideAll() {
        if (hwnd != nullptr) {
            KillTimer(hwnd, kMeaningTimer);
            ShowWindow(hwnd, SW_HIDE);
        }
        if (meaningHwnd != nullptr) ShowWindow(meaningHwnd, SW_HIDE);
        meaningKey.clear();
        hover = -1;
    }

    static LRESULT CALLBACK WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
        if (message == WM_NCCREATE) {
            auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(create->lpCreateParams));
        }
        auto* self = reinterpret_cast<Impl*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (self == nullptr) return DefWindowProcW(hwnd, message, wParam, lParam);
        switch (message) {
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;  // クリックしても入力欄からフォーカスを奪わない
            case WM_PAINT: {
                PAINTSTRUCT ps;
                BeginPaint(hwnd, &ps);
                self->Paint();
                EndPaint(hwnd, &ps);
                return 0;
            }
            case WM_ERASEBKGND:
                return 1;
            case WM_MOUSEMOVE: {
                int hit = self->HitTest(GET_Y_LPARAM_SAFE(lParam) / self->scale);
                if (hit != self->hover) {
                    self->hover = hit;
                    InvalidateRect(hwnd, nullptr, FALSE);
                    TRACKMOUSEEVENT track = {sizeof(track), TME_LEAVE, hwnd, 0};
                    TrackMouseEvent(&track);
                }
                return 0;
            }
            case WM_MOUSELEAVE:
                self->hover = -1;
                InvalidateRect(hwnd, nullptr, FALSE);
                return 0;
            case WM_LBUTTONUP: {
                int hit = self->HitTest(GET_Y_LPARAM_SAFE(lParam) / self->scale);
                if (hit >= 0 && self->onSelect) self->onSelect(hit);
                return 0;
            }
            case WM_TIMER:
                if (wParam == kMeaningTimer) self->ShowMeaning();
                return 0;
            case WM_DPICHANGED:
                return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    static LRESULT CALLBACK MeaningProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
        if (message == WM_NCCREATE) {
            auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(create->lpCreateParams));
        }
        auto* self = reinterpret_cast<Impl*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (self == nullptr) return DefWindowProcW(hwnd, message, wParam, lParam);
        switch (message) {
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_PAINT: {
                PAINTSTRUCT ps;
                BeginPaint(hwnd, &ps);
                self->PaintMeaning();
                EndPaint(hwnd, &ps);
                return 0;
            }
            case WM_ERASEBKGND:
                return 1;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    static int GET_Y_LPARAM_SAFE(LPARAM lParam) { return static_cast<int>(static_cast<short>(HIWORD(lParam))); }
};

static bool EnsureClasses() {
    std::call_once(g_classOnce, [] {
    WNDCLASSEXW wc = {sizeof(wc)};
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = CandidateWindow::Impl::WndProc;
    wc.hInstance = g_module;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.lpszClassName = kClassName;
    if (!RegisterClassExW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return;
    wc.lpfnWndProc = CandidateWindow::Impl::MeaningProc;
    wc.lpszClassName = kMeaningClassName;
    if (!RegisterClassExW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return;
    g_registered = true;
    });
    return g_registered;
}

CandidateWindow::CandidateWindow(SelectHandler onSelect) : impl_(new Impl()) { impl_->onSelect = std::move(onSelect); }

CandidateWindow::~CandidateWindow() {
    if (impl_->target) impl_->target->Release();
    if (impl_->meaningTarget) impl_->meaningTarget->Release();
    if (impl_->meaningHwnd) DestroyWindow(impl_->meaningHwnd);
    if (impl_->hwnd) DestroyWindow(impl_->hwnd);
    delete impl_;
}

bool CandidateWindow::Visible() const { return impl_->hwnd != nullptr && IsWindowVisible(impl_->hwnd); }

void CandidateWindow::Hide() { impl_->HideAll(); }

void CandidateWindow::Show(const CandidateView& view, const RECT& anchor) {
    if (!EnsureFactories() || !EnsureClasses()) return;
    Impl& d = *impl_;
    if (d.hwnd == nullptr) {
        d.hwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, kClassName, L"", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, g_module, &d);
        if (d.hwnd == nullptr) return;
        PrepareWindow(d.hwnd, CurrentPalette().border);
    }
    float scale = GetDpiForWindow(d.hwnd) / 96.0f;
    if (scale <= 0) scale = 1;
    // DPI が変わったときも、描画 (render target) と当たり判定の scale がずれないように置き直す
    bool samePlace = Visible() && EqualRect(&d.anchor, &anchor) && d.view.candidates == view.candidates && d.view.suggestion == view.suggestion &&
                     d.view.numbered == view.numbered && d.view.typed == view.typed && d.view.hint == view.hint && scale == d.scale;
    d.view = view;
    d.anchor = anchor;
    d.first = std::max(0, view.selected) / kPageSize * kPageSize;
    d.scale = scale;

    if (!samePlace) {
        d.width = d.Measure();
        if (d.target != nullptr) {
            d.target->Release();
            d.target = nullptr;
        }
        // DPI の違うモニターへ移すと、ウィンドウの DPI も変わる。変わったら、その DPI で大きさを決め直す
        for (int i = 0; i < 2; i++) {
            int width = static_cast<int>(d.width * d.scale + 0.5f);
            int height = static_cast<int>(d.Height() * d.scale + 0.5f);
            // 候補の文字の左端を、選んでいる文節の左端にそろえる
            // 「もしかして」・打ったキーだけのときは、その文字の左端をそろえる
            float inset = view.candidates.empty() ? kPadding + 12 : kPadding + kTextInset + kNumberWidth;
            int x = anchor.left - static_cast<int>(inset * d.scale);
            int y = anchor.bottom + static_cast<int>(4 * d.scale);
            PlaceWithin(x, y, width, height, anchor);
            SetWindowPos(d.hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            float moved = GetDpiForWindow(d.hwnd) / 96.0f;
            if (moved <= 0 || moved == d.scale) break;
            d.scale = moved;
        }
    }
    InvalidateRect(d.hwnd, nullptr, FALSE);
    d.ScheduleMeaning();
}

}  // namespace meltype
