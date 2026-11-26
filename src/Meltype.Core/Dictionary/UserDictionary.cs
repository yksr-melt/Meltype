// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Libraries.Lexicon;

namespace Meltype.Dictionary;

/// <summary>
/// ユーザー辞書の 1 語。品詞 (Pos) は変換エンジン (Mozc) に渡し、活用 (動詞・形容詞) とコストに使う。品詞の名前は、Mozc の品詞名か、
/// 取り込んだ辞書 (Microsoft IME・Google 日本語入力・ATOK) の品詞名のまま持ち、Mozc が自分の品詞に読み替える。
/// </summary>
public sealed record UserWord(string Reading, string Word, string Pos = UserWord.Noun)
{
    /// <summary>品詞を書いていない語の品詞。</summary>
    public const string Noun = "名詞";
}

/// <summary>
/// ユーザー辞書 (%LOCALAPPDATA%\Meltype\userdict.txt、1 行に「読み[Tab]単語」か「読み[Tab]単語[Tab]品詞」。品詞が無ければ名詞)。
/// 登録した語と同梱の語句 (phrases.txt) は変換エンジン (Mozc) のユーザー辞書に渡し (<see cref="EngineWords"/>)、ほかの語との取り合いと
/// 文節の区切りを Mozc のコストで決める。
/// トレイの「ユーザー辞書...」から登録・削除する。
/// importDirectory (Mac / Linux は データフォルダ/dictionaries) に置いた .plist (macOS の「ユーザ辞書」を書き出したもの) の語も使う (#40)。
/// .plist の語は userdict.txt には書かず、置いたファイルをそのまま読む。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "日本語入力の辞書を表す既存の公開名。コレクション型を表すものではない。")]
public sealed class UserDictionary
{
    /// <summary>登録・取り込みできる読みの最小の長さ。</summary>
    public const int MinRegisteredReadingLength = 1;

    private readonly string? _path;
    private readonly List<UserWord> _words = [];
    // dictionaries/*.plist から読んだ語。userdict.txt の登録の後、同梱の語句の前。保存も表示もしない。
    private readonly List<UserWord> _imported = [];
    // 同梱の語句 (dictionaries/phrases.txt)。ユーザーの登録より後回しで、保存も表示もしない。
    private readonly List<UserWord> _builtIn = [];
    // すべての語 (Lookup)
    private Dictionary<string, List<string>> _byReading = new(StringComparer.Ordinal);
    private readonly string? _importDirectory;
    // 最後に読んだ (読もうとした)・保存したときの userdict.txt の更新時刻と、読めたか。同じ更新時刻のまま読めなければ、読み直さない (警告を繰り返さない)
    private DateTime? _fileTime;
    private bool _read;
    // 読んだときの .plist の一覧 (更新時刻つき)
    private string _plists = "";

    public UserDictionary(string? path, bool builtIn = true, string? importDirectory = null)
    {
        _path = path;
        _importDirectory = importDirectory;
        if (builtIn) Parse(DictionarySource.ReadEmbedded("phrases.txt").Split('\n'), _builtIn);
        Load(FileTime(), ReadWords(warn: true));
    }

    /// <summary>
    /// userdict.txt・.plist がこのオブジェクトの外 (Mac の Swift 側の取り込み・.plist を置いた) で変わっていたら、読み直す。
    /// 入力欄はすべてこのオブジェクトを使うので、開いている入力欄にも効く。
    /// userdict.txt の今の中身を持っていると確かめられなければ (更新時刻・中身を読めない)、前の語のまま false を返す。
    /// </summary>
    public bool Refresh()
    {
        if (FileTime() is not { } fileTime) return false;
        if (_read && fileTime == _fileTime && Plists() == _plists) return true;
        // 読めなかったときと同じ更新時刻なら、警告を繰り返さずに読み直す (ほかのソフトが閉じれば読める)
        if (ReadWords(warn: _read || fileTime != _fileTime) is not { } words)
        {
            _fileTime = fileTime;
            _read = false;
            return false;
        }
        Load(fileTime, words);
        Version++;
        WordsChanged?.Invoke();
        Diagnostics.Log.Info("ユーザー辞書が変わったので読み直しました。");
        return true;
    }

    // 登録・削除の前に Refresh が false なら: 保存すると読めなかった語が消えるので変えない
    private const string Unreadable = "ユーザー辞書 (userdict.txt) を読めないので、変えられません。ファイルを開いているほかのソフトを閉じてから、やり直してください。";

    // words が null (userdict.txt を読めなかった) なら、前に読んだ語のまま
    private void Load(DateTime? fileTime, List<UserWord>? words)
    {
        _fileTime = fileTime;
        _read = fileTime is not null && words is not null;
        if (words is not null)
        {
            _words.Clear();
            _words.AddRange(words);
        }
        _plists = Plists();
        _imported.Clear();
        if (_importDirectory is not null) LoadPlists(_importDirectory);
        Rebuild();
    }

    // 読めなければ null
    private List<UserWord>? ReadWords(bool warn)
    {
        List<UserWord> words = [];
        try
        {
            if (_path is not null) Parse(File.ReadAllLines(_path, Encoding.UTF8), words);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // まだ登録していない
        }
        catch (Exception ex)
        {
            // 利用者が書いた中身から出る例外は型で分けられない (Config.DataFile)
            if (warn) Config.DataFile.ReportUnreadable("ユーザー辞書", ex);
            return null;
        }
        return words;
    }

    // 読めなければ null (変わったか分からない)
    private DateTime? FileTime()
    {
        try
        {
            return _path is null ? default : File.GetLastWriteTimeUtc(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string Plists()
    {
        try
        {
            if (_importDirectory is null || !Directory.Exists(_importDirectory)) return "";
            var files = Directory.GetFiles(_importDirectory, "*.plist");
            Array.Sort(files, StringComparer.Ordinal);
            return string.Join("\n", files.Select(f => $"{f}\t{File.GetLastWriteTimeUtc(f).Ticks}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _plists;
        }
    }

    /// <summary>フォルダの .plist を読む。読めないファイル (バイナリの plist など) は飛ばす。</summary>
    private void LoadPlists(string directory)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(directory)) return;
            files = Directory.GetFiles(directory, "*.plist");
        }
        catch (Exception ex) when (Config.DataFile.IsFileError(ex))
        {
            Diagnostics.Log.Warn($"ユーザー辞書のフォルダを読めませんでした: {ex.Message}");
            return;
        }
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            try
            {
                var result = UserDictionaryFile.ParsePlist(File.ReadAllBytes(file));
                _imported.AddRange(result.Words);
                Diagnostics.Log.Info($"ユーザー辞書 {Path.GetFileName(file)}: {result.Words.Count} 語 (飛ばした項目: {result.Skipped})");
            }
            catch (Exception ex)
            {
                // 利用者が置いた plist の中身から出る例外は型で分けられない (Config.DataFile)
                Config.DataFile.ReportUnreadable($"ユーザー辞書 {Path.GetFileName(file)} ", ex);
            }
        }
    }

    /// <summary>.plist から読んだ語の数 (userdict.txt の語とは別)。</summary>
    public int ImportedCount => _imported.Count;

    private static void Parse(IEnumerable<string> lines, List<UserWord> words)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith('#')) continue;
            var parts = line.TrimEnd('\r').Split('\t');
            // 制御文字 (ESC など) の入った語は、確定するとアプリにキー操作として届くので読まない
            if (parts.Length >= 2 && parts[0].Trim().Length >= MinRegisteredReadingLength && parts[1].Trim().Length > 0 && !parts[0].Any(char.IsControl) && !parts[1].Any(char.IsControl))
            {
                words.Add(new UserWord(parts[0].Trim(), parts[1].Trim(), parts.Length >= 3 && parts[2].Trim() is { Length: > 0 } pos ? pos : UserWord.Noun));
            }
        }
    }

    /// <summary>登録内容が変わるたびに増える。</summary>
    public int Version { get; private set; }

    /// <summary>登録した語 (userdict.txt・.plist) が変わった。変えたスレッド (入力・UI のスレッド) で呼ぶので、受け手は待たない。</summary>
    public event Action? WordsChanged;

    /// <summary>
    /// 変換エンジンに渡す語 (userdict.txt・.plist の語と同梱の語句。読みと語が同じなら先のもの) と、その <see cref="Version"/>。
    /// ユーザーが同じ読みを登録した同梱の語句は渡さない (いちがっき → 壱学期 を登録したら 一学期 と取り合わせない)。
    /// </summary>
    public (int Version, IReadOnlyList<UserWord> Words) EngineWords()
    {
        var user = _words.Concat(_imported).ToList();
        var readings = user.Select(w => w.Reading).ToHashSet(StringComparer.Ordinal);
        return (Version, [.. user.Concat(_builtIn.Where(w => !readings.Contains(w.Reading))).DistinctBy(w => (w.Reading, w.Word))]);
    }

    public int Count => _words.Count;

    public IReadOnlyList<UserWord> Words => _words;

    /// <summary>登録する。同じ読み・同じ単語が既にあれば何もしない。登録できなければ理由を返す。</summary>
    public string? Add(string reading, string word)
    {
        reading = reading.Trim();
        word = word.Trim();
        if (reading.Length < MinRegisteredReadingLength) return "読みを入力してください。";
        if (word.Length == 0) return "単語を入力してください。";
        // CR・ESC などは確定するとアプリにキー操作として届く
        if (reading.Any(char.IsControl) || word.Any(char.IsControl)) return "タブ・改行などの制御文字は使えません。";
        // 外で書き換えた語の上に足す
        if (!Refresh()) return Unreadable;
        if (_words.Any(w => w.Reading == reading && w.Word == word)) return null;
        _words.Add(new UserWord(reading, word));
        Changed();
        return null;
    }

    /// <summary>まとめて登録する (取り込み)。読みと単語が同じものが既にあれば飛ばす。登録した数を返す。userdict.txt を読めなければ IOException。</summary>
    public int AddRange(IEnumerable<UserWord> words)
    {
        if (!Refresh()) throw new IOException(Unreadable);
        var added = 0;
        foreach (var word in words)
        {
            if (word.Reading.Length < MinRegisteredReadingLength || word.Word.Length == 0 || word.Reading.Any(char.IsControl) || word.Word.Any(char.IsControl)) continue;
            if (_words.Any(w => w.Reading == word.Reading && w.Word == word.Word)) continue;
            _words.Add(word);
            added++;
        }
        if (added > 0) Changed();
        return added;
    }

    /// <summary>消す。userdict.txt を読めなければ IOException。</summary>
    public void Remove(UserWord word)
    {
        if (!Refresh()) throw new IOException(Unreadable);
        if (_words.Remove(word)) Changed();
    }

    /// <summary>読みに登録されている単語 (新しく登録したものが先)。</summary>
    public IReadOnlyList<string> Lookup(string reading) =>
        _byReading.TryGetValue(reading, out var words) ? words : [];

    private void Changed()
    {
        Rebuild();
        Version++;
        Save();
        WordsChanged?.Invoke();
    }

    private void Rebuild()
    {
        // 後から登録したものを先にする。.plist の語はユーザーの登録の後、同梱の語句はさらに後。
        _byReading = ByReading(Enumerable.Reverse(_words).Concat(_imported).Concat(_builtIn));
    }

    private static Dictionary<string, List<string>> ByReading(IEnumerable<UserWord> words)
    {
        var byReading = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            if (!byReading.TryGetValue(word.Reading, out var list)) byReading[word.Reading] = list = [];
            if (!list.Contains(word.Word)) list.Add(word.Word);
        }
        return byReading;
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            var lines = new List<string> { "# Meltype ユーザー辞書: 1 行に「読み<Tab>単語」か「読み<Tab>単語<Tab>品詞」(品詞が無ければ名詞)" };
            // 名詞は品詞を書かない (前の版と、macOS の取り込み (MacUserDictionary.swift) が書く 2 列の行と同じ形にする)
            lines.AddRange(_words.Select(w => w.Pos == UserWord.Noun ? $"{w.Reading}\t{w.Word}" : $"{w.Reading}\t{w.Word}\t{w.Pos}"));
            Config.DataFile.Write(_path, temp => File.WriteAllLines(temp, lines, new UTF8Encoding(true)));
            _fileTime = FileTime();
            _read = _fileTime is not null;
        }
        catch (Exception ex) when (Config.DataFile.IsFileError(ex))
        {
            Diagnostics.Log.Warn($"ユーザー辞書を保存できませんでした: {ex.Message}");
        }
    }
}
