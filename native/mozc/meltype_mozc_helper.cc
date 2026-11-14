// Meltype 用の Mozc 変換ヘルパー。
// Copyright (C) 2026 Yukishiro. Mozc 本体は Google LLC の BSD-3-Clause ライセンス。
//
// 標準入力から 1 行に 1 つの要求を受け取り、標準出力に 1 行で答える (UTF-8)。
//   C<TAB>前の文字列<TAB>読み
//       変換。文節ごとに「読み<US>候補1<US>候補2…」を <RS> でつないで返す (US = 0x1F, RS = 0x1E)。変換できなければ空行。
//   L<TAB>前の文字列<TAB>読み1<US>確定した文字列1<RS>読み2<US>確定した文字列2…
//       学習。ユーザーが確定した文節の区切りと文字列を Mozc に覚えさせる。覚えたら "OK"、できなければ "NG"。
//   R<TAB>文字列
//       逆変換。文字列の読み (ひらがな) を返す。逆変換できなければ空行。
//   S   学習データを保存する ("OK")。
//   Q   保存して終了する。
// 起動が終わったら "READY" を 1 行出す。引数 1 つ目は学習データの保存先 (省略可)。

#include <cstdint>
#include <iostream>
#include <memory>
#include <string>
#include <utility>
#include <vector>

#include "absl/strings/str_split.h"
#include "absl/strings/string_view.h"
#include "base/init_mozc.h"
#include "base/system_util.h"
#include "base/util.h"
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
    } else if (fields[0] == "L" && fields.size() >= 3) {
      std::cout << (helper.Learn(fields[1], fields[2]) ? "OK" : "NG") << "\n";
    } else if (fields[0] == "R" && fields.size() >= 2) {
      std::cout << helper.Reverse(fields[1]) << "\n";
    } else if (fields[0] == "S") {
      std::cout << (helper.Save() ? "OK" : "NG") << "\n";
    } else {
      std::cout << "\n";
    }
    std::cout.flush();
  }
  // 入力が閉じた (Meltype が終了した) ときも学習データを保存する。
  helper.Save();
  return 0;
}
