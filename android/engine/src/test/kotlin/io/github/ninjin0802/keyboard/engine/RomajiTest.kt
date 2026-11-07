// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

import org.junit.Test
import java.io.File
import kotlin.test.*

/** ローマ字からかなへの組み立て（spec 05「ローマ字入力」）。 */
class RomajiTest {
    /** spec 05「ローマ字の表」の写し。1行が表の1行で、「打ち方=かな」を空白で区切る。 */
    private val spec = """
        a=あ i=い u=う e=え o=お
        ka=か ki=き ku=く ke=け ko=こ ca=か cu=く co=こ
        sa=さ si=し su=す se=せ so=そ shi=し ci=し ce=せ
        ta=た ti=ち tu=つ te=て to=と chi=ち tsu=つ
        na=な ni=に nu=ぬ ne=ね no=の
        ha=は hi=ひ hu=ふ he=へ ho=ほ fu=ふ
        ma=ま mi=み mu=む me=め mo=も
        ya=や yu=ゆ yo=よ ye=いぇ
        ra=ら ri=り ru=る re=れ ro=ろ
        wa=わ wo=を wu=う wi=うぃ we=うぇ wyi=ゐ wye=ゑ
        nn=ん n'=ん xn=ん
        ga=が gi=ぎ gu=ぐ ge=げ go=ご
        za=ざ zi=じ zu=ず ze=ぜ zo=ぞ ji=じ
        da=だ di=ぢ du=づ de=で do=ど
        ba=ば bi=び bu=ぶ be=べ bo=ぼ
        pa=ぱ pi=ぴ pu=ぷ pe=ぺ po=ぽ
        kya=きゃ kyi=きぃ kyu=きゅ kye=きぇ kyo=きょ
        gya=ぎゃ gyi=ぎぃ gyu=ぎゅ gye=ぎぇ gyo=ぎょ
        sya=しゃ syi=しぃ syu=しゅ sye=しぇ syo=しょ sha=しゃ shu=しゅ she=しぇ sho=しょ
        zya=じゃ zyi=じぃ zyu=じゅ zye=じぇ zyo=じょ ja=じゃ ju=じゅ je=じぇ jo=じょ jya=じゃ jyi=じぃ jyu=じゅ jye=じぇ jyo=じょ
        tya=ちゃ tyi=ちぃ tyu=ちゅ tye=ちぇ tyo=ちょ cha=ちゃ chu=ちゅ che=ちぇ cho=ちょ cya=ちゃ cyi=ちぃ cyu=ちゅ cye=ちぇ cyo=ちょ
        dya=ぢゃ dyi=ぢぃ dyu=ぢゅ dye=ぢぇ dyo=ぢょ
        nya=にゃ nyi=にぃ nyu=にゅ nye=にぇ nyo=にょ
        hya=ひゃ hyi=ひぃ hyu=ひゅ hye=ひぇ hyo=ひょ
        bya=びゃ byi=びぃ byu=びゅ bye=びぇ byo=びょ
        pya=ぴゃ pyi=ぴぃ pyu=ぴゅ pye=ぴぇ pyo=ぴょ
        mya=みゃ myi=みぃ myu=みゅ mye=みぇ myo=みょ
        rya=りゃ ryi=りぃ ryu=りゅ rye=りぇ ryo=りょ
        fa=ふぁ fi=ふぃ fe=ふぇ fo=ふぉ fya=ふゃ fyi=ふぃ fyu=ふゅ fye=ふぇ fyo=ふょ fwa=ふぁ fwi=ふぃ fwu=ふぅ fwe=ふぇ fwo=ふぉ hwa=ふぁ hwi=ふぃ hwe=ふぇ hwo=ふぉ hwyu=ふゅ
        va=ゔぁ vi=ゔぃ vu=ゔ ve=ゔぇ vo=ゔぉ vya=ゔゃ vyi=ゔぃ vyu=ゔゅ vye=ゔぇ vyo=ゔょ
        tha=てゃ thi=てぃ thu=てゅ the=てぇ tho=てょ dha=でゃ dhi=でぃ dhu=でゅ dhe=でぇ dho=でょ t'i=てぃ t'yu=てゅ d'i=でぃ d'yu=でゅ
        twa=とぁ twi=とぃ twu=とぅ twe=とぇ two=とぉ dwa=どぁ dwi=どぃ dwu=どぅ dwe=どぇ dwo=どぉ t'u=とぅ d'u=どぅ
        tsa=つぁ tsi=つぃ tse=つぇ tso=つぉ
        kwa=くぁ kwi=くぃ kwu=くぅ kwe=くぇ kwo=くぉ gwa=ぐぁ gwi=ぐぃ gwu=ぐぅ gwe=ぐぇ gwo=ぐぉ qa=くぁ qi=くぃ qu=く qe=くぇ qo=くぉ qya=くゃ qyu=くゅ qyo=くょ
        swa=すぁ swi=すぃ swu=すぅ swe=すぇ swo=すぉ zwa=ずぁ zwi=ずぃ zwu=ずぅ zwe=ずぇ zwo=ずぉ
        wha=うぁ whi=うぃ whu=う whe=うぇ who=うぉ
        xa=ぁ xi=ぃ xu=ぅ xe=ぇ xo=ぉ la=ぁ li=ぃ lu=ぅ le=ぇ lo=ぉ xya=ゃ xyi=ぃ xyu=ゅ xye=ぇ xyo=ょ lya=ゃ lyi=ぃ lyu=ゅ lye=ぇ lyo=ょ xka=ヵ xke=ヶ lka=ヵ lke=ヶ xtu=っ xtsu=っ ltu=っ ltsu=っ xwa=ゎ lwa=ゎ
        -=ー ,=、 .=。
    """.trimIndent().lines().flatMap { it.trim().split(" ") }.map { it.substringBefore('=') to it.substringAfter('=') }

    /** [text] を1文字ずつ打った結果。できたかな（と、英字のまま読みに入ったもの）と、打ちかけの英字。 */
    private fun type(text: String): Pair<String, String> {
        val kana = StringBuilder(); var pending = ""
        for (c in text) Romaji.feed(pending, c).let { kana.append(it.kana); pending = it.pending }
        return kana.toString() to pending
    }
    private fun typed(text: String): String = type(text).let { it.first + Romaji.finish(it.second) }

    @Test fun the_copy_of_the_table_is_the_table_in_the_spec() {
        val html = File("../docs/spec/05-input-engine.html").readText()
        val section = html.substringAfter("id=\"ローマ字の表\"").substringBefore("<h3")
        val inSpec = Regex("<code>([^<]+)</code>=([^ <]+)").findAll(section)
            .map { it.groupValues[1].replace("&#x27;", "'").replace("&amp;", "&") to it.groupValues[2] }.toList()
        assertEquals(spec, inSpec)
        assertTrue(spec.size > 290, "${spec.size}")
    }
    @Test fun the_engine_has_exactly_the_table() = assertEquals(spec.toMap(), Romaji.table)
    @Test fun every_spelling_gives_its_kana_and_leaves_nothing_pending() {
        for ((romaji, kana) in spec) assertEquals(kana to "", type(romaji), romaji)
    }
    @Test fun no_spelling_is_the_beginning_of_another() {
        for ((a, _) in spec) for ((b, _) in spec) assertFalse(a != b && b.startsWith(a), "$a $b")
    }
    @Test fun every_beginning_of_a_spelling_waits_as_typed() {
        for ((romaji, _) in spec) for (n in 1 until romaji.length) {
            val head = romaji.take(n)
            assertEquals("" to head, type(head), head)
        }
    }
    @Test fun every_spelling_works_right_after_another_kana() {
        for ((romaji, kana) in spec) assertEquals("か$kana" to "", type("ka$romaji"), romaji)
    }
    @Test fun rows_the_brief_names() {
        val expected = mapOf(
            "shi" to "し", "si" to "し", "chi" to "ち", "ti" to "ち", "tsu" to "つ", "tu" to "つ", "fu" to "ふ", "hu" to "ふ",
            "ji" to "じ", "zi" to "じ", "di" to "ぢ", "du" to "づ", "wo" to "を", "kya" to "きゃ", "sha" to "しゃ", "sya" to "しゃ",
            "cha" to "ちゃ", "tya" to "ちゃ", "cya" to "ちゃ", "ja" to "じゃ", "jya" to "じゃ", "zya" to "じゃ", "nya" to "にゃ",
            "hya" to "ひゃ", "mya" to "みゃ", "rya" to "りゃ", "gya" to "ぎゃ", "bya" to "びゃ", "pya" to "ぴゃ",
            "fa" to "ふぁ", "fi" to "ふぃ", "fe" to "ふぇ", "fo" to "ふぉ", "va" to "ゔぁ", "vi" to "ゔぃ", "vu" to "ゔ", "ve" to "ゔぇ",
            "vo" to "ゔぉ", "thi" to "てぃ", "dhi" to "でぃ", "wi" to "うぃ", "we" to "うぇ", "she" to "しぇ", "je" to "じぇ",
            "che" to "ちぇ", "tsa" to "つぁ", "tse" to "つぇ", "tso" to "つぉ", "kwa" to "くぁ", "gwa" to "ぐぁ",
            "xa" to "ぁ", "la" to "ぁ", "xtu" to "っ", "ltu" to "っ", "xtsu" to "っ", "ltsu" to "っ", "xya" to "ゃ", "lya" to "ゃ",
            "xwa" to "ゎ", "-" to "ー", "," to "、", "." to "。",
        )
        for ((romaji, kana) in expected) assertEquals(kana to "", type(romaji), romaji)
    }

    // ---- ん
    @Test fun nn_is_n_at_once() = assertEquals("ん" to "", type("nn"))
    @Test fun n_apostrophe_is_n() = assertEquals("ん" to "", type("n'"))
    @Test fun a_single_n_waits() = assertEquals("" to "n", type("n"))
    @Test fun n_before_a_vowel_or_y_is_the_na_row() {
        assertEquals("な" to "", type("na")); assertEquals("" to "ny", type("ny")); assertEquals("にょ" to "", type("nyo"))
    }
    @Test fun n_before_another_consonant_is_n_and_the_consonant_goes_on() {
        for (c in "bcdfghjklmpqrstvwxz") assertEquals("ん" to "$c", type("n$c"), "n$c")
        assertEquals("んか" to "", type("nka")); assertEquals("んちゃ" to "", type("ncha"))
    }
    @Test fun n_before_punctuation_is_n() {
        assertEquals("ん、" to "", type("n,")); assertEquals("ん。" to "", type("n.")); assertEquals("んー" to "", type("n-"))
    }
    @Test fun a_vowel_after_nn_is_in_the_a_row() {
        assertEquals("んあ" to "", type("nna")); assertEquals("んな" to "", type("nnna")); assertEquals("んや" to "", type("nnya"))
    }
    @Test fun a_trailing_n_becomes_n_when_finished_and_other_letters_stay_as_typed() {
        assertEquals("ん", Romaji.finish("n")); assertEquals("", Romaji.finish(""))
        assertEquals("k", Romaji.finish("k")); assertEquals("ny", Romaji.finish("ny")); assertEquals("lts", Romaji.finish("lts"))
    }

    // ---- っ
    @Test fun a_doubled_consonant_is_a_small_tsu_and_the_consonant_goes_on() {
        for (c in "bcdfghjklmpqrstvwxyz") assertEquals("っ" to "$c", type("$c$c"), "$c$c")
        assertEquals("っか" to "", type("kka")); assertEquals("っしゃ" to "", type("ssha")); assertEquals("っっぱ" to "", type("pppa"))
    }
    @Test fun tc_is_a_small_tsu_before_ch() {
        assertEquals("っ" to "c", type("tc")); assertEquals("っち" to "", type("tchi")); assertEquals("っちゃ" to "", type("tcha"))
    }
    @Test fun a_doubled_vowel_is_two_vowels() = assertEquals("ああ" to "", type("aa"))

    // ---- 表にない並び
    @Test fun a_letter_that_starts_no_spelling_goes_in_as_typed() {
        assertEquals("'" to "", type("'")); assertEquals("1" to "", type("1")); assertEquals("か'" to "", type("ka'"))
        // 英字は、どれも何かの打ち方の先頭になる。
        for (c in 'a'..'z') assertTrue(type("$c").let { it.first.isEmpty() || it.second.isEmpty() && it.first.length == 1 }, "$c")
    }
    @Test fun a_beginning_that_cannot_go_on_goes_in_as_typed_and_the_new_letter_starts_again() {
        assertEquals("k" to "q", type("kq")); assertEquals("kくぁ" to "", type("kqa")); assertEquals("ヵ" to "", type("lka"))
        assertEquals("ky" to "k", type("kyk")); assertEquals("k、" to "", type("k,"))
        assertEquals("sh" to "t", type("sht")); assertEquals("んy" to "k", type("nyk"))
    }
    @Test fun the_pending_tail_never_exceeds_three_letters() {
        val letters = "abcdefghijklmnopqrstuvwxyz-,.'"
        fun walk(pending: String, depth: Int) {
            assertTrue(pending.length <= 3, pending)
            if (depth == 0) return
            for (c in letters) walk(Romaji.feed(pending, c).pending, depth - 1)
        }
        walk("", 4)
    }

    // ---- 続けて打つ
    @Test fun sequences() {
        val expected = listOf(
            "kyouha" to "きょうは", "konnnichiha" to "こんにちは", "konnichiha" to "こんいちは", "kappa" to "かっぱ",
            "matcha" to "まっちゃ", "shinbun" to "しんぶん", "sannnin" to "さんにん", "san'in" to "さんいん",
            "ra-menn" to "らーめん", "honnya" to "ほんや", "gennin" to "げんいん", "kekkonn" to "けっこん", "nippon" to "にっぽん",
            "toukyouto" to "とうきょうと", "fairu" to "ふぁいる", "vaiorin" to "ゔぁいおりん", "thi-" to "てぃー",
            "ltsu" to "っ", "qu-ru" to "くーる", "wu" to "う", "fwi" to "ふぃ", "attoiuma" to "あっというま", "sou,desu." to "そう、です。", "wowo" to "をを",
        )
        for ((romaji, kana) in expected) assertEquals(kana, typed(romaji), romaji)
    }
    @Test fun a_trailing_n_stays_pending_until_finished() {
        assertEquals("しんぶ" to "n", type("shinbun")); assertEquals("さんに" to "n", type("sannnin"))
    }
}
