// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype の fcitx5 のアドオン (issue #99)。IBus のエンジン (linux/ibus-engine-meltype) と同じく、キーを Meltype の本体
// (libMeltypeNative.so、Windows 版と共通の C# の部分を NativeAOT にしたもの) に渡し、返ってきた結果 (確定する文字・変換中の文字・候補)
// を入力欄に出す。本体は dlopen で読むので、ビルドに本体は要らない。

#include <dlfcn.h>

#include <cstdint>
#include <cstdlib>
#include <memory>
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

// ---- 本体 (libMeltypeNative.so) ----

struct Native {
    void *handle = nullptr;
    int (*initMozc)(const char *, const char *) = nullptr;
    void *(*create)() = nullptr;
    void (*destroy)(void *) = nullptr;
    char *(*handleKey)(void *, int, int, int, const char *, const char *) = nullptr;
    char *(*commit)(void *) = nullptr;
    char *(*selectCandidate)(void *, int) = nullptr;
    void (*setDirect)(void *, int) = nullptr;
    void (*setCanDelete)(void *, int) = nullptr; // 古い本体には無い
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
        selectCandidate = reinterpret_cast<decltype(selectCandidate)>(sym("meltype_select_candidate"));
        setDirect = reinterpret_cast<decltype(setDirect)>(sym("meltype_set_direct"));
        setCanDelete = reinterpret_cast<decltype(setCanDelete)>(sym("meltype_set_can_delete"));
        roleKey = reinterpret_cast<decltype(roleKey)>(sym("meltype_role_key"));
        free = reinterpret_cast<decltype(free)>(sym("meltype_free"));
        if (!create || !destroy || !handleKey || !commit || !selectCandidate || !setDirect || !free) {
            FCITX_ERROR() << "Meltype: libMeltypeNative.so の関数が足りません";
            return false;
        }
        if (initMozc && !initMozc((base + "/mozc/meltype_mozc_helper").c_str(), nullptr)) {
            FCITX_WARN() << "Meltype: Mozc を使えません (漢字変換はひらがなのままになります)";
        }
        return true;
    }

    // 本体が返した JSON を受け取って解放する。NULL なら空 (キーはアプリに渡す)。
    std::string take(char *pointer) const {
        if (!pointer) return {};
        std::string text(pointer);
        free(pointer);
        return text;
    }
};

// ---- 本体が返す JSON (SessionResult) を読む。形が決まっているので、必要な分だけの小さな読み取り ----

struct Json {
    enum class Kind { Null, Bool, Number, String, Array, Object } kind = Kind::Null;
    bool boolean = false;
    double number = 0;
    std::string string;
    std::vector<Json> items;
    std::vector<std::pair<std::string, Json>> members;

    const Json *get(const std::string &name) const {
        for (const auto &[key, value] : members)
            if (key == name) return &value;
        return nullptr;
    }
    std::string str(const std::string &name) const {
        auto *v = get(name);
        return v && v->kind == Kind::String ? v->string : std::string();
    }
    int integer(const std::string &name, int fallback = 0) const {
        auto *v = get(name);
        return v && v->kind == Kind::Number ? static_cast<int>(v->number) : fallback;
    }
    bool flag(const std::string &name) const {
        auto *v = get(name);
        return v && v->kind == Kind::Bool && v->boolean;
    }
    std::vector<std::string> strings(const std::string &name) const {
        std::vector<std::string> result;
        if (auto *v = get(name); v && v->kind == Kind::Array)
            for (const auto &item : v->items)
                if (item.kind == Kind::String) result.push_back(item.string);
        return result;
    }
};

class JsonReader {
public:
    explicit JsonReader(const std::string &text) : s_(text) {}

    bool read(Json &out) {
        bool ok = value(out);
        space();
        return ok && i_ == s_.size();
    }

private:
    const std::string &s_;
    size_t i_ = 0;

    void space() {
        while (i_ < s_.size() && (s_[i_] == ' ' || s_[i_] == '\n' || s_[i_] == '\r' || s_[i_] == '\t')) i_++;
    }
    bool literal(const char *word) {
        size_t n = std::char_traits<char>::length(word);
        if (s_.compare(i_, n, word) != 0) return false;
        i_ += n;
        return true;
    }
    static void appendUtf8(std::string &out, uint32_t cp) {
        if (cp < 0x80) out += static_cast<char>(cp);
        else if (cp < 0x800) { out += static_cast<char>(0xC0 | (cp >> 6)); out += static_cast<char>(0x80 | (cp & 0x3F)); }
        else if (cp < 0x10000) {
            out += static_cast<char>(0xE0 | (cp >> 12));
            out += static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
            out += static_cast<char>(0x80 | (cp & 0x3F));
        } else {
            out += static_cast<char>(0xF0 | (cp >> 18));
            out += static_cast<char>(0x80 | ((cp >> 12) & 0x3F));
            out += static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
            out += static_cast<char>(0x80 | (cp & 0x3F));
        }
    }
    bool hex4(uint32_t &cp) {
        if (i_ + 4 > s_.size()) return false;
        cp = static_cast<uint32_t>(std::strtoul(s_.substr(i_, 4).c_str(), nullptr, 16));
        i_ += 4;
        return true;
    }
    bool stringValue(std::string &out) {
        if (s_[i_] != '"') return false;
        i_++;
        while (i_ < s_.size() && s_[i_] != '"') {
            char c = s_[i_++];
            if (c != '\\') { out += c; continue; }
            if (i_ >= s_.size()) return false;
            char e = s_[i_++];
            switch (e) {
            case 'n': out += '\n'; break;
            case 'r': out += '\r'; break;
            case 't': out += '\t'; break;
            case 'b': out += '\b'; break;
            case 'f': out += '\f'; break;
            case 'u': {
                uint32_t cp;
                if (!hex4(cp)) return false;
                // サロゲートペア (絵文字)
                if (cp >= 0xD800 && cp <= 0xDBFF && s_.compare(i_, 2, "\\u") == 0) {
                    i_ += 2;
                    uint32_t low;
                    if (!hex4(low)) return false;
                    cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                }
                appendUtf8(out, cp);
                break;
            }
            default: out += e; break;
            }
        }
        if (i_ >= s_.size()) return false;
        i_++;
        return true;
    }
    bool value(Json &out) {
        space();
        if (i_ >= s_.size()) return false;
        char c = s_[i_];
        if (c == '{') {
            out.kind = Json::Kind::Object;
            i_++;
            space();
            if (i_ < s_.size() && s_[i_] == '}') { i_++; return true; }
            while (true) {
                space();
                std::string key;
                if (i_ >= s_.size() || !stringValue(key)) return false;
                space();
                if (i_ >= s_.size() || s_[i_++] != ':') return false;
                Json member;
                if (!value(member)) return false;
                out.members.emplace_back(std::move(key), std::move(member));
                space();
                if (i_ < s_.size() && s_[i_] == ',') { i_++; continue; }
                if (i_ < s_.size() && s_[i_] == '}') { i_++; return true; }
                return false;
            }
        }
        if (c == '[') {
            out.kind = Json::Kind::Array;
            i_++;
            space();
            if (i_ < s_.size() && s_[i_] == ']') { i_++; return true; }
            while (true) {
                Json item;
                if (!value(item)) return false;
                out.items.push_back(std::move(item));
                space();
                if (i_ < s_.size() && s_[i_] == ',') { i_++; continue; }
                if (i_ < s_.size() && s_[i_] == ']') { i_++; return true; }
                return false;
            }
        }
        if (c == '"') { out.kind = Json::Kind::String; return stringValue(out.string); }
        if (literal("true")) { out.kind = Json::Kind::Bool; out.boolean = true; return true; }
        if (literal("false")) { out.kind = Json::Kind::Bool; out.boolean = false; return true; }
        if (literal("null")) { out.kind = Json::Kind::Null; return true; }
        char *end = nullptr;
        out.number = std::strtod(s_.c_str() + i_, &end);
        if (end == s_.c_str() + i_) return false;
        out.kind = Json::Kind::Number;
        i_ = static_cast<size_t>(end - s_.c_str());
        return true;
    }
};

// ---- キー: fcitx5 のキー → Meltype の本体が使う Windows の仮想キーコード (ibus-engine-meltype と同じ) ----

constexpr int OtherCharacterKey = 0x07;

int specialKey(fcitx::KeySym sym) {
    switch (sym) {
    case FcitxKey_Return: case FcitxKey_KP_Enter: return 0x0D;
    case FcitxKey_Tab: return 0x09;
    case FcitxKey_space: return 0x20;
    case FcitxKey_BackSpace: return 0x08;
    case FcitxKey_Delete: return 0x2E;
    case FcitxKey_Escape: return 0x1B;
    case FcitxKey_Left: return 0x25;
    case FcitxKey_Up: return 0x26;
    case FcitxKey_Right: return 0x27;
    case FcitxKey_Down: return 0x28;
    case FcitxKey_Home: return 0x24;
    case FcitxKey_End: return 0x23;
    case FcitxKey_Page_Up: return 0x21;
    case FcitxKey_Page_Down: return 0x22;
    case FcitxKey_F6: return 0x75;
    case FcitxKey_F7: return 0x76;
    case FcitxKey_F8: return 0x77;
    case FcitxKey_F9: return 0x78;
    case FcitxKey_F10: return 0x79;
    default: return 0;
    }
}

// (仮想キーコード, 入力する文字)。Meltype が扱わないキーなら仮想キーコードは 0。
std::pair<int, uint32_t> virtualKey(fcitx::KeySym sym) {
    if (int vk = specialKey(sym)) return {vk, sym == FcitxKey_space ? 0x20u : 0u};
    uint32_t c = fcitx::Key::keySymToUnicode(sym);
    if (c < 0x20) return {0, 0};
    if (c >= 'a' && c <= 'z') return {static_cast<int>(c - 0x20), c};
    if (c >= 'A' && c <= 'Z') return {static_cast<int>(c), c};
    if (c >= '0' && c <= '9') return {static_cast<int>(c), c};
    switch (c) {
    case ',': return {0xBC, c};
    case '.': return {0xBE, c};
    case '-': return {0xBD, c};
    case '/': return {0xBF, c};
    case '[': return {0xDB, c};
    case ']': return {0xDD, c};
    default: return {OtherCharacterKey, c};
    }
}

// UTF-8 の文字列の、文字単位の位置 → バイトの位置
size_t byteOffset(const std::string &text, size_t chars) {
    size_t i = 0;
    while (i < text.size() && chars > 0) {
        unsigned char c = static_cast<unsigned char>(text[i]);
        i += c < 0x80 ? 1 : c < 0xE0 ? 2 : c < 0xF0 ? 3 : 4;
        chars--;
    }
    return std::min(i, text.size());
}

size_t charLength(const std::string &text) {
    size_t n = 0;
    for (unsigned char c : text)
        if ((c & 0xC0) != 0x80) n++;
    return n;
}

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
    bool direct = false;
    // 変換ボックス (preedit) を出しているか。出している間は、周りの文字 (preedit を含む) を本体に渡さない
    bool preeditVisible = false;

private:
    const Native &native_;
    void *session_ = nullptr;
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
        if (!loaded_ || event.isRelease()) return;
        auto *ic = event.inputContext();
        auto *state = ic->propertyFor(&factory_);
        if (!state->session()) return;
        syncCanDelete(ic, state->session());
        const auto key = event.key();
        const auto sym = key.sym();
        // 入力中の 変換・無変換・ひらがな/カタカナ: 設定で役割を分けていれば、本体が変換ボックスで処理する (#199)
        if (native_.roleKey && !state->direct &&
            (sym == FcitxKey_Henkan || sym == FcitxKey_Muhenkan || sym == FcitxKey_Hiragana_Katakana)) {
            int vk = sym == FcitxKey_Henkan ? 0x1C : sym == FcitxKey_Muhenkan ? 0x1D : 0x15;
            auto handled = native_.take(native_.roleKey(state->session(), vk));
            Json result;
            if (!handled.empty() && JsonReader(handled).read(result)) {
                apply(ic, result);
                event.filterAndAccept();
                return;
            }
        }
        // 半角/全角: 英数 (直接入力) ⇔ 日本語。英数・ひらがなのキーでも切り替える (JIS キーボード)。
        if (sym == FcitxKey_Zenkaku_Hankaku || sym == FcitxKey_Hankaku || sym == FcitxKey_Zenkaku || sym == FcitxKey_Eisu_toggle ||
            sym == FcitxKey_Hiragana_Katakana || sym == FcitxKey_Muhenkan || sym == FcitxKey_Henkan) {
            bool toDirect = sym == FcitxKey_Eisu_toggle || sym == FcitxKey_Muhenkan ||
                            ((sym == FcitxKey_Zenkaku_Hankaku || sym == FcitxKey_Hankaku || sym == FcitxKey_Zenkaku) && !state->direct);
            apply(ic, native_.take(native_.commit(state->session())));
            state->direct = toDirect;
            native_.setDirect(state->session(), toDirect ? 1 : 0);
            event.filterAndAccept();
            return;
        }
        auto [vk, character] = virtualKey(sym);
        if (vk == 0) return;
        const auto states = key.states();
        int modifiers = 0;
        if (states.test(fcitx::KeyState::Shift)) modifiers |= 1;
        if (states.test(fcitx::KeyState::Ctrl)) modifiers |= 2;
        if (states.test(fcitx::KeyState::Alt)) modifiers |= 4;
        if (states.test(fcitx::KeyState::Super)) modifiers |= 8;
        std::string before, after;
        bool surrounding = !state->preeditVisible && readSurrounding(ic, before, after);
        auto text = native_.take(native_.handleKey(state->session(), vk, static_cast<int>(character), modifiers,
                                                   surrounding ? before.c_str() : nullptr, surrounding ? after.c_str() : nullptr));
        if (text.empty()) return;
        Json result;
        if (!JsonReader(text).read(result)) return;
        apply(ic, result);
        if (result.flag("consumed")) event.filterAndAccept();
    }

    // 別の入力欄・アプリに移るとき、入力欄が変わったときは、未確定の内容をそのまま確定する。
    void reset(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override { commitPending(event.inputContext()); }
    void deactivate(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override { commitPending(event.inputContext()); }

    void selectCandidate(fcitx::InputContext *ic, int index) {
        auto *state = ic->propertyFor(&factory_);
        if (!state->session()) return;
        syncCanDelete(ic, state->session());
        apply(ic, native_.take(native_.selectCandidate(state->session(), index)));
    }

private:
    fcitx::Instance *instance_;
    Native native_;
    bool loaded_ = false;
    fcitx::FactoryFor<MeltypeState> factory_;

    void commitPending(fcitx::InputContext *ic) {
        auto *state = ic->propertyFor(&factory_);
        if (!state->session()) return;
        syncCanDelete(ic, state->session());
        apply(ic, native_.take(native_.commit(state->session())));
    }

    // 入力欄のキャレットの前後の文字列 (それぞれ 20 文字まで)。入力欄が対応していなければ false。
    static bool readSurrounding(fcitx::InputContext *ic, std::string &before, std::string &after) {
        if (!ic->capabilityFlags().test(fcitx::CapabilityFlag::SurroundingText) || !ic->surroundingText().isValid()) return false;
        const auto &text = ic->surroundingText().text();
        if (text.empty()) return false;
        size_t cursor = ic->surroundingText().cursor();
        size_t start = cursor > 20 ? cursor - 20 : 0;
        size_t from = byteOffset(text, start), at = byteOffset(text, cursor), to = byteOffset(text, cursor + 20);
        before = text.substr(from, at - from);
        after = text.substr(at, to - at);
        return true;
    }

    // 確定し直し (DeleteBefore) は、入力欄が周りの文字の削除に対応しているときだけ本体が行う。
    // 本体を呼ぶ前に毎回伝える (入力欄を移った最初のキーで、前の入力欄の値のまま確定し直さないように)。
    void syncCanDelete(fcitx::InputContext *ic, void *session) {
        if (!native_.setCanDelete || !session) return;
        bool canDelete = ic->capabilityFlags().test(fcitx::CapabilityFlag::SurroundingText) && ic->surroundingText().isValid();
        native_.setCanDelete(session, canDelete ? 1 : 0);
    }

    void apply(fcitx::InputContext *ic, const std::string &text) {
        if (text.empty()) return;
        Json result;
        if (JsonReader(text).read(result)) apply(ic, result);
    }

    void apply(fcitx::InputContext *ic, const Json &result) {
        if (auto *commits = result.get("commits"); commits && commits->kind == Json::Kind::Array) {
            for (const auto &edit : commits->items) {
                hidePreedit(ic);
                int remove = edit.integer("deleteBefore");
                if (remove > 0) ic->deleteSurroundingText(-remove, static_cast<unsigned int>(remove));
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
        ic->propertyFor(&factory_)->preeditVisible = !text.empty();
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

    void hidePreedit(fcitx::InputContext *ic) {
        ic->inputPanel().reset();
        ic->updatePreedit();
        ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
        ic->propertyFor(&factory_)->preeditVisible = false;
    }
};

void MeltypeCandidate::select(fcitx::InputContext *ic) const { engine_->selectCandidate(ic, index_); }

class MeltypeEngineFactory : public fcitx::AddonFactory {
    fcitx::AddonInstance *create(fcitx::AddonManager *manager) override { return new MeltypeEngine(manager->instance()); }
};

} // namespace

FCITX_ADDON_FACTORY(MeltypeEngineFactory);
