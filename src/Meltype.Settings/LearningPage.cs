// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Ime;

namespace Meltype.SettingsApp;

/// <summary>
/// 学習データ: 学習した語の一覧 (英語 / 日本語・選び直した変換) から選んで忘れる、最近覚えたもの・すべてを消す。
/// 入力している Meltype は、消したファイルを 1 秒ほどで読み直す。
/// </summary>
internal static class LearningPage
{
    private sealed record Conversion(string Reading, string Text, DateTime Used);

    private static string Date(DateTime used) => used.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    public static Control Build(Window owner)
    {
        // ローマ字の表を読むので、ページを開くたびに 1 回だけ作る (一覧・消す操作の前に、ファイルを読み直す)
        var data = LearningData.OpenStandalone(AppPaths.DataDirectory);
        var message = Pages.Message();
        var filter = new TextBox { PlaceholderText = "絞り込み (語・読み)", Width = 220 };
        var languages = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            MaxHeight = 320,
            ItemTemplate = new FuncDataTemplate<LearnedLanguage>((e, _) => new TextBlock
            {
                Text = e is null ? "" : $"{e.Word}　→　{(e.English ? "英語 (英字のまま)" : "日本語 (かな)")}{(e.Context is { } context ? $"・{context}" : "")}" +
                    $"　{e.Count} 回{(e.Active ? "" : "・もう一度で覚える")}　{Date(e.Used)}",
            }),
        };
        var conversions = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            MaxHeight = 320,
            ItemTemplate = new FuncDataTemplate<Conversion>((e, _) => new TextBlock { Text = e is null ? "" : $"{e.Reading}　→　{e.Text}　{Date(e.Used)}" }),
        };
        var languagesHeading = Pages.Heading("");
        var conversionsHeading = Pages.Heading("");

        void Reload()
        {
            var query = (filter.Text ?? "").Trim();
            var words = data.Languages().Where(e => query.Length == 0 || e.Word.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            var chosen = data.Conversions().Where(e => query.Length == 0 || e.Reading.Contains(query) || e.Text.Contains(query)).Select(e => new Conversion(e.Reading, e.Text, e.Used)).ToList();
            languages.ItemsSource = words;
            conversions.ItemsSource = chosen;
            languagesHeading.Text = $"英語 / 日本語 ({words.Count})";
            conversionsHeading.Text = $"変換 ({chosen.Count})";
        }

        void ForgetLanguages()
        {
            var keys = languages.SelectedItems?.OfType<LearnedLanguage>().Select(e => e.EntryKey).ToList() ?? [];
            if (keys.Count == 0) return;
            data.ForgetLanguages(keys);
            Pages.Show(message, $"{keys.Count} 語を忘れました。");
            Reload();
        }

        void ForgetConversions()
        {
            var readings = conversions.SelectedItems?.OfType<Conversion>().Select(e => e.Reading).ToList() ?? [];
            if (readings.Count == 0) return;
            foreach (var reading in readings) data.ForgetConversion(reading);
            Pages.Show(message, $"{readings.Count} 語を忘れました。");
            Reload();
        }

        async Task ForgetSince(DateTime sinceUtc, string label)
        {
            if (!await Dialogs.Ask(owner, "学習データを消す", $"{label}に覚えた学習データ (選び直した変換・英字 / かなに直した語・予測変換の語句) を削除します。よろしいですか？", ok: "削除")) return;
            var count = data.ForgetSince(sinceUtc);
            Pages.Show(message, count > 0 ? $"{label}に覚えたものを {count} 件削除しました。" : "削除するものはありませんでした。");
            Reload();
        }

        filter.TextChanged += (_, _) => Reload();
        languages.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) ForgetLanguages();
        };
        conversions.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) ForgetConversions();
        };

        var reset = SettingEditors.Button("すべて (リセット)…", async () =>
        {
            if (!await Dialogs.Ask(owner, "学習データを消す",
                    "学習データ (選び直した変換・英字 / かなに直した語・予測変換の語句・選んだ英訳) をすべて削除します。よろしいですか？\n\nユーザー辞書と設定は消えません。", ok: "すべて削除")) return;
            data.Reset();
            Pages.Show(message, "学習データをすべて削除しました。");
            Reload();
        });

        Reload();
        return Pages.Layout("学習データ", "",
            Pages.Row(filter),
            message,
            Pages.Card(
                languagesHeading,
                Pages.Hint("英字で打った語を、自動の判定ではなく覚えた方 (英語 / 日本語) で出します。間違えて覚えた語は、選んで「忘れる」を押してください。"),
                languages,
                Pages.Row(SettingEditors.Button("選んだ語を忘れる", ForgetLanguages))),
            Pages.Card(
                conversionsHeading,
                Pages.Hint("変換で選び直した語は、次から最初の候補になります (はし → 箸)。"),
                conversions,
                Pages.Row(SettingEditors.Button("選んだ語を忘れる", ForgetConversions))),
            Pages.Card(
                Pages.Heading("学習データを消す"),
                Pages.Hint("間違えて覚えたものを、まとめて消します。ユーザー辞書と設定は消えません。"),
                Pages.Row(
                    SettingEditors.Button("この 1 時間に覚えたもの…", async () => await ForgetSince(DateTime.UtcNow.AddHours(-1), "この 1 時間")),
                    SettingEditors.Button("今日覚えたもの…", async () => await ForgetSince(DateTime.Today.ToUniversalTime(), "今日")),
                    reset)));
    }
}
