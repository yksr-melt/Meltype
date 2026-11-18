// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Input;

/// <summary>
/// 同時打鍵のかな配列 (新JIS・飛鳥・下駄・新下駄・薙刀式・蜂蜜小梅など)。表は <see cref="KanaChordTables"/>。
/// 押したキーはまずシフトなしの面の文字にし、続けて押したキーと合わせて表にある組み合わせになったら、その文字に打ち直す。
/// 組み合わせにするのは、押したままのキーのうち、(1) 押してから <see cref="KanaLayoutEngine.ChordWindowMs"/> 以内に次のキーを押したもの (同時打鍵)、
/// (2) もう組み合わせに使って押し続けているもの (連続シフト)、(3) 親指キー・Space (押している間ずっとシフト)。
/// 文字にしたキー (組み合わせの最後のキー) は、それ以降の組み合わせのシフトには使わない (速く打って重なっただけのことが多いので)。
/// </summary>
internal sealed class ChordKanaLayout
{
    private const int Space = 0x20;

    // 表の段のキーの名前 (KanaChordTables の行で使う刻印の文字) と仮想キー (日本語 106/109 キーボード)
    private static readonly string[] RowNames = ["1234567890-^\\", "qwertyuiop@[", "asdfghjkl;:]", "zxcvbnm,./_"];

    private static readonly Dictionary<KanaLayout, ChordKanaLayout> Layouts =
        KanaChordTables.Tables.ToDictionary(pair => pair.Key, pair => new ChordKanaLayout(pair.Value));

    // 同時に押すキー (仮想キーを小さい順に並べたもの) → 文字キー → かな。シフトなしの面は空の組
    private readonly Dictionary<string, Dictionary<int, string>> _faces = [];
    // 組み合わせのシフトに使うキー
    private readonly HashSet<int> _shiftKeys = [];

    /// <summary>無変換・変換を親指シフトに使うか。</summary>
    public bool UsesThumbs { get; }
    /// <summary>Space をシフトに使うか (薙刀式)。</summary>
    public bool UsesSpace { get; }
    /// <summary>Shift (小指シフト) の面がかなか (新JIS)。</summary>
    public bool UsesPinky { get; }

    private ChordKanaLayout(string[] table)
    {
        foreach (var line in table)
        {
            var cells = line.Split('\t');
            var chord = cells[0].Length == 0 ? [] : cells[0].Split('+').Select(KeyOf).ToArray();
            var key = KeyOf(cells[1]);
            if (key == 0 || chord.Contains(0)) continue;
            var id = Id(chord);
            if (!_faces.TryGetValue(id, out var face)) _faces[id] = face = [];
            face[key] = cells[2];
            _shiftKeys.UnionWith(chord);
            UsesThumbs |= chord.Contains(KanaLayoutEngine.LeftThumb) || chord.Contains(KanaLayoutEngine.RightThumb);
            UsesSpace |= chord.Contains(Space);
            UsesPinky |= chord.Contains(VirtualKeys.Shift);
        }
    }

    /// <summary>その配列が同時打鍵の配列なら、その表。</summary>
    public static ChordKanaLayout? For(KanaLayout layout) => Layouts.GetValueOrDefault(layout);

    private static int KeyOf(string name)
    {
        switch (name)
        {
            case "L": return KanaLayoutEngine.LeftThumb;
            case "R": return KanaLayoutEngine.RightThumb;
            case "Space": return Space;
            case "Shift": return VirtualKeys.Shift;
        }
        for (var row = 0; row < RowNames.Length; row++)
        {
            var column = RowNames[row].IndexOf(name, StringComparison.Ordinal);
            if (name.Length == 1 && column >= 0) return KanaLayoutEngine.KeyAt(row, column);
        }
        return 0;
    }

    private static string Id(IEnumerable<int> keys) => string.Join('+', keys.Order());

    /// <summary>シフト専用のキー (押しただけでは文字にならない: 親指キー・Space)。</summary>
    public bool IsShiftOnly(int vk) => vk is KanaLayoutEngine.LeftThumb or KanaLayoutEngine.RightThumb ? UsesThumbs : vk == Space && UsesSpace;

    /// <summary>その組み合わせの面に、その文字キーがあれば、そのかな。</summary>
    private string? Lookup(IEnumerable<int> chord, int key) =>
        _faces.TryGetValue(Id(chord), out var face) && face.TryGetValue(key, out var kana) ? kana : null;

    /// <summary>押しているキー。Emitted はそのキーで入れて、まだ打ち直していない文字の数。</summary>
    private sealed class Held(int vk, long time, int emitted, bool shiftOnly)
    {
        public int Vk { get; } = vk;
        public long Time { get; } = time;
        public int Emitted { get; set; } = emitted;
        public bool ShiftOnly { get; } = shiftOnly;
        /// <summary>組み合わせのシフトに使った (押している間は連続シフト)。</summary>
        public bool Shifting { get; set; }
        /// <summary>組み合わせの文字キーにした (シフトには使わない)。</summary>
        public bool Target { get; set; }
    }

    private readonly List<Held> _held = [];

    /// <summary>シフト専用のキー (親指キー・Space) を押したままか。</summary>
    public bool ShiftHeld => _held.Any(key => key.ShiftOnly);

    /// <summary>打ちかけを忘れる (入力を確定・取り消したとき)。押したままのシフト専用のキーは、離すまで覚えておく。</summary>
    public void Reset() => _held.RemoveAll(key => !key.ShiftOnly);

    public KanaStroke Down(int vk, bool shift, long time)
    {
        // 押したままのキーのリピートは無視する (打った文字を増やさない)
        if (_held.Any(key => key.Vk == vk)) return KanaStroke.Consumed;
        if (shift)
        {
            // 小指シフトの面がかなの配列 (新JIS) だけ、Shift を押したキーをかなにする。ほかはふつうの英字・記号
            if (!UsesPinky || Lookup([VirtualKeys.Shift], vk) is not { } pinky) return KanaStroke.NotHandled;
            return Press(vk, time, pinky);
        }
        if (IsShiftOnly(vk))
        {
            // 直前に押した文字キーを、このキーとの組み合わせで打ち直す (文字キーの直後にシフトのキーを押した: 同時打鍵)
            var last = _held.LastOrDefault(key => !key.ShiftOnly && !key.Target && !key.Shifting && key.Emitted > 0 && time - key.Time <= KanaLayoutEngine.ChordWindowMs);
            var held = new Held(vk, time, 0, shiftOnly: true);
            _held.Add(held);
            if (last is not null && Lookup([vk], last.Vk) is { } kana)
            {
                var remove = last.Emitted;
                last.Emitted = kana.Length;
                last.Target = true;
                held.Shifting = true;
                return new KanaStroke(true, remove, kana);
            }
            return KanaStroke.Consumed;
        }
        // 押したままのキーとの組み合わせ。大きい組み合わせ (3 キー) を先に見る
        // 組み合わせの文字キーにしたキーも、同時に押したなら 3 キーの組み合わせには使う (j w → ぎ、j w h → ぎゃ)
        var shifts = _held.Where(key => key.ShiftOnly || key.Shifting || time - key.Time <= KanaLayoutEngine.ChordWindowMs).ToList();
        foreach (var chord in Subsets(shifts))
        {
            if (Lookup(chord.Select(key => key.Vk), vk) is not { } kana) continue;
            var remove = 0;
            foreach (var key in chord)
            {
                remove += key.Emitted;
                key.Emitted = 0;
                if (!key.Target) key.Shifting = true;
            }
            // 入れた文字は文字キーのもの (3 キーの組み合わせになったら打ち直す)
            _held.Add(new Held(vk, time, kana.Length, shiftOnly: false) { Target = true });
            return new KanaStroke(true, remove, kana);
        }
        // 後から押したこのキーがシフトで、押したままの文字キーと組み合わせになる (k の直後に d: d + k の面の k)
        var previous = _held.LastOrDefault(key => !key.ShiftOnly && !key.Target && !key.Shifting && key.Emitted > 0 && time - key.Time <= KanaLayoutEngine.ChordWindowMs);
        if (previous is not null && Lookup([vk], previous.Vk) is { } reversed)
        {
            var remove = previous.Emitted;
            previous.Emitted = reversed.Length;
            previous.Target = true;
            _held.Add(new Held(vk, time, 0, shiftOnly: false) { Shifting = true });
            return new KanaStroke(true, remove, reversed);
        }
        if (Lookup([], vk) is not { } plain)
        {
            // シフトにだけ使うキー (表のシフトなしの面に文字が無い): 押したことだけ覚える
            if (!_shiftKeys.Contains(vk)) return KanaStroke.NotHandled;
            _held.Add(new Held(vk, time, 0, shiftOnly: false));
            return KanaStroke.Consumed;
        }
        return Press(vk, time, plain);
    }

    private KanaStroke Press(int vk, long time, string kana)
    {
        _held.Add(new Held(vk, time, kana.Length, shiftOnly: false));
        return new KanaStroke(true, 0, kana);
    }

    /// <summary>
    /// 押したままのキーの組み合わせ (2 キー、1 キーの順。新しく押したキーを含むものを先に)。
    /// 組み合わせの文字キーにしたキーは、シフトのキーと一緒のときだけ (それだけをシフトにしない)。
    /// </summary>
    private static IEnumerable<Held[]> Subsets(List<Held> keys)
    {
        for (var i = keys.Count - 1; i >= 0; i--)
            for (var j = i - 1; j >= 0; j--)
                if (!keys[i].Target || !keys[j].Target) yield return [keys[j], keys[i]];
        for (var i = keys.Count - 1; i >= 0; i--)
            if (!keys[i].Target) yield return [keys[i]];
    }

    /// <summary>キーを離した。シフト専用のキーを組み合わせに使わずに離したら true (もとの働き: 変換・無変換・Space をする)。</summary>
    public bool Up(int vk)
    {
        var index = _held.FindIndex(key => key.Vk == vk);
        if (index < 0) return false;
        var held = _held[index];
        _held.RemoveAt(index);
        return held.ShiftOnly && !held.Shifting;
    }
}
