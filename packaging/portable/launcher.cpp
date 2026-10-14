// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 rankoi

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <strsafe.h>

#include <string>
#include <cwchar>

namespace {

constexpr WORD kPayloadResource = 101;
constexpr WORD kRunnerResource = 102;

bool WriteResource(WORD id, const wchar_t* path) {
    HRSRC resource = FindResourceW(nullptr, MAKEINTRESOURCEW(id), RT_RCDATA);
    if (!resource) return false;
    HGLOBAL loaded = LoadResource(nullptr, resource);
    const DWORD size = SizeofResource(nullptr, resource);
    const void* bytes = LockResource(loaded);
    if (!loaded || !bytes || size == 0) return false;
    HANDLE file = CreateFileW(path, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    const bool ok = WriteFile(file, bytes, size, &written, nullptr) && written == size;
    CloseHandle(file);
    return ok;
}

std::wstring Quote(const std::wstring& value) {
    std::wstring result = L"\"";
    unsigned int slashes = 0;
    for (wchar_t ch : value) {
        if (ch == L'\\') {
            ++slashes;
        } else if (ch == L'"') {
            result.append(slashes * 2 + 1, L'\\');
            result += ch;
            slashes = 0;
        } else {
            result.append(slashes, L'\\');
            slashes = 0;
            result += ch;
        }
    }
    result.append(slashes * 2, L'\\');
    result += L'"';
    return result;
}

void ShowError(const wchar_t* message) {
    MessageBoxW(nullptr, message, L"Meltype ポータブル版", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
}

} // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    wchar_t executable[MAX_PATH];
    const DWORD executableLength = GetModuleFileNameW(nullptr, executable, ARRAYSIZE(executable));
    if (executableLength == 0 || executableLength >= ARRAYSIZE(executable)) {
        ShowError(L"実行ファイルの場所を取得できませんでした。");
        return 1;
    }
    wchar_t* separator = wcsrchr(executable, L'\\');
    if (!separator) {
        ShowError(L"実行ファイルの場所を取得できませんでした。");
        return 1;
    }
    *separator = L'\0';

    wchar_t temporary[MAX_PATH];
    const DWORD temporaryLength = GetTempPathW(ARRAYSIZE(temporary), temporary);
    if (temporaryLength == 0 || temporaryLength >= ARRAYSIZE(temporary)) {
        ShowError(L"一時フォルダーを取得できませんでした。");
        return 1;
    }
    wchar_t work[MAX_PATH];
    if (FAILED(StringCchPrintfW(work, ARRAYSIZE(work), L"%lsMeltype-portable-%lu-%llu", temporary,
                                GetCurrentProcessId(), GetTickCount64())) || !CreateDirectoryW(work, nullptr)) {
        ShowError(L"一時作業フォルダーを作成できませんでした。");
        return 1;
    }

    wchar_t payload[MAX_PATH], runner[MAX_PATH], data[MAX_PATH];
    if (FAILED(StringCchPrintfW(payload, ARRAYSIZE(payload), L"%ls\\payload.zip", work)) ||
        FAILED(StringCchPrintfW(runner, ARRAYSIZE(runner), L"%ls\\run.ps1", work)) ||
        FAILED(StringCchPrintfW(data, ARRAYSIZE(data), L"%ls\\data", executable)) ||
        !WriteResource(kPayloadResource, payload) || !WriteResource(kRunnerResource, runner)) {
        ShowError(L"ポータブル版のデータを読み出せませんでした。");
        return 1;
    }

    SetEnvironmentVariableW(L"MELTYPE_BUNDLE_WORK", work);
    SetEnvironmentVariableW(L"MELTYPE_BUNDLE_PAYLOAD", payload);
    SetEnvironmentVariableW(L"MELTYPE_DATA_DIR", data);
    wchar_t systemDirectory[MAX_PATH];
    const UINT systemLength = GetSystemDirectoryW(systemDirectory, ARRAYSIZE(systemDirectory));
    if (systemLength == 0 || systemLength >= ARRAYSIZE(systemDirectory)) {
        ShowError(L"Windows PowerShell の場所を取得できませんでした。");
        return 1;
    }
    wchar_t powershell[MAX_PATH];
    if (FAILED(StringCchPrintfW(powershell, ARRAYSIZE(powershell),
                                L"%ls\\WindowsPowerShell\\v1.0\\powershell.exe", systemDirectory))) {
        ShowError(L"Windows PowerShell の場所を取得できませんでした。");
        return 1;
    }
    std::wstring command = Quote(powershell) + L" -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File " + Quote(runner);
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(powershell, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, executable, &startup, &process)) {
        ShowError(L"ポータブル版の起動に失敗しました。Windows PowerShell を利用できるか確認してください。");
        return 1;
    }
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = 1;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    SetEnvironmentVariableW(L"MELTYPE_BUNDLE_WORK", nullptr);
    SetEnvironmentVariableW(L"MELTYPE_BUNDLE_PAYLOAD", nullptr);
    SetEnvironmentVariableW(L"MELTYPE_DATA_DIR", nullptr);
    return static_cast<int>(exitCode);
}
