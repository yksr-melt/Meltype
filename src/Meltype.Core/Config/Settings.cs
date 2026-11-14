// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meltype.Input;

namespace Meltype.Config;

/// <summary>Meltype の OS。<see cref="PlatformsAttribute"/> で組にする。</summary>
[Flags]
public enum Platform
{
    Windows = 1,
    Mac = 2,
    Linux = 4,
}

/// <summary>
/// 一部の OS でしか使わない設定の項目と、使う OS の組。付けていない項目はすべての OS で使う。
/// config.schema.json の説明の先頭に「Windows・Mac のみ。」のように出し、x-platforms に OS の名前を出す。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PlatformsAttribute(Platform platforms) : Attribute
{
    public Platform Platforms { get; } = platforms;
}

public enum InputStyle
{
    /// <summary>ローマ字入力。</summary>
    [Description("ローマ字入力")] Romaji,
    /// <summary>JIS かな入力。</summary>
    [Description("かな入力 (JIS、α版)")] Kana,
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

/// <summary>かなのすぐ後ろで打った / に出す文字。</summary>
public enum SlashStyle
{
    [Description("/ のまま")] Slash,
    [Description("中黒「・」")] MiddleDot,
    [Description("全角のスラッシュ「／」")] FullWidth,
}

/// <summary>日本語入力のときの Space (何も打っていないとき) と Shift + Space で入れるスペース。</summary>
public enum SpaceWidth
{
    /// <summary>Microsoft IME と同じ。</summary>
    [Description("Space は半角、Shift + Space は全角")] Standard,
    [Description("Space も Shift + Space も半角")] HalfWidth,
    [Description("Space は全角、Shift + Space は半角")] FullWidth,
}

/// <summary>入力中の Control + 英字の組み込みの割り当て。</summary>
public enum ControlKeyLayout
{
    /// <summary>Ctrl + U/I/O/P で F6/F7/F10/F9、Ctrl + H で BackSpace。</summary>
    [Description("標準 (Ctrl + U/I/O/P で F6/F7/F10/F9)")] Standard,
    /// <summary>標準に加えて、変換中の Ctrl + K / L で文節を縮める / 伸ばす。</summary>
    [Description("ATOK 風 (標準 + Ctrl + K / L で文節の長さ)")] Atok,
    /// <summary>macOS 標準の日本語入力と同じ。</summary>
    [Description("macOS 風")] Mac,
}

/// <summary>Meltype キーボードで、入力モード (あ / A) をカーソルの近くに出すとき。</summary>
public enum ModeIndicatorTiming
{
    /// <summary>OS と入力方式ごとの既定 (<see cref="Settings.EffectiveModeIndicator"/>)。</summary>
    [Description("OS と入力方式の既定")] Default,
    [Description("表示しない")] Never,
    [Description("切り替えたとき")] OnSwitch,
    [Description("切り替えたときと入力欄に入ったとき")] OnSwitchAndFocus,
}

/// <summary>確定した文字をアプリに入れる方法。</summary>
public enum CommitMethod
{
    /// <summary>Qt アプリは貼り付け、ほかは 1 文字ずつ送る。</summary>
    [Description("自動")] Auto,
    /// <summary>クリップボードを使って貼り付ける (元のクリップボードの中身は戻す)。</summary>
    [Description("貼り付け")] Paste,
    [Description("1 文字ずつ送る")] Keys,
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

    [DisplayName("確定した文字の入れ方"), Description("確定した文字を 1 文字ずつ送ると取り違えるアプリ (DaVinci Resolve で「あいうえお」→「あああああ」) は「貼り付け」にします。Qt アプリ (LINE・OBS など) は「自動」でも貼り付けになるので、Ctrl+V が貼り付けではないアプリは「1 文字ずつ送る」にします。")]
    public CommitMethod Commit { get; set; } = CommitMethod.Auto;

    [DisplayName("入力欄とみなす"), Description("画面を自分で描くため、文字を打つ所なのに入力欄と判定されず変換ボックスが出ないアプリ (Premiere Pro など) で ON にします。1 文字のショートカット (V・C など) も変換ボックスに入るようになるので、ショートカットを使うときは Ctrl + 半角/全角 で一時停止してください。")]
    public bool TextInput { get; set; }

    public AppRule Clone() => (AppRule)MemberwiseClone();

    public override string ToString() => $"{Process}: {(Enabled ? "ON" : "OFF")}, {Kind ?? Profile.ToString()}, {Commit}{(TextInput ? ", 入力欄" : "")}";
}

/// <summary>
/// ユーザーが作るアプリの種類 (例: 「チャット」「ゲーム」)。一般 / コード を元に、自動判定・ライブ変換を変えられる。
/// null の項目は全体の設定のまま。
/// </summary>
public sealed class AppKind
{
    public string Name { get; set; } = "";
    public AppProfile Base { get; set; } = AppProfile.General;
    public bool? AutoDetection { get; set; }
    public bool? LiveConversion { get; set; }

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
    /// <summary>ゲーム。Meltype キーボードは何もしない (キーを横取りせず、Windows の IME の ON/OFF にも触らない。ゲームのチャットは Windows の IME で打つ)。Meltype IME は英数で始め、英数ではキーを横取りしない (ImeSession.IsGame)。</summary>
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

    private const string SchemaKey = "$schema";

    /// <summary>config.json の JSON Schema (<see cref="SettingsSchema"/>) の場所。エディターが読む。Meltype は使わないが、保存で消さないように持っておく。</summary>
    [Browsable(false), JsonPropertyName(SchemaKey)]
    public string? Schema { get; set; } = "./" + SettingsSchema.FileName;

    [Category("1. 全般"), DisplayName("Meltype を有効にする")]
    public bool Enabled { get; set; } = true;

    [Category("1. 全般"), DisplayName("日本語キーボードのときだけ動作"), Platforms(Platform.Windows),
     Description("Windows の入力言語が日本語のときだけ動作します。韓国語・英語などでは入力処理と IME の自動制御を停止し、日本語に戻すと再開します。物理キーボードの JIS / US 配列や IME の「あ」「A」の状態は問いません。全プロファイル共通です。")]
    public bool JapaneseKeyboardOnly { get; set; }

    [Category("1. 全般"), DisplayName("遠隔操作などの入力も処理する"), Platforms(Platform.Windows),
     Description("Windows のタッチキーボード、AnyDesk・VNC などの遠隔操作ソフト、ULE4JIS などキー配列を変えるソフト (JIS キーボードを US 配列で使う) から届いたキーも、手で打ったキーと同じように処理します (OFF だと、ほかのソフトが送ったキーはそのままアプリに渡し、変換ボックスを開きません。キー配列を変えるソフトは Meltype より後に起動してください)。遠隔操作ソフトとキーボードのマクロ・自動入力のソフトは見分けられないので、ON にするとどちらも処理の対象になります。Meltype 自身が送ったキーは、ON でも処理しません。Chrome リモート デスクトップ・Parsec で操作されている間は、OFF でも処理します (Parsec は、接続していなくても Parsec が動いている間はずっと処理するので、AutoHotkey などが送ったキーも変換の対象になります)。全プロファイル共通です。")]
    public bool AllowInjectedInput { get; set; }

    [Category("1. 全般"), DisplayName("動作モード"), Platforms(Platform.Windows),
     Description("Tsf = Windows の IME として入力欄に直接入力 (Win + Space で Meltype を選ぶ) / Keyboard = Meltype の変換ボックスで入力 (英単語は自動で英字、Space で変換、Enter で確定)")]
    [JsonConverter(typeof(InputModeJsonConverter))]
    public InputMode Mode { get; set; } = InputMode.Tsf;

    [Category("2. 入力モード"), DisplayName("入力モード切り替えキー"),
     Description("日本語／英数の入力モードを切り替えるキーを複数登録できます。Windows や他のアプリで使うキー操作を登録すると、元の操作が使えなくなったり、入力モードの切り替えと同時に動いたりする場合があります。")]
    public List<ToggleShortcut>? ToggleShortcuts { get; set; }

    // v5 以前の設定を読み込むためだけに残す。移行後は保存しない。
    [Browsable(false), JsonInclude, JsonPropertyName("HankakuTogglesKeyboard")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal bool? LegacyHankaku { get; set; }

    [JsonIgnore, Browsable(false)]
    public IReadOnlyList<ToggleShortcut> EffectiveToggleShortcuts => ToggleShortcuts ?? ToggleShortcut.Defaults();

    [Category("2. 入力モード"), DisplayName("左 Alt で英数 / 右 Alt で日本語"), Platforms(Platform.Windows),
     Description("左 Alt の単独押しで英数 (直接入力) に、右 Alt の単独押しで日本語入力にします (半角/全角 キーの無い US 配列向け)。Alt + Tab などの組み合わせや Alt + クリックでは切り替えません。単独押しでアプリのメニューバーに移らなくなります。変換中なら、変換中の文字を確定して切り替えます。コードの行では、右 Alt でその行を日本語にします。")]
    public bool AltKeysSwitchKeyboard { get; set; }

    [Category("3. 判定"), DisplayName("確定後も文脈に合わせて直す"),
     Description("英語とも日本語とも読める語 (i, sushi など) を確定した後、次の語で英語か日本語かがはっきりしたら自動で確定し直します (i → 胃 と確定した後に want と打つと I want)。")]
    public bool AutoCorrectAfterCommit { get; set; } = true;

    [Category("3. 判定"), DisplayName("英数状態でもローマ字を検知"),
     Description("英数状態でも単語の打ち始めを判定し、ローマ字 (日本語) なら自動で日本語入力に戻します。Mac 版では使えません。")]
    public bool DirectModeAutoDetect { get; set; } = true;

    [Category("3. 判定"), DisplayName("/ $ @ で始まる語はそのまま入力"),
     Description("Keyboard モードで、入力欄・行の先頭か空白の直後に打った / $ @ と、続く名前 (空白まで) は変換せず、打つたびにそのままアプリに渡します (AI エージェントの /command・$skill・@ファイル名 の補完を選びやすく)。名前の後に空白を打つと、普通の自動判定に戻ります。文章の入力欄 (コードエディターのコード以外) の / は「・」として入り、Space で / にしてから名前を打つと、名前はそのまま渡します。")]
    public bool SigilWordsDirect { get; set; } = true;

    [Category("5. 候補"), DisplayName("定型文を出す記号"),
     Description("Keyboard モードで、入力欄・行の先頭か空白の直後にこの記号と名前を打って Space / Tab を押すと、トレイの「定型文...」で登録した文に置き換えます (;sig + Space → 署名)。名前が登録したものでなければ何もしません。置き換えた直後に Esc を押すと、打った ;名前 に戻します。空にすると使いません。定型文を 1 つも登録していなければ、記号は今までどおり入力します。")]
    public string SnippetMark { get; set; } = ";";

    [Category("6. 記号・スペース"), DisplayName("英単語の前後に半角スペース"),
     Description("確定するときに、日本語と英単語の間に半角スペースを入れます (今日はGitHubにpushした → 今日は GitHub に push した)。数字だけの語 (3時) には入れません。")]
    public bool SpaceAroundEnglish { get; set; }

    [Category("6. 記号・スペース"), DisplayName("半角数字の前後に半角スペース"),
     Description("確定するときに、日本語と半角数字の間に半角スペースを入れます (この処理を3回実行します → この処理を 3 回実行します)。技術文書などの表記に合わせるとき用です。全角数字 (３回) には入れません。")]
    public bool SpaceAroundNumbers { get; set; }

    [Category("4. 変換"), DisplayName("変換中の数字キーで候補を選ぶ"),
     Description("Space・Tab で変換している間に、数字キー (上の段・テンキー) の 1〜9 を押すと、候補の一覧の番号で選びます。かな入力では上の段はかなのキーなので、テンキーで選びます。OFF にすると、今の候補で確定してから数字を入力します (zu Space 1 → 図1、hyou Space 10 → 表10)。")]
    public bool NumberKeysSelectCandidates { get; set; } = true;

    [Category("6. 記号・スペース"), DisplayName("スペースの幅"),
     Description("日本語入力のときの Space (何も打っていないとき) と Shift + Space で入れるスペースです。標準は Microsoft IME と同じです。英数状態では、どちらも半角スペースです。")]
    public SpaceWidth SpaceWidth { get; set; } = SpaceWidth.Standard;

    [Category("4. 変換"), DisplayName("Enter は見えているとおりに確定"),
     Description("Enter で確定するとき、打ち間違いの自動修正をせず、変換ボックスに見えているとおりに確定します (Space で変換するときの修正は今までどおりです)。")]
    public bool EnterCommitsAsShown { get; set; }

    [Category("7. キー操作"), DisplayName("F9 / F10 の 1 回目は打ったまま"),
     Description("入力中の表示が英字でも、F10 (Ctrl+O) の 1 回目は常に打ったまま (小文字) の英字にして、2 回目以降で すべて大文字 → 先頭だけ大文字 と切り替えます (Microsoft IME・Google 日本語入力と同じ)。OFF のときは、英字で見えている語の 1 回目の F10 で すべて大文字になります。")]
    public bool AlphanumericKeyStartsAsTyped { get; set; }

    [Category("6. 記号・スペース"), DisplayName("カッコを全角で入力"),
     Description("日本語入力中の ( ) を全角の（ ）にします。英字の直後でも全角のままです (OFF のときは、打った幅の半角のままで、英語の中では半角です)。")]
    public bool FullWidthParentheses { get; set; }

    [Category("6. 記号・スペース"), DisplayName("[ ] を角括弧［ ］にする"),
     Description("日本語入力中の [ ] を「」ではなく全角の角括弧［ ］にします (Google 日本語入力の「記号」の設定と同じ)。")]
    public bool SquareBrackets { get; set; }

    [Category("7. キー操作"), DisplayName("Esc で一段ずつ戻す"),
     Description("入力中の Esc を、打った内容を全部消すのではなく一段ずつ戻します (漢字 → ひらがな → 打ったローマ字 → 消す。見た目が変わらない段は飛ばします)。戻した語は、日本語 / 英語の判定として覚えません。")]
    public bool EscStepsBack { get; set; }

    [Category("7. キー操作"), DisplayName("変換中の Esc は選んだ文節だけをかなに戻す"),
     Description("文節に分けて変換しているとき、Esc で変換全体をやめるのではなく、選んでいる文節だけをひらがなに戻します (ほかの文節はそのまま)。もうひらがなの文節か、文節が 1 つだけのときの Esc は、今までどおり変換をやめます。")]
    public bool EscRevertsSelectedClause { get; set; }

    [Category("7. キー操作"), DisplayName("変換中の F6〜F10 は選んだ文節だけに効かせる"),
     Description("文節に分けて変換しているとき、F6〜F10 (ひらがな・カタカナ・半角カタカナ・全角英数・半角英数) を全体ではなく、選んでいる文節だけに効かせます (今日は|Google の「今日は」だけをカタカナにするなど)。打った英字が分からない文節 (かな入力など) では何もしません。")]
    public bool FunctionKeysAffectSelectedClause { get; set; }

    [Category("7. キー操作"), DisplayName("Shift + Enter で選んだ文節までを確定"),
     Description("文節に分けて変換しているとき、Shift + Enter で、選んでいる文節までを確定し、残りの文節は未確定のまま (ひらがなに戻して) 続けて編集できるようにします。")]
    public bool PartialCommit { get; set; }

    [Category("7. キー操作"), DisplayName("Control キーの割り当て"),
     Description("入力中の Control + 英字の割り当てです。標準: Ctrl + U / I / O / P で F6 / F7 / F10 / F9、Ctrl + H で BackSpace。ATOK 風: 標準に加えて、変換中の Ctrl + K で選んでいる文節を縮め、Ctrl + L で伸ばします (Shift + ← → と同じ)。macOS 風: Control + J / K / L をひらがな / カタカナ / 全角英字 (F6 / F7 / F9) に、Control + H / D を前 / 後ろの 1 文字を消す、N / P を次 / 前の候補、B / F / A / E をカーソル移動、O / I を変換中の文節を伸ばす / 縮める、にします (macOS 標準の日本語入力と同じ)。")]
    public ControlKeyLayout ControlKeys { get; set; } = ControlKeyLayout.Standard;

    [Category("7. キー操作"), DisplayName("Control キーの個別の割り当て"),
     Description("入力中の Control + 英字に、別のキーの働きを割り当てます。「T=F6;Y=F7;U=F8;I=F9;O=F10」のように、英字=キー名を ; で区切って書きます (F キーが無い HHKB やノートパソコン向け)。キー名: F6〜F10・Left・Right・Up・Down・Home・End・Delete・Back・Enter・Esc・Tab。ここに書いた英字は、「Control キーの割り当て」より優先します。")]
    public string ControlKeyBindings { get; set; } = "";

    [Category("6. 記号・スペース"), DisplayName("カッコを打ったら中にカーソルを移動"),
     Description("カッコだけ（「」・（）・() など）を確定したときに、カーソルをカッコの中へ移動します。Windows の Meltype IME・Meltype キーボードで対応しています。macOS・Linux は未対応です。")]
    public bool CursorIntoBrackets { get; set; }

    [Category("5. 候補"), DisplayName("計算式を変換すると結果を候補に出す"),
     Description("数字と + - * / ( ) だけの式を打って Space で変換すると、計算した結果 (1+2*3 → 7) と「式=結果」を候補に出します。")]
    public bool CalculatorCandidates { get; set; }

    [Category("3. 判定"), DisplayName("かな + w を笑い (ねw・さw) にする"),
     Description("ね・さ などの 1 文字のかなの後ろの w を、英単語 (new・saw) にせず笑いの w にします (なるほどnew → なるほどねw)。英単語の new や saw を打つ人は OFF のままにしてください。")]
    public bool LaughterAfterKana { get; set; }

    [Category("9. 学習"), DisplayName("英語 / 日本語の学習を前の文脈ごとに分ける"),
     Description("英字 / かなに直した語 (new → ねw など) の学習を、直前が英語だったか日本語だったかごとに別々に覚えます。同じローマ字でも、英語の文の中と日本語の文の中で使い分けられます。ON にする前に覚えた学習は、どちらの文脈でも使います。")]
    public bool ContextualLearning { get; set; }

    [Category("4. 変換"), DisplayName("入力中も変換候補をいくつか表示"),
     Description("ひらがなを打っている間も、Space を押す前から変換候補を (5 件まで) 変換ボックスに出します。候補をクリックすると、その候補で変換を始めます。")]
    public bool ShowCandidatesWhileTyping { get; set; }

    /// <summary>旧形式の追加キーを読み込むためだけの項目。正規化時に一覧へ移し、保存時には出力しない。</summary>
    [Browsable(false), JsonInclude, JsonPropertyName("ToggleKeyboardShortcuts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? LegacyToggleKeyboardShortcuts { get; set; }

    [Category("5. 候補"), DisplayName("#番号で GitHub の Issue・PR のタイトルを候補に出す"),
     Description("#123 のように打って Space で変換すると、下の「GitHub のリポジトリ」の Issue・Pull Request のタイトルを候補に出します (開発者向け)。打った番号を GitHub (api.github.com) に問い合わせるので、既定は OFF です。非公開のリポジトリは、環境変数 MELTYPE_GITHUB_TOKEN にトークンを入れると読めます。調べた結果はこのセッションの間だけ覚えます。")]
    public bool GitHubIssueCandidates { get; set; }

    [Category("5. 候補"), DisplayName("GitHub のリポジトリ"),
     Description("「#番号で GitHub の Issue・PR のタイトルを候補に出す」で問い合わせるリポジトリ。「所有者/名前」の形で書きます (例: yksr-melt/Meltype)。")]
    public string GitHubRepository { get; set; } = "";

    [Category("5. 候補"), DisplayName("クリップボードの文字を変換候補に出す"), Platforms(Platform.Windows),
     Description("「くりっぷぼーど」「ぺーすと」を変換したとき、クリップボードにある 1 行の文字 (200 文字まで) を候補に出します。変換した瞬間に 1 回だけ読み、監視・保存・送信はしません。パスワード管理ソフトなど、履歴に残さない印のついた内容は読みません。既定は OFF です。")]
    public bool ClipboardCandidates { get; set; }

    [Category("5. 候補"), DisplayName("「なう」で再生中の曲を候補に出す"), Platforms(Platform.Windows),
     Description("「なう」を変換したとき、今再生している曲 (曲名 / アーティスト) を候補に出します。Windows のメディアの操作 (音量のボタンを押したときに出る再生中の表示) に曲を知らせるアプリ (Spotify・ブラウザーなど) が対象です。ON の間は、曲・再生状態が変わったときに Windows から知らせを受けて今の曲を読みます (変換を待たせないため。変わらない間は読みません)。メモリに今の 1 曲を持つだけで、保存・送信はしません。選んでも学習しません。再生中の曲が無いときは、今までどおり時刻を出します。")]
    public bool NowPlayingCandidates { get; set; } = true;

    [Category("5. 候補"), DisplayName("時計の候補のタイムゾーン"),
     Description("「とけい」「じこく」「いま」「なう」を変換したときに候補に出す、今の時刻 (19:21 / 19時21分 / 午後7時21分) のタイムゾーン。IANA の ID (例: America/New_York・Europe/London) か Windows の ID (例: Eastern Standard Time) を書きます。空ならこの PC のタイムゾーン (既定)。夏時間も反映します。この PC と違うタイムゾーンなら、「とけい」「じこく」では日付とタイムゾーンの名前を付けた候補も出します。")]
    public string ClockTimeZone { get; set; } = "";

    [Category("1. 全般"), DisplayName("Meltype の一時停止 / 再開のホットキー"), Platforms(Platform.Windows),
     Description("Meltype 全体の一時停止 / 再開に使うキーの組み合わせ (例: Ctrl+Alt+F10)。空のときは Ctrl+Alt+F12 (使えなければ Ctrl+Alt+F11 など)。変更は Meltype を起動し直すと効きます。変換・無変換キーが無い US 配列のキーボード向けです。")]
    public string PauseHotkey { get; set; } = "";

    [Category("6. 記号・スペース"), DisplayName("句読点"),
     Description("日本語の中で , と . を打ったときに出す句読点です (Microsoft IME と同じ 4 通り)。論文などで「，．」「，。」「、．」を使うときに変えます。かな入力の 、 。 のキーにも効きます。数字の間の . , (1.5、1,000) や英単語の中の . (tetr.io) は半角のままです。")]
    public PunctuationStyle Punctuation { get; set; } = PunctuationStyle.Japanese;

    [Category("6. 記号・スペース"), DisplayName("英字の後ろの , . も句読点にする"),
     Description("英字のすぐ後ろで打った , . も、日本語の句読点 (設定「句読点」の形) にします (IME, → IME、)。OFF だと、変換ボックスの中が英字だけなら半角の , . のままです (英文を打つとき)。ON でも、キャレットの前が英語 (Hello + world.) のときと、英単語の中の . (tetr.io) は半角のままです。")]
    public bool JapanesePunctuationAfterEnglish { get; set; }

    [Category("4. 変換"), DisplayName("ライブ変換"),
     Description("Keyboard モードで、Space を押さなくても打ったそばから漢字に変換して表示します。")]
    public bool LiveConversion { get; set; } = true;

    // Linux 版は llama-server をまだ同梱していない (#1571)。同梱したら Platform.Linux を足す
    [Category("4. 変換"), DisplayName("Zenzai を使う"), Platforms(Platform.Windows | Platform.Mac),
     Description("ニューラルかな漢字変換モデル Zenzai (azooKey の zenz) で、前の文脈を見て変換します。同梱の llama.cpp の llama-server と zenz-v3.2-small を使います。使えないとき・応答が遅いときは、Mozc で変換します。変換結果は、Mozc の文節ごとの候補で作れるときは Mozc の文節に分け、作れないときは読み全体で 1 つの文節になります。既定は ON です。")]
    public bool UseZenzai { get; set; } = true;

    [Category("4. 変換"), DisplayName("Zenzai: llama-server の場所"), Platforms(Platform.Windows | Platform.Mac),
     Description("llama.cpp の llama-server (Windows では llama-server.exe) の場所。空なら同梱のものを使います (既定)。「Zenzai を使う」が ON のときに使います。")]
    public string ZenzaiServerPath { get; set; } = "";

    [Category("4. 変換"), DisplayName("Zenzai: モデル (GGUF) の場所"), Platforms(Platform.Windows | Platform.Mac),
     Description("zenz のモデルファイル (例: zenz-v3.2-small の ggml-model-Q5_K_M.gguf) の場所。空なら同梱の zenz-v3.2-small を使います (既定)。「Zenzai を使う」が ON のときに使います。")]
    public string ZenzaiModelPath { get; set; } = "";

    [Category("4. 変換"), DisplayName("Zenzai: 使う GPU"), Platforms(Platform.Windows),
     Description("GPU 版 (Vulkan・CUDA) の llama-server で、計算に使う GPU の名前 (例: Vulkan0)。llama-server の --device に渡し、GPU に載せる量は llama-server が空いているメモリに合わせて決めます。空なら CPU だけで計算します (既定)。名前は llama-server --list-devices で確かめられます。変えると llama-server を起動し直します。")]
    public string ZenzaiDevice { get; set; } = "";

    [Category("5. 候補"), DisplayName("英訳の候補"),
     Description("変換の候補の後ろに英訳も出します (複雑な → complex, complicated)。JMdict のよく使う語から。選んだ英訳は少しずつ前に出ます。")]
    public bool TranslationCandidates { get; set; } = true;

    [Category("5. 候補"), DisplayName("予測変換の候補"),
     Description("打っている途中に、続きの候補を変換ボックスの下に出します (前に確定した語句・ユーザー辞書・選び直した変換の学習・よく使うあいさつ・英単語の続き)。Tab / Shift+Tab で選んで Enter で確定します。確定した語句は %LOCALAPPDATA%\\Meltype\\phrases.txt に暗号化せずに覚え (この PC の外には送りません)、「学習データをリセット」で消えます。")]
    public bool PredictiveCandidates { get; set; } = true;

    [Category("5. 候補"), DisplayName("予測変換に英字の社名・サービス名"),
     Description("「予測変換の候補」が ON のとき、読みを打ちかけたところで、英字で書く社名・サービス名・語の候補も出します (しゃお → Xiaomi、ぎっとは → GitHub)。読みを 3 文字以上打ったときだけ出します。英単語の続き (decis → decision) は「予測変換の候補」だけで出ます。")]
    public bool EnglishNamePredictions { get; set; }

    [Category("4. 変換"), DisplayName("Tab で変換"),
     Description("打っている途中に Tab を押すと、Space と同じく変換を始めます (Microsoft IME と同じ)。Tab で始めた変換の中では Tab で次の候補、Shift+Tab で前の候補、Enter で確定します。もしかして・手動の提案・予測変換の候補が出ているときは、今までどおりそちらに使います。英語で終わっているとき・Space で始めた変換の中では、今までどおり確定してから Tab をアプリに渡します。")]
    public bool TabConversion { get; set; } = true;

    [Category("4. 変換"), DisplayName("変換したあとに続けて打っても確定しない"),
     Description("Space で変換したあとに続けて文字を打っても、変換した部分を確定せず、続けて打った文字と一緒に変換ボックスに残します (選んだ候補は、もう一度変換したときにも使います)。OFF なら Microsoft IME と同じく、変換した部分を確定してから続きを打ちます。")]
    public bool ContinueAfterConversion { get; set; }

    [Category("7. キー操作"), DisplayName("入力中のキーの役割を分ける"),
     Description("入力中 (変換ボックスに文字があるとき) の Space・変換・無変換・ひらがな/カタカナ キーの役割を分けます。Space: 英語・日本語の判定にかかわらず候補を開く (英字で見えている語も、確定して空白を入れずに候補を出す)。変換: 日本語を優先して変換。無変換: 英字にする。ひらがな/カタカナ: ひらがな ⇔ カタカナ。確定は Enter です。何も打っていないときの各キーは今までどおりです。")]
    public bool SeparateKeyRoles { get; set; }

    [Category("3. 判定"), DisplayName("英字を主に入力 (変換キー / ↓ で日本語に)"),
     Description("自動判定をせず、打った英字をそのまま入力中にします (Space は空白、Enter は英字のまま確定)。日本語にしたい語は、変換キーか ↓ を押すと、ローマ字として読んで変換します。コマンドを多く打つ使い方向けで、Microsoft IME の「主に英語を入力する」と同じ動きです。")]
    public bool EnglishFirstInput { get; set; }

    [Category("5. 候補"), DisplayName("子音の略入力を候補に出す"),
     Description("子音だけの略入力 (ast → 明日、ngt → ノート など) を、変換の候補に出します。候補は同梱の candidates.txt の英字の読みから出します。既定は OFF です。")]
    public bool ConsonantAbbreviations { get; set; }

    [Category("5. 候補"), DisplayName("候補の意味を表示"),
     Description("変換中に同じ候補で少し (約 1.5 秒) 止まると、その候補の意味をウィクショナリー日本語版から候補の一覧の横に出します (日本語の意味が無い語は JMdict の英訳: 橋 → bridge)。同音異義語を選ぶときの手がかりに。")]
    public bool ShowCandidateMeanings { get; set; } = true;

    [Category("8. 表示"), DisplayName("打ったキーを表示"), Platforms(Platform.Windows),
     Description("打ったキー (ローマ字: kyouhagoogle) を小さく出します。Meltype キーボードでは変換ボックスの文字の下に、Meltype IME では候補の一覧の上に出します。打ち間違いに気づきやすくなります。")]
    public bool ShowTypedKeys { get; set; } = true;

    [Browsable(false), DisplayName("固定の操作ガイドを表示"),
     Description("変換中の表示に Space 変換・F7 カタカナ などの固定ガイドを表示します。OFF でも英字・予測・訂正の提案は表示します。")]
    public bool ShowGuide { get; set; } = true;

    /// <summary>
    /// 固定の操作ガイドを出すか。Mac と Meltype IME は、表示を揃える前 (1.5.0) と同じく出さない
    /// (Mac は候補ウィンドウに重なる)。フロントごとの表示を揃えるときは、ここと <see cref="EffectiveModeIndicator"/> を直す。
    /// </summary>
    public bool ShowsGuide => ShowGuide && !OperatingSystem.IsMacOS() && !(OperatingSystem.IsWindows() && Mode == InputMode.Tsf);

    [Category("4. 変換"), DisplayName("打ち間違いを直す"),
     Description("Space・Enter で変換・確定するときに打ち間違いを直します。ローマ字: 読めない子音が残ったとき、隣のキーの押し間違い・入れ替わり・抜けを 1 文字だけ直します (onegaishimsu → お願いします、sumimasne → すみません。よく使う語の読みになるときだけ)。英語: Windows の自動修正の一覧にある打ち間違いを直します (teh → the、recieve → receive)。")]
    public bool CorrectTypos { get; set; } = true;

    [Category("6. 記号・スペース"), DisplayName("かなの後ろの /"),
     Description("かなのすぐ後ろで打った / を、中黒「・」(いーろん/ますく → イーロン・マスク) か全角のスラッシュ「／」にします。英字・数字の後ろ (and/or、3/4、URL) は / のままです。文章の入力欄の打ち始めの / は、この設定に関係なく「・」になり、Space で / にできます。「/ のまま」のときも、z/ (設定「z + 記号・hjkl で記号・矢印を入れる」が ON のとき) または / を打って Space で「・」にできます。")]
    public SlashStyle Slash { get; set; } = SlashStyle.Slash;

    [Category("6. 記号・スペース"), DisplayName("z + 記号・hjkl で記号・矢印を入れる"),
     Description("日本語入力のとき、z の後ろに記号や h j k l を打つと記号・矢印にします (Microsoft IME・Google 日本語入力と同じ。z/ → ・、z. → …、z, → ‥、z- → ～、z[ → 『、z] → 』、zh zj zk zl → ← ↓ ↑ →)。OFF にすると、z はほかの英字と同じに扱います (1000Hz. や quiz. の z. を … にしません)。")]
    public bool ZSymbols { get; set; } = true;

    [Category("2. 入力モード"), DisplayName("入力モードをカーソルの近くに表示"),
     Description("入力モードを切り替えたとき (と入力欄に入ったとき) に、カーソルの近くに「あ」(日本語) か「A」(英数) を一瞬表示します。Meltype キーボードの使用中は Windows の IME を OFF にしているので、タスクバーの IME の表示は常に「A」になります。今のモードはこの表示かトレイの Meltype のアイコンで確認してください。")]
    public ModeIndicatorTiming ModeIndicator { get; set; } = ModeIndicatorTiming.Default;

    /// <summary>
    /// 入力モードを表示するとき。既定は、表示を揃える前 (1.5.0) のフロントごとの既定と同じ:
    /// Meltype キーボードは入力欄に入ったときも、Meltype IME は出さない、Mac・Linux は切り替えたときだけ。
    /// </summary>
    public ModeIndicatorTiming EffectiveModeIndicator => ModeIndicator != ModeIndicatorTiming.Default ? ModeIndicator
        : OperatingSystem.IsWindows() ? Mode == InputMode.Keyboard ? ModeIndicatorTiming.OnSwitchAndFocus : ModeIndicatorTiming.Never
        : ModeIndicatorTiming.OnSwitch;


    [Category("2. 入力モード"), DisplayName("入力モードの表示の色: 日本語 (あ)"), Platforms(Platform.Windows),
     Description("「あ」の背景色を #RRGGBB で書きます (例: #007ACC)。空なら青。壁紙・アプリのテーマで見えにくいときに変えます。")]
    public string ModeIndicatorJapaneseColor { get; set; } = "";

    [Category("2. 入力モード"), DisplayName("入力モードの表示の色: 英数 (A)"), Platforms(Platform.Windows),
     Description("「A」の背景色を #RRGGBB で書きます (例: #D32F2F で赤)。空なら灰色。英数になっているのに気づかずに打ち始めてしまうときは、目立つ色にします。")]
    public string ModeIndicatorDirectColor { get; set; } = "";

    [Category("2. 入力モード"), DisplayName("入力モードの表示の色: 文字"), Platforms(Platform.Windows),
     Description("「あ」「A」の文字の色を #RRGGBB で書きます (例: #000000)。空なら白。")]
    public string ModeIndicatorTextColor { get; set; } = "";

    [Category("8. 表示"), DisplayName("変換ボックスの位置"), Platforms(Platform.Windows),
     Description("入力位置に重ねる: 打っている文字が入力欄の中の入力位置にそのまま出ているように見えます。カーソルの下: 入力位置の下に別の枠で出します (今までの出し方)。カーソルの上: 入力位置の上に別の枠で出します (候補の一覧が入力欄や下の行を隠さない)。入力位置が分からないアプリでは、どれも入力欄の下に出します。")]
    public CompositionPlacement CompositionPlacement { get; set; } = CompositionPlacement.Overlay;

    [Category("8. 表示"), DisplayName("変換ボックスの文字の大きさ"), Platforms(Platform.Windows),
     Description("自動: 入力欄の文字の高さに合わせます (小さな入力欄では小さく出ます)。入力欄の文字の高さが分からないアプリでは「中」になります。小さな文字が読みにくいときは「特大」「最大」も選べます。")]
    public CompositionSize CompositionSize { get; set; } = CompositionSize.Auto;

    [Category("8. 表示"), DisplayName("変換ボックスのフォント"), Platforms(Platform.Windows),
     Description("変換ボックスの文字のフォントです。既定は Yu Gothic UI です。この PC に無いフォントを選んでいたときは既定のフォントで出します。絵文字はカラーで出せるときはそのフォントで描きます。")]
    public string CompositionFont { get; set; } = "";

    [Category("8. 表示"), DisplayName("変換ボックスの色"), Platforms(Platform.Windows),
     Description("ダーク: 黒っぽい背景に白い文字 (今までの見た目)。ライト: 白っぽい背景に黒い文字。Windows の設定に合わせる: Windows の「既定のアプリ モード」(設定の 個人用設定 > 色) に合わせます。")]
    public CompositionTheme CompositionTheme { get; set; } = CompositionTheme.Dark;

    [Category("8. 表示"), DisplayName("変換ボックスの不透明度"), Platforms(Platform.Windows),
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

    [Category("1. 全般"), DisplayName("通知を出す"), Platforms(Platform.Windows),
     Description("Meltype を有効・一時停止にしたときなどに、画面の右下に通知を出します (Windows の通知の音も鳴ります)。OFF にすると通知も音も出しません。")]
    public bool ShowNotifications { get; set; } = true;

    [Category("12. 更新"), DisplayName("自動で更新する"), Platforms(Platform.Windows),
     Description("新しい版が公開されたら自動でダウンロードし、次に Meltype を起動したとき (Windows にサインインしたとき) に更新します。トレイの「更新して再起動」で今すぐ更新もできます。設定・学習データはそのまま残ります。")]
    public bool AutoUpdate { get; set; } = true;

    [Category("12. 更新"), DisplayName("プレリリース版も自動で更新する"), Platforms(Platform.Windows),
     Description("ON にすると、正式版の前に公開するプレリリース版 (1.3.1-pre.4 など) も自動更新の対象にします。新しい機能や修正を早く試せますが、不具合や動作の不安定さが含まれることがあります。OFF (標準) では正式版だけを対象にします。")]
    public bool UpdatePrerelease { get; set; }

    [Category("1. 全般"), DisplayName("入力方式"), Description("ローマ字入力 / かな入力 (JIS、α版: 試験中)。Meltype キーボードでは、かな入力を選ぶと JIS かな配列で入力し (Shift+E = ぃ, Shift+Z = っ, Shift+ね = 、)、打ったキーの英字が英単語なら英字で見せます。")]
    [JsonConverter(typeof(InputStyleJsonConverter))]
    public InputStyle InputStyle { get; set; } = InputStyle.Romaji;

    [Category("1. 全般"), DisplayName("かな配列"), Platforms(Platform.Windows),
     Description("入力方式が「かな入力」のときの配列。JIS かな (刻印どおり)・月配列 (U9。D・K を押してから次のキーで 2 段目・3 段目)・花配列 (D・K を押してから次のキーで 2 段目)・NICOLA (親指シフト。無変換が左、変換が右の親指キー)・中指NICOLA (NICOLA に加え、K を押したまま左手のキーを押して先に離すと左シフト、D を押したまま右手のキーを押して先に離すと右シフト)・TRON 配列 (親指シフト)。親指シフトは、親指キーを押したまま文字キーを押すか、文字キーの直後 (0.1 秒以内) に親指キーを押します。親指キーだけを押して離すと、ふつうの変換・無変換です。Shift を押したキーはふつうの英字・記号です。JIS かな以外では、打ったキーの英字を英単語として見せません。Meltype キーボードで使えます。")]
    public KanaLayout KanaLayout { get; set; } = KanaLayout.Jis;

    [Category("1. 全般"), DisplayName("ローマ字の配列"), Platforms(Platform.Windows),
     Description("入力方式が「ローマ字入力」のときのキーの配列。QWERTY (ふつう) か SKY 配列 (左手が子音 , W R M H / N T S K Y / P D Z G B、右手が母音 UU AI OU 。 EI / U A O I E / UN AN ON IN EN)。SKY 配列では、英字のキーを SKY 配列のローマ字にしてから読みます (拗音は子音 + Y + 母音、促音は子音を 2 回)。英語の自動判定は使わず、日本語として読みます。Meltype キーボードで使えます。")]
    public RomajiLayout RomajiLayout { get; set; } = RomajiLayout.Qwerty;

    [Category("3. 判定"), DisplayName("英語 / 日本語を自動で判定する"),
     Description("打った英字が英語か日本語 (ローマ字) かを自動で判定し、英語なら英字にします。OFF にすると、Shift で打った大文字で始まる語だけを英字にし、ほかの語は提案だけ出します (変換ボックスで Tab を押すと提案どおり英字に)。英数状態の検知と、確定後の直しもしません。Meltype キーボード・Meltype IME・かな入力のすべてに効きます。")]
    public bool AutoDetection { get; set; } = true;

    /// <summary>config.json の形式のバージョン。古い既定値を持つ設定ファイルを移行するのに使う。</summary>
    [Browsable(false)]
    public int SettingsVersion { get; set; } = CurrentVersion;

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
        SchemaKey, nameof(Profiles), nameof(ActiveProfile), nameof(SettingsVersion),
        nameof(Enabled), nameof(JapaneseKeyboardOnly), nameof(AllowInjectedInput), nameof(FileLog), nameof(LogTypedText), nameof(AutoUpdate), nameof(UpdatePrerelease),
        // 入力の方式 (Windows の IME として入力するか) は PC 全体の選び方なので、プロファイルで変えない
        nameof(Mode),
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
            if (JsonNode.Parse(json, documentOptions: DocumentOptions) is not JsonObject file ||
                file["format"]?.GetValue<string>() != ProfileFileFormat || file["values"] is not JsonObject raw) return null;
            name = (file["name"]?.GetValue<string>() ?? "").Trim();
            var fileVersion = file["version"] is JsonValue version && version.TryGetValue<int>(out var v) ? v : 1;
            if (fileVersion < 8) MigrateToV8(raw);
            if (fileVersion < 9) MigrateToV9(raw);
            if (fileVersion < 10) MigrateToV10(raw, OperatingSystem.IsWindows(), Mode == InputMode.Keyboard);
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
    /// 画面を開いている間にトレイなどで変えた項目 (一時停止・動作モード・自動判定・プロファイルなど) は、画面で変えていなければ current のまま。
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

    public const int CurrentVersion = 10;

    [Category("9. 学習"), DisplayName("ユーザー学習を使う"),
     Description("OFF にすると、選び直した変換・英字 / かなに直した語・予測変換の語句・選んだ英訳を新しく覚えません (今まで覚えた分は使います)。今まで覚えた分を消すときは、トレイの「学習データを消す」を使います。")]
    public bool LearningEnabled { get; set; } = true;

    /// <summary>このアプリの確定した文字の入れ方 (<see cref="AppRule.Commit"/>)。アプリ別設定に無ければ自動。</summary>
    public CommitMethod CommitMethodFor(string? processName) => FindRule(processName)?.Commit ?? CommitMethod.Auto;

    /// <summary>このアプリでは、入力欄と判定できなくてもフォーカスのある所を入力欄として扱うか (<see cref="AppRule.TextInput"/>)。</summary>
    public bool TreatsAsTextInput(string? processName) => FindRule(processName)?.TextInput ?? false;

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

    [Category("10. アプリ"), DisplayName("全画面アプリでは無効"), Platforms(Platform.Windows), Description("ゲームや動画など全画面のウィンドウではキーを保留しません。")]
    public bool ExcludeFullscreen { get; set; } = true;

    [Category("10. アプリ"), DisplayName("ゲームでは止める"), Platforms(Platform.Windows),
     Description("Steam・Epic Games・Riot Games・EA・Ubisoft・Battle.net・Xbox のゲームのフォルダーにあるアプリ (ウィンドウ表示のゲームも) では、Meltype キーボードは何もしません (キーを横取りせず、Windows の IME にも触らないので、ゲームのチャットは Windows の IME で打てます)。Meltype IME は英数で始め、英数ではキーを横取りしません (チャットでは 半角/全角 で日本語にできます)。ほかのゲームは、アプリ別設定で種類を「ゲーム」にしてください。")]
    public bool StopInGames { get; set; } = true;

    [Category("10. アプリ"), DisplayName("アプリ別設定"), Description("アプリ (Windows はプロセス名、macOS は bundle ID、Linux はプログラム名) ごとに、ON/OFF・種類・確定した文字の入れ方・入力欄とみなすかを指定します。種類「コード」(コードエディター・ターミナル) では基本は英数のままで、コメント (// # -- など) と文字列 (\"…\" など) の中だけ日本語を判定します。コードの行で 半角/全角 を押すと、その行だけ日本語で入力できます。README.md などの文章ファイルを開いているときは一般として扱います。")]
    public List<AppRule> AppRules { get; set; } = DefaultAppRules();

    [Category("10. アプリ"), DisplayName("独自の種類"), Description("アプリ別設定の「種類」に使える、自分で作る種類です。一般 / コード を元に、自動判定・ライブ変換を変えられます (「全体と同じ」なら上の設定のまま)。")]
    public List<AppKind> AppKinds { get; set; } = [];

    [Category("11. ログ"), DisplayName("ファイルにログを書く"), Description("%LOCALAPPDATA%\\Meltype\\meltype.log にログを書きます (OFF でも、トレイの「ログ / 判定理由」で見られるログは Meltype が動いている間だけメモリに残ります)。判定した語の先頭の数文字・アプリ名・入力欄の名前が含まれます。確定した文字列などは「ログに入力した文字を残す」が ON のときだけ残ります。")]
    public bool FileLog { get; set; }

    [Category("11. ログ"), DisplayName("ログに入力した文字を残す"), Description("確定した文字列・打った英字・読み・直した語をログに残します (不具合を調べるとき用)。OFF なら文字数だけを残します。パスワード欄では入力を扱わないので残りません。")]
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
        // 確定した文字を 1 文字ずつ送ると取り違える (「あいうえお」→「あああああ」)
        new() { Process = "Resolve.exe", Enabled = true, Commit = CommitMethod.Paste },
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
        // macOS (bundle ID)
        "com.microsoft.VSCode", "com.microsoft.VSCodeInsiders", "com.vscodium", "com.todesktop.230313mzl4w4u92", "com.exafunction.windsurf", "dev.zed.Zed",
        "com.apple.dt.Xcode", "com.jetbrains.intellij", "com.jetbrains.intellij.ce", "com.jetbrains.pycharm", "com.jetbrains.pycharm.ce",
        "com.jetbrains.WebStorm", "com.jetbrains.rider", "com.jetbrains.CLion", "com.jetbrains.goland", "com.google.android.studio", "com.sublimetext.4",
        "com.apple.Terminal", "com.googlecode.iterm2", "com.github.wez.wezterm", "org.alacritty", "com.mitchellh.ghostty", "net.kovidgoyal.kitty", "dev.warp.Warp-Stable",
        // Linux (プログラム名)
        "code", "code-insiders", "codium", "cursor", "windsurf", "zed", "zeditor", "sublime_text",
        "gnome-terminal-server", "konsole", "kitty", "alacritty", "wezterm-gui", "foot", "ghostty", "xterm", "tilix", "terminator", "xfce4-terminal",
    ];

    public bool IsAppEnabled(string? processName) => FindRule(processName)?.Enabled ?? true;

    /// <summary>
    /// ゲームか (Meltype キーボードは止める。Meltype IME は英数で始める): アプリ別設定の種類が「ゲーム」か、アプリ別設定が無く、ゲームのフォルダーにあるアプリ (looksLikeGame) で「ゲームでは止める」が ON。
    /// </summary>
    public bool IsGame(string? processName, bool looksLikeGame) =>
        ProfileFor(processName) == AppProfile.Game || (StopInGames && looksLikeGame && FindRule(processName) is null);

    /// <summary>アプリの種類 (アプリ別設定に無ければ一般)。独自の種類なら、その元にした種類。</summary>
    public AppProfile ProfileFor(string? processName) =>
        KindFor(processName)?.Base ?? FindRule(processName)?.Profile ?? AppProfile.General;

    /// <summary>アプリに割り当てた独自の種類 (無ければ null)。</summary>
    public AppKind? KindFor(string? processName) =>
        FindRule(processName)?.Kind is { Length: > 0 } name ? AppKinds.FirstOrDefault(k => k.Name == name) : null;

    /// <summary>アプリの独自の種類で変えた項目 (自動判定・ライブ変換) を反映した設定。変えていなければ自分自身。</summary>
    public Settings ForApp(string? processName)
    {
        if (KindFor(processName) is not { } kind || (kind.AutoDetection is null && kind.LiveConversion is null)) return this;
        var copy = Clone();
        if (kind.AutoDetection is { } auto) copy.AutoDetection = auto;
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
        copy.AppRules = AppRules.Select(r => r.Clone()).ToList();
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
        ZenzaiDevice = ZenzaiDevice?.Trim() ?? "";
        ClockTimeZone = ClockTimeZone?.Trim() ?? "";
        // 数値で書いた config.json の、今は無い番号 (消した IME 自動切替・両方を判定) は初期値にする
        if (!Enum.IsDefined(Mode)) Mode = InputMode.Tsf;
        if (!Enum.IsDefined(InputStyle)) InputStyle = InputStyle.Romaji;
        if (!Enum.IsDefined(KanaLayout)) KanaLayout = KanaLayout.Jis;
        if (!Enum.IsDefined(RomajiLayout)) RomajiLayout = RomajiLayout.Qwerty;
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
        // v8: 型を変えた項目は、読み込む前の JSON で移す (MigrateToV8)。
        // v9: Zenzai を既定 ON にした。プロファイルの値にも効くよう、読み込む前の JSON で移す (MigrateToV9)。
        SettingsVersion = CurrentVersion;
        return true;
    }

    /// <summary>
    /// v10: 入力モードの表示を、どのフロントも ModeIndicator で決めるようにした (前の ModeIndicator は Meltype キーボードだけの設定)。
    /// Windows で動作モードが Meltype IME (既定) なら、今までの Meltype IME の表示 (ShowTsfModeIndicator。切り替えたときだけ・既定は出さない) にする。
    /// Mac・Linux は前の ModeIndicator を読んでいなかった (1.5.0 は全項目を保存するので、既定の値が書いてある) ので消して既定に戻す。
    /// Mac のメニューで表示を OFF にしていたら (MacShowModeIndicator。Mac のフロントが UserDefaults から移す) 出さない。
    /// </summary>
    /// <param name="keyboard">動作モードが Meltype キーボードか。動作モードは全プロファイル共通で、config.json の一番上にだけある。</param>
    internal static void MigrateToV10(JsonObject values, bool windows, bool keyboard)
    {
        var tsf = values.Remove("ShowTsfModeIndicator", out var shown) && shown is JsonValue value && value.TryGetValue<bool>(out var on) && on;
        var macOff = values.Remove("MacShowModeIndicator", out var mac) && mac is JsonValue macValue && macValue.TryGetValue<bool>(out var macOn) && !macOn;
        if (windows)
        {
            if (!keyboard) values[nameof(ModeIndicator)] = (tsf ? ModeIndicatorTiming.OnSwitch : ModeIndicatorTiming.Never).ToString();
            return;
        }
        values.Remove(nameof(ModeIndicator));
        if (macOff) values[nameof(ModeIndicator)] = ModeIndicatorTiming.Never.ToString();
    }

    private static bool IsKeyboardMode(JsonObject values) =>
        values[nameof(Mode)] is JsonValue mode && mode.TryGetValue<string>(out var name) && string.Equals(name, nameof(InputMode.Keyboard), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// v9: Zenzai を同梱して既定 ON にした。前の既定 (OFF・場所が空) のままなら ON にする。
    /// config.json の一番上・プロファイルの値・書き出したプロファイルの値 (どれも config.json と同じ形) に使う。
    /// </summary>
    internal static void MigrateToV9(JsonObject values)
    {
        // v9: Zenzai を同梱して既定 ON にした (issue #1120)。前の既定 (OFF・場所が空) のままなら ON にする。
        // 自分で llama-server やモデルの場所を書いた人は、OFF にしていればそのまま。書いていない項目は新しい既定 (ON) になる
        static string Text(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var t) ? t : "";
        if (values[nameof(UseZenzai)] is JsonValue use && use.TryGetValue<bool>(out var on) && !on &&
            string.IsNullOrWhiteSpace(Text(values, nameof(ZenzaiServerPath))) && string.IsNullOrWhiteSpace(Text(values, nameof(ZenzaiModelPath))))
            values[nameof(UseZenzai)] = true;
    }

    /// <summary>
    /// v8: 同時に ON にすると片方が負ける真偽値の組を 1 つの選択肢にまとめ、自動判定の強さ (DetectionLevel) を AutoDetection のオン・オフにし、
    /// アプリの一覧の文字列 (PasteApps・NoPasteApps・TextInputApps) をアプリ別設定の項目に移した。
    /// config.json の一番上・プロファイルの値・書き出したプロファイルの値 (どれも config.json と同じ形) に使う。
    /// 消した項目 (JapaneseThreshold・TypoEnabled・MaxPendingKeys・ImeTimeoutMs・LogKeyIntervals) と、state.json に移した項目 (AppState) は、読み込みで無視されて次の保存で消える。
    /// </summary>
    internal static void MigrateToV8(JsonObject values)
    {
        bool Flag(string key, bool fallback) => values[key] is JsonValue value && value.TryGetValue<bool>(out var on) ? on : fallback;
        void Replace(string key, string[] legacy, Func<Enum> choose)
        {
            if (!legacy.Any(values.ContainsKey)) return;
            values[key] = choose().ToString();
            foreach (var old in legacy) values.Remove(old);
        }
        Replace(nameof(Slash), ["SlashAsMiddleDot", "FullWidthSlash"],
            () => Flag("SlashAsMiddleDot", false) ? SlashStyle.MiddleDot : Flag("FullWidthSlash", false) ? SlashStyle.FullWidth : SlashStyle.Slash);
        Replace(nameof(SpaceWidth), ["FullWidthSpaceByDefault", "ShiftSpaceHalfWidth"],
            () => Flag("FullWidthSpaceByDefault", false) ? SpaceWidth.FullWidth : Flag("ShiftSpaceHalfWidth", false) ? SpaceWidth.HalfWidth : SpaceWidth.Standard);
        // 両方 ON だと、変換中の Ctrl + K / L だけ ATOK 風、ほかは macOS 風だった。macOS 風にする
        Replace(nameof(ControlKeys), ["MacControlKeys", "CompatibleShortcuts"],
            () => Flag("MacControlKeys", false) ? ControlKeyLayout.Mac : Flag("CompatibleShortcuts", false) ? ControlKeyLayout.Atok : ControlKeyLayout.Standard);
        Replace(nameof(ModeIndicator), ["ShowModeIndicator", "ShowModeIndicatorOnFocus"],
            () => !Flag("ShowModeIndicator", true) ? ModeIndicatorTiming.Never : Flag("ShowModeIndicatorOnFocus", true) ? ModeIndicatorTiming.OnSwitchAndFocus : ModeIndicatorTiming.OnSwitch);

        // 自動判定の強さ (積極的・標準・慎重・手動) は、手動だけを AutoDetection の OFF にする (積極的・慎重は標準として読む: #1232)。数値で書いたものは 3 が手動
        static bool? Manual(JsonNode? level) => level is not JsonValue value ? null
            : value.TryGetValue<string>(out var name) ? string.Equals(name, "Manual", StringComparison.OrdinalIgnoreCase)
            : value.TryGetValue<int>(out var number) ? number == 3 : null;
        if (values.Remove("DetectionLevel", out var level) && Manual(level) == true) values[nameof(AutoDetection)] = false;
        foreach (var kind in (values[nameof(AppKinds)] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (kind.Remove("DetectionLevel", out var kindLevel) && Manual(kindLevel) is { } manual) kind[nameof(AppKind.AutoDetection)] = !manual;
        }

        string[] lists = ["PasteApps", "NoPasteApps", "TextInputApps"];
        if (!lists.Any(values.ContainsKey)) return;
        if (values[nameof(AppRules)] is not JsonArray rules)
            values[nameof(AppRules)] = rules = JsonSerializer.SerializeToNode(DefaultAppRules(), SettingsJsonContext.Default.ListAppRule)!.AsArray();
        void Move(string key, Action<JsonObject> apply)
        {
            var list = values[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
            values.Remove(key);
            foreach (var app in SplitApps(list))
            {
                // 同じアプリが 2 つ以上あれば Normalize が後ろを残すので、後ろに入れる
                var rule = rules.OfType<JsonObject>().LastOrDefault(r => string.Equals(r[nameof(AppRule.Process)]?.GetValue<string>()?.Trim(), app, StringComparison.OrdinalIgnoreCase));
                if (rule is null)
                {
                    rule = JsonSerializer.SerializeToNode(new AppRule { Process = app }, SettingsJsonContext.Default.AppRule)!.AsObject();
                    rules.Add((JsonNode)rule);
                }
                apply(rule);
            }
        }
        Move("PasteApps", rule => rule[nameof(AppRule.Commit)] = nameof(CommitMethod.Paste));
        Move("NoPasteApps", rule => rule[nameof(AppRule.Commit)] = nameof(CommitMethod.Keys));
        Move("TextInputApps", rule => rule[nameof(AppRule.TextInput)] = true);
    }

    /// <summary>
    /// v8 より前のアプリの一覧 (カンマ・セミコロン・改行区切りのプロセス名) を分ける。「Adobe Premiere Pro.exe」のように空白を含む名前はそのまま 1 つの名前にする。
    /// 前からの書き方 (空白区切り「a.exe b.exe」) は、空白で分けたものがすべて .exe で終わるときだけ分ける。
    /// </summary>
    private static IEnumerable<string> SplitApps(string list)
    {
        foreach (var entry in list.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var apps = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (apps.Length > 1 && apps.All(app => app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                foreach (var app in apps) yield return app;
            else
                yield return entry;
        }
    }

    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static Settings Load(string path)
    {
        try
        {
            return LoadOrThrow(path);
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

    /// <summary><see cref="Load"/> と同じだが、読めなければ例外を投げる (既定値に戻さず、.broken も作らない)。</summary>
    internal static Settings LoadOrThrow(string path)
    {
        if (!File.Exists(path)) return new Settings();
        var json = File.ReadAllText(path);
        if (JsonNode.Parse(json, documentOptions: DocumentOptions) is not JsonObject values) throw new JsonException("config.json の一番上が { } ではありません");
        var version = values[nameof(SettingsVersion)] is JsonValue number && number.TryGetValue<int>(out var v) ? v : 1;
        if (version < 8)
        {
            MigrateToV8(values);
            foreach (var profile in (values[nameof(Profiles)] as JsonArray ?? []).OfType<JsonObject>())
                if (profile[nameof(SettingsProfile.Values)] is JsonObject profileValues) MigrateToV8(profileValues);
        }
        if (version < 9)
        {
            MigrateToV9(values);
            foreach (var profile in (values[nameof(Profiles)] as JsonArray ?? []).OfType<JsonObject>())
                if (profile[nameof(SettingsProfile.Values)] is JsonObject profileValues) MigrateToV9(profileValues);
        }
        if (version < 10)
        {
            var keyboard = IsKeyboardMode(values);
            MigrateToV10(values, OperatingSystem.IsWindows(), keyboard);
            foreach (var profile in (values[nameof(Profiles)] as JsonArray ?? []).OfType<JsonObject>())
                if (profile[nameof(SettingsProfile.Values)] is JsonObject profileValues) MigrateToV10(profileValues, OperatingSystem.IsWindows(), keyboard);
        }
        var settings = values.Deserialize(JsonType) ?? new Settings();
        settings.SettingsVersion = version;
        // 動作モードが IME 自動切替 (消した) だった: Meltype IME として読んでいる (InputModeJsonConverter)。保存して名前を書き直す
        var autoSwitch = TopLevelStringIs(json, nameof(Mode), "AutoSwitch");
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

    /// <summary>config.json の一番上の項目 property が、文字列の value か (プロファイルの中の値は見ない)。</summary>
    internal static bool TopLevelStringIs(string json, string property, string value)
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
