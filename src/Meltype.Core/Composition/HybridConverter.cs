// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// Mozc と Zenzai を組み合わせた、全 OS で使う変換エンジン (#1740)。
/// Zenzai が使えればその答えを Mozc の文節に当てはめ、使えなければ Mozc で変換する。Mozc が使えなければ変換しない (かなのまま)。
/// 組み立ては <see cref="MeltypeRuntime.CreateConverter"/>。
/// </summary>
/// <param name="zenzaiFailed">変換で例外になった Zenzai を、持ち主 (zenzai を渡した側) に知らせる。止めて作り直すのは持ち主が行う。</param>
public sealed class HybridConverter(MozcConverter mozc, Func<ZenzaiConverter?>? zenzai = null, Action<ZenzaiConverter>? zenzaiFailed = null)
    : IKanjiConverter, ILearningConverter, IWideContextConverter, IStandaloneConverter, IBackgroundConverter
{
    /// <summary>Zenzai に渡す広い文脈 (Mozc には渡さない)。</summary>
    public string? WideContext { get; set; }

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
            return z.ConvertClauses(hiragana, WideContext ?? context)?.Select(c => c with { Zenzai = true }).ToList();
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
        (Zenzai(hiragana, null) is [var z] && IsUsable(hiragana, z.Text) ? z.Text : null) ?? ConvertStandalone(hiragana);

    /// <summary>Zenzai の答えを使えるか (<see cref="ConvertClauses"/> と同じ決め方): かなが読みと合うか、Mozc の文節ごとの候補をつないで作れるか。</summary>
    private bool IsUsable(string hiragana, string answer) =>
        ZenzaiConverter.IsPlausible(hiragana, answer) || IsBuiltFromMozcCandidates(hiragana, answer);

    private bool IsBuiltFromMozcCandidates(string hiragana, string answer) =>
        mozc.ConvertWithCandidates(hiragana) is { } fromMozc && Align(fromMozc, answer) is not null;

    /// <summary>Zenzai を使わない変換 (文節ごとの候補に足す「その読みだけの変換」用。IStandaloneConverter)。</summary>
    public string? ConvertStandalone(string hiragana) => mozc.Convert(hiragana);

    /// <summary>
    /// Zenzai が使えれば Zenzai の結果。Mozc の結果と同じ文字列なら、そちらの文節の区切りを使う
    /// (Zenzai の結果は読み全体で 1 つの文節で、区切りを動かせないため)。
    /// 違う文字列でも、Mozc の文節ごとの候補を 1 つずつ選んでつなげると作れるなら、Mozc の文節の区切りで返す (Align)。
    /// 1 つの文節のままだと、文節ごとに候補を選び直せず、前に選び直して覚えた変換 (文節の読みごと) も効かない。
    /// Mozc の文節で返した結果は、確定すると Mozc にも覚えさせる (Mozc の候補に無い 1 つの文節は、Mozc が覚えない)。
    /// Mozc の候補で作れない答えは、かなが読みと合うときだけ使う (<see cref="ZenzaiConverter.IsPlausible"/>)。
    /// 壊れた答え (はーどうぇ → ハードウェアウェア) は使わず、表記が読みと違う語 (きずく → 気づく、あんはさうぇい → アン・ハサウェイ) は Mozc の候補にあれば使う。
    /// </summary>
    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
    {
        var fromMozc = mozc.ConvertWithCandidates(hiragana, context);
        var other = fromMozc?.Select(s => s.Clause).ToList();
        if (Zenzai(hiragana, context) is not { Count: > 0 } z) return other;
        // Zenzai の答えと同じなら、その文節も Zenzai の答えとして印を付ける
        if (other is not null && string.Concat(other.Select(c => c.Text)) == z[0].Text) return other.Select(c => c with { Zenzai = true }).ToList();
        // 候補は、この変換で文節と一緒に受け取ったものを使う (ヘルパーには聞き直さない)。
        // 文節の読みは Mozc が返したまま (全角の（ を ( にするなど、読み全体と少し違うことがある。Mozc だけで変換するときと同じ)
        return (fromMozc is not null ? Align(fromMozc, z[0].Text)?.Select(c => c with { Zenzai = true }).ToList() : null)
            ?? (ZenzaiConverter.IsPlausible(hiragana, z[0].Text) ? z : other);
    }

    /// <summary>
    /// text を、文節ごとに候補を 1 つずつ選んでつなげて作れるなら、文節の区切りはそのままで、選んだ候補を文字列にした文節の並び。
    /// 作れなければ null。候補は順位の高い方から試す (同じ text を作れる選び方が複数あるとき、変換エンジンの順位に近い方)。
    /// 変換エンジンは読みの中の半角の英数字を全角にする (15にち → １５日) ので、文節の読みに半角の英数字があれば、
    /// 候補と text のその部分を半角にそろえてから比べる (CompositionController.NormalizeHalfWidth と同じ。返す文字列も半角)。
    /// 助詞だけの文節 (に・で など) は、かなの候補しか選ばない。CompositionController は助詞だけの文節をかなに戻すので、
    /// ほかの候補で作ると Zenzai の結果と違ってしまう (にもつを|に|かいに の に を 2 にすると、荷物をに階に になる)。
    /// </summary>
    internal static IReadOnlyList<ConversionClause>? Align(IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)> clauses, string text)
    {
        var lists = clauses.Select(c => JapaneseParticles.IsParticle(c.Clause.Reading)
            ? [c.Clause.Reading]
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
