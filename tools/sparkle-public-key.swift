// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 joyamazaki
// Derive only the public key from a Sparkle 2.x seed file; never accesses Keychain.
import CryptoKit
import Foundation
let encoded = try String(contentsOfFile: CommandLine.arguments[1], encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines)
guard let seed = Data(base64Encoded: encoded), seed.count == 32 else {
    fputs("Expected a base64-encoded 32-byte Sparkle signing seed.\n", stderr)
    exit(1)
}
let key = try Curve25519.Signing.PrivateKey(rawRepresentation: seed)
print(key.publicKey.rawRepresentation.base64EncodedString())
