// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// fcitx5 のアドオンを、fcitx5 のテスト用のフロントエンド (testfrontend) で動かして確かめる。
// 本体 (libMeltypeNative.so) の場所を環境変数 MELTYPE_DIR で渡す (Mozc が無ければ、変換はひらがなのまま)。
//   cmake -S linux/fcitx5 -B build -DBUILD_TESTING=ON && cmake --build build && MELTYPE_DIR=… ctest --test-dir build

#include <testfrontend_public.h>
#include <fcitx-utils/eventdispatcher.h>
#include <fcitx-utils/log.h>
#include <fcitx-utils/stringutils.h>
#include <fcitx-utils/testing.h>
#include <fcitx/addonmanager.h>
#include <fcitx/inputcontextmanager.h>
#include <fcitx/inputmethodgroup.h>
#include <fcitx/inputmethodmanager.h>
#include <fcitx/inputpanel.h>
#include <fcitx/candidatelist.h>
#include <fcitx/inputmethodengine.h>
#include <fcitx/inputmethodentry.h>
#include <fcitx/instance.h>

using namespace fcitx;

namespace {

void typeKeys(AddonInstance *frontend, ICUUID uuid, const std::string &keys) {
    for (char c : keys) {
        std::string name = c == ' ' ? "space" : c == '\n' ? "Return" : std::string(1, c);
        frontend->call<ITestFrontend::keyEvent>(uuid, Key(name), false);
        frontend->call<ITestFrontend::keyEvent>(uuid, Key(name), true);
    }
}

void scheduleEvent(EventDispatcher *dispatcher, Instance *instance) {
    dispatcher->schedule([dispatcher, instance]() {
        auto *frontend = instance->addonManager().addon("testfrontend");
        FCITX_ASSERT(instance->addonManager().addon("meltype", true)) << "Meltype のアドオンを読めません";
        // fcitx5 の設定ツールで Meltype の設定を開くと、設定画面 (meltype-settings) を起動する
        const auto *entry = instance->inputMethodManager().entry("meltype");
        FCITX_ASSERT(entry && entry->isConfigurable());
        RawConfig description;
        instance->inputMethodEngine("meltype")->getConfigForInputMethod(*entry)->dumpDescription(description);
        FCITX_ASSERT(description.valueByPath("MeltypeConfig/Settings/Type") &&
                     *description.valueByPath("MeltypeConfig/Settings/Type") == "External");
        const auto *command = description.valueByPath("MeltypeConfig/Settings/External");
        FCITX_ASSERT(command && stringutils::endsWith(*command, "/meltype-settings")) << (command ? *command : "");
        auto group = instance->inputMethodManager().currentGroup();
        group.inputMethodList().clear();
        group.inputMethodList().push_back(InputMethodGroupItem("keyboard-us"));
        group.inputMethodList().push_back(InputMethodGroupItem("meltype"));
        group.setDefaultInputMethod("");
        instance->inputMethodManager().setGroup(group);
        auto uuid = frontend->call<ITestFrontend::createInputContext>("testapp");
        auto *ic = instance->inputContextManager().findByUUID(uuid);
        FCITX_ASSERT(ic);
        instance->setCurrentInputMethod(ic, "meltype", true);
        FCITX_ASSERT(instance->inputMethod(ic) == "meltype");

        // ローマ字の日本語 (Mozc が無いのでひらがなのまま確定)
        frontend->call<ITestFrontend::pushCommitExpectation>("にほんご");
        typeKeys(frontend, uuid, "nihongo\n");
        // 英単語は英字のまま、Space で確定して空白
        frontend->call<ITestFrontend::pushCommitExpectation>("hello ");
        typeKeys(frontend, uuid, "hello ");
        // 日本語の中の英単語
        frontend->call<ITestFrontend::pushCommitExpectation>("きょうはgoogleでけんさく");
        typeKeys(frontend, uuid, "kyouhagoogledekensaku\n");
        // Enter で、変換せずにかなのまま確定
        frontend->call<ITestFrontend::pushCommitExpectation>("かわ");
        typeKeys(frontend, uuid, "kawa\n");
        // Space で変換すると、候補の一覧が出る (確定する候補は Mozc の有無で変わるので、一覧が出ることだけを見る)
        typeKeys(frontend, uuid, "kawa ");
        auto list = ic->inputPanel().candidateList();
        FCITX_ASSERT(list && list->size() > 1) << "変換の候補の一覧が出ない";
        // 変換中の文字 (入力欄が preedit に対応していなければ、fcitx5 の画面の preedit) は、選んでいる候補
        auto preedit = ic->capabilityFlags().test(CapabilityFlag::Preedit) ? ic->inputPanel().clientPreedit() : ic->inputPanel().preedit();
        FCITX_ASSERT(preedit.toString() == list->candidate(list->cursorIndex()).text().toString()) << preedit.toString();
        frontend->call<ITestFrontend::pushCommitExpectation>(preedit.toString());
        typeKeys(frontend, uuid, "\n");
        FCITX_ASSERT(!ic->inputPanel().candidateList()) << "確定したら候補の一覧を閉じる";

        dispatcher->schedule([dispatcher, instance]() {
            dispatcher->detach();
            instance->exit();
        });
    });
}

} // namespace

int main() {
    setupTestingEnvironment(FCITX5_MELTYPE_BINARY_DIR, {FCITX5_MELTYPE_BINARY_DIR, FCITX5_SYSTEM_ADDON_DIR}, {"test"});
    char arg0[] = "testmeltype";
    char arg1[] = "--disable=all";
    char arg2[] = "--enable=testfrontend,testui,testim,keyboard,meltype";
    char *argv[] = {arg0, arg1, arg2};
    fcitx::Log::setLogRule("default=5");
    Instance instance(FCITX_ARRAY_SIZE(argv), argv);
    instance.addonManager().registerDefaultLoader(nullptr);
    EventDispatcher dispatcher;
    dispatcher.attach(&instance.eventLoop());
    scheduleEvent(&dispatcher, &instance);
    return instance.exec();
}
