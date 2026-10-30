// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

namespace Meltype.Config;

/// <summary>
/// config.json の JSON Schema。config.json と同じフォルダーに config.schema.json として書き、config.json の "$schema" から指す
/// (設定画面の無い Mac・Linux 版で config.json を手で書くとき、エディターが補完・説明・値の検査を出す)。
/// Settings の型と、設定画面に出す名前・説明 (DisplayName・Description) から、起動のたびに作る。
/// </summary>
public static class SettingsSchema
{
    public const string FileName = "config.schema.json";

    private static readonly JsonSerializerOptions OutputOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Create()
    {
        var defaults = JsonSerializer.SerializeToNode(new Settings(), SettingsJsonContext.Default.Settings)!.AsObject();
        var schema = SettingsJsonContext.Default.Settings.GetJsonSchemaAsNode(new JsonSchemaExporterOptions
        {
            // config.json の一番上とリストの中身は、null でなく {} を書く
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) => Transform(context, node, defaults),
        });
        return schema.ToJsonString(OutputOptions) + "\n";
    }

    /// <summary>dataDirectory に config.schema.json を書く (中身が同じなら書かない)。書けなくても入力は止めない。</summary>
    public static void Write(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        try
        {
            var json = Create();
            if (File.Exists(path) && File.ReadAllText(path) == json) return;
            DataFile.Write(path, temp => File.WriteAllText(temp, json));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"{FileName} を書けませんでした: {ex.Message}");
        }
    }

    // 選択肢の説明 (列挙型のフィールドの Description) は reflection で読む。Mac・Linux 版 (NativeAOT) では Meltype.Core の型を削らない
    // (Meltype.Mac.Native.csproj の TrimmerRootAssembly) ので読める。削られても説明が空になるだけで、選択肢の名前は Enum.GetNames から入る。
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Meltype.Core は TrimmerRootAssembly。読めなくても説明が空になるだけ")]
    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode node, JsonObject defaults)
    {
        // 何でもよい値の schema は true で来るので、説明を足せるように {} にする
        var schema = node as JsonObject ?? new JsonObject();
        var type = context.TypeInfo.Type;
        var enumType = Nullable.GetUnderlyingType(type) ?? type;
        // 列挙型は名前で書く。独自の JsonConverter (InputModeJsonConverter など) を付けた項目は exporter が中身を知らないので、ここで選択肢を入れる。
        // 数値でも読めるが、前の版との互換のためなので schema には出さない。
        if (enumType.IsEnum)
        {
            var choices = Enum.GetNames(enumType);
            var names = new JsonArray(choices.Select(name => (JsonNode?)name).ToArray());
            var descriptions = new JsonArray(choices.Select(name => (JsonNode?)(enumType.GetField(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "")).ToArray());
            if (enumType != type)
            {
                names.Add((JsonNode?)null);
                descriptions.Add((JsonNode?)"");
            }
            schema.Remove("type");
            schema["enum"] = names;
            // VS Code の拡張 (JSON Schema の標準には無い)。選択肢ごとの説明を補完に出す
            schema["enumDescriptions"] = descriptions;
        }
        // 知らない項目は読み込みで無視されるので、書き間違いに気づけるように警告させる
        if (schema.ContainsKey("properties")) schema["additionalProperties"] = false;
        // 非公開の JsonInclude は古い設定を読むためだけの項目 (Settings.LegacyHankaku など)。保存しないので、補完にも出さない
        if (context.PropertyInfo is null && schema["properties"] is JsonObject properties)
        {
            foreach (var legacy in context.TypeInfo.Properties.Where(p => p.AttributeProvider?.IsDefined(typeof(JsonIncludeAttribute), false) == true))
                properties.Remove(legacy.Name);
        }
        if (context.PropertyInfo is { } property)
        {
            if (property.AttributeProvider?.GetCustomAttributes(typeof(DisplayNameAttribute), false).FirstOrDefault() is DisplayNameAttribute { DisplayName.Length: > 0 } title)
                schema["title"] = title.DisplayName;
            if (property.AttributeProvider?.GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault() is DescriptionAttribute { Description.Length: > 0 } description)
                schema["description"] = description.Description;
            if (property.DeclaringType == typeof(Settings) && defaults.TryGetPropertyValue(property.Name, out var value))
                schema["default"] = value?.DeepClone();
        }
        return schema;
    }
}
