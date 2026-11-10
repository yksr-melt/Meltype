// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MuNeNiCK
// Copyright (C) 2026 aster-void
// Exercise terminal composition through Fcitx5's public frontend interface.
// コードの行かは、入力欄が渡す周りの文字 (surrounding text) だけで決まる。渡さない端末 (foot の TUI など) では、
// コードの行として英数のまま通し、日本語にするのは 半角/全角 などで明示したときだけ。
#include <testfrontend_public.h>
#include <fcitx-utils/eventdispatcher.h>
#include <fcitx-utils/testing.h>
#include <fcitx/addonmanager.h>
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
int testResult = 1;

// 実際のユーザー設定から隔離し、変換 / 無変換 でモードを切り替える設定にする
class SettingsFixture {
public:
    bool create() {
        char path[] = "/tmp/meltype-terminal-XXXXXX";
        const auto *created = mkdtemp(path);
        if (!created) return false;
        directory_ = created;
        std::ofstream config(directory_ + "/config.json");
        config << R"({"LiveConversion":false,"LearningEnabled":false})";
        return config.good() && setenv("MELTYPE_DATA_DIR", directory_.c_str(), 1) == 0;
    }

    ~SettingsFixture() {
        if (!directory_.empty()) {
            std::error_code error;
            std::filesystem::remove_all(directory_, error);
        }
    }

private:
    std::string directory_;
};

bool press(AddonInstance *frontend, ICUUID uuid, const std::string &name) {
    bool consumed = frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key(name), false);
    frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key(name), true);
    return consumed;
}

std::string keyName(char c) { return c == ' ' ? "space" : c == '\n' ? "Return" : c == '"' ? "quotedbl" : std::string(1, c); }

void type(AddonInstance *frontend, ICUUID uuid, const std::string &text) {
    for (char c : text) press(frontend, uuid, keyName(c));
}

// どのキーもアプリに通る (変換中の文字を作らない)
bool passes(AddonInstance *frontend, ICUUID uuid, const std::string &text) {
    for (char c : text)
        if (press(frontend, uuid, keyName(c))) return false;
    return true;
}

bool expect(bool condition, const std::string &message) {
    if (!condition) std::cerr << "FAIL: " << message << '\n';
    return condition;
}

InputContext *terminal(Instance *instance, AddonInstance *frontend, ICUUID &uuid, bool terminalInput = true) {
    uuid = frontend->call<ITestFrontend::createInputContext>("foot");
    auto *ic = instance->inputContextManager().findByUUID(uuid);
    if (!ic) return nullptr;
    CapabilityFlags flags(CapabilityFlag::ClientUnfocusCommit);
    if (terminalInput) flags |= CapabilityFlag::Terminal;
    // foot advertises surrounding text, but a TUI may not supply valid text.
    flags |= CapabilityFlag::SurroundingText;
    ic->setCapabilityFlags(flags);
    instance->setCurrentInputMethod(ic, "meltype", true);
    return ic;
}

std::string preedit(InputContext *ic) { return ic->inputPanel().preedit().toString(); }

// 明示的に日本語にして打つと変換中の文字になり、Enter で確定する
bool composes(AddonInstance *frontend, ICUUID uuid, InputContext *ic, const std::string &message) {
    type(frontend, uuid, "kyouha");
    if (!expect(preedit(ic) == "きょうは", message + ": " + preedit(ic))) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    press(frontend, uuid, "Return");
    return true;
}

bool explicitJapaneseWithoutSurrounding(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "terminal input context exists")) return false;
    if (!expect(passes(frontend, uuid, "j"), "ordinary terminal keys pass through")) return false;
    // キャレットの前が読めなくても、本体は通したキーと確定から今の行を組み立てる (引用符の中は日本語、閉じたら英数、Enter で次の行)
    if (!expect(passes(frontend, uuid, "echo \""), "code keys and the opening quote pass through")) return false;
    if (!composes(frontend, uuid, ic, "without surrounding text, the quote typed on this line is followed")) return false;
    if (!expect(passes(frontend, uuid, "\""), "the closing quote passes through")) return false;
    if (!expect(!press(frontend, uuid, "Return"), "Enter after the closed quote passes through")) return false;
    if (!expect(passes(frontend, uuid, "kyouha"), "a new line is code again")) return false;
    if (!expect(press(frontend, uuid, "Hankaku"), "half/full is consumed")) return false;
    if (!composes(frontend, uuid, ic, "half/full selects Japanese on the code line")) return false;
    if (!composes(frontend, uuid, ic, "Japanese survives commit")) return false;
    ic->reset();
    if (!composes(frontend, uuid, ic, "client reset keeps explicit Japanese")) return false;
    ic->focusOut();
    ic->focusIn();
    if (!expect(passes(frontend, uuid, "kyouha"), "focus change restores code keys")) return false;
    if (!expect(press(frontend, uuid, "Hankaku"), "half/full is consumed again")) return false;
    if (!composes(frontend, uuid, ic, "half/full selects Japanese again")) return false;
    if (!expect(press(frontend, uuid, "Hankaku"), "half/full toggles back")) return false;
    if (!expect(passes(frontend, uuid, "kyouha"), "toggling back keeps the IME open on code keys")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: without surrounding text, terminal Japanese is selected explicitly\n";
    return true;
}

bool quotesFromSurroundingText(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "surrounding text input context exists")) return false;
    auto setSurrounding = [ic](const std::string &text, unsigned int cursor) {
        ic->surroundingText().setText(text, cursor, cursor);
        ic->updateSurroundingText();
    };
    setSurrounding("echo ", 5);
    if (!expect(passes(frontend, uuid, "\""), "the opening quote reaches the terminal")) return false;
    setSurrounding("echo \"", 6);
    type(frontend, uuid, "kyouha");
    if (!expect(preedit(ic) == "きょうは", "text inside the quote composes: " + preedit(ic))) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("きょうは");
    if (!expect(!press(frontend, uuid, "quotedbl"), "the closing quote commits and reaches the terminal")) return false;
    if (!expect(ic->inputPanel().preedit().empty(), "the closing quote ends the composition")) return false;
    setSurrounding("echo \"きょうは\"", 11);
    if (!expect(passes(frontend, uuid, "k"), "code keys pass after the closing quote")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: quotes are decided by the surrounding text\n";
    return true;
}

bool navigationReturnsToCode(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "navigation input context exists")) return false;
    press(frontend, uuid, "Hankaku");
    type(frontend, uuid, "kyouha");
    if (!expect(press(frontend, uuid, "Escape"), "Escape first cancels the composition")) return false;
    if (!composes(frontend, uuid, ic, "consumed Escape keeps Japanese")) return false;
    for (const auto &key : {"Escape", "Left", "Tab", "Control+a", "Control+Shift+space"}) {
        if (!expect(!press(frontend, uuid, key), std::string("navigation reaches app: ") + key)) return false;
        if (!expect(passes(frontend, uuid, "j"), std::string("code keys resume after ") + key)) return false;
        press(frontend, uuid, "Hankaku");
        if (!composes(frontend, uuid, ic, std::string("Japanese can be selected again after ") + key)) return false;
    }
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: only passed navigation returns to code keys\n";
    return true;
}

bool shiftSpaceInsertsFullWidthSpace(Instance *instance, AddonInstance *frontend) {
    // 設定「入力モード切り替えキー」に Shift+Space を登録していない既定の状態で、日本語入力の
    // Shift+Space が本体に消費され、全角スペースが確定することを確かめる (#1034)。
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "shift space input context exists")) return false;
    press(frontend, uuid, "Hankaku");
    frontend->call<ITestFrontend::pushCommitExpectation>("　");
    if (!expect(press(frontend, uuid, "Shift+space"), "Shift+space in Japanese mode is consumed by the core")) return false;
    if (!expect(ic->inputPanel().preedit().empty(), "Shift+space commits without a composition")) return false;
    type(frontend, uuid, "a");
    if (!expect(preedit(ic) == "あ", "Shift+space did not toggle the mode")) return false;
    frontend->call<ITestFrontend::pushCommitExpectation>("あ");
    press(frontend, uuid, "Return");
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: Shift+space in Japanese mode commits a full-width space without a toggle shortcut\n";
    return true;
}

bool triggerKeepsCode(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid);
    if (!expect(ic != nullptr, "trigger input context exists")) return false;
    press(frontend, uuid, "Hankaku");
    if (!expect(press(frontend, uuid, "Control+space"), "Fcitx trigger disables current IM")) return false;
    frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key("Control_L"), true);
    if (!expect(press(frontend, uuid, "Control+space"), "Fcitx trigger enables Meltype")) return false;
    frontend->call<ITestFrontend::sendKeyEvent>(uuid, Key("Control_L"), true);
    if (!expect(instance->inputMethod(ic) == "meltype", "Meltype is selected after the Fcitx trigger")) return false;
    if (!expect(passes(frontend, uuid, "kyouha"), "reactivation forgets the Japanese code line")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: Fcitx reactivation returns the terminal to code keys\n";
    return true;
}

bool modeKeys(Instance *instance, AddonInstance *frontend, const std::string &englishKey, bool terminalInput) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid, terminalInput);
    if (!expect(ic != nullptr, "mode key input context exists")) return false;
    if (!expect(press(frontend, uuid, "Henkan"), "Henkan selects Japanese with the setting")) return false;
    if (!composes(frontend, uuid, ic, "Henkan selects Japanese")) return false;
    if (!expect(press(frontend, uuid, englishKey), "explicit English mode key is consumed")) return false;
    if (!expect(passes(frontend, uuid, "echo \"kyouha"), englishKey + " enters direct English")) return false;
    if (!expect(press(frontend, uuid, "Hiragana_Katakana"), "Hiragana/Katakana is consumed")) return false;
    if (!composes(frontend, uuid, ic, "Hiragana/Katakana selects Japanese")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: " << englishKey << " enters direct English " << (terminalInput ? "in a terminal" : "outside a terminal") << '\n';
    return true;
}
}

// 入力欄に出した変換中の文字は、フォーカスが外れたときにクライアントが表示のまま確定する。Meltype は確定し直さない
bool focusOutLeavesClientPreedit(Instance *instance, AddonInstance *frontend) {
    // foot は既定で「コード」のアプリなので、文章を書くアプリにする
    ICUUID uuid = frontend->call<ITestFrontend::createInputContext>("gedit");
    auto *ic = instance->inputContextManager().findByUUID(uuid);
    if (!expect(ic != nullptr, "client preedit input context exists")) return false;
    ic->setCapabilityFlags(CapabilityFlags{CapabilityFlag::ClientUnfocusCommit, CapabilityFlag::Preedit});
    instance->setCurrentInputMethod(ic, "meltype", true);
    type(frontend, uuid, "kan");
    if (!expect(ic->inputPanel().clientPreedit().toString() == "かん", "client preedit shows what will be committed: " + ic->inputPanel().clientPreedit().toString())) return false;
    // ここで Meltype が確定すると、testfrontend が期待していない確定として失敗する
    ic->focusOut();
    ic->focusIn();
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: focus out leaves the client preedit to the client\n";
    return true;
}

// パスワード欄では何も受け持たない
bool passwordPasses(Instance *instance, AddonInstance *frontend) {
    ICUUID uuid;
    auto *ic = terminal(instance, frontend, uuid, false);
    if (!expect(ic != nullptr, "password input context exists")) return false;
    ic->setCapabilityFlags(ic->capabilityFlags() | CapabilityFlag::Password);
    if (!expect(passes(frontend, uuid, "kyouha"), "password keys pass through")) return false;
    frontend->call<ITestFrontend::destroyInputContext>(uuid);
    std::cout << "PASS: password fields pass through\n";
    return true;
}

int main() {
    SettingsFixture settings;
    if (!expect(settings.create(), "test settings are created")) return 1;
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
            bool ok = explicitJapaneseWithoutSurrounding(&instance, frontend);
            if (ok) ok = quotesFromSurroundingText(&instance, frontend);
            if (ok) ok = navigationReturnsToCode(&instance, frontend);
            if (ok) ok = shiftSpaceInsertsFullWidthSpace(&instance, frontend);
            if (ok) ok = triggerKeepsCode(&instance, frontend);
            if (ok) ok = modeKeys(&instance, frontend, "Eisu_toggle", true);
            if (ok) ok = modeKeys(&instance, frontend, "Muhenkan", true);
            if (ok) ok = modeKeys(&instance, frontend, "Muhenkan", false);
            if (ok) ok = passwordPasses(&instance, frontend);
            if (ok) ok = focusOutLeavesClientPreedit(&instance, frontend);
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
