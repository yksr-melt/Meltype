cask "meltype" do
  version "1.7.1"
  sha256 "7f91df261c359fc5f64ac8f99e1f4ef4ff84483150a3163cab2ef7677b70abf0"

  url "https://github.com/yksr-melt/Meltype/releases/download/v1.7.1/Meltype-#{version}-mac.zip"
  name "Meltype"
  desc "半角/全角を押さずに、日本語と英語をそのまま打てる日本語入力 (英単語は英字のまま、ローマ字は日本語に)"
  homepage "https://github.com/yksr-melt/Meltype"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on arch: :arm64
  depends_on macos: :ventura

  app "Meltype-mac/Meltype Settings.app"

  installer script: {
    executable: "Meltype-mac/install.sh",
    args:       ["--homebrew"],
  }

  uninstall launchctl: "io.github.yksr-melt.Meltype.manual",
            quit:      "io.github.yksr-melt.inputmethod.Meltype",
            script:    {
              executable: "/bin/rm",
              args:       ["-rf", "#{Dir.home}/Library/Input Methods/Meltype.app"],
            }

  zap trash: [
    "~/Library/Application Support/Meltype",
    "~/Library/Logs/Meltype.stderr.log",
    "~/Library/Logs/Meltype.stdout.log",
  ]

  caveats <<~EOS
    初めてのときは、システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype を追加してください
    (出てこないときは、ログアウトしてログインし直してください)。
    設定と学習データも消すときは brew uninstall --zap --cask meltype です。
    Meltype for Mac はプレビュー版です。
  EOS
end
