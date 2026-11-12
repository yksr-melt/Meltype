// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Security.Cryptography;
using Meltype.Update;

namespace Meltype.Tests;

/// <summary>自動更新の zip の署名 (作者の手元の鍵で署名し、埋め込んだ公開鍵で確かめる)。</summary>
internal static class UpdateSignatureTests
{
    private static string Sign(ECDsa key, string version, byte[] zip) =>
        Convert.ToBase64String(key.SignData(UpdateSignature.Message(version, SHA256.HashData(zip)), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    private static bool Verify(string publicKey, string version, byte[] zip, string signature) =>
        UpdateSignature.Verify(publicKey, version, new MemoryStream(zip), signature);

    [Test]
    public static void Verify_AcceptsOnlyTheAuthorsSignatureForThatVersionAndZip()
    {
        using var author = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(author.ExportSubjectPublicKeyInfo());
        byte[] zip = [1, 2, 3, 4, 5];
        var signature = Sign(author, "1.5.1", zip);

        Assert.True(Verify(publicKey, "1.5.1", zip, signature), "作者が署名した zip");
        Assert.True(Verify(publicKey, "1.5.1", zip, signature + "\n"), "末尾の改行は無視する");
        Assert.True(!Verify(publicKey, "1.5.1", [1, 2, 3, 4, 6], signature), "中身を変えた zip");
        Assert.True(!Verify(publicKey, "1.5.2", zip, signature), "ほかの版として出し直した zip");
        Assert.True(!Verify(publicKey, "1.5.1", zip, Sign(attacker, "1.5.1", zip)), "ほかの鍵の署名 (GitHub だけが乗っ取られた)");
        Assert.True(!Verify(publicKey, "1.5.1", zip, ""), "署名が無い");
        Assert.True(!Verify(publicKey, "1.5.1", zip, "これは署名ではない"), "署名として読めない");
        Assert.True(!Verify("", "1.5.1", zip, signature), "公開鍵が無ければ通さない");
        Assert.True(!Verify("AAAA", "1.5.1", zip, signature), "公開鍵として読めない");
    }

    [Test]
    public static void Verify_RejectsKeysThatAreNotP256()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        byte[] zip = [9, 9, 9];
        var signature = Convert.ToBase64String(p384.SignData(UpdateSignature.Message("1.5.1", SHA256.HashData(zip)), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.True(!Verify(Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()), "1.5.1", zip, signature), "P-256 の鍵だけを使う");
    }
}
