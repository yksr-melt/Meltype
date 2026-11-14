// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// 自動更新の zip に、作者の署名を付ける (src/Meltype.Core/Update/UpdateSignature.cs)。秘密鍵は GitHub・CI に置かず、作者の手元だけに置く。
//
//   鍵を作る (最初の 1 回だけ。出てきた公開鍵を UpdateSignature.PublicKey に入れる):
//     node tools/sign-update.mjs keygen <秘密鍵を書き出す場所 (リポジトリの外)>
//   リリースの zip に署名する (<zip>.sig ができる。zip と一緒にリリースに置く):
//     node tools/sign-update.mjs sign <秘密鍵> <版 (1.5.1 など。タグの v は付けない)> <zip>
//   署名を確かめる:
//     node tools/sign-update.mjs verify <公開鍵 (Base64)> <版> <zip>
import { createHash, createPrivateKey, createPublicKey, generateKeyPairSync, sign, verify } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";

const message = (version, zip) =>
  Buffer.concat([Buffer.from(`Meltype update\n${version}\n`, "utf8"), createHash("sha256").update(readFileSync(zip)).digest()]);

const [command, ...args] = process.argv.slice(2);
switch (command) {
  case "keygen": {
    const [path] = args;
    if (!path) throw new Error("秘密鍵を書き出す場所を指定してください");
    if (existsSync(path)) throw new Error(`${path} はもうあります (上書きしません)`);
    const { privateKey, publicKey } = generateKeyPairSync("ec", { namedCurve: "P-256" });
    writeFileSync(path, privateKey.export({ type: "pkcs8", format: "pem" }), { mode: 0o600 });
    console.log(`秘密鍵: ${path} (リポジトリ・GitHub・CI に置かないでください。無くすと、次の版から自動更新できなくなります)`);
    console.log(`公開鍵 (UpdateSignature.PublicKey に入れる): ${publicKey.export({ type: "spki", format: "der" }).toString("base64")}`);
    break;
  }
  case "sign": {
    const [keyPath, version, zip] = args;
    if (!keyPath || !version || !zip) throw new Error("sign <秘密鍵> <版> <zip>");
    if (version.startsWith("v")) throw new Error("版にはタグの v を付けないでください (1.5.1 など)");
    const key = createPrivateKey(readFileSync(keyPath));
    const signature = sign("sha256", message(version, zip), { key, dsaEncoding: "ieee-p1363" });
    writeFileSync(`${zip}.sig`, signature.toString("base64") + "\n");
    console.log(`${zip}.sig を作りました (版 ${version})`);
    break;
  }
  case "verify": {
    const [publicKey, version, zip] = args;
    if (!publicKey || !version || !zip) throw new Error("verify <公開鍵> <版> <zip>");
    const key = createPublicKey({ key: Buffer.from(publicKey, "base64"), format: "der", type: "spki" });
    const ok = verify("sha256", message(version, zip), { key, dsaEncoding: "ieee-p1363" }, Buffer.from(readFileSync(`${zip}.sig`, "utf8").trim(), "base64"));
    console.log(ok ? "署名は正しいです" : "署名が合いません");
    process.exit(ok ? 0 : 1);
  }
  default:
    console.error("node tools/sign-update.mjs keygen|sign|verify …");
    process.exit(2);
}
