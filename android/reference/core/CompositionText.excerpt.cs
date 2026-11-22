// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 AoneSenbongi
//
// CompositionText.cs から BackSpace 後の再判定だけを抜き出したもの。単体ではビルドできない。

/// <summary>1 音 (または 1 文字) 消す。入力途中の子音があればそれを 1 文字消す。</summary>
public void RemoveLast(bool kanaCharacter = false)
{
    // 保護範囲 (メンション・URL・パス) の中は、1 音 (く・ら…) ではなく打った原文の 1 文字ずつ消す。
    if (!KanaInput && (HasProtectedTail || _pending.Length == 0 && _units.Count > 0 &&
        ProtectedSpans().Any(span => span.End > Raw.Length - _units[^1].Raw.Length)))
    {
        var raw = Raw;
        if (raw.Length > 0)
        {
            var truncated = raw[..^1];
            _units.Clear();
            _pending.Clear();
            foreach (var c in truncated) Append(c);
            if (IsEmpty)
            {
                Mode = DisplayMode.Auto;
                LevelOverride = null;
            }
            return;
        }
    }
    if (_pending.Length > 0 && KanaInput) _pending.Length--;
    else if (_pending.Length > 0)
    {
        // 打ちかけの子音を 1 文字消し、残った原文を打ち直す。後ろのキーを見て決めた単位 (ff の っ、nk の ん) は、
        // そのキーを消したら決め直す (ff + BackSpace + uronto が っうろんと、nk + BackSpace + a が んあ になっていた)。
        var truncated = Raw[..^1];
        _units.Clear();
        _pending.Clear();
        foreach (var c in truncated) Append(c);
    }
    else if (_units.Count > 0)
    {
        var englishTail = Mode is DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric ||
                          (Mode == DisplayMode.Auto && Segments() is { Count: > 0 } segments && segments[^1].IsEnglish);
        var last = _units[^1];
        if (kanaCharacter && !englishTail && last.Kana.Length > 1 && last.Kana.All(KanaChars.IsHiraganaOrLongMark))
        {
            var remaining = last.Kana[..^1];
            _units[^1] = new CompositionUnit(remaining, remaining);
            return;
        }
        _units.RemoveAt(_units.Count - 1);
        // 英字として見せている部分は 1 文字ずつ消す (google の "le" のように 1 音にまとまった単位を丸ごと消さない)。
        if (englishTail && last.Raw.Length > 1 && !KanaInput)
        {
            _pending.Append(last.Raw[..^1]);
            Normalize(final: false);
        }
    }
    if (IsEmpty)
    {
        Mode = DisplayMode.Auto;
        Case = LetterCase.AsTyped;
        LevelOverride = null;
    }
}
