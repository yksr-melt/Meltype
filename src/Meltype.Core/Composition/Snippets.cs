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
        if (path is null || !File.Exists(path)) return;
        try
        {
            foreach (var (name, text) in Parse(File.ReadAllText(path))) _snippets[name] = text;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"定型文を読めませんでした: {ex.Message}");
        }
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

    /// <summary>登録する (同じ名前なら置き換える)。名前に使えない文字があるか、文が空なら false。</summary>
    public bool Set(string name, string text)
    {
        if (!IsValidName(name) || text.Length == 0) return false;
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
            if (IsValidName(name) && !string.IsNullOrEmpty(text)) yield return (name, text.ReplaceLineEndings("\n"));
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, Export(), Encoding.UTF8);
            File.Move(temp, _path, overwrite: true);
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
/// for(;i&lt;n;i++)・a;b の ; は前が空白ではないので対象にしない。アプリに届いた文字を追いかけて、直前が先頭・空白かを覚えておく
/// (/ $ @ の <see cref="SigilWord"/> と同じ考え方)。フックのスレッドと UI スレッドの両方から呼ばれる。
/// </summary>
public sealed class SnippetTrigger
{
    private readonly object _gate = new();
    private bool? _boundary = true;
    // 記号から打っている名前 (記号を含まない)。null なら名前の途中ではない。
    private StringBuilder? _name;

    /// <summary>名前の前に付ける記号 (設定。空なら定型文を使わない)。</summary>
    public string Mark { get; set; } = ";";

    /// <summary>記号から始めた名前を打っている途中か。</summary>
    public bool IsActive { get { lock (_gate) return _name is not null; } }

    /// <summary>打っている途中の名前 (記号を含まない)。名前の途中でなければ null。</summary>
    public string? Name { get { lock (_gate) return _name?.ToString(); } }

    /// <summary>
    /// 変換ボックスが空のときに打った文字を、変換せずにそのままアプリへ渡すか。渡した文字は <see cref="Append"/> で伝える
    /// (ここでは名前を溜めない)。before はホストが教えてくれたキャレットの前の文字列 (分からなければ null)。
    /// </summary>
    public bool PassesThrough(char c, string? before = null)
    {
        lock (_gate)
        {
            if (_name is not null) return SnippetStore.IsNameChar(c);
            if (Mark is not [var mark] || c != mark) return false;
            if (!string.IsNullOrEmpty(before)) _boundary = char.IsWhiteSpace(before[^1]);
            else if (before is not null && _boundary is null) _boundary = true;
            return _boundary == true;
        }
    }

    /// <summary>
    /// Space / Tab を押したとき。名前が登録したものなら、消す文字数 (記号 + 名前) と入れる文を返し、名前の途中を終える。
    /// 一致しなければ null (Space / Tab はそのままアプリへ)。
    /// </summary>
    public (int Delete, string Text, string Typed)? Expand(SnippetStore store)
    {
        lock (_gate)
        {
            if (_name is not { Length: > 0 } name || store.Get(name.ToString()) is not { } text) return null;
            var typed = Mark + name;
            _name = null;
            _boundary = false;
            return (typed.Length, text, typed);
        }
    }

    /// <summary>アプリに届いた文字列 (そのまま渡した打鍵の文字・変換ボックスで確定した文字列)。</summary>
    public void Append(string text)
    {
        lock (_gate)
        {
            foreach (var c in text)
            {
                if (_name is not null && SnippetStore.IsNameChar(c))
                {
                    _name.Append(c);
                    continue;
                }
                if (_name is null && Mark is [var mark] && c == mark && _boundary == true)
                {
                    _name = new StringBuilder();
                    _boundary = false;
                    continue;
                }
                _name = null;
                _boundary = char.IsWhiteSpace(c);
            }
        }
    }

    /// <summary>アプリに届いたキー (修飾キーと一緒のキー・キャレットを動かすキーは <see cref="Lose"/>)。</summary>
    public void OnKey(int vk, char? ch)
    {
        switch (vk)
        {
            case Input.VirtualKeys.Return: Start(); return;
            case Input.VirtualKeys.Back: Backspace(); return;
            case Input.VirtualKeys.Space: Append(" "); return;
            case Input.VirtualKeys.Escape: return;
            case Input.VirtualKeys.Tab or (>= 0x21 and <= 0x28) or 0x2E: Lose(); return;
        }
        if (Input.VirtualKeys.IsModifier(vk)) return;
        if (ch is { } c && !char.IsControl(c)) Append(c.ToString());
    }

    /// <summary>BackSpace がアプリに届いた。名前の途中なら 1 文字戻し、記号まで消したら記号の前 (先頭・空白) に戻る。</summary>
    public void Backspace()
    {
        lock (_gate)
        {
            if (_name is null)
            {
                _boundary = null;
                return;
            }
            if (_name.Length > 0) _name.Length--;
            else
            {
                _name = null;
                _boundary = true;
            }
        }
    }

    /// <summary>Enter (新しい行) や、別の入力欄に移ったとき。次の文字は先頭とみなす。</summary>
    public void Start()
    {
        lock (_gate)
        {
            _boundary = true;
            _name = null;
        }
    }

    /// <summary>入力欄のフォーカスが移った。次に打つ文字は先頭とみなす (名前の途中なら続ける: 補完の一覧にフォーカスが移るアプリがある)。</summary>
    public void FocusMoved()
    {
        lock (_gate)
        {
            if (_name is null) _boundary = true;
        }
    }

    /// <summary>キャレットが動いた (矢印・クリック・ショートカット) ので、前の文字が分からない。</summary>
    public void Lose()
    {
        lock (_gate)
        {
            _boundary = null;
            _name = null;
        }
    }
}
