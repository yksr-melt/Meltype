// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 yksr-melt

namespace Meltype.Composition;

/// <summary>後から届いた変換結果の反映。ユーザーが選んだ候補・文節の区切りを維持する。</summary>
internal static class CompositionRefresh
{
    internal static bool Refresh(CompositionState state, CompositionEnv env)
    {
        state.LiveCache.Clear();
        if (state.Scene is not CompositionScene.WithInput { Input: var input } scene || scene is CompositionScene.Converting open && !RefreshOpenConversion(state, open, env)) return false;
        // 文節が分かると最後の文節の予測が出せる。予測を選んでいる間は、選んでいる候補を置き換えない
        if (input.Predictions is not { Index: >= 0 }) input.Predictions = null;
        CompositionPresentation.UpdateView(state, env);
        return true;
    }

    /// <summary>
    /// 候補を開いた後に届いた答えを反映する。文節の区切りを動かしたあとは置き換えない。
    /// 文節の並びが変わる答えは、候補を選び直したか、別の文節へ移動したあとなら置き換えない (選んでいる文節がずれる)。
    /// 候補を選び直した文節は、選んでいる候補を変えずに、新しい候補を上に入れる。何も変わらなければ false。
    /// 英日の区切りの案を候補にした文全体の文節を選び直したあとも置き換えない。作り直すと選んだ文が新しい案に無くなることがあり、
    /// その案の文節に分けられなくなる (← → で選び直せず、文節ごとの学習もできない)。
    /// </summary>
    private static bool RefreshOpenConversion(CompositionState state, CompositionScene.Converting scene, CompositionEnv env)
    {
        var open = scene.Conversion;
        if (open.Edited || scene.Input.Reconversion is not null || open.Clauses is [{ Alternatives: not null, Changed: true }]) return false;
        var incoming = CompositionConversion.BuildConversion(scene.Input, env.WithoutWaiting(), open.StartedBy, open.PreferJapanese);
        var merged = Merge(open, incoming, CompositionConversion.InitialClause(env, open.Clauses.Count));
        if (ReferenceEquals(merged, open)) return false;
        CompositionLifecycle.Convert(state, scene.Input, merged);
        return true;
    }

    /// <summary>
    /// 新しい文節に選び直した候補を引き継ぐ。採用しないときは open をそのまま返す。
    /// 入力欄の状態・表示・変換エンジンに触れず、前の変換を書き換えない。
    /// </summary>
    private static Conversion? Merge(Conversion open, Conversion? incoming, int initialClause)
    {
        var old = open.Clauses;
        var freshClauses = incoming?.Clauses ?? [];
        var sameClauses = freshClauses.Count == old.Count && freshClauses.Zip(old).All(p => p.First.Reading == p.Second.Reading);
        var nothingNew = sameClauses && freshClauses.Zip(old).All(p => p.First.Text == p.Second.Text && p.First.Candidates.All(p.Second.Candidates.Contains));
        if (nothingNew || !sameClauses && (open.Selected != initialClause || old.Any(c => c.Changed))) return open;
        if (sameClauses)
        {
            foreach (var (fresh, previous) in freshClauses.Zip(old))
            {
                if (!previous.Changed) continue;
                fresh.Candidates = CompositionConversion.Distinct([.. fresh.Candidates, .. previous.Candidates]);
                fresh.Index = fresh.Candidates.IndexOf(previous.Text);
                fresh.Changed = true;
                fresh.Translations.UnionWith(previous.Translations);
                fresh.NotLearned.UnionWith(previous.NotLearned);
            }
        }
        if (incoming is not null && sameClauses) incoming.Selected = open.Selected;
        return incoming;
    }
}
