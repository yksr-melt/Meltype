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
        applicationId = "app.meltype.keyboard"
        minSdk = 26
        targetSdk = 36
        versionCode = System.getenv("MELTYPE_VERSION_CODE")?.toIntOrNull() ?: 1
        versionName = System.getenv("MELTYPE_VERSION_NAME") ?: "0.1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        ndk { abiFilters += listOf("arm64-v8a", "x86_64") }
    }
    // Bridge to the existing C# Meltype.Core compiled as Android NativeAOT.
    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
        }
    }
    // Buildable on an upstream checkout without any test-specific keystore.
    // CI may supply a repeatably signed QA key through environment variables.
    val qaKeystore = System.getenv("MELTYPE_TEST_KEYSTORE")?.takeIf { it.isNotBlank() }
    if (qaKeystore != null) {
        signingConfigs.create("qaDebug") {
            storeFile = file(qaKeystore)
            storePassword = System.getenv("MELTYPE_TEST_STORE_PASSWORD")
                ?: error("MELTYPE_TEST_STORE_PASSWORD is missing")
            keyAlias = System.getenv("MELTYPE_TEST_KEY_ALIAS")
                ?: error("MELTYPE_TEST_KEY_ALIAS is missing")
            keyPassword = System.getenv("MELTYPE_TEST_KEY_PASSWORD")
                ?: error("MELTYPE_TEST_KEY_PASSWORD is missing")
        }
    }
    // Distributable APKs are signed only with the long-lived private key.
    // Never fall back to a runner-generated debug key for the release variant.
    val distributionKeystore = System.getenv("MELTYPE_SIGNING_KEYSTORE")?.takeIf { it.isNotBlank() }
    if (distributionKeystore != null) {
        signingConfigs.create("meltypeDistribution") {
            storeFile = file(distributionKeystore)
            storePassword = System.getenv("MELTYPE_SIGNING_PASSWORD")
                ?: error("MELTYPE_SIGNING_PASSWORD is missing")
            keyAlias = "meltype"
            keyPassword = System.getenv("MELTYPE_SIGNING_PASSWORD")
                ?: error("MELTYPE_SIGNING_PASSWORD is missing")
        }
    }
    buildTypes {
        getByName("debug") {
            applicationIdSuffix = ".debug"
            if (qaKeystore != null) {
                signingConfig = signingConfigs.getByName("qaDebug")
            }
        }
        release {
            // 使わないコードを落として小さくする。残すものは mozc/consumer-rules.pro に書いてある。
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // The release variant is deliberately unsigned without the secret key.
            // Only the release workflow may publish Meltype.apk.
            signingConfig = distributionKeystore?.let { signingConfigs.getByName("meltypeDistribution") }
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
    // 端末の上のテストを動かすためだけのもの。アプリには入らない。
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.junit)
}
