// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace Meltype.SettingsApp;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = JapaneseFont() })
            .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// 画面の既定のフォント。日本語のフォントにする: OS の既定のフォントに漢字が無いと、Avalonia は代わりに中国語の字形のフォントで漢字を描くことがある。
    /// Linux は入っているフォントが PC ごとに違うので、fontconfig に日本語のゴシックを聞く。
    /// </summary>
    private static string JapaneseFont()
    {
        if (OperatingSystem.IsWindows()) return "Yu Gothic UI";
        if (OperatingSystem.IsMacOS()) return "Hiragino Sans";
        try
        {
            using var match = Process.Start(new ProcessStartInfo("fc-match", ["--format=%{family[0]}", "sans-serif:lang=ja"]) { RedirectStandardOutput = true })!;
            var family = match.StandardOutput.ReadToEnd().Trim();
            match.WaitForExit();
            if (family.Length > 0) return family;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // fc-match が無い (fontconfig の道具が入っていない)
        }
        return "Noto Sans CJK JP";
    }
}

/// <summary>見た目は WinUI 3 の Fluent 2 (FluentAvalonia)。ライト / ダークとアクセントの色は OS に合わせる。</summary>
internal sealed class App : Application
{
    public override void Initialize()
    {
        Name = "Meltype";
        // UseSystemFontOnWindows: 既定のフォントを Segoe UI にしない (Segoe UI に漢字が無い。JapaneseFont を参照)
        // ワークアラウンド: FluentAvalonia 3.1.0 は、PreferSystemTheme・PreferUserAccentColor で Linux の OS の設定を gsettings で読み、gsettings が無いと起動時に例外を投げる (GUI の無い環境など)。
        // ライト / ダークは Avalonia の既定 (RequestedThemeVariant = Default) で OS に合わせ、アクセントの色は Avalonia が OS から読んだ色を渡す
        var theme = new FluentAvaloniaTheme { UseSystemFontOnWindows = false };
        if (PlatformSettings is { } platform)
        {
            theme.CustomAccentColor = platform.GetColorValues().AccentColor1;
            platform.ColorValuesChanged += (_, colors) => theme.CustomAccentColor = colors.AccentColor1;
        }
        Styles.Add(theme);
        // 窓の背景の上に、項目ごとのカードを少し明るく浮かせる (Windows 11 の設定と同じ形)。色は WinUI 3 のテーマのリソース
        Styles.Add(new Style(x => x.OfType<Window>())
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("SolidBackgroundFillColorBaseBrush")) },
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("card"))
        {
            Setters =
            {
                new Setter(Border.PaddingProperty, new Thickness(16, 14)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(8)),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.BackgroundProperty, new DynamicResourceExtension("CardBackgroundFillColorDefaultBrush")),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("CardStrokeColorDefaultBrush")),
            },
        });
        // ページの中身: 広い窓でも 1 行が長くなりすぎないように、幅に上限を付けて左に寄せる
        Styles.Add(new Style(x => x.OfType<StackPanel>().Class("page"))
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(32, 24)),
                new Setter(Layoutable.MaxWidthProperty, 880.0),
                new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Left),
            },
        });
        // 上のページがスクロールしても境目が分かるように、背景をカードの色にして線で区切る (ダークでは、線の色と窓の背景がほぼ同じ)。左右の余白は page にそろえる
        Styles.Add(new Style(x => x.OfType<Border>().Class("preview"))
        {
            Setters =
            {
                new Setter(Border.PaddingProperty, new Thickness(32, 12)),
                new Setter(Border.BorderThicknessProperty, new Thickness(0, 1, 0, 0)),
                new Setter(Border.BackgroundProperty, new DynamicResourceExtension("CardBackgroundFillColorDefaultBrush")),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("CardStrokeColorDefaultBrush")),
            },
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("sidebar").Descendant().OfType<ListBox>())
        {
            Setters = { new Setter(ListBox.BackgroundProperty, Brushes.Transparent) },
        });
        // 項目の高さを WinUI 3 の NavigationViewItem の 36 にする (ListBoxItem の既定の 40 だと、既定の窓の高さで全ページが入らずスクロールする)
        Styles.Add(new Style(x => x.OfType<Border>().Class("sidebar").Descendant().OfType<ListBoxItem>())
        {
            Setters = { new Setter(Layoutable.MinHeightProperty, 36.0) },
        });
        Styles.Add(new Style(x => x.OfType<TextBlock>().Class("pageHeader"))
        {
            Setters =
            {
                new Setter(TextBlock.FontSizeProperty, 24.0),
                new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold),
                new Setter(Layoutable.MarginProperty, new Thickness(0, 0, 0, 8)),
            },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (OperatingSystem.IsWindows()) WindowsCards.StartMeltype();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow(desktop.Args ?? []);
        base.OnFrameworkInitializationCompleted();
    }
}
