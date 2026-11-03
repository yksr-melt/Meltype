// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

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
}

/// <summary>SKY 配列: QWERTY の位置のキーを、ローマ字の綴りにする (issue #1415)。</summary>
public static class SkyLayout
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
public readonly record struct KanaStroke(bool Handled, int RemoveLast = 0, string Kana = "")
{
    public static KanaStroke NotHandled => default;
    public static KanaStroke Consumed => new(true);
}

/// <summary>キーを離した結果。</summary>
/// <param name="ThumbTap">親指キーを文字に使わずに離した (親指キーのもとの働き: 変換・無変換をする)。</param>
/// <param name="Stroke">離したことで決まった打ち直し (中指NICOLA: シフトのキーを押したまま離した文字キーを、シフト面で打ち直す)。</param>
public readonly record struct KanaKeyUp(bool ThumbTap, KanaStroke Stroke);

/// <summary>
/// JIS かな以外のかな配列 (月・花・NICOLA・中指NICOLA・TRON) で、打鍵をかなにする (issue #1415)。
/// 前置シフト (月・花) は、シフトのキーを押した次の 1 打鍵をシフト面で読む。
/// 親指シフト (NICOLA・TRON) は、親指キー (左 = 無変換、右 = 変換) を押している間の文字キー、または文字キーを押してから
/// <see cref="ChordWindowMs"/> 以内に押した親指キーで、その文字をシフト面で読む (同時打鍵)。
/// 中指NICOLA は、さらに K を押したままの左手のキーを左シフト面、D を押したままの右手のキーを右シフト面で読む。
/// ただし文字キーを、D・K より先に離したときだけ (D・K を先に離したら、速く打って重なっただけ: て と は てと のまま)。
/// 配列の表は benizara (MIT License, k-ayaki) の定義ファイルから取った。
/// </summary>
public sealed class KanaLayoutEngine(KanaLayout layout)
{
    /// <summary>文字キーの後に親指キーを押したとき、同時打鍵とみなす時間 (ミリ秒)。</summary>
    public const int ChordWindowMs = 100;

    public const int LeftThumb = 0x1D, RightThumb = 0x1C;
    private const int KeyD = 0x44, KeyK = 0x4B;

    // 表の行 (数字の段・Q の段・A の段・Z の段) の仮想キー (日本語 106/109 キーボード)。
    private static readonly int[][] Rows =
    [
        [0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0xBD, 0xDE, 0xDC],
        [0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50, 0xC0, 0xDB],
        [0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, 0xBB, 0xBA, 0xDD],
        [0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D, 0xBC, 0xBE, 0xBF, 0xE2],
    ];

    // 配列ごとの面。前置シフトの配列は [シフトなし, 1 のシフト, 2 のシフト]、親指シフトの配列は [シフトなし, 左, 右]。
    // 表の中の "1" "2" は前置シフトのキー。
    private static readonly Dictionary<KanaLayout, string[][]> Tables = new()
    {
        [KanaLayout.Nicola] = [
            [
                ",,,,,,,,,,ー,,",
                "。,か,た,こ,さ,ら,ち,く,つ,、,、,゛",
                "う,し,て,け,せ,は,と,き,い,ん,,",
                "。,ひ,す,ふ,へ,め,そ,ね,ほ,・,",
            ],
            [
                ",・,,,,,,,,,,,",
                "ぁ,え,り,ゃ,れ,ぱ,ぢ,ぐ,づ,ぴ,,゜",
                "を,あ,な,ゅ,も,ば,ど,ぎ,ぽ,,,",
                "ぅ,ー,ろ,や,ぃ,ぷ,ぞ,ぺ,ぼ,,",
            ],
            [
                ",・,,,,,,,,,,,",
                ",が,だ,ご,ざ,よ,に,る,ま,ぇ,,゜",
                "ゔ,じ,で,げ,ぜ,み,お,の,ょ,っ,,",
                ",び,ず,ぶ,べ,ぬ,ゆ,む,わ,ぉ,",
            ],
        ],
        [KanaLayout.NakayubiNicola] = [
            [
                ",,,,,,,,,,ー,,",
                "。,か,た,こ,さ,ら,ち,く,つ,、,、,゛",
                "う,し,て,け,せ,は,と,き,い,ん,,",
                "。,ひ,す,ふ,へ,め,そ,ね,ほ,・,",
            ],
            [
                ",・,,,,,,,,,,,",
                "ぁ,え,り,ゃ,れ,ぱ,ぢ,ぐ,づ,ぴ,,゜",
                "を,あ,な,ゅ,も,ば,ど,ぎ,ぽ,,,",
                "ぅ,ー,ろ,や,ぃ,ぷ,ぞ,ぺ,ぼ,,",
            ],
            [
                ",・,,,,,,,,,,,",
                ",が,だ,ご,ざ,よ,に,る,ま,ぇ,,゜",
                "ゔ,じ,で,げ,ぜ,み,お,の,ょ,っ,,",
                ",び,ず,ぶ,べ,ぬ,ゆ,む,わ,ぉ,",
            ],
        ],
        [KanaLayout.Tron] = [
            [
                ",,,,,,,,,,ー,,",
                "ら,る,こ,は,ょ,き,の,く,あ,れ,ー,",
                "た,と,か,て,も,を,い,う,し,ん,,",
                "ま,り,に,さ,な,す,つ,、,。,っ,",
            ],
            [
                ",・,,,,,,,,,,,",
                "ひ,そ,・,ゃ,ほ,ぎ,げ,ぐ,,ゐ,,",
                "ぬ,ね,ゅ,よ,ふ,゛,ぢ,ゔ,じ,ゑ,,",
                "ぇ,ぉ,せ,ゆ,へ,ず,づ,、,。,ゎ,",
            ],
            [
                ",・,,,,,,,,,,,",
                "び,ぞ,ご,ば,ぼ,え,け,め,む,ろ,,",
                "だ,ど,が,で,ぶ,お,ち,ー,み,や,,",
                "ゕ,ゖ,ぜ,ざ,べ,わ,ぃ,ぁ,゜,ぅ,",
            ],
        ],
        [KanaLayout.Tsuki] = [
            [
                ",,,,,,,,,,,,",
                "ゅ,と,し,こ,ょ,つ,ん,い,か,り,ー,",
                "は,2,1,て,た,く,う,1,2,き,ち,",
                "す,を,に,な,さ,っ,る,の,も,へ,・",
            ],
            [
                ",,,,,,,,,,,,",
                "ぁ,ゃ,ね,ら,め,ぬ,む,み,え,ぇ,,",
                "ぃ,け,、,あ,れ,ま,お,ほ,そ,ろ,,",
                "ぅ,や,せ,よ,ゆ,ひ,わ,ふ,ぃ,ぉ,",
            ],
            [
                ",,,,,,,,,,,,",
                ",ど,じ,ご,,づ,ぴ,ぽ,が,,,",
                "ば,げ,。,で,だ,ぐ,ゔ,ぼ,ぞ,ぎ,ぢ,",
                "ず,ぱ,ぜ,,ざ,び,ぷ,ぶ,ぺ,べ,",
            ],
        ],
        [KanaLayout.Hana] = [
            [
                ",,,,,,,,,,,,ぇ",
                "ょ,て,と,こ,は,っ,く,う,る,ら,ー,",
                "す,か,1,き,た,ん,れ,1,゛,ろ,り,",
                "さ,し,な,の,に,い,つ,。,、,め,え",
            ],
            [
                ",,,,,,,,,,,,え",
                "ひ,け,ぇ,ほ,へ,ゃ,ま,そ,も,ぃ,,",
                "ぁ,よ,ゅ,や,゜,・,ふ,ち,む,ぉ,,",
                "ぅ,せ,あ,わ,ゆ,ね,み,を,お,ぬ,",
            ],
        ],
    };

    private static readonly Dictionary<KanaLayout, Dictionary<int, string>[]> Faces = Tables.ToDictionary(
        pair => pair.Key,
        pair => pair.Value.Select(face =>
        {
            var map = new Dictionary<int, string>();
            for (var row = 0; row < Rows.Length; row++)
            {
                var cells = face[row].Split(',');
                for (var column = 0; column < Rows[row].Length && column < cells.Length; column++)
                    if (cells[column].Length > 0) map[Rows[row][column]] = cells[column];
            }
            return map;
        }).ToArray());

    private readonly Dictionary<int, string>[] _faces = layout == KanaLayout.Jis ? [] : Faces[layout];
    private int _prefix;
    // 最後に入力した文字キー (後から押した親指キーで打ち直すため)。
    private int _lastKey;
    private long _lastTime = long.MinValue;
    private bool _lastShifted = true;
    private bool _lastReleased;
    // 押している最中の親指キー (押したことを、まだ文字に使っていなければ Unused)
    private int _thumb;
    private bool _thumbUsed;
    // 中指NICOLA: 押したままの D / K (シフトに使ったか)
    private int _middle;
    private bool _middleUsed;
    // 中指NICOLA: D / K を押したまま押した文字キー (先に離したらシフト面で打ち直す) とそのシフト面の文字
    private int _middlePending;
    private string _middlePendingKana = "";

    public KanaLayout Layout => layout;

    public bool IsThumbShift => layout is KanaLayout.Nicola or KanaLayout.NakayubiNicola or KanaLayout.Tron;
    public bool IsPrefixShift => layout is KanaLayout.Tsuki or KanaLayout.Hana;

    /// <summary>JIS かな以外で、その配列が文字を割り当てているキーか (変換ボックスを開くキー)。</summary>
    public bool IsLayoutKey(int vk) => _faces.Any(face => face.ContainsKey(vk)) || IsThumbShift && vk is LeftThumb or RightThumb;

    /// <summary>親指キーを押したままか (離すまで、離したことを受け取る必要がある)。</summary>
    public bool ThumbHeld => _thumb != 0;

    /// <summary>前置シフトのキーを押して、次のキーを待っているか。</summary>
    public bool HasPrefix => _prefix > 0;

    /// <summary>左手で打つキー (表の各段の左から 5 つ)。</summary>
    public static bool IsLeftHand(int vk) => Rows.Any(row => Array.IndexOf(row, vk) is >= 0 and < 5);

    /// <summary>打ちかけ (前置シフト・押したままの親指キー) を忘れる。入力を確定・取り消したときに呼ぶ。</summary>
    public void Reset()
    {
        _prefix = 0;
        _lastKey = 0;
        _lastTime = long.MinValue;
        _lastShifted = true;
        _middlePending = 0;
    }

    /// <summary>キーを押した。shift は Shift キー (小指シフト) を押しているか。</summary>
    public KanaStroke Down(int vk, bool shift, long time)
    {
        if (layout == KanaLayout.Jis) return KanaStroke.NotHandled;
        if (IsThumbShift && vk is LeftThumb or RightThumb)
        {
            // 押したままの親指キーのリピート: 押したことは変えない (文字に使ったかを消さない)
            if (vk == _thumb) return KanaStroke.Consumed;
            var face = vk == LeftThumb ? 1 : 2;
            // 文字キーを押している間 (押してから ChordWindowMs 以内) に押した親指キー: その文字をシフト面で打ち直す。
            // 文字キーを離してから押した親指キーは同時打鍵ではない
            if (!_lastShifted && !_lastReleased && time - _lastTime <= ChordWindowMs && _faces[face].TryGetValue(_lastKey, out var shifted))
            {
                _lastShifted = true;
                _thumb = vk;
                _thumbUsed = true;
                return new KanaStroke(true, 1, shifted);
            }
            _thumb = vk;
            _thumbUsed = false;
            return KanaStroke.Consumed;
        }
        // Shift (小指シフト) を押しているキーは、ふつうのキー (英字・記号) として扱う
        if (shift) return KanaStroke.NotHandled;
        if (!_faces[0].TryGetValue(vk, out var plain) && !(_prefix > 0 && _faces[_prefix].ContainsKey(vk))) return KanaStroke.NotHandled;

        if (IsPrefixShift)
        {
            if (_prefix > 0)
            {
                var face = _faces[_prefix];
                _prefix = 0;
                return face.TryGetValue(vk, out var kana) && kana is not ("1" or "2") ? Emit(vk, kana, shifted: true) : KanaStroke.Consumed;
            }
            if (plain is "1" or "2")
            {
                _prefix = plain[0] - '0';
                return KanaStroke.Consumed;
            }
            return Emit(vk, plain!, shifted: true);
        }

        // 親指シフト: 押したままの親指キー
        var thumb = _thumb;
        if (thumb != 0)
        {
            _thumbUsed = true;
            var face = _faces[thumb == LeftThumb ? 1 : 2];
            return face.TryGetValue(vk, out var kana) ? Emit(vk, kana, shifted: true) : KanaStroke.Consumed;
        }
        // 中指NICOLA: 押したままの K + 左手のキー → 左シフト、D + 右手のキー → 右シフト。
        // 押したときはふつうの文字にしておき、D / K より先に離したら打ち直す (Up)。D / K のリピートは無視する
        if (layout == KanaLayout.NakayubiNicola && vk == _middle) return KanaStroke.Consumed;
        _middlePending = 0;
        if (layout == KanaLayout.NakayubiNicola && _middle != 0 && IsLeftHand(vk) == (_middle == KeyK) &&
            _faces[_middle == KeyK ? 1 : 2].TryGetValue(vk, out var middleKana))
        {
            _middlePending = vk;
            _middlePendingKana = middleKana;
        }
        if (layout == KanaLayout.NakayubiNicola && vk is KeyD or KeyK && _middle == 0)
        {
            _middle = vk;
            _middleUsed = false;
        }
        if (plain is null) return KanaStroke.NotHandled;
        return Emit(vk, plain, shifted: false, time);
    }

    /// <summary>キーを離した。</summary>
    public KanaKeyUp Up(int vk)
    {
        if (vk == _lastKey) _lastReleased = true;
        // 中指NICOLA: D / K を押したまま、文字キーを先に離した → シフト面で打ち直す (文字キーと、まだなら D / K の文字を消す)
        if (vk == _middlePending && _middle != 0)
        {
            _middlePending = 0;
            var remove = _middleUsed ? 1 : 2;
            _middleUsed = true;
            Emit(vk, _middlePendingKana, shifted: true);
            return new KanaKeyUp(false, new KanaStroke(true, remove, _middlePendingKana));
        }
        if (vk == _middle)
        {
            _middle = 0;
            _middlePending = 0; // D / K を先に離した: 速く打って重なっただけ
        }
        if (vk != _thumb || vk == 0) return default;
        _thumb = 0;
        return new KanaKeyUp(!_thumbUsed, KanaStroke.NotHandled);
    }

    private KanaStroke Emit(int vk, string kana, bool shifted, long time = long.MinValue)
    {
        _lastKey = vk;
        _lastTime = time;
        _lastShifted = shifted;
        _lastReleased = false;
        return new KanaStroke(true, 0, kana);
    }
}
