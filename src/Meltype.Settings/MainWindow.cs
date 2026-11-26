// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Meltype.Config;
using Meltype.Dictionary;

namespace Meltype.SettingsApp;

/// <summary>左に検索とページの一覧、右に選んだページと、その下に入力プレビュー。ページは設定の分類と、設定以外の操作 (<see cref="Pages"/>)。</summary>
internal sealed class MainWindow : Window
{
    /// <summary>
    /// 左の一覧の順。設定の分類 (Settings の Category) と設定以外のページ (<see cref="Pages"/>) を並べる。
    /// 分類と同じ名前のページは、ページの上に分類の設定を並べる (設定と、それが使うデータを 1 か所で見せる)。
    /// </summary>
    private static readonly string[] PageOrder =
    [
        "全般", "入力方式", "英語 / 日本語の判定", "変換", "便利変換", "記号・スペース", "キー操作", "表示", "動作する場面",
        UserDictionaryPage.Title, SnippetsPage.Title, LearningPage.Title, "打鍵数・ログ", "更新", "バックアップ",
        // プロファイルは使う人が少ないので、一覧の下に置く
        "プロファイル", "Meltype について",
    ];

    private readonly SettingsStore _store = new();
    private readonly IReadOnlyList<(string Category, IReadOnlyList<SettingItem> Items)> _catalog = [.. SettingsCatalog.ForThisPlatform().OrderBy(g => PageIndex(g.Category))];
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
        // 設定以外のページは、分類の設定を出していても作り直さない (ShowsSettings が false)。作り直すと、打ちかけの定型文などが消える
        Page[] others = [.. Pages.All(this, () => _addWord, title => SettingsPage.Cards(_catalog.FirstOrDefault(g => g.Category == title).Items ?? [], _store, this)),
            new Page("プロファイル", () => ProfilesPage.Build(_store, this), ShowsSettings: true)];
        _pages = [.. _catalog.Where(g => others.All(p => p.Title != g.Category))
            .Select(g => new Page(g.Category, () => SettingsPage.Build([g], _store, this, search: false), ShowsSettings: true))
            .Concat(others)
            .OrderBy(p => PageIndex(p.Title))];
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
        // Tab で、ページの項目からプレビューの順に移る (DockPanel の Bottom にすると、プレビューが先になる)
        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        right.Children.Add(_content);
        var preview = Preview();
        Grid.SetRow(preview, 1);
        right.Children.Add(preview);
        root.Children.Add(right);
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

    /// <summary>
    /// 入力プレビュー: 変えた設定を、入力している Meltype でそのまま試す欄。ページの外に置くので、ページを移っても打った文章が残る。
    /// 設定アプリの中で入力の本体 (ImeHost) を動かさない: OS の IME とキーを取り合い、候補の表示も本物と別に描くことになる。
    /// </summary>
    private static Border Preview()
    {
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72, PlaceholderText = "Meltype でここに打つと、今の設定を試せます (変えた設定は 1 秒ほどで反映します)" };
        AutomationProperties.SetName(text, "入力プレビュー");
        var header = new DockPanel();
        // 入力欄にフォーカスを戻さない: 戻すと、fcitx5 では変換中の文字が、消した後の入力欄に確定する
        var clear = SettingEditors.Button("入力をクリア", text.Clear);
        header.Children.Add(Dock(clear, Avalonia.Controls.Dock.Right));
        var heading = Pages.Heading("入力プレビュー");
        heading.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(heading);
        return new Border { Classes = { "preview" }, Child = new StackPanel { Spacing = 8, Children = { header, text } } };
    }

    private static int PageIndex(string title) => Array.IndexOf(PageOrder, title) is >= 0 and var index ? index
        : throw new InvalidOperationException($"左の一覧のどこに並べるかを決めていないページ: 「{title}」 (MainWindow.PageOrder に足す)");

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
