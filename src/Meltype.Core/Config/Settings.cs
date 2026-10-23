// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meltype.Input;

namespace Meltype.Config;

public enum InputStyle
{
    /// <summary>ローマ字入力。</summary>
    [Description("ローマ字入力")] Romaji,
    /// <summary>JIS かな入力。</summary>
    [Description("かな入力 (JIS、α版)")] Kana,
}

/// <summary>英語か日本語かの自動判定の強さ。</summary>
public enum DetectionLevel
{
    /// <summary>少しでも英語らしければ英字にする。</summary>
    [Description("積極的 (Aggressive)")] Aggressive,
    /// <summary>既定。短い語 (no, to) は前後が英語のときだけ英字。</summary>
    [Description("標準 (Balanced)")] Balanced,
    /// <summary>確信度が高いときだけ英字にする。</summary>
    [Description("慎重 (Conservative)")] Conservative,
    /// <summary>自動では切り替えず、提案だけ出す。</summary>
    [Description("手動 (提案のみ)")] Manual,
}

/// <summary>変換ボックスの文字の大きさ。</summary>
public enum CompositionSize
{
    /// <summary>入力欄の文字の高さ (キャレットの高さ) に合わせる。分からなければ「中」。</summary>
    [Description("自動 (入力欄に合わせる)")] Auto,
    [Description("小")] Small,
    [Description("中")] Medium,
    [Description("大")] Large,
    [Description("特大")] ExtraLarge,
    [Description("最大")] Huge,
}

/// <summary>変換ボックスを出す位置。</summary>
public enum CompositionPlacement
{
    /// <summary>打っている行に重ねる (入力欄の中に打っているように見える)。</summary>
    [Description("入力位置に重ねる")] Overlay,
    [Description("カーソルの下")] BelowCaret,
    /// <summary>入力位置の上に別の枠で出す (下の行や入力欄の下の部分を隠さない)。</summary>
    [Description("カーソルの上")] AboveCaret,
}

/// <summary>変換ボックスの色。</summary>
public enum CompositionTheme
{
    /// <summary>既定。黒っぽい背景に白い文字。</summary>
    [Description("ダーク")] Dark,
    /// <summary>白っぽい背景に黒い文字。</summary>
    [Description("ライト")] Light,
    /// <summary>Windows の「既定のアプリ モード」(ライト / ダーク) に合わせる。</summary>
    [Description("Windows の設定に合わせる")] System,
}

/// <summary>変換ボックスの不透明度。</summary>
public enum CompositionOpacity
{
    [Description("100% (透けない)")] Opaque,
    [Description("90%")] Percent90,
    [Description("80%")] Percent80,
    [Description("70%")] Percent70,
}

/// <summary>かな漢字変換のエンジン。</summary>
public enum ConversionEngine
{
    /// <summary>Mozc で変換し、使えないときは OS の変換エンジン。候補は両方。</summary>
    [Description("両方 (Mozc を優先)")] Hybrid,
    [Description("Mozc")] Mozc,
    /// <summary>OS の変換エンジン (Windows では Microsoft IME)。</summary>
    [Description("Microsoft IME")] System,
}

/// <summary>句読点の組み合わせ (Microsoft IME と同じ 4 通り)。, と . を打ったときに出す文字。</summary>
public enum PunctuationStyle
{
    /// <summary>既定。</summary>
    [Description("、。")] Japanese,
    /// <summary>論文などで使う全角のカンマとピリオド。</summary>
    [Description("，．")] FullWidthCommaPeriod,
    [Description("，。")] FullWidthCommaKuten,
    [Description("、．")] ToutenFullWidthPeriod,
}

// config.json に数値で書いても読めるので、番号は前の版のまま (1 は消した IME 自動切替)。
public enum InputMode
{
    /// <summary>Meltype 自身の変換ボックスで入力する (半角/全角 不要)。</summary>
    [Description("Meltype キーボード (変換ボックスで入力)")] Keyboard = 0,
    /// <summary>Windows の IME (TSF) として、入力欄に直接入力する。キーボードフックは使わない。</summary>
    [Description("Meltype IME (入力欄に直接入力)")] Tsf = 2,
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class AppRule
{
    [DisplayName("プロセス名"), Description("例: code.exe / chrome.exe / WindowsTerminal.exe")]
    public string Process { get; set; } = "";

    [DisplayName("有効"), Description("OFF にすると、このアプリでは変換ボックスを出しません。Meltype IME でも英字がそのまま入ります。")]
    public bool Enabled { get; set; } = true;

    [DisplayName("種類"), Description("コード = コードエディターやターミナル。基本は英数で、コメントや \"…\" の中だけ日本語を判定します。")]
    public AppProfile Profile { get; set; } = AppProfile.General;

    /// <summary>ユーザーが作った種類 (<see cref="Settings.AppKinds"/>) の名前。指定があれば Profile より優先。</summary>
    public string? Kind { get; set; }

    public override string ToString() => $"{Process}: {(Enabled ? "ON" : "OFF")}, {Kind ?? Profile.ToString()}";
}

/// <summary>
/// ユーザーが作るアプリの種類 (例: 「チャット」「ゲーム」)。一般 / コード を元に、判定の強さ・ライブ変換・最初の入力モードを変えられる。
/// null の項目は全体の設定のまま。
/// </summary>
public sealed class AppKind
{
    public string Name { get; set; } = "";
    public AppProfile Base { get; set; } = AppProfile.General;
    public DetectionLevel? DetectionLevel { get; set; }
    public bool? LiveConversion { get; set; }
    /// <summary>このアプリに切り替えたら英数 (直接入力) から始める。</summary>
    public bool StartInEnglish { get; set; }

    public AppKind Clone() => (AppKind)MemberwiseClone();
}

/// <summary>
/// プロファイル (仕事用・趣味用・SNS 用など)。設定の値をまとめて名前を付けたもの。
/// 使っているプロファイルの値は Settings そのもの。ほかのプロファイルの値は Values に入れておき、切り替えたときに Settings に読み込む。
/// </summary>
public sealed class SettingsProfile
{
    public string Name { get; set; } = "";

    /// <summary>このプロファイルの設定の値 (config.json と同じ形)。全員で共通の項目 (ログ・更新など) は入れない。</summary>
    public JsonObject? Values { get; set; }

    public SettingsProfile Clone() => new() { Name = Name, Values = Values?.DeepClone() as JsonObject };
}

/// <summary>アプリの種類。アプリに合わせて、英語と日本語のどちらを基本にするかを変える。</summary>
public enum AppProfile
{
    /// <summary>一般 (文章を書くアプリ)。日本語が基本で、英単語を自動で見分ける。</summary>
    [Description("一般")] General,
    /// <summary>コードエディター・ターミナル。英数が基本で、コメントと文字列 ("…") の中だけ日本語を判定する。</summary>
    [Description("コード")] Code,
    /// <summary>ゲーム。Meltype は何もしない (キーを横取りせず、Windows の IME の ON/OFF にも触らない。ゲームのチャットは Windows の IME で打つ)。</summary>
    [Description("ゲーム")] Game,
}

/// <summary>
/// config.json の内容。値はすべて保守的な初期値にしてある (設計書 §18)。
/// インスタンスは不変として扱い、変更時は Clone して差し替える。
/// </summary>
public sealed class Settings
{
    // 型の情報はビルド時に作ったもの (SettingsJsonContext)。Mac・Linux の NativeAOT でも読める (issue #151)。
    // 字下げ・コメントと末尾の , を許す・列挙型は名前で読み書き は SettingsJsonContext の属性で指定している。
    private static JsonSerializerOptions JsonOptions => SettingsJsonContext.Default.Options;
    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<Settings> JsonType => SettingsJsonContext.Default.Settings;

    [Category("1. 全般"), DisplayName("Meltype を有効にする")]
    public bool Enabled { get; set; } = true;

    [Category("1. 全般"), DisplayName("日本語キーボードのときだけ動作"),
     Description("Windows の入力言語が日本語のときだけ動作します。韓国語・英語などでは入力処理と IME の自動制御を停止し、日本語に戻すと再開します。物理キーボードの JIS / US 配列や IME の「あ」「A」の状態は問いません。全プロファイル共通です。")]
    public bool JapaneseKeyboardOnly { get; set; }

    [Category("1. 全般"), DisplayName("遠隔操作などの入力も処理する"),
     Description("AnyDesk・VNC などの遠隔操作ソフトや、ULE4JIS などキー配列を変えるソフト (JIS キーボードを US 配列で使う) から届いたキーも、手で打ったキーと同じように処理します (OFF だと、ほかのソフトが送ったキーはそのままアプリに渡し、変換ボックスを開きません。キー配列を変えるソフトは Meltype より後に起動してください)。遠隔操作ソフトとキーボードのマクロ・自動入力のソフトは見分けられないので、ON にするとどちらも処理の対象になります。Meltype 自身が送ったキーは、ON でも処理しません。Chrome リモート デスクトップ・Parsec で操作されている間は、OFF でも処理します (Parsec は、接続していなくても Parsec が動いている間はずっと処理するので、AutoHotkey などが送ったキーも変換の対象になります)。全プロファイル共通です。")]
    public bool AllowInjectedInput { get; set; }

    [Category("1. 全般"), DisplayName("動作モード"),
     Description("Tsf = Windows の IME として入力欄に直接入力 (Win + Space で Meltype を選ぶ) / Keyboard = Meltype の変換ボックスで入力 (英単語は自動で英字、Space で変換、Enter で確定)")]
    [JsonConverter(typeof(InputModeJsonConverter))]
    public InputMode Mode { get; set; } = InputMode.Tsf;

    [Category("1. 全般"), DisplayName("入力モード切り替えキー"),
     Description("日本語／英数の入力モードを切り替えるキーを複数登録できます。Windows や他のアプリで使うキー操作を登録すると、元の操作が使えなくなったり、入力モードの切り替えと同時に動いたりする場合があります。")]
    public List<ToggleShortcut>? ToggleShortcuts { get; set; }

    // v5 以前の設定を読み込むためだけに残す。移行後は保存しない。
    [Browsable(false), JsonInclude, JsonPropertyName("HankakuTogglesKeyboard")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal bool? LegacyHankaku { get; set; }

    [JsonIgnore, Browsable(false)]
    public IReadOnlyList<ToggleShortcut> EffectiveToggleShortcuts => ToggleShortcuts ?? ToggleShortcut.Defaults();

    [Category("1. 全般"), DisplayName("無変換で英数 / 変換で日本語"),
     Description("Keyboard モードで、変換ボックスが出ていないときの 無変換 キーで英数 (直接入力) に、変換 キーで日本語入力にします (Mac の 英数 / かな キーと同じく、押す前のモードによらず決まったモードになります)。もう日本語入力のときの 変換 キーは今までどおり選択した文字の再変換です。変換ボックスが出ている間の 無変換・変換 は今までどおりで、切り替えません。「コード」のアプリのコードの行では、変換 キーでその行を日本語にします (半角/全角 と同じ)。")]
    public bool ConvertKeysSwitchKeyboard { get; set; }

    [Category("1. 全般"), DisplayName("左 Alt で英数 / 右 Alt で日本語"),
     Description("左 Alt の単独押しで英数 (直接入力) に、右 Alt の単独押しで日本語入力にします (半角/全角 キーの無い US 配列向け。Meltype キーボードと Meltype IME で使えます)。Alt + Tab などの組み合わせや Alt + クリックでは切り替えません。単独押しでアプリのメニューバーに移らなくなります。Meltype キーボードでは、変換ボックスが出ている間は切り替えません (Meltype IME では、変換中の文字を確定して切り替えます)。「コード」のアプリのコードの行では、右 Alt でその行を日本語にします (半角/全角 と同じ。Meltype キーボード)。")]
    public bool AltKeysSwitchKeyboard { get; set; }

    [Category("1. 全般"), DisplayName("確定後も文脈に合わせて直す"),
     Description("英語とも日本語とも読める語 (i, sushi など) を確定した後、次の語で英語か日本語かがはっきりしたら自動で確定し直します (i → 胃 と確定した後に want と打つと I want)。")]
    public bool AutoCorrectAfterCommit { get; set; } = true;

    [Category("1. 全般"), DisplayName("英数状態でもローマ字を検知"),
     Description("Keyboard モードまたは Meltype IME の英数状態でも単語の打ち始めを判定し、ローマ字 (日本語) なら自動で日本語入力に戻します。")]
    public bool DirectModeAutoDetect { get; set; } = true;

    [Category("1. 全般"), DisplayName("/ $ @ で始まる語はそのまま入力"),
     Description("Keyboard モードで、入力欄・行の先頭か空白の直後に打った / $ @ と、続く名前 (空白まで) は変換せず、打つたびにそのままアプリに渡します (AI エージェントの /command・$skill・@ファイル名 の補完を選びやすく)。名前の後に空白を打つと、普通の自動判定に戻ります。文章の入力欄 (コードエディターのコード以外) の / は「・」として入り、Space で / にしてから名前を打つと、名前はそのまま渡します。")]
    public bool SigilWordsDirect { get; set; } = true;

    [Category("1. 全般"), DisplayName("定型文を出す記号"),
     Description("Keyboard モードで、入力欄・行の先頭か空白の直後にこの記号と名前を打って Space / Tab を押すと、トレイの「定型文...」で登録した文に置き換えます (;sig + Space → 署名)。名前が登録したものでなければ何もしません。置き換えた直後に Esc を押すと、打った ;名前 に戻します。空にすると使いません。定型文を 1 つも登録していなければ、記号は今までどおり入力します。")]
    public string SnippetMark { get; set; } = ";";

    [Category("1. 全般"), DisplayName("英単語の前後に半角スペース"),
     Description("確定するときに、日本語と英単語の間に半角スペースを入れます (今日はGitHubにpushした → 今日は GitHub に push した)。数字だけの語 (3時) には入れません。")]
    public bool SpaceAroundEnglish { get; set; }

    [Category("1. 全般"), DisplayName("Shift + Space で半角スペース"),
     Description("日本語入力のときの Shift + Space を、全角スペースではなく半角スペースにします (プログラミングの癖で Shift + Space を半角スペースに使う人向け)。")]
    public bool ShiftSpaceHalfWidth { get; set; }

    [Category("1. 全般"), DisplayName("Enter は見えているとおりに確定"),
     Description("Enter で確定するとき、打ち間違いの自動修正をせず、変換ボックスに見えているとおりに確定します (Space で変換するときの修正は今までどおりです)。")]
    public bool EnterCommitsAsShown { get; set; }

    [Category("1. 全般"), DisplayName("F9 / F10 の 1 回目は打ったまま"),
     Description("入力中の表示が英字でも、F10 (Ctrl+O) の 1 回目は常に打ったまま (小文字) の英字にして、2 回目以降で すべて大文字 → 先頭だけ大文字 と切り替えます (Microsoft IME・Google 日本語入力と同じ)。OFF のときは、英字で見えている語の 1 回目の F10 で すべて大文字になります。")]
    public bool AlphanumericKeyStartsAsTyped { get; set; }

    [Category("1. 全般"), DisplayName("カッコを全角で入力"),
     Description("日本語入力中の ( ) を全角の（ ）にします。英字の直後でも全角のままです (OFF のときは、打った幅の半角のままで、英語の中では半角です)。")]
    public bool FullWidthParentheses { get; set; }

    [Category("1. 全般"), DisplayName("/ を全角のスラッシュ／にする"),
     Description("かなのすぐ後ろで打った / を、中黒「・」ではなく全角のスラッシュ／にします。「/ キーで中黒「・」」が ON のときは、そちらが優先です。")]
    public bool FullWidthSlash { get; set; }

    [Category("1. 全般"), DisplayName("[ ] を角括弧［ ］にする"),
     Description("日本語入力中の [ ] を「」ではなく全角の角括弧［ ］にします (Google 日本語入力の「記号」の設定と同じ)。")]
    public bool SquareBrackets { get; set; }

    [Category("1. 全般"), DisplayName("Esc で一段ずつ戻す"),
     Description("入力中の Esc を、打った内容を全部消すのではなく一段ずつ戻します (漢字 → ひらがな → 打ったローマ字 → 消す。見た目が変わらない段は飛ばします)。戻した語は、日本語 / 英語の判定として覚えません。")]
    public bool EscStepsBack { get; set; }

    [Category("1. 全般"), DisplayName("変換中の Esc は選んだ文節だけをかなに戻す"),
     Description("文節に分けて変換しているとき、Esc で変換全体をやめるのではなく、選んでいる文節だけをひらがなに戻します (ほかの文節はそのまま)。もうひらがなの文節か、文節が 1 つだけのときの Esc は、今までどおり変換をやめます。")]
    public bool EscRevertsSelectedClause { get; set; }

    [Category("1. 全般"), DisplayName("変換中の F6〜F10 は選んだ文節だけに効かせる"),
     Description("文節に分けて変換しているとき、F6〜F10 (ひらがな・カタカナ・半角カタカナ・全角英数・半角英数) を全体ではなく、選んでいる文節だけに効かせます (今日は|Google の「今日は」だけをカタカナにするなど)。")]
    public bool FunctionKeysAffectSelectedClause { get; set; }

    [Category("1. 全般"), DisplayName("Ctrl + K / L で文節の長さを変える"),
     Description("変換中の Ctrl + K で選んでいる文節を縮め、Ctrl + L で伸ばします (Shift + ← → と同じ。以前の IME の手グセのショートカット)。")]
    public bool CompatibleShortcuts { get; set; }

    [Category("1. 全般"), DisplayName("Shift + Enter で選んだ文節までを確定"),
     Description("文節に分けて変換しているとき、Shift + Enter で、選んでいる文節までを確定し、残りの文節は未確定のまま (ひらがなに戻して) 続けて編集できるようにします。")]
    public bool PartialCommit { get; set; }

    [Category("1. 全般"), DisplayName("Control キーを macOS 標準の日本語入力と同じにする"),
     Description("入力中の Control + J / K / L をひらがな / カタカナ / 全角英字 (F6 / F7 / F9) に、Control + H / D を前 / 後ろの 1 文字を消す、N / P を次 / 前の候補、B / F / A / E をカーソル移動、O / I を変換中の文節を伸ばす / 縮める、にします (macOS 標準の日本語入力と同じ)。ON のときは、ATOK 風の Control + U / I / O / P より優先します。")]
    public bool MacControlKeys { get; set; }

    [Category("1. 全般"), DisplayName("カッコを打ったら中にカーソルを移動"),
     Description("カッコだけ（「」・（）・() など）を確定したときに、カーソルをカッコの中へ移動します (← キーを自動で送ります)。")]
    public bool CursorIntoBrackets { get; set; }

    [Category("1. 全般"), DisplayName("Control キーの割り当て"),
     Description("入力中の Control + 英字に、別のキーの働きを割り当てます。「T=F6;Y=F7;U=F8;I=F9;O=F10」のように、英字=キー名を ; で区切って書きます (F キーが無い HHKB やノートパソコン向け)。キー名: F6〜F10・Left・Right・Up・Down・Home・End・Delete・Back・Enter・Esc・Tab。ここに書いた英字は、組み込みの Control + U/I/O/P/H より優先します。")]
    public string ControlKeyBindings { get; set; } = "";

    [Category("1. 全般"), DisplayName("計算式を変換すると結果を候補に出す"),
     Description("数字と + - * / ( ) だけの式を打って Space で変換すると、計算した結果 (1+2*3 → 7) と「式=結果」を候補に出します。")]
    public bool CalculatorCandidates { get; set; }

    [Category("1. 全般"), DisplayName("打鍵の間隔を判定の記録に残す (実験)"),
     Description("判定の記録 (ログ) に、キーとキーの間の時間 (ミリ秒) だけを残します (打った文字は残しません)。英語と日本語の見分けに打鍵のリズムが使えるかを調べるための実験用です。")]
    public bool LogKeyIntervals { get; set; }

    [Category("1. 全般"), DisplayName("かな + w を笑い (ねw・さw) にする"),
     Description("ね・さ などの 1 文字のかなの後ろの w を、英単語 (new・saw) にせず笑いの w にします (なるほどnew → なるほどねw)。英単語の new や saw を打つ人は OFF のままにしてください。")]
    public bool LaughterAfterKana { get; set; }

    [Category("1. 全般"), DisplayName("英語 / 日本語の学習を前の文脈ごとに分ける"),
     Description("英字 / かなに直した語 (new → ねw など) の学習を、直前が英語だったか日本語だったかごとに別々に覚えます。同じローマ字でも、英語の文の中と日本語の文の中で使い分けられます。ON にする前に覚えた学習は、どちらの文脈でも使います。")]
    public bool ContextualLearning { get; set; }

    [Category("1. 全般"), DisplayName("スペースを全角で入力 (Shift + Space で半角)"),
     Description("日本語入力のときの Space (何も打っていないとき) を全角スペースにし、Shift + Space を半角スペースにします (全角スペースを多く使う人向け。Meltype キーボード・Meltype IME で使えます)。")]
    public bool FullWidthSpaceByDefault { get; set; }

    [Category("1. 全般"), DisplayName("入力中も変換候補をいくつか表示"),
     Description("ひらがなを打っている間も、Space を押す前から変換候補を (5 件まで) 変換ボックスに出します。候補をクリックすると、その候補で変換を始めます。")]
    public bool ShowCandidatesWhileTyping { get; set; }

    /// <summary>旧形式の追加キーを読み込むためだけの項目。正規化時に一覧へ移し、保存時には出力しない。</summary>
    [Browsable(false), JsonInclude, JsonPropertyName("ToggleKeyboardShortcuts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? LegacyToggleKeyboardShortcuts { get; set; }

    [Category("1. 全般"), DisplayName("#番号で GitHub の Issue・PR のタイトルを候補に出す"),
     Description("#123 のように打って Space で変換すると、下の「GitHub のリポジトリ」の Issue・Pull Request のタイトルを候補に出します (開発者向け)。打った番号を GitHub (api.github.com) に問い合わせるので、既定は OFF です。非公開のリポジトリは、環境変数 MELTYPE_GITHUB_TOKEN にトークンを入れると読めます。調べた結果はこのセッションの間だけ覚えます。")]
    public bool GitHubIssueCandidates { get; set; }

    [Category("1. 全般"), DisplayName("GitHub のリポジトリ"),
     Description("「#番号で GitHub の Issue・PR のタイトルを候補に出す」で問い合わせるリポジトリ。「所有者/名前」の形で書きます (例: yksr-melt/Meltype)。")]
    public string GitHubRepository { get; set; } = "";

    [Category("1. 全般"), DisplayName("クリップボードの文字を変換候補に出す"),
     Description("「くりっぷぼーど」「ぺーすと」を変換したとき、クリップボードにある 1 行の文字 (200 文字まで) を候補に出します。変換した瞬間に 1 回だけ読み、監視・保存・送信はしません。パスワード管理ソフトなど、履歴に残さない印のついた内容は読みません。Windows のみ。既定は OFF です。")]
    public bool ClipboardCandidates { get; set; }

    [Category("1. 全般"), DisplayName("Meltype の一時停止 / 再開のホットキー"),
     Description("Meltype 全体の一時停止 / 再開に使うキーの組み合わせ (例: Ctrl+Alt+F10)。空のときは Ctrl+Alt+F12 (使えなければ Ctrl+Alt+F11 など)。変更は Meltype を起動し直すと効きます。Windows のみ。変換・無変換キーが無い US 配列のキーボード向けです。")]
    public string PauseHotkey { get; set; } = "";

    [Category("1. 全般"), DisplayName("句読点"),
     Description("日本語の中で , と . を打ったときに出す句読点です (Microsoft IME と同じ 4 通り)。論文などで「，．」「，。」「、．」を使うときに変えます。かな入力の 、 。 のキーにも効きます。数字の間の . , (1.5、1,000) や英単語の中の . (tetr.io) は半角のままです。")]
    public PunctuationStyle Punctuation { get; set; } = PunctuationStyle.Japanese;

    [Category("1. 全般"), DisplayName("英字の後ろの , . も句読点にする"),
     Description("英字のすぐ後ろで打った , . も、日本語の句読点 (設定「句読点」の形) にします (IME, → IME、)。OFF だと、変換ボックスの中が英字だけなら半角の , . のままです (英文を打つとき)。ON でも、キャレットの前が英語 (Hello + world.) のときと、英単語の中の . (tetr.io) は半角のままです。")]
    public bool JapanesePunctuationAfterEnglish { get; set; }

    [Category("1. 全般"), DisplayName("ライブ変換"),
     Description("Keyboard モードで、Space を押さなくても打ったそばから漢字に変換して表示します。")]
    public bool LiveConversion { get; set; } = true;

    [Category("1. 全般"), DisplayName("変換エンジン"),
     Description("かな漢字変換に使うエンジン。「両方」は Mozc (Google 日本語入力のオープンソース版) で変換し、Mozc が使えないときは Microsoft IME で変換します。候補には両方の候補が出ます。")]
    public ConversionEngine ConversionEngine { get; set; } = ConversionEngine.Hybrid;

    [Category("1. 全般"), DisplayName("Zenzai を使う (Windows・実験)"),
     Description("ニューラルかな漢字変換モデル Zenzai (azooKey の zenz) で変換します。llama.cpp の llama-server と zenz の GGUF ファイルを自分で用意し、下の 2 つの場所を指定してください (どちらも Meltype には同梱しません)。使えないとき・応答が遅いときは、今までの変換エンジンで変換します。変換結果は、Mozc の文節ごとの候補で作れるときは Mozc の文節に分け、作れないときは読み全体で 1 つの文節になります。既定は OFF です。")]
    public bool UseZenzai { get; set; }

    [Category("1. 全般"), DisplayName("Zenzai: llama-server の場所"),
     Description("llama.cpp の llama-server (Windows では llama-server.exe) の場所。「Zenzai を使う」が ON のときに使います。")]
    public string ZenzaiServerPath { get; set; } = "";

    [Category("1. 全般"), DisplayName("Zenzai: モデル (GGUF) の場所"),
     Description("zenz のモデルファイル (例: zenz-v3.1-small の ggml-model-Q5_K_M.gguf) の場所。「Zenzai を使う」が ON のときに使います。")]
    public string ZenzaiModelPath { get; set; } = "";

    [Category("1. 全般"), DisplayName("英訳の候補"),
     Description("変換の候補の後ろに英訳も出します (複雑な → complex, complicated)。JMdict のよく使う語から。選んだ英訳は少しずつ前に出ます。")]
    public bool TranslationCandidates { get; set; } = true;

    [Category("1. 全般"), DisplayName("予測変換の候補"),
     Description("打っている途中に、続きの候補を変換ボックスの下に出します (前に確定した語句・ユーザー辞書・選び直した変換の学習・英単語の続き)。Tab / Shift+Tab で選んで Enter で確定します。確定した語句は %LOCALAPPDATA%\\Meltype\\phrases.txt に暗号化せずに覚え (この PC の外には送りません)、「学習データをリセット」で消えます。")]
    public bool PredictiveCandidates { get; set; } = true;

    [Category("1. 全般"), DisplayName("予測変換に英字の社名・サービス名"),
     Description("「予測変換の候補」が ON のとき、読みを打ちかけたところで、英字で書く社名・サービス名・語の候補も出します (しゃお → Xiaomi、ぎっとは → GitHub)。読みを 3 文字以上打ったときだけ出します。英単語の続き (decis → decision) は「予測変換の候補」だけで出ます。")]
    public bool EnglishNamePredictions { get; set; }

    [Category("1. 全般"), DisplayName("Tab で変換"),
     Description("打っている途中に Tab を押すと、Space と同じく変換を始めます (Microsoft IME と同じ)。Tab で始めた変換の中では Tab で次の候補、Shift+Tab で前の候補、Enter で確定します。もしかして・手動の提案・予測変換の候補が出ているときは、今までどおりそちらに使います。英語で終わっているとき・Space で始めた変換の中では、今までどおり確定してから Tab をアプリに渡します。")]
    public bool TabConversion { get; set; } = true;

    [Category("1. 全般"), DisplayName("変換したあとに続けて打っても確定しない"),
     Description("Space で変換したあとに続けて文字を打っても、変換した部分を確定せず、続けて打った文字と一緒に変換ボックスに残します (選んだ候補は、もう一度変換したときにも使います)。OFF なら Microsoft IME と同じく、変換した部分を確定してから続きを打ちます。")]
    public bool ContinueAfterConversion { get; set; }

    [Category("1. 全般"), DisplayName("入力中のキーの役割を分ける"),
     Description("入力中 (変換ボックスに文字があるとき) の Space・変換・無変換・ひらがな/カタカナ キーの役割を分けます。Space: 英語・日本語の判定にかかわらず候補を開く (英字で見えている語も、確定して空白を入れずに候補を出す)。変換: 日本語を優先して変換。無変換: 英字にする。ひらがな/カタカナ: ひらがな ⇔ カタカナ。確定は Enter です。何も打っていないときの各キーは今までどおりです。")]
    public bool SeparateKeyRoles { get; set; }

    [Category("1. 全般"), DisplayName("英字を主に入力 (変換キー / ↓ で日本語に)"),
     Description("自動判定をせず、打った英字をそのまま入力中にします (Space は空白、Enter は英字のまま確定)。日本語にしたい語は、変換キーか ↓ を押すと、ローマ字として読んで変換します。コマンドを多く打つ使い方向けで、Microsoft IME の「主に英語を入力する」と同じ動きです。")]
    public bool EnglishFirstInput { get; set; }

    [Category("1. 全般"), DisplayName("子音の略入力を候補に出す"),
     Description("子音だけの略入力 (ast → 明日、ngt → ノート など) を、変換の候補に出します。候補は同梱の candidates.txt の英字の読みから出します。既定は OFF です。")]
    public bool ConsonantAbbreviations { get; set; }

    [Category("1. 全般"), DisplayName("候補の意味を表示"),
     Description("変換中に同じ候補で少し (約 1.5 秒) 止まると、その候補の意味をウィクショナリー日本語版から候補の一覧の横に出します (日本語の意味が無い語は JMdict の英訳: 橋 → bridge)。同音異義語を選ぶときの手がかりに。")]
    public bool ShowCandidateMeanings { get; set; } = true;

    [Category("1. 全般"), DisplayName("打ったキーを表示"),
     Description("打ったキー (ローマ字: kyouhagoogle) を小さく出します。Meltype キーボードでは変換ボックスの文字の下に、Meltype IME では候補の一覧の上に出します。打ち間違いに気づきやすくなります。")]
    public bool ShowTypedKeys { get; set; } = true;

    [Category("1. 全般"), DisplayName("打ち間違いを直す"),
     Description("Space・Enter で変換・確定するときに打ち間違いを直します。ローマ字: 読めない子音が残ったとき、隣のキーの押し間違い・入れ替わり・抜けを 1 文字だけ直します (onegaishimsu → お願いします、sumimasne → すみません。よく使う語の読みになるときだけ)。英語: Windows の自動修正の一覧にある打ち間違いを直します (teh → the、recieve → receive)。")]
    public bool CorrectTypos { get; set; } = true;

    [Category("1. 全般"), DisplayName("/ キーで中黒「・」"),
     Description("かなのすぐ後ろで打った / を中黒「・」にします (いーろん/ますく → イーロン・マスク)。英字・数字の後ろ (and/or、3/4、URL) は / のままです。文章の入力欄の打ち始めの / は、この設定に関係なく「・」になり、Space で / にできます。OFF のときも、z/ または / を打って Space で「・」にできます。")]
    public bool SlashAsMiddleDot { get; set; }

    [Category("1. 全般"), DisplayName("入力モードをカーソルの近くに表示"),
     Description("入力欄をクリックしたときと 半角/全角 を押したときに、カーソルの近くに「あ」(日本語) か「A」(英数) を一瞬表示します。Meltype キーボードの使用中は Windows の IME を OFF にしているので、タスクバーの IME の表示は常に「A」になります。今のモードはこの表示かトレイの Meltype のアイコンで確認してください。")]
    public bool ShowModeIndicator { get; set; } = true;

    [Category("1. 全般"), DisplayName("入力欄に入ったときも入力モードを表示"),
     Description("「入力モードをカーソルの近くに表示」が ON のとき、入力欄をクリックしたとき (フォーカスが入ったとき) にも「あ」「A」を出します。OFF にすると、半角/全角 を押したときだけ出します。")]
    public bool ShowModeIndicatorOnFocus { get; set; } = true;

    [Category("1. 全般"), DisplayName("入力モードの表示の色: 日本語 (あ)"),
     Description("「あ」の背景色を #RRGGBB で書きます (例: #007ACC)。空なら青。壁紙・アプリのテーマで見えにくいときに変えます。")]
    public string ModeIndicatorJapaneseColor { get; set; } = "";

    [Category("1. 全般"), DisplayName("入力モードの表示の色: 英数 (A)"),
     Description("「A」の背景色を #RRGGBB で書きます (例: #D32F2F で赤)。空なら灰色。英数になっているのに気づかずに打ち始めてしまうときは、目立つ色にします。")]
    public string ModeIndicatorDirectColor { get; set; } = "";

    [Category("1. 全般"), DisplayName("入力モードの表示の色: 文字"),
     Description("「あ」「A」の文字の色を #RRGGBB で書きます (例: #000000)。空なら白。")]
    public string ModeIndicatorTextColor { get; set; } = "";

    [Category("1. 全般"), DisplayName("変換ボックスの位置"),
     Description("入力位置に重ねる: 打っている文字が入力欄の中の入力位置にそのまま出ているように見えます。カーソルの下: 入力位置の下に別の枠で出します (今までの出し方)。カーソルの上: 入力位置の上に別の枠で出します (候補の一覧が入力欄や下の行を隠さない)。入力位置が分からないアプリでは、どれも入力欄の下に出します。")]
    public CompositionPlacement CompositionPlacement { get; set; } = CompositionPlacement.Overlay;

    [Category("1. 全般"), DisplayName("変換ボックスの文字の大きさ"),
     Description("自動: 入力欄の文字の高さに合わせます (小さな入力欄では小さく出ます)。入力欄の文字の高さが分からないアプリでは「中」になります。小さな文字が読みにくいときは「特大」「最大」も選べます。")]
    public CompositionSize CompositionSize { get; set; } = CompositionSize.Auto;

    [Category("1. 全般"), DisplayName("変換ボックスのフォント"),
     Description("変換ボックスの文字のフォントです。既定は Yu Gothic UI です。この PC に無いフォントを選んでいたときは既定のフォントで出します。絵文字はカラーで出せるときはそのフォントで描きます。")]
    public string CompositionFont { get; set; } = "";

    [Category("1. 全般"), DisplayName("変換ボックスの色"),
     Description("ダーク: 黒っぽい背景に白い文字 (今までの見た目)。ライト: 白っぽい背景に黒い文字。Windows の設定に合わせる: Windows の「既定のアプリ モード」(設定の 個人用設定 > 色) に合わせます。")]
    public CompositionTheme CompositionTheme { get; set; } = CompositionTheme.Dark;

    [Category("1. 全般"), DisplayName("変換ボックスの不透明度"),
     Description("変換ボックスを少し透かして、後ろの文字を見えるようにします。100% は透けません。")]
    public CompositionOpacity CompositionOpacity { get; set; } = CompositionOpacity.Opaque;

    /// <summary>変換ボックスをライトの色で出すか。windowsLight は Windows のアプリ モードがライトか (分からなければ null = ダーク)。</summary>
    public bool CompositionIsLight(bool? windowsLight) => CompositionTheme switch
    {
        CompositionTheme.Light => true,
        CompositionTheme.System => windowsLight ?? false,
        _ => false,
    };

    /// <summary>変換ボックスの不透明度 (0.7〜1)。</summary>
    [Browsable(false), JsonIgnore]
    public double CompositionOpacityValue => CompositionOpacity switch
    {
        CompositionOpacity.Percent90 => 0.9,
        CompositionOpacity.Percent80 => 0.8,
        CompositionOpacity.Percent70 => 0.7,
        _ => 1.0,
    };

    [Category("1. 全般"), DisplayName("通知を出す"),
     Description("Meltype を有効・一時停止にしたときなどに、画面の右下に通知を出します (Windows の通知の音も鳴ります)。OFF にすると通知も音も出しません。")]
    public bool ShowNotifications { get; set; } = true;

    [Category("9. 更新"), DisplayName("自動で更新する"),
     Description("新しい版が公開されたら自動でダウンロードし、次に Meltype を起動したとき (Windows にサインインしたとき) に更新します。トレイの「更新して再起動」で今すぐ更新もできます。設定・学習データはそのまま残ります。")]
    public bool AutoUpdate { get; set; } = true;

    [Category("1. 全般"), DisplayName("入力方式"), Description("ローマ字入力 / かな入力 (JIS、α版: 試験中)。Meltype キーボードでは、かな入力を選ぶと JIS かな配列で入力し (Shift+E = ぃ, Shift+Z = っ, Shift+ね = 、)、打ったキーの英字が英単語なら英字で見せます。")]
    [JsonConverter(typeof(InputStyleJsonConverter))]
    public InputStyle InputStyle { get; set; } = InputStyle.Romaji;

    [Category("2. 判定"), DisplayName("自動判定の強さ"),
     Description("積極的 = 英語らしければすぐ英字 / 標準 = 短い語 (no, to, ga) は前後が英語のときだけ英字 / 慎重 = 確信度が高いときだけ英字 / 手動 = 自動では切り替えず提案だけ (変換ボックスで Tab を押すと提案どおり英字に)。Meltype キーボード・英数状態の検知・かな入力のすべてに効きます。")]
    public DetectionLevel DetectionLevel { get; set; } = DetectionLevel.Balanced;

    [Category("2. 判定"), DisplayName("区切りを点数で選ぶ (α版)"),
     Description("変換ボックスの中で英語と日本語をどこで区切るかを、あり得る区切り方を全部比べて一番自然なもので決めます (α版: 試験中)。英単語の後ろの「は」(medalsha → medalsは、presidentha → presidentは) などが正しく分かれやすくなります。OFF なら今までどおり、先頭から順に決めます。")]
    public bool ScoredSegmentation { get; set; }

    /// <summary>判定の強さを反映した日本語判定の閾値 (英数状態の検知)。</summary>
    [Browsable(false), JsonIgnore]
    public int EffectiveJapaneseThreshold => JapaneseThreshold + DetectionLevel switch
    {
        DetectionLevel.Aggressive => -1,
        DetectionLevel.Conservative => 3,
        _ => 0,
    };

    [Category("2. 判定"), DisplayName("日本語判定の閾値"), Description("JapaneseScore がこの値以上、かつ EnglishScore をこの値以上上回ったときだけ切り替えます。大きいほど誤爆が減ります。")]
    public int JapaneseThreshold { get; set; } = 4;

    [Category("2. 判定"), DisplayName("Typo 判定を使う"), Description("辞書語との編集距離 1 以内を補助的な日本語スコアとして加点します。Typo 一致だけでは切り替えません。")]
    public bool TypoEnabled { get; set; } = true;

    [Category("3. 保留"), DisplayName("最大保留キー数"), Description("判定のために保留する文字数の上限。超えたら判定不能としてそのまま出力します。")]
    public int MaxPendingKeys { get; set; } = 6;

    /// <summary>config.json の形式のバージョン。古い既定値を持つ設定ファイルを移行するのに使う。</summary>
    [Browsable(false)]
    public int SettingsVersion { get; set; } = CurrentVersion;

    /// <summary>初めて起動したときの「使い方」を見せたか (見せたら true にして、次からは出さない)。</summary>
    [Browsable(false)]
    public bool WelcomeShown { get; set; }

    /// <summary>
    /// Meltype IME (TSF) が入っているのを見つけて、動作モードを Meltype IME にしたか (一度だけ切り替える。後でユーザーが戻したら、そのまま)。
    /// </summary>
    [Browsable(false)]
    public bool TsfIntroduced { get; set; }

    /// <summary>
    /// Meltype IME を入れるかを聞いたか (Meltype IME が入る前の版から更新した人に、起動したときに一度だけ聞く)。
    /// </summary>
    [Browsable(false)]
    public bool TsfOffered { get; set; }

    /// <summary>設定画面の下に、項目の説明を出すか (設定画面のチェックボックスで切り替える)。</summary>
    [Browsable(false)]
    public bool SettingsShowHelp { get; set; } = true;

    /// <summary>設定画面で、プルダウン・数値の上でホイールを回したときに値を変えるか (OFF なら設定の一覧をスクロールする)。</summary>
    [Browsable(false)]
    public bool SettingsWheelChangesValue { get; set; } = true;

    /// <summary>プロファイル (仕事用・趣味用・SNS 用など)。設定画面の上と、トレイのメニューで切り替える。</summary>
    [Browsable(false)]
    public List<SettingsProfile> Profiles { get; set; } = [];

    /// <summary>使っているプロファイルの名前。</summary>
    [Browsable(false)]
    public string ActiveProfile { get; set; } = DefaultProfileName;

    public const string DefaultProfileName = "標準";

    /// <summary>
    /// プロファイルごとに変えない項目 (どのプロファイルでも共通)。Meltype の ON/OFF・ログ・更新と、内部で使う値。
    /// </summary>
    private static readonly HashSet<string> SharedKeys =
    [
        nameof(Profiles), nameof(ActiveProfile), nameof(SettingsVersion), nameof(WelcomeShown),
        nameof(Enabled), nameof(JapaneseKeyboardOnly), nameof(AllowInjectedInput), nameof(FileLog), nameof(LogTypedText), nameof(AutoUpdate),
        // 入力の方式 (Windows の IME として入力するか) は PC 全体の選び方なので、プロファイルで変えない
        nameof(Mode), nameof(TsfIntroduced), nameof(TsfOffered),
        // 設定画面の見た目は、プロファイルを切り替えても変えない
        nameof(SettingsShowHelp), nameof(SettingsWheelChangesValue),
    ];

    /// <summary>今の設定の値のうち、プロファイルに入れるもの。</summary>
    public JsonObject ProfileValues()
    {
        var values = JsonSerializer.SerializeToNode(this, JsonType)!.AsObject();
        foreach (var key in SharedKeys) values.Remove(key);
        return values;
    }

    /// <summary>プロファイルの名前の一覧 (使っているものも含む。並びは作った順)。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ProfileNames => Profiles.Select(p => p.Name).ToList();

    /// <summary>
    /// プロファイルを切り替えた設定を返す。今の値は今のプロファイルに入れておき、切り替え先の値を読み込む。
    /// 切り替え先が無ければ、今の設定のまま (名前だけが変わることはない)。
    /// </summary>
    public Settings SwitchProfile(string name)
    {
        var current = Clone().Normalize();
        if (name == current.ActiveProfile || current.Profiles.FirstOrDefault(p => p.Name == name) is not { } target) return current;
        current.Profiles.First(p => p.Name == current.ActiveProfile).Values = current.ProfileValues();
        // 共通の項目は今の値のまま、プロファイルの項目だけを切り替え先の値にする
        var merged = JsonSerializer.SerializeToNode(current, JsonType)!.AsObject();
        InitializeToggleShortcuts(target.Values ??= new JsonObject(), name);
        // 前の版で保存したプロファイルには、今は共通にした項目 (動作モードなど) が入っていることがあるので飛ばす
        foreach (var (key, value) in target.Values ?? [])
        {
            if (!SharedKeys.Contains(key)) merged[key] = value?.DeepClone();
        }
        var next = merged.Deserialize(JsonType) ?? current;
        next.ActiveProfile = name;
        next.Normalize();
        // 除外したキーを保存用の値にも反映し、再読み込み時に同じ登録を復活させない。
        next.Profiles.First(p => p.Name == next.ActiveProfile).Values = next.ProfileValues();
        return next;
    }

    /// <summary>今の値をそのまま写した新しいプロファイルを足して、それに切り替えた設定を返す。名前が空・使われているなら null。</summary>
    public Settings? AddProfile(string name)
    {
        name = name.Trim();
        var current = Clone().Normalize();
        if (name.Length == 0 || current.Profiles.Any(p => p.Name == name)) return null;
        current.Profiles.First(p => p.Name == current.ActiveProfile).Values = current.ProfileValues();
        current.Profiles.Add(new SettingsProfile { Name = name, Values = current.ProfileValues() });
        current.ActiveProfile = name;
        return current;
    }

    /// <summary>プロファイルの名前を変えた設定を返す。新しい名前が空・使われているなら null。</summary>
    public Settings? RenameProfile(string oldName, string newName)
    {
        newName = newName.Trim();
        var current = Clone().Normalize();
        if (newName.Length == 0 || current.Profiles.Any(p => p.Name == newName) || current.Profiles.FirstOrDefault(p => p.Name == oldName) is not { } profile) return null;
        profile.Name = newName;
        if (current.ActiveProfile == oldName) current.ActiveProfile = newName;
        return current;
    }

    /// <summary>プロファイルを消した設定を返す。最後の 1 つは消せない (null)。使っているものを消したら、残りの最初のものに切り替える。</summary>
    public Settings? RemoveProfile(string name)
    {
        var current = Clone().Normalize();
        if (current.Profiles.Count <= 1 || current.Profiles.All(p => p.Name != name)) return null;
        if (current.ActiveProfile == name) current = current.SwitchProfile(current.Profiles.First(p => p.Name != name).Name);
        current.Profiles.RemoveAll(p => p.Name == name);
        return current;
    }

    /// <summary>書き出したプロファイルのファイルの印 (ほかの JSON と見分ける)。</summary>
    private const string ProfileFileFormat = "meltype-profile";

    /// <summary>
    /// 使っているプロファイルを、ほかの人に渡せる形 (JSON) にする。共通の項目 (ON/OFF・ログ・更新) は入れない。
    /// アプリ別設定 (プロセス名) は入るので、渡す前に見られてもよいか確かめてもらう。
    /// </summary>
    public string ExportProfile()
    {
        var file = new JsonObject
        {
            ["format"] = ProfileFileFormat,
            ["version"] = CurrentVersion,
            ["name"] = ActiveProfile,
            ["values"] = ProfileValues(),
        };
        return file.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// 書き出したプロファイルを新しいプロファイルとして足し、それに切り替えた設定を返す。読めないファイルなら null。
    /// 同じ名前があれば「名前 (2)」にする。知らない項目と共通の項目は無視し、値は範囲に収める (Normalize)。
    /// </summary>
    public Settings? ImportProfile(string json)
    {
        JsonObject? values;
        string name;
        try
        {
            if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject file ||
                file["format"]?.GetValue<string>() != ProfileFileFormat || file["values"] is not JsonObject raw) return null;
            name = (file["name"]?.GetValue<string>() ?? "").Trim();
            InitializeToggleShortcuts(raw, name.Length == 0 ? "読み込んだプロファイル" : name);
            // 既定の設定に、知っている項目だけを重ねてから読み直す (型の違う値はここで例外になる)
            var merged = JsonSerializer.SerializeToNode(new Settings().Normalize(), JsonType)!.AsObject();
            foreach (var (key, value) in raw)
            {
                if (merged.ContainsKey(key) && !SharedKeys.Contains(key)) merged[key] = value?.DeepClone();
            }
            var imported = merged.Deserialize(JsonType) ?? new Settings();
            imported.ActiveProfile = name.Length == 0 ? "読み込んだプロファイル" : name;
            values = imported.Normalize().ProfileValues();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            return null;
        }
        if (name.Length == 0) name = "読み込んだプロファイル";
        if (name.Length > 50) name = name[..50];
        var current = Clone().Normalize();
        var unique = name;
        for (var i = 2; current.Profiles.Any(p => p.Name == unique); i++) unique = $"{name} ({i})";
        current.Profiles.First(p => p.Name == current.ActiveProfile).Values = current.ProfileValues();
        current.Profiles.Add(new SettingsProfile { Name = unique, Values = values });
        return current.SwitchProfile(unique);
    }

    /// <summary>
    /// 設定画面で変えた項目だけを今の設定 (current) に重ねた設定を返す。opened は画面を開いた時点の画面の値、edited は閉じる時点の画面の値。
    /// 画面を開いている間にトレイなどで変えた項目 (一時停止・動作モード・判定の強さ・プロファイルなど) は、画面で変えていなければ current のまま。
    /// プロファイルごとの値はプロファイルの名前ごとに比べる。トレイでプロファイルを切り替えても、画面で変えた値は画面で選んでいたプロファイルに入る。
    /// 使っているプロファイルは、画面で切り替えたときだけ画面のものにする。プロファイルの一覧 (足す・名前を変える・消す) は画面のもの (トレイでは切り替えるだけ)。
    /// </summary>
    public static Settings MergeEdits(Settings opened, Settings edited, Settings current)
    {
        if (edited.ToJson() == opened.ToJson()) return current.Clone().Normalize();
        var openedJson = JsonSerializer.SerializeToNode(opened, JsonType)!.AsObject();
        var editedJson = JsonSerializer.SerializeToNode(edited, JsonType)!.AsObject();
        var merged = JsonSerializer.SerializeToNode(current, JsonType)!.AsObject();
        foreach (var key in SharedKeys)
        {
            if (key is nameof(Profiles) or nameof(ActiveProfile)) continue;
            if (!JsonNode.DeepEquals(openedJson[key], editedJson[key])) merged[key] = editedJson[key]?.DeepClone();
        }
        var active = edited.ActiveProfile != opened.ActiveProfile || edited.Profiles.All(p => p.Name != current.ActiveProfile) ? edited.ActiveProfile : current.ActiveProfile;
        var profiles = new List<SettingsProfile>();
        foreach (var name in edited.ProfileNames)
        {
            var values = MergeValues(opened.ValuesOf(name), edited.ValuesOf(name)!, current.ValuesOf(name));
            if (name == active)
            {
                foreach (var (key, value) in values)
                {
                    if (!SharedKeys.Contains(key)) merged[key] = value?.DeepClone();
                }
            }
            profiles.Add(new SettingsProfile { Name = name, Values = values });
        }
        var next = merged.Deserialize(JsonType) ?? current.Clone();
        next.Profiles = profiles;
        next.ActiveProfile = active;
        return next.Normalize();

        // 画面で足した (名前を変えた・読み込んだ) プロファイルは、画面の値のまま
        static JsonObject MergeValues(JsonObject? opened, JsonObject edited, JsonObject? current)
        {
            if (opened is null || current is null) return edited.DeepClone().AsObject();
            var values = current.DeepClone().AsObject();
            foreach (var (key, value) in edited)
            {
                if (!JsonNode.DeepEquals(opened[key], value)) values[key] = value?.DeepClone();
            }
            return values;
        }
    }

    /// <summary>プロファイルの値。使っているプロファイルは今の値。そのプロファイルが無ければ null。</summary>
    private JsonObject? ValuesOf(string name) =>
        name == ActiveProfile ? ProfileValues() : Profiles.FirstOrDefault(p => p.Name == name) is { } profile ? profile.Values ?? [] : null;

    /// <summary>旧プロファイルの追加キーを移行する。未設定と明示的な空の一覧は区別する。</summary>
    private static void InitializeToggleShortcuts(JsonObject? values, string profile = DefaultProfileName, bool report = true)
    {
        if (values is null) return;
        if (values[nameof(ToggleShortcuts)] is null)
            values[nameof(ToggleShortcuts)] = JsonSerializer.SerializeToNode(
                values["HankakuTogglesKeyboard"] is JsonValue legacy && legacy.TryGetValue<bool>(out var enabled) && !enabled
                    ? new List<ToggleShortcut>() : ToggleShortcut.Defaults(), SettingsJsonContext.Default.ListToggleShortcut);
        values.Remove("HankakuTogglesKeyboard");
        if (values["ToggleKeyboardShortcuts"] is JsonValue old && old.TryGetValue<string>(out var text))
        {
            var shortcuts = values[nameof(ToggleShortcuts)]!.Deserialize(SettingsJsonContext.Default.ListToggleShortcut)!;
            AppendLegacyToggleShortcuts(shortcuts, text, profile, report);
            values[nameof(ToggleShortcuts)] = JsonSerializer.SerializeToNode(shortcuts, SettingsJsonContext.Default.ListToggleShortcut);
        }
        values.Remove("ToggleKeyboardShortcuts");
    }

    /// <summary>旧文字列を共通のキー一覧へ追加し、既存の登録と重なるものは一つにまとめる。</summary>
    private static void AppendLegacyToggleShortcuts(List<ToggleShortcut> shortcuts, string? text, string profile, bool report)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var part in text.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!KeyShortcut.TryParse(part, out var parsed))
            {
                if (report) Diagnostics.Log.Warn($"旧入力モード切り替えキーを移行できませんでした: {part}; 理由: キーの書式を解釈できません; プロファイル: {profile}");
                continue;
            }
            var shortcut = new ToggleShortcut
            {
                Key = ToggleShortcut.NormalizeHankakuKey(parsed.Vk),
                Modifiers = (parsed.Ctrl ? ShortcutModifiers.Control : 0) | (parsed.Alt ? ShortcutModifiers.Alt : 0)
                    | (parsed.Shift ? ShortcutModifiers.Shift : 0) | (parsed.Win ? ShortcutModifiers.Win : 0),
            };
            if (!shortcuts.Any(existing => existing is not null && existing.Overlaps(shortcut))) shortcuts.Add(shortcut);
        }
    }

    /// <summary>旧追加キーを移行して読み込み専用項目を空にし、繰り返し正規化しても再追加しない。</summary>
    private void MigrateLegacyToggleShortcuts(bool report)
    {
        ToggleShortcuts ??= LegacyHankaku == false ? [] : ToggleShortcut.Defaults();
        AppendLegacyToggleShortcuts(ToggleShortcuts, LegacyToggleKeyboardShortcuts, ActiveProfile, report);
        LegacyToggleKeyboardShortcuts = null;
        foreach (var profile in Profiles ?? [])
            if (profile is not null) InitializeToggleShortcuts(profile.Values, profile.Name, report);
    }

    public const int CurrentVersion = 7;

    [Category("5. 学習"), DisplayName("ユーザー学習を使う"),
     Description("OFF にすると、選び直した変換・英字 / かなに直した語・予測変換の語句・選んだ英訳を新しく覚えません (今まで覚えた分は使います)。今まで覚えた分を消すときは、トレイの「学習データを消す」を使います。")]
    public bool LearningEnabled { get; set; } = true;

    [Category("6. IME"), DisplayName("IME 操作のタイムアウト (ms)")]
    public int ImeTimeoutMs { get; set; } = 300;

    [Category("7. アプリ"), DisplayName("貼り付けで入力するアプリ"),
     Description("確定した文字を 1 文字ずつ送ると取り違えるアプリ (DaVinci Resolve で「あいうえお」→「あああああ」)。ここに書いたアプリ (プロセス名、カンマ区切り) では、クリップボードを使って貼り付けで入れます (元のクリップボードの中身は戻します)。Qt アプリはここに書かなくても自動で貼り付けになります。")]
    public string PasteApps { get; set; } = "Resolve.exe";

    [Category("7. アプリ"), DisplayName("貼り付けを使わないアプリ"),
     Description("Qt アプリ (LINE・OBS など) では、確定した文字をクリップボード経由 (貼り付け) で入れます。1 文字ずつキーとして送ると、keyup がアプリに届かない環境で最初の 1 文字が繰り返されるためです。ここに書いたアプリ (プロセス名、カンマ区切り) では貼り付けを使わず、これまでどおり 1 文字ずつ送ります (Ctrl+V が貼り付けではないアプリなど)。")]
    public string NoPasteApps { get; set; } = "";

    /// <summary>このアプリでは確定した文字を貼り付けで入れるか (<see cref="PasteApps"/>)。</summary>
    public bool UsesPaste(string? processName) => ContainsApp(PasteApps, processName);

    /// <summary>このアプリでは貼り付けを使わないか (<see cref="NoPasteApps"/>)。</summary>
    public bool UsesNoPaste(string? processName) => ContainsApp(NoPasteApps, processName);

    [Category("7. アプリ"), DisplayName("入力欄とみなすアプリ"),
     Description("画面を自分で描くため、文字を打つ所なのに入力欄と判定されず変換ボックスが出ないアプリ (Premiere Pro など)。ここに書いたアプリ (プロセス名、カンマ区切り。例: Adobe Premiere Pro.exe) では、入力欄と判定できなくても、フォーカスのある所を入力欄として扱います。1 文字のショートカット (V・C など) も変換ボックスに入るようになるので、ショートカットを使うときは Ctrl + 半角/全角 で一時停止してください。")]
    public string TextInputApps { get; set; } = "";

    /// <summary>このアプリでは、入力欄と判定できなくてもフォーカスのある所を入力欄として扱うか (<see cref="TextInputApps"/>)。</summary>
    public bool TreatsAsTextInput(string? processName) => ContainsApp(TextInputApps, processName);

    /// <summary>
    /// #RRGGBB (# は無くてもよい・#RGB も可) の色を (赤, 緑, 青) にする。空・読めない値なら null (既定の色を使う: issue #359)。
    /// </summary>
    public static (int R, int G, int B)? ParseColor(string? text)
    {
        var hex = (text ?? "").Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value)) return null;
        return ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    }

    /// <summary>
    /// カンマ・セミコロン区切りのプロセス名の一覧に processName があるか (大文字小文字は無視)。
    /// 「Adobe Premiere Pro.exe」のように空白を含む名前はそのまま 1 つの名前として比べる。
    /// 前からの書き方 (空白区切り「a.exe b.exe」) は、空白で分けたものがすべて .exe で終わるときだけ 1 つずつと比べる。
    /// </summary>
    private static bool ContainsApp(string? list, string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        foreach (var entry in (list ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(entry, processName, StringComparison.OrdinalIgnoreCase)) return true;
            var apps = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (apps.Length > 1 && apps.All(app => app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) &&
                apps.Any(app => string.Equals(app, processName, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    [Category("7. アプリ"), DisplayName("全画面アプリでは無効"), Description("ゲームや動画など全画面のウィンドウではキーを保留しません。")]
    public bool ExcludeFullscreen { get; set; } = true;

    [Category("7. アプリ"), DisplayName("ゲームでは止める"),
     Description("Steam・Epic Games・Riot Games・EA・Ubisoft・Battle.net・Xbox のゲームのフォルダーにあるアプリ (ウィンドウ表示のゲームも) では、Meltype は何もしません (キーを横取りせず、Windows の IME にも触らないので、ゲームのチャットは Windows の IME で打てます)。ほかのゲームは、アプリ別設定で種類を「ゲーム」にしてください。")]
    public bool StopInGames { get; set; } = true;

    [Category("7. アプリ"), DisplayName("アプリ別設定"), Description("プロセス名ごとに、ON/OFF と種類を指定します。種類「コード」(コードエディター・ターミナル) では基本は英数のままで、コメント (// # -- など) と文字列 (\"…\" など) の中だけ日本語を判定します。コードの行で 半角/全角 を押すと、その行だけ日本語で入力できます。README.md などの文章ファイルを開いているときは一般として扱います。")]
    public List<AppRule> AppRules { get; set; } = DefaultAppRules();

    [Category("7. アプリ"), DisplayName("独自の種類"), Description("アプリ別設定の「種類」に使える、自分で作る種類です。一般 / コード を元に、判定の強さ・ライブ変換・最初は英数にするか を変えられます (「全体と同じ」なら上の設定のまま)。")]
    public List<AppKind> AppKinds { get; set; } = [];

    [Category("8. ログ"), DisplayName("ファイルにログを書く"), Description("%LOCALAPPDATA%\\Meltype\\meltype.log にログを書きます (OFF でも、トレイの「ログ / 判定理由」で見られるログは Meltype が動いている間だけメモリに残ります)。判定した語の先頭の数文字・アプリ名・入力欄の名前が含まれます。確定した文字列などは「ログに入力した文字を残す」が ON のときだけ残ります。")]
    public bool FileLog { get; set; }

    [Category("8. ログ"), DisplayName("ログに入力した文字を残す"), Description("確定した文字列・打った英字・読み・直した語をログに残します (不具合を調べるとき用)。OFF なら文字数だけを残します。パスワード欄では入力を扱わないので残りません。")]
    public bool LogTypedText { get; set; }

    public static List<AppRule> DefaultAppRules() =>
    [
        // キー入力が別のマシン/VM に届くアプリ。保留すると相手側の IME 状態と食い違う。
        new() { Process = "mstsc.exe", Enabled = false },
        new() { Process = "msrdc.exe", Enabled = false },
        new() { Process = "vmconnect.exe", Enabled = false },
        new() { Process = "VirtualBoxVM.exe", Enabled = false },
        new() { Process = "vmware-vmx.exe", Enabled = false },
        new() { Process = "vmware.exe", Enabled = false },
        .. CodeApps.Select(process => new AppRule { Process = process, Enabled = true, Profile = AppProfile.Code }),
    ];

    /// <summary>既定で「コード」として扱うアプリ (コードエディター・IDE・ターミナル)。</summary>
    public static readonly string[] CodeApps =
    [
        "Code.exe", "Code - Insiders.exe", "Cursor.exe", "Windsurf.exe", "zed.exe", "devenv.exe",
        "idea64.exe", "pycharm64.exe", "webstorm64.exe", "rider64.exe", "clion64.exe", "goland64.exe",
        "phpstorm64.exe", "rubymine64.exe", "datagrip64.exe", "studio64.exe", "sublime_text.exe", "notepad++.exe",
        "WindowsTerminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wezterm-gui.exe", "alacritty.exe", "mintty.exe", "rio.exe",
        // アプリの中にスクリプトエディターがある 3DCG ソフト (Maya の Script Editor)
        "maya.exe",
    ];

    public bool IsAppEnabled(string? processName) => FindRule(processName)?.Enabled ?? true;

    /// <summary>
    /// ゲームとして Meltype を止めるか: アプリ別設定の種類が「ゲーム」か、アプリ別設定が無く、ゲームのフォルダーにあるアプリ (looksLikeGame) で「ゲームでは止める」が ON。
    /// </summary>
    public bool IsGame(string? processName, bool looksLikeGame) =>
        ProfileFor(processName) == AppProfile.Game || (StopInGames && looksLikeGame && FindRule(processName) is null);

    /// <summary>アプリの種類 (アプリ別設定に無ければ一般)。独自の種類なら、その元にした種類。</summary>
    public AppProfile ProfileFor(string? processName) =>
        KindFor(processName)?.Base ?? FindRule(processName)?.Profile ?? AppProfile.General;

    /// <summary>アプリに割り当てた独自の種類 (無ければ null)。</summary>
    public AppKind? KindFor(string? processName) =>
        FindRule(processName)?.Kind is { Length: > 0 } name ? AppKinds.FirstOrDefault(k => k.Name == name) : null;

    /// <summary>アプリの独自の種類で変えた項目 (判定の強さ・ライブ変換) を反映した設定。変えていなければ自分自身。</summary>
    public Settings ForApp(string? processName)
    {
        if (KindFor(processName) is not { } kind || (kind.DetectionLevel is null && kind.LiveConversion is null)) return this;
        var copy = Clone();
        if (kind.DetectionLevel is { } level) copy.DetectionLevel = level;
        if (kind.LiveConversion is { } live) copy.LiveConversion = live;
        return copy;
    }

    private AppRule? FindRule(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return null;
        foreach (var rule in AppRules)
        {
            if (string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase)) return rule;
        }
        return null;
    }

    public Settings Clone()
    {
        var copy = (Settings)MemberwiseClone();
        copy.AppRules = AppRules.Select(r => new AppRule { Process = r.Process, Enabled = r.Enabled, Profile = r.Profile, Kind = r.Kind }).ToList();
        copy.AppKinds = AppKinds.Select(k => k.Clone()).ToList();
        copy.Profiles = (Profiles ?? []).Select(p => p.Clone()).ToList();
        copy.ToggleShortcuts = ToggleShortcuts?.Select(b => b is null ? null! : b with { }).ToList();
        return copy;
    }

    /// <summary>設定値を安全な範囲に収める。編集中の設定収集では reportShortcutExclusions を false にし、キー除外のログ・通知を抑制する。</summary>
    public Settings Normalize(bool reportShortcutExclusions = true)
    {
        MigrateLegacyToggleShortcuts(reportShortcutExclusions);
        if (ToggleShortcuts is { } shortcuts)
        {
            var valid = new List<ToggleShortcut>();
            foreach (var shortcut in shortcuts)
            {
                var error = shortcut is null ? ShortcutValidationError.Invalid : ToggleShortcutValidator.Validate(shortcut, this, valid);
                if (shortcut is not null && error == ShortcutValidationError.None)
                    valid.Add(shortcut with { Key = ToggleShortcut.NormalizeHankakuKey(shortcut.Key) });
                else if (reportShortcutExclusions)
                    Diagnostics.ShortcutExclusions.Report(shortcut, error, ActiveProfile);
            }
            ToggleShortcuts = valid;
        }
        JapaneseThreshold = Math.Clamp(JapaneseThreshold, 2, 20);
        MaxPendingKeys = Math.Clamp(MaxPendingKeys, 2, 12);
        ImeTimeoutMs = Math.Clamp(ImeTimeoutMs, 50, 2000);
        // 数値で書いた config.json の、今は無い番号 (消した IME 自動切替・両方を判定) は初期値にする
        if (!Enum.IsDefined(Mode)) Mode = InputMode.Tsf;
        if (!Enum.IsDefined(InputStyle)) InputStyle = InputStyle.Romaji;
        AppRules ??= [];
        AppKinds ??= [];
        AppRules.RemoveAll(r => r is null || string.IsNullOrWhiteSpace(r.Process));
        // プロファイル: 名前の無いもの・同じ名前のものは除き、使っているプロファイルは必ず一覧にある
        Profiles ??= [];
        Profiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name));
        Profiles = Profiles.GroupBy(p => p.Name.Trim()).Select(g => { var p = g.First(); p.Name = g.Key; return p; }).ToList();
        if (string.IsNullOrWhiteSpace(ActiveProfile)) ActiveProfile = Profiles.FirstOrDefault()?.Name ?? DefaultProfileName;
        ActiveProfile = ActiveProfile.Trim();
        if (Profiles.All(p => p.Name != ActiveProfile)) Profiles.Insert(0, new SettingsProfile { Name = ActiveProfile });
        foreach (var rule in AppRules) rule.Process = rule.Process.Trim();
        // 同じアプリの設定が 2 つ以上あれば、後ろ (あとから足した方) を使う。既定の一覧にある Code.exe (コード) の後ろに
        // Enabled: false の Code.exe を足しても、前の方が使われて OFF にならなかった (issue #233)
        AppRules = AppRules.Where((rule, i) => !AppRules.Skip(i + 1).Any(later => string.Equals(later.Process, rule.Process, StringComparison.OrdinalIgnoreCase))).ToList();
        return this;
    }

    /// <summary>古い既定値のままの項目だけを新しい既定値に更新する (ユーザーが変えた値は触らない)。</summary>
    internal bool Migrate()
    {
        if (SettingsVersion >= CurrentVersion) return false;
        // v2: IME 自動切替の保留の時間を延ばした (IME 自動切替は消した)。
        // v3: Meltype キーボード (変換ボックス) を追加。v2 以前の config.json には Mode が無いので、
        // 読み込み時に初期値になる。
        if (SettingsVersion < 4)
        {
            // v4: アプリの種類 (コード) を追加。コードエディター・ターミナルを「コード」にする (ユーザーが OFF にしたものはそのまま)。
            foreach (var process in CodeApps)
            {
                var rule = FindRule(process);
                if (rule is null) AppRules.Add(new AppRule { Process = process, Enabled = true, Profile = AppProfile.Code });
                else rule.Profile = AppProfile.Code;
            }
        }
        if (SettingsVersion < 5)
        {
            // v5: 既定の「コード」のアプリに Maya (Script Editor) を追加。ユーザーが自分で入れていれば、そのまま。
            if (FindRule("maya.exe") is null) AppRules.Add(new AppRule { Process = "maya.exe", Enabled = true, Profile = AppProfile.Code });
        }
        if (SettingsVersion < 6)
        {
            // v6: 旧 OFF は空の一覧に移し、保存済みの一覧はそのまま維持する。
            ToggleShortcuts ??= LegacyHankaku == false ? [] : ToggleShortcut.Defaults();
            LegacyHankaku = null;
            foreach (var profile in Profiles ?? [])
                if (profile is not null) InitializeToggleShortcuts(profile.Values ??= new JsonObject(), profile.Name);
        }
        // v7: 追加キーの文字列を共通の一覧へ移し、旧項目を保存しない。
        MigrateLegacyToggleShortcuts(report: true);
        SettingsVersion = CurrentVersion;
        return true;
    }

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize(json, JsonType) ?? new Settings();
            if (!json.Contains(nameof(SettingsVersion))) settings.SettingsVersion = 1;
            // 動作モードが IME 自動切替 (消した) だった: Meltype IME として読んでいる (InputModeJsonConverter)。
            // 初めて起動したときと同じく、Meltype IME を使えなければ Meltype キーボードに戻し、使えれば入力方式も Meltype IME にする
            var autoSwitch = TopLevelStringIs(json, nameof(Mode), "AutoSwitch");
            if (autoSwitch) settings.TsfIntroduced = false;
            var migrated = settings.Migrate();
            // 移行したキーの競合も保存前に除外し、次回起動時の再通知を防ぐ。
            settings.Normalize();
            if (migrated || autoSwitch)
            {
                // 保存できなくても、読み込んだ設定は使う (既定値に戻すと、次の保存でユーザーの設定が消える)。
                try { settings.Save(path); }
                catch (Exception ex) { Diagnostics.Log.Warn($"移行した config.json を保存できませんでした: {ex.Message}"); }
            }
            return settings;
        }
        catch (Exception ex)
        {
            // 壊れた設定で起動不能にしない。元ファイルは退避して既定値で動く。
            try { File.Copy(path, path + ".broken", overwrite: true); }
            catch (Exception copyEx) { Diagnostics.Log.Warn($"{Path.GetFileName(path)} を .broken に残せませんでした: {copyEx.Message}"); }
            Diagnostics.Log.Warn($"config.json を読み込めなかったため既定値を使います: {ex.Message}");
            return new Settings();
        }
    }

    /// <summary>config.json の一番上の項目 property が、文字列の value か (プロファイルの中の値は見ない)。</summary>
    private static bool TopLevelStringIs(string json, string property, string value)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1 && reader.ValueTextEquals(property))
                return reader.Read() && reader.TokenType == JsonTokenType.String && string.Equals(reader.GetString(), value, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>config.json と同じ形式の文字列 (変更があったかを比べるのに使う)。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonType);

    public void Save(string path)
    {
        DataFile.Write(path, temp => File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonType)));
    }
}
