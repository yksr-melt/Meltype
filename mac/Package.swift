// swift-tools-version:6.1
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype の Mac 版 (Input Method Kit の IME)。ビルドとインストールは build.sh で行う。
// 英語 / 日本語の判定・漢字変換 (Mozc の変換ヘルパーを起動する) などの本体は、C# の Meltype.Core を NativeAOT で
// libMeltypeNative.dylib にしたものを dlopen で読み込む。

import PackageDescription

let package = Package(
    name: "MeltypeIME",
    platforms: [.macOS("13.3")],
    targets: [
        .executableTarget(
            name: "MeltypeIME",
            path: "Sources/MeltypeIME",
            linkerSettings: [
                .linkedFramework("InputMethodKit"),
                .linkedFramework("Carbon"),
            ]
        ),
        // 入力ソースの登録をやり直す小さな道具 (#134)。Meltype.app に入れて、install.sh / build.sh が
        // 入れ替えたあとに呼ぶ。
        .executableTarget(
            name: "RegisterInputSource",
            path: "Sources/RegisterInputSource",
            linkerSettings: [
                .linkedFramework("Carbon"),
            ]
        ),
        .executableTarget(
            name: "InstallSupport",
            path: "Sources/InstallSupport"
        ),
        .testTarget(
            name: "MeltypeIMETests",
            dependencies: ["MeltypeIME"],
            path: "Tests/MeltypeIMETests"
        ),
    ],
    swiftLanguageModes: [.v5]
)
