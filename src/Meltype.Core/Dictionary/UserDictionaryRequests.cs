// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada

using System.Text.Json;
using Meltype.Config;

namespace Meltype.Dictionary;

/// <summary>ユーザー辞書への 1 つの頼み。Op は list・add・edit・remove。add は Reading・Word、edit は Target・Reading・Word、remove は Target を使う。</summary>
internal sealed record UserDictionaryRequest(string Op, string? Reading = null, string? Word = null, UserWord? Target = null);

/// <summary>頼みの後の userdict.txt の語 (ファイルの順。読めなければ null) と、できなかった理由 (できたら null)。</summary>
internal sealed record UserDictionaryResponse(IReadOnlyList<UserWord>? Words, string? Error);

/// <summary>
/// 画面を別の言語で描く OS (Android の設定画面。Core.CApi の meltype_user_dictionary) から、ユーザー辞書 (userdict.txt) の
/// 一覧・登録・編集・削除を頼む。読み書きと検査は <see cref="UserDictionary"/> がするので、どの OS でも同じ形式・同じ理由になる。
/// 読みのローマ字は、頼む前に <see cref="Ime.TypedReading"/> でひらがなにしておく (Windows・Mac・Linux の設定アプリと同じ)。
/// </summary>
public static class UserDictionaryRequests
{
    /// <summary>JSON の頼み (<see cref="UserDictionaryRequest"/>) を受けて、JSON の答え (<see cref="UserDictionaryResponse"/>) を返す。</summary>
    public static string Handle(string dataDirectory, string request)
    {
        var dictionary = new UserDictionary(Path.Combine(dataDirectory, AppPaths.UserDictionaryFileName), builtIn: false);
        string? error;
        try
        {
            error = JsonSerializer.Deserialize(request, UserDictionaryJsonContext.Default.UserDictionaryRequest) is not { } r ? "頼みを読めませんでした。" : r.Op switch
            {
                "list" => null,
                "add" => dictionary.Add(r.Reading ?? "", r.Word ?? ""),
                "edit" when r.Target is { } target => dictionary.Edit(target, r.Reading ?? "", r.Word ?? ""),
                "remove" when r.Target is { } target => Remove(dictionary, target),
                _ => $"知らない頼みです: {r.Op}",
            };
        }
        catch (JsonException)
        {
            error = "頼みを読めませんでした。";
        }
        // 読めなかったら (Refresh が false) 前の語を出さない。画面は一覧の代わりに理由を出す
        var words = dictionary.Refresh() ? dictionary.Words.ToList() : null;
        return JsonSerializer.Serialize(new UserDictionaryResponse(words, error), UserDictionaryJsonContext.Default.UserDictionaryResponse);
    }

    private static string? Remove(UserDictionary dictionary, UserWord target)
    {
        try
        {
            dictionary.Remove(target);
            return null;
        }
        catch (IOException ex)
        {
            return ex.Message;
        }
    }
}
