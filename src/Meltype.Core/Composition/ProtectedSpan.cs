// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

namespace Meltype.Composition;

/// <summary>自動変換してはいけない区間の種類。</summary>
public enum ProtectedKind
{
    Mention,
    Url,
    Email,
    PosixPath,
    WindowsPath,
}

/// <summary>
/// 打った原文 (raw) のうち、自動変換・幅変換・かな化・誤字補正から保護する区間。
/// 位置は raw の UTF-16 インデックス。サロゲートペアの途中には置かない。
/// </summary>
public readonly record struct ProtectedSpan(int Start, int Length, ProtectedKind Kind)
{
    public int End => Start + Length;
}

/// <summary>
/// 未確定の原文から、技術的な文字列 (メンション・URL・メール・パス) の区間を見つける。
/// 「ネットワーク上で妥当か」ではなく「構造の強い開始条件を満たすか」だけを見る (妥当性の検証ではない)。
/// 初期実装は読みやすい線形スキャン。
/// </summary>
public static class ProtectedSpanScanner
{
    /// <summary>この位置から始まる保護区間を、優先順 (URL → WindowsPath → Email → Mention → PosixPath) で返す。</summary>
    public static IReadOnlyList<ProtectedSpan> Scan(string raw)
    {
        var spans = new List<ProtectedSpan>();
        if (string.IsNullOrEmpty(raw)) return spans;
        var index = 0;
        var schemeEnd = 0;
        while (index < raw.Length)
        {
            var span = MatchAt(raw, index, spans.Count > 0 ? spans[^1].End : 0, ref schemeEnd);
            if (span is { } found)
            {
                spans.Add(found);
                index = Math.Max(found.End, index + 1);
            }
            else
            {
                index++;
            }
        }
        return spans;
    }

    /// <summary>raw 全体が 1 つの保護区間か。</summary>
    public static bool CoversWhole(string raw) =>
        Scan(raw) is { Count: 1 } spans && spans[0].Start == 0 && spans[0].End == raw.Length;

    /// <summary>raw の末尾が保護区間で終わっているか (その区間を返す)。</summary>
    public static bool TryTail(string raw, out ProtectedSpan span)
    {
        span = default;
        if (string.IsNullOrEmpty(raw)) return false;
        var spans = Scan(raw);
        if (spans.Count == 0 || spans[^1].End != raw.Length) return false;
        span = spans[^1];
        return true;
    }

    private static ProtectedSpan? MatchAt(string raw, int start, int previousEnd, ref int schemeEnd)
    {
        // 長い構造 (URL / パス / メール) を優先し、メンションは最後に回す。
        if (TryUrl(raw, start, ref schemeEnd) is { } url) return url;
        if (TryWindowsPath(raw, start) is { } windowsPath) return windowsPath;
        // メールのローカル部は @ から左へ探す。先に保護したメンション等の文字を再利用しない。
        if (TryEmail(raw, start) is { } email && email.Start >= previousEnd) return email;
        return TryMention(raw, start) ?? TryPosixPath(raw, start);
    }

    // ---- URL ----
    private static ProtectedSpan? TryUrl(string raw, int start, ref int schemeEnd)
    {
        if (!char.IsAsciiLetter(raw[start])) return null;
        // 同じ英字列の途中から、残り全文を何度も走査しない。scheme 候補の末尾は Scan ごとに使い回す。
        if (start >= schemeEnd)
        {
            schemeEnd = start + 1;
            while (schemeEnd < raw.Length && (char.IsAsciiLetterOrDigit(raw[schemeEnd]) || raw[schemeEnd] is '+' or '-' or '.')) schemeEnd++;
        }
        if (schemeEnd + 2 >= raw.Length || raw[schemeEnd] != ':' || raw[schemeEnd + 1] != '/' || raw[schemeEnd + 2] != '/') return null;
        var end = schemeEnd + 3;
        while (end < raw.Length && IsUrlChar(raw[end])) end++;
        return new ProtectedSpan(start, end - start, ProtectedKind.Url);
    }

    private static bool IsUrlChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or ':' or '/' or '?' or '#' or '[' or ']'
            or '@' or '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '=' or '%';

    // ---- Windows パス ----
    private static ProtectedSpan? TryWindowsPath(string raw, int start)
    {
        // UNC: \\server\share...
        if (raw[start] == '\\' && start + 1 < raw.Length && raw[start + 1] == '\\')
        {
            var server = start + 2;
            while (server < raw.Length && (char.IsAsciiLetterOrDigit(raw[server]) || raw[server] is '.' or '-' or '_')) server++;
            if (server > start + 2 && server < raw.Length && raw[server] is '\\' or '/')
            {
                var end = server + 1;
                if (end < raw.Length && IsPathElementStart(raw[end]))
                {
                    end++;
                    while (end < raw.Length && IsPathChar(raw[end])) end++;
                    return new ProtectedSpan(start, end - start, ProtectedKind.WindowsPath);
                }
            }
        }
        // C:\... / C:/...
        if (char.IsAsciiLetter(raw[start]) && start + 2 < raw.Length && raw[start + 1] == ':' && raw[start + 2] is '\\' or '/')
        {
            if (start + 3 < raw.Length && IsPathElementStart(raw[start + 3]))
            {
                var end = start + 3;
                while (end < raw.Length && IsPathChar(raw[end])) end++;
                return new ProtectedSpan(start, end - start, ProtectedKind.WindowsPath);
            }
        }
        return null;
    }

    // ---- メールアドレス ----
    private static ProtectedSpan? TryEmail(string raw, int start)
    {
        if (raw[start] != '@') return null;
        var localStart = start;
        while (localStart > 0 && IsEmailLocalChar(raw[localStart - 1])) localStart--;
        if (localStart == start) return null; // ローカル部が無い (@kuraido はメンション)
        var end = start + 1;
        while (end < raw.Length && IsEmailDomainChar(raw[end])) end++;
        return new ProtectedSpan(localStart, end - localStart, ProtectedKind.Email);
    }

    private static bool IsEmailLocalChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '%' or '+' or '-';

    private static bool IsEmailDomainChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '-';

    // ---- メンション ----
    private static ProtectedSpan? TryMention(string raw, int start)
    {
        if (raw[start] != '@') return null;
        // @ が語の先頭にある (直前が識別子文字でない)
        if (start > 0 && IsMentionChar(raw[start - 1])) return null;
        if (start + 1 >= raw.Length) return null;
        var first = raw[start + 1];
        if (!(char.IsAsciiLetterOrDigit(first) || first == '_')) return null;
        var end = start + 2;
        while (end < raw.Length && IsMentionChar(raw[end])) end++;
        return new ProtectedSpan(start, end - start, ProtectedKind.Mention);
    }

    private static bool IsMentionChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    // POSIX / macOS の代表的なルートディレクトリ名。1 階層の /home や /tmp などは保護対象にする。
    private static readonly HashSet<string> CommonRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "boot", "dev", "etc", "home", "lib", "lib64", "media", "mnt", "opt",
        "proc", "root", "run", "sbin", "srv", "sys", "tmp", "usr", "var",
        "Users", "Applications", "Library", "System", "Volumes", "private", "Network", "cores", "data"
    };

    // ---- POSIX パス ----
    private static ProtectedSpan? TryPosixPath(string raw, int start)
    {
        // パスは語の先頭からのみ。a/b や 1/2 の途中の / を保護しない。
        if (start > 0 && !IsTokenBoundary(raw[start - 1])) return null;
        // ./ ../ ~/ と、先頭の / + パス要素
        if (start + 2 < raw.Length &&
            ((raw[start] == '.' && (raw[start + 1] == '/' || (raw[start + 1] == '.' && start + 2 < raw.Length && raw[start + 2] == '/')))
             || (raw[start] == '~' && raw[start + 1] == '/')))
        {
            var after = raw[start + 1] == '.' ? start + 3 : start + 2;
            if (after < raw.Length && IsPathElementStart(raw[after]))
            {
                var end = after;
                while (end < raw.Length && IsPathChar(raw[end])) end++;
                return new ProtectedSpan(start, end - start, ProtectedKind.PosixPath);
            }
            return null;
        }
        // 先頭の / (/// や単独の /、区切りの / の後ろの普通のローマ字語 /nihongo は対象外。2 つ目の / や記号・数字を含むパス、代表的なルートディレクトリを対象にする)
        if (raw[start] == '/' && start + 1 < raw.Length && IsPathElementStart(raw[start + 1]))
        {
            var end = start + 1;
            while (end < raw.Length && IsPathChar(raw[end])) end++;
            var segment = raw[(start + 1)..end];
            // 2 つ目以降の / や記号 (@, ., _ 等)・数字を含むパスは保護する (/home/user, /app.sh, /a@b, /.config など)
            if (segment.Any(c => !char.IsAsciiLetter(c) && c != '-'))
            {
                return new ProtectedSpan(start, end - start, ProtectedKind.PosixPath);
            }
            // 1 階層かつ英字・ハイフンのみの場合:
            // 実在する代表的なルートディレクトリ (/home, /tmp, /etc, /usr など) は保護する。
            if (CommonRootDirectories.Contains(segment))
            {
                return new ProtectedSpan(start, end - start, ProtectedKind.PosixPath);
            }
        }
        return null;
    }

    /// <summary>語の区切り (直前が識別子・パスの文字でない)。</summary>
    private static bool IsTokenBoundary(char previous) =>
        !(char.IsAsciiLetterOrDigit(previous) || previous is '.' or '_' or '-' or '~' or '/' or '\\' or '%' or '+' or '@');

    private static bool IsPathElementStart(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~';

    private static bool IsPathChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '~' or '/' or '\\' or '%' or '+' or '@';
}
