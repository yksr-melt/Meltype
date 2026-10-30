// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// Mozc と OS の変換エンジン (Windows では Microsoft IME) を組み合わせる。
/// 両方 (既定) なら Mozc で変換し、Mozc が使えない・変換できないときは OS の変換エンジンで変換する。
/// 候補の一覧は Mozc の候補の後ろに OS の変換エンジンの候補を足す (Microsoft IME に登録した単語も出る)。
/// </summary>
/// <param name="zenzaiFailed">変換で例外になった Zenzai を、持ち主 (zenzai を渡した側) に知らせる。止めて作り直すのは持ち主が行う。</param>
public sealed class HybridConverter(Func<ConversionEngine> engine, MozcConverter? mozc, IKanjiConverter system,
    Func<string, IReadOnlyList<string>>? systemCandidates, Func<ZenzaiConverter?>? zenzai = null, Action<ZenzaiConverter>? zenzaiFailed = null)
    : IKanjiConverter, ILearningConverter, IWideContextConverter, IStandaloneConverter
{
    /// <summary>Zenzai に渡す広い文脈 (Mozc / Microsoft IME には渡さない)。</summary>
    public string? WideContext { get; set; }

    /// <summary>
    /// まだ処理していない打鍵が溜まっているか。溜まっているあいだは、途中の読みの変換で Zenzai の答えを待たない (issue #1209)。
    /// ライブ変換は 1 打鍵ごとに Zenzai の答えを待つので、速く打つと Zenzai が追いつかず、溜まった打鍵の数だけ待って入力が数秒止まった。
    /// 途中の読みはすぐ次の打鍵で変わるので、今までの変換エンジンで変換し、溜まった打鍵を処理し終えた読みだけ Zenzai に頼む。
    /// </summary>
    public Func<bool>? KeysPending { get; set; }

    /// <summary>
    /// 設定で Zenzai が使えるとき、その変換結果 (読み全体で 1 つの文節)。使えない・変換できなければ null。
    /// Zenzai を作れない・変換で例外になったときも null (Mozc などで変換する)。Zenzai の失敗で入力全体を止めないように
    /// (#973: 同梱の .NET に System.Net.Http が無く、Zenzai を作るたびに例外になって、日本語を打てなくなった)。
    /// 変換で例外になった Zenzai は、それからは使わず (打つたびに例外にしない)、持ち主に知らせる (zenzaiFailed)。
    /// 止める (Dispose) のは持ち主で、ここでは止めない。持ち主が新しく作ったら、それは使う。
    /// </summary>
    private IReadOnlyList<ConversionClause>? Zenzai(string hiragana, string? context)
    {
        ZenzaiConverter? z = null;
        try
        {
            if (KeysPending?.Invoke() == true) return null;
            z = zenzai?.Invoke();
            if (z is null || ReferenceEquals(z, _failedZenzai) || !z.IsAvailable) return null;
            return z.ConvertClauses(hiragana, WideContext ?? context);
        }
        catch (Exception ex)
        {
            var failed = (object?)z ?? CouldNotCreate;
            if (!ReferenceEquals(failed, _failedZenzai)) Diagnostics.Log.Error($"Zenzai で変換できないので、今までの変換エンジンで変換します: {ex}");
            _failedZenzai = failed;
            if (z is not null) zenzaiFailed?.Invoke(z);
            return null;
        }
    }

    // 例外になった Zenzai (持ち主が同じものをまた渡しても使わない)。作るところで例外になったときは CouldNotCreate
    // (このときはログを 1 回にするだけ。Windows 版では ZenzaiHolder が作れなかったことを覚えて待つので、ここまで例外は来ない)
    private object? _failedZenzai;
    private static readonly object CouldNotCreate = new();

    private bool UseMozc => engine() != ConversionEngine.System && mozc is { IsAvailable: true };
    private bool UseSystem => engine() != ConversionEngine.Mozc || mozc is not { IsAvailable: true };

    public string? Convert(string hiragana) =>
        (Zenzai(hiragana, null) is { Count: > 0 } z ? string.Concat(z.Select(c => c.Text)) : null) ?? ConvertStandalone(hiragana);

    /// <summary>Zenzai を使わない変換 (文節ごとの候補に足す「その読みだけの変換」用。IStandaloneConverter)。</summary>
    public string? ConvertStandalone(string hiragana) =>
        (UseMozc ? mozc!.Convert(hiragana) : null) ?? (UseSystem ? system.Convert(hiragana) : null);

    /// <summary>
    /// Zenzai が使えれば Zenzai の結果。Mozc などの結果と同じ文字列なら、そちらの文節の区切りを使う
    /// (Zenzai の結果は読み全体で 1 つの文節で、区切りを動かせないため)。
    /// 違う文字列でも、Mozc の文節ごとの候補を 1 つずつ選んでつなげると作れるなら、Mozc の文節の区切りで返す (Align)。
    /// 1 つの文節のままだと、文節ごとに候補を選び直せず、前に選び直して覚えた変換 (文節の読みごと) も効かない。
    /// Mozc の文節で返した結果は、確定すると Mozc にも覚えさせる (Mozc の候補に無い 1 つの文節は、Mozc が覚えない)。
    /// </summary>
    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
    {
        var fromMozc = UseMozc ? mozc!.ConvertWithCandidates(hiragana, context) : null;
        var other = fromMozc?.Select(s => s.Clause).ToList() ?? (UseSystem ? system.ConvertClauses(hiragana, context) : null);
        if (Zenzai(hiragana, context) is not { Count: > 0 } z) return other;
        if (other is not null && string.Concat(other.Select(c => c.Text)) == z[0].Text) return other;
        // 候補は、この変換で文節と一緒に受け取ったものを使う (ヘルパーには聞き直さない)。
        // 文節の読みは Mozc が返したまま (全角の（ を ( にするなど、読み全体と少し違うことがある。Mozc だけで変換するときと同じ)
        return (fromMozc is not null ? Align(fromMozc, z[0].Text) : null) ?? z;
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

    /// <summary>確定した文節を Mozc に覚えさせる (Microsoft IME の学習は Microsoft IME に任せる)。</summary>
    public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
    {
        if (UseMozc) mozc!.Learn(context, clauses);
    }

    /// <summary>読みの候補の一覧 (Mozc → OS の変換エンジン)。</summary>
    public IReadOnlyList<string> Candidates(string reading)
    {
        var list = new List<string>();
        if (UseMozc) list.AddRange(mozc!.Candidates(reading));
        if (engine() != ConversionEngine.Mozc && systemCandidates is not null)
        {
            foreach (var candidate in systemCandidates(reading)) if (!list.Contains(candidate)) list.Add(candidate);
        }
        return list;
    }
}
