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
}

/// <summary>かな配列の 1 打鍵の結果。</summary>
/// <param name="Handled">配列のキーとして扱った (false ならふつうのキーとして処理する)。</param>
/// <param name="Kana">入力するかな (空なら何も足さない。前置シフトのキー)。</param>
public readonly record struct KanaStroke(bool Handled, string Kana = "")
{
    public static KanaStroke NotHandled => default;
    public static KanaStroke Consumed => new(true);
}

/// <summary>
/// JIS かな以外のかな配列 (月・花) で、打鍵をかなにする (issue #1415)。
/// 前置シフト: シフトのキー (D・K) を押した次の 1 打鍵をシフト面で読む。
/// 配列の表は benizara (MIT License, k-ayaki) の定義ファイルから取った。
/// </summary>
public sealed class KanaLayoutEngine(KanaLayout layout)
{
    // 表の行 (数字の段・Q の段・A の段・Z の段) の仮想キー (日本語 106/109 キーボード)。
    private static readonly int[][] Rows =
    [
        [0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0xBD, 0xDE, 0xDC],
        [0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50, 0xC0, 0xDB],
        [0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, 0xBB, 0xBA, 0xDD],
        [0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D, 0xBC, 0xBE, 0xBF, 0xE2],
    ];

    // 配列ごとの面 [シフトなし, 1 のシフト, 2 のシフト]。表の中の "1" "2" は前置シフトのキー。
    private static readonly Dictionary<KanaLayout, string[][]> Tables = new()
    {
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
    // 押した前置シフトのキー (1 か 2。0 なら無し)
    private int _prefix;

    public KanaLayout Layout => layout;

    /// <summary>前置シフトのキーを押して、次のキーを待っているか。</summary>
    public bool HasPrefix => _prefix > 0;

    /// <summary>
    /// 打ちかけの前置シフトを忘れる。配列のキー以外を押したとき (BackSpace・Enter・Esc など)・入力を確定・取り消したときに呼ぶ
    /// (月配列で D BackSpace J と打って、消したはずの D のシフトで J が「お」にならないように)。
    /// </summary>
    public void Reset() => _prefix = 0;

    /// <summary>キーを押した。shift は Shift キーを押しているか (Shift を押したキーはふつうの英字・記号)。</summary>
    public KanaStroke Down(int vk, bool shift)
    {
        if (layout == KanaLayout.Jis || shift) return KanaStroke.NotHandled;
        if (_prefix > 0)
        {
            var face = _faces[_prefix];
            if (!face.ContainsKey(vk) && !_faces[0].ContainsKey(vk)) return KanaStroke.NotHandled;
            _prefix = 0;
            // シフト面に文字の無いキーは、何も入れない (シフトのキーどうしも)
            return face.TryGetValue(vk, out var kana) && kana is not ("1" or "2") ? new KanaStroke(true, kana) : KanaStroke.Consumed;
        }
        if (!_faces[0].TryGetValue(vk, out var plain)) return KanaStroke.NotHandled;
        if (plain is "1" or "2")
        {
            _prefix = plain[0] - '0';
            return KanaStroke.Consumed;
        }
        return new KanaStroke(true, plain);
    }
}
