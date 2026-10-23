// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using System.Text.Json.Serialization;
using Meltype.Composition;

namespace Meltype.Config;

/// <summary>
/// 設定 (config.json) の JSON の読み書き。型の情報をビルド時に作る (System.Text.Json のソース生成)。
/// Mac・Linux 版の本体は NativeAOT なので、実行時に型を調べる読み書き (reflection) では列挙型のプロパティを読めず、
/// 設定が毎回既定値になっていた (issue #151)。列挙型は名前 ("Keyboard") で読み書きする。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// 列挙型を名前で読み書きし、前の版で選べた今は無い選択肢 (removed) は replacement として読む。
/// 知らない名前があると config.json 全体を読めず、設定がすべて既定値に戻る。
/// </summary>
internal abstract class RemovedChoiceJsonConverter<T>(string removed, T replacement) : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && string.Equals(reader.GetString(), removed, StringComparison.OrdinalIgnoreCase)) return replacement;
        return Names(options).Read(ref reader, typeToConvert, options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => Names(options).Write(writer, value, options);

    private static JsonConverter<T> Names(JsonSerializerOptions options) => (JsonConverter<T>)new JsonStringEnumConverter<T>().CreateConverter(typeof(T), options);
}

/// <summary>動作モードの IME 自動切替 (消した) は、初期値の Meltype IME として読む (使えない PC では、起動したときに Meltype キーボードに戻す)。</summary>
internal sealed class InputModeJsonConverter() : RemovedChoiceJsonConverter<InputMode>("AutoSwitch", InputMode.Tsf);

/// <summary>入力方式の「両方を判定」(IME 自動切替でしか効かなかったので消した) は、ローマ字入力として読む。</summary>
internal sealed class InputStyleJsonConverter() : RemovedChoiceJsonConverter<InputStyle>("Both", InputStyle.Romaji);

/// <summary>学習データ (languages.json・translations.json) の JSON の読み書き (issue #151)。</summary>
[JsonSerializable(typeof(Dictionary<string, LanguageMemory.Entry>), TypeInfoPropertyName = "Languages")]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, int>>), TypeInfoPropertyName = "Translations")]
internal sealed partial class LearningJsonContext : JsonSerializerContext;

/// <summary>変換の学習 (conversions.json) の JSON の読み書き。Entry の名前が LanguageMemory と重なるので分けている。</summary>
[JsonSerializable(typeof(Dictionary<string, ConversionHistory.Entry>), TypeInfoPropertyName = "Conversions")]
internal sealed partial class ConversionJsonContext : JsonSerializerContext;

/// <summary>定型文 (snippets.json) の JSON の読み書き (issue #290)。手で読み書きしやすいように字下げする。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "Snippets")]
internal sealed partial class SnippetJsonContext : JsonSerializerContext;

/// <summary>打鍵数 (keycount.json) の JSON の読み書き。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Meltype.Input.KeyCounter.Data))]
internal sealed partial class KeyCountJsonContext : JsonSerializerContext;
