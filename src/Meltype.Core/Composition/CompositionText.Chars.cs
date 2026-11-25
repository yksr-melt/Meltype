// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Detection;
using Meltype.Dictionary;
using Meltype.Libraries.Chars;

namespace Meltype.Composition;

/// <summary>記号・カタカナ・全角の文字の変換。</summary>
internal static partial class CompositionText
{
    private static string PendingText(CompositionTextState state, TextEnv env, bool final)
    {
        var pending = CompositionText.Pending(state);
        if (pending.Length == 0) return "";
        return env.Detector.Romaji.ConvertLenient(pending.ToLowerInvariant(), final);
    }

    /// <summary>日本語の中で打った記号 (全角。テスターの報告で決めた)。英語の区間では打ったままの半角で出す。数字と括弧は半角のまま。</summary>
    private static char Symbol(CompositionTextState state, char c) => c switch
    {
        '-' => 'ー',
        // 読みにするときに TypedMarks が決める。
        ',' => ',',
        '.' => '.',
        // 設定の括弧の幅は、句読点と違って CompositionOutput では当てられない。出口には区間の種類が届かず、英語の区間の ( や、半角を選んだ候補の () まで全角になる (#1735)
        // TODO (#2010): 英語の区間かを状態で判別し、記号の表示の幅を読みに焼き込んでいるのがよくない。読みに全角の記号が入るので、Zenzai には半角に直して渡している (ZenzaiConverter.ToHalfWidth)
        '[' => state.SquareBrackets ? '［' : '「',
        ']' => state.SquareBrackets ? '］' : '」',
        // ASCII の括弧はチャット本文でもそのまま使われるため、入力した幅を保つ。
        '(' => state.FullWidthParentheses ? '（' : '(',
        ')' => state.FullWidthParentheses ? '）' : ')',
        // ! ? は全角 (テスターのチャットでも、日本語の後ろの ？ ！ は 96% が全角だった)。
        // : ; | はチャット本文でも半角で使うので、入力した幅を保つ。
        ':' => ':',
        ';' => ';',
        '|' => '|',
        '~' => '～',
        '\'' => '’',
        '"' => '”',
        // ` は Markdown 記法に使うので、日本語の中でも半角のまま (Space で ‘ ｀)。
        '`' => '`',
        // @ はメールアドレス・メンション、/ は URL・日付・パスで使うので、日本語の中でも半角のまま (Space で ＠ ／ ・)。
        '@' => '@',
        // # はハッシュタグ・チャンネル名 (Discord の #雑談) で使うので半角のまま (Space で # と半角空白に確定)。
        '#' => '#',
        '/' => '/',
        // JIS キーボードの ￥ キー
        '\\' => '￥',
        _ when c is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(c) => (char)(c + 0xFEE0),
        _ => c,
    };

    /// <summary>全角の記号 (＃ （ ％ ’ ￥) を半角に戻す。かな・漢字・句読点 (、。「」ー) はそのまま。</summary>
    public static string SymbolsToHalfWidth(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                '’' => '\'',
                '”' => '"',
                '￥' => '¥',
                >= '！' and <= '～' when !char.IsLetterOrDigit(chars[i]) => (char)(chars[i] - 0xFEE0),
                _ => chars[i],
            };
        }
        return new string(chars);
    }

    /// <summary>z + 記号 (Microsoft IME と同じ): z/ → ・、z. → …、z, → ‥、z- → ～、z[ → 『、z] → 』。zh zj zk zl → ← ↓ ↑ →。</summary>
    private static char? ZSymbol(char c) => char.ToLowerInvariant(c) switch
    {
        '/' => '・',
        '.' => '…',
        ',' => '‥',
        '-' => '～',
        '[' => '『',
        ']' => '』',
        'h' => '←',
        'j' => '↓',
        'k' => '↑',
        'l' => '→',
        _ => null,
    };

    public static string ToKatakana(string hiragana) => KanaChars.ToKatakana(hiragana);

    /// <summary>全角カタカナを半角カタカナにする。濁点・半濁点は半角の ﾞ / ﾟ に分ける。</summary>
    public static string ToHalfWidthKatakana(string katakana)
    {
        const string full = "。「」、・ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワンヵヶ";
        const string half = "｡｢｣､･ｦｧｨｩｪｫｬｭｮｯｰｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝｶｹ";
        var builder = new StringBuilder(katakana.Length);
        foreach (var c in katakana.Normalize(NormalizationForm.FormD))
        {
            if (c is '\u3099' or '゛')
            {
                builder.Append('ﾞ');
                continue;
            }
            if (c is '\u309A' or '゜')
            {
                builder.Append('ﾟ');
                continue;
            }
            var index = full.IndexOf(c);
            builder.Append(index >= 0 ? half[index] : c);
        }
        return builder.ToString();
    }

    public static string ToFullWidth(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '!' and <= '~') chars[i] = (char)(chars[i] + 0xFEE0);
            else if (chars[i] == ' ') chars[i] = '　';
        }
        return new string(chars);
    }
}
