// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// ユーザーが自分で直した誤判定 1 件 (issue #284)。誤判定の報告のひな形 (2-misdetection.yml) と同じ項目:
/// 打ったもの (キーをそのまま)・出たもの (自動の判定)・期待した結果 (直した結果)・最後に押したキー・前の文。
/// </summary>
public sealed record Correction(string Typed, string Shown, string Corrected, string LastKey, string Before, DateTime Time)
{
    /// <summary>報告の種類 (ひな形の選択肢)。</summary>
    public string Kind =>
        Corrected.Any(char.IsAsciiLetter) && !Shown.Any(char.IsAsciiLetter) ? "英語のつもりがかなになった (みいてぃんg)" :
        Shown.Any(char.IsAsciiLetter) && !Corrected.Any(char.IsAsciiLetter) ? "日本語のつもりが英字になった (にほんgo)" : "そのほか";

    /// <summary>コピーして貼り付ける用の文 (GitHub・報告フォームの欄に合わせた並び)。</summary>
    public string ToReportText() => string.Join("\n",
    [
        $"種類: {Kind}",
        $"打ったもの (キーをそのまま): {Typed}",
        $"出たもの: {Shown}",
        $"期待した結果: {Corrected}",
        $"最後に押したキー: {LastKey}",
        $"前後の文: {Before}",
    ]);
}

/// <summary>
/// 最近直した誤判定 (メモリの中だけ。ファイルにも外にも保存しない。Meltype を終了すると消える)。
/// トレイの「直した誤判定を報告...」で、送る前に中身を全部見せ、書き換えてから報告できるようにする (issue #284)。
/// </summary>
public sealed class CorrectionLog
{
    private const int MaxEntries = 20;
    private readonly object _gate = new();
    private readonly List<Correction> _entries = [];

    public void Add(Correction correction)
    {
        lock (_gate)
        {
            _entries.Add(correction);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        }
    }

    /// <summary>新しい順。</summary>
    public IReadOnlyList<Correction> Recent()
    {
        lock (_gate) return Enumerable.Reverse(_entries).ToList();
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}
