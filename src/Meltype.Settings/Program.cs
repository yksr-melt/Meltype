// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Meltype.SettingsApp;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (SingleInstance.SendToRunning(args)) return 0;
        using var single = SingleInstance.Listen(received => Dispatcher.UIThread.Post(() =>
        {
            if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not MainWindow window) return;
            window.Open(received);
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }));
        return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

/// <summary>OS のライト / ダークに合わせる (FluentTheme の既定)。</summary>
internal sealed class App : Application
{
    public override void Initialize()
    {
        Name = "Meltype";
        Styles.Add(new FluentTheme());
        // 窓の背景の上に、項目ごとのカードを少し明るく浮かせる (Windows 11 の設定と同じ形)。ダークでもカードを背景より明るくするので、色はテーマごとに決める
        Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            ["PageBackground"] = new SolidColorBrush(Color.Parse("#F3F3F3")),
            ["CardBackground"] = new SolidColorBrush(Color.Parse("#FFFFFF")),
            ["CardBorder"] = new SolidColorBrush(Color.Parse("#E5E5E5")),
        };
        Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            ["PageBackground"] = new SolidColorBrush(Color.Parse("#202020")),
            ["CardBackground"] = new SolidColorBrush(Color.Parse("#2B2B2B")),
            ["CardBorder"] = new SolidColorBrush(Color.Parse("#1D1D1D")),
        };
        Styles.Add(new Style(x => x.OfType<Window>())
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension("PageBackground")) },
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("card"))
        {
            Setters =
            {
                new Setter(Border.PaddingProperty, new Thickness(16, 14)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(8)),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.BackgroundProperty, new DynamicResourceExtension("CardBackground")),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("CardBorder")),
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
        Styles.Add(new Style(x => x.OfType<Border>().Class("sidebar").Descendant().OfType<ListBox>())
        {
            Setters = { new Setter(ListBox.BackgroundProperty, Brushes.Transparent) },
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("sidebar").Descendant().OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(12, 6)), new Setter(ListBoxItem.CornerRadiusProperty, new CornerRadius(4)) },
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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow(desktop.Args ?? []);
        base.OnFrameworkInitializationCompleted();
    }
}
