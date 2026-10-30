cask "meltype" do
  version "1.4.1-pre.1"
  sha256 "926916ca4a05c8d810175b5796526d1ceaf727508c8fff599d32106b6335a416"

  url "https://github.com/yksr-melt/Meltype/releases/download/v1.4.1-pre.1/Meltype-#{version}-mac.zip"
  name "Meltype"
  desc "半角/全角を押さずに、日本語と英語をそのまま打てる日本語入力 (英単語は英字のまま、ローマ字は日本語に)"
  homepage "https://github.com/yksr-melt/Meltype"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: ">= :ventura"
  depends_on arch: :arm64

  input_method "Meltype-mac/Meltype.app"

  postflight do
    app = "#{Dir.home}/Library/Input Methods/Meltype.app"
    # 署名が自分用 (ad hoc) の版もあるので、隔離属性を外さないと macOS が起動させない (install.sh と同じ)
    system_command "/usr/bin/xattr", args: ["-dr", "com.apple.quarantine", app], must_succeed: false
    # 入力ソースに登録する (#21、#134)
    system_command "#{app}/Contents/MacOS/Meltype", args: ["--register-input-source"], must_succeed: false
    register = "#{app}/Contents/MacOS/MeltypeRegisterInputSource"
    system_command register, args: [app], must_succeed: false if File.executable?(register)
    system_command "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister",
                   args: ["-f", app], must_succeed: false
    system_command "/bin/bash", args: ["#{staged_path}/Meltype-mac/start-input-method.sh", app], must_succeed: false
  end

  uninstall launchctl: "io.github.yksr-melt.Meltype.manual",
            quit:      "io.github.yksr-melt.inputmethod.Meltype"

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
