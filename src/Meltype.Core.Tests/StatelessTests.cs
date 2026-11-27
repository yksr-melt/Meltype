// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using System.Reflection;
using System.Runtime.CompilerServices;
using Meltype.Composition;
using Meltype.Ime;

namespace Meltype.Tests;

/// <summary>入力欄の状態は ImeHost が持つ状態のクラスだけに置き、処理の関数は状態を持たない。</summary>
internal static class StatelessTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Test]
    public static void Functions_HaveNoMutableStaticFields()
    {
        foreach (var type in new[] { typeof(CompositionController), typeof(CompositionText), typeof(CompositionOutput), typeof(ImeSession) })
        {
            var mutable = type.GetFields(Declared).Where(f => !f.IsInitOnly && !f.IsLiteral).Select(f => f.Name).ToList();
            Assert.True(mutable.Count == 0, $"{type.Name} の書き換えられる static フィールド (状態は CompositionState・CompositionTextState・CompositionOutputState・ImeSessionState に置く): {string.Join(", ", mutable)}");
        }
    }

    [Test]
    public static void States_AreDataOnly()
    {
        foreach (var type in new[] { typeof(CompositionState), typeof(CompositionInput), typeof(CompositionTextState), typeof(CompositionOutputState), typeof(ImeSessionState) })
        {
            var methods = type.GetMethods(Declared).Where(m => !m.IsSpecialName && m.GetCustomAttribute<CompilerGeneratedAttribute>() is null).Select(m => m.Name).ToList();
            Assert.True(methods.Count == 0, $"{type.Name} の関数 (処理は CompositionController・CompositionText・CompositionOutput・ImeSession に置く): {string.Join(", ", methods)}");
        }
    }
}
