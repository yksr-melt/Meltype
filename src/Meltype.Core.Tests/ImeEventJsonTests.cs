// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors

using Meltype.Ime;

namespace Meltype.Tests;

/// <summary>出来事 (<see cref="ImeEvent"/>) の JSON。種類ごとに、要る値が無い JSON と、種類に無い項目の付いた JSON は読めない。</summary>
internal static class ImeEventJsonTests
{
    private static readonly ImeCaret Clicked = new() { Moved = true, PassedKey = 0 };

    /// <summary>種類ごとの項目 (version・type のほか) と、読んだ結果。</summary>
    private static readonly (string Fields, ImeEvent Event)[] Readable =
    [
        ("""{"key":{"vk":65,"ch":97}}""", new ImeEvent.KeyPressed(new ImeKey { Vk = 65, Ch = 97 })),
        ("""{"key":{"ch":97,"shift":true}}""", new ImeEvent.KeyPressed(new ImeKey { Ch = 97, Shift = true })),
        ("""{"key":{"vk":164,"interrupted":true}}""", new ImeEvent.KeyReleased(new ImeKeyRelease(164, Interrupted: true))),
        ("""{"key":{"vk":29}}""", new ImeEvent.KeyReleased(new ImeKeyRelease(29))),
        ("{}", new ImeEvent.ModeToggleRequested()),
        ("{}", new ImeEvent.JapaneseModeSelected()),
        ("{}", new ImeEvent.AlphanumericModeSelected()),
        ("""{"caret":{"moved":true,"passedKey":0}}""", new ImeEvent.CommitRequested(Clicked)),
        ("{}", new ImeEvent.CommitRequested()),
        ("""{"caret":{"moved":true,"passedKey":0}}""", new ImeEvent.CommittedByApp(Clicked)),
        ("""{"index":0}""", new ImeEvent.CandidateClicked(0)),
        ("""{"index":2}""", new ImeEvent.CandidateHighlighted(2)),
        ("{}", new ImeEvent.ConversionFinished()),
        ("""{"caret":{"moved":true,"passedKey":0}}""", new ImeEvent.CaretMoved(Clicked)),
        ("""{"caret":{"moved":false}}""", new ImeEvent.CaretMoved(new ImeCaret())),
        ("{}", new ImeEvent.StateRequested()),
        ("{}", new ImeEvent.InstanceDestroyed()),
    ];

    /// <summary>version の後に type、その後に項目 (フロントの書く順)。last なら type を最後に書く (Mac の辞書は順が決まっていない)。</summary>
    private static string Json(string type, string fields, bool last = false)
    {
        var body = fields[1..^1];
        var separator = body.Length > 0 ? "," : "";
        return last
            ? $$"""{"version":{{ImeHost.Version}},{{body}}{{separator}}"type":"{{type}}"}"""
            : $$"""{"version":{{ImeHost.Version}},"type":"{{type}}"{{separator}}{{body}}}""";
    }

    [Test]
    public static void EveryKind_IsRead()
    {
        // JsonDerivedType の付け忘れはコンパイルエラーにならない
        var kinds = typeof(ImeEvent).GetNestedTypes().Where(type => type.IsSealed && typeof(ImeEvent).IsAssignableFrom(type)).Select(type => type.Name).Order();
        var covered = Readable.Select(sample => sample.Event.GetType().Name).Distinct().Order();
        Assert.Equal(string.Join(",", kinds), string.Join(",", covered), "すべての種類を確かめる");
        foreach (var (fields, e) in Readable)
        {
            foreach (var last in new[] { false, true })
            {
                var json = Json(e.GetType().Name, fields, last);
                Assert.Equal(e, ImeHost.Parse(json), json);
            }
        }
    }

    [Test]
    public static void MissingRequiredValues_AreUnreadable()
    {
        foreach (var (type, fields) in new[]
                 {
                     ("KeyPressed", "{}"), ("KeyPressed", """{"key":null}"""),
                     ("KeyReleased", "{}"), ("KeyReleased", """{"key":{}}"""), ("KeyReleased", """{"key":{"interrupted":true}}"""),
                     ("CommittedByApp", "{}"), ("CommittedByApp", """{"caret":null}"""),
                     ("CaretMoved", "{}"), ("CaretMoved", """{"caret":null}"""),
                     ("CandidateClicked", "{}"), ("CandidateClicked", """{"index":null}"""),
                     ("CandidateHighlighted", "{}"),
                 })
        {
            var json = Json(type, fields);
            Assert.True(ImeHost.Parse(json) is null, json);
        }
        Assert.True(ImeHost.Parse("""{"type":"StateRequested"}""") is null, "version が無い");
        Assert.True(ImeHost.Parse($$"""{"version":{{ImeHost.Version}}}""") is null, "type が無い");
    }

    [Test]
    public static void ItemsOfOtherKinds_AreUnreadable()
    {
        var items = new[] { """{"key":{"vk":65}}""", """{"caret":{"moved":true}}""", """{"index":0}""" };
        foreach (var (fields, e) in Readable)
        {
            var type = e.GetType().Name;
            foreach (var item in items)
            {
                var name = item.Split('"')[1];
                if (fields.Contains(name) || e is ImeEvent.CommitRequested && name == "caret") continue;
                var json = Json(type, fields == "{}" ? item : fields[..^1] + "," + item[1..]);
                Assert.True(ImeHost.Parse(json) is null, json);
            }
        }
        Assert.True(ImeHost.Parse(Json("KeyPressed", """{"key":{"vk":65,"interrupted":true}}""")) is null, "押したキーに interrupted は無い");
        Assert.True(ImeHost.Parse(Json("KeyReleased", """{"key":{"vk":65,"ch":97}}""")) is null, "離したキーに ch は無い");
    }
}
