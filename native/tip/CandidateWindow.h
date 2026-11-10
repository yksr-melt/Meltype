// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// 変換の候補の一覧 (Windows 11 の Microsoft IME に近い見た目)。アプリのプロセスの中に出す、フォーカスを奪わないウィンドウ。

#pragma once

#include <functional>
#include <string>
#include <vector>

#include "Globals.h"

namespace meltype {

struct CandidateView {
    std::vector<std::wstring> candidates;
    std::vector<std::wstring> notes;  // 候補ごとの注釈 (英訳など)。無ければ空
    int selected = -1;
    std::wstring suggestion;  // もしかして (無ければ空)
    std::wstring meaning;     // 選んでいる候補の意味 (無ければ空)
    bool numbered = false;    // 番号を出すか (数字キーで選べる一覧)
    std::wstring typed;       // 打ったキー (ローマ字。出さないなら空)
    std::wstring hint;        // 案内 (無ければ空)
    UINT meaningMs = 0;       // 選んでいる候補で止まってから意味を出すまで
};

class CandidateWindow {
public:
    using SelectHandler = std::function<void(int index)>;

    explicit CandidateWindow(SelectHandler onSelect);
    ~CandidateWindow();
    CandidateWindow(const CandidateWindow&) = delete;
    CandidateWindow& operator=(const CandidateWindow&) = delete;

    // anchor は選んでいる文節の画面上の四角形。その左下に出す (画面からはみ出すなら上)。
    void Show(const CandidateView& view, const RECT& anchor);
    void Hide();
    bool Visible() const;

    static void RegisterClasses();
    static void UnregisterClasses();
    // DllCanUnloadNow で S_OK を返すときに呼ぶ。DllMain からは呼ばない
    static void ReleaseFactories();

    struct Impl;

private:
    Impl* impl_;
};

}  // namespace meltype
