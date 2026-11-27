// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
}

android {
    namespace = "app.meltype.keyboard"
    compileSdk = 36
    defaultConfig {
        applicationId = "app.meltype.keyboard"
        minSdk = 26
        targetSdk = 36
        versionCode = System.getenv("MELTYPE_VERSION_CODE")?.toIntOrNull() ?: 1
        versionName = System.getenv("MELTYPE_VERSION_NAME") ?: "0.1.0"
        ndk { abiFilters += listOf("arm64-v8a", "x86_64") }
    }
    // Bridge to the existing C# Meltype.Core compiled as Android NativeAOT.
    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
        }
    }
    buildTypes {
        getByName("debug") {
            applicationIdSuffix = ".debug"
        }
        release {
            // 使わないコードを落として小さくする。残すものは mozc/consumer-rules.pro に書いてある。
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // 配布用の署名は、ビルド後に android-release.yml の apksigner で行う。
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
    implementation(project(":mozc"))

    testImplementation(libs.junit)
    testImplementation(libs.kotlin.test)
}
