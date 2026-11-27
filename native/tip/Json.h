// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// Meltype.exe から返ってくる JSON を読むための小さなパーサー (外部のライブラリに頼らない)。

#pragma once

#include <map>
#include <memory>
#include <string>
#include <vector>

namespace meltype {

struct JsonValue {
    enum class Type { Null, Bool, Number, String, Array, Object };
    Type type = Type::Null;
    bool boolean = false;
    double number = 0;
    std::wstring string;
    std::vector<JsonValue> array;
    std::map<std::wstring, JsonValue> object;

    bool IsNull() const { return type == Type::Null; }
    const JsonValue& operator[](const wchar_t* key) const {
        static const JsonValue empty;
        if (type != Type::Object) return empty;
        auto it = object.find(key);
        return it == object.end() ? empty : it->second;
    }
    bool Bool() const { return type == Type::Bool && boolean; }
    int Int() const { return type == Type::Number ? static_cast<int>(number) : 0; }
    const std::wstring& Str() const { return string; }
};

// UTF-8 の JSON を読む。読めなければ false。
bool ParseJson(const std::string& text, JsonValue& out);

// JSON の文字列にする ("…" を含む)。UTF-8 で返す。
std::string JsonString(const std::wstring& text);

}  // namespace meltype
