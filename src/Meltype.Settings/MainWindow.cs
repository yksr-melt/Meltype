// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia;
using Avalonia.Controls;
using Meltype.Config;
using Meltype.Dictionary;

namespace Meltype.SettingsApp;

/// <summary>左に検索とページの一覧、右に選んだページ。ページは設定の分類と、設定以外の操作 (<see cref="Pages"/>)。</summary>
internal sealed class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly IReadOnlyList<(string Category, IReadOnlyList<SettingItem> Items)> _catalog = SettingsCatalog.ForThisPlatform();
    private readonly IReadOnlyList<Page> _pages;
    private readonly TextBox _search = new() { PlaceholderText = "設定を検索" };
    private readonly ListBox _navigation = new();
    private readonly ScrollViewer _content = new();

    public MainWindow(string[] args)
    {
        Title = "Meltype の設定";
        Width = 1000;
        Height = 720;
        MinWidth = 640;
        MinHeight = 400;
        _pages = [.. _catalog.Select(g => new Page(g.Category, () => SettingsPage.Build([g], _store, this, search: false), ShowsSettings: true)), .. Pages.All(this, () => _addWord),
            // プロファイルは使う人が少ないので、一覧のいちばん下に置く
            new Page("プロファイル", () => ProfilesPage.Build(_store, this), ShowsSettings: true)];
        _navigation.ItemsSource = _pages.Select(p => p.Title).ToList();
        _navigation.SelectionChanged += (_, _) =>
        {
            // ほかのページに移ったら、起動の引数の語は使い終わり
            if (_navigation.SelectedItem as string != UserDictionaryPage.Title) _addWord = null;
            ShowPage();
        };
        _search.TextChanged += (_, _) => ShowPage();
        // ほかのプロセスが config.json を書いたら、出している値を読み直す
        // 設定を出していないページ (ユーザー辞書・定型文など) は、打ちかけの入力を消さないように作り直さない
        _store.Changed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_search.Text) || _content.Tag is Page { ShowsSettings: true }) ShowPage();
        };
        _store.Failed += message => _ = Dialogs.Inform(this, "設定を保存できませんでした", $"{AppPaths.ConfigFile}\n\n{message}");

        var sidebar = new DockPanel { Width = 240, Margin = new Thickness(8) };
        _search.Margin = new Thickness(0, 0, 0, 8);
        sidebar.Children.Add(Dock(_search, Avalonia.Controls.Dock.Top));
        sidebar.Children.Add(_navigation);
        var root = new DockPanel();
        root.Children.Add(Dock(new Border { Classes = { "sidebar" }, Child = sidebar }, Avalonia.Controls.Dock.Left));
        root.Children.Add(_content);
        Content = root;

        // --add-word [語 [読み]]: ユーザー辞書のページを、登録欄に語と読みを入れて開く (Windows の単語の登録のホットキー)
        if (args is ["--add-word", .. var word])
        {
            _addWord = new UserWord(word.ElementAtOrDefault(1) ?? "", word.ElementAtOrDefault(0) ?? "");
            _navigation.SelectedIndex = _pages.ToList().FindIndex(p => p.Title == UserDictionaryPage.Title);
        }
        else _navigation.SelectedIndex = 0;
    }

    /// <summary>起動の引数で登録欄に入れる語。ほかのページに移ったら捨てる。</summary>
    private UserWord? _addWord;

    private static Control Dock(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    /// <summary>検索の文字があれば、すべての分類から一致する項目を出す。無ければ一覧で選んだページを出す。</summary>
    private void ShowPage()
    {
        var offset = _content.Offset;
        var words = (_search.Text ?? "").Split(' ', '　', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0)
        {
            _content.Tag = null;
            var found = _catalog.Select(g => (g.Category, (IReadOnlyList<SettingItem>)g.Items.Where(i => SettingsPage.Matches(i, words)).ToList()))
                .Where(g => g.Item2.Count > 0).ToList();
            _content.Content = found.Count > 0
                ? SettingsPage.Build(found, _store, this, search: true)
                : new TextBlock { Text = "一致する設定がありません。", Margin = new Thickness(32, 24), Opacity = 0.7 };
            return;
        }
        if (_navigation.SelectedIndex < 0) return;
        var page = _pages[_navigation.SelectedIndex];
        var changedPage = !ReferenceEquals(_content.Tag, page);
        _content.Content = page.Build();
        _content.Tag = page;
        // 同じページを読み直したときは、見ていた位置のままにする
        _content.Offset = changedPage ? default : offset;
    }
}

/// <summary>左の一覧の 1 項目。選ぶたびに Build で作り直す (状態を持たない)。ShowsSettings は config.json の値を出すページ (書き換わったら作り直す)。</summary>
internal sealed record Page(string Title, Func<Control> Build, bool ShowsSettings = false);
