// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.engine

/**
 * ローマ字からかなへの組み立て（spec 05「ローマ字入力」）。状態は持たない。
 * 打ちかけの英字は入力セッションが持ち、1文字打つたびに [feed] に渡す。
 */
object Romaji {
    /** [kana]: 読みに足すもの（かな。表にない英字は、そのまま入る）。[pending]: 新しい打ちかけの英字。 */
    data class Step(val kana: String, val pending: String)

    /** 打ち方からかなへ。spec 05「ローマ字の表」と同じ並び（1行が表の1行）。どの打ち方も、ほかの打ち方の途中までと一致しない。 */
    val table: Map<String, String> = """
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
    """.trimIndent().lines().flatMap { it.trim().split(" ") }.associate { it.substringBefore('=') to it.substringAfter('=') }

    /** 表の打ち方の、途中まで（1文字以上で、打ち方そのものより短いもの）。 */
    private val beginnings: Set<String> = table.keys.flatMapTo(HashSet()) { key -> (1 until key.length).map(key::take) }

    /** 2つ続くと っ になる子音。表の打ち方の先頭に出てくる子音から、n を除いたもの。 */
    private val doubling: Set<Char> = table.keys.mapTo(HashSet()) { it[0] }.filterTo(HashSet()) { it in 'a'..'z' && it !in "aiueon" }

    /** 打ちかけの英字 [pending] のあとに [c] を打つ。 */
    fun feed(pending: String, c: Char): Step {
        var rest = pending + c
        val kana = StringBuilder()
        while (rest.isNotEmpty() && rest !in beginnings) {
            val whole = table[rest]
            if (whole != null) {
                kana.append(whole)
                rest = ""
                break
            }
            // ここから先は、先頭の1文字を片付けて、残りを見直す。
            val first = rest[0]
            kana.append(
                when {
                    first == 'n' -> 'ん'
                    rest.length > 1 && first in doubling && (rest[1] == first || (first == 't' && rest[1] == 'c')) -> 'っ'
                    else -> first      // 表にない並び。英字のまま入れる。
                },
            )
            rest = rest.substring(1)
        }
        return Step(kana.toString(), rest)
    }

    /** 確定や変換のときの、打ちかけの英字の読み方。n ひとつは ん、ほかは打ったまま。 */
    fun finish(pending: String): String = if (pending == "n") "ん" else pending
}

/** 英字の大文字と小文字（英字フリック配列の a⇔A キー。spec 04）。 */
object Latin {
    /** 英字なら、大文字と小文字を入れ替えたもの。英字でなければ null。 */
    fun toggleCase(c: Char): Char? = when (c) {
        in 'a'..'z' -> c.uppercaseChar()
        in 'A'..'Z' -> c.lowercaseChar()
        else -> null
    }
}
