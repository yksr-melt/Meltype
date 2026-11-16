// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using System.Text.Json;

namespace Meltype.Composition;

/// <summary>
/// ユーザーが登録した定型文 (issue #290)。;名前 + Space / Tab で、登録した文 (複数行でもよい) に置き換える。
/// %LOCALAPPDATA%\Meltype\snippets.json に「名前 → 文」で保存する (ユーザー辞書・学習データと同じく PC の外に出さない)。
/// 書き出し・取り込みも同じ形式の JSON (別の PC に移す・チームで同じひな形を配る)。
/// </summary>
public sealed class SnippetStore
{
    private readonly string? _path;
    private readonly Dictionary<string, string> _snippets = new(StringComparer.Ordinal);
    // フックのスレッド (展開) と UI スレッド (編集の画面) の両方から使う
    private readonly object _gate = new();

    public SnippetStore(string? path)
    {
        _path = path;
        Config.DataFile.Load(path, "定型文", content =>
        {
            foreach (var (name, text) in Parse(content)) _snippets[name] = text;
        });
    }

    public int Count { get { lock (_gate) return _snippets.Count; } }

    /// <summary>名前に使える文字: 空白・; 以外の半角の英数字・記号 (sig、ty、pr-template)。</summary>
    public static bool IsNameChar(char c) => c is > ' ' and <= '~' && c != ';';

    public static bool IsValidName(string name) => name.Length > 0 && name.All(IsNameChar);

    public string? Get(string name) { lock (_gate) return _snippets.TryGetValue(name, out var text) ? text : null; }

    /// <summary>登録した定型文 (名前の順)。</summary>
    public IReadOnlyList<(string Name, string Text)> Entries()
    {
        lock (_gate) return _snippets.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => (s.Key, s.Value)).ToList();
    }

    /// <summary>文に使えるか。改行・タブ以外の制御文字 (ESC・Ctrl+U など) は、入れるとアプリにキー操作として届くので使えない。</summary>
    public static bool IsValidText(string text) => text.Length > 0 && !text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));

    /// <summary>登録する (同じ名前なら置き換える)。名前に使えない文字があるか、文が空か、文に制御文字があれば false。</summary>
    public bool Set(string name, string text)
    {
        if (!IsValidName(name) || !IsValidText(text)) return false;
        lock (_gate) _snippets[name] = text.ReplaceLineEndings("\n");
        Save();
        return true;
    }

    public void Remove(string name)
    {
        bool removed;
        lock (_gate) removed = _snippets.Remove(name);
        if (removed) Save();
    }

    /// <summary>書き出す形式 (取り込みと同じ JSON)。</summary>
    public string Export()
    {
        Dictionary<string, string> sorted;
        lock (_gate) sorted = _snippets.OrderBy(s => s.Key, StringComparer.Ordinal).ToDictionary(s => s.Key, s => s.Value);
        return JsonSerializer.Serialize(sorted, Config.SnippetJsonContext.Default.Snippets);
    }

    /// <summary>書き出したファイルの中身を取り込む。同じ名前は取り込んだ方にする。取り込んだ数を返す。</summary>
    public int Import(string json)
    {
        var parsed = Parse(json).ToList();
        lock (_gate) foreach (var (name, text) in parsed) _snippets[name] = text;
        if (parsed.Count > 0) Save();
        return parsed.Count;
    }

    private static IEnumerable<(string Name, string Text)> Parse(string json)
    {
        var loaded = JsonSerializer.Deserialize(json, Config.SnippetJsonContext.Default.Snippets) ?? [];
        foreach (var (name, text) in loaded)
            if (IsValidName(name) && text is not null && IsValidText(text)) yield return (name, text.ReplaceLineEndings("\n"));
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Config.DataFile.Write(_path, temp => File.WriteAllText(temp, Export(), Encoding.UTF8));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"定型文を保存できませんでした: {ex.Message}");
        }
    }
}

/// <summary>
/// ;名前 を見分ける (issue #290)。入力欄・行の先頭か空白の直後に打った記号 (既定 ;) と、続く名前は変換せずにそのままアプリへ渡し、
/// Space / Tab を押したときに名前が登録したものなら、;名前 を消して定型文を入れる (一致しなければ何もしない)。
/// for(;i&lt;n;i++)・a;b の ; は前が空白ではないので対象にしない。どれもキャレットの前の文字 (before) だけで決める。
/// </summary>
public static class SnippetTrigger
{
    /// <summary>キャレットの前に c を打つと、;名前 の途中になるか (変換せずにアプリへ渡す)。</summary>
    public static bool PassesThrough(string mark, string before, char c) => IsTyped(mark, Word(before) + c);

    /// <summary>キャレットの前が ;名前 で終わるなら、その ;名前。</summary>
    public static string? TypedBefore(string mark, string before) => Word(before) is { Length: > 1 } word && IsTyped(mark, word) ? word : null;

    private static bool IsTyped(string mark, string word) =>
        mark is [var m] && word.Length > 0 && word[0] == m && word.Skip(1).All(SnippetStore.IsNameChar);

    // 空白の後ろ (無ければ先頭から)
    private static string Word(string before) => before[(before.LastIndexOfAny([' ', '\t', '\n', '\r', '\u3000']) + 1)..];
}
