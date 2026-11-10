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
#include <functional>
#include <memory>
#include <string>
#include <vector>

#include <fcitx-config/configuration.h>
#include <fcitx-config/option.h>
#include <fcitx-utils/capabilityflags.h>
#include <fcitx-utils/event.h>
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
    // NumLock を切ったテンキーも、専用のキーと同じにする (Windows と同じ。PageUp の無いキーボードで候補のページを送れる)
    case FcitxKey_Delete: case FcitxKey_KP_Delete: return 0x2E;
    case FcitxKey_Escape: return 0x1B;
    case FcitxKey_Left: case FcitxKey_KP_Left: return 0x25;
    case FcitxKey_Up: case FcitxKey_KP_Up: return 0x26;
    case FcitxKey_Right: case FcitxKey_KP_Right: return 0x27;
    case FcitxKey_Down: case FcitxKey_KP_Down: return 0x28;
    case FcitxKey_Home: case FcitxKey_KP_Home: return 0x24;
    case FcitxKey_End: case FcitxKey_KP_End: return 0x23;
    case FcitxKey_Page_Up: case FcitxKey_KP_Prior: return 0x21;
    case FcitxKey_Page_Down: case FcitxKey_KP_Next: return 0x22;
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

// 入力欄ごとの表示の状態。入力の本体 (ImeHost) はエンジンに 1 つで、入力モードはすべての入力欄で同じ
class MeltypeState : public fcitx::InputContextProperty {
public:
    // Shift を押しているか。fcitx5 は Space のキーに Shift を付けて渡さないことがある (どの環境で起きるかは
    // 未確認。手元は KDE Plasma 6 / Wayland、X11 と GNOME は試していない) ので、キーから覚えておく: #1033
    bool shiftHeld = false;
    // 入力モードの表示 (A / あ) と候補の意味を、本体が決めた時間のあとに消す・出すタイマー
    std::unique_ptr<fcitx::EventSourceTime> modeTimer, meaningTimer;
    // 入力欄に入ったときの入力モードを出すまでのタイマー
    std::unique_ptr<fcitx::EventSourceTime> enterTimer;
    // 意味を出すのを待っている候補 (選んでいる番号と意味)。変わったら待ち直す
    std::string meaningKey;
    // 変換中の文字 (preedit) を出しているか
    bool preeditVisible = false;
    // 入力欄に入ったときの出来事を送っているか (入力モードの表示を遅らせる)
    bool entering = false;
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
        : instance_(instance), factory_([](fcitx::InputContext &) { return new MeltypeState(); }) {
        loaded_ = native_.load();
        if (loaded_) {
            host_ = native_.create();
            dispatcher_.attach(&instance_->eventLoop());
            s_engine = this;
            native_.setConversionReady(&MeltypeEngine::onConversionReady);
        }
        instance_->inputContextManager().registerProperty("meltypeState", &factory_);
    }

    ~MeltypeEngine() override {
        s_engine = nullptr;
        if (host_) native_.destroy(host_);
    }

    // パネル (トレイ) の入力メソッドの表示。本体が最後に返した言語モードを出す
    std::string subMode(const fcitx::InputMethodEntry &, fcitx::InputContext &) override {
        return languageMode_.empty() ? std::string() : languageMode_ == "Japanese" ? "日本語" : "英数";
    }
    std::string subModeLabelImpl(const fcitx::InputMethodEntry &, fcitx::InputContext &) override {
        return languageMode_.empty() ? std::string() : languageMode_ == "Japanese" ? "あ" : "A";
    }

    void keyEvent(const fcitx::InputMethodEntry &, fcitx::KeyEvent &event) override {
        if (!loaded_) return;
        auto *ic = event.inputContext();
        auto *state = ic->propertyFor(&factory_);
        const auto key = event.key();
        if (key.sym() == FcitxKey_Shift_L || key.sym() == FcitxKey_Shift_R) {
            state->shiftHeld = !event.isRelease();
            return;
        }
        if (event.isRelease()) return;
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
        if (send(ic, "KeyPressed", fields, output) && output.get("result")->flag("consumed")) event.filterAndAccept();
    }

    // 入力欄にフォーカスが入った・入力メソッドを Meltype にした
    void activate(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override {
        Json output;
        auto *state = event.inputContext()->propertyFor(&factory_);
        state->entering = true;
        send(event.inputContext(), "CaretMoved", ",\"caret\":{\"moved\":true,\"passedKey\":0,\"focusEntered\":true}", output);
        state->entering = false;
    }

    // Reset (クリックなど) は Enter のたびに来るクライアントがあるので、日本語にしていたコードの行はそのまま
    void reset(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override {
        event.inputContext()->propertyFor(&factory_)->shiftHeld = false;
        leave(event.inputContext(), false);
    }

    // フォーカスが外れた・入力メソッドを切り替えた。Shift を離したことは届かないことがあるので忘れる (#1033)
    void deactivate(const fcitx::InputMethodEntry &, fcitx::InputContextEvent &event) override {
        event.inputContext()->propertyFor(&factory_)->shiftHeld = false;
        event.inputContext()->propertyFor(&factory_)->enterTimer.reset();
        leave(event.inputContext(), true);
    }

    const fcitx::Configuration *getConfig() const override { return &config_; }

    void selectCandidate(fcitx::InputContext *ic, int index) {
        Json output;
        send(ic, "CandidateClicked", ",\"index\":" + std::to_string(index), output);
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
        send(ic, "ConversionFinished", "", output);
    }

    fcitx::Instance *instance_;
    Native native_;
    void *host_ = nullptr;
    // 本体が最後に返した言語モード (パネルの「あ / A」に出す)
    std::string languageMode_;
    bool loaded_ = false;
    fcitx::EventDispatcher dispatcher_;
    fcitx::FactoryFor<MeltypeState> factory_;
    MeltypeConfig config_;

    // 未確定の内容を確定する。キャレットはこの入力欄の外か、クリックした所に移る
    // フォーカスが外れたとき、入力欄に出した変換中の文字 (client preedit) は、クライアント (ClientUnfocusCommit) か fcitx5 が
    // 表示のまま確定するので、本体には「表示のまま確定された」と伝える (本体の確定も入れると二重になる)
    void leave(fcitx::InputContext *ic, bool focusOut) {
        Json output;
        auto *state = ic->propertyFor(&factory_);
        const bool committedByClient = focusOut && state->preeditVisible && ic->capabilityFlags().test(fcitx::CapabilityFlag::Preedit);
        send(ic, committedByClient ? "CommittedByApp" : "CommitRequested",
             std::string(",\"caret\":{\"moved\":true,\"passedKey\":0,\"focusLeft\":") + boolean(focusOut) + "}", output);
    }

    // 出来事を本体に送り、返ってきた指示を入力欄に出す。本体が処理できなければ false
    bool send(fcitx::InputContext *ic, const char *type, const std::string &fields, Json &output) {
        auto *state = ic->propertyFor(&factory_);
        if (!host_) return false;
        const auto text = native_.send(host_, std::string("{\"version\":") + std::to_string(ImeVersion) + ",\"type\":\"" + type +
            "\",\"instance\":\"fcitx5\",\"field\":" + field(ic) + fields + "}");
        if (text.empty() || !JsonReader(text).read(output) || !output.get("state") || !output.get("result")) return false;
        apply(ic, *output.get("result"));
        const auto *shown = output.get("state");
        if (const auto mode = shown->str("languageMode"); mode != languageMode_) {
            languageMode_ = mode;
            ic->updateUserInterface(fcitx::UserInterfaceComponent::StatusArea);
        }
        if (const int ms = shown->integer("modeIndicatorMs"); ms > 0) {
            const std::string mode = shown->str("languageMode") == "Japanese" ? "あ" : "A";
            // 入ったばかりの入力欄は、アプリがまだキャレットの位置を知らせていない。fcitx5 が入力メソッドの情報を出すときと同じく 30ms 待つ
            if (state->entering) state->enterTimer = after(30, [this, ic, mode, ms]() { showMode(ic, mode, ms); });
            else showMode(ic, mode, ms);
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
        for (const auto &[flag, name] : {std::pair{fcitx::CapabilityFlag::Terminal, "Terminal"}, std::pair{fcitx::CapabilityFlag::Password, "Password"},
                                          std::pair{fcitx::CapabilityFlag::Sensitive, "Private"}}) {
            if (flags.test(flag)) traits += std::string(traits.empty() ? "" : ",") + "\"" + name + "\"";
        }
        return "{\"before\":" + before + ",\"after\":" + after + ",\"selection\":" + selection +
            ",\"composing\":" + boolean(ic->propertyFor(&factory_)->preeditVisible) + ",\"traits\":[" + traits + "]" +
            (ic->program().empty() ? "" : ",\"app\":" + quote(ic->program())) + ",\"canRemove\":" + boolean(surrounding) + "}";
    }

    // 入力モード (A / あ) をカーソル位置に、本体が決めた時間だけ出す (Mozc と同じ入力パネルの枠)
    void showMode(fcitx::InputContext *ic, const std::string &mode, int milliseconds) {
        ic->inputPanel().setAuxUp(fcitx::Text(mode));
        ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
        ic->propertyFor(&factory_)->modeTimer = after(milliseconds, [ic]() {
            ic->inputPanel().setAuxUp(fcitx::Text());
            ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
        });
    }

    std::unique_ptr<fcitx::EventSourceTime> after(int milliseconds, std::function<void()> action) {
        return instance_->eventLoop().addTimeEvent(CLOCK_MONOTONIC, fcitx::now(CLOCK_MONOTONIC) + static_cast<uint64_t>(milliseconds) * 1000, 0,
            [action](fcitx::EventSourceTime *, uint64_t) {
                action();
                return true;
            });
    }

    void apply(fcitx::InputContext *ic, const Json &result) {
        if (auto *commits = result.get("commits"); commits && commits->kind == Json::Kind::Array) {
            for (const auto &edit : commits->items) {
                // 確定し直し: 本体がキャレットの前と照合した前の文字 (removeBefore) を、文字数 (コードポイント) で消してから入れる
                if (!edit.str("removeBefore").empty()) {
                    const size_t length = charLength(edit.str("removeBefore"));
                    hidePreedit(ic);
                    ic->deleteSurroundingText(-static_cast<int>(length), static_cast<unsigned int>(length));
                } else {
                    ic->inputPanel().reset();
                    ic->propertyFor(&factory_)->preeditVisible = false;
                }
                if (const auto committed = edit.str("text"); !committed.empty()) ic->commitString(committed);
                // 確定した文字の中にキャレットを戻す (空の括弧の中など)。fcitx5 からキャレットは動かせないので、← をアプリに送る
                for (int i = 0; i < edit.integer("moveCaretBack"); i++) {
                    ic->forwardKey(fcitx::Key(FcitxKey_Left), false);
                    ic->forwardKey(fcitx::Key(FcitxKey_Left), true);
                }
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
        // 打ったキー・もしかして (書き間違い。Tab で直せる)・案内を 1 行で出す
        std::string aux;
        for (const auto &part : {view.str("typedKeys"), view.str("spellingSuggestion"), view.str("hint")})
            if (!part.empty()) aux += (aux.empty() ? "" : "　") + part;
        if (!aux.empty()) panel.setAuxDown(fcitx::Text(aux));
        auto *state = ic->propertyFor(&factory_);
        const auto *listed = view.get("candidates");
        if (!listed || listed->kind != Json::Kind::Object) {
            state->meaningTimer.reset();
            state->meaningKey.clear();
        } else {
            // 注記 (候補の種類など) は候補の後ろに付ける
            auto candidates = listed->strings("items");
            if (const auto *notes = listed->get("notes"); notes && notes->kind == Json::Kind::Array)
                for (size_t i = 0; i < candidates.size() && i < notes->items.size(); i++)
                    if (notes->items[i].kind == Json::Kind::String) candidates[i] += "　" + notes->items[i].string;
            auto list = std::make_unique<fcitx::CommonCandidateList>();
            list->setLayoutHint(fcitx::CandidateLayoutHint::Vertical);
            list->setPageSize(9);
            // 番号は本体が決める (数字キーで選べる一覧だけ)
            if (listed->flag("numbered")) list->setLabels({"1. ", "2. ", "3. ", "4. ", "5. ", "6. ", "7. ", "8. ", "9. "});
            else list->setLabels(std::vector<std::string>(9, ""));
            list->setCursorPositionAfterPaging(fcitx::CursorPositionAfterPaging::DonotChange);
            for (size_t i = 0; i < candidates.size(); i++) list->append<MeltypeCandidate>(this, candidates[i], static_cast<int>(i));
            int selected = listed->integer("selected", -1);
            // setGlobalCursorIndex はページを変えないので、選んでいる候補のページに移ってから置く
            if (selected >= 0 && selected < static_cast<int>(candidates.size())) {
                list->setPage(selected / list->pageSize());
                list->setGlobalCursorIndex(selected);
            }
            panel.setCandidateList(std::move(list));
            // 選んでいる候補で止まったら、本体が決めた時間のあとに意味を出す
            const auto meaning = listed->str("selectedMeaning");
            const auto key = meaning.empty() ? std::string() : std::to_string(selected) + ":" + meaning;
            if (key != state->meaningKey) {
                state->meaningKey = key;
                state->meaningTimer = key.empty() ? nullptr : after(listed->integer("meaningDelayMs"), [ic, meaning]() {
                    ic->inputPanel().setAuxDown(fcitx::Text(meaning));
                    ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
                });
            }
        }
        ic->updatePreedit();
        ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
    }

    void hidePreedit(fcitx::InputContext *ic) {
        ic->inputPanel().reset();
        ic->updatePreedit();
        ic->updateUserInterface(fcitx::UserInterfaceComponent::InputPanel);
        auto *state = ic->propertyFor(&factory_);
        state->preeditVisible = false;
        state->meaningTimer.reset();
        state->meaningKey.clear();
    }
};

void MeltypeCandidate::select(fcitx::InputContext *ic) const { engine_->selectCandidate(ic, index_); }

class MeltypeEngineFactory : public fcitx::AddonFactory {
    fcitx::AddonInstance *create(fcitx::AddonManager *manager) override { return new MeltypeEngine(manager->instance()); }
};

} // namespace

FCITX_ADDON_FACTORY(MeltypeEngineFactory);
