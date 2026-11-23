// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// ブラウザー版 (web-ime/) の Mozc。meltype_mozc_helper と同じ要求 1 行を、標準入出力ではなく関数で受けて答える。
//   meltype_mozc_request("C\t前の文字列\t読み") → 答えの 1 行 (次の呼び出しまで有効)
// 要求と答えの形は meltype_mozc_helper.cc の先頭にある。
// ワークアラウンド: Helper を共有するために meltype_mozc_helper.cc をそのまま取り込み、その main は名前を変えて使わない
// (Helper を別のファイルに分けると、Windows・Mac・Linux のビルド (BUILD.fragment・Build-MozcHelper.ps1) も変えることになるため)。

#define main meltype_mozc_helper_main
#include "converter/meltype_mozc_helper.cc"
#undef main

#include <emscripten/emscripten.h>

namespace {

Helper* g_helper = nullptr;
std::string g_answer;

}  // namespace

extern "C" {

// 最初の要求の前に 1 回呼ぶ (学習データはブラウザーのメモリー上の /mozc に置く)。作れなければ 0。
EMSCRIPTEN_KEEPALIVE int meltype_mozc_init() {
  if (g_helper != nullptr) return 1;
  mozc::SystemUtil::SetUserProfileDirectory("/mozc");
  auto engine = mozc::EngineFactory::Create();
  if (!engine.ok()) return 0;
  g_helper = new Helper(*std::move(engine));
  return 1;
}

EMSCRIPTEN_KEEPALIVE const char* meltype_mozc_request(const char* line) {
  if (g_helper == nullptr && !meltype_mozc_init()) return nullptr;
  const std::vector<absl::string_view> fields = absl::StrSplit(line, '\t');
  if (fields[0] == "C" && fields.size() >= 3) {
    g_answer = g_helper->Convert(fields[1], fields[2]);
  } else if (fields[0] == "L" && fields.size() >= 3) {
    g_answer = g_helper->Learn(fields[1], fields[2]) ? "OK" : "NG";
  } else if (fields[0] == "R" && fields.size() >= 2) {
    g_answer = g_helper->Reverse(fields[1]);
  } else if (fields[0] == "S") {
    g_answer = g_helper->Save() ? "OK" : "NG";
  } else {
    g_answer = "";
  }
  return g_answer.c_str();
}

}  // extern "C"

// Emscripten は main が無いと実行時の初期化をしない
int main() { return 0; }
