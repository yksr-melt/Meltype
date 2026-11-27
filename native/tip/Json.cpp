// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

#include "Json.h"

#include "Globals.h"

namespace meltype {
namespace {

class Parser {
public:
    explicit Parser(const std::string& text) : s_(text) {}

    bool Parse(JsonValue& out) {
        if (!Value(out)) return false;
        Skip();
        return i_ == s_.size();
    }

private:
    const std::string& s_;
    size_t i_ = 0;
    int depth_ = 0;

    void Skip() {
        while (i_ < s_.size() && (s_[i_] == ' ' || s_[i_] == '\t' || s_[i_] == '\r' || s_[i_] == '\n')) i_++;
    }

    bool Literal(const char* word) {
        size_t n = strlen(word);
        if (s_.compare(i_, n, word) != 0) return false;
        i_ += n;
        return true;
    }

    bool Value(JsonValue& out) {
        // 入れ子が深すぎるものは読まない (スタックを使い切ってアプリごと落ちないように)
        if (++depth_ > 32) return false;
        bool ok = ValueInner(out);
        depth_--;
        return ok;
    }

    bool ValueInner(JsonValue& out) {
        Skip();
        if (i_ >= s_.size()) return false;
        char c = s_[i_];
        if (c == '{') return Object(out);
        if (c == '[') return Array(out);
        if (c == '"') {
            out.type = JsonValue::Type::String;
            return String(out.string);
        }
        if (Literal("true")) {
            out.type = JsonValue::Type::Bool;
            out.boolean = true;
            return true;
        }
        if (Literal("false")) {
            out.type = JsonValue::Type::Bool;
            out.boolean = false;
            return true;
        }
        if (Literal("null")) {
            out.type = JsonValue::Type::Null;
            return true;
        }
        return Number(out);
    }

    bool Number(JsonValue& out) {
        size_t start = i_;
        while (i_ < s_.size() && strchr("+-0123456789.eE", s_[i_]) != nullptr) i_++;
        if (start == i_) return false;
        out.type = JsonValue::Type::Number;
        out.number = strtod(s_.substr(start, i_ - start).c_str(), nullptr);
        return true;
    }

    static int Hex(char c) {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    bool String(std::wstring& out) {
        i_++;  // "
        std::string raw;
        while (i_ < s_.size()) {
            char c = s_[i_++];
            if (c == '"') {
                out += FromUtf8(raw);
                return true;
            }
            if (c != '\\') {
                raw += c;
                continue;
            }
            if (i_ >= s_.size()) return false;
            char e = s_[i_++];
            switch (e) {
                case '"': raw += '"'; break;
                case '\\': raw += '\\'; break;
                case '/': raw += '/'; break;
                case 'b': raw += '\b'; break;
                case 'f': raw += '\f'; break;
                case 'n': raw += '\n'; break;
                case 'r': raw += '\r'; break;
                case 't': raw += '\t'; break;
                case 'u': {
                    if (i_ + 4 > s_.size()) return false;
                    int code = 0;
                    for (int k = 0; k < 4; k++) {
                        int h = Hex(s_[i_ + k]);
                        if (h < 0) return false;
                        code = code * 16 + h;
                    }
                    i_ += 4;
                    // UTF-16 の 1 単位のまま足す (サロゲートの組もそのままつながる)
                    out += FromUtf8(raw);
                    raw.clear();
                    out += static_cast<wchar_t>(code);
                    break;
                }
                default: return false;
            }
        }
        return false;
    }

    bool Array(JsonValue& out) {
        out.type = JsonValue::Type::Array;
        i_++;
        Skip();
        if (i_ < s_.size() && s_[i_] == ']') {
            i_++;
            return true;
        }
        while (true) {
            JsonValue item;
            if (!Value(item)) return false;
            out.array.push_back(std::move(item));
            Skip();
            if (i_ >= s_.size()) return false;
            if (s_[i_] == ',') {
                i_++;
                continue;
            }
            if (s_[i_] == ']') {
                i_++;
                return true;
            }
            return false;
        }
    }

    bool Object(JsonValue& out) {
        out.type = JsonValue::Type::Object;
        i_++;
        Skip();
        if (i_ < s_.size() && s_[i_] == '}') {
            i_++;
            return true;
        }
        while (true) {
            Skip();
            if (i_ >= s_.size() || s_[i_] != '"') return false;
            std::wstring key;
            if (!String(key)) return false;
            Skip();
            if (i_ >= s_.size() || s_[i_] != ':') return false;
            i_++;
            JsonValue value;
            if (!Value(value)) return false;
            out.object[key] = std::move(value);
            Skip();
            if (i_ >= s_.size()) return false;
            if (s_[i_] == ',') {
                i_++;
                continue;
            }
            if (s_[i_] == '}') {
                i_++;
                return true;
            }
            return false;
        }
    }
};

}  // namespace

bool ParseJson(const std::string& text, JsonValue& out) {
    Parser parser(text);
    return parser.Parse(out);
}

std::string JsonString(const std::wstring& text) {
    std::string out = "\"";
    for (char c : ToUtf8(text)) {
        switch (c) {
            case '"': out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\n': out += "\\n"; break;
            case '\r': out += "\\r"; break;
            case '\t': out += "\\t"; break;
            default:
                if (static_cast<unsigned char>(c) < 0x20) {
                    char buffer[8];
                    sprintf_s(buffer, "\\u%04x", c);
                    out += buffer;
                } else {
                    out += c;
                }
        }
    }
    out += '"';
    return out;
}

}  // namespace meltype
