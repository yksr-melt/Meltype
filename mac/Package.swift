// swift-tools-version:6.1
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
        // main は API がよく変わるので、確かめたコミットに固定する (更新するときは Converter.swift も合わせて直す)。
        // Zenzai (ニューラルかな漢字変換、llama.cpp + zenz モデル) を CPU で使う (Metal の版は macOS 27 でシェーダーの読み込みに失敗して落ちる)。モデル (gguf) が無ければこれまでどおりの変換になる。
        .package(url: "https://github.com/azooKey/AzooKeyKanaKanjiConverter", revision: "d59a28e4c7ca049aef04f29a91eae9677a7753f2", traits: ["ZenzaiCPU"]),
    ],
    targets: [
        .executableTarget(
            name: "MeltypeIME",
            dependencies: [
                .product(name: "KanaKanjiConverterModuleWithDefaultDictionary", package: "AzooKeyKanaKanjiConverter"),
            ],
            path: "Sources/MeltypeIME",
            // Zenzai を使う azooKey は C++ のライブラリ (llama.cpp・marisa-trie) を読むので、C++ との相互運用を有効にする。
            swiftSettings: [.interoperabilityMode(.Cxx)],
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
        .executableTarget(
            name: "InstallSupport",
            path: "Sources/InstallSupport"
        ),
        .testTarget(
            name: "MeltypeIMETests",
            dependencies: ["MeltypeIME"],
            path: "Tests/MeltypeIMETests",
            swiftSettings: [.interoperabilityMode(.Cxx)]
        ),
    ],
    swiftLanguageModes: [.v5]
)
