// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void
//
// Meltype の fcitx5 のアドオン (issue #99)。fcitx5 から読んだ事実 (キー・キャレットの前後の文字・入力欄の種類) を
// 本体 (libMeltypeNative.so の ImeHost) に渡し、返ってきた指示 (確定・変換中の表示・候補・入力モードの表示) を入力欄に出す。
// 入力モードとモードを切り替えるキーの意味は本体が決めるので、ここでは持たない。本体は dlopen で読むので、ビルドに本体は要らない。

#include <dlfcn.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <memory>
#include <string>
#include <vector>

#include <fcitx-config/configuration.h>
#include <fcitx-config/option.h>
#include <fcitx-utils/capabilityflags.h>
#include <fcitx-utils/eventdispatcher.h>
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

// 本体のプロトコルの版 (ImeHost.Version)
constexpr int ImeVersion = 1;

// 判定の文脈とコードの行の判定に使う、キャレットの前の文字数
constexpr size_t BeforeLength = 4000;
constexpr size_t AfterLength = 20;

// libMeltypeNative.so と設定画面 (meltype-settings) を入れた場所
std::string meltypeDir() {
    const char *dir = std::getenv("MELTYPE_DIR");
    return dir && *dir ? dir : MELTYPE_DIR;
}

// fcitx5 の設定ツールは、External の項目が 1 つだけの設定を開くと、その項目のプログラムを直接起動する
FCITX_CONFIGURATION(MeltypeConfig,
                    fcitx::ExternalOption settings{this, "Settings", "設定", meltypeDir() + "/meltype-settings"};);

// ---- 本体 (libMeltypeNative.so) ----

struct Native {
    void *handle = nullptr;
    int (*initMozc)(const char *, const char *) = nullptr;
    void (*setConversionReady)(void (*)()) = nullptr;
    void *(*create)() = nullptr;
    char *(*handle_)(void *, const char *) = nullptr;
    void (*destroy)(void *) = nullptr;
    void (*free)(void *) = nullptr;

    bool load() {
        handle = dlopen((meltypeDir() + "/libMeltypeNative.so").c_str(), RTLD_NOW | RTLD_LOCAL);
        if (!handle) {
            FCITX_ERROR() << "Meltype: libMeltypeNative.so を読めません: " << dlerror();
            return false;
        }
        auto sym = [this](const char *name) { return dlsym(handle, name); };
        initMozc = reinterpret_cast<decltype(initMozc)>(sym("meltype_init_mozc"));
        setConversionReady = reinterpret_cast<decltype(setConversionReady)>(sym("meltype_set_conversion_ready"));
        create = reinterpret_cast<decltype(create)>(sym("meltype_ime_create"));
        handle_ = reinterpret_cast<decltype(handle_)>(sym("meltype_ime_handle"));
        destroy = reinterpret_cast<decltype(destroy)>(sym("meltype_ime_destroy"));
        free = reinterpret_cast<decltype(free)>(sym("meltype_free"));
        if (!initMozc || !setConversionReady || !create || !handle_ || !destroy || !free) {
            FCITX_ERROR() << "Meltype: libMeltypeNative.so の関数が足りません";
            return false;
        }
        if (!initMozc((meltypeDir() + "/mozc/meltype_mozc_helper").c_str(), nullptr)) {
            FCITX_WARN() << "Meltype: Mozc を使えません (漢字変換はひらがなのままになります)";
        }
        return true;
    }

    // 出来事 (ImeEvent の JSON) を送って、応答 (ImeOutput の JSON) を受け取る。本体が処理できなければ空。
    std::string send(void *host, const std::string &event) const {
        char *pointer = handle_(host, event.c_str());
        if (!pointer) return {};
        std::string text(pointer);
        free(pointer);
        return text;
    }
};

// ---- 本体が返す JSON (ImeOutput) を読む。形が決まっているので、必要な分だけの小さな読み取り ----

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
    bool flag(const std::string &name, bool fallback = false) const {
        auto *v = get(name);
        return v && v->kind == Kind::Bool ? v->boolean : fallback;
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


// JSON の文字列にする ("…" を含む)。text は UTF-8
std::string quote(const std::string &text) {
    std::string out = "\"";
    for (unsigned char c : text) {
        if (c == '"' || c == '\\') {
            out += '\\';
            out += static_cast<char>(c);
        } else if (c < 0x20) {
            char escaped[8];
            std::snprintf(escaped, sizeof(escaped), "\\u%04x", c);
            out += escaped;
        } else {
            out += static_cast<char>(c);
        }
    }
    return out + "\"";
}

const char *boolean(bool value) { return value ? "true" : "false"; }

// ---- キー: 文字の無いキー → 本体が使う Windows の仮想キーコード (ibus-engine-meltype と同じ)。文字を入力するキーは文字だけを渡す ----

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
    // 入力モードのキー (意味は本体が決める)
    case FcitxKey_Zenkaku_Hankaku: case FcitxKey_Hankaku: case FcitxKey_Zenkaku: return 0x19;
    case FcitxKey_Eisu_toggle: return 0xF0;
    case FcitxKey_Hiragana_Katakana: return 0x15;
    case FcitxKey_Henkan: return 0x1C;
    case FcitxKey_Muhenkan: return 0x1D;
    default: return 0;
    }
}

// (仮想キーコード, 入力する文字)。文字を入力するキーの仮想キーコードは 0 (本体が文字から決める)。Meltype が扱わないキーなら両方 0。
std::pair<int, uint32_t> virtualKey(fcitx::KeySym sym) {
    if (int vk = specialKey(sym)) return {vk, sym == FcitxKey_space ? 0x20u : 0u};
    uint32_t c = fcitx::Key::keySymToUnicode(sym);
    return {0, c < 0x20 ? 0 : c};
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

// 入力欄ごとの本体 (ImeHost)
class MeltypeState : public fcitx::InputContextProperty {
public:
    MeltypeState(const Native &native) : native_(native) {
        if (native_.create) host_ = native_.create();
    }
    ~MeltypeState() override {
        if (host_) native_.destroy(host_);
    }
    void *host() const { return host_; }
    // Shift を押しているか。fcitx5 は Space のキーに Shift を付けて渡さないことがある (どの環境で起きるかは
    // 未確認。手元は KDE Plasma 6 / Wayland、X11 と GNOME は試していない) ので、キーから覚えておく: #1033
    bool shiftHeld = false;
    // 入力モードの表示 (A / あ) を出したか。次のキーで 1 回だけ消す (#1034)。
    bool modeShown = false;
    // 押している左右の Alt の仮想キーコード。ほかのキーを押したら 0 (単独押しではない)
    int tap = 0;
    // 変換中の文字 (preedit) を出しているか
    bool preeditVisible = false;

private:
    const Native &native_;
    void *host_ = nullptr;
};

class MeltypeCandidate : public fcitx::CandidateWord {
public:
    MeltypeCandidate(MeltypeEngine *engine, std::string text, int index, bool prediction)
        : fcitx::CandidateWord(fcitx::Text(std::move(text))), engine_(engine), index_(index), prediction_(prediction) {}
    void select(fcitx::InputContext *ic) const override;

private:
    MeltypeEngine *engine_;
    int index_;
    bool prediction_;  // 予測変換の候補か
};

class MeltypeEngine : public fcitx::InputMethodEngineV2 {
public:
    explicit MeltypeEngine(fcitx::Instance *instance)
        : instance_(instance), factory_([this](fcitx::InputContext &) { return new MeltypeState(native_); }) {
        loaded_ = native_.load();
        if (loaded_) {
            dispatcher_.attach(&instance_->eventLoop());
            s_engine = this;
            native_.setConversionReady(&MeltypeEngine::onConversionReady);
        }
        instance_->inputContextManager().registerProperty("meltypeState", &factory_);
    }

    ~MeltypeEngine() override { s_engine = nullptr; }

    void keyEvent(const fcitx::InputMethodEntry &, fcitx::KeyEvent &event) override {
        if (!loaded_) return;
        auto *ic = event.inputContext();
        auto *state = ic->propertyFor(&factory_);
        const auto key = event.key();
        if (key.sym() == FcitxKey_Shift_L || key.sym() == FcitxKey_Shift_R) {
            state->shiftHeld = !event.isRelease();
            state->tap = 0;
            return;
        }
        if (key.sym() == FcitxKey_Alt_L || key.sym() == FcitxKey_Alt_R) {
            const int vk = key.sym() == FcitxKey_Alt_L ? 0xA4 : 0xA5;
            if (!event.isRelease()) {
                state->tap = vk;
                return;
            }
            if (state->tap != vk) return;
            state->tap = 0;
            Json output;
            if (send(ic, "key", ",\"key\":{\"vk\":" + std::to_string(vk) + ",\"tap\":true}", output) && output.get("result")->flag("consumed"))
                event.filterAndAccept();
            return;
        }
        if (event.isRelease()) return;
        state->tap = 0;
        // 出した A / あ は、次に実際のキーを押したときに消す (#1034)。離したときに消すと、押している間しか見えない
        if (state->modeShown) {
            showMode(ic, "");
            state->modeShown = false;
        }
        auto [vk, character] = virtualKey(key.sym());
        if (vk == 0 && character == 0) return;
        const auto states = key.states();
        // Shift を離したことが伝わらず shiftHeld が残っていても、Space 以外には付けない (Tab や矢印が Shift 付きにならないように)
        const bool shift = states.test(fcitx::KeyState::Shift) || (vk == 0x20 && state->shiftHeld);
        const std::string fields = std::string(",\"key\":{\"vk\":") + std::to_string(vk) + ",\"ch\":" + std::to_string(character) +
            ",\"shift\":" + boolean(shift) + ",\"ctrl\":" + boolean(states.test(fcitx::KeyState::Ctrl)) +
            ",\"alt\":" + boolean(states.test(fcitx::KeyState::Alt)) + ",\"command\":" + boolean(states.test(fcitx::KeyState::Super)) + "}";
        Json output;
        // 本体が使わなかったキーは受け取らず、fcitx5 とアプリに任せる
        if (send(ic, "key", fields, output) && output.get("result")->flag("consumed")) event.filterAndAccept();
    }

    // Reset (クリックなど) は Enter のたびに来るクライアントがあるので、日本語にしていたコードの行はそのまま
    void reset(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override {
        event.inputContext()->propertyFor(&factory_)->shiftHeld = false;
        event.inputContext()->propertyFor(&factory_)->tap = 0;
        leave(event.inputContext(), false);
    }

    // フォーカスが外れた・入力メソッドを切り替えた。Shift を離したことは届かないことがあるので忘れる (#1033)
    void deactivate(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override {
        event.inputContext()->propertyFor(&factory_)->shiftHeld = false;
        // Alt + Tab などはウィンドウマネージャーが受け取るので、フォーカスが移ったら単独押しではない
        event.inputContext()->propertyFor(&factory_)->tap = 0;
        leave(event.inputContext(), true);
    }

    const fcitx::Configuration *getConfig() const override { return &config_; }

    void selectCandidate(fcitx::InputContext *ic, int index, bool prediction) {
        Json output;
        send(ic, "select", ",\"index\":" + std::to_string(index) + (prediction ? ",\"prediction\":true" : ""), output);
    }

private:
    // ワーカーのスレッドで呼ばれるので、メインループに移す。エンジンはプロセスで 1 つで、meltype_set_conversion_ready は解除できない
    static inline std::atomic<MeltypeEngine *> s_engine = nullptr;
    static void onConversionReady() {
        if (auto *engine = s_engine.load()) engine->dispatcher_.schedule([engine]() { engine->refreshConversion(); });
    }

    void refreshConversion() {
        auto *ic = instance_->mostRecentInputContext();
        if (!ic || !ic->hasFocus() || instance_->inputMethodEngine(ic) != this) return;
        Json output;
        send(ic, "refresh", "", output);
    }

    fcitx::Instance *instance_;
    Native native_;
    bool loaded_ = false;
    fcitx::EventDispatcher dispatcher_;
    fcitx::FactoryFor<MeltypeState> factory_;
    MeltypeConfig config_;

    // 未確定の内容を確定する。キャレットはこの入力欄の外か、クリックした所に移る
    void leave(fcitx::InputContext *ic, bool focusOut) {
        Json output;
        send(ic, "commit", std::string(",\"caret\":{\"moved\":true,\"passed\":0,\"left\":") + boolean(focusOut) + "}", output);
    }

    // 出来事を本体に送り、返ってきた指示を入力欄に出す。本体が処理できなければ false
    bool send(fcitx::InputContext *ic, const char *type, const std::string &fields, Json &output) {
        auto *state = ic->propertyFor(&factory_);
        if (!state->host()) return false;
        const auto text = native_.send(state->host(), std::string("{\"v\":") + std::to_string(ImeVersion) + ",\"type\":\"" + type +
            "\",\"sid\":\"fcitx5\",\"field\":" + field(ic) + fields + "}");
        if (text.empty() || !JsonReader(text).read(output) || !output.get("state") || !output.get("result")) return false;
        apply(ic, *output.get("result"));
        if (const auto *shown = output.get("state"); shown->flag("indicator")) {
            showMode(ic, shown->flag("japanese") ? "あ" : "A");
            state->modeShown = true;
        }
        return true;
    }

    // 入力欄の事実 (ImeField)
    std::string field(fcitx::InputContext *ic) {
        const auto &flags = ic->capabilityFlags();
        const bool surrounding = flags.test(fcitx::CapabilityFlag::SurroundingText) && ic->surroundingText().isValid();
        std::string before = "null", after = "null", selection = "null";
        if (surrounding) {
            // 選んでいる文字の前と後ろ
            const auto &text = ic->surroundingText().text();
            const size_t start = std::min(ic->surroundingText().cursor(), ic->surroundingText().anchor());
            const size_t end = std::max(ic->surroundingText().cursor(), ic->surroundingText().anchor());
            const size_t from = byteOffset(text, start > BeforeLength ? start - BeforeLength : 0);
            const size_t at = byteOffset(text, start), until = byteOffset(text, end), to = byteOffset(text, end + AfterLength);
            before = quote(text.substr(from, at - from));
            after = quote(text.substr(until, to - until));
            if (until > at) selection = quote(text.substr(at, until - at));
        }
        std::string traits;
        for (const auto &[flag, name] : {std::pair{fcitx::CapabilityFlag::Terminal, "terminal"}, std::pair{fcitx::CapabilityFlag::Password, "password"},
                                          std::pair{fcitx::CapabilityFlag::Sensitive, "private"}}) {
            if (flags.test(flag)) traits += std::string(traits.empty() ? "" : ",") + "\"" + name + "\"";
        }
        return "{\"before\":" + before + ",\"after\":" + after + ",\"selection\":" + selection +
            ",\"composing\":" + boolean(ic->propertyFor(&factory_)->preeditVisible) + ",\"traits\":[" + traits + "]" +
            (ic->program().empty() ? "" : ",\"app\":" + quote(ic->program())) + ",\"canDelete\":" + boolean(surrounding) + "}";
    }

    // 入力モード (A / あ) をカーソル位置に出す (Mozc と同じ入力パネルの枠)。空なら消す
    static void showMode(fcitx::InputContext *ic, const std::string &mode) {
        ic->inputPanel().setAuxUp(fcitx::Text(mode));
        ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
    }

    void apply(fcitx::InputContext *ic, const Json &result) {
        if (auto *commits = result.get("commits"); commits && commits->kind == Json::Kind::Array) {
            for (const auto &edit : commits->items) {
                // 確定し直し: 本体がキャレットの前と照合した前の文字 (expect) を消してから入れる。
                // 本体の deleteBefore は UTF-16 の単位なので使わず、expect の文字数 (コードポイント) だけ消す
                if (edit.integer("deleteBefore") > 0) {
                    const size_t length = charLength(edit.str("expect"));
                    hidePreedit(ic);
                    ic->deleteSurroundingText(-static_cast<int>(length), static_cast<unsigned int>(length));
                } else {
                    ic->inputPanel().reset();
                    ic->propertyFor(&factory_)->preeditVisible = false;
                }
                if (const auto committed = edit.str("text"); !committed.empty()) ic->commitString(committed);
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
        auto hint = view.str("hint");
        if (auto suggestion = view.str("suggestion"); !suggestion.empty())
            hint = suggestion + (hint.empty() ? "" : "　" + hint);
        if (!hint.empty()) panel.setAuxDown(fcitx::Text(hint));
        if (const auto *listed = view.get("list"); listed && listed->kind == Json::Kind::Object) {
            const auto candidates = listed->strings("items");
            const bool prediction = listed->flag("prediction");
            auto list = std::make_unique<fcitx::CommonCandidateList>();
            list->setLayoutHint(fcitx::CandidateLayoutHint::Vertical);
            list->setPageSize(9);
            list->setLabels({"1. ", "2. ", "3. ", "4. ", "5. ", "6. ", "7. ", "8. ", "9. "});
            list->setCursorPositionAfterPaging(fcitx::CursorPositionAfterPaging::DonotChange);
            for (size_t i = 0; i < candidates.size(); i++) list->append<MeltypeCandidate>(this, candidates[i], static_cast<int>(i), prediction);
            int selected = listed->integer("selected", -1);
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

void MeltypeCandidate::select(fcitx::InputContext *ic) const { engine_->selectCandidate(ic, index_, prediction_); }

class MeltypeEngineFactory : public fcitx::AddonFactory {
    fcitx::AddonInstance *create(fcitx::AddonManager *manager) override { return new MeltypeEngine(manager->instance()); }
};

} // namespace

FCITX_ADDON_FACTORY(MeltypeEngineFactory);
