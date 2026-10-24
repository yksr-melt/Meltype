// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
plugins {
    alias(libs.plugins.android.library)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.protobuf)
}

android {
    namespace = "io.github.ninjin0802.keyboard.mozc"
    compileSdk = 36
    defaultConfig {
        minSdk = 26
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        consumerProguardFiles("consumer-rules.pro")
        ndk { abiFilters += listOf("arm64-v8a", "x86_64") }
    }
    packaging {
        // tools/build-mozc.sh がデバッグ情報を落としてから置くので、ここでは触らない。
        jniLibs { keepDebugSymbols += "**/libmozc.so" }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    jvmToolchain(17)
}

// Mozc の命令定義は src/main/proto/ にある（tools/build-mozc.sh が Mozc の src/protocol/ から写したもの）。
// そこはプラグインが最初から見る場所なので、srcDir の指定は要らない（足すと二重になって失敗する）。
protobuf {
    protoc {
        artifact = libs.protobuf.protoc.get().toString()
    }
    generateProtoTasks {
        all().forEach { task ->
            task.builtins {
                create("java") { option("lite") }
            }
        }
    }
}

// Mozc の成果物は Git に入れていない。ないままビルドすると、動かない APK ができてしまうので止める。
// ライセンス表記（MOZC_NOTICES.txt）も確かめる。これがない APK は、Mozc に含まれるものの配布条件を満たさない。
val checkMozcArtifacts by tasks.registering {
    description = "Mozc のライブラリ、辞書、ライセンス表記が置かれていることを確かめる。"
    group = "verification"
    val artifacts = files(
        "src/main/jniLibs/arm64-v8a/libmozc.so",
        "src/main/jniLibs/x86_64/libmozc.so",
        "src/main/assets/mozc.data",
        "src/main/assets/MOZC_NOTICES.txt",
    )
    val projectDir = layout.projectDirectory.asFile
    doLast {
        val missing = artifacts.filter { !it.isFile }.map { it.relativeTo(projectDir).path }
        if (missing.isNotEmpty()) {
            throw GradleException(
                "Mozc の成果物がありません: ${missing.joinToString("、") { "mozc/$it" }}。" +
                    "先に bash tools/build-mozc.sh を実行してください。",
            )
        }
    }
}

tasks.named("preBuild") {
    dependsOn(checkMozcArtifacts)
}

dependencies {
    api(project(":engine"))
    api(libs.protobuf.javalite)
    // テストを動かすためだけのもの。アプリには入らない。
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.junit)
}
