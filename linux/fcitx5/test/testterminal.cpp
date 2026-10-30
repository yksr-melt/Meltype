// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MuNeNiCK
// Exercise terminal composition through Fcitx5's public frontend interface.
#include <testfrontend_public.h>
#include <fcitx-utils/eventdispatcher.h>
#include <fcitx-utils/testing.h>
#include <fcitx/addonmanager.h>
#include <fcitx/inputcontextmanager.h>
#include <fcitx/inputmethodgroup.h>
#include <fcitx/inputmethodmanager.h>
#include <fcitx/inputpanel.h>
#include <fcitx/instance.h>
#include <iostream>
#include <string>

using namespace fcitx;

namespace {
int testResult = 1;

bool press(AddonInstance *frontend, ICUUID uuid, const std::string &name) {
    bool consumed = frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key(name), false);
    frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key(name), true);
    return consumed;
}

void type(AddonInstance *frontend, ICUUID uuid, const std::string &text) {
    for (char c : text) press(frontend, uuid, c == ' ' ? "space" : c == '\n' ? "Return" : std::string(1, c));
}

bool expect(bool condition, const std::string &message) {
    if (!condition) std::cerr << "FAIL: " << message << '\n';
    return condition;
}

InputContext *terminal(Instance *instance, AddonInstance *frontend, ICUUID &uuid) {
    uuid = frontend->call<ITestFrontend::createInputContext>("foot");
    auto *ic = instance->inputContextManager().findByUUID(uuid);
    if (!ic) return nullptr;
    CapabilityFlags flags(CapabilityFlag::Terminal);
    flags |= CapabilityFlag::ClientUnfocusCommit;
    flags |= CapabilityFlag::SurroundingText;
    // foot advertises surrounding text, but a TUI may not supply valid text.
    ic->setCapabilityFlags(flags);
    instance->setCurrentInputMethod(ic, "meltype", true);
    return ic;
}

bool quotedJapanese(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "terminal input context exists")) return false;
    if (!expect(!press(frontend, uuid, "j"), "ordinary terminal keys pass through")) return false;
    press(frontend, uuid, "Return");
    type(frontend, uuid, "echo \"");
    type(frontend, uuid, "kyouha");
    const auto preedit = ic->inputPanel().preedit().toString();
    if (!expect(preedit == "きょうは", "quoted Japanese survives capability sync: " + preedit)) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    if (!expect(!press(frontend, uuid, "quotedbl"), "closing quote reaches terminal once")) return false;
    if (!expect(ic->inputPanel().preedit().empty(), "closing quote commits composition")) return false;
    if (!expect(!press(frontend, uuid, "k"), "code protection resumes after closing quote")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: terminal quotes preserve code tracking and commit Japanese\n";
    return true;
}

bool explicitJapanese(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "explicit Japanese input context exists")) return false;
    if (!expect(press(frontend, uuid, "Henkan"), "Japanese mode key is consumed")) return false;
    type(frontend, uuid, "nihongo");
    if (!expect(ic->inputPanel().preedit().toString() == "にほんご", "explicit Japanese starts composition in a terminal")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("にほんご");
    press(frontend, uuid, "Return");
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "Japanese mode survives commit and repeated capability sync")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    press(frontend, uuid, "Return");
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: explicit terminal Japanese survives commit\n";
    return true;
}

bool protectedAfterNavigation(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "navigation input context exists")) return false;
    press(frontend, uuid, "Henkan");
    type(frontend, uuid, "kyouha");
    if (!expect(press(frontend, uuid, "Escape"), "first Escape cancels composition")) return false;
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "consumed Escape keeps Japanese mode")) return false;
    press(frontend, uuid, "Escape");
    for (const auto &key : {"Escape", "Left", "Tab", "Control+a"}) {
        if (!expect(!press(frontend, uuid, key), std::string("navigation reaches app: ") + key)) return false;
        if (!expect(!press(frontend, uuid, "j"), std::string("code protection resumes after ") + key)) return false;
        press(frontend, uuid, "Return");
        press(frontend, uuid, "Henkan");
        type(frontend, uuid, "kyouha");
        frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
        press(frontend, uuid, "Return");
    }
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: only passed navigation returns to protected terminal mode\n";
    return true;
}

bool resetKeepsJapanese(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "reset input context exists")) return false;
    press(frontend, uuid, "Henkan");
    type(frontend, uuid, "nihongo");
    if (!expect(ic->inputPanel().preedit().toString() == "にほんご", "Japanese is selected before reset")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("にほんご");
    ic->reset();
    if (!expect(ic->inputPanel().preedit().empty(), "client reset still commits pending composition")) return false;
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "client reset keeps explicit Japanese without reselecting it")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    press(frontend, uuid, "Return");
    ic->reset();
    type(frontend, uuid, "nihongo");
    if (!expect(ic->inputPanel().preedit().toString() == "にほんご", "an empty reset after commit also keeps Japanese")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("にほんご");
    ic->focusOut();
    ic->focusIn();
    for (char c : std::string("kyouha")) {
        if (!expect(!press(frontend, uuid, std::string(1, c)), "focus change restores protected code keys")) return false;
    }
    if (!expect(ic->inputPanel().preedit().empty(), "focus change clears Japanese override")) return false;
    press(frontend, uuid, "Return");
    type(frontend, uuid, "echo \"");
    ic->reset();
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "client reset preserves an open quote without a Japanese override")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    if (!expect(!press(frontend, uuid, "quotedbl"), "closing quote after reset reaches the terminal")) return false;
    if (!expect(!press(frontend, uuid, "j"), "code protection resumes after the quote")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: client reset keeps Japanese and code tracking, focus changes restore protection\n";
    return true;
}

bool triggerKeepsProtection(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "trigger input context exists")) return false;
    if (!expect(press(frontend, uuid, "Control+space"), "Fcitx trigger disables current IM")) return false;
    frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key("Control_L"), true);
    if (!expect(press(frontend, uuid, "Control+space"), "Fcitx trigger enables Meltype")) return false;
    frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key("Control_L"), true);
    if (!expect(instance->inputMethod(ic) == "meltype", "Meltype is selected after the Fcitx trigger")) return false;
    for (char c : std::string("kyouha")) {
        if (!expect(!press(frontend, uuid, std::string(1, c)), "Fcitx activation keeps terminal code keys protected")) return false;
    }
    if (!expect(ic->inputPanel().preedit().empty(), "Fcitx activation does not start Japanese composition")) return false;
    press(frontend, uuid, "Return");
    if (!expect(press(frontend, uuid, "Henkan"), "explicit Japanese mode key still works after Fcitx activation")) return false;
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "only the explicit mode key selects terminal Japanese")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    press(frontend, uuid, "Return");
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: Fcitx activation preserves terminal protection until explicit Japanese selection\n";
    return true;
}

bool toggleJapanese(Instance *instance, AddonInstance *frontend, const std::string &japaneseKey) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "toggle input context exists")) return false;
    if (!expect(press(frontend, uuid, japaneseKey), "Japanese mode key is consumed")) return false;
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "mode key selects Japanese in a protected terminal")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    press(frontend, uuid, "Return");
    if (!expect(press(frontend, uuid, "Hankaku"), "toggle clears explicit Japanese")) return false;
    for (char c : std::string("kyouha")) {
        if (!expect(!press(frontend, uuid, std::string(1, c)), "toggle restores protected code keys")) return false;
    }
    if (!expect(ic->inputPanel().preedit().empty(), "protected code does not start composition")) return false;
    press(frontend, uuid, "Return");
    type(frontend, uuid, "echo \"kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "toggle restores code protection, not Direct: quoted Japanese still composes")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    if (!expect(!press(frontend, uuid, "quotedbl"), "closing quote still reaches the terminal")) return false;
    if (!expect(!press(frontend, uuid, "j"), "protection resumes after the quote")) return false;
    press(frontend, uuid, "Return");
    press(frontend, uuid, "Hankaku");
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "toggle returns from protected English to Japanese")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    press(frontend, uuid, "Return");
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: " << japaneseKey << " and half/full toggles preserve terminal code protection\n";
    return true;
}

bool directEnglish(Instance *instance, AddonInstance *frontend, const std::string &englishKey, bool terminalInput) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "direct English input context exists")) return false;
    if (!terminalInput) ic->setCapabilityFlags(CapabilityFlag::ClientUnfocusCommit);
    press(frontend, uuid, "Henkan");
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "Japanese is selected before direct English")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    press(frontend, uuid, "Return");
    if (!expect(press(frontend, uuid, englishKey), "explicit English mode key is consumed")) return false;
    for (char c : std::string("echo \"kyouha")) {
        if (!expect(!press(frontend, uuid, c == ' ' ? "space" : c == '"' ? "quotedbl" : std::string(1, c)), "direct English also passes quoted text unchanged")) return false;
    }
    if (!expect(ic->inputPanel().preedit().empty(), "direct English does not compose quoted Japanese")) return false;
    press(frontend, uuid, "Henkan");
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "explicit Japanese still exits direct English")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    press(frontend, uuid, "Return");
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: " << englishKey << " retains direct English " << (terminalInput ? "in a terminal" : "outside a terminal") << '\n';
    return true;
}

bool quotedEscapeProtection(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "quoted Escape input context exists")) return false;
    type(frontend, uuid, "echo \"kyouha");
    if (!expect(press(frontend, uuid, "Escape"), "Escape first cancels quoted composition")) return false;
    type(frontend, uuid, "kyouha");
    if (!expect(ic->inputPanel().preedit().toString() == "きょうは", "consumed Escape preserves tracked quoted context")) return false;
    press(frontend, uuid, "Escape");
    if (!expect(!press(frontend, uuid, "Escape"), "second Escape reaches terminal")) return false;
    if (!expect(!press(frontend, uuid, "j"), "passed Escape invalidates tracked quote before vim navigation")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: passed Escape clears quote tracking, consumed Escape does not\n";
    return true;
}
}

int main() {
    setupTestingEnvironment(FCITX5_MELTYPE_BINARY_DIR,
        {FCITX5_MELTYPE_BINARY_DIR, FCITX5_SYSTEM_ADDON_DIR}, {"test"});
    char arg0[] = "testterminal";
    char arg1[] = "--disable=all";
    char arg2[] = "--enable=testfrontend,testui,testim,meltype";
    char *argv[] = {arg0, arg1, arg2};
    Instance instance(FCITX_ARRAY_SIZE(argv), argv);
    instance.addonManager().registerDefaultLoader(nullptr);
    EventDispatcher dispatcher;
    dispatcher.attach(&instance.eventLoop());
    dispatcher.schedule([&]() {
        auto *frontend = instance.addonManager().addon("testfrontend");
        bool loaded = frontend && instance.addonManager().addon("meltype", true);
        if (loaded) {
            auto group = instance.inputMethodManager().currentGroup();
            group.inputMethodList().clear();
            group.inputMethodList().push_back(InputMethodGroupItem("keyboard-us"));
            group.inputMethodList().push_back(InputMethodGroupItem("meltype"));
            group.setDefaultInputMethod("");
            instance.inputMethodManager().setGroup(group);
            bool ok = quotedJapanese(&instance, frontend);
            if (ok) ok = explicitJapanese(&instance, frontend);
            if (ok) ok = protectedAfterNavigation(&instance, frontend);
            if (ok) ok = resetKeepsJapanese(&instance, frontend);
            if (ok) ok = triggerKeepsProtection(&instance, frontend);
            if (ok) ok = toggleJapanese(&instance, frontend, "Hankaku");
            if (ok) ok = toggleJapanese(&instance, frontend, "Henkan");
            if (ok) ok = directEnglish(&instance, frontend, "Eisu_toggle", true);
            if (ok) ok = directEnglish(&instance, frontend, "Muhenkan", true);
            if (ok) ok = directEnglish(&instance, frontend, "Hankaku", false);
            if (ok) ok = quotedEscapeProtection(&instance, frontend);
            testResult = ok ? 0 : 1;
        } else {
            std::cerr << "FAIL: terminal test addons load\n";
        }
        dispatcher.detach();
        instance.exit();
    });
    const int eventResult = instance.exec();
    return eventResult ? eventResult : testResult;
}
