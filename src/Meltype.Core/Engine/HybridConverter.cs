// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Libraries.Chars;

namespace Meltype.Engine;

/// <summary>
/// Mozc と Zenzai を組み合わせた、全 OS で使う変換エンジン (#1740)。
/// Zenzai が使えればその答えを Mozc の文節に当てはめ、使えなければ Mozc で変換する。Mozc が使えなければ変換しない (かなのまま)。
/// 組み立ては Ime の MeltypeRuntime.CreateConverter。
/// </summary>
internal sealed class HybridConverter(MozcConverter mozc, Func<ZenzaiConverter?>? zenzai = null)
    : IKanjiConverter
{
    private ZenzaiConverter? _subscribedZenzai;
    private Action? _zenzaiReady;

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
        z.ResultReady += OnZenzaiReady;
    }

    private void OnZenzaiReady() => _zenzaiReady?.Invoke();

    /// <summary>
    /// 設定で Zenzai が使えるとき、その変換結果 (読み全体で 1 つの文節)。使えない・変換できなければ null (Mozc で変換する)。
    /// Zenzai の失敗 (作れない・答えない・壊れた答え) は例外にならない。続けて失敗した Zenzai は自分で諦め (GaveUp)、持ち主 (ZenzaiHolder) が止めて作り直す。
    /// </summary>
    private List<ConversionClause>? Zenzai(ConversionRequest request)
    {
        if (zenzai?.Invoke() is not { IsAvailable: true } z) return null;
        Subscribe(z);
        return z.Convert(request)?.Select(c => c with { Zenzai = true }).ToList();
    }

    /// <summary>
    /// Zenzai の答え (answer) を、Mozc の文節ごとの候補を 1 つずつ選んでつなげて作れるか (前の文脈なし)。Mozc が答えなかったら null。
    /// ヘルパーが答えるまで待つので、入力スレッドでは呼ばない (Ime の MeltypeRuntime.ForgetZenzaiHallucinations が裏のスレッドで呼ぶ)。
    /// </summary>
    public bool? IsBuiltFromMozcCandidatesNow(string hiragana, string answer) =>
        mozc.ConvertWithCandidatesNow(hiragana) is { } fromMozc ? Align(fromMozc, answer) is not null : null;

    /// <summary>
    /// Zenzai が使えれば Zenzai の結果。Mozc の結果と同じ文字列なら、そちらの文節の区切りを使う
    /// (Zenzai の結果は読み全体で 1 つの文節で、区切りを動かせないため)。
    /// 違う文字列でも、Mozc の文節ごとの候補を 1 つずつ選んでつなげると作れるなら、Mozc の文節の区切りで返す (Align)。
    /// 1 つの文節のままだと、文節ごとに候補を選び直せず、前に選び直して覚えた変換 (文節の読みごと) も効かない。
    /// Mozc の候補で作れない答えは、かなと記号が読みと合うときだけ使う (<see cref="ZenzaiConverter.IsPlausible"/>・<see cref="ZenzaiConverter.KeepsSymbols"/>)。
    /// 壊れた答え (はーどうぇ → ハードウェアウェア) は使わず、表記が読みと違う語 (きずく → 気づく、あんはさうぇい → アン・ハサウェイ) は Mozc の候補にあれば使う。
    /// Mozc がユーザー辞書の語を含む候補にした文節 (しのが → 紫乃が、とった → 撮った) は、Zenzai の答えで置き換えない (Mozc のコストで選んだ登録語を優先する)。
    /// その文節を Mozc の答えのままにして Zenzai の答えを作れなければ、Mozc の結果を使う。
    /// request.Zenzai が false なら Mozc だけで変換する。
    /// </summary>
    public IReadOnlyList<ConversionClause>? Convert(ConversionRequest request)
    {
        var fromMozc = mozc.ConvertWithCandidates(request);
        if (!request.Zenzai || Zenzai(request) is not { Count: > 0 } z) return fromMozc?.Select(s => s.Clause).ToList();
        return Choose(request.Hiragana, fromMozc, z, request.Before);
    }

    /// <summary><see cref="Convert"/> の、Zenzai の答え z と Mozc の結果 fromMozc から使う結果を選ぶところ。</summary>
    internal static IReadOnlyList<ConversionClause>? Choose(string hiragana, IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)>? fromMozc,
        IReadOnlyList<ConversionClause> z, string? before = null)
    {
        var other = fromMozc?.Select(s => s.Clause).ToList();
        // Zenzai の答えと同じなら、その文節も Zenzai の答えとして印を付ける
        if (other is not null && string.Concat(other.Select(c => c.Text)) == z[0].Text) return other.Select(c => c with { Zenzai = true }).ToList();
        // 候補は、この変換で文節と一緒に受け取ったものを使う (ヘルパーには聞き直さない)。
        // 文節の読みは Mozc が返したまま (全角の（ を ( にするなど、読み全体と少し違うことがある。Mozc だけで変換するときと同じ)
        if (fromMozc is not null && fromMozc.Any(s => s.Clause.UserWord))
        {
            var kept = fromMozc.Select(s => s.Clause.UserWord ? (s.Clause, (IReadOnlyList<string>)[s.Clause.Text]) : s).ToList();
            return Align(kept, z[0].Text)?.Select(c => c with { Zenzai = true }).ToList() ?? other;
        }
        var answer = z[0].Text;
        if (fromMozc is not null && Align(fromMozc, answer) is { } aligned)
            return aligned.Select(c => c with { Zenzai = true }).ToList();
        // 英単語の後ろで、Mozc の候補には無い区切りで助詞を作った答え (python + はしる → は知る) は使わない。
        if (other is [{ Text: var first }, ..] && TextChars.LanguageOf(before) == true && answer != hiragana &&
            HasNewLeadingParticle(hiragana, answer, first)) return other;
        return ZenzaiConverter.KeepsSymbols(hiragana, answer) && ZenzaiConverter.IsPlausible(hiragana, answer) ? z : other;
    }

    private static bool HasNewLeadingParticle(string reading, string answer, string mozc)
    {
        // 最長の共通のかなが付属語で、Mozc の最初の候補にはその境界がないか。
        var end = 0;
        while (end < reading.Length && end < answer.Length && reading[end] == answer[end]) end++;
        return end > 0 && JapaneseParticles.IsAttachedWord(reading[..end]) && !mozc.StartsWith(reading[..end], StringComparison.Ordinal);
    }

    /// <summary>
    /// text を、文節ごとに候補を 1 つずつ選んでつなげて作れるなら、文節の区切りはそのままで、選んだ候補を文字列にした文節の並び。
    /// 作れなければ null。候補は順位の高い方から試す (同じ text を作れる選び方が複数あるとき、変換エンジンの順位に近い方)。
    /// 変換エンジンは読みの中の半角の英数字を全角にする (15にち → １５日) ので、文節の読みに半角の英数字があれば、
    /// 候補と text のその部分を半角にそろえてから比べる (TextChars.NormalizeHalfWidth と同じ。返す文字列も半角)。
    /// 助詞だけの文節 (に・で など) は、かなの候補しか選ばない。CompositionController は助詞だけの文節をかなに戻すので、
    /// ほかの候補で作ると Zenzai の結果と違ってしまう (にもつを|に|かいに の に を 2 にすると、荷物をに階に になる)。
    /// </summary>
    internal static IReadOnlyList<ConversionClause>? Align(IReadOnlyList<(ConversionClause Clause, IReadOnlyList<string> Candidates)> clauses, string text)
    {
        var lists = clauses.Select(c => JapaneseParticles.IsParticle(c.Clause.Reading)
            ? [c.Clause.Reading]
            : (c.Candidates.Contains(c.Clause.Text) ? c.Candidates : [c.Clause.Text, .. c.Candidates])
                .Select(candidate => TextChars.NormalizeHalfWidth(candidate, c.Clause.Reading)).ToList()).ToList();
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
                    ? TextChars.NormalizeHalfWidth(text.Substring(position, candidate.Length), reading) == candidate
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

    /// <summary>Mozc のユーザー辞書を words で入れ替える (<see cref="MozcConverter.SetUserDictionary"/>)。</summary>
    public bool SetUserDictionary(IReadOnlyList<(string Reading, string Word, string Pos)> words) => mozc.SetUserDictionary(words);

    /// <summary>読みの候補の一覧 (Mozc。deadline まで待つ)。</summary>
    public IReadOnlyList<string> Candidates(string reading, long deadline) => mozc.Candidates(reading, deadline);

    /// <summary>文字列 (漢字かな混じり) の読み。かなだけならそのまま (カタカナはひらがなに)。再変換とユーザー辞書の登録で使う。分からなければ null。</summary>
    public string? Reading(string text) =>
        text.All(c => KanaChars.IsHiraganaOrLongMark(c) || KanaChars.IsKatakana(c)) ? KanaChars.ToHiragana(text) : mozc.Reading(text);
}
