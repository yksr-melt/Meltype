// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Input;

/// <summary>
/// 打つキーの並びでかなを決める配列 (新月配列)。表は <see cref="KanaSequenceTables"/> (Google 日本語入力のローマ字テーブルと同じ形)。
/// 打ったキーの並びが表の綴りで、もっと長い綴りの始まりでもあるときは、その綴りのかなを仮に出しておき、
/// 長い綴りになったら打ち直す (か → が のように後から ゛ を打つ配列で、打ったかなをすぐ見せるため)。
/// 続くキーで表の綴りにならなかったら、仮に出したかなで決め、表の「次の入力」から読み直す。
/// </summary>
internal sealed class SequenceKanaLayout
{
    // US 配列の刻印の文字 → キーの位置 (表の段と列)。KanaLayoutEngine の表の段と同じ並び
    private static readonly string[] UsRows = ["1234567890-=", "qwertyuiop[]", "asdfghjkl;'", "zxcvbnm,./"];

    private static readonly Dictionary<KanaLayout, SequenceKanaLayout> Layouts =
        KanaSequenceTables.Tables.ToDictionary(pair => pair.Key, pair => new SequenceKanaLayout(pair.Value));

    // 綴り (キーの位置の文字) → (かな, 次の入力)
    private readonly Dictionary<string, (string Kana, string Next)> _rules = new(StringComparer.Ordinal);
    // もっと長い綴りの始まりになる並び
    private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);
    private readonly Dictionary<int, char> _keys = [];

    private SequenceKanaLayout(string[] table)
    {
        foreach (var line in table)
        {
            var cells = line.Split('\t');
            _rules[cells[0]] = (cells[1], cells.Length > 2 ? cells[2] : "");
            for (var i = 1; i < cells[0].Length; i++) _prefixes.Add(cells[0][..i]);
            foreach (var c in cells[0])
            {
                if (KeyOf(c) is var vk and > 0) _keys[vk] = c;
            }
        }
    }

    /// <summary>その配列がキーの並びの配列なら、その表。</summary>
    public static SequenceKanaLayout? For(KanaLayout layout) => Layouts.GetValueOrDefault(layout);

    private static int KeyOf(char c)
    {
        for (var row = 0; row < UsRows.Length; row++)
        {
            var column = UsRows[row].IndexOf(c, StringComparison.Ordinal);
            if (column >= 0) return KanaLayoutEngine.KeyAt(row, column);
        }
        return 0;
    }

    // 打ちかけの並びと、その並びで仮に出したかな (文字数) と、仮に出した綴りの次の入力
    private string _buffer = "";
    private int _shown;
    private string _next = "";

    /// <summary>
    /// 何も出さずに次のキーを待っているか (☆・★ を押した後)。かなを仮に出しているときは含めない
    /// (BackSpace でそのかなを消せるように)。
    /// </summary>
    public bool Pending => _buffer.Length > 0 && _shown == 0;

    public void Reset()
    {
        _buffer = "";
        _shown = 0;
        _next = "";
    }

    public KanaStroke Down(int vk, bool shift)
    {
        if (shift || !_keys.TryGetValue(vk, out var key)) return KanaStroke.NotHandled;
        var buffer = _buffer + key;
        if (_rules.ContainsKey(buffer) || _prefixes.Contains(buffer)) return Advance(buffer, _shown);
        // 続かなかった: 仮に出したかなで決め、次の入力から読み直す (仮に出していなければ打ちかけを捨てる)
        var restart = _shown > 0 ? _next : "";
        Reset();
        buffer = restart + key;
        return _rules.ContainsKey(buffer) || _prefixes.Contains(buffer) ? Advance(buffer, 0) : KanaStroke.Consumed;
    }

    private KanaStroke Advance(string buffer, int remove)
    {
        if (!_rules.TryGetValue(buffer, out var rule))
        {
            // 綴りの途中: 仮に出したかなはそのまま、キーを待つ
            _buffer = buffer;
            return KanaStroke.Consumed;
        }
        if (_prefixes.Contains(buffer))
        {
            _buffer = buffer;
            _shown = rule.Kana.Length;
            _next = rule.Next;
        }
        else
        {
            // 綴りが決まった。次の入力があれば、そこから続ける (表示はしない)
            Reset();
            _buffer = rule.Next;
        }
        return new KanaStroke(true, remove, rule.Kana);
    }
}
