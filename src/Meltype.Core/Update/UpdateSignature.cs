// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Security.Cryptography;
using System.Text;

namespace Meltype.Update;

/// <summary>
/// 自動更新で入れる zip の、作者の署名。作者が GitHub の外 (手元) に置いた秘密鍵で、版と zip の SHA-256 に署名し (tools/sign-update.mjs)、
/// zip と一緒にリリースに置く (&lt;zip の名前&gt;.sig)。Meltype は、埋め込んだ公開鍵で確かめてから展開する。
/// GitHub のアカウント・トークン・CI が乗っ取られても、秘密鍵が無ければ、確かめを通る更新は作れない。
/// 署名は ECDSA (P-256・SHA-256)。.sig は、署名 (r と s を並べた 64 バイト) の Base64 を 1 行。
/// </summary>
public static class UpdateSignature
{
    /// <summary>
    /// 作者の公開鍵 (SubjectPublicKeyInfo の Base64)。作者が `node tools/sign-update.mjs keygen` で作った鍵の公開鍵を入れる。
    /// 空のあいだは、どの更新も確かめを通らない (自動更新で入れない)。
    /// </summary>
    public const string PublicKey = "";

    /// <summary>署名する内容: 「Meltype update」・版・zip の SHA-256 (版を入れて、前の版の zip を新しい版として出し直せないようにする)。</summary>
    public static byte[] Message(string version, byte[] zipSha256) =>
        [.. Encoding.UTF8.GetBytes($"Meltype update\n{version}\n"), .. zipSha256];

    /// <summary>zip が、公開鍵 publicKey (SubjectPublicKeyInfo の Base64) の持ち主が版 version として署名したものか。</summary>
    public static bool Verify(string publicKey, string version, Stream zip, string signature)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(version)) return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (ecdsa.KeySize != 256) return false;
            var hash = SHA256.HashData(zip);
            var bytes = Convert.FromBase64String(signature.Trim());
            return ecdsa.VerifyData(Message(version, hash), bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>埋め込んだ公開鍵で確かめる。</summary>
    public static bool Verify(string version, Stream zip, string signature) => Verify(PublicKey, version, zip, signature);
}
