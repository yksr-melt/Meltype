// Meltype 用の Mozc 変換ヘルパー。
// Copyright (C) 2026 Yukishiro. Mozc 本体は Google LLC の BSD-3-Clause ライセンス。
//
// 標準入力から 1 行に 1 つの要求を受け取り、標準出力に 1 行で答える (UTF-8)。
//   C<TAB>前の文字列<TAB>読み
//       変換。文節ごとに「読み<US>候補1<US>候補2…」を <RS> でつないで返す (US = 0x1F, RS = 0x1E)。変換できなければ空行。
//       ユーザー辞書の語を含む候補 (活用した形も) は、頭に <GS> (0x1D) を付ける。
//   L<TAB>前の文字列<TAB>読み1<US>確定した文字列1<RS>読み2<US>確定した文字列2…
//       学習。ユーザーが確定した文節の区切りと文字列を Mozc に覚えさせる。覚えたら "OK"、できなければ "NG"。
//   R<TAB>文字列
//       逆変換。文字列の読み (ひらがな) を返す。逆変換できなければ空行。
//   D<TAB>読み1<US>語1<US>品詞1<RS>読み2<US>語2<US>品詞2…
//       ユーザー辞書。語をすべて入れ替え、変換に使えるようにしてから "OK" (できなければ "NG") を返す。
//       品詞は Mozc の品詞名 (名詞・人名・動詞ワ行五段…) か、ほかの日本語入力の品詞名 (さ変名詞・ら行五段…)。知らない品詞は名詞。
//       語はメモリにだけ置き、起動し直したら Meltype が送り直す。
//   S   学習データを保存する ("OK")。
//   Q   保存して終了する。
// 起動が終わったら "READY" を 1 行出す。引数 1 つ目は学習データの保存先 (省略可)。
// ブラウザー版は同じ要求を関数で受ける (ファイルの末尾の __EMSCRIPTEN__)。

#include <cstdint>
#include <iostream>
#include <memory>
#include <string>
#include <utility>
#include <vector>

#include "absl/status/status.h"
#include "absl/strings/str_cat.h"
#include "absl/strings/str_split.h"
#include "absl/strings/string_view.h"
#include "base/init_mozc.h"
#include "base/system_util.h"
#include "base/util.h"
#include "composer/composer.h"
#include "config/config_handler.h"
#include "converter/attribute.h"
#include "converter/candidate.h"
#include "converter/converter_interface.h"
#include "converter/segments.h"
#include "dictionary/dictionary_interface.h"
#include "dictionary/user_dictionary_util.h"
#include "dictionary/user_dictionary_importer.h"
#include "engine/engine.h"
#include "engine/engine_factory.h"
#include "protocol/commands.pb.h"
#include "protocol/config.pb.h"
#include "protocol/user_dictionary_storage.pb.h"
#include "request/conversion_request.h"

#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#endif

namespace {

constexpr char kUnitSeparator = '\x1f';
constexpr char kRecordSeparator = '\x1e';
constexpr char kUserDictionaryMark = '\x1d';

class Helper {
 public:
  explicit Helper(std::unique_ptr<mozc::Engine> engine)
      : engine_(std::move(engine)),
        config_(mozc::config::ConfigHandler::DefaultConfig()),
        converter_(engine_->GetConverter()),
        composer_(request_, config_) {}

  // 変換する。segments に結果が入る。
  bool Start(absl::string_view context, absl::string_view reading,
             mozc::Segments* segments) {
    if (!context.empty()) {
      // 前の文字列を文脈にする (失敗しても変換は続ける)。
      (void)converter_->ReconstructHistory(segments, context);
    }
    composer_.Reset();
    composer_.SetPreeditTextForTestOnly(reading);
    return converter_->StartConversion(Request(), segments);
  }

  std::string Convert(absl::string_view context, absl::string_view reading) {
    mozc::Segments segments;
    if (!Start(context, reading, &segments)) return "";
    std::string out;
    for (size_t i = 0; i < segments.conversion_segments_size(); ++i) {
      const mozc::Segment& segment = segments.conversion_segment(i);
      if (i > 0) out += kRecordSeparator;
      out += segment.key();
      for (size_t j = 0; j < segment.candidates_size(); ++j) {
        out += kUnitSeparator;
        if (segment.candidate(j).attributes & mozc::converter::Attribute::USER_DICTIONARY) out += kUserDictionaryMark;
        out += segment.candidate(j).value;
      }
    }
    return out;
  }

  // ユーザーが確定した文節 (読みと文字列) を覚えさせる。同じ読みを変換し直し、文節の区切りを合わせて、
  // 各文節で確定した文字列の候補を選んで確定する (Mozc 自身が確定したときと同じ学習になる)。
  bool Learn(absl::string_view context, absl::string_view clauses) {
    std::vector<std::pair<std::string, std::string>> chosen;
    std::string reading;
    for (absl::string_view record : absl::StrSplit(clauses, kRecordSeparator)) {
      const std::vector<std::string> fields = absl::StrSplit(record, kUnitSeparator);
      if (fields.size() < 2 || fields[0].empty() || fields[1].empty()) return false;
      reading += fields[0];
      chosen.emplace_back(fields[0], fields[1]);
    }
    if (chosen.empty()) return false;

    mozc::Segments segments;
    if (!Start(context, reading, &segments)) return false;
    // 文節の区切りをユーザーが確定したものに合わせる (長さは文字数)。
    std::vector<uint8_t> sizes;
    for (const auto& [key, value] : chosen) {
      const size_t length = mozc::Util::CharsLen(key);
      if (length == 0 || length > 255) return false;
      sizes.push_back(static_cast<uint8_t>(length));
    }
    if (segments.conversion_segments_size() != chosen.size() ||
        !SameKeys(segments, chosen)) {
      if (!converter_->ResizeSegments(&segments, Request(), 0, sizes)) return false;
    }
    if (segments.conversion_segments_size() != chosen.size()) return false;

    for (size_t i = 0; i < chosen.size(); ++i) {
      const mozc::Segment& segment = segments.conversion_segment(i);
      if (segment.key() != chosen[i].first) return false;
      int index = -1;
      for (size_t j = 0; j < segment.candidates_size(); ++j) {
        if (segment.candidate(j).value == chosen[i].second) {
          index = static_cast<int>(j);
          break;
        }
      }
      // Mozc の候補に無い文字列 (Meltype の辞書の候補) は覚えさせられない。
      if (index < 0) return false;
      if (!converter_->CommitSegmentValue(&segments, i, index)) return false;
    }
    converter_->FinishConversion(Request(), &segments);
    return true;
  }

  // 逆変換。文字列 (漢字かな混じり) の読みを返す。逆変換できなければ空。
  // 半角の数字は 1 字ずつ読む (2026年 → にぜろにろくねん) ので、全角にして渡し (２０２６年 → ２０２６ねん)、読みの数字は半角に戻す。
  std::string Reverse(absl::string_view text) {
    std::string key;
    for (const char c : text) {
      if (c >= '0' && c <= '9') {
        key += "\xEF\xBC";  // U+FF10 (０) から
        key += static_cast<char>('\x90' + (c - '0'));
      } else {
        key += c;
      }
    }
    mozc::Segments segments;
    if (!converter_->StartReverseConversion(&segments, key)) return "";
    std::string reading;
    for (size_t i = 0; i < segments.conversion_segments_size(); ++i) {
      const mozc::Segment& segment = segments.conversion_segment(i);
      if (segment.candidates_size() == 0) return "";
      reading += segment.candidate(0).value;
    }
    std::string half;
    for (size_t i = 0; i < reading.size(); ++i) {
      if (i + 2 < reading.size() && reading[i] == '\xEF' && reading[i + 1] == '\xBC' &&
          reading[i + 2] >= '\x90' && reading[i + 2] <= '\x99') {
        half += static_cast<char>('0' + (reading[i + 2] - '\x90'));
        i += 2;
      } else {
        half += reading[i];
      }
    }
    return half;
  }

  bool Save() { return engine_->Sync(); }

  // ユーザー辞書を words の語で入れ替える。Mozc のユーザー辞書のファイル (user_dictionary.db) は使わない。
  // 品詞は、ほかの日本語入力の品詞名 (Microsoft IME・ATOK など) も Mozc の取り込みと同じ対応表 (ConvertEntry) で読み替える。
  // build-mozc-helper.sh・Build-MozcHelper.ps1 が、取り込みの Bazel の visibility をこのパッケージに広げている。
  bool SetDictionary(absl::string_view words) {
    mozc::user_dictionary::UserDictionaryStorage storage;
    mozc::user_dictionary::UserDictionary* dictionary = storage.add_dictionaries();
    for (absl::string_view record : absl::StrSplit(words, kRecordSeparator, absl::SkipEmpty())) {
      const std::vector<absl::string_view> fields = absl::StrSplit(record, kUnitSeparator);
      if (fields.size() < 3) return false;
      const mozc::user_dictionary::RawEntry raw{.key = std::string(fields[0]), .value = std::string(fields[1]), .pos = std::string(fields[2])};
      mozc::user_dictionary::UserDictionary::Entry entry;
      // 知らない品詞名は名詞にする (語を落とさない)
      if (!mozc::user_dictionary::ConvertEntry(raw, &entry)) {
        entry.set_key(raw.key);
        entry.set_value(raw.value);
        entry.set_pos(mozc::user_dictionary::UserDictionary::NOUN);
      }
      mozc::user_dictionary::SanitizeEntry(&entry);
      // Mozc が登録できない語 (読みが長すぎる・使えない文字) は飛ばし、ほかの語は登録する
      if (!mozc::user_dictionary::ValidateEntry(entry).ok()) continue;
      *dictionary->add_entries() = std::move(entry);
    }
    mozc::dictionary::UserDictionaryInterface& user_dictionary = engine_->GetModulesForTesting().GetUserDictionary();
    // 起動したときに Mozc がファイルから読む (裏のスレッド) のを待ってから入れ替える (後から上書きされないように)
    user_dictionary.WaitForReloader();
    return user_dictionary.Load(storage);
  }

 private:
  static bool SameKeys(const mozc::Segments& segments,
                       const std::vector<std::pair<std::string, std::string>>& chosen) {
    for (size_t i = 0; i < chosen.size(); ++i) {
      if (segments.conversion_segment(i).key() != chosen[i].first) return false;
    }
    return true;
  }

  mozc::ConversionRequest Request() {
    mozc::ConversionRequest::Options options = {
        .request_type = mozc::ConversionRequest::CONVERSION,
        .max_conversion_candidates_size = 100,
        .create_partial_candidates = false,
    };
    return mozc::ConversionRequestBuilder()
        .SetComposer(composer_)
        .SetRequestView(request_)
        .SetConfigView(config_)
        .SetOptions(std::move(options))
        .Build();
  }

  std::unique_ptr<mozc::Engine> engine_;
  const mozc::commands::Request request_;
  const mozc::config::Config config_;
  std::shared_ptr<const mozc::ConverterInterface> converter_;
  mozc::composer::Composer composer_;
};

// 要求 1 行 (Q を除く) に答える。標準入出力で動くデスクトップ版と、関数で呼ぶブラウザー版 (下の __EMSCRIPTEN__) が共有する。
std::string Answer(Helper& helper, absl::string_view line) {
  const std::vector<absl::string_view> fields = absl::StrSplit(line, '\t');
  if (fields[0] == "C" && fields.size() >= 3) return helper.Convert(fields[1], fields[2]);
  if (fields[0] == "L" && fields.size() >= 3) return helper.Learn(fields[1], fields[2]) ? "OK" : "NG";
  if (fields[0] == "R" && fields.size() >= 2) return helper.Reverse(fields[1]);
  if (fields[0] == "D" && fields.size() >= 2) return helper.SetDictionary(fields[1]) ? "OK" : "NG";
  if (fields[0] == "S") return helper.Save() ? "OK" : "NG";
  return "";
}

}  // namespace

#ifdef __EMSCRIPTEN__
// ブラウザー版 (web-engine/) は Mozc を WebAssembly にして同じページで動かし、要求 1 行を標準入出力ではなく関数で受ける
// (native/mozc/build-mozc-wasm.sh がビルドする)。
//   meltype_mozc_init() → 最初の要求の前に 1 回呼ぶ。Engine を作れなければ 0
//   meltype_mozc_request("C\t前の文字列\t読み") → 答えの 1 行 (次の呼び出しまで有効)
// 学習データはブラウザーのメモリー上の /mozc に置く。
#include <emscripten/emscripten.h>

#include <filesystem>

namespace {
Helper* g_helper = nullptr;
std::string g_answer;
}  // namespace

extern "C" EMSCRIPTEN_KEEPALIVE int meltype_mozc_init() {
  if (g_helper != nullptr) return 1;
  // 置き場が無いと、Mozc は学習データを開けず、何も覚えない (デスクトップ版は Meltype が作ってから起動する)
  std::filesystem::create_directories("/mozc");
  mozc::SystemUtil::SetUserProfileDirectory("/mozc");
  auto engine = mozc::EngineFactory::Create();
  if (!engine.ok()) return 0;
  g_helper = new Helper(*std::move(engine));
  return 1;
}

extern "C" EMSCRIPTEN_KEEPALIVE const char* meltype_mozc_request(const char* line) {
  g_answer = Answer(*g_helper, line);
  return g_answer.c_str();
}

// Emscripten は main が無いと実行時の初期化をしない
int main() { return 0; }
#else
int main(int argc, char** argv) {
  mozc::InitMozc(argv[0], &argc, &argv);
#ifdef _WIN32
  _setmode(_fileno(stdin), _O_BINARY);
  _setmode(_fileno(stdout), _O_BINARY);
#endif
  if (argc >= 2 && argv[1][0] != '\0') {
    mozc::SystemUtil::SetUserProfileDirectory(argv[1]);
  }

  auto engine = mozc::EngineFactory::Create();
  if (!engine.ok()) {
    std::cout << "ERROR " << engine.status().message() << std::endl;
    return 1;
  }
  Helper helper(*std::move(engine));

  std::cout << "READY" << std::endl;
  std::string line;
  while (std::getline(std::cin, line)) {
    if (!line.empty() && line.back() == '\r') line.pop_back();
    if (absl::string_view(line).substr(0, line.find('\t')) == "Q") break;
    std::cout << Answer(helper, line) << "\n";
    std::cout.flush();
  }
  // 入力が閉じた (Meltype が終了した) ときも学習データを保存する。
  helper.Save();
  return 0;
}
#endif
