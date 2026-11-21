// Meltype 用の Mozc 変換ヘルパー。
// Copyright (C) 2026 Yukishiro. Mozc 本体は Google LLC の BSD-3-Clause ライセンス。
//
// 標準入力から 1 行に 1 つの要求を受け取り、標準出力に 1 行で答える (UTF-8)。
//   C<TAB>前の文字列<TAB>読み
//       変換。文節ごとに「読み<US>候補1<US>候補2…」を <RS> でつないで返す (US = 0x1F, RS = 0x1E)。変換できなければ空行。
//   R<TAB>文字列
//       逆変換。文字列の読み (ひらがな) を返す。逆変換できなければ空行。
//   Q   終了する。
// 起動が終わったら "READY" を 1 行出す。引数 1 つ目は Mozc のユーザーデータの場所 (省略可)。
// Mozc には何も学習させない。確定した区切りを覚えさせると、読みを誤った位置で区切る変換を固定してしまう (issue #2014)。

#include <iostream>
#include <memory>
#include <string>
#include <utility>
#include <vector>

#include "absl/strings/str_split.h"
#include "absl/strings/string_view.h"
#include "base/init_mozc.h"
#include "base/system_util.h"
#include "composer/composer.h"
#include "config/config_handler.h"
#include "converter/candidate.h"
#include "converter/converter_interface.h"
#include "converter/segments.h"
#include "engine/engine.h"
#include "engine/engine_factory.h"
#include "protocol/commands.pb.h"
#include "protocol/config.pb.h"
#include "request/conversion_request.h"

#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#endif

namespace {

constexpr char kUnitSeparator = '\x1f';
constexpr char kRecordSeparator = '\x1e';

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
        out += segment.candidate(j).value;
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
    } else {
      std::cout << "\n";
    }
    std::cout.flush();
  }
  return 0;
}
