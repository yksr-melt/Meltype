// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Libraries.Lexicon;

namespace Meltype.Input;

/// <summary>かな入力の配列 (issue #1415)。</summary>
public enum KanaLayout
{
    /// <summary>JIS かな配列 (キーボードの刻印どおり)。</summary>
    [System.ComponentModel.Description("JIS かな")] Jis,
    /// <summary>月配列 (U9。D・K の前置シフト)。</summary>
    [System.ComponentModel.Description("月配列")] Tsuki,
    /// <summary>花配列 (D・K の前置シフト)。</summary>
    [System.ComponentModel.Description("花配列")] Hana,
    /// <summary>NICOLA (親指シフト。無変換・変換が左右の親指キー)。</summary>
    [System.ComponentModel.Description("NICOLA (親指シフト)")] Nicola,
    /// <summary>中指 NICOLA (NICOLA に加え、K + 左手のキーで左シフト、D + 右手のキーで右シフト)。</summary>
    [System.ComponentModel.Description("中指NICOLA")] NakayubiNicola,
    /// <summary>TRON 配列 (親指シフト。無変換・変換が左右の親指キー)。</summary>
    [System.ComponentModel.Description("TRON 配列")] Tron,
}

/// <summary>ローマ字入力のキーの配列 (issue #1415)。</summary>
public enum RomajiLayout
{
    [System.ComponentModel.Description("QWERTY")] Qwerty,
    /// <summary>SKY 配列 (白鳥・小橋 1987 の基本配列。左手が子音、右手が母音・二重母音・撥音)。</summary>
    [System.ComponentModel.Description("SKY 配列")] Sky,
    [System.ComponentModel.Description("Dvorak")] Dvorak,
    [System.ComponentModel.Description("Colemak")] Colemak,
    [System.ComponentModel.Description("Workman")] Workman,
    [System.ComponentModel.Description("大西配列")] Ohnishi,
    [System.ComponentModel.Description("Eucalyn 配列")] Eucalyn,
}

/// <summary>SKY 配列: QWERTY の位置のキーを、ローマ字の綴りにする (issue #1415)。</summary>
internal static class SkyLayout
{
    // 二重母音・撥音のキーは、そのまま読めるローマ字にする (ん は nn。un + a が う + な にならないように)
    private static readonly Dictionary<int, string> Keys = new()
    {
        [0x51] = ",", [0x57] = "w", [0x45] = "r", [0x52] = "m", [0x54] = "h",
        [0x59] = "uu", [0x55] = "ai", [0x49] = "ou", [0x4F] = ".", [0x50] = "ei",
        [0x41] = "n", [0x53] = "t", [0x44] = "s", [0x46] = "k", [0x47] = "y",
        [0x48] = "u", [0x4A] = "a", [0x4B] = "o", [0x4C] = "i", [0xBB] = "e",
        [0x5A] = "p", [0x58] = "d", [0x43] = "z", [0x56] = "g", [0x42] = "b",
        [0x4E] = "unn", [0x4D] = "ann", [0xBC] = "onn", [0xBE] = "inn", [0xBF] = "enn",
    };

    /// <summary>そのキーのローマ字。SKY 配列のキーでなければ null。</summary>
    public static string? Romaji(int vk) => Keys.GetValueOrDefault(vk);
}

/// <summary>かな配列の 1 打鍵の結果。</summary>
/// <param name="Handled">配列のキーとして扱った (false ならふつうのキーとして処理する)。</param>
/// <param name="RemoveLast">先に入力中の文字から消す音の数 (後から押した親指キー・中指キーで、前の文字を打ち直すとき)。</param>
/// <param name="Kana">入力するかな (空なら何も足さない。前置シフトのキーなど)。</param>
internal readonly record struct KanaStroke(bool Handled, int RemoveLast = 0, string Kana = "")
{
    public static KanaStroke NotHandled => default;
    public static KanaStroke Consumed => new(true);
}

/// <summary>キーを離した結果。</summary>
/// <param name="ThumbTap">親指キーを文字に使わずに離した (親指キーのもとの働き: 変換・無変換をする)。</param>
/// <param name="Stroke">離したことで決まった打ち直し (中指NICOLA: シフトのキーを押したまま離した文字キーを、シフト面で打ち直す)。</param>
internal readonly record struct KanaKeyUp(bool ThumbTap, KanaStroke Stroke);

/// <summary>
/// JIS かな以外のかな配列で、打鍵をかなにする (issue #1415)。表は dictionaries/kana-layouts.txt (配列ごとの「打つキー → かな」)。
/// 打つキーの書き方で、読み方が 2 つある。
/// <list type="bullet">
/// <item>並び (d f): 前置シフト。打ったキーの並びが表の並びの途中なら、次のキーを待つ。</item>
/// <item>同時 (L+f): 押したままのキーと合わせて読む。押したままの親指キー (L・R) とはいつでも、
/// ほかのキーとは、押してから <see cref="ChordWindowMs"/> 以内に次のキーを押したとき。押したときはシフトなしの文字にしておき、
/// 組み合わせになったら打ち直す (文字キーの直後に押した親指キーも同時打鍵)。</item>
/// </list>
/// 中指NICOLA の D・K のシフトだけは、時間でなく離した順で決める: 文字キーを D・K より先に離したときだけシフトにする
/// (D・K を先に離したら、速く打って重なっただけ: て と は てと のまま)。
/// </summary>
internal sealed class KanaLayoutEngine(KanaLayout layout)
{
    /// <summary>同時打鍵とみなす時間 (ミリ秒)。</summary>
    public const int ChordWindowMs = 100;

    public const int LeftThumb = 0x1D, RightThumb = 0x1C;

    // 刻印 (表の打つキーの書き方) と仮想キー (日本語 106/109 キーボード)。数字の段・Q の段・A の段・Z の段
    private const string Names = "1234567890-^\\qwertyuiop@[asdfghjkl;:]zxcvbnm,./_";
    private static readonly int[] Keys =
    [
        0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0xBD, 0xDE, 0xDC,
        0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50, 0xC0, 0xDB,
        0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, 0xBB, 0xBA, 0xDD,
        0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D, 0xBC, 0xBE, 0xBF, 0xE2,
    ];

    private static readonly Lazy<Dictionary<KanaLayout, Table>> Tables = new(() => Table.Load(DictionarySource.ReadEmbedded("kana-layouts.txt")));

    /// <summary>1 つの配列の表。並びは「仮想キーを , でつないだもの」、同時は「押したままのキー (小さい順に +) → 文字キー」。</summary>
    private sealed class Table
    {
        public readonly Dictionary<string, string> Sequences = new(StringComparer.Ordinal);
        public readonly HashSet<string> Prefixes = new(StringComparer.Ordinal);
        public readonly Dictionary<(string Shifts, int Key), string> Chords = [];

        public static Dictionary<KanaLayout, Table> Load(string text)
        {
            var tables = new Dictionary<KanaLayout, Table>();
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var cells = line.TrimEnd('\r').Split('\t');
                var layout = Enum.Parse<KanaLayout>(cells[0]);
                if (!tables.TryGetValue(layout, out var table)) tables[layout] = table = new Table();
                if (cells[1].Contains('+'))
                {
                    var keys = cells[1].Split('+').Select(KeyOf).ToArray();
                    table.Chords[(Id(keys[..^1]), keys[^1])] = cells[2];
                    continue;
                }
                var sequence = string.Join(',', cells[1].Split(' ').Select(KeyOf));
                table.Sequences[sequence] = cells[2];
                for (var end = sequence.IndexOf(','); end > 0; end = sequence.IndexOf(',', end + 1)) table.Prefixes.Add(sequence[..end]);
            }
            return tables;
        }

        private static int KeyOf(string name) => name switch
        {
            "L" => LeftThumb,
            "R" => RightThumb,
            _ when name.Length == 1 && Names.IndexOf(name[0], StringComparison.Ordinal) is >= 0 and var index => Keys[index],
            _ => throw new FormatException($"kana-layouts.txt: 知らないキー {name}"),
        };
    }

    private static string Id(IEnumerable<int> keys) => string.Join('+', keys.Order());

    private readonly Table? _table = layout == KanaLayout.Jis ? null : Tables.Value[layout];

    /// <summary>押しているキー。Emitted はそのキーで入れて、まだ打ち直していない文字の数。</summary>
    private sealed class Held(int vk, long time, int emitted)
    {
        public int Vk { get; } = vk;
        public long Time { get; set; } = time;
        public int Emitted { get; set; } = emitted;
        /// <summary>組み合わせのシフトに使った (押している間は連続シフト)。</summary>
        public bool Shifting { get; set; }
        /// <summary>組み合わせの文字キーにした (シフトには使わない)。</summary>
        public bool Target { get; set; }
        /// <summary>中指NICOLA: D・K より先に離したら打ち直すかなと、そのシフトのキー。</summary>
        public (string Kana, Held Shift, int RemoveLast)? OnRelease { get; set; }
    }

    private readonly List<Held> _held = [];
    // 中指の打ち直しで、シフトキー自身の文字も消せるのは、その文字が直前の出力のときだけ。
    private Held? _lastOutput;
    // 前置シフトの並びの打ちかけ (仮想キーを , でつないだもの)
    private string _sequence = "";

    public KanaLayout Layout => layout;

    /// <summary>親指キーを押したままか (離すまで、離したことを受け取る必要がある)。</summary>
    public bool ThumbHeld => _held.Any(key => IsThumb(key.Vk));

    /// <summary>前置シフトのキーを押して、次のキーを待っているか。</summary>
    public bool HasPrefix => _sequence.Length > 0;

    private static bool IsThumb(int vk) => vk is LeftThumb or RightThumb;

    /// <summary>打ちかけ (前置シフト・同時打鍵の打ち直し) を忘れる。入力を確定・取り消したときに呼ぶ。押したままの親指キーは離すまで覚えておく。</summary>
    public void Reset()
    {
        _sequence = "";
        _lastOutput = null;
        _held.RemoveAll(key => !IsThumb(key.Vk));
    }

    /// <summary>キーを押した。shift は Shift キー (小指シフト) を押しているか。Shift を押したキーはふつうの英字・記号。</summary>
    public KanaStroke Down(int vk, bool shift, long time)
    {
        // 表の段のキーと親指キーのほか (BackSpace・Enter・Esc・Space など) は、ふつうのキー
        if (_table is null || shift || Array.IndexOf(Keys, vk) < 0 && !IsThumb(vk)) return KanaStroke.NotHandled;
        if (_held.FirstOrDefault(key => key.Vk == vk) is { } repeated)
        {
            // シフト自身のリピートは止める。文字の長押しは押したままの親指の面で繰り返す。
            if (IsThumb(vk) || repeated.Shifting || layout == KanaLayout.NakayubiNicola && vk is 0x44 or 0x4B) return KanaStroke.Consumed;
            foreach (var key in _held) key.OnRelease = null;
            var thumb = _held.FirstOrDefault(key => IsThumb(key.Vk));
            var kana = thumb is not null ? _table.Chords.GetValueOrDefault(($"{thumb.Vk}", vk)) : _table.Sequences.GetValueOrDefault($"{vk}");
            repeated.Time = time;
            repeated.Emitted = kana?.Length ?? 0;
            repeated.Target = thumb is not null;
            _lastOutput = repeated;
            return kana is null ? KanaStroke.Consumed : new KanaStroke(true, 0, kana);
        }
        return _table.Chords.Count > 0 ? Chord(vk, time) : Sequence(vk);
    }

    private KanaStroke Sequence(int vk)
    {
        var sequence = _sequence.Length == 0 ? $"{vk}" : $"{_sequence},{vk}";
        _sequence = "";
        if (_table!.Prefixes.Contains(sequence))
        {
            _sequence = sequence;
            return KanaStroke.Consumed;
        }
        if (_table.Sequences.TryGetValue(sequence, out var kana)) return new KanaStroke(true, 0, kana);
        // 前置シフトの次の、表に無いキーは何も入れない。前置シフトでもないキーはふつうのキー
        return sequence.Contains(',') ? KanaStroke.Consumed : KanaStroke.NotHandled;
    }

    private KanaStroke Chord(int vk, long time)
    {
        var table = _table!;
        if (IsThumb(vk))
        {
            // 左右が重なっても、最後に押した親指の面を使う。交代した親指の解放は単独タップにしない。
            _held.RemoveAll(key => IsThumb(key.Vk));
            var thumb = new Held(vk, time, 0);
            _held.Add(thumb);
            // 文字キーの直後 (押したまま ChordWindowMs 以内) の親指キー: その文字を打ち直す
            if (_lastOutput is { Target: false, Shifting: false, Emitted: > 0 } last && _held.Contains(last) && time - last.Time <= ChordWindowMs &&
                table.Chords.TryGetValue(($"{vk}", last.Vk), out var shifted))
            {
                var remove = last.Emitted;
                last.Emitted = shifted.Length;
                last.Target = true;
                thumb.Shifting = true;
                return new KanaStroke(true, remove, shifted);
            }
            return table.Chords.Keys.Any(chord => chord.Shifts == $"{vk}") ? KanaStroke.Consumed : KanaStroke.NotHandled;
        }
        // 押したままの親指キー: その面の文字 (無ければ何も入れない)
        if (_held.FirstOrDefault(key => IsThumb(key.Vk)) is { } held)
        {
            held.Shifting = true;
            var thumbKana = table.Chords.GetValueOrDefault(($"{held.Vk}", vk));
            _lastOutput = new Held(vk, time, thumbKana?.Length ?? 0) { Target = true };
            _held.Add(_lastOutput);
            return thumbKana is not null ? new KanaStroke(true, 0, thumbKana) : KanaStroke.Consumed;
        }
        var plain = table.Sequences.GetValueOrDefault($"{vk}");
        // 中指NICOLA: D・K を押したままの文字キーは、ふつうの文字にしておき、先に離したら打ち直す
        if (layout == KanaLayout.NakayubiNicola)
        {
            // 次の文字が入ったら、前の打ち直しはもう末尾ではない。古い削除数で別の文字を消さない。
            foreach (var key in _held) key.OnRelease = null;
            // 中指のシフトは最初に押した D・K。重なった文字キーをシフトに選び直さない。
            var shift = _held.FirstOrDefault(key => key.Vk is 0x44 or 0x4B && !key.Target);
            var press = new Held(vk, time, plain?.Length ?? 0) { Target = vk is 0x44 or 0x4B && shift is not null };
            if (shift is not null && table.Chords.TryGetValue(($"{shift.Vk}", vk), out var middle))
                press.OnRelease = (middle, shift, press.Emitted + (ReferenceEquals(_lastOutput, shift) ? shift.Emitted : 0));
            _held.Add(press);
            _lastOutput = press;
            return plain is null ? KanaStroke.NotHandled : new KanaStroke(true, 0, plain);
        }
        if (plain is null) return KanaStroke.NotHandled;
        _lastOutput = new Held(vk, time, plain.Length);
        _held.Add(_lastOutput);
        return new KanaStroke(true, 0, plain);
    }

    /// <summary>キーを離した。</summary>
    public KanaKeyUp Up(int vk)
    {
        var index = _held.FindIndex(key => key.Vk == vk);
        if (index < 0) return default;
        var held = _held[index];
        _held.RemoveAt(index);
        // 中指NICOLA: D・K を押したまま、文字キーを先に離した → シフト面で打ち直す (文字キーと、まだなら D・K の文字を消す)
        if (held.OnRelease is ({ } kana, { } shift, var remove) && _held.Contains(shift))
        {
            shift.Emitted = 0;
            shift.Shifting = true;
            return new KanaKeyUp(false, new KanaStroke(true, remove, kana));
        }
        return new KanaKeyUp(IsThumb(vk) && !held.Shifting, KanaStroke.NotHandled);
    }
}
