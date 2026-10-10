// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using System.Text.Json.Serialization;
using Meltype.Composition;
using Meltype.Learning;

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

/// <summary>学習データ (languages.json・translations.json・model.json) の JSON の読み書き (issue #151)。</summary>
[JsonSerializable(typeof(Dictionary<string, LanguageMemory.Entry>), TypeInfoPropertyName = "Languages")]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, int>>), TypeInfoPropertyName = "Translations")]
[JsonSerializable(typeof(UserModel.ModelFile))]
internal sealed partial class LearningJsonContext : JsonSerializerContext;

/// <summary>変換の学習 (conversions.json) の JSON の読み書き。Entry の名前が LanguageMemory と重なるので分けている。</summary>
[JsonSerializable(typeof(Dictionary<string, ConversionHistory.Entry>), TypeInfoPropertyName = "Conversions")]
internal sealed partial class ConversionJsonContext : JsonSerializerContext;

/// <summary>model.json は字下げして書く。</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UserModel.ModelFile))]
internal sealed partial class IndentedLearningJsonContext : JsonSerializerContext;

/// <summary>定型文 (snippets.json) の JSON の読み書き (issue #290)。手で読み書きしやすいように字下げする。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "Snippets")]
internal sealed partial class SnippetJsonContext : JsonSerializerContext;
