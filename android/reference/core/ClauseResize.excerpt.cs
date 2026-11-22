// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 AoneSenbongi
//
// CompositionController.cs から文節の伸縮とその周辺だけを抜き出したもの。
// キー処理は元の switch から該当する case を抜いてメソッドにまとめた。単体ではビルドできない。

/// <summary>Shift は文節の伸縮と大文字入力のためにいったん保持し、ほかのキーと一緒に送り直す。</summary>
private bool HandleShift(VirtualKeys vk)
{
    if (!IsShift(vk)) return false;
    _swallowedShift.Add(vk);
    return true;
}

/// <summary>Shift+左右で文節を伸縮し、Shift なしで文節を移動する。</summary>
private bool HandleArrowKey(VirtualKeys vk)
{
    var shift = _swallowedShift.Count > 0;
    switch (vk)
    {
        case VirtualKeys.Right when shift:
            Resize(+1);
            return true;
        case VirtualKeys.Left when shift:
            Resize(-1);
            return true;
        case VirtualKeys.Right:
            _selectedClause = Math.Min(_selectedClause + 1, _clauses.Count - 1);
            return true;
        case VirtualKeys.Left:
            _selectedClause = Math.Max(_selectedClause - 1, 0);
            return true;
        default:
            return false;
    }
}

/// <summary>変換前に矢印キーを押したとき: 文節に区切って、← なら最後の文節、→ なら最初の文節を選ぶ。</summary>
private void EnterClauseSelection(int vk)
{
    var shift = _swallowedShift.Count > 0;
    StartConversion(preferJapanese: shift && _options.ShiftArrowsConvertAutoEnglish);
    if (!_converting) return;
    _selectedClause = vk == VirtualKeys.Left ? _clauses.Count - 1 : 0;
    if (shift && vk is VirtualKeys.Left or VirtualKeys.Right) Resize(vk == VirtualKeys.Left ? -1 : +1);
    else if (vk == VirtualKeys.Up) NextCandidate(-1);
}

/// <summary>
/// Shift+→ / Shift+←: 選択中の文節を 1 文字伸ばす / 縮める。はみ出した・空いた分は次の文節とやり取りし、
/// 変わった文節は読みから変換し直す。
/// 英語の文節との区切りを動かすときは、英語の文節を打ったローマ字のかなで日本語の文節に読み直してからやり取りする
/// (みー|thin|ぐ の みー を伸ばす → みーて|ぃん|ぐ)。縮めて空いた分は、後ろが英語の文節なら英語の文節はそのままにして間に新しい文節を作る。
/// 読み直せない英語の文節 (vrc、r) との区切りは、打った元ローマ字の 1 音で動かす。日本語の文節の 1 音を
/// 英語の文節に取り込むときは、読みに対応する元ローマ字を足す (vrc|はって → vrcha|って)。日本語の文節の次が
/// 読み直せない英語なら、日本語の文節は元ローマ字ごとまとめて英語の 1 文節にする (vrchat|てr → vrchat|ter)。
/// </summary>
private void Resize(int delta)
{
    _clauses[_selectedClause].Resized = true;
    // 英語の文節を、打ったローマ字のかなに読み直せるなら読み直してから動かす (読み直せなければ英語のまま動かす)
    if (_clauses[_selectedClause].IsEnglish) ReadAsJapanese(_selectedClause);
    var current = _clauses[_selectedClause];
    var next = _selectedClause + 1 < _clauses.Count ? _clauses[_selectedClause + 1] : null;

    if (delta > 0)
    {
        if (next is null) return;
        // 伸ばす: 次の文節の先頭の 1 音を取り込む。英語の文節は、かなに読み直せるなら読み直してから取り込む
        if (next is { IsEnglish: true } && ReadAsJapanese(_selectedClause + 1)) next = _clauses[_selectedClause + 1];
        if (current.IsEnglish)
        {
            // 英語の文節に取り込む。読みは元ローマ字なので、取り込むのは打った元ローマ字の 1 音
            if (RawUnits() is { } units && UnitAt(units, RawLengthBefore(_selectedClause + 1)) is { } unit)
            {
                current.Reading += unit.Raw;
                // 英語の文節の読みは元ローマ字なので、元ローマ字の文字数だけ外す
                var length = (next.IsEnglish || IsEnglishOnly(next) ? unit.Raw : unit.Kana).Length;
                next.Reading = next.Reading[length..];
                if (next.Raw is not null && next.Raw.StartsWith(unit.Raw, StringComparison.Ordinal))
                    next.Raw = next.Raw[unit.Raw.Length..];
                if (next.Reading.Length == 0) _clauses.Remove(next);
                else if (next.IsEnglish || IsEnglishOnly(next)) ReconvertEnglish(next);
                else Reconvert(next);
            }
            else if (next.IsEnglish || IsEnglishOnly(next))
            {
                // 対応が取れない読み直せない英語 (vrc|r): 元ローマ字の 1 文字
                current.Reading += next.Reading[0];
                next.Reading = next.Reading[1..];
                if (next.Reading.Length == 0) _clauses.Remove(next);
                else ReconvertEnglish(next);
            }
            else return;
            ReconvertEnglish(current);
            return;
        }
        if (next is { IsEnglish: true } || IsEnglishOnly(next))
        {
            // 日本語の文節の次に読み直せない英語: 元ローマ字ごとまとめて英語の 1 文節にする (vrchat|てr → vrchat|ter)
            if (ClauseRaw(current) is not { } raw) return;
            var merged = raw + next.Reading;
            _clauses[_selectedClause] = new Clause(merged, true, EnglishCandidates(merged));
            _clauses.RemoveAt(_selectedClause + 1);
            return;
        }
        // 日本語の文節どうし: 読みの 1 文字
        current.Reading += next.Reading[0];
        next.Reading = next.Reading[1..];
        if (next.Reading.Length == 0) _clauses.Remove(next);
        else Reconvert(next);
        Reconvert(current);
        return;
    }

    // 縮める: 選択中の文節の末尾の 1 音を次の文節へ渡す (無ければ新しい文節にする)
    if (current.IsEnglish || HasEnglishReading(current))
    {
        // 英語の文節 (読みは元ローマ字) と、英字の混ざった文節: 末尾の 1 音の元ローマ字を外す
        var units = RawUnits();
        var start = RawLengthBefore(_selectedClause);
        var end = RawLengthBefore(_selectedClause + 1);
        if (units is null || LastUnitAt(units, start, end) is not { } unit) return;
        if (current.IsEnglish || IsEnglishOnly(current))
        {
            if (current.Reading.Length <= unit.Raw.Length) return;
            current.Reading = current.Reading[..^unit.Raw.Length];
        }
        else
        {
            // 英字の混ざった日本語の文節: 読みからはかなの文字、元ローマ字からは打った文字を外す
            if (current.Reading.Length <= unit.Kana.Length ||
                current.Raw is null || current.Raw.Length < unit.Raw.Length) return;
            current.Reading = current.Reading[..^unit.Kana.Length];
            current.Raw = current.Raw[..^unit.Raw.Length];
        }
        if (next is null ||
            HasEnglishReading(next) && !IsEnglishOnly(next) ||
            !(next.IsEnglish || IsEnglishOnly(next)) && !unit.Kana.All(IsKana))
        {
            // 混在の文節には渡せず、かなに読めない音 (m) は日本語の文節に渡せない。読み直せない英語の文節として間に置く
            next = new Clause(unit.Raw, false, EnglishCandidates(unit.Raw));
            _clauses.Insert(_selectedClause + 1, next);
        }
        else if (next.IsEnglish || IsEnglishOnly(next))
        {
            next.Reading = unit.Raw + next.Reading;
            ReconvertEnglish(next);
        }
        else
        {
            next.Reading = unit.Kana + next.Reading;
            if (next.Raw is not null) next.Raw = unit.Raw + next.Raw;
            Reconvert(next);
        }
        if (current.IsEnglish || IsEnglishOnly(current)) ReconvertEnglish(current);
        else Reconvert(current);
        return;
    }
    if (current.Reading.Length <= 1) return;
    var moved = current.Reading[^1];
    // 動かす音に対応する元ローマ字を文節どうしで受け渡す (Windows の ReconvertKeepingRaw 相当)。
    var backUnits = RawUnits();
    (string Kana, string Raw)? backUnit = current.Raw is not null && backUnits is not null &&
        LastUnitAt(backUnits, RawLengthBefore(_selectedClause), RawLengthBefore(_selectedClause + 1)) is { } u &&
        u.Kana == moved.ToString() ? u : null;
    var movedRaw = backUnit?.Raw;
    current.Reading = current.Reading[..^1];
    if (movedRaw is not null && current.Raw is not null && current.Raw.EndsWith(movedRaw, StringComparison.Ordinal))
        current.Raw = current.Raw[..^movedRaw.Length];
    if (next is not null && IsEnglishOnly(next) && !next.IsEnglish && current.Raw is not null && backUnit is not null)
    {
        // 外れた文字の元ローマ字が分かるなら、読み直せない英語の文節に元ローマ字として戻す
        next.Reading = backUnit.Value.Raw + next.Reading;
        ReconvertEnglish(next);
    }
    else if (next is null || next.IsEnglish)
    {
        // 英語の文節はそのままにして、間に新しい文節を作る
        next = new Clause(moved.ToString(), false, []) { Raw = movedRaw };
        _clauses.Insert(_selectedClause + 1, next);
    }
    else
    {
        next.Reading = moved + next.Reading;
        if (movedRaw is not null && next.Raw is not null) next.Raw = movedRaw + next.Raw;
    }
    Reconvert(next);
    Reconvert(current);
}

/// <summary>文節の読みが英字だけ (読み直せない英語の keep 文節。r、...)。元ローマ字は読みそのもの。</summary>
private static bool IsEnglishOnly(Clause clause) =>
    clause.Reading.Length > 0 && clause.Reading.All(c => c is >= '!' and <= '~' && !char.IsAsciiDigit(c));

/// <summary>文節の読みに英字が混ざっている (ローマ字として読み直したときに、かなに読めない部分が残った文節。てあm)。</summary>
private static bool HasEnglishReading(Clause clause) => clause.Reading.Any(char.IsAsciiLetter);

/// <summary>文節の打った元ローマ字。英語の文節 (読み直せない英語も) は読みが元ローマ字、日本語・混在の文節は Raw。</summary>
private static string? ClauseRaw(Clause clause)
{
    if (clause.IsEnglish || IsEnglishOnly(clause)) return clause.Reading;
    return clause.Raw;
}

/// <summary>
/// 読み直せない英語の文節との区切りを動かすのに使う、打った音の列。
/// 文節の元ローマ字 (英語の文節は読み、日本語の文節は Raw) をつないだものが、打った元ローマ字と一致していないと対応を取れない。
/// </summary>
private List<(string Kana, string Raw)>? RawUnits()
{
    var builder = new System.Text.StringBuilder();
    foreach (var clause in _clauses)
    {
        if (ClauseRaw(clause) is not { } raw) return null;
        builder.Append(raw);
    }
    if (builder.ToString() != _text.Raw) return null;
    return _text.FinalUnits().ToList();
}

/// <summary>音の列のうち、元ローマ字の連結で offset 番目の文字から始まる音。音の途中なら null。</summary>
private static (string Kana, string Raw)? UnitAt(List<(string Kana, string Raw)> units, int offset)
{
    var position = 0;
    foreach (var (kana, raw) in units)
    {
        if (position == offset) return (kana, raw);
        position += raw.Length;
        if (position > offset) return null;
    }
    return null;
}

/// <summary>音の列のうち、元ローマ字の連結で [start, end) の範囲を終わる音 (文節の末尾の 1 音)。音の途中で終わっているなら null。</summary>
private static (string Kana, string Raw)? LastUnitAt(List<(string Kana, string Raw)> units, int start, int end)
{
    var position = 0;
    foreach (var (kana, raw) in units)
    {
        var stop = position + raw.Length;
        if (stop == end && position >= start) return (kana, raw);
        position = stop;
    }
    return null;
}

/// <summary>index 番目の文節より前の、元ローマ字の文字数。</summary>
private int RawLengthBefore(int index)
{
    var total = 0;
    for (var i = 0; i < index; i++) total += ClauseRaw(_clauses[i])!.Length;
    return total;
}

/// <summary>英語の文節を、打ったローマ字のかなを読みにした日本語の文節に置き換える。かなに読めない英字だけ (xyz) なら置き換えずに false。</summary>
private bool ReadAsJapanese(int index)
{
    if (!CanReadAsJapanese(_clauses[index])) return false;
    var clause = new Clause(_clauses[index].Kana!, false, []) { Raw = _clauses[index].Reading };
    Reconvert(clause);
    _clauses[index] = clause;
    return true;
}

private static bool CanReadAsJapanese(Clause clause) =>
    clause is { IsEnglish: true, Kana: { } kana } && kana.Any(IsKana);

private void Reconvert(Clause clause)
{
    clause.Candidates = JapaneseCandidates(clause.Reading, null);
    clause.Index = 0;
    clause.Changed = false;
    // 前の読みのもの。候補の一覧は、文節を選んだときに新しい読みで取り直す
    clause.Expanded = false;
    clause.Translations.Clear();
    if (_options.History?.Get(clause.Reading) is { } learned) Prefer(clause, learned);
}

/// <summary>英語の文節の読み (元ローマ字) が変わったので、英字の候補を作り直す。</summary>
private void ReconvertEnglish(Clause clause)
{
    clause.Candidates = EnglishCandidates(clause.Reading);
    clause.Index = 0;
    clause.Changed = false;
    clause.Expanded = false;
}
