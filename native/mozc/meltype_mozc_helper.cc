// Meltype 用の Mozc 変換ヘルパー。
// Copyright (C) 2026 Meltype Contributors. Mozc 本体は Google LLC の BSD-3-Clause ライセンス。
//
// 標準入力から 1 行に 1 つの要求を受け取り、標準出力に 1 行で答える (UTF-8)。
//   C<TAB>前の文字列<TAB>読み
//       変換。文節ごとに「読み<US>候補1<US>候補2…」を <RS> でつないで返す (US = 0x1F, RS = 0x1E)。変換できなければ空行。
//       ユーザー辞書の語を含む候補 (活用した形も) は、頭に <GS> (0x1D) を付ける。
//   R<TAB>文字列
//       逆変換。文字列の読み (ひらがな) を返す。逆変換できなければ空行。
//   D<TAB>読み1<US>語1<US>品詞1<RS>読み2<US>語2<US>品詞2…
//       ユーザー辞書。語をすべて入れ替え、変換に使えるようにしてから "OK" (できなければ "NG") を返す。
//       品詞は Mozc の品詞名 (名詞・人名・動詞ワ行五段…) か、ほかの日本語入力の品詞名 (さ変名詞・ら行五段…)。知らない品詞は名詞。
//       語はメモリにだけ置き、起動し直したら Meltype が送り直す。
//   Q   終了する。
// 起動が終わったら "READY" を 1 行出す。引数 1 つ目は学習データの保存先 (省略可)。

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
        const auto& candidate = segment.candidate(j);
        // Mozc の設定を切り替える候補は入力する文字ではないので、Core に渡さない (#2442)。
        if (candidate.attributes & mozc::converter::Attribute::COMMAND_CANDIDATE) continue;
        out += kUnitSeparator;
        if (candidate.attributes & mozc::converter::Attribute::USER_DICTIONARY) out += kUserDictionaryMark;
        out += candidate.value;
      }
    }
    return out;
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

}  // namespace

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
    const std::vector<absl::string_view> fields = absl::StrSplit(line, '\t');
    if (fields.empty() || fields[0] == "Q") break;
    if (fields[0] == "C" && fields.size() >= 3) {
      std::cout << helper.Convert(fields[1], fields[2]) << "\n";
    } else if (fields[0] == "R" && fields.size() >= 2) {
      std::cout << helper.Reverse(fields[1]) << "\n";
    } else if (fields[0] == "D" && fields.size() >= 2) {
      std::cout << (helper.SetDictionary(fields[1]) ? "OK" : "NG") << "\n";
    } else {
      std::cout << "\n";
    }
    std::cout.flush();
  }
  return 0;
}
