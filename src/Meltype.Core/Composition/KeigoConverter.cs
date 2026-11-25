// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 shiromenya

namespace Meltype.Composition;

/// <summary>
/// 敬語モード: ライブ変換・通常変換の日本語文節を、リアルタイムに丁寧語・謙譲語・ビジネス敬語へ変換する。
/// すでに敬語になっている文節は二重敬語にせず、変換前の候補も第 2 候補に残して Space で元に戻せるようにする。
/// </summary>
internal static class KeigoConverter
{
    // すでに尊敬語・謙譲語になっている文末 (TailTextSuffixes の二重適用を防ぐ)
    private static readonly string[] AlreadyKeigoEndings =
    [
        "いたします", "いたしました", "いたしません", "いたしかねます",
        "ございます", "ございました", "ございません",
        "おります", "おりました", "おりません",
        "存じます", "存じました", "存じ上げます",
        "申し上げます", "申し上げました",
        "いただきます", "いただきました", "いただけます", "いただけますでしょうか",
        "くださいます", "くださいました",
        "いらっしゃいます", "いらっしゃいました",
        "おっしゃいます", "おっしゃいました",
        "まいります", "まいりました", "参ります", "参りました",
        "伺います", "伺いました", "拝見します", "拝見しました",
        "ご覧ください", "ご教示ください", "ご確認ください", "お送りください", "ご連絡ください", "少々お待ちください",
    ];

    // すでに敬体・敬語で終わっている文末 (文末の「だ → です」や形容詞「です」付加を防ぐ)
    private static readonly string[] AlreadyPoliteEndings =
    [
        .. AlreadyKeigoEndings,
        "ください", "くださいませ",
        "よろしいでしょうか", "でしょうか", "でしょう",
        "ませんか", "ますか", "ましたか", "ですか", "でしたか",
        "ません", "ました", "ます", "でした", "です", "ましょう",
    ];

    // 文中のどこにあっても置き換える語彙 (読み → 敬語)
    private static readonly Dictionary<string, string> ReadingWords = new(StringComparer.Ordinal)
    {
        // 返事・承諾
        ["りょうかい"] = "承知いたしました",
        ["りょうかいです"] = "承知いたしました",
        ["りょ"] = "承知いたしました",
        ["わかった"] = "承知いたしました",
        ["わかりました"] = "承知いたしました",
        ["おっけー"] = "かしこまりました",
        ["おっけーです"] = "かしこまりました",
        ["いいよ"] = "差し支えございません",
        ["いいですよ"] = "差し支えございません",
        ["まかせて"] = "お任せください",
        ["まかせろ"] = "お任せください",
        // 感謝
        ["ありがとう"] = "ありがとうございます",
        ["ありがと"] = "ありがとうございます",
        ["さんきゅー"] = "ありがとうございます",
        ["あざす"] = "ありがとうございます",
        ["あざっす"] = "ありがとうございます",
        ["かんしゃ"] = "感謝申し上げます",
        ["たすかる"] = "大変助かります",
        ["たすかります"] = "大変助かります",
        ["たすかった"] = "大変助かりました",
        ["うれしい"] = "大変光栄に存じます",
        // 謝罪
        ["すみません"] = "申し訳ございません",
        ["すみませんでした"] = "申し訳ございませんでした",
        ["すいません"] = "申し訳ございません",
        ["すいませんでした"] = "申し訳ございませんでした",
        ["ごめん"] = "申し訳ございません",
        ["ごめんなさい"] = "申し訳ございません",
        ["もうしわけない"] = "申し訳ございません",
        ["しつれい"] = "失礼いたしました",
        ["おまたせ"] = "お待たせいたしました",
        ["おそくなった"] = "遅くなり申し訳ございません",
        ["おそくなってごめん"] = "遅くなり申し訳ございません",
        ["おくれてごめん"] = "遅れてしまい申し訳ございません",
        // あいさつ・結び
        ["おはよう"] = "おはようございます",
        ["おはよ"] = "おはようございます",
        ["よろしく"] = "よろしくお願いいたします",
        ["よろ"] = "よろしくお願いいたします",
        ["おねがい"] = "お願いいたします",
        ["おつかれ"] = "お疲れ様です",
        ["おつかれさま"] = "お疲れ様です",
        ["おつかれさまです"] = "お疲れ様です",
        ["おさきに"] = "お先に失礼いたします",
        ["ばいばい"] = "失礼いたします",
        ["じゃあね"] = "失礼いたします",
        ["またね"] = "またよろしくお願いいたします",
        // 肯定・否定・断り
        ["だいじょうぶ"] = "問題ございません",
        ["だいじょうぶです"] = "問題ございません",
        ["もんだいない"] = "問題ございません",
        ["もんだいないです"] = "問題ございません",
        ["へいき"] = "問題ございません",
        ["きにしないで"] = "お気になさらないでください",
        ["わすれてた"] = "失念しておりました",
        ["わすれた"] = "失念しておりました",
        ["まちがえた"] = "誤っておりました",
        ["まちがった"] = "誤っておりました",
        ["しらない"] = "わかりかねます",
        ["わからない"] = "わかりかねます",
        ["わかんない"] = "わかりかねます",
        ["むり"] = "いたしかねます",
        ["だめ"] = "いたしかねます",
        // 待ち・依頼
        ["まって"] = "少々お待ちください",
        ["まってて"] = "少々お待ちください",
        ["ちょっとまって"] = "少々お待ちください",
        ["てつだって"] = "お力添えいただけますでしょうか",
        // 時間・副詞・接続
        ["いまから"] = "これより",
        ["あとで"] = "後ほど",
        ["さっき"] = "先ほど",
        ["きょう"] = "本日",
        ["きのう"] = "昨日",
        ["こんど"] = "次回",
        ["すぐ"] = "ただちに",
        ["すぐに"] = "ただちに",
        ["もうすぐ"] = "まもなく",
        ["たぶん"] = "おそらく",
        ["めっちゃ"] = "大変",
        ["めちゃくちゃ"] = "大変",
        ["すごく"] = "大変",
        ["ちょっと"] = "少々",
        ["ほんとに"] = "誠に",
        ["ほんとうに"] = "誠に",
        ["まじで"] = "誠に",
        ["やっぱり"] = "やはり",
        ["やっぱ"] = "やはり",
        ["とりあえず"] = "取り急ぎ",
        ["ちなみに"] = "なお",
        ["だから"] = "そのため",
        ["じゃあ"] = "それでは",
        // 人称・指示語
        ["ぼく"] = "私",
        ["おれ"] = "私",
        ["ぼくら"] = "私ども",
        ["おれたち"] = "私ども",
        ["わたしたち"] = "私ども",
        ["こっち"] = "こちら",
        ["そっち"] = "そちら",
        ["あっち"] = "あちら",
        ["どっち"] = "どちら",
        ["だれ"] = "どなた",
        ["みんな"] = "皆様",
    };

    // 助詞付きまたは文頭接頭辞として置き換える読み (長い順)
    private static readonly (string ReadingPrefix, string KeigoPrefix, bool AllowsParticle)[] ReadingPrefixes =
    [
        ("めちゃくちゃ", "大変", false),
        ("わたしたち", "私ども", true),
        ("おれたち", "私ども", true),
        ("とりあえず", "取り急ぎ", false),
        ("ほんとうに", "誠に", false),
        ("ほんとに", "誠に", false),
        ("ちなみに", "なお", false),
        ("やっぱり", "やはり", false),
        ("もうすぐ", "まもなく", false),
        ("いまから", "これより", false),
        ("ちょっと", "少々", false),
        ("めっちゃ", "大変", false),
        ("すごく", "大変", false),
        ("すぐに", "ただちに", false),
        ("まじで", "誠に", false),
        ("たぶん", "おそらく", false),
        ("やっぱ", "やはり", false),
        ("だから", "そのため", false),
        ("ぼくら", "私ども", true),
        ("みんな", "皆様", true),
        ("きのう", "昨日", true),
        ("きょう", "本日", true),
        ("さっき", "先ほど", true),
        ("あとで", "後ほど", false),
        ("こんど", "次回", true),
        ("こっち", "こちら", true),
        ("そっち", "そちら", true),
        ("あっち", "あちら", true),
        ("どっち", "どちら", true),
        ("じゃあ", "それでは", false),
        ("ぼく", "私", true),
        ("おれ", "私", true),
        ("だれ", "どなた", true),
        ("すぐ", "ただちに", false),
    ];

    private static readonly string[] Particles =
        ["には", "では", "にも", "でも", "から", "まで", "より", "は", "が", "を", "に", "で", "と", "も", "の", "へ"];

    // 目上・相手が主語のときの尊敬語マップ (読み完全一致)
    private static readonly Dictionary<string, string> RespectfulReadingMap = new(StringComparer.Ordinal)
    {
        ["いく"] = "いらっしゃいます",
        ["いきます"] = "いらっしゃいます",
        ["いきました"] = "いらっしゃいました",
        ["くる"] = "お越しになります",
        ["きます"] = "お越しになります",
        ["きた"] = "お越しになりました",
        ["きました"] = "お越しになりました",
        ["いる"] = "いらっしゃいます",
        ["います"] = "いらっしゃいます",
        ["いた"] = "いらっしゃいました",
        ["いました"] = "いらっしゃいました",
        ["みる"] = "ご覧になります",
        ["みます"] = "ご覧になります",
        ["みた"] = "ご覧になりました",
        ["みました"] = "ご覧になりました",
        ["いう"] = "おっしゃいます",
        ["いいます"] = "おっしゃいます",
        ["いいました"] = "おっしゃいました",
        ["たべる"] = "召し上がります",
        ["たべます"] = "召し上がります",
        ["たべた"] = "召し上がりました",
        ["たべました"] = "召し上がりました",
        ["のむ"] = "召し上がります",
        ["のみます"] = "召し上がります",
        ["のんだ"] = "召し上がりました",
        ["のみました"] = "召し上がりました",
        ["しる"] = "ご存じです",
        ["しってる"] = "ご存じです",
        ["しっている"] = "ご存じです",
        ["しっています"] = "ご存じです",
        ["おもう"] = "お思いになります",
        ["おもいます"] = "お思いになります",
        ["おもった"] = "お思いになりました",
        ["おもいました"] = "お思いになりました",
        ["する"] = "なさいます",
        ["します"] = "なさいます",
        ["した"] = "なさいました",
        ["しました"] = "なさいました",
        ["くれる"] = "くださいます",
        ["くれます"] = "くださいます",
        ["くれた"] = "くださいました",
        ["くれました"] = "くださいました",
    };

    // 目上・相手が主語のときの尊敬語語尾 (テキスト末尾)
    private static readonly (string Suffix, string Replacement)[] RespectfulTextSuffixes =
    [
        ("行きます", "いらっしゃいます"),
        ("行く", "いらっしゃいます"),
        ("行きました", "いらっしゃいました"),
        ("行った", "いらっしゃいました"),
        ("来ます", "お越しになります"),
        ("来る", "お越しになります"),
        ("来ました", "お越しになりました"),
        ("来た", "お越しになりました"),
        ("見ます", "ご覧になります"),
        ("見る", "ご覧になります"),
        ("見ました", "ご覧になりました"),
        ("見た", "ご覧になりました"),
        ("言います", "おっしゃいます"),
        ("言う", "おっしゃいます"),
        ("言いました", "おっしゃいました"),
        ("言った", "おっしゃいました"),
        ("食べます", "召し上がります"),
        ("食べる", "召し上がります"),
        ("食べました", "召し上がりました"),
        ("食べた", "召し上がりました"),
        ("飲みます", "召し上がります"),
        ("飲む", "召し上がります"),
        ("飲みました", "召し上がりました"),
        ("飲んだ", "召し上がりました"),
        ("知っています", "ご存じです"),
        ("知っている", "ご存じです"),
        ("知ってる", "ご存じです"),
        ("思います", "お思いになります"),
        ("思う", "お思いになります"),
        ("思いました", "お思いになりました"),
        ("思った", "お思いになりました"),
        ("しています", "なさっています"),
        ("している", "なさっています"),
        ("してる", "なさっています"),
        ("します", "なさいます"),
        ("する", "なさいます"),
        ("しました", "なさいました"),
        ("した", "なさいました"),
        ("くれます", "くださいます"),
        ("くれる", "くださいます"),
        ("くれました", "くださいました"),
        ("くれた", "くださいました"),
        ("います", "いらっしゃいます"),
        ("いました", "いらっしゃいました"),
    ];

    private static readonly string[] OtherSubjectMarkers =
        ["お客様", "皆様", "皆さん", "先方", "御社", "貴社", "先生", "社長", "部長", "課長", "係長", "専務", "常務", "取締役", "担当者", "相手", "そちら", "あなた", "様", "さん"];

    private static readonly string[] SelfSubjectMarkers =
        ["私ども", "弊社", "当社", "こちら", "私", "僕", "俺", "自分"];

    // 疑問文 (？ / ?) のときの読み完全一致
    private static readonly Dictionary<string, string> QuestionReadingMap = new(StringComparer.Ordinal)
    {
        ["だいじょうぶ"] = "問題ございませんでしょうか",
        ["だいじょうぶです"] = "問題ございませんでしょうか",
        ["もんだいない"] = "問題ございませんでしょうか",
        ["もんだいないです"] = "問題ございませんでしょうか",
        ["いい"] = "よろしいでしょうか",
        ["いいです"] = "よろしいでしょうか",
        ["できる"] = "可能でしょうか",
        ["できます"] = "可能でしょうか",
        ["ある"] = "ございますでしょうか",
        ["あります"] = "ございますでしょうか",
        ["ない"] = "ございませんでしょうか",
        ["ありません"] = "ございませんでしょうか",
        ["みた"] = "ご覧になりましたでしょうか",
        ["みました"] = "ご覧になりましたでしょうか",
        ["みる"] = "ご覧になりますでしょうか",
        ["みます"] = "ご覧になりますでしょうか",
        ["いった"] = "行かれましたでしょうか",
        ["いきました"] = "行かれましたでしょうか",
        ["いく"] = "行かれますでしょうか",
        ["いきます"] = "行かれますでしょうか",
        ["きた"] = "お越しになりましたでしょうか",
        ["きました"] = "お越しになりましたでしょうか",
        ["くる"] = "お越しになりますでしょうか",
        ["きます"] = "お越しになりますでしょうか",
        ["おくった"] = "お送りいただきましたでしょうか",
        ["おくりました"] = "お送りいただきましたでしょうか",
        ["しってる"] = "ご存じでしょうか",
        ["しっている"] = "ご存じでしょうか",
        ["しっています"] = "ご存じでしょうか",
        ["わかった"] = "ご理解いただけましたでしょうか",
        ["わかりました"] = "ご理解いただけましたでしょうか",
        ["おわった"] = "完了されましたでしょうか",
        ["おわりました"] = "完了されましたでしょうか",
        ["かくにんした"] = "ご確認されましたでしょうか",
        ["かくにんしました"] = "ご確認されましたでしょうか",
    };

    // 疑問文 (？ / ?) のときの文末変換 (相手への質問なので謙譲語にせず尊敬・丁寧形にする)
    private static readonly (string Suffix, string Replacement)[] QuestionSuffixes =
    [
        ("問題ございません", "問題ございませんでしょうか"),
        ("確認しました", "ご確認されましたでしょうか"),
        ("確認した", "ご確認されましたでしょうか"),
        ("見ました", "ご覧になりましたでしょうか"),
        ("見た", "ご覧になりましたでしょうか"),
        ("見ます", "ご覧になりますでしょうか"),
        ("見る", "ご覧になりますでしょうか"),
        ("行きました", "行かれましたでしょうか"),
        ("行った", "行かれましたでしょうか"),
        ("行きます", "行かれますでしょうか"),
        ("行く", "行かれますでしょうか"),
        ("来ました", "お越しになりましたでしょうか"),
        ("来た", "お越しになりましたでしょうか"),
        ("来ます", "お越しになりますでしょうか"),
        ("来る", "お越しになりますでしょうか"),
        ("送りました", "お送りいただきましたでしょうか"),
        ("送った", "お送りいただきましたでしょうか"),
        ("知っています", "ご存じでしょうか"),
        ("知っている", "ご存じでしょうか"),
        ("知ってる", "ご存じでしょうか"),
        ("終わりました", "完了されましたでしょうか"),
        ("終わった", "完了されましたでしょうか"),
        ("できます", "可能でしょうか"),
        ("できる", "可能でしょうか"),
        ("いいです", "よろしいでしょうか"),
        ("いい", "よろしいでしょうか"),
        ("あります", "ございますでしょうか"),
        ("ある", "ございますでしょうか"),
        ("ありません", "ございませんでしょうか"),
        ("ない", "ございませんでしょうか"),
        ("しました", "されましたでしょうか"),
        ("した", "されましたでしょうか"),
        ("します", "されますでしょうか"),
        ("する", "されますでしょうか"),
        ("だった", "でしたでしょうか"),
        ("でした", "でしたでしょうか"),
        ("です", "でしょうか"),
        ("だ", "でしょうか"),
    ];

    // 文末の動詞・表現の置き換え (読み → 敬語)
    private static readonly Dictionary<string, string> TailReadingMap = new(StringComparer.Ordinal)
    {
        // 移動・面会
        ["いく"] = "伺います",
        ["いきます"] = "伺います",
        ["いった"] = "伺いました",
        ["いきました"] = "伺いました",
        ["いきたい"] = "伺いたく存じます",
        ["くる"] = "参ります",
        ["きます"] = "参ります",
        ["きた"] = "参りました",
        ["きました"] = "参りました",
        ["あう"] = "お目にかかります",
        ["あいます"] = "お目にかかります",
        ["あった"] = "お目にかかりました",
        ["あいたい"] = "お目にかかりたく存じます",
        // 見る・読む・確認
        ["みる"] = "拝見いたします",
        ["みます"] = "拝見いたします",
        ["みた"] = "拝見いたしました",
        ["みました"] = "拝見いたしました",
        ["みて"] = "ご覧ください",
        ["みてほしい"] = "ご覧いただけますでしょうか",
        ["よむ"] = "拝読いたします",
        ["よみます"] = "拝読いたします",
        ["よんだ"] = "拝読いたしました",
        ["よみました"] = "拝読いたしました",
        ["かくにんする"] = "確認いたします",
        ["かくにんした"] = "確認いたしました",
        ["かくにんして"] = "ご確認ください",
        ["かくにんしてほしい"] = "ご確認いただけますでしょうか",
        // 聞く・話す・伝える・教える
        ["きく"] = "伺います",
        ["ききます"] = "伺います",
        ["きいた"] = "伺いました",
        ["ききました"] = "伺いました",
        ["いう"] = "申し上げます",
        ["いいます"] = "申し上げます",
        ["はなす"] = "お話しいたします",
        ["はなします"] = "お話しいたします",
        ["はなした"] = "お話しいたしました",
        ["つたえる"] = "お伝えいたします",
        ["つたえた"] = "お伝えいたしました",
        ["おしえる"] = "お知らせいたします",
        ["おしえた"] = "お知らせいたしました",
        ["おしえて"] = "ご教示ください",
        ["おしえてほしい"] = "ご教示いただけますでしょうか",
        // 知る・思う・考える・調べる
        ["しる"] = "存じます",
        ["しってる"] = "存じております",
        ["しっている"] = "存じております",
        ["しっています"] = "存じております",
        ["おもう"] = "存じます",
        ["おもいます"] = "存じます",
        ["おもった"] = "存じました",
        ["おもいました"] = "存じました",
        ["かんがえる"] = "検討いたします",
        ["かんがえた"] = "検討いたしました",
        ["しらべる"] = "調査いたします",
        ["しらべた"] = "調査いたしました",
        // 送る・渡す・受け取る・借りる・返す
        ["おくる"] = "お送りいたします",
        ["おくります"] = "お送りいたします",
        ["おくった"] = "お送りいたしました",
        ["おくりました"] = "お送りいたしました",
        ["おくって"] = "お送りください",
        ["おくってほしい"] = "お送りいただけますでしょうか",
        ["わたす"] = "お渡しいたします",
        ["わたします"] = "お渡しいたします",
        ["わたした"] = "お渡しいたしました",
        ["もらう"] = "頂戴いたします",
        ["もらいます"] = "頂戴いたします",
        ["もらった"] = "頂戴いたしました",
        ["もらいました"] = "頂戴いたしました",
        ["かりる"] = "拝借いたします",
        ["かります"] = "拝借いたします",
        ["かりた"] = "拝借いたしました",
        ["かりました"] = "拝借いたしました",
        ["かえす"] = "ご返却いたします",
        ["かえします"] = "ご返却いたします",
        ["かえした"] = "ご返却いたしました",
        ["かえしました"] = "ご返却いたしました",
        ["へんしんする"] = "ご返信いたします",
        ["へんしんします"] = "ご返信いたします",
        ["へんしんした"] = "ご返信いたしました",
        ["へんしんしました"] = "ご返信いたしました",
        // 業務動詞・一般動詞
        ["れんらくする"] = "ご連絡いたします",
        ["れんらくします"] = "ご連絡いたします",
        ["れんらくした"] = "ご連絡いたしました",
        ["れんらくしました"] = "ご連絡いたしました",
        ["れんらくして"] = "ご連絡ください",
        ["ほうこくする"] = "ご報告いたします",
        ["ほうこくします"] = "ご報告いたします",
        ["ほうこくした"] = "ご報告いたしました",
        ["ほうこくしました"] = "ご報告いたしました",
        ["そうだんする"] = "ご相談いたします",
        ["そうだんします"] = "ご相談いたします",
        ["そうだんしたい"] = "ご相談したく存じます",
        ["そうだんしたいです"] = "ご相談したく存じます",
        ["きょうゆうする"] = "ご共有いたします",
        ["きょうゆうします"] = "ご共有いたします",
        ["きょうゆうした"] = "ご共有いたしました",
        ["きょうゆうしました"] = "ご共有いたしました",
        ["しゅうせいする"] = "修正いたします",
        ["しゅうせいします"] = "修正いたします",
        ["しゅうせいした"] = "修正いたしました",
        ["しゅうせいしました"] = "修正いたしました",
        ["たいおうする"] = "対応いたします",
        ["たいおうします"] = "対応いたします",
        ["たいおうした"] = "対応いたしました",
        ["たいおうしました"] = "対応いたしました",
        ["さくせいする"] = "作成いたします",
        ["さくせいします"] = "作成いたします",
        ["さくせいした"] = "作成いたしました",
        ["さくせいしました"] = "作成いたしました",
        ["じゅんびする"] = "準備いたします",
        ["じゅんびします"] = "準備いたします",
        ["じゅんびした"] = "準備いたしました",
        ["じゅんびしました"] = "準備いたしました",
        ["ちょうせいする"] = "調整いたします",
        ["ちょうせいします"] = "調整いたします",
        ["ちょうせいした"] = "調整いたしました",
        ["ちょうせいしました"] = "調整いたしました",
        ["さんかする"] = "参加いたします",
        ["さんかします"] = "参加いたします",
        ["さんかした"] = "参加いたしました",
        ["さんかしました"] = "参加いたしました",
        ["てつだう"] = "お手伝いいたします",
        ["てつだいます"] = "お手伝いいたします",
        ["まつ"] = "お待ちいたします",
        ["まちます"] = "お待ちいたします",
        ["まってる"] = "お待ちしております",
        ["まっている"] = "お待ちしております",
        ["まっています"] = "お待ちしております",
        ["たべる"] = "いただきます",
        ["たべます"] = "いただきます",
        ["たべた"] = "いただきました",
        ["たべました"] = "いただきました",
        ["のむ"] = "いただきます",
        ["のみます"] = "いただきます",
        ["のんだ"] = "いただきました",
        ["のみました"] = "いただきました",
        ["やすむ"] = "お休みをいただきます",
        ["やすみます"] = "お休みをいただきます",
        ["やすんだ"] = "お休みをいただきました",
        ["やすみました"] = "お休みをいただきました",
        ["おわる"] = "完了いたします",
        ["おわります"] = "完了いたします",
        ["おわった"] = "完了いたしました",
        ["おわりました"] = "完了いたしました",
        ["はじめる"] = "開始いたします",
        ["はじめます"] = "開始いたします",
        ["はじめた"] = "開始いたしました",
        ["はじめました"] = "開始いたしました",
        ["なおす"] = "修正いたします",
        ["なおします"] = "修正いたします",
        ["なおした"] = "修正いたしました",
        ["なおしました"] = "修正いたしました",
        ["つくる"] = "作成いたします",
        ["つくります"] = "作成いたします",
        ["つくった"] = "作成いたしました",
        ["つくりました"] = "作成いたしました",
        ["だす"] = "提出いたします",
        ["だします"] = "提出いたします",
        ["だした"] = "提出いたしました",
        ["だしました"] = "提出いたしました",
        ["かく"] = "書きます",
        ["かいた"] = "書きました",
        ["つかう"] = "使用いたします",
        ["つかいます"] = "使用いたします",
        ["つかった"] = "使用いたしました",
        ["つかいました"] = "使用いたしました",
        ["かう"] = "購入いたします",
        ["かいます"] = "購入いたします",
        ["かった"] = "購入いたしました",
        ["かいました"] = "購入いたしました",
        ["とどく"] = "届きます",
        ["とどいた"] = "届きました",
        ["きめる"] = "決定いたします",
        ["きめます"] = "決定いたします",
        ["きめた"] = "決定いたしました",
        ["きめました"] = "決定いたしました",
        ["かえる"] = "変更いたします",
        ["かえます"] = "変更いたします",
        ["かえた"] = "変更いたしました",
        ["かえました"] = "変更いたしました",
        ["すすむ"] = "進みます",
        ["すすんだ"] = "進みました",
        ["すすめる"] = "進めます",
        ["すすめた"] = "進めました",
        ["えらぶ"] = "選びます",
        ["えらんだ"] = "選びました",
        ["まとめる"] = "取りまとめます",
        ["まとめた"] = "取りまとめました",
        // 状態・助動詞
        ["できる"] = "可能でございます",
        ["できます"] = "可能でございます",
        ["できた"] = "完了いたしました",
        ["できました"] = "完了いたしました",
        ["できない"] = "いたしかねます",
        ["できません"] = "いたしかねます",
        ["ある"] = "ございます",
        ["あります"] = "ございます",
        ["ない"] = "ございません",
        ["ありません"] = "ございません",
        ["する"] = "いたします",
        ["します"] = "いたします",
        ["した"] = "いたしました",
        ["しました"] = "いたしました",
        ["しない"] = "いたしません",
        ["しません"] = "いたしません",
        ["してる"] = "しております",
        ["している"] = "しております",
        ["しています"] = "しております",
        ["してた"] = "しておりました",
        ["していた"] = "しておりました",
        ["していました"] = "しておりました",
        ["したい"] = "したく存じます",
        ["したいです"] = "したく存じます",
        ["してほしい"] = "していただけますでしょうか",
        ["してほしいです"] = "していただけますでしょうか",
        ["わかりません"] = "わかりかねます",
        ["だ"] = "です",
        ["だよ"] = "ですよ",
        ["だね"] = "ですね",
        ["だな"] = "ですね",
        ["だった"] = "でした",
        ["かも"] = "かもしれません",
        ["かもしれない"] = "かもしれません",
        ["かな"] = "でしょうか",
        ["だろう"] = "でしょう",
        ["でしょ"] = "でしょう",
    };

    // 変換後テキスト内の語彙置き換え (Zenzai などで 1 文節にまとまった文にも対応)
    private static readonly (string From, string To)[] InTextReplacements =
    [
        ("問題ないですか", "問題ございませんでしょうか"),
        ("大丈夫ですか", "問題ございませんでしょうか"),
        ("問題ないです", "問題ございません"),
        ("大丈夫です", "問題ございません"),
        ("問題ない", "問題ございません"),
        ("大丈夫", "問題ございません"),
        ("今日は", "本日は"),
        ("今日の", "本日の"),
        ("今日中", "本日中"),
        ("今日から", "本日から"),
        ("今日まで", "本日まで"),
        ("今日も", "本日も"),
        ("さっき", "先ほど"),
        ("後で", "後ほど"),
        ("あとで", "後ほど"),
        ("今から", "これより"),
        ("いまから", "これより"),
        ("もうすぐ", "まもなく"),
        ("めちゃくちゃ", "大変"),
        ("めっちゃ", "大変"),
        ("すごく", "大変"),
        ("ちょっと", "少々"),
        ("本当に", "誠に"),
        ("ほんとに", "誠に"),
        ("マジで", "誠に"),
        ("まじで", "誠に"),
        ("とりあえず", "取り急ぎ"),
    ];

    // 文末の変換後テキストの語尾ルール (長いものから順に照合)
    private static readonly (string Suffix, string Replacement)[] TailTextSuffixes =
    [
        // 丁寧な依頼・定型句 (「〜てください」がすでに付いている形も含む)
        ("確認してください", "ご確認ください"),
        ("確認してほしい", "ご確認いただけますでしょうか"),
        ("確認して欲しい", "ご確認いただけますでしょうか"),
        ("確認して", "ご確認ください"),
        ("確認する", "確認いたします"),
        ("確認します", "確認いたします"),
        ("確認した", "確認いたしました"),
        ("確認しました", "確認いたしました"),
        ("教えてください", "ご教示ください"),
        ("教えてほしい", "ご教示いただけますでしょうか"),
        ("教えて欲しい", "ご教示いただけますでしょうか"),
        ("教えて", "ご教示ください"),
        ("連絡してください", "ご連絡ください"),
        ("連絡する", "ご連絡いたします"),
        ("連絡します", "ご連絡いたします"),
        ("連絡した", "ご連絡いたしました"),
        ("連絡しました", "ご連絡いたしました"),
        ("連絡して", "ご連絡ください"),
        ("報告します", "ご報告いたします"),
        ("報告する", "ご報告いたします"),
        ("報告しました", "ご報告いたしました"),
        ("報告した", "ご報告いたしました"),
        ("相談したいです", "ご相談したく存じます"),
        ("相談したい", "ご相談したく存じます"),
        ("相談します", "ご相談いたします"),
        ("相談する", "ご相談いたします"),
        ("共有します", "ご共有いたします"),
        ("共有する", "ご共有いたします"),
        ("共有しました", "ご共有いたしました"),
        ("共有した", "ご共有いたしました"),
        ("返信します", "ご返信いたします"),
        ("返信する", "ご返信いたします"),
        ("返信しました", "ご返信いたしました"),
        ("返信した", "ご返信いたしました"),
        ("送ってください", "お送りください"),
        ("送ってほしい", "お送りいただけますでしょうか"),
        ("送って欲しい", "お送りいただけますでしょうか"),
        ("送って", "お送りください"),
        ("送る", "お送りいたします"),
        ("送ります", "お送りいたします"),
        ("送った", "お送りいたしました"),
        ("送りました", "お送りいたしました"),
        ("見てください", "ご覧ください"),
        ("見てほしい", "ご覧いただけますでしょうか"),
        ("見て欲しい", "ご覧いただけますでしょうか"),
        ("見て", "ご覧ください"),
        ("見る", "拝見いたします"),
        ("見ます", "拝見いたします"),
        ("見た", "拝見いたしました"),
        ("見ました", "拝見いたしました"),
        ("待ってください", "少々お待ちください"),
        ("読む", "拝読いたします"),
        ("読んだ", "拝読いたしました"),
        ("行きたい", "伺いたく存じます"),
        ("行く", "伺います"),
        ("行きます", "伺います"),
        ("行った", "伺いました"),
        ("行きました", "伺いました"),
        ("来る", "参ります"),
        ("来ます", "参ります"),
        ("来た", "参りました"),
        ("来ました", "参りました"),
        ("会いたい", "お目にかかりたく存じます"),
        ("会う", "お目にかかります"),
        ("会った", "お目にかかりました"),
        ("聞く", "伺います"),
        ("聞いた", "伺いました"),
        ("言う", "申し上げます"),
        ("言った", "申し上げました"),
        ("伝える", "お伝えいたします"),
        ("伝えた", "お伝えいたしました"),
        ("知っている", "存じております"),
        ("知ってる", "存じております"),
        ("と思います", "と存じます"),
        ("と思う", "と存じます"),
        ("と思いました", "と存じました"),
        ("と思った", "と存じました"),
        ("思う", "存じます"),
        ("思います", "存じます"),
        ("思った", "存じました"),
        ("思いました", "存じました"),
        ("考えます", "検討いたします"),
        ("考える", "検討いたします"),
        ("考えました", "検討いたしました"),
        ("考えた", "検討いたしました"),
        ("調べます", "調査いたします"),
        ("調べる", "調査いたします"),
        ("調べました", "調査いたしました"),
        ("調べた", "調査いたしました"),
        ("貰います", "頂戴いたします"),
        ("もらいます", "頂戴いたします"),
        ("貰う", "頂戴いたします"),
        ("もらう", "頂戴いたします"),
        ("貰いました", "頂戴いたしました"),
        ("もらいました", "頂戴いたしました"),
        ("貰った", "頂戴いたしました"),
        ("もらった", "頂戴いたしました"),
        ("借ります", "拝借いたします"),
        ("借りる", "拝借いたします"),
        ("借りました", "拝借いたしました"),
        ("借りた", "拝借いたしました"),
        ("渡します", "お渡しいたします"),
        ("渡す", "お渡しいたします"),
        ("渡しました", "お渡しいたしました"),
        ("渡した", "お渡しいたしました"),
        ("食べます", "いただきます"),
        ("食べる", "いただきます"),
        ("食べました", "いただきました"),
        ("食べた", "いただきました"),
        ("飲みます", "いただきます"),
        ("飲む", "いただきます"),
        ("飲みました", "いただきました"),
        ("飲んだ", "いただきました"),
        ("終わります", "完了いたします"),
        ("終わる", "完了いたします"),
        ("終わりました", "完了いたしました"),
        ("終わった", "完了いたしました"),
        ("直します", "修正いたします"),
        ("直す", "修正いたします"),
        ("直しました", "修正いたしました"),
        ("直した", "修正いたしました"),
        ("作ります", "作成いたします"),
        ("作る", "作成いたします"),
        ("作りました", "作成いたしました"),
        ("作った", "作成いたしました"),
        ("使います", "使用いたします"),
        ("使う", "使用いたします"),
        ("使いました", "使用いたしました"),
        ("使った", "使用いたしました"),
        ("買います", "購入いたします"),
        ("買う", "購入いたします"),
        ("買いました", "購入いたしました"),
        ("買った", "購入いたしました"),
        ("決めます", "決定いたします"),
        ("決める", "決定いたします"),
        ("決めました", "決定いたしました"),
        ("決めた", "決定いたしました"),
        ("変えます", "変更いたします"),
        ("変える", "変更いたします"),
        ("変えました", "変更いたしました"),
        ("変えた", "変更いたしました"),
        ("始めます", "開始いたします"),
        ("始める", "開始いたします"),
        ("始めました", "開始いたしました"),
        ("始めた", "開始いたしました"),
        ("続けます", "継続いたします"),
        ("続ける", "継続いたします"),
        ("続けました", "継続いたしました"),
        ("続けた", "継続いたしました"),
        ("止めます", "停止いたします"),
        ("止める", "停止いたします"),
        ("止めました", "停止いたしました"),
        ("止めた", "停止いたしました"),
        ("消します", "削除いたします"),
        ("消す", "削除いたします"),
        ("消しました", "削除いたしました"),
        ("消した", "削除いたしました"),
        ("探します", "お探しいたします"),
        ("探す", "お探しいたします"),
        ("探しました", "お探しいたしました"),
        ("探した", "お探しいたしました"),
        ("しています", "しております"),
        ("している", "しております"),
        ("してる", "しております"),
        ("していました", "しておりました"),
        ("していた", "しておりました"),
        ("してた", "しておりました"),
        ("っています", "っております"),
        ("っている", "っております"),
        ("ってる", "っております"),
        ("んでいます", "んでおります"),
        ("んでいる", "んでおります"),
        ("んでる", "んでおります"),
        ("ています", "ております"),
        ("ている", "ております"),
        ("てる", "ております"),
        ("でいます", "でおります"),
        ("でいる", "でおります"),
        ("でる", "でおります"),
        ("ていました", "ておりました"),
        ("ていた", "ておりました"),
        ("てた", "ておりました"),
        ("ではありません", "ではございません"),
        ("じゃありません", "ではございません"),
        ("ではない", "ではございません"),
        ("じゃない", "ではございません"),
        ("したいです", "したく存じます"),
        ("したい", "したく存じます"),
        ("しません", "いたしません"),
        ("しない", "いたしません"),
        ("する", "いたします"),
        ("します", "いたします"),
        ("した", "いたしました"),
        ("しました", "いたしました"),
        ("だった", "でした"),
        ("だろう", "でしょう"),
        ("でしょ", "でしょう"),
        ("かな", "でしょうか"),
        ("だよ", "ですよ"),
        ("だね", "ですね"),
    ];

    // 文末に「です」を付けられる代表的な い形容詞の語尾 (漢字 + い / かった)
    private static readonly string[] PoliteAdjectives =
    [
        "難しい", "忙しい", "早い", "遅い", "多い", "少ない", "新しい", "古い",
        "高い", "低い", "大きい", "小さい", "長い", "短い", "近い", "遠い",
        "強い", "弱い", "重い", "軽い", "良い", "悪い", "厳しい", "詳しい", "正しい", "ありがたい",
    ];

    public static void Apply(List<Clause> clauses, string? before = null)
    {
        if (clauses.Count == 0) return;

        // 全文節を連結した読みが 1 つの定型句に一致する場合 (ちょっと + まって → 少々お待ちください など)
        if (clauses.Count > 1)
        {
            var fullReading = string.Concat(clauses.Select(c => c.Reading));
            var (coreFull, trailFull) = SplitTrailingPunctuation(fullReading);
            if (!IsQuestion(trailFull) && (ReadingWords.TryGetValue(coreFull, out var fullWord) || TailReadingMap.TryGetValue(coreFull, out fullWord)))
            {
                var original = string.Concat(clauses.Select(c => c.Text));
                var keigo = fullWord + trailFull;
                var merged = new Clause(fullReading, false, keigo == original ? [keigo] : [keigo, original]) { Engine = original };
                merged.NotLearned.Add(keigo);
                clauses.Clear();
                clauses.Add(merged);
                return;
            }
        }

        for (var i = 0; i < clauses.Count; i++)
        {
            if (IsPunctuationOnly(clauses[i].Text) && IsPunctuationOnly(clauses[i].Reading)) continue;

            var hasClauseEnd = HasClauseEnd(clauses[i].Text) || HasClauseEnd(clauses[i].Reading);
            var nextIsPunct = i + 1 < clauses.Count && IsPunctuationOnly(clauses[i + 1].Text);
            var hasNextContent = clauses.Skip(i + 1).Any(c => !IsPunctuationOnly(c.Text));
            var isSentenceEnd = !hasNextContent || hasClauseEnd || nextIsPunct;
            var nextIsQuestion = i + 1 < clauses.Count && IsPunctuationOnly(clauses[i + 1].Text) && IsQuestion(clauses[i + 1].Text);

            var prevReading = i > 0 ? clauses[i - 1].Reading : "";
            var nextReading = i + 1 < clauses.Count ? clauses[i + 1].Reading : "";
            var nextText = i + 1 < clauses.Count ? clauses[i + 1].Text : "";

            // 次の文節と合わせてすでに敬語になっている場合は二重敬語を防ぐ
            if (!nextIsPunct && IsAlreadyFollowedByPolite(clauses[i].Reading, nextReading, nextText)) continue;

            var contextText = (before ?? "") + string.Concat(clauses.Take(i + 1).Select(c => c.Text));
            var respectfulSubject = IsOtherSubject(contextText);

            if (ConvertClause(clauses[i].Reading, clauses[i].Text, isSentenceEnd, nextIsQuestion, respectfulSubject, prevReading, hasNextContent) is { } converted && converted != clauses[i].Text)
            {
                SetKeigo(clauses[i], converted);
            }
        }
    }

    /// <summary>直近の主語手がかりから、相手・目上の人の動作 (尊敬語) か、自分の動作 (謙譲語) かを判別する。</summary>
    private static bool IsOtherSubject(string text)
    {
        var lastOther = LastSubjectIndex(text, OtherSubjectMarkers);
        var lastSelf = LastSubjectIndex(text, SelfSubjectMarkers);
        return lastOther > lastSelf;
    }

    private static int LastSubjectIndex(string text, string[] markers)
    {
        var best = -1;
        foreach (var marker in markers)
        {
            foreach (var particle in (string[])["は", "が", "も"])
            {
                var idx = text.LastIndexOf(marker + particle, StringComparison.Ordinal);
                if (idx > best) best = idx;
            }
        }
        return best;
    }

    private static bool IsAlreadyFollowedByPolite(string reading, string nextReading, string nextText)
    {
        if (nextReading.Length == 0) return false;
        // ありがとう + ございます / よろしく + お願いします / お疲れ + 様です / 大丈夫 + ですか
        if (reading is "ありがとう" or "ありがと" && nextReading.StartsWith("ござい", StringComparison.Ordinal)) return true;
        if (reading is "よろしく" or "おねがい" && (nextReading.StartsWith("おねがい", StringComparison.Ordinal) || nextReading.StartsWith("いたし", StringComparison.Ordinal))) return true;
        if (reading is "おつかれ" or "おつかれさま" && (nextReading.StartsWith("さま", StringComparison.Ordinal) || nextReading.StartsWith("です", StringComparison.Ordinal))) return true;
        if (reading is "だいじょうぶ" or "もんだいない" && (nextReading.StartsWith("です", StringComparison.Ordinal) || nextText.StartsWith("です", StringComparison.Ordinal))) return true;
        return false;
    }

    private static string? ConvertClause(string reading, string text, bool isSentenceEnd, bool nextIsQuestion, bool respectfulSubject, string prevReading, bool hasNextContent)
    {
        var (readingCore, readingPunct) = SplitTrailingPunctuation(reading);
        var (textCore, textPunct) = SplitTrailingPunctuation(text);
        var punct = textPunct.Length > 0 ? textPunct : readingPunct;
        if (readingCore.Length == 0) return null;

        var prevEndsWithTe = prevReading.EndsWith('て') || prevReading.EndsWith('で');
        var isQuestion = IsQuestion(punct) || nextIsQuestion;

        // 0. 疑問文 (？ / ?) の文末は相手への質問として尊敬・丁寧形にする
        if (isSentenceEnd && isQuestion)
            return ConvertQuestion(readingCore, textCore, punct);

        // 0.5 相手・目上が主語の文末は尊敬語を優先する (社長が見る → ご覧になります、お客様が来る → お越しになります)
        if (isSentenceEnd && respectfulSubject && !IsAlreadyKeigo(textCore))
        {
            if (RespectfulReadingMap.TryGetValue(readingCore, out var respWord))
                return respWord + punct;
            var respText = ApplyInTextReplacements(textCore);
            foreach (var (suffix, replacement) in RespectfulTextSuffixes)
            {
                if (respText.EndsWith(suffix, StringComparison.Ordinal))
                    return respText[..^suffix.Length] + replacement + punct;
            }
        }

        // 1. 前の文節が「〜て / 〜で」で、この文節が補助動詞のとき (確認して + ほしい → いただけますでしょうか)
        if (isSentenceEnd && prevEndsWithTe)
        {
            if (readingCore is "ほしい" or "くれ" or "ちょうだい") return "いただけますでしょうか" + punct;
            if (readingCore is "いる" or "る") return "おります" + punct;
            if (readingCore is "いた" or "た") return "おりました" + punct;
            if (readingCore is "もらえる") return "いただけますでしょうか" + punct;
            if (readingCore is "もらった") return "いただきました" + punct;
            if (readingCore is "もらう") return "いただきます" + punct;
            if (readingCore is "くれた") return "くださいました" + punct;
            if (readingCore is "くれる") return "くださいます" + punct;
            if (readingCore is "いい") return "よろしいでしょうか" + punct;
        }

        // 2. 単語・定型句の完全一致
        if (ReadingWords.TryGetValue(readingCore, out var word))
            return word + punct;
        if (isSentenceEnd && TailReadingMap.TryGetValue(readingCore, out var tailWord))
            return tailWord + punct;

        // 3. 語頭接頭辞 (+ 助詞) の一致と、残りの部分の再帰変換 (きょうはいく → 本日は + 伺います)
        foreach (var (readingPrefix, keigoPrefix, allowsParticle) in ReadingPrefixes)
        {
            if (!readingCore.StartsWith(readingPrefix, StringComparison.Ordinal)) continue;
            var afterPrefix = readingCore[readingPrefix.Length..];
            if (afterPrefix.Length == 0) return keigoPrefix + punct;

            if (allowsParticle)
            {
                foreach (var particle in Particles)
                {
                    if (!afterPrefix.StartsWith(particle, StringComparison.Ordinal)) continue;
                    var tailReading = afterPrefix[particle.Length..];
                    if (tailReading.Length == 0) return keigoPrefix + particle + punct;
                    if (ConvertReadingRemainder(tailReading, isSentenceEnd) is { } convertedTail)
                        return keigoPrefix + particle + convertedTail + punct;
                }
            }

            if (ConvertReadingRemainder(afterPrefix, isSentenceEnd) is { } convertedRest)
                return keigoPrefix + convertedRest + punct;
        }

        // 4. 変換後テキスト内の語彙と文末表現の変換
        var rewritten = ApplyInTextReplacements(textCore);

        if (isSentenceEnd)
        {
            var suffixApplied = false;
            if (!IsAlreadyKeigo(rewritten))
            {
                foreach (var (suffix, replacement) in TailTextSuffixes)
                {
                    if (rewritten.EndsWith(suffix, StringComparison.Ordinal) &&
                        (rewritten.Length > suffix.Length || suffix.Length >= 2))
                    {
                        rewritten = rewritten[..^suffix.Length] + replacement;
                        suffixApplied = true;
                        break;
                    }
                }
            }

            if (!suffixApplied && !IsAlreadyPolite(rewritten))
            {
                // 名詞・形容動詞語幹 + だ (予定だ → 予定です、テストだ → テストです)。動詞過去形 (読んだ・進んだ) は除外
                if (rewritten.Length >= 2 && rewritten.EndsWith('だ') && IsNominalStem(rewritten[^2]))
                {
                    rewritten = rewritten[..^1] + "です";
                }
                // 文末の い形容詞 (難しい → 難しいです、難しかった → 難しかったです)
                else if (TryPoliteAdjective(rewritten) is { } politeAdj)
                {
                    rewritten = politeAdj;
                }
                // 文末の「〜て」依頼形 (教えて → 教えてください、確認して → 確認してください)。後ろに実質の文節があるときは付けない
                else if (!hasNextContent && rewritten.Length >= 2 &&
                    (rewritten.EndsWith("して", StringComparison.Ordinal) || (!IsKanaOnly(rewritten) && (rewritten.EndsWith('て') || rewritten.EndsWith('で')))))
                {
                    rewritten += "ください";
                }
            }
        }

        return rewritten != textCore ? rewritten + punct : null;
    }

    private static string? ConvertQuestion(string readingCore, string textCore, string punct)
    {
        if (QuestionReadingMap.TryGetValue(readingCore, out var qWord))
            return qWord + punct;

        // 語頭接頭辞 + 疑問の残り (きょうはいく？ → 本日は行かれますでしょうか？)
        foreach (var (readingPrefix, keigoPrefix, allowsParticle) in ReadingPrefixes)
        {
            if (!readingCore.StartsWith(readingPrefix, StringComparison.Ordinal)) continue;
            var afterPrefix = readingCore[readingPrefix.Length..];
            if (allowsParticle)
            {
                foreach (var particle in Particles)
                {
                    if (!afterPrefix.StartsWith(particle, StringComparison.Ordinal)) continue;
                    var tail = afterPrefix[particle.Length..];
                    if (QuestionReadingMap.TryGetValue(tail, out var qTail))
                        return keigoPrefix + particle + qTail + punct;
                }
            }
            if (QuestionReadingMap.TryGetValue(afterPrefix, out var qRest))
                return keigoPrefix + qRest + punct;
        }

        var rewritten = ApplyInTextReplacements(textCore);
        if (!rewritten.EndsWith("でしょうか", StringComparison.Ordinal))
        {
            foreach (var (suffix, replacement) in QuestionSuffixes)
            {
                if (rewritten.EndsWith(suffix, StringComparison.Ordinal))
                {
                    rewritten = rewritten[..^suffix.Length] + replacement;
                    break;
                }
            }
        }

        return rewritten != textCore ? rewritten + punct : null;
    }

    private static string ApplyInTextReplacements(string text)
    {
        var rewritten = text;
        foreach (var (from, to) in InTextReplacements)
        {
            if (rewritten.Contains(from, StringComparison.Ordinal))
                rewritten = rewritten.Replace(from, to, StringComparison.Ordinal);
        }
        return rewritten;
    }

    private static string? TryPoliteAdjective(string text)
    {
        foreach (var adj in PoliteAdjectives)
        {
            if (text.EndsWith(adj, StringComparison.Ordinal)) return text + "です";
            var past = adj[..^1] + "かった";
            if (text.EndsWith(past, StringComparison.Ordinal)) return text + "です";
        }
        return null;
    }

    private static bool IsAlreadyKeigo(string text) =>
        AlreadyKeigoEndings.Any(e => text.EndsWith(e, StringComparison.Ordinal));

    private static bool IsAlreadyPolite(string text) =>
        AlreadyPoliteEndings.Any(e => text.EndsWith(e, StringComparison.Ordinal));

    // 「〜だ → 〜です」を安全に適用できる直前文字 (漢字・カタカナ・英数字。ひらがなの「んだ」「まだ」は除外)
    private static bool IsNominalStem(char c) =>
        c is (>= '一' and <= '鿿') or (>= 'ァ' and <= 'ヺ') or 'ー' || char.IsAsciiLetterOrDigit(c);

    private static bool IsPunctuationChar(char c) =>
        c is '。' or '、' or '．' or '，' or '.' or ',' or '！' or '？' or '!' or '?' or '…' or '‥';

    private static bool IsPunctuationOnly(string text) =>
        text.Length > 0 && text.All(IsPunctuationChar);

    private static bool HasClauseEnd(string text) =>
        text.Length > 0 && IsPunctuationChar(text[^1]);

    private static bool IsQuestion(string punct) =>
        punct.Contains('？') || punct.Contains('?');

    private static string? ConvertReadingRemainder(string reading, bool isSentenceEnd)
    {
        if (ReadingWords.TryGetValue(reading, out var word)) return word;
        if (isSentenceEnd && TailReadingMap.TryGetValue(reading, out var tail)) return tail;
        foreach (var (readingPrefix, keigoPrefix, allowsParticle) in ReadingPrefixes)
        {
            if (!reading.StartsWith(readingPrefix, StringComparison.Ordinal)) continue;
            var rest = reading[readingPrefix.Length..];
            if (rest.Length == 0) return keigoPrefix;
            if (allowsParticle)
            {
                foreach (var particle in Particles)
                {
                    if (rest == particle) return keigoPrefix + particle;
                    if (rest.StartsWith(particle, StringComparison.Ordinal) &&
                        ConvertReadingRemainder(rest[particle.Length..], isSentenceEnd) is { } convertedAfterParticle)
                        return keigoPrefix + particle + convertedAfterParticle;
                }
            }
            if (ConvertReadingRemainder(rest, isSentenceEnd) is { } convertedRest)
                return keigoPrefix + convertedRest;
        }
        return null;
    }

    private static bool IsKanaOnly(string text) => text.All(c => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー');

    private static (string Core, string Punctuation) SplitTrailingPunctuation(string text)
    {
        var end = text.Length;
        while (end > 0 && IsPunctuationChar(text[end - 1]))
            end--;
        return (text[..end], text[end..]);
    }

    private static void SetKeigo(Clause clause, string keigo)
    {
        clause.NotLearned.Add(keigo);
        clause.Candidates.Remove(keigo);
        clause.Candidates.Insert(0, keigo);
        clause.Index = 0;
    }
}
