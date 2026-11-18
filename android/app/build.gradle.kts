plugins { id("com.android.application") }
abstract class GenerateRustBridge : Exec() {
    @get:org.gradle.api.tasks.OutputDirectory
    abstract val generatedSources: org.gradle.api.file.DirectoryProperty
}
val bridgeExecutable = layout.buildDirectory.file("rustBridge/generate-bridge" + if (System.getProperty("os.name").startsWith("Windows")) ".exe" else "")
val compileRustBridge by tasks.registering(Exec::class) {
    inputs.files(rootProject.file("bridge.rs"), rootProject.file("bridge_events.rs"))
    outputs.file(bridgeExecutable)
    doFirst { bridgeExecutable.get().asFile.parentFile.mkdirs() }
    commandLine("rustc", "--edition=2021", rootProject.file("bridge.rs"), "-o", bridgeExecutable.get().asFile)
}
android {
    namespace = "moe.meltype.rust"
    compileSdk = 35
    defaultConfig {
        applicationId = "moe.meltype.rust"
        minSdk = 26
        targetSdk = 35
        versionCode = 28
        versionName = "1.1.24"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        ndk { abiFilters += listOf("arm64-v8a", "x86_64") }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}
androidComponents.onVariants(androidComponents.selector().all()) { variant ->
    val generateRustBridge = tasks.register<GenerateRustBridge>("generate${variant.name.replaceFirstChar { it.uppercaseChar() }}RustBridge") {
        dependsOn(compileRustBridge)
        inputs.file(bridgeExecutable)
        doFirst { commandLine(bridgeExecutable.get().asFile, generatedSources.get().asFile) }
    }
    variant.sources.java?.addGeneratedSourceDirectory(generateRustBridge) { it.generatedSources }
}
dependencies {
    // 利用状況の集計 (Aptabase)。androidx.test:monitor は SDK の宣言だけで使っていないので外す。
    implementation("com.github.aptabase:aptabase-kotlin:0.1.0") { exclude(group = "androidx.test") }
    androidTestImplementation("androidx.test:runner:1.7.0")
    androidTestImplementation("androidx.test.ext:junit:1.3.0")
}
val verifyNative by tasks.registering {
    doLast {
        for (abi in listOf("arm64-v8a", "x86_64")) {
            check(file("src/main/jniLibs/$abi/libmeltype_native.so").isFile) {
                "Missing Rust library for $abi. Run cargo ndk as described in android/README.md."
            }
        }
    }
}
tasks.named("preBuild") { dependsOn(verifyNative) }
