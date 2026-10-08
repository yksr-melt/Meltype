// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 品質の採点テスト。日本語・英語・混在・記号・アプリ別 (コードのコメント) などの例をまとめて流し、カテゴリーごとの正解率を出す。
/// 1 つずつの細かいテストと違い、「全体としてどれくらい正しく打てるか」を数字で見るためのもの。
/// 期待値は「理想の結果」で書いてあり、今の実装で外れるものもある (外れたものは一覧に出る)。
/// 正解率が基準を下回ったら失敗にして、ある直しで別の場所が壊れたことに気づけるようにする。
///
///   dotnet run --project src/Meltype.Tests -- --eval     … カテゴリーごとの正解率と、外れた例の一覧
/// </summary>
internal static class Quality
{
    /// <summary>変換ボックスで打つ例。Typed は打つ文字 (' ' は Space、Enter は最後に自動で押す)。Expected は入力欄に入る文字列。</summary>
    private sealed record TypingCase(string Category, string Typed, string Expected, string? Before = null);

    private static readonly TypingCase[] Typing =
    [
        // --- 日本語の文 (テスト用の変換エンジンはかなのまま返す) ---
        new("日本語", "kyouhaiitenkidesune", "きょうはいいてんきですね"),
        new("日本語", "hosuthingu", "ほすてぃんぐ"),
        new("日本語", "tsukuenouenoitsumonohon", "つくえのうえのいつものほん"),
        new("日本語", "matsurinokatsudounoketsuron", "まつりのかつどうのけつろん"),
        new("日本語", "watashihagakuseidesu", "わたしはがくせいです"),
        new("日本語", "arigatougozaimasu", "ありがとうございます"),
        new("日本語", "yoroshikuonegaishimasu", "よろしくおねがいします"),
        new("日本語", "ashitahaamedesu", "あしたはあめです"),
        new("日本語", "konnnichiha", "こんにちは"),
        new("日本語", "nihongowobenkyoushiteimasu", "にほんごをべんきょうしています"),
        new("日本語", "kaigiha3jikaradesu", "かいぎは3じからです"),
        new("日本語", "sushigatabetai", "すしがたべたい"),
        new("日本語", "anatahadaredesuka", "あなたはだれですか"),
        new("日本語", "sorehachigaimasu", "それはちがいます"),
        new("日本語", "kinouhatanoshikatta", "きのうはたのしかった"),
        new("日本語", "mainichiasagohanwotabemasu", "まいにちあさごはんをたべます"),
        new("日本語", "denshaninoriokureteshimatta", "でんしゃにのりおくれてしまった"),
        new("日本語", "nanimoshiranai", "なにもしらない"),
        new("日本語", "makenaide", "まけないで"),
        new("日本語", "sonotoori", "そのとおり"),
        new("日本語", "hontouni", "ほんとうに"),
        new("日本語", "dokodemoii", "どこでもいい"),
        new("日本語", "chottomatte", "ちょっとまって"),
        new("日本語", "kaimononiikimasu", "かいものにいきます"),
        new("日本語", "namaewooshiete", "なまえをおしえて"),
        new("日本語", "kanjiwokaku", "かんじをかく"),
        new("日本語", "sakewonomu", "さけをのむ"),
        new("日本語", "animegasuki", "あにめがすき"),
        new("日本語", "mangawoyomu", "まんがをよむ"),
        new("日本語", "karaokeniiku", "からおけにいく"),
        new("日本語", "itadakimasu", "いただきます"),
        new("日本語", "otsukaresamadesu", "おつかれさまです"),
        new("日本語", "sumimasen", "すみません"),
        new("日本語", "daijoubu", "だいじょうぶ"),
        new("日本語", "ganbatte", "がんばって"),
        new("日本語", "tanakasannnihanashita", "たなかさんにはなした"),
        new("日本語", "toukyouniiku", "とうきょうにいく"),
        new("日本語", "kore ha pen desu", "これはぺんです"),
        new("日本語", "sore de ii", "それでいい"),
        new("日本語", "watashi ha sushi ga suki", "わたしはすしがすき"),
        new("日本語", "nani wo shiteru no", "なにをしてるの"),

        // --- 英語とも日本語とも読める語 (文脈がなければ日本語) ---
        new("曖昧な語", "same", "さめ"),
        new("曖昧な語", "mine", "みね"),
        new("曖昧な語", "sake", "さけ"),
        new("曖昧な語", "sushi", "すし"),
        new("曖昧な語", "sushi", "sushi", Before: "I like "),
        new("曖昧な語", "sushi", "すし", Before: "今日は"),
        new("曖昧な語", "repo", "れぽ"),
        new("曖昧な語", "make", "make", Before: "Please "),

        // --- 英語 ---
        new("英語", "hello", "hello"),
        new("英語", "google", "google"),
        new("英語", "github", "github"),
        new("英語", "python", "python"),
        new("英語", "javascript", "javascript"),
        new("英語", "thank you", "thank you"),
        new("英語", "good morning", "good morning"),
        new("英語", "I love you", "I love you"),
        new("英語", "see you later", "see you later"),
        new("英語", "what is this", "what is this"),
        new("英語", "how are you", "how are you"),
        new("英語", "the quick brown fox", "the quick brown fox"),
        new("英語", "please check the log", "please check the log"),
        new("英語", "open the file", "open the file"),
        new("英語", "run the tests", "run the tests"),
        new("英語", "git push origin main", "git push origin main"),
        new("英語", "npm install", "npm install"),
        new("英語", "meeting at nine", "meeting at nine"),
        new("英語", "I want to go to the park", "I want to go to the park"),
        new("英語", "make sure you have time", "make sure you have time"),
        new("英語", "this is a pen", "this is a pen"),
        new("英語", "my name is taro", "my name is taro"),
        new("英語", "do it now", "do it now"),
        new("英語", "let me know", "let me know"),
        new("英語", "can you send me the file", "can you send me the file"),

        // --- 日本語の中の英単語 ---
        new("混在", "kyouhagoogledekensaku", "きょうはgoogleでけんさく"),
        new("混在", "tanaka@example.com", "tanaka@example.com"),
        new("混在", "yamada.taro@example.co.jp", "yamada.taro@example.co.jp"),
        new("混在", "foo_bar@example.com", "foo_bar@example.com"),
        new("混在", "taro+news@example.com", "taro+news@example.com"),
        new("混在", "yamada-taro@example.com", "yamada-taro@example.com"),
        new("混在", "reflectsareta", "reflectされた"),
        new("混在", "selectshita", "selectした"),
        new("混在", "inviteshimashita", "inviteしました"),
        new("混在", "invitewookutta", "inviteをおくった"),
        new("混在", "githubnipushshita", "githubにpushした"),
        new("混在", "pythondekaita", "pythonでかいた"),
        new("混在", "korehathin", "これはthin"),
        new("混在", "zoomdekaigi", "zoomでかいぎ"),
        new("混在", "slackderenraku", "slackでれんらく"),
        new("混在", "amazondekaimono", "amazonでかいもの"),
        new("混在", "youtubewomiru", "youtubeをみる"),
        new("混在", "chatgptnikiku", "chatgptにきく"),
        new("混在", "netflixdemiru", "netflixでみる"),
        new("混在", "discordnisanka", "discordにさんか"),
        new("混在", "linewookuru", "lineをおくる"),
        new("混在", "apinoerror", "apiのerror"),
        new("混在", "bugwonaosu", "bugをなおす"),
        new("混在", "windowsnoupdate", "windowsのupdate"),
        new("日本語", "shiryouwookurimasu", "しりょうをおくります"),
        new("日本語", "kakuninshitekudasai", "かくにんしてください"),
        new("日本語", "yoroshiidesuka", "よろしいですか"),
        new("日本語", "ryoukaishimashita", "りょうかいしました"),
        new("日本語", "ashitanokaiginitsuite", "あしたのかいぎについて"),
        new("日本語", "shoushouomachikudasai", "しょうしょうおまちください"),
        new("日本語", "otsukaresamadeshita", "おつかれさまでした"),
        new("日本語", "tesutogaowarimashita", "てすとがおわりました"),
        new("日本語", "sonokenhakentoushimasu", "そのけんはけんとうします"),
        new("日本語", "mouichidoonegaishimasu", "もういちどおねがいします"),
        new("日本語", "nanjigaiidesuka", "なんじがいいですか"),
        new("日本語", "zenzenmondainai", "ぜんぜんもんだいない"),
        new("日本語", "tokorodesa", "ところでさ"),
        new("日本語", "hayakukaerou", "はやくかえろう"),
        new("日本語", "kyouhasamuine", "きょうはさむいね"),
        new("英語", "good job", "good job"),
        new("英語", "see you tomorrow", "see you tomorrow"),
        new("英語", "no problem", "no problem"),
        new("英語", "hello world", "hello world"),
        new("英語", "thank you so much", "thank you so much"),
        new("英語", "we need to fix the bug", "we need to fix the bug"),
        new("英語", "please take a look at this", "please take a look at this"),
        new("英語", "the meeting is at nine", "the meeting is at nine"),
        new("英語", "happy birthday", "happy birthday"),
        new("英語", "what time is it", "what time is it"),
        new("混在", "reactdekaita", "reactでかいた"),
        new("混在", "dockergaugokanai", "dockerがうごかない"),
        new("混在", "typescriptnikaeta", "typescriptにかえた"),
        new("混在", "excelnofairu", "excelのふぁいる"),
        new("混在", "spotifydekiku", "spotifyできく"),
        new("混在", "microsoftnokaisha", "microsoftのかいしゃ"),
        new("混在", "kyouhaclaudetohanashita", "きょうはclaudeとはなした"),
        new("混在", "pythonnobug", "pythonのbug"),
        new("混在", "GitHub no repo", "GitHub のれぽ"),
        new("混在", "Google to Apple", "Google とApple"),
        // 助詞「や」+ 英単語 / 英単語 + 助詞「や」(Particles / TrailingParticles に ya)
        new("混在", "yagithub", "やgithub"),
        new("混在", "kyouhayagithub", "きょうはやgithub"),
        new("混在", "hagithub", "はgithub"),
        new("混在", "githubya", "githubや"),
        new("混在", "githubni", "githubに"),
        // 助詞「や」+ 英単語が 1 語にまとまらないこと (IsUnknownEnglishWord / yap|lay の誤分割)
        new("混在", "yapython", "やpython"),
        new("混在", "yacursor", "やcursor"),
        new("混在", "yaslack", "やslack"),
        new("混在", "yastack", "やstack"),
        new("混在", "yaplay", "やplay"),
        new("混在", "yasync", "やsync"),
        // site (=して) のあと英字が続いても、確定し直しで site に戻さない
        new("混在", "sitePR", "してPR"),
        new("混在", "site PR", "して PR"),
        // -pedia (ローマ字だと ぺぢあ。#207)
        new("混在", "pedia", "pedia"),
        new("混在", "protopedia", "protopedia"),
        new("混在", "conservapedia", "conservapedia"),
        new("混在", "wikipedia", "wikipedia"),
        new("英語", "yahoo", "yahoo"),

        // --- 英語の後の短い語 (助詞と同じ形) ---
        new("短い語", "no", "の", Before: "GitHub"),
        new("短い語", "de", "で", Before: "Chrome"),
        new("短い語", "ni", "に", Before: "今日は GitHub "),
        new("短い語", "ga", "が", Before: "Python"),
        new("短い語", "ya", "や", Before: "GitHub"),
        new("短い語", "to", "to", Before: "I want "),
        new("短い語", "is", "is", Before: "this "),
        new("短い語", "at", "at", Before: "look "),
        new("短い語", "a", "a", Before: "this is "),

        // --- 記号・数字 ---
        new("記号・数字", "!", "！"),
        new("記号・数字", "?", "？"),
        new("記号・数字", "~", "～"),
        new("記号・数字", "!", "!", Before: "Hello"),
        new("記号・数字", "kore,sore.", "これ、それ。"),
        new("記号・数字", "[kagi]", "「かぎ」"),
        new("記号・数字", "ra-men", "らーめん"),
        new("記号・数字", "3.14", "3.14"),
        new("記号・数字", "2026/09/30", "2026/09/30"),
        new("記号・数字", "10ji", "10じ"),

        // --- 小書き文字・特殊な綴り ---
        new("綴り", "xa", "ぁ"),
        new("綴り", "la", "ぁ"),
        new("綴り", "mala", "まぁ"),
        new("綴り", "ltu", "っ"),
        new("綴り", "thi", "てぃ"),
        new("綴り", "fa", "ふぁ"),
        new("綴り", "je", "じぇ"),
        new("綴り", "nn", "ん"),
        new("綴り", "kanji", "かんじ"),

        // --- Shift を押した大文字 ---
        new("大文字", "Tokyo", "Tokyo"),
        new("大文字", "Apple", "Apple"),
        new("大文字", "I", "I"),
        new("大文字", "W", "W"),
        new("大文字", "kaW", "かW"),
        new("大文字", "AInituite", "AIについて"),
        new("大文字", "AIdekiru", "AIできる"),
    ];

    /// <summary>コードの行のキャレット位置 (コメント・文字列の中か)。</summary>
    private static readonly (string Line, LineKind Expected)[] CodeLines =
    [
        ("int x = 0;", LineKind.Code),
        ("// コメント", LineKind.Comment),
        ("x = 1  // note ", LineKind.Comment),
        ("# comment ", LineKind.Comment),
        ("    ## 見出し", LineKind.Comment),
        ("#include <stdio.h>", LineKind.Code),
        ("#region Foo", LineKind.Code),
        ("print(\"hello ", LineKind.String),
        ("print(\"hi\") ", LineKind.Code),
        ("console.log('abc", LineKind.String),
        ("if (x) { // TODO ", LineKind.Comment),
        ("it's fine", LineKind.Code),
        ("/* block ", LineKind.Comment),
        ("/* done */ x = ", LineKind.Code),
        (" * continued ", LineKind.Comment),
        ("-- SQL comment ", LineKind.Comment),
        ("x--;", LineKind.Code),
        ("i--", LineKind.Code),
        ("<!-- html ", LineKind.Comment),
        ("git commit -m \"", LineKind.String),
        ("echo `date", LineKind.String),
        ("REM バッチ ", LineKind.Comment),
        ("s = \"a\\\"b", LineKind.String),
        ("url = 'http://x' + ", LineKind.Code),
        ("const s = `template ${x} ", LineKind.String),
        // ターミナルで動く AI・チャット (Claude Code・Codex など)
        ("> ", LineKind.Prompt),
        ("│ > こんにちは", LineKind.Prompt),
        ("› fix the bug ", LineKind.Prompt),
        (">> ", LineKind.Code),
        (@"PS C:\Users\me> ", LineKind.Code),
        ("$ ls ", LineKind.Code),
        ("❯ git status ", LineKind.Code),
        ("", LineKind.Code),
    ];

    /// <summary>「コード」のアプリで、フォーカスのある入力欄の種類 (プロセス名, UI Automation の名前, クラス名)。</summary>
    private static readonly (string Process, string Name, string ClassName, CodeFocus Expected)[] Focuses =
    [
        ("Code.exe", "Editor content;Press Alt+F1 for Accessibility Options.", "", CodeFocus.Editor),
        ("Code.exe", "The editor is not accessible at this time.", "", CodeFocus.Editor),
        ("Code.exe", "エディターのコンテンツ", "", CodeFocus.Editor),
        ("Code.exe", "Chat Input", "", CodeFocus.None),
        ("Code.exe", "チャット入力", "", CodeFocus.None),
        ("Code.exe", "", "", CodeFocus.None),
        ("Code.exe", "Message Claude…", "", CodeFocus.None),
        ("Code.exe", "Terminal 1, pwsh", "", CodeFocus.Terminal),
        ("Code.exe", "ターミナル 1、bash", "", CodeFocus.Terminal),
        ("Code.exe", "Type the name of a command to run.", "", CodeFocus.Editor),
        ("Cursor.exe", "Composer", "", CodeFocus.None),
        ("WindowsTerminal.exe", "", "TermControl", CodeFocus.Terminal),
        ("pwsh.exe", "", "", CodeFocus.Terminal),
        ("idea64.exe", "Editor", "", CodeFocus.Editor),
        ("idea64.exe", "AI Assistant chat", "", CodeFocus.None),
        ("devenv.exe", "", "", CodeFocus.Editor),
    ];

    private static readonly (string Title, bool Document)[] Titles =
    [
        ("README.md - Meltype - Visual Studio Code", true),
        ("Program.cs - Meltype - Visual Studio Code", false),
        ("notes.txt - メモ帳", true),
        ("page.mdx - site - Visual Studio Code", false),
        ("Windows PowerShell", false),
    ];

    /// <summary>よく使う英単語 (IME 自動切替で日本語と誤判定しないか)。</summary>
    private static readonly string[] CommonEnglish =
    [
        "the", "this", "that", "with", "from", "have", "what", "when", "where", "which", "there", "their", "would", "could", "should",
        "about", "after", "before", "because", "people", "think", "know", "want", "need", "like", "just", "only", "also", "very", "really",
        "hello", "thanks", "please", "sorry", "okay", "great", "good", "nice", "cool", "awesome", "right", "left", "yes",
        "google", "github", "python", "javascript", "windows", "microsoft", "apple", "amazon", "youtube", "twitter", "discord", "slack",
        "function", "return", "class", "const", "static", "public", "private", "import", "export", "default", "async", "await",
        "string", "number", "boolean", "array", "object", "value", "index", "count", "length", "result", "error", "debug", "test",
        "meeting", "schedule", "project", "update", "message", "email", "password", "account", "settings", "download", "install",
        "morning", "tonight", "tomorrow", "yesterday", "weekend", "holiday", "birthday", "friend", "family", "school", "office",
    ];

    /// <summary>1 つの例の結果。Key は「[分類] 入力」(版をまたいで同じ例を比べるのに使う)。</summary>
    public sealed record Case(string Key, bool Ok, string Detail);

    public sealed record Result(
Dictionary<string, (int Pass, int Total)> ByCategory, List<string> Failures, List<Case> Cases)
    {
        public int Pass => ByCategory.Values.Sum(v => v.Pass);
        public int Total => ByCategory.Values.Sum(v => v.Total);
        public double Rate => Total == 0 ? 1 : (double)Pass / Total;
    }

    public static Result Run()
    {
        var categories = new Dictionary<string, (int Pass, int Total)>();
        var failures = new List<string>();
        var cases = new List<Case>();
        void Score(string category, bool ok, string detail)
        {
            var arrow = detail.IndexOf(" → ", StringComparison.Ordinal);
            cases.Add(new Case($"[{category}] {(arrow >= 0 ? detail[..arrow] : detail)}", ok, detail));
            var (pass, total) = categories.GetValueOrDefault(category);
            categories[category] = (pass + (ok ? 1 : 0), total + 1);
            if (!ok) failures.Add($"[{category}] {detail}");
        }

        // 実際と同じく、使えるなら Windows のスペルチェッカーも使う。
        // Windows のスペルチェッカーが無い環境 (Linux・Mac の CI) では、アプリの Mac 版・Linux 版と同じく同梱の英単語の一覧を使う。
        IWordChecker? spell = Environment.GetEnvironmentVariable("MELTYPE_NO_SPELLCHECK") is not null ? null
            : TestSupport.WordChecker is { IsAvailable: true } checker ? checker : Detection.BuiltInWordChecker.Shared;
        CompositionTests.Detector.SpellChecker = spell;
        try
        {
            foreach (var c in Typing)
            {
                var k = new CompositionTests.Keyboard();
                k.Host.PrecedingText = c.Before;
                k.Type(c.Typed + "\n");
                var actual = k.Host.Document;
                Score(c.Category, actual == c.Expected, $"{(c.Before is null ? "" : $"「{c.Before}」+ ")}{c.Typed} → {actual} (期待: {c.Expected})");
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }

        // かな入力 (JIS)
        foreach (var (keys, expected) in new[] { ("byiaf", "こんにちは"), ("google", "google"), ("t@u", "がな"), ("github", "github") })
        {
            var k = new CompositionTests.Keyboard { Kana = true };
            k.TypeKanaKeys(keys);
            k.Type("\n");
            Score("かな入力", k.Host.Document == expected, $"キー {keys} → {k.Host.Document} (期待: {expected})");
        }

        // コードの行
        foreach (var (line, expected) in CodeLines)
        {
            var actual = LineContext.Classify(line);
            Score("コードの行", actual == expected, $"「{line}」→ {actual} (期待: {expected})");
        }
        foreach (var (process, name, className, expected) in Focuses)
        {
            var actual = LineContext.ClassifyFocus(process, name, className);
            Score("入力欄の種類", actual == expected, $"{process} 「{name}」({className}) → {actual} (期待: {expected})");
        }
        foreach (var (title, expected) in Titles)
        {
            var actual = LineContext.IsDocumentTitle(title);
            Score("文章ファイル", actual == expected, $"「{title}」→ {actual} (期待: {expected})");
        }

        // IME 自動切替 (打ち始めの数文字で Microsoft IME を ON にするか): 日本語の辞書の語は英語と決めつけない、よく使う英単語は日本語にしない。
        var scoreEngine = TestSupport.CreateEngine();
        var japaneseWords = DictionarySource.Load("japanese.txt", null).Where(w => w.Length >= 4).Distinct().ToList();
        foreach (var word in japaneseWords)
        {
            var result = TestSupport.Classify(scoreEngine, word);
            Score("自動切替: 日本語", result.Verdict != Verdict.English, $"{word} → {result.Verdict} ({result.Summary})");
        }
        foreach (var word in CommonEnglish)
        {
            var result = TestSupport.Classify(scoreEngine, word);
            Score("自動切替: 英語", result.Verdict != Verdict.Japanese, $"{word} → {result.Verdict} ({result.Summary})");
        }

        // 絵文字・顔文字の候補
        var candidates = CandidateDictionary.Load(null);
        foreach (var (reading, expected) in new[] { ("えがお", "😊"), ("かおもじ", "(^^)"), ("ねこ", "🐱"), ("わらい", "(笑)"), ("ありがとう", "🙏"), ("ばんざい", @"\(^o^)/"),
            ("かんがえるかお", "🤔"), ("かんがえる", "🤔"), ("にほん", "🇯🇵"), ("てへぺろ", "(・ω<)"), ("ぴえん", "🥺"), ("すし", "🍣"), ("はくしゅ", "👏"), ("ろけっと", "🚀"), ("おすし", "🍣"), ("ほのお", "🔥") })
        {
            var list = candidates.Lookup(reading);
            Score("絵文字", list.Contains(expected), $"{reading} → {string.Join(" ", list.Take(6))} (期待: {expected} を含む)");
        }

        // もしかして (書き間違い)
        var misspellings = MisspellingDictionary.Load(null);
        foreach (var (reading, expected) in new (string, string?)[]
        {
            ("ぶれすれっど", "ブレスレット"), ("しゅみれーしょん", "シミュレーション"), ("ばとみんとん", "バドミントン"), ("ふぃぎあすけーと", "フィギュア"),
            ("ぶれーすれっと", "ブレスレット"), ("こみにゅけーしょん", "コミュニケーション"),
            ("でばっく", "デバッグ"), ("でぃすくとっぷ", "デスクトップ"), ("すたんだーと", "スタンダード"), ("えくすぷれっそ", "エスプレッソ"), ("はいぶりっと", "ハイブリッド"),
            ("ぶれすれっと", null), ("でばっぐ", null), ("ぷろぐらみんぐ", null), ("きょうはいいてんき", null), ("ばっくをもつ", null), ("しみゅれーしょん", null), ("こーひーをのむ", null),
        })
        {
            var actual = misspellings.Find(reading)?.Right;
            Score("もしかして", actual == expected, $"{reading} → {actual ?? "(なし)"} (期待: {expected ?? "(なし)"})");
        }

        return new Result(categories, failures, cases);
    }

    public static void Print(Result result)
    {
        Console.WriteLine($"品質テスト: {result.Pass}/{result.Total} ({result.Rate:P1})");
        foreach (var (category, (pass, total)) in result.ByCategory)
        {
            Console.WriteLine($"  {category,-8} {pass,3}/{total,-3} {(double)pass / total,7:P0}");
        }
        if (result.Failures.Count > 0)
        {
            Console.WriteLine("外れた例:");
            foreach (var failure in result.Failures) Console.WriteLine("  " + failure);
        }
    }

    [Test]
    public static void Quality_Corpus()
    {
        var result = Run();
        Print(result);
        // 基準: 全体 95% 以上、どのカテゴリーも 80% 以上。
        Assert.True(result.Rate >= 0.95, $"全体の正解率 {result.Rate:P1} が基準 (95%) を下回った");
        foreach (var (category, (pass, total)) in result.ByCategory)
        {
            Assert.True((double)pass / total >= 0.8, $"{category} の正解率 {(double)pass / total:P0} が基準 (80%) を下回った");
        }
    }
}
