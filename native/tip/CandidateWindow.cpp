// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// 候補の一覧。Windows 11 の Microsoft IME に寄せた見た目:
//   角丸 (DWM)、ライト / ダークは Windows の設定に合わせる、縦 1 列で番号 + 候補、
//   選んでいる行は背景を薄く塗って左端にアクセントの縦線、9 個ずつのページ。
// 打ったキーと「もしかして」は一覧の上に、候補の意味は少し止まったら一覧の右に出す。
//
// 候補の一覧は、TSF の UI Element (ITfCandidateListUIElement) として ITfUIElementMgr にも知らせる。
// 候補を自分で描くアプリ (UILess。ゲームなど) は、GUID_TFCAT_TIPCAP_UIELEMENTENABLED の IME だけを有効にし (Dll.cpp)、
// BeginUIElement で窓を出さないように答えて、候補をこの UI Element から読む。そのときは窓を出さない。

#include "CandidateWindow.h"

#include <d2d1_1.h>
#include <dwmapi.h>
#include <dwrite.h>
#include <wrl/client.h>

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
constexpr float kSwatchSize = 14;    // 色見本の大きさ
constexpr float kSwatchGap = 8;      // 候補と色見本の間
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

void DrawColorSwatch(ID2D1RenderTarget* target, const D2D1_RECT_F& rect, unsigned char r, unsigned char g, unsigned char b, unsigned char a,
                     const D2D1_COLOR_F& borderColor) {
    if (a < 255) {
        float midX = (rect.left + rect.right) * 0.5f;
        float midY = (rect.top + rect.bottom) * 0.5f;
        ID2D1SolidColorBrush* light = nullptr;
        ID2D1SolidColorBrush* dark = nullptr;
        target->CreateSolidColorBrush(Rgba(255, 255, 255), &light);
        target->CreateSolidColorBrush(Rgba(204, 204, 204), &dark);
        if (light && dark) {
            target->FillRectangle(D2D1::RectF(rect.left, rect.top, midX, midY), light);
            target->FillRectangle(D2D1::RectF(midX, rect.top, rect.right, midY), dark);
            target->FillRectangle(D2D1::RectF(rect.left, midY, midX, rect.bottom), dark);
            target->FillRectangle(D2D1::RectF(midX, midY, rect.right, rect.bottom), light);
        }
        if (light) light->Release();
        if (dark) dark->Release();
    }
    ID2D1SolidColorBrush* fill = nullptr;
    target->CreateSolidColorBrush(Rgba(r, g, b, a / 255.0f), &fill);
    if (fill) {
        target->FillRectangle(rect, fill);
        fill->Release();
    }
    ID2D1SolidColorBrush* border = nullptr;
    target->CreateSolidColorBrush(borderColor, &border);
    if (border) {
        target->DrawRectangle(rect, border, 1.0f);
        border->Release();
    }
}

int HexDigit(wchar_t c) {
    if (c >= L'0' && c <= L'9') return c - L'0';
    if (c >= L'a' && c <= L'f') return c - L'a' + 10;
    if (c >= L'A' && c <= L'F') return c - L'A' + 10;
    return -1;
}

// Core が決めた色見本 (#rrggbbaa) を RGBA に展開する
bool DecodeRgbaHex(const std::wstring& hex, unsigned char& outR, unsigned char& outG, unsigned char& outB, unsigned char& outA) {
    if (hex.size() != 9 || hex[0] != L'#') return false;
    int d[8] = {};
    for (size_t i = 0; i < 8; i++) {
        d[i] = HexDigit(hex[1 + i]);
        if (d[i] < 0) return false;
    }
    outR = static_cast<unsigned char>((d[0] << 4) | d[1]);
    outG = static_cast<unsigned char>((d[2] << 4) | d[3]);
    outB = static_cast<unsigned char>((d[4] << 4) | d[5]);
    outA = static_cast<unsigned char>((d[6] << 4) | d[7]);
    return true;
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

class CandidateElement;
// 窓に出す (大きさと位置を決め直す)
static void Render(CandidateWindow::Impl& d, const CandidateView& view, const RECT& anchor);

struct CandidateWindow::Impl {
    SelectHandler onSelect;
    Microsoft::WRL::ComPtr<ITfThreadMgr> threadMgr;
    Microsoft::WRL::ComPtr<ITfUIElementMgr> elements;  // 取れなければ (古い Windows) 窓だけで出す
    CandidateElement* element = nullptr;
    DWORD elementId = TF_INVALID_UIELEMENTID;  // 出している UI Element (BeginUIElement から EndUIElement まで)
    bool shown = true;  // 窓を出すか (アプリが自分で描くなら false)
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
            unsigned char cr = 0, cg = 0, cb = 0, ca = 0;
            if (i < view.colors.size() && DecodeRgbaHex(view.colors[i], cr, cg, cb, ca)) w += kSwatchGap + kSwatchSize;
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
            float textLeft = row.left + kTextInset + kNumberWidth;
            float maxRight = row.right - 8 - noteWidth;
            unsigned char cr = 0, cg = 0, cb = 0, ca = 0;
            if (index < static_cast<int>(view.colors.size()) && DecodeRgbaHex(view.colors[index], cr, cg, cb, ca)) {
                float candWidth = TextWidth(candidate, view.candidates[index]);
                float swatchX = std::min(textLeft + candWidth + kSwatchGap, maxRight - kSwatchSize);
                float swatchY = row.top + (kRowHeight - kSwatchSize) * 0.5f;
                DrawColorSwatch(target, D2D1::RectF(swatchX, swatchY, swatchX + kSwatchSize, swatchY + kSwatchSize), cr, cg, cb, ca, p.secondary);
                maxRight = swatchX - kSwatchGap;
            }
            DrawText(target, candidate, view.candidates[index], D2D1::RectF(textLeft, row.top, maxRight, row.bottom), p.text);
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

    // アプリが UI Element の表示を切り替えた (ITfUIElement::Show)
    void SetShown(bool show) {
        shown = show;
        if (!show) HideAll();
        else if (elementId != TF_INVALID_UIELEMENTID) Render(*this, CandidateView(view), RECT(anchor));
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

// 候補の一覧の UI Element。アプリのスレッド (TSF のスレッド) だけで使う。
// 中身は CandidateWindow が持つ今の候補をそのまま読む (写しを持たない)。CandidateWindow が先に消えたら owner は null
class CandidateElement final : public ITfCandidateListUIElement {
public:
    explicit CandidateElement(CandidateWindow::Impl* owner) : owner_(owner) {}
    void Detach() { owner_ = nullptr; }

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfUIElement) || IsEqualIID(riid, IID_ITfCandidateListUIElement)) {
            *ppv = static_cast<ITfCandidateListUIElement*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        ULONG refs = InterlockedDecrement(&refs_);
        if (refs == 0) delete this;
        return refs;
    }

    // ITfUIElement
    STDMETHODIMP GetDescription(BSTR* description) override {
        if (description == nullptr) return E_INVALIDARG;
        *description = SysAllocString(L"Meltype の候補");
        return *description != nullptr ? S_OK : E_OUTOFMEMORY;
    }
    STDMETHODIMP GetGUID(GUID* guid) override {
        if (guid == nullptr) return E_INVALIDARG;
        // {0EBEF5FC-6C4D-49E1-AC64-9F16D8014A47}
        *guid = {0x0ebef5fc, 0x6c4d, 0x49e1, {0xac, 0x64, 0x9f, 0x16, 0xd8, 0x01, 0x4a, 0x47}};
        return S_OK;
    }
    STDMETHODIMP Show(BOOL show) override {
        if (owner_ != nullptr) owner_->SetShown(show != FALSE);
        return S_OK;
    }
    STDMETHODIMP IsShown(BOOL* shown) override {
        if (shown == nullptr) return E_INVALIDARG;
        *shown = owner_ != nullptr && owner_->shown ? TRUE : FALSE;
        return S_OK;
    }

    // ITfCandidateListUIElement
    STDMETHODIMP GetUpdatedFlags(DWORD* flags) override {
        if (flags == nullptr) return E_INVALIDARG;
        // 変わったところを覚えずに、毎回すべて読み直してもらう
        *flags = TF_CLUIE_DOCUMENTMGR | TF_CLUIE_COUNT | TF_CLUIE_SELECTION | TF_CLUIE_STRING | TF_CLUIE_PAGEINDEX | TF_CLUIE_CURRENTPAGE;
        return S_OK;
    }
    STDMETHODIMP GetDocumentMgr(ITfDocumentMgr** documentMgr) override {
        if (documentMgr == nullptr) return E_INVALIDARG;
        *documentMgr = nullptr;
        if (owner_ == nullptr || owner_->threadMgr == nullptr) return E_FAIL;
        return owner_->threadMgr->GetFocus(documentMgr);
    }
    STDMETHODIMP GetCount(UINT* count) override {
        if (count == nullptr) return E_INVALIDARG;
        *count = Count();
        return S_OK;
    }
    STDMETHODIMP GetSelection(UINT* index) override {
        if (index == nullptr) return E_INVALIDARG;
        *index = owner_ != nullptr && owner_->view.selected > 0 ? static_cast<UINT>(owner_->view.selected) : 0;
        return S_OK;
    }
    STDMETHODIMP GetString(UINT index, BSTR* text) override {
        if (text == nullptr) return E_INVALIDARG;
        *text = nullptr;
        if (index >= Count()) return E_INVALIDARG;
        const std::wstring& candidate = owner_->view.candidates[index];
        *text = SysAllocStringLen(candidate.c_str(), static_cast<UINT>(candidate.size()));
        return *text != nullptr ? S_OK : E_OUTOFMEMORY;
    }
    // ページは窓と同じく 9 個ずつ
    STDMETHODIMP GetPageIndex(UINT* index, UINT size, UINT* pages) override {
        if (pages == nullptr) return E_INVALIDARG;
        const UINT count = (Count() + kPage - 1) / kPage;
        *pages = count;
        if (index == nullptr) return S_OK;
        for (UINT i = 0; i < count && i < size; i++) index[i] = i * kPage;
        return size < count ? S_FALSE : S_OK;
    }
    // ページの区切りは Meltype が決める
    STDMETHODIMP SetPageIndex(UINT*, UINT) override { return E_NOTIMPL; }
    STDMETHODIMP GetCurrentPage(UINT* page) override {
        if (page == nullptr) return E_INVALIDARG;
        UINT selected = 0;
        GetSelection(&selected);
        *page = selected / kPage;
        return S_OK;
    }

private:
    static constexpr UINT kPage = static_cast<UINT>(kPageSize);
    ~CandidateElement() = default;
    UINT Count() const { return owner_ != nullptr ? static_cast<UINT>(owner_->view.candidates.size()) : 0; }

    LONG refs_ = 1;
    CandidateWindow::Impl* owner_;
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

CandidateWindow::CandidateWindow(ITfThreadMgr* threadMgr, SelectHandler onSelect) : impl_(new Impl()) {
    impl_->onSelect = std::move(onSelect);
    impl_->threadMgr = threadMgr;
    if (threadMgr != nullptr && SUCCEEDED(threadMgr->QueryInterface(IID_PPV_ARGS(&impl_->elements)))) impl_->element = new CandidateElement(impl_);
}

CandidateWindow::~CandidateWindow() {
    Hide();
    if (impl_->element) {
        impl_->element->Detach();
        impl_->element->Release();
    }
    if (impl_->target) impl_->target->Release();
    if (impl_->meaningTarget) impl_->meaningTarget->Release();
    if (impl_->meaningHwnd) DestroyWindow(impl_->meaningHwnd);
    if (impl_->hwnd) DestroyWindow(impl_->hwnd);
    delete impl_;
}

bool CandidateWindow::Visible() const { return impl_->hwnd != nullptr && IsWindowVisible(impl_->hwnd); }

void CandidateWindow::Hide() {
    Impl& d = *impl_;
    if (d.elementId != TF_INVALID_UIELEMENTID) {
        const DWORD id = d.elementId;
        d.elementId = TF_INVALID_UIELEMENTID;
        d.elements->EndUIElement(id);
    }
    d.shown = true;
    d.HideAll();
}

void CandidateWindow::Show(const CandidateView& view, const RECT& anchor) {
    Impl& d = *impl_;
    const bool begin = d.element != nullptr && d.elementId == TF_INVALID_UIELEMENTID;
    if (begin) {
        // BeginUIElement の中でアプリが候補を読むので、先に入れておく (窓は置き直す)
        d.HideAll();
        d.view = view;
        d.anchor = anchor;
        BOOL show = TRUE;
        DWORD id = TF_INVALID_UIELEMENTID;
        if (SUCCEEDED(d.elements->BeginUIElement(d.element, &show, &id))) {
            d.elementId = id;
            d.shown = show != FALSE;
            if (!d.shown) TipLog(L"候補の一覧はアプリが描きます (UI Element)");
        }
    }
    if (d.shown) {
        Render(d, view, anchor);
    } else {
        d.view = view;
        d.anchor = anchor;
    }
    if (!begin && d.elementId != TF_INVALID_UIELEMENTID) d.elements->UpdateUIElement(d.elementId);
}

static void Render(CandidateWindow::Impl& d, const CandidateView& view, const RECT& anchor) {
    if (!EnsureFactories() || !EnsureClasses()) return;
    if (d.hwnd == nullptr) {
        d.hwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, kClassName, L"", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, g_module, &d);
        if (d.hwnd == nullptr) return;
        PrepareWindow(d.hwnd, CurrentPalette().border);
    }
    float scale = GetDpiForWindow(d.hwnd) / 96.0f;
    if (scale <= 0) scale = 1;
    // DPI が変わったときも、描画 (render target) と当たり判定の scale がずれないように置き直す
    bool samePlace = d.hwnd != nullptr && IsWindowVisible(d.hwnd) && EqualRect(&d.anchor, &anchor) && d.view.candidates == view.candidates && d.view.suggestion == view.suggestion &&
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
