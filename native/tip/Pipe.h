// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// Meltype.exe (TipServer) との名前付きパイプ。1 要求 1 応答で、待つのは決まった時間まで。

#pragma once

#include <string>

#include "Globals.h"

namespace meltype {

class PipeClient {
public:
    PipeClient() = default;
    ~PipeClient() { Close(); }
    PipeClient(const PipeClient&) = delete;
    PipeClient& operator=(const PipeClient&) = delete;

    // 要求を送って応答を待つ。つながらない・時間切れなら false (次の呼び出しでつなぎ直す)。
    // optional: 失敗しても入力に困らない問い合わせ。時間切れでも、しばらくつながないようにはしない
    bool Transact(const std::string& request, std::string& response, DWORD timeoutMs, bool optional = false);
    void Close();

private:
    bool Connect();
    bool Wait(OVERLAPPED& overlapped, DWORD timeoutMs, DWORD& transferred, DWORD& error);
    void TimedOut(bool optional);

    static constexpr size_t kMaxResponse = 1024 * 1024;
    HANDLE pipe_ = INVALID_HANDLE_VALUE;
    HANDLE event_ = nullptr;
    // つながらなかったとき、すぐにまた試さない (Meltype.exe が動いていないときに毎回待たないように)
    ULONGLONG retryAfter_ = 0;
    ULONGLONG lastTimeout_ = 0;   // 最後に時間切れになった時刻 (optional の問い合わせは数えない)
    DWORD lastConnectError_ = 0;  // 最後につながらなかった理由 (同じ理由をログに書き続けない)
    bool refusalLogged_ = false;  // つなぐのを断ったことをログに書いた (続けて書かない)
};

}  // namespace meltype
