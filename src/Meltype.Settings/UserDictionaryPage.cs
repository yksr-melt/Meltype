// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Meltype.Config;
using Meltype.Ime;
using Meltype.Dictionary;

namespace Meltype.SettingsApp;

/// <summary>ユーザー辞書 (userdict.txt) の登録・編集・削除・取り込み・書き出し。入力している Meltype は、次に入力を始めたときに読み直す。</summary>
internal static class UserDictionaryPage
{
    public const string Title = "ユーザー辞書";

    // 同梱の語 (phrases.txt) は読まない。この画面で扱うのは userdict.txt の語だけ
    private static UserDictionary Open() => new(AppPaths.UserDictionaryFile, builtIn: false);

    /// <param name="newWord">登録欄に入れておく語と読み (Windows の単語の登録のホットキーで、選んでいた語)。</param>
    public static Control Build(Window owner, UserWord? newWord = null)
    {
        var typed = new TypedReading(AppPaths.DataDirectory);

        var reading = new TextBox { PlaceholderText = "読み (ローマ字・ひらがな)", Width = 220, Text = newWord?.Reading };
        var preview = Pages.Hint("");
        preview.VerticalAlignment = VerticalAlignment.Center;
        var word = new TextBox { PlaceholderText = "単語", Width = 220, Text = newWord?.Word };
        // 分類: 登録・取り込み・「分類を変える」で語に付ける名前 (Google 日本語入力の辞書を分ける使い方の代わり)
        var category = new AutoCompleteBox { PlaceholderText = "分類 (なくてもよい)", Width = 220, FilterMode = AutoCompleteFilterMode.Contains };
        const string AllCategories = "すべての分類";
        const string NoCategory = "分類なし";
        var categoryFilter = new ComboBox { MinWidth = 160 };
        var message = Pages.Message();
        var filter = new TextBox { PlaceholderText = "絞り込み (読み・単語)", Width = 220 };
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            MaxHeight = 420,
            ItemTemplate = new FuncDataTemplate<UserWord>((w, _) => new TextBlock { Text = w is null ? "" : $"{w.Reading}　→　{w.Word}" + (w.Category.Length > 0 ? $"　［{w.Category}］" : "") }),
        };
        var count = Pages.Hint("");
        UserWord? editing = null;

        // 絞り込みの一覧を作り直すと SelectionChanged が出るので、作り直している間は読み直さない
        var reloading = false;
        void Reload()
        {
            if (reloading) return;
            reloading = true;
            try
            {
                Fill();
            }
            finally
            {
                reloading = false;
            }
        }

        void Fill()
        {
            var query = (filter.Text ?? "").Trim();
            var dictionary = Open();
            var categories = dictionary.Categories;
            var chosen = categoryFilter.SelectedItem as string ?? AllCategories;
            List<string> choices = [AllCategories, NoCategory, .. categories];
            if (!choices.Contains(chosen)) chosen = AllCategories;
            categoryFilter.ItemsSource = choices;
            categoryFilter.SelectedItem = chosen;
            category.ItemsSource = categories;
            // 新しく登録したものから
            var shown = dictionary.Words.Reverse()
                .Where(w => chosen == AllCategories || w.Category == (chosen == NoCategory ? "" : chosen))
                .Where(w => query.Length == 0 || w.Reading.Contains(query) || w.Word.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            list.ItemsSource = shown;
            count.Text = shown.Count == dictionary.Count ? $"{dictionary.Count} 語" : $"{shown.Count} / {dictionary.Count} 語";
        }

        var edit = new Button { Content = "編集" };
        var update = new Button { Content = "更新" };
        edit.Click += (_, e) =>
        {
            e.Handled = true;
            EditSelected();
        };
        update.Click += (_, e) =>
        {
            e.Handled = true;
            UpdateWord();
        };

        void RefreshButtons()
        {
            edit.IsEnabled = list.SelectedItems?.Count == 1;
            var kana = typed.ToHiragana(reading.Text ?? "").Trim();
            var text = (word.Text ?? "").Trim();
            update.IsEnabled = editing is { } target && CanUpdate(target, kana, text);
        }

        static bool CanUpdate(UserWord target, string kana, string text) =>
            kana.Length >= UserDictionary.MinRegisteredReadingLength && text.Length > 0
            && (kana != target.Reading || text != target.Word);

        void EditSelected()
        {
            if (list.SelectedItems?.Count != 1 || list.SelectedItem is not UserWord selected) return;
            editing = selected;
            reading.Text = selected.Reading;
            word.Text = selected.Word;
            RefreshButtons();
            Pages.Show(message, "読みか単語を直して「更新」を押してください。");
            reading.Focus();
        }

        void UpdateWord()
        {
            if (editing is not { } target) return;
            var kana = typed.ToHiragana(reading.Text ?? "").Trim();
            var text = (word.Text ?? "").Trim();
            if (!CanUpdate(target, kana, text)) return;
            try
            {
                if (Open().Edit(target, kana, text) is { } error)
                {
                    Pages.Show(message, error, error: true);
                    return;
                }
            }
            catch (IOException ex)
            {
                Pages.Show(message, $"更新できませんでした: {ex.Message}", error: true);
                return;
            }
            FinishEntry("更新しました。");
        }

        void FinishEntry(string text)
        {
            editing = null;
            Pages.Show(message, text);
            reading.Text = "";
            word.Text = "";
            Reload();
            RefreshButtons();
            reading.Focus();
        }

        void Register()
        {
            var kana = typed.ToHiragana(reading.Text ?? "");
            if (Open().Add(kana, word.Text ?? "", category.Text ?? "") is { } error)
            {
                Pages.Show(message, error, error: true);
                return;
            }
            FinishEntry($"登録しました: {kana} → {(word.Text ?? "").Trim()}");
        }

        void RemoveSelected()
        {
            var selected = list.SelectedItems?.OfType<UserWord>().ToList() ?? [];
            if (selected.Count == 0) return;
            try
            {
                var dictionary = Open();
                foreach (var w in selected) dictionary.Remove(w);
                Pages.Show(message, $"{selected.Count} 語を削除しました。");
            }
            catch (IOException ex)
            {
                Pages.Show(message, $"削除できませんでした: {ex.Message}", error: true);
            }
            Reload();
        }

        void SetCategoryOfSelected()
        {
            var selected = list.SelectedItems?.OfType<UserWord>().ToList() ?? [];
            if (selected.Count == 0) return;
            var name = (category.Text ?? "").Trim();
            try
            {
                Open().SetCategory(selected, name);
                Pages.Show(message, name.Length > 0 ? $"{selected.Count} 語を「{name}」に分けました。" : $"{selected.Count} 語の分類を外しました。");
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                Pages.Show(message, $"分類を変えられませんでした: {ex.Message}", error: true);
            }
            Reload();
        }

        categoryFilter.SelectionChanged += (_, _) =>
        {
            Reload();
        };
        reading.TextChanged += (_, _) =>
        {
            preview.Text = (reading.Text ?? "").Any(char.IsAsciiLetter) ? "→ " + typed.ToHiragana(reading.Text!) : "";
            RefreshButtons();
        };
        word.TextChanged += (_, _) => RefreshButtons();
        list.SelectionChanged += (_, _) => RefreshButtons();
        filter.TextChanged += (_, _) => Reload();
        foreach (var box in new[] { reading, word })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                if (editing is null) Register();
                else UpdateWord();
            };
        }
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) RemoveSelected();
        };

        var import = SettingEditors.Button("取り込む…", async () =>
        {
            if (await Pages.OpenFile(owner, "ユーザー辞書を取り込む", "辞書のテキストファイル・macOS のユーザ辞書", ["*.txt", "*.plist"]) is not { } bytes) return;
            try
            {
                var result = UserDictionaryFile.Parse(bytes);
                // 取り込んだ語には、分類の欄の名前を付ける (辞書ファイルごとに分けられるように)
                var name = (category.Text ?? "").Trim();
                var added = Open().AddRange(result.Words.Select(w => w with { Category = name }));
                var duplicates = result.Words.Count - added;
                Pages.Show(message, $"{added} 語を登録しました。" + (duplicates > 0 ? $" 登録済みの語: {duplicates}。" : "") +
                    (result.Skipped > 0 ? $" 読みがかなでない・短すぎるなどで飛ばした行: {result.Skipped}。" : ""));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or System.Xml.XmlException)
            {
                Pages.Show(message, $"取り込めませんでした: {ex.Message}", error: true);
            }
            Reload();
        });
        var export = SettingEditors.Button("書き出す…", async () =>
        {
            var dictionary = Open();
            if (await Pages.SaveFile(owner, "ユーザー辞書を書き出す", "辞書のテキストファイル", "*.txt", $"Meltype-ユーザー辞書-{DateTime.Now:yyyyMMdd}.txt", UserDictionaryFile.Export(dictionary.Words)))
                Pages.Show(message, $"{dictionary.Count} 語を書き出しました。Microsoft IME・Google 日本語入力・ATOK の辞書ツールで取り込めます。");
        });
        var remove = SettingEditors.Button("選んだ語を削除", RemoveSelected);
        var setCategory = SettingEditors.Button("選んだ語を分類の欄の名前に分ける", SetCategoryOfSelected);

        Reload();
        RefreshButtons();
        return Pages.Layout(Title, "登録した語は、読みを変換したときに候補の先頭に出ます。読みはローマ字でも打てます。分類を付けると、分類ごとに絞り込めます。",
            Pages.Card(
                Pages.Row(reading, preview),
                Pages.Row(word),
                Pages.Row(category),
                Pages.Row(update),
                Pages.Row(SettingEditors.Button("登録", Register)),
                message),
            Pages.Row(filter, categoryFilter, count),
            list,
            Pages.Row(import, export, edit, remove, setCategory));
    }
}
