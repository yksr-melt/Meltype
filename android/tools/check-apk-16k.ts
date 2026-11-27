// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// APK に入れた ARM64 の JNI のライブラリが、16 KiB のページで動くかを確かめる。
//   node android/tools/check-apk-16k.ts <APK>
// ELF の PT_LOAD の揃えと、ZIP の中の置き場所の揃えは、Android の 16 KiB 対応の別々の条件なので、両方を見る。

import fs from 'node:fs';
import zlib from 'node:zlib';

const PAGE = 16384;
const PT_LOAD = 1;

function verifyElf(data: Buffer, name: string): void {
  if (!data.subarray(0, 6).equals(Buffer.from([0x7f, 0x45, 0x4c, 0x46, 2, 1])))
    throw new Error(`${name}: expected little-endian ELF64`);
  const headerOffset = Number(data.readBigUInt64LE(32));
  const headerSize = data.readUInt16LE(54);
  const headerCount = data.readUInt16LE(56);
  if (headerSize < 56 || headerCount < 1) throw new Error(`${name}: malformed ELF program headers`);
  let loads = 0;
  for (let i = 0; i < headerCount; i++) {
    const at = headerOffset + i * headerSize;
    if (at + 56 > data.length) throw new Error(`${name}: truncated ELF program headers`);
    if (data.readUInt32LE(at) !== PT_LOAD) continue;
    loads++;
    const align = data.readBigUInt64LE(at + 48);
    if (align < BigInt(PAGE) || align % BigInt(PAGE) !== 0n)
      throw new Error(`${name}: PT_LOAD[${i}] p_align=0x${align.toString(16)}, need 0x4000`);
  }
  if (loads === 0) throw new Error(`${name}: no PT_LOAD segments`);
}

type Entry = { name: string; method: number; compressedSize: number; dataOffset: number };

/** ZIP の中央ディレクトリから、中身の置き場所を読む。APK は 4 GiB より小さいので ZIP64 は扱わない。 */
function entries(zip: Buffer): Entry[] {
  // 中央ディレクトリの終わり (EOCD) は、末尾の最大 64 KiB のコメントの前にある
  const eocd = zip.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]), zip.length - 22);
  if (eocd < 0 || eocd < zip.length - 22 - 0xffff) throw new Error('not a ZIP file');
  const count = zip.readUInt16LE(eocd + 10);
  let at = zip.readUInt32LE(eocd + 16);
  if (count === 0xffff || at === 0xffffffff) throw new Error('ZIP64 is not supported');
  const result: Entry[] = [];
  for (let i = 0; i < count; i++) {
    if (zip.readUInt32LE(at) !== 0x02014b50) throw new Error('malformed ZIP central directory');
    const method = zip.readUInt16LE(at + 10);
    const compressedSize = zip.readUInt32LE(at + 20);
    const nameLength = zip.readUInt16LE(at + 28);
    const extraLength = zip.readUInt16LE(at + 30);
    const commentLength = zip.readUInt16LE(at + 32);
    const local = zip.readUInt32LE(at + 42);
    const name = zip.toString('utf8', at + 46, at + 46 + nameLength);
    if (zip.readUInt32LE(local) !== 0x04034b50) throw new Error(`${name}: malformed ZIP local header`);
    // ローカルヘッダーの名前と extra の長さは、中央ディレクトリのものと違うことがある (zipalign は extra で揃える)
    const dataOffset = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
    result.push({ name, method, compressedSize, dataOffset });
    at += 46 + nameLength + extraLength + commentLength;
  }
  return result;
}

function verifyApk(file: string): void {
  const zip = fs.readFileSync(file);
  const checked = new Set<string>();
  for (const entry of entries(zip)) {
    if (!(entry.name.startsWith('lib/arm64-v8a/') && entry.name.endsWith('.so'))) continue;
    checked.add(entry.name);
    const raw = zip.subarray(entry.dataOffset, entry.dataOffset + entry.compressedSize);
    if (entry.method === 0) verifyElf(raw, entry.name);
    else if (entry.method === 8) verifyElf(zlib.inflateRawSync(raw), entry.name);
    else throw new Error(`${entry.name}: unsupported ZIP compression method ${entry.method}`);
    // 圧縮したライブラリは Android が取り出して使うので、ZIP の中の揃えは関係ない
    if (entry.method === 0 && entry.dataOffset % PAGE !== 0)
      throw new Error(`${entry.name}: ZIP offset=${entry.dataOffset} is not 16 KiB aligned`);
  }
  if (checked.size === 0) throw new Error(`${file}: missing arm64-v8a native libraries`);
  console.log(`PASS: ${checked.size} ARM64 native libraries, 16 KiB ELF/ZIP alignment`);
}

const [apk, ...rest] = process.argv.slice(2);
if (!apk || rest.length > 0) {
  console.error('usage: node android/tools/check-apk-16k.ts <APK>');
  process.exit(2);
}
try {
  verifyApk(apk);
} catch (error) {
  console.error(`FAIL: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
}
