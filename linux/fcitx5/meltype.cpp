// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype の fcitx5 のアドオン (issue #99)。IBus のエンジン (linux/ibus-engine-meltype) と同じく、キーを Meltype の本体
// (libMeltypeNative.so、標準構成はRust本体) に渡し、返ってきた結果 (確定する文字・変換中の文字・候補)
// を入力欄に出す。本体は dlopen で読むので、ビルドに本体は要らない。
// 旧NativeAOT用のJSON互換解析は -DMELTYPE_RUST_CORE=OFF で明示的に選ぶ。

#include <dlfcn.h>
#ifndef MELTYPE_RUST_CORE
#include "compat/json_reader.hpp"
#include "compat/key_mapping.hpp"
#include "compat/text.hpp"
#endif

#include <cstdint>
#include <cstdlib>
#include <memory>
#include <limits>
#include <string>
#include <vector>

#include <fcitx-utils/capabilityflags.h>
#include <fcitx-utils/key.h>
#include <fcitx-utils/keysym.h>
#include <fcitx-utils/log.h>
#include <fcitx-utils/textformatflags.h>
#include <fcitx/addonfactory.h>
#include <fcitx/addoninstance.h>
#include <fcitx/addonmanager.h>
#include <fcitx/candidatelist.h>
#include <fcitx/event.h>
#include <fcitx/inputcontext.h>
#include <fcitx/inputcontextmanager.h>
#include <fcitx/inputcontextproperty.h>
#include <fcitx/inputmethodengine.h>
#include <fcitx/inputpanel.h>
#include <fcitx/instance.h>
#include <fcitx/text.h>
#include <fcitx/userinterface.h>

#ifndef MELTYPE_DIR
#define MELTYPE_DIR "/opt/meltype"
#endif

namespace {
#ifndef MELTYPE_RUST_CORE
using meltype_fcitx::Json;
#endif

// ---- 本体 (libMeltypeNative.so) ----

struct Native {
    void *handle = nullptr;
#ifdef MELTYPE_RUST_CORE
    struct Surrounding { const uint8_t *before=nullptr;uint32_t beforeLength=0;const uint8_t *after=nullptr;uint32_t afterLength=0; };
    using Query=int (*)(void *,uint32_t,Surrounding *);
    using ViewCallback = int (*)(void *,uint32_t,int,const uint8_t *,uint32_t);
    int (*keyEvent)(void *,uint32_t,uint32_t,uint32_t,int,void *,Query,ViewCallback)=nullptr;
    int (*action)(void *,uint32_t,int,void *,Query,ViewCallback)=nullptr;
    int (*frontendState)(void *) = nullptr;
    void (*setPreedit)(void *,int) = nullptr;
    int (*contextOffsets)(const uint8_t *,uint32_t,uint32_t,uint32_t,uint32_t *) = nullptr;
    int (*deleteCount)(const uint8_t *,uint32_t,uint32_t,const uint8_t *,uint32_t,int) = nullptr;
    void (*setInputAttributes)(void *,uint32_t) = nullptr;
    void (*setCanMoveCaret)(void *,int) = nullptr;
#endif
    int (*initMozc)(const char *, const char *) = nullptr;
    void *(*create)() = nullptr;
    void (*destroy)(void *) = nullptr;
    char *(*handleKey)(void *, int, int, int, const char *, const char *) = nullptr;
    char *(*commit)(void *) = nullptr;
    char *(*resetSession)(void *) = nullptr;
    char *(*selectCandidate)(void *, int) = nullptr;
    void (*setDirect)(void *, int) = nullptr;
    void (*setCanDelete)(void *, int) = nullptr; // 古い本体には無い
    void (*setCodeInput)(void *, int) = nullptr; // 古い本体には無い (コード入力: ターミナルの入力欄で既定にする、#715)
    int (*setApplication)(void *, const char *) = nullptr;
    char *(*roleKey)(void *, int) = nullptr;     // 古い本体には無い (入力中のキーの役割: #199)
    void (*free)(void *) = nullptr;

    bool load() {
        const char *dir = std::getenv("MELTYPE_DIR");
        std::string base = dir && *dir ? dir : MELTYPE_DIR;
        handle = dlopen((base + "/libMeltypeNative.so").c_str(), RTLD_NOW | RTLD_LOCAL);
        if (!handle) {
            FCITX_ERROR() << "Meltype: libMeltypeNative.so を読めません: " << dlerror();
            return false;
        }
        auto sym = [this](const char *name) { return dlsym(handle, name); };
        initMozc = reinterpret_cast<decltype(initMozc)>(sym("meltype_init_mozc"));
        create = reinterpret_cast<decltype(create)>(sym("meltype_create"));
        destroy = reinterpret_cast<decltype(destroy)>(sym("meltype_destroy"));
        handleKey = reinterpret_cast<decltype(handleKey)>(sym("meltype_handle_key"));
        commit = reinterpret_cast<decltype(commit)>(sym("meltype_commit"));
        resetSession = reinterpret_cast<decltype(resetSession)>(sym("meltype_reset"));
        selectCandidate = reinterpret_cast<decltype(selectCandidate)>(sym("meltype_select_candidate"));
        setDirect = reinterpret_cast<decltype(setDirect)>(sym("meltype_set_direct"));
        setCanDelete = reinterpret_cast<decltype(setCanDelete)>(sym("meltype_set_can_delete"));
        setCodeInput = reinterpret_cast<decltype(setCodeInput)>(sym("meltype_set_code_input"));
        setApplication = reinterpret_cast<decltype(setApplication)>(sym("meltype_set_application"));
        roleKey = reinterpret_cast<decltype(roleKey)>(sym("meltype_role_key"));
        free = reinterpret_cast<decltype(free)>(sym("meltype_free"));
#ifdef MELTYPE_RUST_CORE
        keyEvent=reinterpret_cast<decltype(keyEvent)>(sym("meltype_fcitx_key_event"));
        action=reinterpret_cast<decltype(action)>(sym("meltype_fcitx_action"));
        create = reinterpret_cast<decltype(create)>(sym("meltype_fcitx_create"));
        destroy = reinterpret_cast<decltype(destroy)>(sym("meltype_fcitx_destroy"));
        setDirect = reinterpret_cast<decltype(setDirect)>(sym("meltype_fcitx_set_direct"));
        frontendState = reinterpret_cast<decltype(frontendState)>(sym("meltype_fcitx_state"));
        setPreedit = reinterpret_cast<decltype(setPreedit)>(sym("meltype_fcitx_set_preedit"));
        contextOffsets = reinterpret_cast<decltype(contextOffsets)>(sym("meltype_fcitx_context_offsets"));
        deleteCount = reinterpret_cast<decltype(deleteCount)>(sym("meltype_fcitx_delete_count"));
        setInputAttributes = reinterpret_cast<decltype(setInputAttributes)>(sym("meltype_set_input_attributes"));
        setCanMoveCaret = reinterpret_cast<decltype(setCanMoveCaret)>(sym("meltype_set_can_move_caret"));
        if (!keyEvent || !action || !frontendState || !setPreedit || !contextOffsets || !deleteCount || !setInputAttributes || !setCanMoveCaret || !setApplication) {
            FCITX_ERROR() << "Meltype: Rust fcitx event/context ABI is missing";
            return false;
        }
#endif
        if (!create || !destroy || !handleKey || !commit || !selectCandidate || !setDirect || !free) {
            FCITX_ERROR() << "Meltype: libMeltypeNative.so の関数が足りません";
            return false;
        }
        if (initMozc && !initMozc((base + "/mozc/meltype_mozc_helper").c_str(), nullptr)) {
            FCITX_WARN() << "Meltype: Mozc を使えません (漢字変換はひらがなのままになります)";
        }
        return true;
    }

#ifndef MELTYPE_RUST_CORE
    bool readJson(const std::string &text, Json &value) const {
        return meltype_fcitx::JsonReader(text).read(value);
    }

    // 本体が返した JSON を受け取って解放する。NULL なら空 (キーはアプリに渡す)。
    std::string take(char *pointer) const {
        if (!pointer) return {};
        std::string text(pointer);
        free(pointer);
        return text;
    }
#endif
};

// ---- キー: fcitx5 のキー → Meltype の本体が使う Windows の仮想キーコード (ibus-engine-meltype と同じ) ----

class MeltypeEngine;

// 入力欄ごとの本体のセッション
class MeltypeState : public fcitx::InputContextProperty {
public:
    MeltypeState(const Native &native) : native_(native) {
        if (native_.create) session_ = native_.create();
    }
    ~MeltypeState() override {
        if (session_ && native_.destroy) native_.destroy(session_);
    }
    void *session() const { return session_; }
    bool direct() const {
#ifdef MELTYPE_RUST_CORE
        const int flags=native_.frontendState(session_); return flags>=0 && (flags&1)!=0;
#else
        return direct_;
#endif
    }
    void setDirect(bool value) {
#ifndef MELTYPE_RUST_CORE
        direct_=value;
#endif
        native_.setDirect(session_,value?1:0);
    }
    bool preeditVisible() const {
#ifdef MELTYPE_RUST_CORE
        const int flags=native_.frontendState(session_); return flags>=0 && (flags&2)!=0;
#else
        return preeditVisible_;
#endif
    }
    void setPreeditVisible(bool value) {
#ifdef MELTYPE_RUST_CORE
        native_.setPreedit(session_,value?1:0);
#else
        preeditVisible_=value;
#endif
    }
#ifdef MELTYPE_RUST_CORE
    uint32_t privacyAttributes = ~0u;
#endif

private:
    const Native &native_;
    void *session_ = nullptr;
#ifndef MELTYPE_RUST_CORE
    bool direct_=false,preeditVisible_=false;
#endif
};

class MeltypeCandidate : public fcitx::CandidateWord {
public:
    MeltypeCandidate(MeltypeEngine *engine, std::string text, int index)
        : fcitx::CandidateWord(fcitx::Text(std::move(text))), engine_(engine), index_(index) {}
    void select(fcitx::InputContext *ic) const override;

private:
    MeltypeEngine *engine_;
    int index_;
};

class MeltypeEngine : public fcitx::InputMethodEngineV2 {
public:
    explicit MeltypeEngine(fcitx::Instance *instance)
        : instance_(instance), factory_([this](fcitx::InputContext &) { return new MeltypeState(native_); }) {
        loaded_ = native_.load();
        instance_->inputContextManager().registerProperty("meltypeState", &factory_);
    }

    void keyEvent(const fcitx::InputMethodEntry &, fcitx::KeyEvent &event) override {
#ifdef MELTYPE_RUST_CORE
        if(!loaded_ || event.isRelease())return;
        auto *ic=event.inputContext();
        syncContext(ic);
        if (ic->propertyFor(&factory_)->privacyAttributes & 1u) return;
        ViewSink sink{this,ic,{},nullptr,{},{}};
        const auto key=event.key();
        if(native_.keyEvent(ic->propertyFor(&factory_)->session(),key.sym(),fcitx::Key::keySymToUnicode(key.sym()),
            key.states().toInteger(),event.isRelease()?1:0,&sink,queryContext,viewEvent))event.filterAndAccept();
#else
        if (!loaded_ || event.isRelease()) return;
        auto *ic = event.inputContext();
        auto *state = ic->propertyFor(&factory_);
        if (!state->session()) return;
        syncCanDelete(ic, state->session());
        const auto key = event.key();
        const auto sym = key.sym();
        const auto modePlan = meltype_fcitx::modePlan(sym,state->direct());
        // 入力中の 変換・無変換・ひらがな/カタカナ: 設定で役割を分けていれば、本体が変換ボックスで処理する (#199)
        const int roleKey = static_cast<int>((modePlan>>8)&0xffu);
        if (native_.roleKey && roleKey != 0) {
            const int vk = roleKey;
            auto handled = native_.take(native_.roleKey(state->session(), vk));
            Json result;
            if (!handled.empty() && native_.readJson(handled, result)) {
                apply(ic, result, handled);
                event.filterAndAccept();
                return;
            }
        }
        // 半角/全角: 英数 (直接入力) ⇔ 日本語。英数・ひらがなのキーでも切り替える (JIS キーボード)。
        if (modePlan & 1u) {
            const bool toDirect = (modePlan & 2u) != 0;
            apply(ic, native_.take(native_.commit(state->session())));
            state->setDirect(toDirect);
            // 切り替えたモードをカーソル位置に出す (Mozc と同じ入力パネルの枠)
            ic->inputPanel().setAuxUp(fcitx::Text(toDirect ? "A" : "あ"));
            ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
            event.filterAndAccept();
            return;
        }
        auto [vk, character] = meltype_fcitx::virtualKey(sym);
        if (vk == 0) return;
        const auto states = key.states();
        int modifiers = 0;
        if (states.test(fcitx::KeyState::Shift)) modifiers |= 1;
        if (states.test(fcitx::KeyState::Ctrl)) modifiers |= 2;
        if (states.test(fcitx::KeyState::Alt)) modifiers |= 4;
        if (states.test(fcitx::KeyState::Super)) modifiers |= 8;
        std::string before, after;
        bool surrounding = !state->preeditVisible() && readSurrounding(ic, before, after);
        auto text = native_.take(native_.handleKey(state->session(), vk, static_cast<int>(character), modifiers,
                                                   surrounding ? before.c_str() : nullptr, surrounding ? after.c_str() : nullptr));
        if (text.empty()) return;
        Json result;
        if (!native_.readJson(text, result)) return;
        apply(ic, result, text);
        if (result.flag("consumed")) event.filterAndAccept();
#endif

    }

    void activate(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override {
#ifdef MELTYPE_RUST_CORE
        if (loaded_) syncContext(event.inputContext());
#else
        (void)event;
#endif
    }
    void reset(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override {
#ifdef MELTYPE_RUST_CORE
        performAction(event.inputContext(),3);
#else
        commitPending(event.inputContext());
#endif
    }
    void deactivate(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override {
#ifdef MELTYPE_RUST_CORE
        auto *ic=event.inputContext();
        if(event.type()==fcitx::EventType::InputContextFocusOut) {
            // fcitx (or the client) already committed clientPreedit. Server-only
            // preedit still needs one commit, preserving the displayed text.
            performAction(ic,ic->capabilityFlags().test(fcitx::CapabilityFlag::Preedit)?2:4);
        } else { commitPending(ic); }
#else
        commitPending(event.inputContext());
#endif
    }

    void selectCandidate(fcitx::InputContext *ic, int index) {
#ifdef MELTYPE_RUST_CORE
        if(!loaded_)return;
        syncContext(ic);
        ViewSink sink{this,ic,{},nullptr,{},{}};
        native_.action(ic->propertyFor(&factory_)->session(),1,index,&sink,queryContext,viewEvent);
#else
        auto *state = ic->propertyFor(&factory_);
        if (!state->session()) return;
        syncCanDelete(ic, state->session());
        apply(ic, native_.take(native_.selectCandidate(state->session(), index)));
#endif
    }

private:
    fcitx::Instance *instance_;
    Native native_;
    bool loaded_ = false;
    fcitx::FactoryFor<MeltypeState> factory_;
#ifdef MELTYPE_RUST_CORE
    void syncContext(fcitx::InputContext *ic) {
        auto *state=ic->propertyFor(&factory_);
        const auto caps=ic->capabilityFlags();
        // Plasma's Qt lock screen can mark a password as Sensitive without Password.
        const bool password = caps.test(fcitx::CapabilityFlag::Password)
            || (caps.test(fcitx::CapabilityFlag::Sensitive) && ic->program() == "kscreenlocker_greet");
        const uint32_t flags=(password?1u:0u)
            |(caps.test(fcitx::CapabilityFlag::Sensitive)?2u:0u)
            |(caps.test(fcitx::CapabilityFlag::Terminal)?4u:0u);
        if(state->privacyAttributes!=(flags&3u))hidePreedit(ic);
        state->privacyAttributes=flags&3u;
        native_.setApplication(state->session(),ic->program().c_str());
        native_.setInputAttributes(state->session(),flags);
#ifdef MELTYPE_CURSOR_COMMIT
        native_.setCanMoveCaret(state->session(),caps.test(fcitx::CapabilityFlag::CommitStringWithCursor)?1:0);
#else
        native_.setCanMoveCaret(state->session(),0);
#endif
    }
    void performAction(fcitx::InputContext *ic,uint32_t action) {
        if(!loaded_)return;
        syncContext(ic);
        ViewSink sink{this,ic,{},nullptr,{},{}};
        native_.action(ic->propertyFor(&factory_)->session(),action,0,&sink,queryContext,viewEvent);
    }
#endif

    void commitPending(fcitx::InputContext *ic) {
#ifdef MELTYPE_RUST_CORE
        performAction(ic,0);
#else
        auto *state = ic->propertyFor(&factory_);
        if (!state->session()) return;
        syncCanDelete(ic, state->session());
        apply(ic, native_.take(native_.commit(state->session())));
#endif
    }

    // 入力欄のキャレットの前後の文字列 (それぞれ 20 文字まで)。入力欄が対応していなければ false。
    bool readSurrounding(fcitx::InputContext *ic, std::string &before, std::string &after, size_t limit = 20) {
        if (!ic->capabilityFlags().test(fcitx::CapabilityFlag::SurroundingText) || !ic->surroundingText().isValid()
            || ic->surroundingText().cursor()!=ic->surroundingText().anchor()
            || ic->capabilityFlags().testAny(fcitx::CapabilityFlags{fcitx::CapabilityFlag::Password,fcitx::CapabilityFlag::Sensitive})) return false;
        const auto &text = ic->surroundingText().text();
        if (text.empty()) return false;
        size_t cursor = ic->surroundingText().cursor();
#ifdef MELTYPE_RUST_CORE
        if(text.size()>std::numeric_limits<uint32_t>::max() || cursor>std::numeric_limits<uint32_t>::max() || limit>std::numeric_limits<uint32_t>::max())return false;
        uint32_t offsets[3];
        if(!native_.contextOffsets(reinterpret_cast<const uint8_t *>(text.data()),static_cast<uint32_t>(text.size()),static_cast<uint32_t>(cursor),static_cast<uint32_t>(limit),offsets))return false;
        const size_t from=offsets[0],at=offsets[1],to=offsets[2];
#else
        size_t start = cursor > limit ? cursor - limit : 0;
        size_t from = meltype_fcitx::byteOffset(text, start), at = meltype_fcitx::byteOffset(text, cursor), to = meltype_fcitx::byteOffset(text, cursor + 20);
#endif
        before = text.substr(from, at - from);
        after = text.substr(at, to - at);
        return true;
    }

#ifndef MELTYPE_RUST_CORE
    // キャレットの前が expected と一致すると確認できたら、消す文字数 (コードポイント) を返す。確認できないときは 0。
    // 本体の deleteBefore は UTF-16 の単位なので使わず、確認できた文字列から数える (絵文字など、2 単位の文字でもそろえる)。
    static size_t expectedBeforeCaret(fcitx::InputContext *ic, const std::string &expected) {
        if (expected.empty() || !ic->capabilityFlags().test(fcitx::CapabilityFlag::SurroundingText) ||
            !ic->surroundingText().isValid())
            return 0;
        const auto &text = ic->surroundingText().text();
        const size_t cursor = ic->surroundingText().cursor();
        const size_t length = meltype_fcitx::charLength(expected);
        if (cursor < length) return 0;
        const size_t from = meltype_fcitx::byteOffset(text, cursor - length);
        if (text.compare(from, meltype_fcitx::byteOffset(text, cursor) - from, expected) != 0) return 0;
        return length;
    }

    // 確定し直し (DeleteBefore) は、入力欄が周りの文字の削除に対応しているときだけ本体が行う。
    // 本体を呼ぶ前に毎回伝える (入力欄を移った最初のキーで、前の入力欄の値のまま確定し直さないように)。
    void syncCanDelete(fcitx::InputContext *ic, void *session) {
        if (!native_.setCanDelete || !session) return;
        bool canDelete = ic->capabilityFlags().test(fcitx::CapabilityFlag::SurroundingText) && ic->surroundingText().isValid();
        native_.setCanDelete(session, canDelete ? 1 : 0);
        // 入力欄がターミナル (capability flag の Terminal) なら、コード入力を既定にする (j k やコマンド名がアプリに届くように)
        const int applicationCode = native_.setApplication ? native_.setApplication(session, ic->program().c_str()) : 0;
        if (native_.setCodeInput) native_.setCodeInput(session, applicationCode == 1 || ic->capabilityFlags().test(fcitx::CapabilityFlag::Terminal) ? 1 : 0);
    }

    void apply(fcitx::InputContext *ic, const std::string &text) {
        if (text.empty()) return;
        Json result;
        if (native_.readJson(text, result)) apply(ic, result, text);
    }

    void apply(fcitx::InputContext *ic, [[maybe_unused]] const Json &result, [[maybe_unused]] const std::string &raw) {
        if (auto *commits = result.get("commits"); commits && commits->kind == Json::Kind::Array) {
            for (const auto &edit : commits->items) {
                int remove = edit.integer("deleteBefore");
                // 確定し直し: 消す文字が今もキャレットの前にあると確認できたときだけ消し、今打った文字を入れる。
                // 確認できない (入力欄が周りの文字に対応していない・確定したときと変わった) ときは、前の文字は
                // 消さずに今打った文字だけを入れる (本体は text が入った前提で状態を進めるので、text を捨てない)。
                const size_t length = remove > 0 ? expectedBeforeCaret(ic, edit.str("expect")) : 0;
                if (length > 0) {
                    hidePreedit(ic);
                    ic->deleteSurroundingText(-static_cast<int>(length), static_cast<unsigned int>(length));
                } else {
                    if (remove > 0) FCITX_WARN() << "Meltype: 確定し直す文字を確認できないため、前の文字を消しません";
                    ic->inputPanel().reset();
                    ic->propertyFor(&factory_)->setPreeditVisible(false);
                }
                auto committed = edit.str("text");
                if (!committed.empty()) ic->commitString(committed);
            }
        }
        auto *view = result.get("view");
        if (view && view->kind == Json::Kind::Object) show(ic, *view);
        else hidePreedit(ic);
    }

    void show(fcitx::InputContext *ic, const Json &view) {
        auto &panel = ic->inputPanel();
        panel.reset();
        const auto text = view.str("text");
        const auto clauses = view.strings("clauses");
        const bool converting = view.flag("converting");
        const int selectedClause = view.integer("selectedClause", -1);
        fcitx::Text preedit;
        if (converting && !clauses.empty()) {
            // 変換中: 文節ごとに下線。選んでいる文節は強調する。
            for (size_t i = 0; i < clauses.size(); i++) {
                fcitx::TextFormatFlags format = fcitx::TextFormatFlag::Underline;
                if (static_cast<int>(i) == selectedClause) format |= fcitx::TextFormatFlag::HighLight;
                preedit.append(clauses[i], format);
            }
        } else {
            preedit.append(text, fcitx::TextFormatFlag::Underline);
        }
        preedit.setCursor(static_cast<int>(preedit.toString().size()));
        if (ic->capabilityFlags().test(fcitx::CapabilityFlag::Preedit)) panel.setClientPreedit(preedit);
        else panel.setPreedit(preedit);
        ic->propertyFor(&factory_)->setPreeditVisible(!text.empty());
        // 案内 (Space で変換 など) と もしかして は補助テキストに出す
        auto hint = view.str("hint");
        if (auto suggestion = view.str("suggestion"); !suggestion.empty()) hint = suggestion + "　" + hint;
        if (!hint.empty()) panel.setAuxDown(fcitx::Text(hint));
        const auto candidates = view.strings("candidates");
        if (converting && candidates.size() > 1) {
            auto list = std::make_unique<fcitx::CommonCandidateList>();
            list->setPageSize(9);
            list->setLabels({"1. ", "2. ", "3. ", "4. ", "5. ", "6. ", "7. ", "8. ", "9. "});
            list->setCursorPositionAfterPaging(fcitx::CursorPositionAfterPaging::DonotChange);
            for (size_t i = 0; i < candidates.size(); i++) list->append<MeltypeCandidate>(this, candidates[i], static_cast<int>(i));
            int selected = view.integer("selectedIndex", 0);
            if (selected >= 0 && selected < static_cast<int>(candidates.size())) list->setGlobalCursorIndex(selected);
            panel.setCandidateList(std::move(list));
        }
        ic->updatePreedit();
        ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
    }

#else
    struct ViewSink {
        MeltypeEngine *engine;
        fcitx::InputContext *ic;
        fcitx::Text preedit;
        std::unique_ptr<fcitx::CommonCandidateList> candidates;
        std::string before,after;
        std::string deleteExpected{};
        int deleteUnits=0;
    };
    static int queryContext(void *opaque,uint32_t query,Native::Surrounding *output) noexcept {
        try {
            auto &sink=*static_cast<ViewSink *>(opaque);
            auto *ic=sink.ic;
            if(query==0)return ic->capabilityFlags().test(fcitx::CapabilityFlag::SurroundingText)&&ic->surroundingText().isValid()
                &&ic->surroundingText().cursor()==ic->surroundingText().anchor()
                &&!ic->capabilityFlags().testAny(fcitx::CapabilityFlags{fcitx::CapabilityFlag::Password,fcitx::CapabilityFlag::Sensitive})?1:0;
            if(query!=1 || !sink.engine->readSurrounding(ic,sink.before,sink.after))return 0;
            if(sink.before.size()>std::numeric_limits<uint32_t>::max() || sink.after.size()>std::numeric_limits<uint32_t>::max())return 0;
            *output={reinterpret_cast<const uint8_t *>(sink.before.data()),static_cast<uint32_t>(sink.before.size()),
                reinterpret_cast<const uint8_t *>(sink.after.data()),static_cast<uint32_t>(sink.after.size())};
            return 1;
        } catch(...) { return -1; }
    }
    static int viewEvent(void *opaque,uint32_t operation,int argument,const uint8_t *bytes,uint32_t length) noexcept {
        try {
            auto &sink=*static_cast<ViewSink *>(opaque);
            auto *ic=sink.ic;
            auto &panel=ic->inputPanel();
            const std::string text(reinterpret_cast<const char *>(bytes),length);
            switch(operation) {
            case 0: panel.reset(); break;
            case 1: {
                fcitx::TextFormatFlags format;
                if(argument&1)format|=fcitx::TextFormatFlag::Underline;
                if(argument&2)format|=fcitx::TextFormatFlag::HighLight;
                sink.preedit.append(text,format); break;
            }
            case 2:
                sink.preedit.setCursor(static_cast<int>(sink.preedit.toString().size()));
                if(ic->capabilityFlags().test(fcitx::CapabilityFlag::Preedit))panel.setClientPreedit(sink.preedit);
                else panel.setPreedit(sink.preedit);
                ic->propertyFor(&sink.engine->factory_)->setPreeditVisible(argument!=0); break;
            case 3: panel.setAuxDown(fcitx::Text(text)); break;
            case 4:
                sink.candidates=std::make_unique<fcitx::CommonCandidateList>();
                sink.candidates->setPageSize(argument);
                sink.candidates->setLabels({"1. ","2. ","3. ","4. ","5. ","6. ","7. ","8. ","9. "});
                sink.candidates->setCursorPositionAfterPaging(fcitx::CursorPositionAfterPaging::DonotChange); break;
            case 5: sink.candidates->append<MeltypeCandidate>(sink.engine,text,argument); break;
            case 6:
                if(argument>=0)sink.candidates->setGlobalCursorIndex(argument);
                panel.setCandidateList(std::move(sink.candidates)); break;
            case 7:
                ic->updatePreedit();
                ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel); break;
            case 8: sink.engine->hidePreedit(ic); break;
            case 9: {
                if(!ic->capabilityFlags().test(fcitx::CapabilityFlag::SurroundingText) || !ic->surroundingText().isValid())return 0;
                if(ic->surroundingText().cursor()!=ic->surroundingText().anchor()
                    || ic->capabilityFlags().testAny(fcitx::CapabilityFlags{fcitx::CapabilityFlag::Password,fcitx::CapabilityFlag::Sensitive}))return 0;
                const auto &surrounding=ic->surroundingText().text();
                if(surrounding.size()>std::numeric_limits<uint32_t>::max())return 0;
                sink.deleteExpected=text;
                sink.deleteUnits=argument;
                return sink.engine->native_.deleteCount(reinterpret_cast<const uint8_t *>(surrounding.data()),static_cast<uint32_t>(surrounding.size()),
                    ic->surroundingText().cursor(),bytes,length,argument);
            }
            case 10: {
                // パネルを閉じる通知で入力欄が変わる場合も、削除直前に確認する。
                if(argument<=0 || !ic->capabilityFlags().test(fcitx::CapabilityFlag::SurroundingText)
                    || !ic->surroundingText().isValid() || ic->surroundingText().cursor()!=ic->surroundingText().anchor()
                    || ic->capabilityFlags().testAny(fcitx::CapabilityFlags{fcitx::CapabilityFlag::Password,fcitx::CapabilityFlag::Sensitive}))return 0;
                const auto &surrounding=ic->surroundingText().text();
                if(surrounding.size()>std::numeric_limits<uint32_t>::max()
                    || sink.engine->native_.deleteCount(reinterpret_cast<const uint8_t *>(surrounding.data()),static_cast<uint32_t>(surrounding.size()),
                        ic->surroundingText().cursor(),reinterpret_cast<const uint8_t *>(sink.deleteExpected.data()),
                        static_cast<uint32_t>(sink.deleteExpected.size()),sink.deleteUnits)!=argument)return 0;
                ic->deleteSurroundingText(-argument,static_cast<unsigned int>(argument)); break;
            }
            case 11: ic->commitString(text); break;
            case 15:
#ifdef MELTYPE_CURSOR_COMMIT
                if(argument!=1 || !ic->capabilityFlags().test(fcitx::CapabilityFlag::CommitStringWithCursor))return 0;
                ic->commitStringWithCursor(text,1); break;
#else
                return 0;
#endif
            case 14:
                panel.setAuxUp(fcitx::Text(text));
                ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel); break;
            case 12:
                panel.reset();
                ic->propertyFor(&sink.engine->factory_)->setPreeditVisible(false); break;
            default: return 0;
            }
            return 1;
        } catch(...) { return -1; }
    }

#endif
    void hidePreedit(fcitx::InputContext *ic) {
        ic->inputPanel().reset();
        ic->updatePreedit();
        ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
        ic->propertyFor(&factory_)->setPreeditVisible(false);
    }
};

void MeltypeCandidate::select(fcitx::InputContext *ic) const { engine_->selectCandidate(ic, index_); }

class MeltypeEngineFactory : public fcitx::AddonFactory {
    fcitx::AddonInstance *create(fcitx::AddonManager *manager) override { return new MeltypeEngine(manager->instance()); }
};

} // namespace

FCITX_ADDON_FACTORY(MeltypeEngineFactory);
