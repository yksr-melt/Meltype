// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai

#include "Pipe.h"

#include <aclapi.h>
#include <sddl.h>

namespace meltype {
namespace {

// \\.\pipe\Meltype.Tip.<ユーザーの SID> (Meltype.exe の TipServer.PipeName と同じ)
std::wstring PipeName() {
    std::wstring name = L"\\\\.\\pipe\\Meltype.Tip.";
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return name + L"user";
    DWORD size = 0;
    GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<BYTE> buffer(size);
    std::wstring sid = L"user";
    if (size > 0 && GetTokenInformation(token, TokenUser, buffer.data(), size, &size)) {
        LPWSTR text = nullptr;
        if (ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer.data())->User.Sid, &text)) {
            sid = text;
            LocalFree(text);
        }
    }
    CloseHandle(token);
    return name + sid;
}

}  // namespace

// トークンの整合性レベル (SECURITY_MANDATORY_*_RID)。読めなければ 0
static DWORD IntegrityOf(HANDLE token) {
    DWORD size = 0;
    GetTokenInformation(token, TokenIntegrityLevel, nullptr, 0, &size);
    std::vector<BYTE> buffer(size);
    if (size == 0 || !GetTokenInformation(token, TokenIntegrityLevel, buffer.data(), size, &size)) return 0;
    PSID sid = reinterpret_cast<TOKEN_MANDATORY_LABEL*>(buffer.data())->Label.Sid;
    return *GetSidSubAuthority(sid, *GetSidSubAuthorityCount(sid) - 1);
}

// 自分 (IME を読み込んだアプリ) の整合性レベル。読めなければ 0
static DWORD SelfIntegrity() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return 0;
    DWORD level = IntegrityOf(token);
    CloseHandle(token);
    return level;
}

// 自分が整合性レベル Low 以下 (ストアアプリ・サンドボックス) で動いているか
static bool SelfIsLowIntegrity() { return SelfIntegrity() < SECURITY_MANDATORY_MEDIUM_RID; }

// パイプの所有者が自分と同じユーザーか。所有者は作ったプロセスのユーザーになり、ほかのユーザーには自分の SID を所有者にできない。
// 整合性レベルの印は、Meltype.exe もストアアプリのために Low にしているので見ない。
// 読むのに要る READ_CONTROL は、つなぐときの GENERIC_READ と、Meltype.exe がストアアプリに渡す権限に含まれる
static bool PipeOwnedBySelf(HANDLE pipe) {
    PSID owner = nullptr;
    PSECURITY_DESCRIPTOR descriptor = nullptr;
    if (GetSecurityInfo(pipe, SE_KERNEL_OBJECT, OWNER_SECURITY_INFORMATION, &owner, nullptr, nullptr, nullptr, &descriptor) != ERROR_SUCCESS) return false;
    bool same = false;
    HANDLE token = nullptr;
    if (owner != nullptr && OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
        DWORD size = 0;
        GetTokenInformation(token, TokenUser, nullptr, 0, &size);
        std::vector<BYTE> user(size);
        if (size > 0 && GetTokenInformation(token, TokenUser, user.data(), size, &size)) {
            same = EqualSid(owner, reinterpret_cast<TOKEN_USER*>(user.data())->User.Sid) != FALSE;
        }
        CloseHandle(token);
    }
    LocalFree(descriptor);
    return same;
}

// つないだパイプのサーバーが、自分と同じユーザーの、整合性レベル Medium 以上で、自分より低くないプロセスか。
// (Meltype.exe が動いていない間に、権限の低いプログラムが同じ名前のパイプを作って打鍵を受け取るのを防ぐ。
//  管理者として動いているアプリの打鍵を、ふつうの権限で動く Meltype.exe に渡さない。UAC を切っていて、
//  アプリも Meltype.exe も管理者として動いている PC では使える)
// ストアアプリなど自分が Low のときは、ほかのプロセスを調べられないので、パイプの所有者が自分と同じユーザーかだけを確かめる。このときは、
// Meltype.exe が動いていない間に同じユーザーの Low のプロセスが同じ名前のパイプを作ると見分けられない (SECURITY.md。Meltype.exe は先に作られていたらログに書く)
// lower: 相手が自分より低い権限だったので断った (Meltype.exe を起動し直すまで変わらない)
static bool IsTrustedServer(HANDLE pipe, bool& lower) {
    lower = false;
    ULONG pid = 0;
    if (!GetNamedPipeServerProcessId(pipe, &pid)) return false;
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (process == nullptr) return SelfIsLowIntegrity() && PipeOwnedBySelf(pipe);
    bool trusted = false;
    HANDLE token = nullptr;
    if (OpenProcessToken(process, TOKEN_QUERY, &token)) {
        DWORD size = 0;
        GetTokenInformation(token, TokenUser, nullptr, 0, &size);
        std::vector<BYTE> user(size);
        HANDLE self = nullptr;
        if (size > 0 && GetTokenInformation(token, TokenUser, user.data(), size, &size) && OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &self)) {
            DWORD selfSize = 0;
            GetTokenInformation(self, TokenUser, nullptr, 0, &selfSize);
            std::vector<BYTE> selfUser(selfSize);
            if (selfSize > 0 && GetTokenInformation(self, TokenUser, selfUser.data(), selfSize, &selfSize)) {
                trusted = EqualSid(reinterpret_cast<TOKEN_USER*>(user.data())->User.Sid, reinterpret_cast<TOKEN_USER*>(selfUser.data())->User.Sid) != FALSE;
            }
            CloseHandle(self);
        }
        DWORD level = trusted ? IntegrityOf(token) : 0;
        DWORD own = SelfIntegrity();
        trusted = level >= SECURITY_MANDATORY_MEDIUM_RID && level >= own;
        lower = level >= SECURITY_MANDATORY_MEDIUM_RID && level < own;
        CloseHandle(token);
    } else {
        // 相手の権限を調べられない (ほかのユーザーのプロセスか、このアプリより権限の高いプロセス。
        // UAC が有効な PC で Meltype.exe を管理者として動かすと、ふつうのアプリからはこうなる)。確かめられない相手にはつながない
    }
    CloseHandle(process);
    return trusted;
}

bool PipeClient::Connect() {
    if (pipe_ != INVALID_HANDLE_VALUE) return true;
    if (GetTickCount64() < retryAfter_) return false;
    static const std::wstring name = PipeName();
    // GENERIC_WRITE には FILE_APPEND_DATA (パイプでは「パイプを増やす」権限) が含まれ、ストアアプリにはその権限を渡していないので
    // 開けなくなる。読み書きと、メッセージ単位の読み取りにする (SetNamedPipeHandleState) のに要る権限だけにする
    const DWORD access = GENERIC_READ | FILE_WRITE_DATA | FILE_WRITE_ATTRIBUTES;
    // サーバーには、こちらの身元を確かめることだけを許す (なりすまして動くことはさせない)
    const DWORD flags = FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION;
    pipe_ = CreateFileW(name.c_str(), access, 0, nullptr, OPEN_EXISTING, flags, nullptr);
    if (pipe_ == INVALID_HANDLE_VALUE && GetLastError() == ERROR_PIPE_BUSY && WaitNamedPipeW(name.c_str(), 100)) {
        pipe_ = CreateFileW(name.c_str(), access, 0, nullptr, OPEN_EXISTING, flags, nullptr);
    }
    if (pipe_ == INVALID_HANDLE_VALUE) {
        // 同じ理由でつながらないことが続いても (Meltype.exe が動いていない・つなげないサンドボックス)、ログは理由が変わったときだけ
        DWORD error = GetLastError();
        if (error != lastConnectError_) TipLog(L"パイプにつながりません: %lu", error);
        lastConnectError_ = error;
        retryAfter_ = GetTickCount64() + 2000;
        return false;
    }
    lastConnectError_ = 0;
    bool lower = false;
    if (!IsTrustedServer(pipe_, lower)) {
        // ログは理由ごとに 1 回だけ (つなぎ直すたびに書かない)
        if (!refusalLogged_) {
            TipLog(lower ? L"このアプリは Meltype.exe より高い権限 (管理者など) で動いているので、つなぎません"
                         : L"パイプの相手が Meltype.exe と確かめられないので、つなぎません (ほかのユーザーのプロセスか、このアプリより高い権限のプロセス)");
        }
        refusalLogged_ = true;
        CloseHandle(pipe_);
        pipe_ = INVALID_HANDLE_VALUE;
        // 権限の違いは Meltype.exe を起動し直すまで変わらないので、しばらくつなぎ直さない
        retryAfter_ = GetTickCount64() + (lower ? 60000 : 5000);
        return false;
    }
    refusalLogged_ = false;
    DWORD mode = PIPE_READMODE_MESSAGE;
    SetNamedPipeHandleState(pipe_, &mode, nullptr, nullptr);
    if (event_ == nullptr) event_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    return true;
}

void PipeClient::Close() {
    if (pipe_ != INVALID_HANDLE_VALUE) {
        CancelIoEx(pipe_, nullptr);
        CloseHandle(pipe_);
        pipe_ = INVALID_HANDLE_VALUE;
    }
    if (event_ != nullptr) {
        CloseHandle(event_);
        event_ = nullptr;
    }
}

// 終わらなかったときの error は、時間切れなら WAIT_TIMEOUT、それより前に失敗で終わったらそのエラー (切れていたなど)。
bool PipeClient::Wait(OVERLAPPED& overlapped, DWORD timeoutMs, DWORD& transferred, DWORD& error) {
    error = ERROR_SUCCESS;
    if (WaitForSingleObject(event_, timeoutMs) != WAIT_OBJECT_0) {
        CancelIoEx(pipe_, &overlapped);
        GetOverlappedResult(pipe_, &overlapped, &transferred, TRUE);
        error = WAIT_TIMEOUT;
        return false;
    }
    if (GetOverlappedResult(pipe_, &overlapped, &transferred, FALSE)) return true;
    error = GetLastError();
    return error == ERROR_MORE_DATA;
}

namespace {
// 前の Meltype.exe とのつながりが切れていたときのエラー (起動し直した)
bool IsDisconnected(DWORD error) {
    return error == ERROR_NO_DATA || error == ERROR_BROKEN_PIPE || error == ERROR_PIPE_NOT_CONNECTED;
}
}  // namespace

// 書き込み・読み取りが時間内に終わらなかった。閉じる (閉じないと、遅れて来た応答を次の要求の応答として読んでしまう)。
// 応答しない Meltype.exe を毎回待つと、どのアプリでも打鍵のたびに止まるので、少し前にも時間切れになっていたら、しばらくつながない。
// 1 回だけなら閉じるだけにする。Meltype.exe の画面のスレッドが少しふさがっただけでも時間切れになり、
// しばらくつながないと、その間に打ったキーが打った文字のまま入る。
// optional の問い合わせは待つ時間が短く、時間切れになりやすいので数えない
void PipeClient::TimedOut(bool optional) {
    Close();
    if (optional) return;
    ULONGLONG now = GetTickCount64();
    if (lastTimeout_ != 0 && now - lastTimeout_ < 5000) retryAfter_ = now + 3000;
    lastTimeout_ = now;
}

bool PipeClient::Transact(const std::string& request, std::string& response, DWORD timeoutMs, bool optional) {
    response.clear();
    // 前からのつながりを使う (Meltype.exe を起動し直していたら、書き込みで切れていたと分かる)
    bool reused = pipe_ != INVALID_HANDLE_VALUE;
    if (!Connect()) return false;
    ULONGLONG deadline = GetTickCount64() + timeoutMs;
    auto remaining = [&]() -> DWORD {
        ULONGLONG now = GetTickCount64();
        return now >= deadline ? 0 : static_cast<DWORD>(deadline - now);
    };

    OVERLAPPED overlapped = {};
    overlapped.hEvent = event_;
    ResetEvent(event_);
    DWORD transferred = 0;
    if (!WriteFile(pipe_, request.data(), static_cast<DWORD>(request.size()), nullptr, &overlapped) && GetLastError() != ERROR_IO_PENDING) {
        DWORD error = GetLastError();
        TipLog(L"パイプに書けません: %lu", error);
        Close();
        // 前の Meltype.exe とのつながりが切れていた (起動し直した): まだ何も送っていないので、つなぎ直して 1 回だけ送り直す。
        // 送り直さないと、起動し直した後の最初のキーが、応答が無いときと同じく打った文字のまま入る
        if (reused && IsDisconnected(error)) {
            TipLog(L"つなぎ直して送り直します");
            return Transact(request, response, remaining(), optional);
        }
        return false;
    }
    DWORD waitError = ERROR_SUCCESS;
    if (!Wait(overlapped, remaining(), transferred, waitError)) {
        // WriteFile は ERROR_IO_PENDING を返し、切れていたことは終わるのを待って分かることが多い (#1112)。
        // その場で失敗したときと同じく、つなぎ直して 1 回だけ送り直す (時間切れとは分けて、TimedOut にしない)
        if (reused && IsDisconnected(waitError)) {
            TipLog(L"パイプへの書き込みが切れていました (エラー %lu)。つなぎ直して送り直します", waitError);
            Close();
            return Transact(request, response, remaining(), optional);
        }
        TipLog(L"パイプへの書き込みが終わりません (エラー %lu)", waitError);
        TimedOut(optional);
        return false;
    }

    char buffer[16 * 1024];
    while (true) {
        overlapped = {};
        overlapped.hEvent = event_;
        ResetEvent(event_);
        BOOL ok = ReadFile(pipe_, buffer, sizeof(buffer), nullptr, &overlapped);
        DWORD error = ok ? ERROR_SUCCESS : GetLastError();
        if (!ok && error != ERROR_IO_PENDING && error != ERROR_MORE_DATA) {
            // 切られた (Meltype.exe が終わった・つながりが多すぎて断られた): すぐにはつなぎ直さない
            TipLog(L"パイプから読めません: %lu", error);
            Close();
            retryAfter_ = GetTickCount64() + 2000;
            return false;
        }
        if (!Wait(overlapped, remaining(), transferred, waitError)) {
            TipLog(L"Meltype.exe の応答が時間内に来ません (エラー %lu)", waitError);
            TimedOut(optional);
            return false;
        }
        response.append(buffer, transferred);
        if (response.size() > kMaxResponse) {
            TipLog(L"応答が大きすぎます");
            Close();
            return false;
        }
        // メッセージの続きがあれば ERROR_MORE_DATA
        if (GetOverlappedResult(pipe_, &overlapped, &transferred, FALSE)) return true;
        if (GetLastError() != ERROR_MORE_DATA) {
            Close();
            return false;
        }
    }
}

}  // namespace meltype
