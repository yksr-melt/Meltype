// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meltype.Tests;

/// <summary>
/// 配布用パッケージの .NET ランタイムを小さくするための道具 (Build-Package.ps1 から呼ぶ)。
/// app フォルダーの Meltype.dll から、実際に使う「型」をたどって、同梱したランタイム (app\dotnet\shared) のうち
/// 必要なアセンブリのファイル名を 1 行ずつ出す。
///
/// アセンブリの参照 (AssemblyRef) を単純にたどると、互換用のまとめ部品 (mscorlib, netstandard) がランタイムの
/// ほぼ全部を参照しているので何も削れない。そこで型の参照 (TypeRef) を単位にし、
///   ・その型を定義しているアセンブリは必要 (そのアセンブリが使う型もたどる)
///   ・その型を別のアセンブリへ転送しているだけのアセンブリ (型の転送) は、転送先まで含めて必要
/// とする。
/// </summary>
internal static class RuntimeClosure
{
    /// <summary>
    /// 型の参照をたどっても見つからないが、実行時に名前で読み込まれるもの。
    /// (UI Automation は .NET のライブラリではなく COM で直接使うので、UIAutomationClientSideProviders などは不要)
    /// </summary>
    private static readonly string[] AlwaysKeep = ["System.Private.CoreLib"];

    private sealed class Assembly(string path, PEReader pe)
    {
        public string Path { get; } = path;
        public PEReader PE { get; } = pe;
        public MetadataReader Reader { get; } = pe.GetMetadataReader();
        public bool Scanned { get; set; }
    }

    public static void Run(string appDirectory)
    {
        var assemblies = Directory.GetDirectories(Path.Combine(appDirectory, "dotnet", "shared"))
            .SelectMany(Directory.GetDirectories)
            .SelectMany(dir => Directory.GetFiles(dir, "*.dll"))
            .Select(Open)
            .Where(a => a is not null)
            .GroupBy(a => System.IO.Path.GetFileNameWithoutExtension(a!.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()!, StringComparer.OrdinalIgnoreCase);

        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Scan(Assembly assembly)
        {
            if (assembly.Scanned) return;
            assembly.Scanned = true;
            var reader = assembly.Reader;
            foreach (var handle in reader.TypeReferences)
            {
                if (Outermost(reader, handle) is not { } outer) continue;
                var (scope, ns, name) = outer;
                Request(reader.GetString(reader.GetAssemblyReference(scope).Name), ns, name);
            }
            // 属性のコンストラクターなどは TypeRef 経由なので上で拾える。アセンブリ全体の参照先も念のため読み込み可能にしておく必要はない。
        }

        void Request(string assemblyName, string ns, string name)
        {
            if (!assemblies.TryGetValue(assemblyName, out var assembly)) return;
            if (!seen.Add($"{assemblyName}|{ns}.{name}")) return;
            needed.Add(assemblyName);
            var reader = assembly.Reader;
            foreach (var handle in reader.ExportedTypes)
            {
                var exported = reader.GetExportedType(handle);
                if (!exported.IsForwarder || reader.GetString(exported.Name) != name || reader.GetString(exported.Namespace) != ns) continue;
                if (exported.Implementation.Kind != HandleKind.AssemblyReference) continue;
                // 型の転送: 転送先のアセンブリで同じ型を探す。
                var target = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
                Request(reader.GetString(target.Name), ns, name);
                return;
            }
            Scan(assembly);
        }

        var app = Open(Path.Combine(appDirectory, "Meltype.dll")) ?? throw new FileNotFoundException("Meltype.dll が見つかりません。");
        Scan(app);
        // OS に依存しない部分 (Meltype.Core.dll) が使う型もたどる。
        if (Open(Path.Combine(appDirectory, "Meltype.Core.dll")) is { } core) Scan(core);
        foreach (var name in AlwaysKeep)
        {
            if (assemblies.TryGetValue(name, out var assembly))
            {
                needed.Add(name);
                Scan(assembly);
            }
        }

        foreach (var name in needed.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) Console.WriteLine(name + ".dll");
    }

    /// <summary>入れ子の型は外側の型までたどって、(参照先アセンブリ, 名前空間, 名前) を返す。</summary>
    private static (AssemblyReferenceHandle Scope, string Namespace, string Name)? Outermost(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        while (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            type = reader.GetTypeReference((TypeReferenceHandle)type.ResolutionScope);
        }
        if (type.ResolutionScope.Kind != HandleKind.AssemblyReference) return null;
        return ((AssemblyReferenceHandle)type.ResolutionScope, reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    private static Assembly? Open(string path)
    {
        try
        {
            var pe = new PEReader(File.OpenRead(path));
            if (pe.HasMetadata && pe.GetMetadataReader().IsAssembly) return new Assembly(path, pe);
            pe.Dispose();
        }
        catch
        {
        }
        return null;
    }
}
