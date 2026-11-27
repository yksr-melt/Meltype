// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Ime;

/// <summary>コードの行の、キャレットの位置がどこか。</summary>
public enum LineKind
{
    /// <summary>コード (英数が基本)。</summary>
    Code,
    /// <summary>コメントの中 (// # -- /* <!-- など)。日本語を書くことが多い。</summary>
    Comment,
    /// <summary>文字列の中 ("…" '…' `…`)。日本語を書くことが多い。</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "文字列リテラルの中という位置を表す値。データ型の名前ではない。")]
    String,
    /// <summary>ターミナルで動く AI・チャットの入力 (Claude Code・Codex などの「&gt; 」の後)。日本語を書くことが多い。</summary>
    Prompt,
}

/// <summary>「コード」のアプリで、フォーカスのある入力欄の種類。</summary>
internal enum CodeFocus
{
    /// <summary>コードを書く場所ではない (チャット・AI への質問、設定の画面など)。一般のアプリと同じに扱う。</summary>
    None,
    /// <summary>コードエディター。</summary>
    Editor,
    /// <summary>ターミナル (プロンプトは出力なので、改行のたびに読み直す)。</summary>
    Terminal,
}

/// <summary>
/// コードエディター・ターミナル (アプリの種類が「コード」) で、今の行のキャレットより前の文字列から、
/// キャレットがコメントや文字列の中にあるか (日本語を書く場所か) を調べる。
/// 前の行が読めるときは、複数行のコメント・文字列 (/* … */、""" … """) も調べる。
/// </summary>
internal static class LineContext
{
    /// <summary>
    /// キャレットより前の文字列 (前の行も含んでよい) から、キャレットの位置の種類を調べる。
    /// 前の行から続く複数行の文字列・コメント (Python の """ … """、/* … */、&lt;!-- … --&gt;) の中なら、その種類。
    /// それ以外は、今の行 (最後の改行より後ろ) だけで調べる。
    /// terminal はターミナルか (<see cref="IsTerminalComment"/>)。
    /// </summary>
    public static LineKind ClassifyText(string text, Settings settings, bool terminal = false)
    {
        var lastNewline = text.LastIndexOfAny(['\r', '\n']);
        var line = lastNewline >= 0 ? text[(lastNewline + 1)..] : text;
        if (lastNewline >= 0 && OpenBlock(text, settings, terminal) is { } block) return block;
        return Classify(line, settings, terminal);
    }

    /// <summary>
    /// 前の行から始まって、まだ閉じていない複数行の文字列・コメントがあれば、その種類 (無ければ null)。
    /// 1 行のコメント (# //) と 1 行の文字列 ("…" '…') の中の """ /* は数えない。
    /// </summary>
    private static LineKind? OpenBlock(string text, Settings settings, bool terminal)
    {
        string? close = null; // 閉じる記号 (""" ''' */ -->)
        var kind = LineKind.Code;
        var lineStart = true;
        var startedBeforeLastLine = false;
        var lastNewline = text.LastIndexOfAny(['\r', '\n']);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\r' or '\n')
            {
                lineStart = true;
                continue;
            }
            if (close is not null)
            {
                if (StartsWith(text, i, close))
                {
                    i += close.Length - 1;
                    close = null;
                }
                continue;
            }
            var atStart = lineStart && !char.IsWhiteSpace(c);
            if (!char.IsWhiteSpace(c)) lineStart = false;
            if (UrlLength(text, i) is > 0 and var url)
            {
                i += url - 1;
                continue;
            }
            string? open = null;
            if (StartsWith(text, i, "\"\"\"")) (open, kind) = ("\"\"\"", LineKind.String);
            else if (StartsWith(text, i, "'''")) (open, kind) = ("'''", LineKind.String);
            else if (settings.CodeCommentBlock && IsBlockComment(text, i, terminal)) (open, kind) = ("*/", LineKind.Comment);
            else if (settings.CodeCommentHtml && StartsWith(text, i, "<!--")) (open, kind) = ("-->", LineKind.Comment);
            if (open is not null)
            {
                close = open;
                startedBeforeLastLine = i < lastNewline;
                i += (open == "*/" ? 2 : open == "-->" ? 4 : 3) - 1;
                continue;
            }
            // 1 行のコメント: 行の終わりまで飛ばす。
            // ターミナルの引数の // (//src/work/* など) は行コメントではないので飛ばさない。引数の中の /* は語の先頭にないので、IsBlockComment がコメントにしない
            if (settings.CodeCommentSlash && IsSlashComment(text, i, terminal) ||
                settings.CodeCommentHash && IsHashComment(text, i, terminal: false) || settings.CodeCommentDash && IsDashComment(text, i, terminal: false) ||
                settings.CodeCommentRem && atStart && StartsWithIgnoreCase(text, i, "rem "))
            {
                while (i + 1 < text.Length && text[i + 1] is not ('\r' or '\n')) i++;
                continue;
            }
            // 1 行の文字列: 同じ引用符か行の終わりまで飛ばす
            if (c is '"' or '\'' && !(c == '\'' && i > 0 && char.IsLetterOrDigit(text[i - 1])))
            {
                for (i++; i < text.Length && text[i] != c && text[i] is not ('\r' or '\n'); i++)
                {
                    if (text[i] == '\\') i++;
                }
                if (i < text.Length && text[i] is '\r' or '\n') i--;
            }
        }
        // 今の行より前で開いて、まだ閉じていない。今の行で開いたものは、今の行だけで調べる (Classify)。
        return close is not null && startedBeforeLastLine ? kind : null;
    }

    public static LineKind Classify(string line, Settings settings, bool terminal = false)
    {
        if (IsChatPrompt(line)) return LineKind.Prompt;
        char? quote = null;
        var firstText = true; // まだ空白以外の文字が出ていない
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote is { } q)
            {
                if (c == '\\') i++; // エスケープ (\" は文字列の終わりではない)
                else if (c == q) quote = null;
                continue;
            }
            if (char.IsWhiteSpace(c)) continue;
            var atStart = firstText;
            firstText = false;

            if (c is '"' or '`' || (c == '\'' && !(i > 0 && char.IsLetterOrDigit(line[i - 1]))))
            {
                // ' は英単語の中 (don't) なら文字列の始まりではない。
                quote = c;
                continue;
            }
            if (UrlLength(line, i) is > 0 and var url)
            {
                i += url - 1;
                continue;
            }
            if (settings.CodeCommentSlash && IsSlashComment(line, i, terminal)) return LineKind.Comment;
            if (settings.CodeCommentBlock && IsBlockComment(line, i, terminal))
            {
                var close = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0) return LineKind.Comment;
                i = close + 1;
                continue;
            }
            if (settings.CodeCommentHtml && StartsWith(line, i, "<!--"))
            {
                var close = line.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (close < 0) return LineKind.Comment;
                i = close + 2;
                continue;
            }
            if (settings.CodeCommentHash && IsHashComment(line, i, terminal)) return LineKind.Comment;
            if (settings.CodeCommentDash && IsDashComment(line, i, terminal)) return LineKind.Comment;
            // 複数行コメントの途中の行 ( * …)
            if (settings.CodeCommentBlock && atStart && c == '*' && (i + 1 == line.Length || line[i + 1] == ' ')) return LineKind.Comment;
            // REM コメント (バッチファイル)
            if (settings.CodeCommentRem && atStart && StartsWithIgnoreCase(line, i, "rem ")) return LineKind.Comment;
        }
        return quote is null ? LineKind.Code : LineKind.String;
    }

    // ターミナルのシェルでは、コメントの記号は語の先頭 (行頭か空白の後) にあるときだけコメント。語の途中の # は $#・${#name}・${path#*/}・16#ff・.#pkg。
    // 語の先頭でも、直後に ASCII が続く // -- /* はコマンドの引数 (Bazel の //src/main:app、bash --kyouha、ls /*.c) なので、直後が空白か ASCII 以外 (確定した日本語) のときだけコメントにする。
    // 打ったばかりの記号も、続く引数を直接入力するためにコードとする。中で動くエディター (Vim など) のコメントも、記号の後に空白を打てば日本語で書ける。
    // 改行は本文の始まりにしない。ls /* を実行した後の行を、閉じていない /* コメントの中にしない。
    private static bool IsTerminalComment(string text, int index, int length) =>
        IsWordStart(text, index) && index + length < text.Length && text[index + length] is not ('\r' or '\n') && StartsCommentBody(text[index + length]);

    private static bool IsWordStart(string text, int index) => index == 0 || char.IsWhiteSpace(text[index - 1]);

    // コメントの本文の始まり: 空白か、確定した日本語 (「」※ や全角の文字も)。
    private static bool StartsCommentBody(char c) => c is > '~' || char.IsWhiteSpace(c);

    private static bool IsSlashComment(string text, int index, bool terminal) =>
        StartsWith(text, index, "//") && (!terminal || IsTerminalComment(text, index, 2));

    // ターミナルの /* は、ls src/*.c・ls /* のようなワイルドカードのほうが多い。
    private static bool IsBlockComment(string text, int index, bool terminal) =>
        StartsWith(text, index, "/*") && (!terminal || IsTerminalComment(text, index, 2));

    // #1623: # の直後に日本語を確定してもコメントのままにする。## (区切り) もコメント。
    // # の直後が ASCII の文字なら、#include・#region・r#type のような指令や名前なのでコードのまま。
    private static bool IsHashComment(string text, int index, bool terminal) =>
        text[index] == '#' && (terminal
            ? IsWordStart(text, index) && index + 1 < text.Length && (text[index + 1] == '#' || StartsCommentBody(text[index + 1]))
            : index + 1 == text.Length || text[index + 1] == '#' || StartsCommentBody(text[index + 1]));

    // #1623: -- コメント (SQL・Lua・Haskell) は、直後に日本語・全角の本文を確定してもコメントのままにする。
    // 直前の境界は x-- の減算、直後の ASCII は --count のようなオプションと区別するために残す。
    private static bool IsDashComment(string text, int index, bool terminal) =>
        StartsWith(text, index, "--") && (terminal
            ? IsTerminalComment(text, index, 2)
            : IsWordStart(text, index) && (index + 2 == text.Length || StartsCommentBody(text[index + 2])));

    /// <summary>
    /// 行コメントの // にすぐ続けて打った記号 c か (C# の ///、Rust の //!、区切りの //---- //==== //****)。
    /// コメントの記号の一部なので、日本語の記号にしない。ほかの記号・英数字・日本語・空白を打ったら本文。
    /// kana はかな入力か。記号のキーもかな (; は れ、- は ほ) を打つので、/// の / だけを記号とする。
    /// </summary>
    public static bool ContinuesSlashCommentMarker(string before, char c, bool kana)
    {
        if (!IsSlashCommentMarker(c) || kana && c != '/') return false;
        var start = before.Length;
        while (start > 0 && IsSlashCommentMarker(before[start - 1])) start--;
        return StartsWith(before, start, "//");
    }

    // コメントの記号に使うものだけにする。[ ( " などは「せつめい」のように本文の始まりで、日本語の記号にする。
    private static bool IsSlashCommentMarker(char c) => c is '/' or '!' or '-' or '=' or '*';

    // #1623: http:// や file:/// のスキーム名の後の / から URL の終わりまでの長さ (URL でなければ 0)。
    // URL の中の // と /* (gs://bucket/* のワイルドカード) をコメントとみなさない。
    // :// はほぼ URL にしか出ないので、スキーム名は一覧にせず「英字で始まり英数字・+・-・. が続く名前 + :」で見分ける (s3://・git+ssh://)。
    // URL には空白と引用符が入らないので、そこを URL の終わりとする。後ろの // や # はコメントとして調べる。
    // URL より前で開いた括弧を閉じる ) も URL の終わり (CSS の url(https://x/a.png);/* の /* はコメント)。
    // URL の中で開いた括弧 (https://en.wikipedia.org/wiki/Foo_(bar)) は URL に含める。
    private static int UrlLength(string text, int index)
    {
        if (index < 2 || text[index - 1] != ':') return 0;
        var scheme = index - 1;
        while (scheme > 0 && (char.IsAsciiLetterOrDigit(text[scheme - 1]) || text[scheme - 1] is '+' or '-' or '.')) scheme--;
        if (scheme == index - 1 || !char.IsAsciiLetter(text[scheme])) return 0;
        var slashes = index;
        while (slashes < text.Length && text[slashes] == '/') slashes++;
        if (slashes - index is not (2 or 3)) return 0;
        var end = slashes;
        var depth = 0; // URL の中で開いた括弧の数
        for (; end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('"' or '\'' or '`'); end++)
        {
            if (text[end] == '(') depth++;
            else if (text[end] == ')' && depth-- == 0) break;
        }
        return end - index;
    }

    /// <summary>
    /// ターミナルで動く AI・チャットの入力行か (Claude Code・Gemini CLI の「&gt; 」、Codex の「› 」。枠線の │ は飛ばす)。
    /// PowerShell の続きの行「&gt;&gt; 」や、シェルのプロンプト (PS C:\&gt;、$、❯) は含めない。
    /// </summary>
    private static bool IsChatPrompt(string line)
    {
        var i = 0;
        while (i < line.Length && (char.IsWhiteSpace(line[i]) || line[i] is '│' or '┃' or '║' or '|' or '╎' or '┆')) i++;
        if (i >= line.Length || line[i] is not ('>' or '›')) return false;
        return i + 1 == line.Length || line[i + 1] == ' ' || line[i + 1] == '\u00A0';
    }

    /// <summary>
    /// キャレットより前の文字列の最後の行が、パスワードを尋ねるプロンプトか。コロン (日本語・中国語の訳は全角) で終わり、次のどちらかに当たる。
    /// sudo の訳 ([sudo] %p のパスワード: など 38 言語) は password の訳語がさまざまなので、先頭の「[sudo] 」(セルビア語は「[судо] 」) で見分ける。sudo-rs は「[sudo: authenticate] 」で始まる。
    /// ssh・git (Password for 'https://…':)・PAM・鍵のパスフレーズは password・passphrase (日本語訳はパスワード・パスフレーズ) を含む。
    /// </summary>
    public static bool IsPasswordPrompt(string text)
    {
        var line = text[(text.LastIndexOfAny(['\r', '\n']) + 1)..].TrimEnd();
        if (line.Length == 0 || line[^1] is not (':' or '：')) return false;
        return line.StartsWith("[sudo] ", StringComparison.Ordinal) || line.StartsWith("[sudo: ", StringComparison.Ordinal) || line.StartsWith("[судо] ", StringComparison.Ordinal) ||
            ContainsAny(line, PasswordWords);
    }

    private static readonly string[] PasswordWords = ["password", "passphrase", "パスワード", "パスフレーズ"];

    private static readonly string[] ChatWords =
        ["chat", "チャット", "copilot", "claude", "codex", "gemini", "prompt", "プロンプト", "message", "メッセージ", "ask ", "質問", "composer", "agent", "エージェント", "cascade", "assistant", "アシスタント"];

    private static readonly string[] EditorWords = ["editor", "エディター", "エディタ"];

    private static readonly string[] TerminalWords = ["terminal", "ターミナル", "端末", "console", "コンソール"];

    // VS Code の検索・コマンドパレット・ファイル名の入力など (英数で打つもの)
    private static readonly string[] SearchWords = ["search", "検索", "command", "コマンド", "quick", "file name", "ファイル名", "filter", "フィルター", "go to", "移動", "find", "replace", "置換"];

    /// <summary>ターミナルのアプリ (アプリ全体がターミナル)。</summary>
    public static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal.exe", "OpenConsole.exe", "conhost.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "wezterm-gui.exe", "alacritty.exe", "mintty.exe", "rio.exe",
        "com.apple.Terminal", "com.googlecode.iterm2", "com.github.wez.wezterm", "org.alacritty", "com.mitchellh.ghostty", "net.kovidgoyal.kitty", "dev.warp.Warp-Stable",
        "gnome-terminal-server", "konsole", "kitty", "alacritty", "wezterm-gui", "foot", "ghostty", "xterm", "tilix", "terminator", "xfce4-terminal",
    };

    /// <summary>エディター以外の入力欄 (チャット・拡張機能の画面) も多い、Electron 製のエディター。</summary>
    private static readonly HashSet<string> ElectronEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code.exe", "Code - Insiders.exe", "Cursor.exe", "Windsurf.exe", "zed.exe",
    };

    /// <summary>
    /// 「コード」のアプリで、フォーカスのある入力欄がコードを書く場所か (UI Automation の名前・クラス名から)。
    /// チャット・AI への入力欄 (VS Code の Copilot Chat、Claude Code の画面など) は一般のアプリと同じに扱う。
    /// VS Code などの Electron 製のエディターは入力欄の種類が多いので、エディター・ターミナル・検索と分かるものだけをコードとする。
    /// element は UI Automation で調べた要素 (Windows)。分からなければ、アプリ全体をコードを書く場所とする。
    /// </summary>
    public static CodeFocus ClassifyFocus(string process, Ime.ImeElement? element)
    {
        var name = element?.Name ?? "";
        var className = element?.ClassName ?? "";
        if (ContainsAny(name, ChatWords)) return CodeFocus.None;
        if (TerminalProcesses.Contains(process) || ContainsAny(name, TerminalWords) || className.Contains("TermControl", StringComparison.Ordinal)) return CodeFocus.Terminal;
        if (element is null) return CodeFocus.Editor;
        if (ElectronEditors.Contains(process)) return ContainsAny(name, EditorWords) || ContainsAny(name, SearchWords) ? CodeFocus.Editor : CodeFocus.None;
        return CodeFocus.Editor;
    }

    private static bool ContainsAny(string text, string[] words)
    {
        foreach (var word in words)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool StartsWith(string text, int index, string value) =>
        string.CompareOrdinal(text, index, value, 0, value.Length) == 0 && index + value.Length <= text.Length;

    private static bool StartsWithIgnoreCase(string text, int index, string value) =>
        index + value.Length <= text.Length && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>ウィンドウのタイトルが文章のファイル (README.md など) を開いているか。コードエディターでも文章なら「一般」として扱う。</summary>
    public static bool IsDocumentTitle(string title)
    {
        foreach (var extension in (ReadOnlySpan<string>)[".md", ".markdown", ".txt", ".rst", ".adoc", ".org", ".tex"])
        {
            var index = title.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index > 0 && (index + extension.Length == title.Length || !char.IsLetterOrDigit(title[index + extension.Length]))) return true;
        }
        return false;
    }
}
