// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// stub (MeltypeTip.dll) と impl (MeltypeTipImpl.dll) の両方にリンクする、文字コードの変換とログ。

#include <strsafe.h>

#include <cstdarg>

#include "Globals.h"

namespace meltype {

HINSTANCE g_module = nullptr;

std::string ToUtf8(const std::wstring& text) {
    if (text.empty()) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string out(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), size, nullptr, nullptr);
    return out;
}

std::wstring FromUtf8(const std::string& text) {
    if (text.empty()) return {};
    int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring out(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), size);
    return out;
}

// 自分 (IME を読み込んだアプリ) の整合性レベルが Medium 以下か。読めなければ false
// (管理者として動くアプリは、ふつうの権限のプログラムがジャンクションで向けた先にログを作ってしまうので書かない)
static bool SelfIsMediumOrLower() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
    DWORD level = 0;
    DWORD size = 0;
    GetTokenInformation(token, TokenIntegrityLevel, nullptr, 0, &size);
    std::vector<BYTE> buffer(size);
    if (size > 0 && GetTokenInformation(token, TokenIntegrityLevel, buffer.data(), size, &size)) {
        PSID sid = reinterpret_cast<TOKEN_MANDATORY_LABEL*>(buffer.data())->Label.Sid;
        level = *GetSidSubAuthority(sid, *GetSidSubAuthorityCount(sid) - 1);
    }
    CloseHandle(token);
    return level != 0 && level <= SECURITY_MANDATORY_MEDIUM_RID;
}

void TipLog(const wchar_t* format, ...) {
    static int enabled = -1;
    if (enabled < 0 && !SelfIsMediumOrLower()) enabled = 0;
    if (enabled < 0) {
        wchar_t value[8] = {};
        enabled = GetEnvironmentVariableW(L"MELTYPE_TIP_LOG", value, 8) > 0 && value[0] == L'1' ? 1 : 0;
        // 環境変数が無ければ、データフォルダーに tip-log という空のファイルがあるときだけ
        if (!enabled) {
            wchar_t path[MAX_PATH] = {};
            if (GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH) > 0) {
                StringCchCatW(path, MAX_PATH, L"\\Meltype\\tip-log");
                enabled = GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES ? 1 : 0;
            }
        }
    }
    if (!enabled) return;
    wchar_t message[1024];
    va_list args;
    va_start(args, format);
    StringCchVPrintfW(message, ARRAYSIZE(message), format, args);
    va_end(args);
    wchar_t path[MAX_PATH] = {};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH) == 0) return;
    StringCchCatW(path, MAX_PATH, L"\\Meltype\\tip.log");
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME now;
    GetLocalTime(&now);
    wchar_t exe[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exe, MAX_PATH);
    const wchar_t* name = wcsrchr(exe, L'\\');
    wchar_t line[1400];
    StringCchPrintfW(line, ARRAYSIZE(line), L"%02d:%02d:%02d.%03d [%s %lu] %s\r\n", now.wHour, now.wMinute, now.wSecond, now.wMilliseconds,
                     name ? name + 1 : exe, GetCurrentThreadId(), message);
    std::string utf8 = ToUtf8(line);
    DWORD written = 0;
    WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
    CloseHandle(file);
}

}  // namespace meltype
