// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app.emoji

/** 絵文字の画面に出すタブと、開いたときに選んでおくタブ（spec 04「種類」「最近使ったもの」）。 */
object EmojiTabs {
    const val NO_RECENTS_NOTE = "最近使った絵文字は、まだありません"

    /** 出すタブ。[showRecents] が false の欄（パスワード）では、「最近使ったもの」を出さない。 */
    fun offered(showRecents: Boolean): List<EmojiCategory> =
        if (showRecents) EmojiCategory.entries else EmojiCategory.entries.filter { it != EmojiCategory.RECENT }

    /** 画面を開いたときに選んでおくタブ。[hasRecents]: 「最近使ったもの」に1つでもあるか。 */
    fun initial(showRecents: Boolean, hasRecents: Boolean): EmojiCategory =
        if (showRecents && hasRecents) EmojiCategory.RECENT else EmojiCategory.SMILEYS

    /**
     * [category] のます目に絵文字が1つもないときに書く文。書くのは「最近使ったもの」だけ。
     * ほかの種類が空なのは、一覧を調べ終わる前か、一覧を読めなかったときで、そこには何も書かない。
     */
    fun emptyNote(category: EmojiCategory): String = if (category == EmojiCategory.RECENT) NO_RECENTS_NOTE else ""
}
