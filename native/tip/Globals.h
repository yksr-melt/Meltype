// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// Meltype IME (TSF のテキストサービス) の共通の定義。

#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <msctf.h>
#include <ctffunc.h>
#include <olectl.h>
#include <string>
#include <vector>

namespace meltype {

// テキストサービスの CLSID (COM のクラス)
// {417D801B-A9BD-4C26-BD16-356A825A6998}
inline constexpr CLSID CLSID_TextService = {0x417d801b, 0xa9bd, 0x4c26, {0xbd, 0x16, 0x35, 0x6a, 0x82, 0x5a, 0x69, 0x98}};

// 日本語のキーボードとしてのプロファイル
// {21F643F4-72D5-4946-BF70-0A126AB52D05}
inline constexpr GUID GUID_Profile = {0x21f643f4, 0x72d5, 0x4946, {0xbf, 0x70, 0x0a, 0x12, 0x6a, 0xb5, 0x2d, 0x05}};

// 下線の種類: 打っている途中 / 変換した文節 / 選んでいる文節
// {C8107D36-2806-4D8F-A183-FD0AEB8ED862}
inline constexpr GUID GUID_AttrInput = {0xc8107d36, 0x2806, 0x4d8f, {0xa1, 0x83, 0xfd, 0x0a, 0xeb, 0x8e, 0xd8, 0x62}};
// {E17AA399-C5EB-42E6-BE45-C44F0651F2E2}
inline constexpr GUID GUID_AttrConverted = {0xe17aa399, 0xc5eb, 0x42e6, {0xbe, 0x45, 0xc4, 0x4f, 0x06, 0x51, 0xf2, 0xe2}};
// {CEF94813-C47F-48C1-B4EF-D4582D5D4054}
inline constexpr GUID GUID_AttrTarget = {0xcef94813, 0xc47f, 0x48c1, {0xb4, 0xef, 0xd4, 0x58, 0x2d, 0x5d, 0x40, 0x54}};

inline constexpr LANGID kLangId = MAKELANGID(LANG_JAPANESE, SUBLANG_JAPANESE_JAPAN);
inline constexpr wchar_t kDisplayName[] = L"Meltype";

extern HINSTANCE g_module;

// ---- stub (MeltypeTip.dll) と impl (MeltypeTipImpl.dll) の ABI ----
// プロセスに残った古い stub が、新しい impl をロードする。
// stub は、impl の MeltypeTipAbi が自分の kStubImplAbi と一致したときだけ MeltypeTipCreate を呼ぶ。一致しなければ、プロセスを再起動するまで、そのプロセスの impl は差し替わらない

// MeltypeTipCreateFn・kStubImplStale を変更したら上げる
inline constexpr UINT kStubImplAbi = 1;

// impl がエクスポートする関数 MeltypeTipAbi。impl の kStubImplAbi を返す。どの版の stub からも呼ぶので、シグネチャを変更しない
using MeltypeTipAbiFn = UINT(STDAPICALLTYPE*)();

// impl がエクスポートする関数 MeltypeTipCreate。stub: kStubImplStale の送信先のウィンドウ
using MeltypeTipCreateFn = HRESULT(STDAPICALLTYPE*)(HWND stub, ITfTextInputProcessorEx** service);

// impl が stub のウィンドウに PostMessage する: Meltype.exe が {"stale":true} を返した
inline constexpr UINT kStubImplStale = WM_APP + 1;

// 文字コードの変換
std::string ToUtf8(const std::wstring& text);
std::wstring FromUtf8(const std::string& text);

// デバッグ用のログ (%LOCALAPPDATA%\Meltype\tip.log、MELTYPE_TIP_LOG=1 のときだけ)
void TipLog(const wchar_t* format, ...);

}  // namespace meltype
