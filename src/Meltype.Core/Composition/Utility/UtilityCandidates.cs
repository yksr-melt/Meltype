// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.Composition;

/// <summary>
/// 読みに応じて、変換エンジンの答えとは別に候補を足すユーティリティ変換 (いま → 17:22、れいわ8ねん → 2026年)。
/// 足すときは、このインターフェースを実装して <see cref="UtilityCandidates"/> の一覧に入れる。
/// </summary>
internal interface IUtilityCandidateProvider
{
    /// <summary>読みに対する候補。対象の読みでなければ null (次の provider に聞く。最初に答えた provider の候補だけを出す)。</summary>
    IEnumerable<string>? Candidates(string reading);

    /// <summary>打つたびに変わる候補か。選んでも学習しない (覚えると、次に打ったときに古い日時が最初に出る)。</summary>
    bool IsVolatile(string reading, string text) => false;
}

/// <summary>ユーティリティ変換の一覧。先に書いた provider を優先する。</summary>
internal sealed class UtilityCandidates
{
    private readonly TypedDateCandidateProvider _typedDate;
    private readonly IUtilityCandidateProvider[] _providers;

    public UtilityCandidates(CompositionOptions options, ICompositionHost host)
    {
        _typedDate = new TypedDateCandidateProvider(options);
        _providers =
        [
            _typedDate,
            new SeasonalGreetingProvider(options),
            new EraYearProvider(),
            new TraditionalUnitProvider(),
            new ClipboardCandidateProvider(options, host),
            new ClockCandidateProvider(options),
            new NowCandidateProvider(options, host),
        ];
    }

    public IEnumerable<string> Candidates(string reading)
    {
        foreach (var provider in _providers)
        {
            if (provider.Candidates(reading) is { } candidates) return candidates;
        }
        return [];
    }

    public bool IsVolatile(string reading, string text) => _providers.Any(p => p.IsVolatile(reading, text));

    /// <summary>月/日 (10/20) の日付の候補。月/日でなければ空。</summary>
    public string[] SlashDate(string raw) => _typedDate.SlashDate(raw) ?? [];
}
