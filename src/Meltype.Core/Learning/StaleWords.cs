// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Meltype.Libraries.Chars;

namespace Meltype.Learning;

/// <summary>学習しない語・学習データから読み込むときに捨てる語。</summary>
internal static class StaleWords
{
    private static readonly string[] EraNames = ["令和", "平成", "昭和", "大正", "明治"];
    private static readonly string[] EraReadings = ["れいわ", "へいせい", "しょうわ", "たいしょう", "めいじ"];

    /// <summary>
    /// 変換エンジンが今の日時・版から作る語か (きょう → 2026/11/10・火曜日、ことし → 令和八年、ばーじょん → Mozc-2.31)。学習しない。
    /// 読みに無い数字・元号を含むか、読みに無い「曜日」で終わる語。いちばん → 1番 も当てはまるが、Meltype の学習から外すだけで、Mozc は自分で学習する。
    /// </summary>
    // TODO: 推測をやめ、変換エンジンが付ける「学習しない」の印で決める。Mozc は日時・版の候補に NO_LEARNING を付けているので、
    // meltype_mozc_helper が候補と一緒に返す。
    // そうすれば いちばん → 1番 を巻き添えにしない。
    public static bool IsVolatile(string reading, string text) =>
        !reading.Any(IsDigit) && text.Any(IsDigit) ||
        EraNames.Any(text.Contains) && !EraReadings.Any(reading.Contains) ||
        text.EndsWith("曜日", StringComparison.Ordinal) && !reading.Contains("ようび", StringComparison.Ordinal);

    private static bool IsDigit(char c) => char.IsAsciiDigit(c) || c is >= '０' and <= '９';

    /// <summary>
    /// 覚えない語 (学習データに入っていても、読み込むときに捨てる)。日時・版から作る語、前の版が学習から外し漏れていた
    /// ユーティリティ変換の候補 (時候の挨拶・クリップボードの文字)、アプリの画面の文字が混ざった語 (U+FFFC を含む。
    /// Discord の画面の文字が文脈に入り、Zenzai がそれをそのまま返した: issue #1537)。
    /// </summary>
    public static bool IsStale(string reading, string text) =>
        IsVolatile(reading, text) ||
        text.Contains('\uFFFC', StringComparison.Ordinal) ||
        // 補助辞書に入っていた旧版では普通の変換として覚えた。今は順位だけ学習する候補。
        (reading, text) is ("じゃくしゃだんせい", "ふにゃオス") or ("ふにゃおす", "弱者男性") ||
        reading is "じこう" or "じこうのあいさつ" && text.Contains("の候", StringComparison.Ordinal) ||
        reading is "くりっぷぼーど" or "ぺーすと" && text != KanaChars.ToKatakana(reading);
}
