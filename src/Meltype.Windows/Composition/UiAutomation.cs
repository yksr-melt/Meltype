// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Composition;

/// <summary>
/// Windows の UI Automation (UIAutomationCore.dll) を COM で直接使う薄い包み。
/// .NET の System.Windows.Automation は WPF 一式 (約 17MB) を必要とし、配布用パッケージが大きくなるので使わない。
/// フォーカスのある要素のプロパティ (種類・名前・パスワード欄か・読み取り専用か・位置) と、
/// テキストのキャレットの前後の文字列だけを取る。作成した MTA スレッドからだけ使う。
/// </summary>
internal sealed class UiAutomation
{
    // プロパティ ID・パターン ID・コントロールの種類 (UIAutomationClient.h)
    public const int ControlTypeEdit = 50004, ControlTypeDocument = 50030, ControlTypeComboBox = 50003, ControlTypePane = 50033, ControlTypeText = 50020;
    private const int BoundingRectangleProperty = 30001, ProcessIdProperty = 30002, ControlTypeProperty = 30003, NameProperty = 30005,
        AutomationIdProperty = 30011, NativeWindowHandleProperty = 30020,
        IsKeyboardFocusableProperty = 30009, ClassNameProperty = 30012, IsPasswordProperty = 30019,
        ValueValueProperty = 30045, ValueIsReadOnlyProperty = 30046, IsValuePatternAvailableProperty = 30043,
        IsTextPatternAvailableProperty = 30040;
    private const int TextPatternId = 10014, LegacyIAccessiblePatternId = 10018;
    private const int Ia2StateEditable = 0x8;
    private const int Ia2StatesVTableSlot = 35;
    private const int TextUnitCharacter = 0;
    private const int EndpointStart = 0, EndpointEnd = 1;

    private static readonly Guid ClsidCUIAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid IidIServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid IidIAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    private static readonly Guid IidIAccessible2 = new("E89F726E-C4F4-4C19-BB19-B647D7FA8478");

    private readonly IUIAutomation _automation;

    public UiAutomation()
    {
        var type = Type.GetTypeFromCLSID(ClsidCUIAutomation, throwOnError: true)!;
        _automation = (IUIAutomation)Activator.CreateInstance(type)!;
    }

    /// <summary>フォーカスのある要素。無ければ null。</summary>
    public Element? Focused()
    {
        var hr = _automation.GetFocusedElement(out var element);
        return hr >= 0 && element is not null ? new Element(element) : null;
    }

    /// <summary>画面全体 (デスクトップ) の要素。自己診断用。</summary>
    public Element? Root()
    {
        var hr = _automation.GetRootElement(out var element);
        return hr >= 0 && element is not null ? new Element(element) : null;
    }

    /// <summary>窓直下にも現れる Affinity の Static を、PART_RenderControl の子の HWND と照合する。</summary>
    public bool IsRenderControl(IntPtr foreground, Element focused, int focusedWindow)
    {
        if (foreground == IntPtr.Zero || focusedWindow == 0 ||
            _automation.ElementFromHandle(foreground, out var window) < 0 || window is null ||
            new Element(window).ProcessId != focused.ProcessId ||
            _automation.CreatePropertyCondition(AutomationIdProperty, "PART_RenderControl", out var hostCondition) < 0 || hostCondition is null ||
            window.FindFirst(4 /* Descendants */, hostCondition, out var host) < 0 || host is null) return false;
        var hostInfo = new Element(host);
        if (hostInfo.ClassName != "HwndHost" || hostInfo.ProcessId != focused.ProcessId ||
            _automation.CreatePropertyCondition(NativeWindowHandleProperty, focusedWindow, out var handleCondition) < 0 || handleCondition is null ||
            host.FindFirst(2 /* Children */, handleCondition, out var child) < 0 || child is null) return false;
        var childInfo = new Element(child);
        return childInfo.ProcessId == focused.ProcessId && (childInfo.ControlType is ControlTypePane or ControlTypeText) && childInfo.ClassName == "Static";
    }

    public sealed class Element
    {
        private readonly IUIAutomationElement _element;

        internal Element(IUIAutomationElement element) => _element = element;

        private object? Get(int property) => _element.GetCurrentPropertyValue(property, out var value) >= 0 ? value : null;

        public int ControlType => Get(ControlTypeProperty) is int type ? type : 0;
        public int ProcessId => Get(ProcessIdProperty) is int process ? process : 0;
        public int NativeWindowHandle => Get(NativeWindowHandleProperty) is int window ? window : 0;
        public string Name => Get(NameProperty) as string ?? "";
        public string ClassName => Get(ClassNameProperty) as string ?? "";
        public bool IsPassword => Get(IsPasswordProperty) is true;
        public bool IsKeyboardFocusable => Get(IsKeyboardFocusableProperty) is true;
        public bool HasValuePattern => Get(IsValuePatternAvailableProperty) is true;
        public bool HasTextPattern => Get(IsTextPatternAvailableProperty) is true;
        public bool IsReadOnly => Get(ValueIsReadOnlyProperty) is true;
        public string Value => Get(ValueValueProperty) as string ?? "";

        /// <summary>
        /// UI Automation では入力欄と分からない要素が、IAccessible2 では編集可能として
        /// 公開されているか調べる。Chromium / Electron の contenteditable などで使う。
        /// </summary>
        public unsafe bool IsIa2Editable()
        {
            try
            {
                if (_element.GetCurrentPattern(LegacyIAccessiblePatternId, out var patternObject) < 0 ||
                    patternObject is not IUIAutomationLegacyIAccessiblePattern pattern ||
                    pattern.GetIAccessible(out var accessible) < 0 ||
                    accessible == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    var serviceProviderIid = IidIServiceProvider;
                    if (Marshal.QueryInterface(accessible, in serviceProviderIid, out var serviceProvider) < 0 ||
                        serviceProvider == IntPtr.Zero)
                    {
                        return false;
                    }

                    try
                    {
                        var service = IidIAccessible;
                        var iid = IidIAccessible2;
                        IntPtr ia2 = IntPtr.Zero;

                        var hr =
                            ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)
                                VTable(serviceProvider, 3))(
                                    serviceProvider, &service, &iid, &ia2);

                        if (hr < 0 || ia2 == IntPtr.Zero) return false;

                        try
                        {
                            int states;
                            hr =
                                ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)
                                    VTable(ia2, Ia2StatesVTableSlot))(
                                        ia2, &states);

                            return hr >= 0 && (states & Ia2StateEditable) != 0;
                        }
                        finally
                        {
                            Marshal.Release(ia2);
                        }
                    }
                    finally
                    {
                        Marshal.Release(serviceProvider);
                    }
                }
                finally
                {
                    Marshal.Release(accessible);
                }
            }
            catch
            {
                // IAccessible2 を使えない要素・アプリでは、従来の判定だけを使う。
                return false;
            }
        }

        private static unsafe IntPtr VTable(IntPtr instance, int slot) =>
            (*(IntPtr**)instance)[slot];

        /// <summary>単一の選択範囲を取得。無ければ null。</summary>
        private IUIAutomationTextRange? SelectionRange()
        {
            if (_element.GetCurrentPattern(TextPatternId, out var value) < 0 || value is not IUIAutomationTextPattern pattern ||
                pattern.GetSelection(out var ranges) < 0 || ranges is null || ranges.get_Length(out var count) < 0 || count != 1 ||
                ranges.GetElement(0, out var range) < 0) return null;
            return range;
        }

        /// <summary>単一の選択範囲を取得し、同じ範囲・文字が選択されているか確かめる関数を返す。</summary>
        public string? SelectedText(out Func<Element, bool>? matches)
        {
            matches = null;
            var range = SelectionRange();
            // 長文の誤選択を再変換に渡さない。超過を検知するため取得は上限 + 1 文字。
            const int limit = 128;
            if (range is null || range.GetText(limit + 1, out var text) < 0 || string.IsNullOrEmpty(text) || text.Length > limit) return null;
            matches = current => current.SelectionRange() is { } selected &&
                range.Compare(selected, out var same) >= 0 && same &&
                selected.GetText(limit + 1, out var actual) >= 0 && actual == text;
            return text;
        }

        public Rectangle? Bounds =>
            Get(BoundingRectangleProperty) is double[] { Length: 4 } r && r[2] > 0 && r[3] > 0 && !double.IsInfinity(r[2])
                ? new Rectangle((int)r[0], (int)r[1], (int)r[2], (int)r[3])
                : null;

        /// <summary>テキストのキャレット (選択範囲) の前後の文字列 (それぞれ最大 count 文字)。テキストパターンが無ければ null。</summary>
        public (string? Before, string? After)? Surrounding(int count)
        {
            if (_element.GetCurrentPattern(TextPatternId, out var patternObject) < 0 || patternObject is not IUIAutomationTextPattern pattern) return null;
            if (pattern.GetSelection(out var ranges) < 0 || ranges is null) return null;
            if (ranges.get_Length(out var length) < 0 || length == 0 || ranges.GetElement(0, out var selection) < 0 || selection is null) return null;

            selection.Clone(out var before);
            before.MoveEndpointByRange(EndpointEnd, before, EndpointStart);
            before.MoveEndpointByUnit(EndpointStart, TextUnitCharacter, -count, out _);
            before.GetText(count * 2, out var beforeText);

            selection.Clone(out var after);
            after.MoveEndpointByRange(EndpointStart, after, EndpointEnd);
            after.MoveEndpointByUnit(EndpointEnd, TextUnitCharacter, count, out _);
            after.GetText(count * 2, out var afterText);
            return (beforeText, afterText);
        }

        /// <summary>
        /// キャレット (入力位置) の画面上の四角形 (幅 1)。テキストパターンで取れなければ null。
        /// 何も選んでいない (幅 0 の) 範囲は四角形を返さないアプリが多いので、前の 1 文字の右端か、次の 1 文字の左端を使う。
        /// </summary>
        public Rectangle? CaretBounds()
        {
            if (_element.GetCurrentPattern(TextPatternId, out var patternObject) < 0 || patternObject is not IUIAutomationTextPattern pattern) return null;
            if (pattern.GetSelection(out var ranges) < 0 || ranges is null) return null;
            if (ranges.get_Length(out var length) < 0 || length == 0 || ranges.GetElement(0, out var selection) < 0 || selection is null) return null;

            if (FirstRectangle(selection) is { } own) return new Rectangle(own.Left, own.Top, 1, own.Height);
            selection.Clone(out var before);
            before.MoveEndpointByRange(EndpointEnd, before, EndpointStart);
            before.MoveEndpointByUnit(EndpointStart, TextUnitCharacter, -1, out var movedBack);
            if (movedBack != 0 && LastRectangle(before) is { } previous) return new Rectangle(previous.Right, previous.Top, 1, previous.Height);
            selection.Clone(out var after);
            after.MoveEndpointByRange(EndpointStart, after, EndpointEnd);
            after.MoveEndpointByUnit(EndpointEnd, TextUnitCharacter, 1, out var movedForward);
            if (movedForward != 0 && FirstRectangle(after) is { } next) return new Rectangle(next.Left, next.Top, 1, next.Height);
            return null;
        }

        private static Rectangle? FirstRectangle(IUIAutomationTextRange range) => Rectangles(range).FirstOrDefault() is { Height: > 0 } r ? r : null;

        private static Rectangle? LastRectangle(IUIAutomationTextRange range) => Rectangles(range).LastOrDefault() is { Height: > 0 } r ? r : null;

        private static List<Rectangle> Rectangles(IUIAutomationTextRange range)
        {
            var list = new List<Rectangle>();
            if (range.GetBoundingRectangles(out var values) < 0 || values is null) return list;
            for (var i = 0; i + 3 < values.Length; i += 4)
            {
                if (values[i + 3] > 0) list.Add(new Rectangle((int)values[i], (int)values[i + 1], Math.Max(1, (int)values[i + 2]), (int)values[i + 3]));
            }
            return list;
        }
    }

    // ---- COM の定義 (UIAutomationClient.h の順番どおり。使わないメソッドは並びを保つための仮の宣言) ----

    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig] int CompareElements();
        [PreserveSig] int CompareRuntimeIds();
        [PreserveSig] int GetRootElement(out IUIAutomationElement? root);
        [PreserveSig] int ElementFromHandle(IntPtr window, out IUIAutomationElement? element);
        [PreserveSig] int ElementFromPoint();
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement? element);
        [PreserveSig] int GetRootElementBuildCache();
        [PreserveSig] int ElementFromHandleBuildCache();
        [PreserveSig] int ElementFromPointBuildCache();
        [PreserveSig] int GetFocusedElementBuildCache();
        [PreserveSig] int CreateTreeWalker();
        [PreserveSig] int get_ControlViewWalker();
        [PreserveSig] int get_ContentViewWalker();
        [PreserveSig] int get_RawViewWalker();
        [PreserveSig] int get_RawViewCondition();
        [PreserveSig] int get_ControlViewCondition();
        [PreserveSig] int get_ContentViewCondition();
        [PreserveSig] int CreateCacheRequest();
        [PreserveSig] int CreateTrueCondition();
        [PreserveSig] int CreateFalseCondition();
        [PreserveSig] int CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, out IUIAutomationCondition? condition);
    }

    [ComImport, Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationCondition { }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId();
        [PreserveSig] int FindFirst(int scope, IUIAutomationCondition condition, out IUIAutomationElement? element);
        [PreserveSig] int FindAll();
        [PreserveSig] int FindFirstBuildCache();
        [PreserveSig] int FindAllBuildCache();
        [PreserveSig] int BuildUpdatedCache();
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
        [PreserveSig] int GetCurrentPropertyValueEx();
        [PreserveSig] int GetCachedPropertyValue();
        [PreserveSig] int GetCachedPropertyValueEx();
        [PreserveSig] int GetCurrentPatternAs();
        [PreserveSig] int GetCachedPatternAs();
        [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
    }

    [ComImport, Guid("828055AD-355B-4435-86D5-3B51C14A9B1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationLegacyIAccessiblePattern
    {
        [PreserveSig] int Select();
        [PreserveSig] int DoDefaultAction();
        [PreserveSig] int SetValue();
        [PreserveSig] int get_CurrentChildId();
        [PreserveSig] int get_CurrentName();
        [PreserveSig] int get_CurrentValue();
        [PreserveSig] int get_CurrentDescription();
        [PreserveSig] int get_CurrentRole();
        [PreserveSig] int get_CurrentState();
        [PreserveSig] int get_CurrentHelp();
        [PreserveSig] int get_CurrentKeyboardShortcut();
        [PreserveSig] int GetCurrentSelection();
        [PreserveSig] int get_CurrentDefaultAction();
        [PreserveSig] int get_CachedChildId();
        [PreserveSig] int get_CachedName();
        [PreserveSig] int get_CachedValue();
        [PreserveSig] int get_CachedDescription();
        [PreserveSig] int get_CachedRole();
        [PreserveSig] int get_CachedState();
        [PreserveSig] int get_CachedHelp();
        [PreserveSig] int get_CachedKeyboardShortcut();
        [PreserveSig] int GetCachedSelection();
        [PreserveSig] int get_CachedDefaultAction();
        [PreserveSig] int GetIAccessible(out IntPtr accessible);
    }

    [ComImport, Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        [PreserveSig] int RangeFromPoint();
        [PreserveSig] int RangeFromChild();
        [PreserveSig] int GetSelection(out IUIAutomationTextRangeArray? ranges);
    }

    [ComImport, Guid("CE4AE76A-E717-4C98-81EA-47371D028EB6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        [PreserveSig] int get_Length(out int length);
        [PreserveSig] int GetElement(int index, out IUIAutomationTextRange? range);
    }

    [ComImport, Guid("A543CC6A-F4AE-494B-8239-C814481187A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        [PreserveSig] int Clone(out IUIAutomationTextRange range);
        [PreserveSig] int Compare(IUIAutomationTextRange range, [MarshalAs(UnmanagedType.Bool)] out bool equal);
        [PreserveSig] int CompareEndpoints();
        [PreserveSig] int ExpandToEnclosingUnit();
        [PreserveSig] int FindAttribute();
        [PreserveSig] int FindText();
        [PreserveSig] int GetAttributeValue();
        [PreserveSig] int GetBoundingRectangles([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] out double[]? rectangles);
        [PreserveSig] int GetEnclosingElement();
        [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string? text);
        [PreserveSig] int Move();
        [PreserveSig] int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);
        [PreserveSig] int MoveEndpointByRange(int endpoint, IUIAutomationTextRange range, int targetEndpoint);
    }
}
