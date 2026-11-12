// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Composition;

/// <summary>ユーザー辞書の 1 語。Kind は品詞 (指定なしなら今までどおり、読みが見つかればどこでも使う)。</summary>
public sealed record UserWord(string Reading, string Word, UserWordKind Kind = UserWordKind.None);

/// <summary>
/// ユーザー辞書の語の品詞 (issue #487)。名詞・人名・地名は、読みの後ろが入力の終わりか助詞・敬称 (は が の さん …) のときだけ使う
/// (ちな → 千奈 を登録しても、ちなみに を 千奈ミニ にしない)。短縮よみは、入力の全体が読みと同じときだけ使う。
/// </summary>
public enum UserWordKind
{
    [System.ComponentModel.Description("指定なし")] None,
    [System.ComponentModel.Description("名詞")] Noun,
    [System.ComponentModel.Description("人名")] PersonName,
    [System.ComponentModel.Description("地名")] PlaceName,
    [System.ComponentModel.Description("短縮よみ")] Abbreviation,
}

/// <summary>品詞の名前 (userdict.txt の 3 列目・Microsoft IME と Google 日本語入力の品詞) との変換。</summary>
public static class UserWordKinds
{
    public static string Label(UserWordKind kind) => kind switch
    {
        UserWordKind.Noun => "名詞",
        UserWordKind.PersonName => "人名",
        UserWordKind.PlaceName => "地名",
        UserWordKind.Abbreviation => "短縮よみ",
        _ => "",
    };

    /// <summary>品詞の名前を読む。知らない名前・動詞などは「指定なし」(今までどおりどこでも使う)。</summary>
    public static UserWordKind Parse(string? label) => (label ?? "").Trim() switch
    {
        "名詞" or "固有名詞" or "組織" or "名詞サ変" or "名詞形動" => UserWordKind.Noun,
        "人名" or "姓" or "名" => UserWordKind.PersonName,
        "地名" => UserWordKind.PlaceName,
        "短縮よみ" => UserWordKind.Abbreviation,
        _ => UserWordKind.None,
    };

    /// <summary>
    /// ほかの日本語入力の辞書から取り込むときの品詞。人名・地名・短縮よみだけを品詞として読み、名詞・固有名詞などは「指定なし」にする
    /// (Microsoft IME はほとんどの語を名詞で書き出すので、名詞として扱うと、今まで使えていた所 (きごうとう|ふくめ) で使えなくなる)。
    /// </summary>
    public static UserWordKind ParseImported(string? label) =>
        Parse(label) is var kind and (UserWordKind.PersonName or UserWordKind.PlaceName or UserWordKind.Abbreviation) ? kind : UserWordKind.None;

    /// <summary>名詞・人名・地名の語の後ろに続いてよい語 (助詞・敬称・だ / です)。</summary>
    private static readonly string[] AfterNoun =
    [
        "は", "が", "を", "に", "で", "と", "も", "の", "へ", "や", "か", "ね", "よ", "な", "って", "から", "まで", "より", "だけ", "しか", "など", "こそ",
        "さん", "くん", "ちゃん", "さま", "様", "せんせい", "せんぱい", "たち", "ら", "だ", "です", "でし", "じゃ", "ちゃ", "けど", "し",
    ];

    /// <summary>読み reading が kana の start から見つかったとき、品詞 kind の語として使ってよいか。</summary>
    public static bool Fits(UserWordKind kind, string kana, int start, int length) => kind switch
    {
        UserWordKind.Abbreviation => start == 0 && length == kana.Length,
        UserWordKind.Noun or UserWordKind.PersonName or UserWordKind.PlaceName =>
            start + length == kana.Length || !KanaChars.IsHiraganaOrLongMark(kana[start + length]) ||
            AfterNoun.Any(after => kana.AsSpan(start + length).StartsWith(after, StringComparison.Ordinal)),
        _ => true,
    };
}

/// <summary>
/// ユーザー辞書 (%LOCALAPPDATA%\Meltype\userdict.txt、1 行に「読み[Tab]単語」)。
/// 変換で最優先に使う: 変換する読みの中に登録した読みが含まれていれば、その部分は変換エンジンの区切りに関係なく
/// 登録した単語にする (きごうとう → 記号等 を登録すると、きごうとうふくめ → 記号等|含め)。
/// トレイの「ユーザー辞書...」から登録・削除する。
/// importDirectory (Mac / Linux は データフォルダ/dictionaries) に置いた .plist (macOS の「ユーザ辞書」を書き出したもの) の語も使う (#40)。
/// .plist の語は userdict.txt には書かず、置いたファイルをそのまま読む。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "日本語入力の辞書を表す既存の公開名。コレクション型を表すものではない。")]
public sealed class UserDictionary
{
    /// <summary>登録・取り込みできる読みの最小の長さ。</summary>
    public const int MinRegisteredReadingLength = 1;

    /// <summary>
    /// 読みの途中に見つけて使える読みの最小の長さ。1 文字の読み (お → お父さん) は、ほかの語の中にも現れやすく巻き込みが大きいので、
    /// 変換する読みが 1 文字だけのとき (その文字だけを変換するとき) にだけ使う (issue #710)。
    /// </summary>
    private const int MinSplitReadingLength = 2;

    private readonly string? _path;
    private readonly List<UserWord> _words = [];
    // dictionaries/*.plist から読んだ語。userdict.txt の登録の後、同梱の語句の前。保存も表示もしない。
    private readonly List<UserWord> _imported = [];
    // 同梱の語句 (dictionaries/phrases.txt)。変換エンジンが苦手な語句を補う。ユーザーの登録より後回しで、保存も表示もしない。
    private readonly List<UserWord> _builtIn = [];
    // 同梱の語句のうち、読みの頭に ^ を付けたもの (^けんりつ)。変換エンジンの文節の頭から始まるときだけ使う。
    private readonly HashSet<string> _headOnly = new(StringComparer.Ordinal);
    // 同梱の語句のうち、| で文節の区切りを書いたもの (いち|がっき → 一|学期)。読みと語句は | を除いた形で _builtIn に入れる。
    private readonly Dictionary<string, (string Reading, string Word)[]> _clauses = new(StringComparer.Ordinal);
    private Dictionary<string, List<string>> _byReading = new(StringComparer.Ordinal);
    private int _maxReadingLength;
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
        if (builtIn)
        {
            Parse(Detection.DictionarySource.ReadEmbedded("phrases.txt").Split('\n'), _builtIn);
            MarkHeadOnly();
            SplitBuiltInClauses();
        }
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
            if (warn) Diagnostics.Log.Warn($"ユーザー辞書を読めませんでした: {ex.Message}");
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
        catch (Exception ex)
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
                Diagnostics.Log.Warn($"ユーザー辞書 {Path.GetFileName(file)} を読めませんでした: {ex.Message}");
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
                words.Add(new UserWord(parts[0].Trim(), parts[1].Trim(), parts.Length >= 3 ? UserWordKinds.Parse(parts[2]) : UserWordKind.None));
            }
        }
    }

    /// <summary>
    /// 同梱の語句の | (文節の区切り) を読む。いち|がっき → 一|学期 は、読み いちがっき を 一学期 として見つけ、変換では 一|学期 の 2 つの文節にする
    /// (数字と 学期 を別に選び直せるように)。読みと語句の | の数が違う行は、同梱の辞書の誤りなので例外にする
    /// (tools/check-dictionaries.mjs と BuiltInPhrases_AreSplitOut で見つける)。
    /// </summary>
    private void SplitBuiltInClauses()
    {
        for (var i = 0; i < _builtIn.Count; i++)
        {
            var (reading, word) = (_builtIn[i].Reading, _builtIn[i].Word);
            if (!reading.Contains('|') && !word.Contains('|')) continue;
            var readings = reading.Split('|');
            var words = word.Split('|');
            if (readings.Length != words.Length || readings.Any(r => r.Length == 0) || words.Any(w => w.Length == 0))
                throw new InvalidDataException($"phrases.txt: 読み「{reading}」と語句「{word}」の | の区切りが合いません");
            _builtIn[i] = new UserWord(reading.Replace("|", ""), word.Replace("|", ""));
            _clauses[_builtIn[i].Reading + "\t" + _builtIn[i].Word] = [.. readings.Zip(words)];
        }
    }

    /// <summary>
    /// 見つけた語を文節に分ける。同梱の語句に | で区切りを書いたもの (いちがっき → 一学期 は いち|がっき → 一|学期) は分けた文節、
    /// それ以外はそのまま 1 つの文節。
    /// </summary>
    public IReadOnlyList<(string Reading, string Word)> Clauses(string reading, string word) =>
        _clauses.TryGetValue(reading + "\t" + word, out var parts) ? parts : [(reading, word)];

    /// <summary>
    /// 同梱の語句の読みの頭の ^ を読む。^けんりつ は、変換エンジンの文節の頭から始まるときだけ使い、
    /// 長い語の途中 (はっけんりつ = 発見率 の けんりつ) では使わない (AlignUserWords)。
    /// </summary>
    private void MarkHeadOnly()
    {
        for (var i = 0; i < _builtIn.Count; i++)
        {
            if (!_builtIn[i].Reading.StartsWith('^')) continue;
            _builtIn[i] = _builtIn[i] with { Reading = _builtIn[i].Reading[1..] };
            _headOnly.Add(_builtIn[i].Reading.Replace("|", ""));
        }
    }

    /// <summary>変換エンジンの文節の頭から始まるときだけ使う同梱の語句の読みか (^けんりつ)。</summary>
    public bool IsHeadOnly(string reading) => _headOnly.Contains(reading);

    /// <summary>登録内容が変わるたびに増える (変換結果のキャッシュを捨てるため)。</summary>
    public int Version { get; private set; }

    public int Count => _words.Count;

    public IReadOnlyList<UserWord> Words => _words;

    /// <summary>登録する。同じ読み・同じ単語が既にあれば何もしない。登録できなければ理由を返す。</summary>
    public string? Add(string reading, string word, UserWordKind kind = UserWordKind.None)
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
        _words.Add(new UserWord(reading, word, kind));
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

    /// <summary>
    /// かなを、登録した読みの部分とそれ以外に分ける。先頭から見て、その位置から始まる最も長い登録済みの読みを取る。
    /// 登録した読みが 1 つも含まれていなければ null。
    /// </summary>
    public List<(string Reading, string? Word)>? Split(string kana)
    {
        if (_byReading.Count == 0 || kana.Length < MinRegisteredReadingLength) return null;
        var pieces = new List<(string Reading, string? Word)>();
        var plain = new StringBuilder();
        var found = false;
        var i = 0;
        while (i < kana.Length)
        {
            string? matched = null;
            for (var length = Math.Min(_maxReadingLength, kana.Length - i); length >= (kana.Length == 1 ? MinRegisteredReadingLength : MinSplitReadingLength); length--)
            {
                // 拗音・小さい母音 (じょ・ふぁ) の途中では切らない (いしょくじょうたい が しょくじ|ょうたい で い食事ょうたい になっていた)
                if (i + length < kana.Length && IsSmallKana(kana[i + length])) continue;
                // 品詞を決めた語は、読みの後ろに続くものが合うときだけ (ちな = 千奈 (人名) は ちなみに の中では使わない: issue #487)
                if (_byReading.ContainsKey(kana.Substring(i, length)) && UserWordKinds.Fits(_kinds.GetValueOrDefault(kana.Substring(i, length)), kana, i, length))
                {
                    matched = kana.Substring(i, length);
                    break;
                }
            }
            if (matched is null)
            {
                plain.Append(kana[i]);
                i++;
                continue;
            }
            if (plain.Length > 0)
            {
                pieces.Add((plain.ToString(), null));
                plain.Clear();
            }
            pieces.Add((matched, _byReading[matched][0]));
            found = true;
            i += matched.Length;
        }
        if (plain.Length > 0) pieces.Add((plain.ToString(), null));
        return found ? pieces : null;
    }

    /// <summary>前の文字と 1 つの音になる小さいかな (ゃ ゅ ょ ぁ ぃ ぅ ぇ ぉ ゎ)。</summary>
    private static bool IsSmallKana(char c) => c is 'ゃ' or 'ゅ' or 'ょ' or 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'ゎ';

    private void Changed()
    {
        Rebuild();
        Version++;
        Save();
    }

    private void Rebuild()
    {
        var byReading = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var kinds = new Dictionary<string, UserWordKind>(StringComparer.Ordinal);
        // 後から登録したものを先にする。.plist の語はユーザーの登録の後、同梱の語句はさらに後。
        foreach (var word in Enumerable.Reverse(_words).Concat(_imported).Concat(_builtIn))
        {
            if (!byReading.TryGetValue(word.Reading, out var list))
            {
                byReading[word.Reading] = list = [];
                // 読みの中に見つけたときに使うのは最初の語なので、その語の品詞で決める
                kinds[word.Reading] = word.Kind;
            }
            if (!list.Contains(word.Word)) list.Add(word.Word);
        }
        _kinds = kinds;
        _byReading = byReading;
        _maxReadingLength = byReading.Count == 0 ? 0 : byReading.Keys.Max(k => k.Length);
        _builtInOnly = _builtIn.Select(w => w.Reading).Except(_words.Concat(_imported).Select(w => w.Reading)).ToHashSet(StringComparer.Ordinal);
    }

    // 読みごとの、最初の語の品詞
    private Dictionary<string, UserWordKind> _kinds = new(StringComparer.Ordinal);

    // 同梱の語句だけにある読み (ユーザーが登録した語ではない)。
    private HashSet<string> _builtInOnly = new(StringComparer.Ordinal);

    /// <summary>ユーザーが登録した (取り込んだ) 語の読みか。同梱の語句だけの読みなら false。</summary>
    public bool IsUserReading(string reading) => _byReading.ContainsKey(reading) && !_builtInOnly.Contains(reading);

    private void Save()
    {
        if (_path is null) return;
        try
        {
            var lines = new List<string> { "# Meltype ユーザー辞書: 1 行に「読み<Tab>単語」(品詞を決めた語は「読み<Tab>単語<Tab>品詞」)" };
            lines.AddRange(_words.Select(w => w.Kind == UserWordKind.None ? $"{w.Reading}\t{w.Word}" : $"{w.Reading}\t{w.Word}\t{UserWordKinds.Label(w.Kind)}"));
            Config.DataFile.Write(_path, temp => File.WriteAllLines(temp, lines, new UTF8Encoding(true)));
            _fileTime = FileTime();
            _read = _fileTime is not null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"ユーザー辞書を保存できませんでした: {ex.Message}");
        }
    }
}
