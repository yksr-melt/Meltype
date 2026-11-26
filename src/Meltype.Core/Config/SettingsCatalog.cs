// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Meltype.Config;

/// <summary>設定画面に出す 1 項目。Category は番号を除いた分類の名前 ("1. 全般" → "全般")。</summary>
public sealed record SettingItem(PropertyInfo Property, string Title, string Description, string Category);

/// <summary>
/// 設定画面に出す項目を、この OS で使うものだけ分類の順に並べる。
/// 名前・説明・分類は Settings のプロパティの属性 (DisplayName・Description・Category) から読み、
/// Browsable(false) と、ほかの OS だけで使う項目 (Platforms) は出さない。
/// </summary>
public static class SettingsCatalog
{
    // Settings のプロパティの属性を reflection で読む。NativeAOT では、設定アプリ (src/Meltype.Settings) が Meltype.Core を TrimmerRootAssembly にして削らない
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Meltype.Core は TrimmerRootAssembly")]
    public static IReadOnlyList<(string Category, IReadOnlyList<SettingItem> Items)> ForThisPlatform()
    {
        var platform = OperatingSystem.IsWindows() ? Platform.Windows : OperatingSystem.IsMacOS() ? Platform.Mac : Platform.Linux;
        return typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            .Where(p => p.GetCustomAttribute<PlatformsAttribute>() is not { } platforms || platforms.Platforms.HasFlag(platform))
            // ポータブル版は Meltype IME を登録しないので、動作モードを選ばせない
            .Where(p => !AppPaths.IsPortable || p.Name != nameof(Settings.Mode))
            .Select(p => (Property: p, Category: p.GetCustomAttribute<CategoryAttribute>()?.Category ?? ""))
            .GroupBy(p => p.Category)
            .OrderBy(g => CategoryNumber(g.Key))
            .Select(g => (Name(g.Key), (IReadOnlyList<SettingItem>)g.Select(p => new SettingItem(
                p.Property,
                p.Property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Property.Name,
                p.Property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "",
                Name(g.Key))).ToList()))
            .ToList();
    }

    /// <summary>列挙型の値の、画面に出す名前 (フィールドの Description。無ければ値の名前)。</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "列挙型は Meltype.Core のもので、Meltype.Core は TrimmerRootAssembly")]
    public static string ChoiceName(Type enumType, object value)
    {
        var name = Enum.GetName(enumType, value) ?? value.ToString() ?? "";
        return enumType.GetField(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
    }

    // 分類は "1. 全般" のように番号から始まり、番号の順に並べる。番号の無い分類は最後
    private static int CategoryNumber(string category)
    {
        var dot = category.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 && int.TryParse(category[..dot], out var number) ? number : int.MaxValue;
    }

    private static string Name(string category)
    {
        var dot = category.IndexOf(". ", StringComparison.Ordinal);
        return category.Length == 0 ? "その他" : dot > 0 && int.TryParse(category[..dot], out _) ? category[(dot + 2)..] : category;
    }
}
