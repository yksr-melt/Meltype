// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// Mozc と Zenzai を組み合わせた、全 OS で使う変換エンジン (#1740)。
/// Mozc と Zenzai の答えのどちらを使うかは <see cref="Choose"/> が決める。組み立ては <see cref="MeltypeRuntime.CreateConverter"/>。
/// </summary>
/// <param name="zenzaiFailed">変換で例外になった Zenzai を、持ち主 (zenzai を渡した側) に知らせる。止めて作り直すのは持ち主が行う。</param>
/// <param name="userWords">読みに登録したユーザー辞書の語 (<see cref="UserDictionary.Lookup"/>)。</param>
public sealed class HybridConverter(MozcConverter mozc, Func<ZenzaiConverter?>? zenzai = null, Action<ZenzaiConverter>? zenzaiFailed = null,
    Func<string, IReadOnlyList<string>>? userWords = null)
    : IKanjiConverter, ILearningConverter, IStandaloneConverter, IBackgroundConverter
{
    private ZenzaiConverter? _subscribedZenzai;
    private Action? _zenzaiReady;

    public ConversionBudget Budget => mozc.Budget;

    /// <summary>Mozc のヘルパーがあるか (起動できるかは使ってみるまで分からない)。</summary>
    public bool MozcInstalled => mozc.IsInstalled;

    public event Action? ResultReady
    {
        add
        {
            mozc.ResultReady += value;
            _zenzaiReady += value;
        }
        remove
        {
            mozc.ResultReady -= value;
            _zenzaiReady -= value;
        }
    }

    /// <summary>Mozc のヘルパーを裏で起動しておく (最初の変換を待たせない)。</summary>
    public void WarmUp()
    {
        if (mozc.IsInstalled) mozc.WarmUp();
    }

    // ZenzaiHolder が Zenzai を作り直すので、今の Zenzai にだけつなぐ
    private void Subscribe(ZenzaiConverter z)
    {
        if (ReferenceEquals(z, _subscribedZenzai)) return;
        if (_subscribedZenzai is not null) _subscribedZenzai.ResultReady -= OnZenzaiReady;
        _subscribedZenzai = z;
        z.Budget = Budget;
        z.ResultReady += OnZenzaiReady;
    }

    private void OnZenzaiReady() => _zenzaiReady?.Invoke();

    /// <summary>
    /// 設定で Zenzai が使えるとき、その変換結果 (読み全体で 1 つの文節)。使えない・変換できなければ null。
    /// Zenzai を作れない・変換で例外になったときも null (Mozc で変換する)。Zenzai の失敗で入力全体を止めないように
    /// (#973: 同梱の .NET に System.Net.Http が無く、Zenzai を作るたびに例外になって、日本語を打てなくなった)。
    /// 変換で例外になった Zenzai は、それからは使わず (打つたびに例外にしない)、持ち主に知らせる (zenzaiFailed)。
    /// 止める (Dispose) のは持ち主で、ここでは止めない。持ち主が新しく作ったら、それは使う。
    /// </summary>
    private List<ConversionClause>? Zenzai(string hiragana, string? context)
    {
        ZenzaiConverter? z = null;
        try
        {
            z = zenzai?.Invoke();
            if (z is null || ReferenceEquals(z, _failedZenzai) || !z.IsAvailable) return null;
            Subscribe(z);
            return z.ConvertClauses(hiragana, context)?.Select(c => c with { Zenzai = true }).ToList();
        }
        catch (Exception ex)
        {
            var failed = (object?)z ?? CouldNotCreate;
            if (!ReferenceEquals(failed, _failedZenzai)) Diagnostics.Log.Error($"Zenzai で変換できないので、Mozc で変換します: {ex}");
            _failedZenzai = failed;
            if (z is not null) zenzaiFailed?.Invoke(z);
            return null;
        }
    }

    // 例外になった Zenzai (持ち主が同じものをまた渡しても使わない)。作るところで例外になったときは CouldNotCreate
    // (このときはログを 1 回にするだけ。Windows 版では ZenzaiHolder が作れなかったことを覚えて待つので、ここまで例外は来ない)
    private object? _failedZenzai;
    private static readonly object CouldNotCreate = new();

    public string? Convert(string hiragana) =>
        ConvertClauses(hiragana) is { Count: > 0 } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;

    /// <summary>
    /// Mozc の文節ごとの候補をつないで answer を作れるか (文脈なし)。Mozc が答えなかったら null。
    /// ヘルパーが答えるまで待つので、入力スレッドでは呼ばない (<see cref="MeltypeRuntime.ForgetZenzaiHallucinations"/> が裏のスレッドで呼ぶ)。
    /// </summary>
    public bool? IsBuiltFromMozcCandidatesNow(string hiragana, string answer) =>
        mozc.ConvertWithCandidatesNow(hiragana) is { } fromMozc ? Align(fromMozc, answer) is not null : null;

    /// <summary>Zenzai を使わない変換 (文節ごとの候補に足す「その読みだけの変換」用。IStandaloneConverter)。</summary>
    public string? ConvertStandalone(string hiragana) => mozc.Convert(hiragana);

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
        Choose(hiragana, mozc.ConvertWithCandidates(hiragana, context), Zenzai(hiragana, context) is [var z] ? z.Text : null, userWords);

    /// <summary>
    /// Mozc の答え (文節と文節ごとの候補) と Zenzai の答え (読み全体の文字列) から、使う文節を決める。どちらも無ければ null。
    /// Zenzai の答えが Mozc の文節ごとの候補を 1 つずつ選んでつなげて作れるなら、Mozc の文節の区切りで返す (Align。同じ文字列ならそのまま)。
    /// 1 つの文節のままだと、文節ごとに候補を選び直せず、前に選び直して覚えた変換 (文節の読みごと) も効かない。
    /// Mozc の文節で返した結果は、確定すると Mozc にも覚えさせる (Mozc の候補に無い 1 つの文節は、Mozc が覚えない)。
    /// Mozc の候補で作れない答えは、かなが読みと合うときだけ、読み全体で 1 つの文節にして使う (<see cref="ZenzaiConverter.IsPlausible"/>)。
    /// 壊れた答え (はーどうぇ → ハードウェアウェア) は使わず、表記が読みと違う語 (きずく → 気づく、あんはさうぇい → アン・ハサウェイ) は Mozc の候補にあれば使う。
    /// Mozc が最初の候補にしたユーザー辞書の語 (userWords) は、Zenzai の答えで変えない。Zenzai はユーザー辞書の語を知らない。
    /// </summary>
    internal static IReadOnlyList<ConversionClause>? Choose(string hiragana, IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)>? fromMozc, string? zenzai,
        Func<string, IReadOnlyList<string>>? userWords = null)
    {
        var plausible = zenzai is not null && ZenzaiConverter.IsPlausible(hiragana, zenzai);
        if (fromMozc is null) return plausible ? [new ConversionClause(hiragana, zenzai!, Zenzai: true)] : null;
        if (zenzai is not null && Align(fromMozc, zenzai, userWords) is { } aligned) return [.. aligned.Select(c => c with { Zenzai = true })];
        var hasUserWord = fromMozc.Any(c => userWords?.Invoke(c.Clause.Reading).Contains(c.Clause.Text) == true);
        return plausible && !hasUserWord ? [new ConversionClause(hiragana, zenzai!, Zenzai: true)] : [.. fromMozc.Select(c => c.Clause)];
    }

    /// <summary>Zenzai を使わない、文節に区切った変換 (英日の区切りの案の変換用。IStandaloneConverter)。</summary>
    public IReadOnlyList<ConversionClause>? ConvertClausesStandalone(string hiragana, string? context = null) =>
        mozc.ConvertWithCandidates(hiragana, context)?.Select(s => s.Clause).ToList();

    /// <summary>
    /// text を、文節ごとに候補を 1 つずつ選んでつなげて作れるなら、文節の区切りはそのままで、選んだ候補を文字列にした文節の並び。
    /// 作れなければ null。候補は順位の高い方から試す (同じ text を作れる選び方が複数あるとき、変換エンジンの順位に近い方)。
    /// 変換エンジンは読みの中の半角の英数字を全角にする (15にち → １５日) ので、文節の読みに半角の英数字があれば、
    /// 候補と text のその部分を半角にそろえてから比べる (CompositionController.NormalizeHalfWidth と同じ。返す文字列も半角)。
    /// 助詞だけの文節 (に・で など) は、かなの候補しか選ばない。CompositionController は助詞だけの文節をかなに戻すので、
    /// ほかの候補で作ると Zenzai の結果と違ってしまう (にもつを|に|かいに の に を 2 にすると、荷物をに階に になる)。
    /// 最初の候補が userWords の語の文節は、最初の候補しか選ばない。
    /// </summary>
    internal static IReadOnlyList<ConversionClause>? Align(IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)> clauses, string text,
        Func<string, IReadOnlyList<string>>? userWords = null)
    {
        var lists = clauses.Select(c => JapaneseParticles.IsParticle(c.Clause.Reading) ? [c.Clause.Reading]
            : userWords?.Invoke(c.Clause.Reading).Contains(c.Clause.Text) == true ? [c.Clause.Text]
            : (c.Candidates.Contains(c.Clause.Text) ? c.Candidates : [c.Clause.Text, .. c.Candidates])
                .Select(candidate => CompositionController.NormalizeHalfWidth(candidate, c.Clause.Reading)).ToList()).ToList();
        var chosen = new string[clauses.Count];
        var dead = new HashSet<(int Clause, int Position)>();
        return Fill(0, 0) ? clauses.Select((c, i) => new ConversionClause(c.Clause.Reading, chosen[i])).ToList() : null;

        // clause 番目から後ろの文節で、text の position 文字目から後ろを作れるか (作れなかった組は覚えて、もう試さない)
        bool Fill(int clause, int position)
        {
            if (clause == clauses.Count) return position == text.Length;
            if (dead.Contains((clause, position))) return false;
            foreach (var candidate in lists[clause])
            {
                if (candidate.Length == 0 || position + candidate.Length > text.Length) continue;
                var reading = clauses[clause].Clause.Reading;
                var matches = reading.Any(char.IsAsciiLetterOrDigit)
                    ? CompositionController.NormalizeHalfWidth(text.Substring(position, candidate.Length), reading) == candidate
                    : text.AsSpan(position, candidate.Length).SequenceEqual(candidate);
                if (!matches) continue;
                if (!Fill(clause + 1, position + candidate.Length)) continue;
                chosen[clause] = candidate;
                return true;
            }
            dead.Add((clause, position));
            return false;
        }
    }

    /// <summary>確定した文節を Mozc に覚えさせる。</summary>
    public void Learn(string? context, IReadOnlyList<ConversionClause> clauses) => mozc.Learn(context, clauses);

    /// <summary>読みの候補の一覧 (Mozc)。</summary>
    public IReadOnlyList<string> Candidates(string reading) => mozc.Candidates(reading);

    /// <summary>文字列 (漢字かな混じり) の読み。かなだけならそのまま (カタカナはひらがなに)。再変換とユーザー辞書の登録で使う。分からなければ null。</summary>
    public string? Reading(string text) =>
        text.All(c => KanaChars.IsHiraganaOrLongMark(c) || KanaChars.IsKatakana(c)) ? KanaChars.ToHiragana(text) : mozc.Reading(text);
}
