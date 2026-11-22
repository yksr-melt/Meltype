// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
        Styles.Add(new Style(x => x.OfType<Border>().Class("card"))
        {
            Setters =
            {
                new Setter(Border.PaddingProperty, new Thickness(16, 12)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("SystemControlForegroundBaseLowBrush")),
            },
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("sidebar"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush")) },
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
            Setters = { new Setter(TextBlock.FontSizeProperty, 18.0), new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold) },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow(desktop.Args ?? []);
        base.OnFrameworkInitializationCompleted();
    }
}
