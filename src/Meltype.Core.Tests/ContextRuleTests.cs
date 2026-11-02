// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>文脈の手がかりを、文節の読みの頭だけに当てはめない (issue #1360)。</summary>
internal static class ContextRuleTests
{
    [Test]
    public static void Issue1360_ContextRuleOnlyBeforeTrailingWords()
    {
        var rules = new ContextRules();
        rules.AddText("はし 端 : 左 右\nいけ 行け : 早く");
        Assert.Equal(null, rules.Choose("はしもと", "左内", "橋本"), "語の続き (はし|もと) には使わない");
        Assert.Equal(null, rules.Choose("はしら", "左の", "柱"), "1 文字の続き (はし|ら) にも使わない");
        Assert.Equal("端", rules.Choose("はし", "左の"), "読みそのものは今までどおり");
        Assert.Equal("端を", rules.Choose("はしを", "左の"), "助詞 (を) が続くなら使う");
        Assert.Equal("行けよ", rules.Choose("いけよ", "早く"), "終助詞 (よ) が続くなら使う");
        Assert.Equal("端っこ", rules.Choose("はしっこ", "左の", "端っこ"), "変換エンジンがかなのまま付けた残り (っこ) なら使う");
    }
}
