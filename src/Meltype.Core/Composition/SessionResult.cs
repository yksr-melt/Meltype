// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Diagnostics;
using Meltype.Dictionary;
using Meltype.Engine;

namespace Meltype.Composition;

/// <summary>
/// 入力欄への書き込み 1 回分。キャレットの前の Expect (DeleteBefore 文字) を消してから Text を入れる (確定し直すとき以外は DeleteBefore が 0)。
/// 確定し直しは、ImeSession がフロントの渡したキャレットの前の文字と Expect を照合してから出す。フロントは照合せずに、Expect の長さ (入力欄の単位) だけ消す。
/// Current は、確定し直すときに Text の後ろに続けて入る、今確定した語 (先に今の語を入れてから前を書き換えるフロント向け)。
/// CaretBack は、この確定を入れた後、末尾から左へ戻す UTF-16 の文字数。クリックなどで入力先が移ったときは、フロント側で移動を適用しない。
/// </summary>
/// Paste は、キーを送って文字を入れるフロント (Meltype キーボード) が、貼り付けで入れるか (アプリ別設定の「確定した文字の入れ方」・改行を含む文)。
public readonly record struct TextEdit(int DeleteBefore, string Text, string? Expect = null, string? Current = null, int CaretBack = 0, bool Paste = false);

/// <summary>
/// 1 回のキー入力の結果。Consumed が false ならそのキーはアプリにそのまま渡す (Commits を入れた後で)。
/// View は変換ボックスの内容 (null なら変換ボックスを閉じる)。
/// </summary>
public sealed record SessionResult(bool Consumed, IReadOnlyList<TextEdit> Commits, CompositionView? View)
{
    /// <summary>
    /// フロントに渡す JSON にする (NativeAOT でも使えるよう手書き)。View は <see cref="ForFront"/> で作ったもの。
    /// 項目は値が無くても毎回すべて出す (null・0・false)。view と candidates だけは、出さないときに丸ごと null。
    /// 項目を足したら、各フロントが読むか使わないかを FrontProtocolTests で決める。
    /// 確定し直す (Commits の DeleteBefore が 1 以上) なら、消す文字 (Expect。ImeSession がキャレットの前と照合済み) を removeBefore で渡す。
    /// </summary>
    public string ToJson()
    {
        var builder = new StringBuilder();
        builder.Append("{\"consumed\":").Append(Consumed ? "true" : "false").Append(",\"commits\":[");
        for (var i = 0; i < Commits.Count; i++)
        {
            if (i > 0) builder.Append(',');
            var rewrite = Commits[i].DeleteBefore > 0;
            builder.Append("{\"text\":");
            AppendString(builder, Commits[i].Text);
            builder.Append(",\"removeBefore\":");
            AppendNullable(builder, rewrite ? Commits[i].Expect ?? throw new InvalidOperationException("確定し直す文字が照合されていない") : null);
            builder.Append(",\"currentWord\":");
            AppendNullable(builder, rewrite ? Commits[i].Current : null);
            builder.Append(",\"moveCaretBack\":").Append(Commits[i].CaretBack);
            builder.Append(",\"insertByPaste\":").Append(Commits[i].Paste ? "true" : "false");
            builder.Append('}');
        }
        builder.Append("],\"view\":");
        if (View is not { } view)
        {
            builder.Append("null}");
            return builder.ToString();
        }
        builder.Append("{\"text\":");
        AppendString(builder, view.Text);
        builder.Append(",\"converting\":").Append(view.Converting ? "true" : "false");
        builder.Append(",\"clauses\":");
        AppendArray(builder, view.Clauses ?? []);
        builder.Append(",\"selectedClause\":").Append(view.SelectedClause);
        builder.Append(",\"hint\":");
        AppendString(builder, view.Hint);
        builder.Append(",\"spellingSuggestion\":");
        AppendNullable(builder, view.Suggestion);
        builder.Append(",\"typedKeys\":");
        AppendNullable(builder, view.Typed);
        builder.Append(",\"candidates\":");
        var predictions = ListsPredictions(view) == true;
        var items = predictions ? view.Predictions! : view.Candidates;
        if (items.Count == 0)
        {
            builder.Append("null}}");
            return builder.ToString();
        }
        builder.Append("{\"items\":");
        AppendArray(builder, items);
        // 候補ごとの注記を表示用の文字列にする (注記が無ければ null)
        builder.Append(",\"notes\":[");
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) builder.Append(',');
            AppendNullable(builder, predictions ? null : view.Notes?[i].ToDisplayText());
        }
        builder.Append("],\"numbered\":").Append(Numbered(view) ? "true" : "false");
        builder.Append(",\"selected\":").Append(predictions ? view.SelectedPrediction : view.SelectedIndex)
            .Append(",\"selectedMeaning\":");
        AppendNullable(builder, view.Meaning);
        // 一覧・view・result を閉じる
        builder.Append(",\"meaningDelayMs\":").Append(MeaningDelayMs).Append("}}}");
        return builder.ToString();
    }

    /// <summary>選んでいる候補で止まってから、その意味を出すまでの時間 (ミリ秒)。</summary>
    public const int MeaningDelayMs = 1500;

    /// <summary>フロントに渡した表示 (<see cref="ForFront"/>) の一覧に番号を出すか。</summary>
    public static bool Numbered(CompositionView view) => view.Numbered;

    /// <summary>フロントに渡した表示 (<see cref="ForFront"/>) の一覧が、予測変換の候補か。一覧が無ければ null。</summary>
    public static bool? ListsPredictions(CompositionView view) =>
        view.Predictions is { Count: > 0 } ? true : view.Candidates.Count > 0 ? false : null;

    /// <summary>
    /// フロントに渡す表示。どのフロントも、ここに入っているものをそのまま出す:
    /// text は確定される形 (OS が表示のまま確定しても、本体の確定と同じになる。打っている途中の英字は typed で見える)、
    /// hint は操作ガイドの設定を反映した案内、typed は text と違うときだけの、打ったもの、
    /// 一覧は 1 つだけ (変換中で候補が 2 つ以上なら変換の候補、変換していなければ予測変換の候補、無ければ入力中の変換の候補)。
    /// </summary>
    /// <param name="numberKeysSelect">設定「変換中の数字キーで候補を選ぶ」。番号は、数字キーで選べる変換中の候補だけに出す。</param>
    internal static CompositionView ForFront(CompositionView view, bool showGuide, bool numberKeysSelect)
    {
        var predictions = !view.Converting && view.Predictions is { Count: > 0 };
        var candidates = !predictions && (view.Converting ? view.Candidates.Count > 1 : view.Candidates.Count > 0);
        var text = view.Committed ?? view.Text;
        return view with
        {
            Text = text,
            Hint = showGuide ? view.Hint : view.ContextHint ?? "",
            Typed = view.Typed == text ? null : view.Typed,
            Candidates = candidates ? view.Candidates : [],
            SelectedIndex = candidates ? view.SelectedIndex : -1,
            Notes = candidates ? view.Notes : null,
            Meaning = candidates ? view.Meaning : null,
            Predictions = predictions ? view.Predictions : null,
            SelectedPrediction = predictions ? view.SelectedPrediction : -1,
            Numbered = candidates && view.Converting && numberKeysSelect,
        };
    }

    private static void AppendNullable(StringBuilder builder, string? text)
    {
        if (text is null) builder.Append("null");
        else AppendString(builder, text);
    }

    private static void AppendArray(StringBuilder builder, IReadOnlyList<string> items)
    {
        builder.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) builder.Append(',');
            AppendString(builder, items[i]);
        }
        builder.Append(']');
    }

    private static void AppendString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}

/// <summary>
/// 入力欄の本体 (<see cref="Ime.ImeSession"/>) を作る部品。プロセスで 1 つ作り、入力欄で共有する。
/// 本物は Ime の MeltypeRuntime.SessionParts で作り、テストは偽の変換エンジン・学習データで作る。
/// </summary>
internal sealed record SessionParts(CompositionDetector Detector, IKanjiConverter Converter, CompositionServices Services)
{
    /// <summary>英数状態で打ち始めた英字がローマ字 (日本語) かの判定 (打った英字, もう続きが無いか, 入力欄のアプリの設定)。null なら判定しない。</summary>
    public Func<string, bool, Settings, Verdict>? ClassifyDirect { get; init; }

    /// <summary>漢字かな交じりの文の読み (再変換)。分からなければ null を返す。</summary>
    public Func<string, string?> ReadingOf { get; init; } = _ => null;

    /// <summary>今再生している曲 (OS の機能)。読めなければ null を返す。</summary>
    public Func<(string Title, string Artist)?> NowPlaying { get; init; } = () => null;

    /// <summary>キーを処理した時刻 (ミリ秒。テストでは打った間隔を決める)。</summary>
    public Func<long> TimeMs { get; init; } = () => Environment.TickCount64;

    /// <summary>ユーザーが判定を直した (トレイの「直した誤判定を報告...」)。</summary>
    public Action<Correction>? Corrected { get; init; }

    /// <summary>
    /// 変換エンジンの答えを後から受け取れるフロントか (Converter の <see cref="IKanjiConverter.ResultReady"/> を受けて ConversionFinished を送る)。
    /// true なら入力スレッドは答えを待たない (Space・確定のときだけ少し待つ)。false なら答えるまで待つ (--repro・--self-test)。
    /// 出来事ごとに読む (フロントは入力欄を作った後に、答えを後から受け取ると知らせることがある: fcitx5)。
    /// </summary>
    public Func<bool> LateResults { get; init; } = () => false;

    /// <summary>リアルタイム敬語変換の ON / OFF を切り替え、切り替え後の状態を返す (全 OS 共通の判断を Core に集約)。</summary>
    public Func<bool>? ToggleKeigoMode { get; init; }
}
