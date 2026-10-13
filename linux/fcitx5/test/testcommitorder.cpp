// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 sahenjp
//
// 確定 (issue #486) で、変換中の文字を消すだけの更新が確定より先に送られないことを確かめる。
// Wayland の text-input-v3 では、preedit の消去と commit が別の done になると、Chromium 系の
// アプリが 1 つ目の done を確定と受け取って、同じ文字が二重に入る。
// ここでは入力欄の代わりに、アドオンから来た呼び出し (確定・変換中の文字の更新・周りの文字の削除) を
// 順番に記録する入力欄を使って、確定のときに最初に来るのが commit であることを見る。
//
// 本体 (libMeltypeNative.so) の場所を環境変数 MELTYPE_DIR で渡す。
//   cmake -S linux/fcitx5 -B build -DBUILD_TESTING=ON && cmake --build build && MELTYPE_DIR=… ctest --test-dir build

#include <fcitx-utils/eventdispatcher.h>
#include <fcitx-utils/key.h>
#include <fcitx-utils/log.h>
#include <fcitx-utils/testing.h>
#include <fcitx/addonmanager.h>
#include <fcitx/event.h>
#include <fcitx/inputcontext.h>
#include <fcitx/inputcontextmanager.h>
#include <fcitx/inputmethodgroup.h>
#include <fcitx/inputmethodmanager.h>
#include <fcitx/inputpanel.h>
#include <fcitx/instance.h>

#include <string>
#include <vector>

using namespace fcitx;

namespace {

/// アドオンからの呼び出しを、来た順に記録するだけの入力欄。
class RecordingInputContext : public InputContext {
public:
    RecordingInputContext(InputContextManager &manager, const std::string &program)
        : InputContext(manager, program) {
        created();
    }
    ~RecordingInputContext() override { destroy(); }

    const char *frontend() const override { return "recording"; }

    const std::vector<std::string> &events() const { return events_; }
    void clearEvents() { events_.clear(); }
    const std::string &committed() const { return committed_; }

    void commitStringImpl(const std::string &text) override {
        events_.push_back("commit");
        committed_ += text;
    }
    void deleteSurroundingTextImpl(int, unsigned int) override { events_.push_back("delete"); }
    void forwardKeyImpl(const ForwardKeyEvent &) override {}
    void updatePreeditImpl() override { events_.push_back("preedit"); }

private:
    std::vector<std::string> events_;
    std::string committed_;
};

std::string join(const std::vector<std::string> &events) {
    std::string text;
    for (const auto &event : events) text += (text.empty() ? "" : " ") + event;
    return text;
}

void check(Instance *instance) {
    FCITX_ASSERT(instance->addonManager().addon("meltype", true)) << "Meltype のアドオンを読めません";
    auto group = instance->inputMethodManager().currentGroup();
    group.inputMethodList().clear();
    group.inputMethodList().push_back(InputMethodGroupItem("meltype"));
    group.setDefaultInputMethod("");
    instance->inputMethodManager().setGroup(group);

    RecordingInputContext ic(instance->inputContextManager(), "testapp");
    // 入力欄が変換中の文字を自分で出す (Wayland の text-input-v3 と同じ) 前提で見る。
    ic.setCapabilityFlags(CapabilityFlags{CapabilityFlag::Preedit});
    instance->setCurrentInputMethod(&ic, "meltype", true);
    FCITX_ASSERT(instance->inputMethod(&ic) == "meltype");
    ic.focusIn();

    auto press = [&ic](const std::string &name) {
        KeyEvent event(&ic, Key(name), false);
        ic.keyEvent(event);
    };

    // ローマ字を打って、確定する (Mozc が無ければ、かなのまま確定する)
    for (const char *key : {"k", "a", "w", "a"}) press(key);
    FCITX_ASSERT(!ic.inputPanel().clientPreedit().toString().empty()) << "打った文字が入力欄に出ない";

    ic.clearEvents();
    press("Return");

    // 確定より先に来るものが無いこと。ここに preedit が入ると、Wayland では
    // 「消すだけの done」が確定として扱われ、確定の文字が二重に入る (#486)。
    const auto &events = ic.events();
    FCITX_ASSERT(!events.empty() && events.front() == "commit")
        << "確定より先に別の更新が送られています (" << join(events) << ")";
    FCITX_ASSERT(!ic.committed().empty()) << "確定した文字がありません (MELTYPE_DIR は合っていますか)";
    FCITX_ASSERT(ic.inputPanel().clientPreedit().toString().empty()) << "確定した後も入力欄に文字が残っています";
    FCITX_INFO() << "確定のときに送られたもの: " << join(events) << " / 確定した文字: " << ic.committed();
}

void scheduleEvent(EventDispatcher *dispatcher, Instance *instance) {
    dispatcher->schedule([dispatcher, instance]() {
        check(instance);
        dispatcher->schedule([dispatcher, instance]() {
            dispatcher->detach();
            instance->exit();
        });
    });
}

} // namespace

int main() {
    setupTestingEnvironment(FCITX5_MELTYPE_BINARY_DIR, {FCITX5_MELTYPE_BINARY_DIR, FCITX5_SYSTEM_ADDON_DIR}, {"test"});
    char arg0[] = "testcommitorder";
    char arg1[] = "--disable=all";
    char arg2[] = "--enable=testim,keyboard,meltype";
    char *argv[] = {arg0, arg1, arg2};
    fcitx::Log::setLogRule("default=5");
    Instance instance(FCITX_ARRAY_SIZE(argv), argv);
    instance.addonManager().registerDefaultLoader(nullptr);
    EventDispatcher dispatcher;
    dispatcher.attach(&instance.eventLoop());
    scheduleEvent(&dispatcher, &instance);
    return instance.exec();
}
