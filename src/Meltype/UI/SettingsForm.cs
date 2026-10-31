// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Reflection;
using System.Text;
using Meltype.Composition;
using Meltype.Config;
using Microsoft.Win32;

namespace Meltype.UI;

/// <summary>
/// 設定画面。Settings の各項目を種類に合わせた部品で並べる:
///   ON/OFF → スイッチ (ToggleSwitch)、選択肢 (列挙型) → 日本語名のプルダウン、数値 → 数値入力、アプリ別設定 → 表 (ON/OFF はスイッチ)。
/// 項目名・分類・説明は Settings の属性 (DisplayName / Category / Description) から取る。
/// 下の「判定テスト」欄では、打った英字をローマ字 (日本語) と英語のどちらと判定するかと理由を確認できる。
/// </summary>
internal sealed class SettingsForm : Form, IMessageFilter
{
    private const string On = "ON";
    private const string Off = "OFF";

    private static readonly object FontNamesGate = new();
    private static string[]? s_fontNames;

    static SettingsForm()
    {
        SystemEvents.InstalledFontsChanged += (_, _) => { lock (FontNamesGate) s_fontNames = null; };
    }

    private readonly MeltypeEngine _engine;
    private readonly List<Binding> _bindings = [];
    private readonly TableLayoutPanel _body = new() { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(8, 8, SystemInformation.VerticalScrollBarWidth + 4, 8) };
    // 左の分類の一覧。選ぶと、その分類の枠まで設定の一覧をスクロールする (issue #1200)
    private readonly ListBox _categories = new() { Dock = DockStyle.Left, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly List<GroupBox> _groups = [];
    private bool _scrollingToCategory;
    private readonly Label _help = new() { Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, Padding = new Padding(8, 4, 8, 4), AutoEllipsis = true };
    // 説明の欄。出している説明に合わせて高さを変えると、カーソルを動かすたびに設定の一覧の高さが変わり、
    // 文字が一瞬上に飛んだり、スクロールバーのつまみの大きさが変わったりするので、高さを 4 行ぶんに固定する。
    // 4 行に収まらない説明は末尾を「…」にする (全文はツールチップで見られる)
    private readonly Panel _helpPanel = new() { Dock = DockStyle.Bottom, BorderStyle = BorderStyle.FixedSingle };
    private const int HelpLines = 4;
    private readonly TextBox _testInput = new() { Dock = DockStyle.Top, ImeMode = ImeMode.Disable };
    private readonly TextBox _testResult = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 20000 };
    // プロファイル (仕事用・趣味用・SNS 用など) を選ぶ欄
    private readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Anchor = AnchorStyles.Left };
    private bool _loadingProfiles;
    private bool _loadingSettings;
    private long _fontNamesMs;
    private readonly ToggleSwitch _showHelp = new() { Text = "下部に説明を表示" };
    private readonly ToggleSwitch _wheelChangesValue = new() { Text = "マウスホイールで値を変更" };

    /// <summary>画面の部品に出していない値 (プロファイルの一覧・使っているプロファイル など) を持つ設定。</summary>
    private Settings _draft;

    /// <summary>開いた時点の画面の値。閉じるときに比べて、画面で変えた項目だけを今の設定に重ねる (開いている間にトレイで変えた設定を戻さない)。</summary>
    private readonly Settings _opened = null!;

    /// <summary>1 項目分の部品と、設定値との受け渡し。</summary>
    private sealed record Binding(PropertyInfo Property, Control Control, Action<Settings> Load, Action<Settings> Store);

    public SettingsForm(MeltypeEngine engine)
    {
        // 開くまでにかかった時間をログに書く (遅いときに、部品を作るのと表示するののどちらに時間がかかったかを測る。issue #1199)
        var opening = System.Diagnostics.Stopwatch.StartNew();
        _engine = engine;
        _draft = engine.Settings.Clone().Normalize();
        Text = "Meltype 設定";
        StartPosition = FormStartPosition.CenterScreen;
        // 上のプロファイルの欄のボタンが 1 段に並ぶ幅に、左の分類の一覧の幅を足す。高さは分類の一覧を足す前の幅と同じにする。
        // 文字は画面の拡大率に合わせて大きくなるので、窓の大きさも拡大率に合わせる。
        // 画面に収まらないときは画面に合わせる (中身はスクロールできる)。
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var size = LogicalToDeviceUnits(740);
        Size = new Size(Math.Min(size + LogicalToDeviceUnits(120), area.Width - 40), Math.Min(size, area.Height - 60));
        MinimumSize = new Size(480, 400);
        Font = new Font("Yu Gothic UI", 9.5F);

        // 部品を並べ終わるまで配置の計算を止める (項目を 1 つ足すたびに全体を並べ直して、開くのが遅かった。issue #1199)
        SuspendLayout();
        _body.SuspendLayout();
        // 1 列の表に分類ごとの枠を縦に並べる (どの枠も画面の幅いっぱいにそろう)。
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var group in BuildGroups())
        {
            group.Dock = DockStyle.Fill;
            _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _body.Controls.Add(group);
            _groups.Add(group);
            _categories.Items.Add(group.Text);
        }
        // 自動スクロールの表は、最後の行の下の余白を含めないことがあり、一番下の枠が少し見切れる。空の行で余白を取る
        _body.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        _body.Controls.Add(new Panel { Height = 16, Margin = Padding.Empty });

        _helpPanel.Controls.Add(_help);
        _helpPanel.Height = TextRenderer.MeasureText("あ", Font).Height * HelpLines + _help.Padding.Vertical + 6;

        var testLabel = new Label { Text = "判定テスト (ローマ字か英語かの判定。英字で入力):", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 2) };
        _testInput.TextChanged += (_, _) => RunTest();
        var testPanel = new Panel { Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(8, 0, 8, 0) };
        testPanel.Controls.Add(_testResult);
        testPanel.Controls.Add(_testInput);
        testPanel.Controls.Add(testLabel);

        var ok = new Button { Text = "OK", AutoSize = true, Padding = new Padding(13, 4, 13, 4) };
        var cancel = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(13, 4, 13, 4) };
        var defaults = new Button { Text = "既定値に戻す", AutoSize = true, Padding = new Padding(13, 4, 13, 4) };
        // OK と × (閉じる) は保存する。変更を捨てるのは キャンセル だけ。
        var discard = false;
        ok.Click += (_, _) => Close();
        cancel.Click += (_, _) =>
        {
            discard = true;
            Close();
        };
        FormClosing += (_, e) =>
        {
            if (discard) return;
            var next = CollectValues();
            foreach (var binding in next.EffectiveToggleShortcuts)
            {
                if (ShortcutEditor.ValidationError(binding, next, []) is not { } error) continue;
                MessageBox.Show(this, error, "入力モード切り替えキー", MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
                return;
            }
            next = Settings.MergeEdits(_opened, next, _engine.Settings);
            if (next.ToJson() != _engine.Settings.Clone().Normalize().ToJson()) _engine.ApplySettings(next);
        };
        defaults.Click += (_, _) =>
        {
            LoadFrom(new Settings());
            RunTest();
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange(cancel, ok, defaults);

        Controls.Add(_body);
        Controls.Add(BuildCategoryList());
        Controls.Add(BuildViewOptions());
        Controls.Add(BuildProfileBar());
        Controls.Add(_helpPanel);
        Controls.Add(testPanel);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        var controlsBuilt = opening.ElapsedMilliseconds;
        LoadFrom(_draft);
        RefreshProfiles();
        _opened = Collect();
        var valuesLoaded = opening.ElapsedMilliseconds;
        var handlesCreated = valuesLoaded;
        var laidOut = valuesLoaded;
        // Show() で子のウィンドウハンドルを作ると、プルダウンなどの高さが変わり、表の再配置が起きる。
        // そこまで止めておき、すべてのハンドルがそろった Load で内側 → 外側の順に一度だけ配置する。
        Load += (_, _) =>
        {
            handlesCreated = opening.ElapsedMilliseconds;
            foreach (var group in _body.Controls.OfType<GroupBox>())
            {
                foreach (Control child in group.Controls) child.ResumeLayout();
                group.ResumeLayout();
            }
            _body.ResumeLayout();
            ResumeLayout();
            laidOut = opening.ElapsedMilliseconds;
        };
        Shown += (_, _) =>
        {
            var shown = opening.ElapsedMilliseconds;
            Diagnostics.Log.Info($"設定画面を開くまで {shown} ms (部品を作るまで {valuesLoaded} ms)");
            Diagnostics.Log.Info($"設定画面の内訳: 部品作成 {controlsBuilt} ms (フォント一覧 {_fontNamesMs} ms)、値の読込 {valuesLoaded - controlsBuilt} ms、ハンドル作成 {handlesCreated - valuesLoaded} ms、配置 {laidOut - handlesCreated} ms、表示 {shown - laidOut} ms");
        };
    }

    /// <summary>
    /// 左の分類の一覧。選んだ分類の枠を設定の一覧の一番上に出す。スクロールしたら、いま一番上にある分類を選んだ状態にする。
    /// </summary>
    private Panel BuildCategoryList()
    {
        _categories.Width = LogicalToDeviceUnits(110);
        // 項目の間を空ける (ItemHeight は自分で描くときだけ効く)
        _categories.ItemHeight = _categories.Font.Height + LogicalToDeviceUnits(10);
        _categories.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, (string)_categories.Items[e.Index], e.Font, e.Bounds, e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
        _categories.SelectedIndexChanged += (_, _) =>
        {
            if (_scrollingToCategory || _categories.SelectedIndex < 0) return;
            _scrollingToCategory = true;
            // 枠の Top は今のスクロール位置からの位置なので、スクロールの量を足して一覧の先頭からの位置にする
            _body.AutoScrollPosition = new Point(0, _groups[_categories.SelectedIndex].Top - _body.AutoScrollPosition.Y - _body.Padding.Top);
            _scrollingToCategory = false;
        };
        void FollowScroll()
        {
            if (_scrollingToCategory || _groups.Count == 0) return;
            var index = Math.Max(0, _groups.FindLastIndex(g => g.Top <= _body.Padding.Top + 1));
            // 一番下までスクロールしたときは、最後の方の短い枠は上端まで来ない。見えている枠を選んでいればそのまま、そうでなければ最後の分類にする
            var scroll = _body.VerticalScroll;
            var atBottom = scroll.Visible && scroll.Value >= scroll.Maximum - scroll.LargeChange;
            if (atBottom)
            {
                var current = _categories.SelectedIndex;
                index = current > index && _groups[current].Top < _body.ClientSize.Height ? current : _groups.Count - 1;
            }
            _scrollingToCategory = true;
            _categories.SelectedIndex = index;
            _scrollingToCategory = false;
        }
        _body.Scroll += (_, _) => FollowScroll();
        _body.MouseWheel += (_, _) => FollowScroll();
        // Tab でフォーカスを移したときの自動のスクロールと、窓の大きさを変えたときも合わせ直す (スクロールが終わってから)
        _body.Resize += (_, _) => FollowScroll();
        foreach (var group in _groups) group.Enter += (_, _) => BeginInvoke(FollowScroll);
        if (_categories.Items.Count > 0)
        {
            _scrollingToCategory = true;
            _categories.SelectedIndex = 0;
            _scrollingToCategory = false;
        }
        var panel = new Panel { Dock = DockStyle.Left, Width = _categories.Width + LogicalToDeviceUnits(9), Padding = new Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(8), 0, LogicalToDeviceUnits(8)) };
        panel.Controls.Add(_categories);
        return panel;
    }

    /// <summary>プロファイルの欄の下: 説明の欄を出すか、ホイールで値を変えるか。</summary>
    private FlowLayoutPanel BuildViewOptions()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8, 4, 8, 0) };
        _showHelp.Checked = _draft.SettingsShowHelp;
        _helpPanel.Visible = _showHelp.Checked;
        _showHelp.CheckedChanged += (_, _) => _helpPanel.Visible = _showHelp.Checked;
        _wheelChangesValue.Checked = _draft.SettingsWheelChangesValue;
        _toolTip.SetToolTip(_wheelChangesValue, WrapToolTip("OFF にすると、プルダウンや数値の上でホイールを回しても値を変えず、設定の一覧をスクロールします。"));
        bar.Controls.AddRange([_showHelp, _wheelChangesValue]);
        return bar;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Application.AddMessageFilter(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.RemoveMessageFilter(this);
        base.OnFormClosed(e);
    }

    /// <summary>「マウスホイールで値を変更」が OFF のとき、プルダウン・数値の上で回したホイールを設定の一覧のスクロールに回す。</summary>
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != Native.WM_MOUSEWHEEL || _wheelChangesValue.Checked || !_body.IsHandleCreated) return false;
        // 数値の入力欄は NumericUpDown の中の部品なので、親をたどって見る
        for (var control = FromChildHandle(m.HWnd); control is not null; control = control.Parent)
        {
            if (control is ComboBox { DroppedDown: true }) return false; // 開いた一覧はホイールで選べるままにする
            if (control is ComboBox or NumericUpDown && _body.Contains(control))
            {
                Native.SendMessage(_body.Handle, m.Msg, m.WParam, m.LParam);
                return true;
            }
        }
        return false;
    }

    /// <summary>上のプロファイルの欄: 選ぶと、そのプロファイルの値を画面に読み込む。新規は今の値を写して作る。</summary>
    private FlowLayoutPanel BuildProfileBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8, 8, 8, 0) };
        var label = new Label { Text = "プロファイル:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) };
        var add = new Button { Text = "新規...", AutoSize = true };
        var rename = new Button { Text = "名前を変更...", AutoSize = true };
        var remove = new Button { Text = "削除", AutoSize = true };
        var export = new Button { Text = "書き出す...", AutoSize = true };
        var import = new Button { Text = "読み込む...", AutoSize = true };
        ShowHelpFor(_profiles, "プロファイル", "仕事用・趣味用・SNS 用など、設定の値をまとめて切り替えられます。トレイのメニューの「プロファイル」からも切り替えられます。Meltype の ON/OFF・ログ・更新の設定は、どのプロファイルでも共通です。「書き出す...」でファイルにして、ほかの人に渡せます (「読み込む...」で新しいプロファイルとして足せます)。");
        _profiles.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingProfiles || _profiles.SelectedItem is not string name || name == _draft.ActiveProfile) return;
            // 今の画面の値を今のプロファイルに入れてから、選んだプロファイルの値を読み込む
            _draft = Collect().SwitchProfile(name);
            LoadFrom(_draft);
            RunTest();
        };
        add.Click += (_, _) =>
        {
            if (TextPrompt.Ask(this, "新しいプロファイル", "名前 (例: 仕事用、趣味用、SNS 用)。今の設定を写して作ります。", "") is not { } name) return;
            if (Collect().AddProfile(name) is not { } next)
            {
                MessageBox.Show(this, "名前が空か、同じ名前のプロファイルがあります。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _draft = next;
            RefreshProfiles();
        };
        rename.Click += (_, _) =>
        {
            var old = _draft.ActiveProfile;
            if (TextPrompt.Ask(this, "プロファイルの名前を変更", "新しい名前:", old) is not { } name || name == old) return;
            if (Collect().RenameProfile(old, name) is not { } next)
            {
                MessageBox.Show(this, "名前が空か、同じ名前のプロファイルがあります。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _draft = next;
            RefreshProfiles();
        };
        remove.Click += (_, _) =>
        {
            var name = _draft.ActiveProfile;
            if (_draft.Profiles.Count <= 1)
            {
                MessageBox.Show(this, "プロファイルが 1 つだけのときは削除できません。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, $"プロファイル「{name}」を削除しますか?", "プロファイル", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _draft = Collect().RemoveProfile(name) ?? _draft;
            LoadFrom(_draft);
            RefreshProfiles();
            RunTest();
        };
        // プロファイルを人に渡す: 書き出したファイルを、相手が「読み込む...」で新しいプロファイルとして足す
        export.Click += (_, _) =>
        {
            var draft = Collect();
            using var dialog = new SaveFileDialog
            {
                Title = "プロファイルを書き出す",
                Filter = "Meltype のプロファイル (*.meltype-profile.json)|*.meltype-profile.json|すべてのファイル (*.*)|*.*",
                FileName = $"{string.Concat(draft.ActiveProfile.Split(Path.GetInvalidFileNameChars()))}.meltype-profile.json",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dialog.FileName, draft.ExportProfile());
                MessageBox.Show(this, $"プロファイル「{draft.ActiveProfile}」を書き出しました。\nアプリ別設定 (アプリのプロセス名) も入っています。渡す前に、見られてもよいか確かめてください。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"書き出せませんでした: {ex.Message}", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        import.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "プロファイルを読み込む",
                Filter = "Meltype のプロファイル (*.meltype-profile.json)|*.meltype-profile.json|JSON (*.json)|*.json|すべてのファイル (*.*)|*.*",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            Settings? next = null;
            try
            {
                // プロファイルは数 KB。大きすぎるファイルは読まない
                if (new FileInfo(dialog.FileName).Length <= 1024 * 1024) next = Collect().ImportProfile(File.ReadAllText(dialog.FileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            if (next is null)
            {
                MessageBox.Show(this, "Meltype のプロファイルとして読めませんでした。「書き出す...」で作ったファイルを選んでください。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _draft = next;
            LoadFrom(_draft);
            RefreshProfiles();
            RunTest();
        };
        bar.Controls.AddRange([label, _profiles, add, rename, remove, export, import]);
        return bar;
    }

    /// <summary>プロファイルの一覧を出し直して、使っているものを選ぶ。</summary>
    private void RefreshProfiles()
    {
        _loadingProfiles = true;
        _profiles.Items.Clear();
        _profiles.Items.AddRange(_draft.ProfileNames.Cast<object>().ToArray());
        _profiles.SelectedItem = _draft.ActiveProfile;
        _loadingProfiles = false;
    }

    /// <summary>分類 (Category) ごとの枠に、項目を 1 行ずつ並べる。</summary>
    private IEnumerable<GroupBox> BuildGroups()
    {
        var properties = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            // ポータブル版では動作モードと IME 切替の詳細設定を使わないため、設定画面には表示しない。
            .Where(p => !AppPaths.IsPortable || (p.GetCustomAttribute<CategoryAttribute>()?.Category != "6. IME" && p.Name != nameof(Settings.Mode)))
            .GroupBy(p => p.GetCustomAttribute<CategoryAttribute>()?.Category ?? "その他")
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var category in properties)
        {
            var group = new GroupBox { Text = StripNumber(category.Key), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6) };
            group.SuspendLayout();
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
            table.SuspendLayout();
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            foreach (var property in category)
            {
                if (CreateBinding(property) is not { } binding) continue;
                var name = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name;
                var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                var label = new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 6) };
                // スイッチには見出しの文字が無いので、ナレーターには項目名で読ませる
                binding.Control.AccessibleName = name;
                // 表 (アプリ別設定は表 + ボタンの枠) は、右の列だと狭くて列が見切れるので、名前の下に幅いっぱいで出す
                var fullRow = binding.Control is DataGridView or Panel;
                if (fullRow)
                {
                    table.Controls.Add(label);
                    table.SetColumnSpan(label, 2);
                    table.Controls.Add(binding.Control);
                    table.SetColumnSpan(binding.Control, 2);
                }
                else
                {
                    table.Controls.Add(label);
                    table.Controls.Add(binding.Control);
                }
                ShowHelpFor(label, name, description);
                ShowHelpFor(binding.Control, name, description);
                _bindings.Add(binding);
            }
            group.Controls.Add(table);
            // ここでは再開しない。親に付けてフォントを引き継ぎ、設定の値を入れてからコンストラクターでまとめて再開する。
            yield return group;
        }
    }

    /// <summary>項目の種類に合わせた部品を作る。</summary>
    private Binding? CreateBinding(PropertyInfo property)
    {
        var type = property.PropertyType;
        if (property.Name == nameof(Settings.ToggleShortcuts))
        {
            var editor = new ShortcutEditor(_engine, () => Collect());
            return new Binding(property, editor,
                s => editor.LoadBindings(s.EffectiveToggleShortcuts),
                s => s.ToggleShortcuts = editor.CollectBindings());
        }
        if (type == typeof(bool))
        {
            var toggle = Toggle();
            if (property.Name == nameof(Settings.UpdatePrerelease)) toggle.Toggled += (_, _) => ConfirmPrerelease(toggle);
            return new Binding(property, toggle,
                s => toggle.Checked = (bool)property.GetValue(s)!,
                s => property.SetValue(s, toggle.Checked));
        }
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToList();
            var combo = DropDown(values.Select(v => EnumName(type, v)).ToArray());
            // Meltype IME を使えない (ARM64 の Windows・登録されていない・キーボードの一覧に無い) なら、灰色にして選ばせない
            if (!AppPaths.IsPortable && property.Name == nameof(Settings.Mode) &&
                _engine.Settings.Mode != InputMode.Tsf && !Tip.TipServer.IsUsable)
                DisableChoice(combo, values.IndexOf(InputMode.Tsf));
            return new Binding(property, combo,
                s => combo.SelectedIndex = values.IndexOf(property.GetValue(s)!),
                s => property.SetValue(s, values[Math.Max(0, combo.SelectedIndex)]));
        }
        if (type == typeof(string) && property.Name == nameof(Settings.CompositionFont))
        {
            // 一覧は使い回す。最初から全選択肢を入れ、閉じたまま ↑↓ でも選べるようにする。
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var combo = DropDown([DefaultFontChoice, .. FontNames()]);
            _fontNamesMs += (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return new Binding(property, combo,
                s =>
                {
                    var name = (string?)property.GetValue(s) ?? "";
                    if (name.Length > 0 && !combo.Items.Contains(name)) combo.Items.Add(name); // 今は無いフォントでも選んだままにする
                    combo.SelectedItem = name.Length == 0 ? DefaultFontChoice : name;
                },
                s => property.SetValue(s, combo.SelectedItem is string name && name != DefaultFontChoice ? name : ""));
        }
        if (type == typeof(string) && property.Name is nameof(Settings.TextInputApps) or nameof(Settings.PasteApps) or nameof(Settings.NoPasteApps))
        {
            var panel = AppListEditor(out var text);
            return new Binding(property, panel,
                s => text.Text = (string?)property.GetValue(s) ?? "",
                s => property.SetValue(s, text.Text.Trim()));
        }
        if (type == typeof(string) && property.Name is nameof(Settings.ModeIndicatorJapaneseColor) or nameof(Settings.ModeIndicatorDirectColor) or nameof(Settings.ModeIndicatorTextColor))
        {
            var fallback = property.Name switch
            {
                nameof(Settings.ModeIndicatorJapaneseColor) => ModeIndicatorWindow.DefaultJapaneseColor,
                nameof(Settings.ModeIndicatorDirectColor) => ModeIndicatorWindow.DefaultDirectColor,
                _ => Color.White,
            };
            var panel = ColorEditor(fallback, out var text);
            return new Binding(property, panel,
                s => text.Text = (string?)property.GetValue(s) ?? "",
                s => property.SetValue(s, text.Text.Trim()));
        }
        if (type == typeof(string))
        {
            // 入力モードの表示の色・定型文を出す記号など
            var box = new TextBox { Width = 120, Anchor = AnchorStyles.Left };
            return new Binding(property, box,
                s => box.Text = (string?)property.GetValue(s) ?? "",
                s => property.SetValue(s, box.Text));
        }
        if (type == typeof(int))
        {
            var number = new NumericUpDown { Minimum = 0, Maximum = 60000, Width = 120, Anchor = AnchorStyles.Left };
            return new Binding(property, number,
                s => number.Value = Math.Clamp((int)property.GetValue(s)!, (int)number.Minimum, (int)number.Maximum),
                s => property.SetValue(s, (int)number.Value));
        }
        if (type == typeof(List<AppRule>))
        {
            var grid = _rulesGrid = AppRulesGrid();
            // プロセス名 (maya.exe など) を知らなくても足せるように、実行中のアプリから選べるようにする。
            var add = new Button { Text = "実行中のアプリから追加…", AutoSize = true };
            add.Click += (_, _) => ShowRunningApps(grid, add);
            // 行の削除は Delete キーでもできるが、気づきにくいのでボタンも置く
            var remove = new Button { Text = "選んだ行を削除", AutoSize = true };
            remove.Click += (_, _) =>
            {
                foreach (var row in grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow).OfType<DataGridViewRow>().Distinct().Where(r => !r.IsNewRow).ToList()) grid.Rows.Remove(row);
            };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            actions.Controls.AddRange([add, remove]);
            var panel = new Panel { Height = grid.Height + add.PreferredSize.Height + 10, Dock = DockStyle.Fill };
            panel.Controls.Add(grid);
            panel.Controls.Add(actions);
            return new Binding(property, panel,
                s =>
                {
                    RefreshKindChoices(s.AppKinds.Select(k => k.Name));
                    grid.Rows.Clear();
                    foreach (var rule in (List<AppRule>)property.GetValue(s)!)
                    {
                        var kind = rule.Kind is { Length: > 0 } name && s.AppKinds.Any(k => k.Name == name) ? name : EnumName(typeof(AppProfile), rule.Profile);
                        grid.Rows.Add(rule.Process, rule.Enabled, kind);
                    }
                },
                s => property.SetValue(s, grid.Rows.Cast<DataGridViewRow>()
                    .Where(r => !r.IsNewRow)
                    .Select(r => (Process: (r.Cells[0].Value as string ?? "").Trim(), r.Cells[1].Value, Kind: r.Cells[2].Value as string ?? ""))
                    .Where(r => r.Process.Length > 0)
                    .Select(r =>
                    {
                        var builtIn = Enum.GetValues<AppProfile>().Where(p => EnumName(typeof(AppProfile), p) == r.Kind).Cast<AppProfile?>().FirstOrDefault();
                        return new AppRule
                        {
                            Process = r.Process,
                            Enabled = r.Value is not false, // 新しい行の空の値は ON
                            Profile = builtIn ?? AppProfile.General,
                            Kind = builtIn is null && r.Kind.Length > 0 ? r.Kind : null,
                        };
                    })
                    .ToList()));
        }
        if (type == typeof(List<AppKind>))
        {
            var grid = AppKindsGrid();
            return new Binding(property, grid,
                s =>
                {
                    grid.Rows.Clear();
                    foreach (var kind in (List<AppKind>)property.GetValue(s)!)
                    {
                        grid.Rows.Add(kind.Name, EnumName(typeof(AppProfile), kind.Base),
                            kind.DetectionLevel is { } level ? EnumName(typeof(DetectionLevel), level) : SameAsGlobal,
                            kind.LiveConversion is { } live ? (live ? On : Off) : SameAsGlobal,
                            kind.StartInEnglish);
                    }
                },
                s => property.SetValue(s, ReadKinds(grid)));
        }
        return null;
    }

    private const string SameAsGlobal = "全体と同じ";
    private DataGridView? _rulesGrid;

    private static List<AppKind> ReadKinds(DataGridView grid) => grid.Rows.Cast<DataGridViewRow>()
        .Where(r => !r.IsNewRow && (r.Cells[0].Value as string ?? "").Trim().Length > 0)
        .Select(r => new AppKind
        {
            Name = (r.Cells[0].Value as string ?? "").Trim(),
            Base = Equals(r.Cells[1].Value, EnumName(typeof(AppProfile), AppProfile.Code)) ? AppProfile.Code : AppProfile.General,
            DetectionLevel = Enum.GetValues<DetectionLevel>().Where(l => Equals(r.Cells[2].Value, EnumName(typeof(DetectionLevel), l))).Cast<DetectionLevel?>().FirstOrDefault(),
            LiveConversion = Equals(r.Cells[3].Value, On) ? true : Equals(r.Cells[3].Value, Off) ? false : null,
            StartInEnglish = r.Cells[4].Value is true,
        })
        .GroupBy(k => k.Name).Select(g => g.First())
        .ToList();

    /// <summary>アプリ別設定の「種類」の選択肢を、一般 / コード + 独自の種類 にする。</summary>
    private void RefreshKindChoices(IEnumerable<string> kinds)
    {
        if (_rulesGrid?.Columns[2] is not DataGridViewComboBoxColumn column) return;
        var choices = Enum.GetValues<AppProfile>().Select(p => EnumName(typeof(AppProfile), p)).Concat(kinds).Distinct().ToList();
        // 使われている値が選択肢から消えるとエラーになるので、今の値も残す。
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.Cells[2].Value is string value && !choices.Contains(value)) choices.Add(value);
        }
        column.Items.Clear();
        column.Items.AddRange(choices.Cast<object>().ToArray());
    }

    private DataGridView AppKindsGrid()
    {
        var grid = new DataGridView
        {
            Height = 140,
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersWidth = 24,
            BackgroundColor = SystemColors.Window,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "名前 (例: チャット)", FillWeight = 26 });
        grid.Columns.Add(Choices("元にする種類", 18, Enum.GetValues<AppProfile>().Select(p => EnumName(typeof(AppProfile), p))));
        grid.Columns.Add(Choices("判定の強さ", 20, [SameAsGlobal, .. Enum.GetValues<DetectionLevel>().Select(l => EnumName(typeof(DetectionLevel), l))]));
        grid.Columns.Add(Choices("ライブ変換", 18, [SameAsGlobal, On, Off]));
        grid.Columns.Add(new ToggleSwitchColumn("最初は英数", 18));
        grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells[1].Value = EnumName(typeof(AppProfile), AppProfile.General);
            e.Row.Cells[2].Value = SameAsGlobal;
            e.Row.Cells[3].Value = SameAsGlobal;
            e.Row.Cells[4].Value = false;
        };
        CommitSwitchesImmediately(grid);
        // 種類を足したり名前を変えたりしたら、アプリ別設定の「種類」の選択肢にすぐ出す。
        void Changed()
        {
            if (!_loadingSettings) RefreshKindChoices(ReadKinds(grid).Select(k => k.Name));
        }
        grid.CellValueChanged += (_, _) => Changed();
        grid.RowsRemoved += (_, _) => Changed();
        grid.DataError += (_, e) => e.ThrowException = false;
        return grid;

        static DataGridViewComboBoxColumn Choices(string header, int weight, IEnumerable<string> items)
        {
            var column = new DataGridViewComboBoxColumn { HeaderText = header, FillWeight = weight, FlatStyle = FlatStyle.Flat };
            column.Items.AddRange(items.Cast<object>().ToArray());
            return column;
        }
    }

    private const string DefaultFontChoice = "既定 (Yu Gothic UI)";

    private static string[] FontNames()
    {
        lock (FontNamesGate)
        {
            if (s_fontNames is not null) return s_fontNames;
            using var installed = new System.Drawing.Text.InstalledFontCollection();
            return s_fontNames = installed.Families.Select(f => f.Name).Where(n => n.Length > 0 && !n.StartsWith('@'))
                .Distinct().OrderBy(n => n, StringComparer.CurrentCulture).ToArray();
        }
    }

    private ComboBox DropDown(string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, Anchor = AnchorStyles.Left };
        combo.Items.AddRange(items);
        combo.SelectedIndexChanged += (_, _) => RunTest();
        return combo;
    }

    /// <summary>ON/OFF の項目のスイッチ。切り替えたら下の判定テストをやり直す (プルダウンと同じ)。</summary>
    private ToggleSwitch Toggle()
    {
        var toggle = new ToggleSwitch { Anchor = AnchorStyles.Left };
        toggle.CheckedChanged += (_, _) => RunTest();
        return toggle;
    }

    /// <summary>プルダウンの 1 項目を灰色で出し、選んだら前の項目に戻す。</summary>
    private static void DisableChoice(ComboBox combo, int disabled)
    {
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var color = e.Index == disabled ? SystemColors.GrayText : e.ForeColor;
            TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), e.Font, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        };
        var previous = disabled == 0 ? 1 : 0;
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedIndex == disabled) combo.SelectedIndex = previous;
            else previous = combo.SelectedIndex;
        };
    }

    /// <summary>窓を開いている実行中のアプリの一覧を出し、選んだものをアプリ別設定の表に足す (既に表にあれば、その行を選ぶ)。</summary>
    private static void ShowRunningApps(DataGridView grid, Control anchor) => ShowRunningApps(anchor, name =>
    {
        var existing = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => !r.IsNewRow && string.Equals(r.Cells[0].Value as string, name, StringComparison.OrdinalIgnoreCase));
        var row = existing ?? grid.Rows[grid.Rows.Add(name, true, EnumName(typeof(AppProfile), AppProfile.General))];
        grid.ClearSelection();
        row.Selected = true;
        grid.FirstDisplayedScrollingRowIndex = row.Index;
        grid.CurrentCell = row.Cells[2];
    });

    /// <summary>窓を開いている実行中のアプリ (プロセス名) を一覧で出し、選んだものを渡す。</summary>
    private static void ShowRunningApps(Control anchor, Action<string> picked)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.Id != Environment.ProcessId) names.Add(process.ProcessName + ".exe");
            }
            catch
            {
                // 終わったばかりのプロセスなど
            }
            finally
            {
                process.Dispose();
            }
        }
        var menu = new ContextMenuStrip();
        foreach (var name in names) menu.Items.Add(name, null, (_, _) => picked(name));
        if (menu.Items.Count == 0) menu.Items.Add("(窓を開いているアプリがありません)").Enabled = false;
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    /// <summary>
    /// アプリの一覧 (プロセス名、カンマ区切り) の設定 (入力欄とみなすアプリ・貼り付けで入力するアプリなど) の部品。
    /// 手で書くほか、実行中のアプリから選んで足せる (issue #223)。
    /// </summary>
    private static Panel AppListEditor(out TextBox text)
    {
        var box = text = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "例: Adobe Premiere Pro.exe, foo.exe" };
        var add = new Button { Text = "実行中のアプリから選ぶ…", AutoSize = true, Dock = DockStyle.Right };
        add.Click += (_, _) => ShowRunningApps(add, name =>
        {
            var apps = box.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (apps.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            apps.Add(name);
            box.Text = string.Join(", ", apps);
        });
        var panel = new Panel { Height = Math.Max(box.PreferredHeight, add.PreferredSize.Height) + 4, Dock = DockStyle.Fill };
        panel.Controls.Add(box);
        panel.Controls.Add(add);
        return panel;
    }

    /// <summary>
    /// 色の設定 (#RRGGBB) の部品。手で書くほか、Windows の色の選択画面で選べる。
    /// 横の見本は今の色で、空・読めない値なら既定の色 (fallback) を出す。
    /// </summary>
    private static UserControl ColorEditor(Color fallback, out TextBox text)
    {
        var box = text = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "空なら既定の色" };
        var swatch = new Panel { Width = 28, Dock = DockStyle.Right, BorderStyle = BorderStyle.FixedSingle };
        var pick = new Button { Text = "選ぶ…", AutoSize = true, Dock = DockStyle.Right };
        Color Current() => Settings.ParseColor(box.Text) is var (r, g, b) ? Color.FromArgb(r, g, b) : fallback;
        box.TextChanged += (_, _) => swatch.BackColor = Current();
        swatch.BackColor = fallback;
        pick.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = Current(), FullOpen = true };
            if (dialog.ShowDialog(pick.FindForm()) == DialogResult.OK) box.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        };
        // Panel だと表と同じく名前の下に幅いっぱいで出るので、ほかの項目と同じ右の列に置く UserControl にする
        var panel = new UserControl { Height = Math.Max(box.PreferredHeight, pick.PreferredSize.Height) + 4, Dock = DockStyle.Fill };
        panel.Controls.Add(box);
        panel.Controls.Add(swatch);
        panel.Controls.Add(pick);
        return panel;
    }

    private static DataGridView AppRulesGrid()
    {
        var grid = new DataGridView
        {
            Height = 200,
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersWidth = 24,
            BackgroundColor = SystemColors.Window,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "プロセス名 (例: code.exe)", FillWeight = 55 });
        grid.Columns.Add(new ToggleSwitchColumn("有効", 22));
        // 種類: 一般 / コード (コメント・文字列の中だけ日本語)
        var profile = new DataGridViewComboBoxColumn { HeaderText = "種類", FillWeight = 23, FlatStyle = FlatStyle.Flat };
        profile.Items.AddRange(Enum.GetValues<AppProfile>().Select(p => (object)EnumName(typeof(AppProfile), p)).ToArray());
        grid.Columns.Add(profile);
        grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells[1].Value = true;
            e.Row.Cells[2].Value = EnumName(typeof(AppProfile), AppProfile.General);
        };
        grid.DataError += (_, e) => e.ThrowException = false;
        CommitSwitchesImmediately(grid);
        return grid;
    }

    /// <summary>
    /// スイッチのセルは、クリックした時点で値を確定する (チェックボックスの列は、ほかのセルに移るまで値が変わらないので、
    /// 切り替えてすぐ OK を押したときや、独自の種類の名前の一覧を出し直すときに、前の値が読まれる)。
    /// </summary>
    private static void CommitSwitchesImmediately(DataGridView grid)
    {
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is ToggleSwitchCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    private static string EnumName(Type type, object value) =>
        type.GetField(value.ToString()!)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString()!;

    /// <summary>"1. 全般" → "全般"</summary>
    private static string StripNumber(string category)
    {
        var dot = category.IndexOf(". ", StringComparison.Ordinal);
        return dot >= 0 && category[..dot].All(char.IsAsciiDigit) ? category[(dot + 2)..] : category;
    }

    private void ShowHelpFor(Control control, string name, string description)
    {
        if (description.Length > 0) _toolTip.SetToolTip(control, WrapToolTip(description));
        var text = description.Length > 0 ? $"{name}: {description}" : name;
        void Show(object? sender, EventArgs e) => _help.Text = text;
        control.Enter += Show;
        control.MouseEnter += Show;
    }

    /// <summary>
    /// ツールチップの説明を、文 (「。」) ごとに改行し、長い文は半角 80 字 (全角 40 字) くらいで折り返す。
    /// ツールチップは画面の幅まで 1 行で出るので、そのままだと横に長くて読みにくい。
    /// 英単語の途中では折り返さず、閉じ括弧・句読点は行頭に、開き括弧は行末に置かない。
    /// </summary>
    internal static string WrapToolTip(string text, int maxWidth = 80)
    {
        const string opening = "(（「『[";
        const string closing = ")）」』]、。,.・";
        var lines = new List<string>();
        foreach (var sentence in Sentences(text))
        {
            var line = new StringBuilder();
            var width = 0;
            foreach (var token in Tokens(sentence))
            {
                var tokenWidth = token.Sum(c => c < 0x80 ? 1 : 2);
                if (width > 0 && width + tokenWidth > maxWidth && !string.IsNullOrWhiteSpace(token))
                {
                    lines.Add(line.ToString().TrimEnd());
                    line.Clear();
                    width = 0;
                }
                if (width == 0 && string.IsNullOrWhiteSpace(token)) continue;
                line.Append(token);
                width += tokenWidth;
            }
            if (line.Length > 0) lines.Add(line.ToString().TrimEnd());
        }
        return string.Join("\n", lines);

        static IEnumerable<string> Sentences(string text)
        {
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '。') continue;
                var end = i + 1;
                while (end < text.Length && ")）」』".Contains(text[end])) end++;
                yield return text[start..end].Trim();
                start = end;
                i = end - 1;
            }
            if (text[start..].Trim() is { Length: > 0 } rest) yield return rest;
        }

        // 折り返してよい単位: 英数字の語 1 つ、または全角の 1 字 (前の開き括弧・後ろの閉じ括弧と句読点を含める)
        static IEnumerable<string> Tokens(string sentence)
        {
            var i = 0;
            while (i < sentence.Length)
            {
                var start = i;
                while (i < sentence.Length && opening.Contains(sentence[i])) i++;
                if (i < sentence.Length && IsWordChar(sentence[i]))
                {
                    while (i < sentence.Length && IsWordChar(sentence[i])) i++;
                }
                else if (i < sentence.Length)
                {
                    i++;
                }
                while (i < sentence.Length && closing.Contains(sentence[i])) i++;
                yield return sentence[start..i];
            }
        }

        static bool IsWordChar(char c) => c is > ' ' and < (char)0x80 && !opening.Contains(c) && !closing.Contains(c);
    }

    private void LoadFrom(Settings settings)
    {
        _loadingSettings = true;
        try
        {
            foreach (var binding in _bindings) binding.Load(settings);
            RefreshKindChoices(settings.AppKinds.Select(k => k.Name));
        }
        finally { _loadingSettings = false; }
    }

    /// <summary>
    /// 「プレリリース版も自動で更新する」を OFF から ON にしたときだけ確かめる (issue #1206)。キャンセルなら OFF に戻す。
    /// ON から OFF にするときと、設定を読み込んだとき (利用者の操作でないとき) は何も聞かない。
    /// </summary>
    private void ConfirmPrerelease(ToggleSwitch toggle)
    {
        if (!toggle.Checked) return;
        var answer = MessageBox.Show(this,
            "プレリリース版は、正式版の前に試してもらうための版です。不具合や動作の不安定さが含まれることがあります。\n\n" +
            "プレリリース版も自動で更新するようにしますか？ (「OK」で有効にします。あとから OFF に戻せます)",
            "プレリリース版の自動更新", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK) toggle.Checked = false;
    }

    /// <summary>画面の内容を設定にする。画面に出していない項目は引き継ぎ、編集中の正規化では除外ログ・通知を出さない。</summary>
    private Settings Collect() => CollectValues().Normalize(reportShortcutExclusions: false);

    private Settings CollectValues()
    {
        var settings = _draft.Clone();
        foreach (var binding in _bindings) binding.Store(settings);
        settings.SettingsShowHelp = _showHelp.Checked;
        settings.SettingsWheelChangesValue = _wheelChangesValue.Checked;
        return settings;
    }

    private void RunTest()
    {
        if (_loadingSettings || _bindings.Count == 0) return;
        var words = _testInput.Text.ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            _testResult.Text = "";
            return;
        }
        var settings = Collect();
        var lines = new List<string>();
        foreach (var word in words)
        {
            var letters = new string(word.Where(c => c is >= 'a' and <= 'z').ToArray());
            if (letters.Length == 0) continue;
            // 実際の動作と同じく 1 文字ずつ判定し、最初に結論が出た時点の結果を表示する。
            for (var i = 1; i <= letters.Length; i++)
            {
                var result = _engine.Evaluate(letters[..i], settings);
                if (result.Verdict != Detection.Verdict.Undecided || i == letters.Length)
                {
                    var verdict = result.Verdict == Detection.Verdict.Undecided ? "Unknown (Space で確定)" : result.Verdict.ToString();
                    lines.Add($"{word}: {verdict} — \"{letters[..i]}\" の時点, JP={result.JapaneseScore} EN={result.EnglishScore}\r\n    " +
                              string.Join("\r\n    ", result.Contributions.Select(c => c.ToString())));
                    break;
                }
            }
        }
        _testResult.Text = string.Join("\r\n", lines);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
