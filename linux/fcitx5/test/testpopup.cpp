// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MuNeNiCK
// 実際の Native と公開 frontend で、固定ガイドと入力に応じた提案を区別して確かめる。
#include <testfrontend_public.h>
#include <fcitx-utils/eventdispatcher.h>
#include <fcitx-utils/testing.h>
#include <fcitx/addonmanager.h>
#include <fcitx/candidatelist.h>
#include <fcitx/inputcontextmanager.h>
#include <fcitx/inputmethodgroup.h>
#include <fcitx/inputmethodmanager.h>
#include <fcitx/inputpanel.h>
#include <fcitx/instance.h>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

using namespace fcitx;

namespace {
class SettingsFixture {
public:
    bool create(bool manual) {
        char path[] = "/tmp/meltype-popup-XXXXXX";
        const auto *created = mkdtemp(path);
        if (!created) return false;
        directory_ = created;
        std::ofstream config(directory_ + "/config.json");
        config << R"({"SettingsVersion":5,"DetectionLevel":")"
               << (manual ? "Manual" : "Balanced")
               << R"(","InputStyle":"Romaji","LiveConversion":false,"LearningEnabled":false,"PredictiveCandidates":false,"TabConversion":true,"SpaceAroundEnglish":false})";
        return config.good() && setenv("MELTYPE_DATA_DIR", directory_.c_str(), 1) == 0;
    }

    ~SettingsFixture() {
        // mkdtemp で作った専用のツリーだけを、Instance の終了後に片付ける (Mozc の学習先も含む)。
        if (!directory_.empty()) {
            std::error_code error;
            std::filesystem::remove_all(directory_, error);
        }
    }

private:
    std::string directory_;
};

bool expect(bool condition, const std::string &message) {
    if (!condition) std::cerr << "FAIL: " << message << '\n';
    return condition;
}

bool press(AddonInstance *frontend, ICUUID uuid, const std::string &name) {
    const bool consumed = frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key(name), false);
    frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key(name), true);
    return consumed;
}

void type(AddonInstance *frontend, ICUUID uuid, const std::string &text) {
    for (char c : text) press(frontend, uuid, std::string(1, c));
}

bool manualProposal(Instance *instance, AddonInstance *frontend, bool legacy) {
    const auto uuid = frontend->call<ITestFrontend::createInputContext>("testapp");
    auto *ic = instance->inputContextManager().findByUUID(uuid);
    if (!expect(ic != nullptr, "手動判定の入力欄を作れる")) return false;
    auto flags = ic->capabilityFlags();
    flags |= CapabilityFlag::Preedit;
    ic->setCapabilityFlags(flags);
    instance->setCurrentInputMethod(ic, "meltype", true);
    type(frontend, uuid, "google");
    if (!expect(ic->inputPanel().clientPreedit().toString() == "ごおgぇ", "手動判定では勝手に英字へ変えない")) return false;
    const auto hint = ic->inputPanel().auxDown().toString();
    if (legacy) {
        if (!expect(hint.find("Tab → google (英字に)") == 0 && hint.find("F7 カタカナ") != std::string::npos,
            "案内が未分離の旧 Native では、提案ごと消さず従来の表示を保つ")) return false;
    } else if (!expect(hint == "Tab → google (英字に)", "固定ガイドを隠しても手動判定の英字提案は表示する")) return false;
    if (!expect(press(frontend, uuid, "Tab"), "Tab で提案を採用する")) return false;
    if (!expect(ic->inputPanel().clientPreedit().toString() == "google", "提案どおり英字になる")) return false;
    const auto acceptedHint = ic->inputPanel().auxDown().toString();
    if (!expect(acceptedHint.find("Tab → google") == std::string::npos, "採用した提案は残さない")) return false;
    if (!legacy && !expect(acceptedHint.empty(), "案内が分離された Native では固定ガイドを表示しない")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("google");
    press(frontend, uuid, "Return");
    if (!expect(ic->inputPanel().clientPreedit().empty() && ic->inputPanel().auxDown().empty(), "確定後に表示を閉じる")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << (legacy
        ? "PASS: 旧 Native の固定ガイドを保ち、手動判定の提案・採用・確定を表示する\n"
        : "PASS: 手動判定の提案・採用・確定を、長い固定ガイドなしで表示する\n");
    return true;
}

bool compactComposition(Instance *instance, AddonInstance *frontend) {
    const auto uuid = frontend->call<ITestFrontend::createInputContext>("testapp");
    auto *ic = instance->inputContextManager().findByUUID(uuid);
    if (!expect(ic != nullptr, "通常入力の入力欄を作れる")) return false;
    instance->setCurrentInputMethod(ic, "meltype", true);
    type(frontend, uuid, "nihongo");
    if (!expect(ic->inputPanel().preedit().toString() == "にほんご" && ic->inputPanel().auxDown().empty(), "入力文字を残して固定ガイドを隠す")) return false;
    press(frontend, uuid, "space");
    if (!expect(ic->inputPanel().candidateList() && ic->inputPanel().auxDown().empty(), "候補を残して変換中の固定ガイドも隠す")) return false;
    const auto selected = ic->inputPanel().preedit().toString();
    if (!expect(!selected.empty(), "選択中の候補を表示する")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>(selected);
    press(frontend, uuid, "Return");
    if (!expect(ic->inputPanel().preedit().empty() && !ic->inputPanel().candidateList(), "確定で文字と候補一覧を閉じる")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: 通常の入力・候補・確定を保ち、固定ガイドだけを隠す\n";
    return true;
}

bool verticalCandidates(Instance *instance, AddonInstance *frontend) {
    const auto uuid = frontend->call<ITestFrontend::createInputContext>("testapp");
    auto *ic = instance->inputContextManager().findByUUID(uuid);
    if (!expect(ic != nullptr, "縦候補の入力欄を作れる")) return false;
    auto flags = ic->capabilityFlags();
    flags |= CapabilityFlag::Preedit;
    ic->setCapabilityFlags(flags);
    instance->setCurrentInputMethod(ic, "meltype", true);
    type(frontend, uuid, "kawa");
    press(frontend, uuid, "space");
    const auto list = ic->inputPanel().candidateList();
    if (!expect(list && list->size() > 1, "変換候補を表示する")) return false;
    if (!expect(list->layoutHint() == CandidateLayoutHint::Vertical, "全体設定と独立にMeltypeの候補を縦表示にする")) return false;
    const auto selected = ic->inputPanel().clientPreedit().toString();
    if (!expect(!selected.empty(), "入力欄内の未確定文字を表示する")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>(selected);
    press(frontend, uuid, "Return");
    if (!expect(!ic->inputPanel().candidateList() && ic->inputPanel().clientPreedit().empty(), "確定で縦候補と未確定文字を閉じる")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: 候補を縦表示にし、入力欄内の文字表示と確定を保つ\n";
    return true;
}

bool spellingProposal(Instance *instance, AddonInstance *frontend) {
    const auto uuid = frontend->call<ITestFrontend::createInputContext>("testapp");
    auto *ic = instance->inputContextManager().findByUUID(uuid);
    if (!expect(ic != nullptr, "訂正提案の入力欄を作れる")) return false;
    instance->setCurrentInputMethod(ic, "meltype", true);
    type(frontend, uuid, "buresureddo");
    if (!expect(ic->inputPanel().preedit().toString() == "ぶれすれっど", "訂正前の文字を表示する")) return false;
    if (!expect(ic->inputPanel().auxDown().toString() == "もしかして: ブレスレット　<Tab>で修正", "実際の辞書の訂正提案だけを表示する")) return false;
    if (!expect(press(frontend, uuid, "Tab"), "Tab は変換より先に訂正提案を採用する")) return false;
    if (!expect(ic->inputPanel().preedit().toString() == "ぶれすれっと", "提案どおり訂正する")) return false;
    if (!expect(ic->inputPanel().auxDown().empty(), "採用済みの訂正提案を消す")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("ぶれすれっと");
    press(frontend, uuid, "Return");
    if (!expect(ic->inputPanel().preedit().empty() && ic->inputPanel().auxDown().empty(), "訂正後も確定できる")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: 固定ガイドを隠しても、実際の訂正提案・採用・確定を保つ\n";
    return true;
}
}

int main(int argc, char *args[]) {
    const std::string mode = argc > 1 ? args[1] : "manual";
    if (mode != "manual" && mode != "spelling" && mode != "legacy") return 1;
    // Native の初期化より前に、実際のユーザー設定から隔離する。
    SettingsFixture settings;
    if (!expect(settings.create(mode != "spelling"), "テスト専用の設定を作れる")) return 1;
    setupTestingEnvironment(FCITX5_MELTYPE_BINARY_DIR,
        {FCITX5_MELTYPE_BINARY_DIR, FCITX5_SYSTEM_ADDON_DIR}, {"test"});
    char arg0[] = "testpopup";
    char arg1[] = "--disable=all";
    char arg2[] = "--enable=testfrontend,testui,testim,meltype";
    char *argv[] = {arg0, arg1, arg2};
    Instance instance(FCITX_ARRAY_SIZE(argv), argv);
    instance.addonManager().registerDefaultLoader(nullptr);
    EventDispatcher dispatcher;
    dispatcher.attach(&instance.eventLoop());
    int result = 1;
    dispatcher.schedule([&]() {
        auto *frontend = instance.addonManager().addon("testfrontend");
        if (frontend && instance.addonManager().addon("meltype", true)) {
            auto group = instance.inputMethodManager().currentGroup();
            group.inputMethodList().clear();
            group.inputMethodList().push_back(InputMethodGroupItem("keyboard-us"));
            group.inputMethodList().push_back(InputMethodGroupItem("meltype"));
            group.setDefaultInputMethod("");
            instance.inputMethodManager().setGroup(group);
            bool ok = mode == "spelling" ? spellingProposal(&instance, frontend) : manualProposal(&instance, frontend, mode == "legacy");
            if (ok && mode == "spelling") ok = compactComposition(&instance, frontend);
            if (ok && mode == "spelling") ok = verticalCandidates(&instance, frontend);
            result = ok ? 0 : 1;
        } else {
            std::cerr << "FAIL: 表示テストのアドオンを読めない\n";
        }
        dispatcher.detach();
        instance.exit();
    });
    const int eventResult = instance.exec();
    return eventResult ? eventResult : result;
}
