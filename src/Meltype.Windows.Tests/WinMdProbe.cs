// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meltype.Tests;

/// <summary>調査用: WinMD から型の GUID とメソッドの並び (vtable の順) を読む。</summary>
internal static class WinMdProbe
{
    public static void Run(string path, IEnumerable<string> typeNames)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var wanted = typeNames.ToHashSet();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
            if (!wanted.Contains(name)) continue;
            string guid = "(なし)";
            foreach (var attributeHandle in type.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var ctor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                var parent = reader.GetTypeReference((TypeReferenceHandle)ctor.Parent);
                if (reader.GetString(parent.Name) != "GuidAttribute") continue;
                var blob = reader.GetBlobReader(attribute.Value);
                blob.ReadUInt16();
                var a = blob.ReadUInt32(); var b = blob.ReadUInt16(); var c = blob.ReadUInt16();
                var rest = new byte[8];
                for (var i = 0; i < 8; i++) rest[i] = blob.ReadByte();
                guid = new Guid((int)a, (short)b, (short)c, rest).ToString();
            }
            var methods = type.GetMethods().Select(m => reader.GetString(reader.GetMethodDefinition(m).Name));
            Console.WriteLine($"{name} {guid}\n  {string.Join(", ", methods)}");
        }
    }
}
