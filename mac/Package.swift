// swift-tools-version:5.10
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype の Mac 版 (Input Method Kit の IME)。ビルドとインストールは build.sh で行う。
// 漢字変換には azooKey の変換エンジン (AzooKeyKanaKanjiConverter, MIT License) を使う。
// 英語 / 日本語の判定などの本体は、C# の Meltype.Core を NativeAOT で libMeltypeNative.dylib にしたものを dlopen で読み込む。

import PackageDescription

let package = Package(
    name: "MeltypeIME",
    platforms: [.macOS(.v13)],
    dependencies: [
        // 間接依存のCollectionsもCIのSwift 6.1でビルドできる版に固定する。
        .package(url: "https://github.com/apple/swift-collections", exact: "1.1.4"),
        .package(url: "https://github.com/sparkle-project/Sparkle", exact: "2.10.0"),
        // main は API がよく変わるので、確かめたコミットに固定する (更新するときは Converter.swift も合わせて直す)。
        .package(url: "https://github.com/azooKey/AzooKeyKanaKanjiConverter", revision: "d59a28e4c7ca049aef04f29a91eae9677a7753f2"),
    ],
    targets: [
        .executableTarget(
            name: "MeltypeIME",
            dependencies: [
                .product(name: "Sparkle", package: "Sparkle"),
                .product(name: "KanaKanjiConverterModuleWithDefaultDictionary", package: "AzooKeyKanaKanjiConverter"),
            ],
            path: "Sources/MeltypeIME",
            linkerSettings: [
                .linkedFramework("InputMethodKit"),
                .linkedFramework("Carbon"),
            ]
        ),
        // 入力ソースの登録をやり直す小さな道具 (#134)。Meltype.app に入れて、install.sh / build.sh が
        // 入れ替えたあとに呼ぶ。azooKey には依存しない。
        .executableTarget(
            name: "RegisterInputSource",
            path: "Sources/RegisterInputSource",
            linkerSettings: [
                .linkedFramework("Carbon"),
            ]
        ),
        .testTarget(
            name: "MeltypeIMETests",
            dependencies: ["MeltypeIME"],
            path: "Tests/MeltypeIMETests"
        ),
    ],
    swiftLanguageVersions: [.v5]
)
