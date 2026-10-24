// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
}

android {
    namespace = "io.github.ninjin0802.keyboard.app"
    compileSdk = 36
    defaultConfig {
        applicationId = "io.github.ninjin0802.keyboard"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        ndk { abiFilters += listOf("arm64-v8a", "x86_64") }
    }
    buildTypes {
        release {
            // 使わないコードを落として小さくする。残すものは mozc/consumer-rules.pro に書いてある。
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
            // 手元で確かめるためだけに、デバッグ用の鍵で署名する。配るときは、配布用の鍵に替える。
            signingConfig = signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

// ライセンスの全文（リポジトリ直下の LICENSE）と android/THIRD-PARTY-NOTICES.md を、ビルドのたびにアセットへ写す（複製はコミットしない）。
abstract class CopyNotices : DefaultTask() {
    @get:InputFiles abstract val sources: ConfigurableFileCollection
    @get:OutputDirectory abstract val outputDir: DirectoryProperty

    @TaskAction fun copy() {
        val out = outputDir.get().asFile
        out.deleteRecursively(); out.mkdirs()
        sources.files.forEach { it.copyTo(out.resolve(it.name)) }
    }
}

androidComponents {
    onVariants { variant ->
        val task = tasks.register<CopyNotices>("copyNotices${variant.name.replaceFirstChar { it.uppercase() }}") {
            sources.from(rootProject.file("../LICENSE"), rootProject.file("THIRD-PARTY-NOTICES.md"))
        }
        variant.sources.assets?.addGeneratedSourceDirectory(task, CopyNotices::outputDir)
    }
}

kotlin {
    jvmToolchain(17)
}

dependencies {
    implementation(project(":engine"))
    implementation(project(":mozc"))

    testImplementation(libs.junit)
    testImplementation(libs.kotlin.test)
    // 端末の上のテストを動かすためだけのもの。アプリには入らない。
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.junit)
}
