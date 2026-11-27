// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Meltype.Config;
using Meltype.Dictionary;

namespace Meltype.SettingsApp;

/// <summary>定型文 (snippets.json) の登録・直す・消す・取り込み・書き出し。一覧で選ぶと下の欄に出るので、直して「登録」すれば上書きする。</summary>
internal static class SnippetsPage
{
    private sealed record Item(string Name, string Text);

    private static SnippetStore Open() => new(Path.Combine(AppPaths.DataDirectory, "snippets.json"));

    public static Control Build(Window owner)
    {
        var mark = new SettingsFile(AppPaths.ConfigFile).Current.SnippetMark is { Length: > 0 } m ? m : ";";
        var name = new TextBox { PlaceholderText = "名前 (半角の英数字・記号。例: sig)", Width = 280, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var text = new TextBox { PlaceholderText = "文 (複数行でもよい)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
        var message = Pages.Message();
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            MaxHeight = 360,
            ItemTemplate = new FuncDataTemplate<Item>((item, _) => new TextBlock
            {
                // 複数行の文は 1 行目だけを出す
                Text = item is null ? "" : $"{mark}{item.Name}　{item.Text.Split('\n')[0]}{(item.Text.Contains('\n') ? " …" : "")}",
                TextTrimming = TextTrimming.CharacterEllipsis,
            }),
        };

        void Reload() => list.ItemsSource = Open().Entries().Select(e => new Item(e.Name, e.Text)).ToList();

        void Register()
        {
            var key = (name.Text ?? "").Trim();
            if (key.StartsWith(mark, StringComparison.Ordinal)) key = key[mark.Length..];
            var body = text.Text ?? "";
            var error = !SnippetStore.IsValidName(key) ? "名前は、空白と ; を使わない半角の英数字・記号にしてください (例: sig、ty、pr-template)。"
                : body.Length == 0 ? "文を入れてください。"
                : !SnippetStore.IsValidText(body) ? "文に、改行・タブ以外の制御文字は使えません。"
                : null;
            if (error is not null)
            {
                Pages.Show(message, error, error: true);
                return;
            }
            var store = Open();
            var replaced = store.Get(key) is not null;
            store.Set(key, body);
            Pages.Show(message, replaced ? $"{mark}{key} を直しました。" : $"{mark}{key} を登録しました。");
            name.Text = "";
            text.Text = "";
            Reload();
            name.Focus();
        }

        void RemoveSelected()
        {
            var selected = list.SelectedItems?.OfType<Item>().ToList() ?? [];
            if (selected.Count == 0) return;
            var store = Open();
            foreach (var item in selected) store.Remove(item.Name);
            Pages.Show(message, $"{selected.Count} 件削除しました。");
            Reload();
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItems is not [Item item]) return;
            name.Text = item.Name;
            text.Text = item.Text;
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) RemoveSelected();
        };

        var import = SettingEditors.Button("取り込む…", async () =>
        {
            if (await Pages.OpenFile(owner, "定型文を取り込む", "定型文", ["*.json"]) is not { } bytes) return;
            try
            {
                var count = Open().Import(Encoding.UTF8.GetString(bytes));
                Pages.Show(message, $"{count} 件取り込みました (同じ名前は取り込んだ方にしました)。");
            }
            catch (System.Text.Json.JsonException ex)
            {
                Pages.Show(message, $"取り込めませんでした: {ex.Message}", error: true);
            }
            Reload();
        });
        var export = SettingEditors.Button("書き出す…", async () =>
        {
            var store = Open();
            if (await Pages.SaveFile(owner, "定型文を書き出す", "定型文", "*.json", $"Meltype-定型文-{DateTime.Now:yyyyMMdd}.json", Encoding.UTF8.GetBytes(store.Export())))
                Pages.Show(message, $"{store.Count} 件書き出しました。");
        });

        Reload();
        return Pages.Layout("定型文", $"行の先頭か空白の直後に {mark}名前 と打って Space / Tab を押すと、登録した文に置き換えます。直後に Esc で元に戻します。",
            Pages.Card(name, text, Pages.Row(SettingEditors.Button("登録", Register)), message),
            list,
            Pages.Row(SettingEditors.Button("選んだ定型文を削除", RemoveSelected), import, export));
    }
}
