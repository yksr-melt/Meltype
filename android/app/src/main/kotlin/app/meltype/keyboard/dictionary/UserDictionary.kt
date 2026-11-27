// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package app.meltype.keyboard.dictionary

import android.content.Context
import app.meltype.keyboard.core.NativeApi
import app.meltype.keyboard.core.coreDataDirectory
import org.json.JSONException
import org.json.JSONObject

/**
 * ユーザー辞書の 1 語。[pos] は Core の UserWord と同じく、品詞を書いていない行では名詞。[category] は分類 (無ければ空)。
 * 編集・削除の対象は Core が品詞・分類まで比べて探すので、一覧で受け取ったまま返す。
 */
data class UserWord(val reading: String, val word: String, val pos: String = NOUN, val category: String = "") {
    companion object {
        const val NOUN = "名詞"
    }
}

/**
 * 頼みの後のユーザー辞書。[words] は userdict.txt の順 (読めなければ null)、[error] はできなかった理由 (できたら null)。
 * 理由は Core が作る文で、Windows・Mac・Linux の設定アプリと同じ。
 */
data class UserDictionaryResult(val words: List<UserWord>?, val error: String?)

/**
 * Core のユーザー辞書 (データフォルダの userdict.txt) の一覧・登録・編集・削除。読み書きと検査は Core
 * (UserDictionaryRequests・UserDictionary) がし、ここは頼みを JSON で渡して答えを読むだけ。
 * 書いた語は、キーボードを開き直さなくても変換に効く (Core が userdict.txt の更新時刻を見て読み直す)。
 */
object CoreUserDictionary {
    fun list(context: Context) = request(context, JSONObject().put("op", "list"))

    /** 読みのローマ字は、先に [TypedReading] でひらがなにしておく。 */
    fun add(context: Context, reading: String, word: String) =
        request(context, JSONObject().put("op", "add").put("reading", reading).put("word", word))

    /** [target] の読みと単語を直す。品詞と並びの位置はそのまま。 */
    fun edit(context: Context, target: UserWord, reading: String, word: String) =
        request(context, JSONObject().put("op", "edit").put("target", target.json()).put("reading", reading).put("word", word))

    fun remove(context: Context, target: UserWord) =
        request(context, JSONObject().put("op", "remove").put("target", target.json()))

    private fun UserWord.json() = JSONObject().put("reading", reading).put("word", word).put("pos", pos).put("category", category)

    private fun request(context: Context, request: JSONObject): UserDictionaryResult {
        val response = try {
            NativeApi.userDictionary(
                coreDataDirectory(context.applicationContext).absolutePath.encodeToByteArray(),
                request.toString().encodeToByteArray(),
            )?.decodeToString()
        } catch (_: LinkageError) {
            null
        }
        return response?.let(::parse) ?: UserDictionaryResult(null, UNAVAILABLE)
    }

    private fun parse(text: String): UserDictionaryResult? = try {
        val json = JSONObject(text)
        val words = json.optJSONArray("words")?.let { array ->
            (0 until array.length()).map { i ->
                val word = array.getJSONObject(i)
                UserWord(word.getString("reading"), word.getString("word"), word.optString("pos", UserWord.NOUN), word.optString("category", ""))
            }
        }
        UserDictionaryResult(words, if (json.isNull("error")) null else json.getString("error"))
    } catch (_: JSONException) {
        null
    }

    /** Core を呼べなかったときの理由 (読みをひらがなにできなかったときも、登録を止めてこれを出す)。 */
    const val UNAVAILABLE = "ユーザー辞書を読み書きできませんでした。"
}
