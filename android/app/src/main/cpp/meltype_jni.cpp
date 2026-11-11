// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
// Android JNI adapter for the Meltype.Native NativeAOT C ABI (meltype_ime_*).
#include <jni.h>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <new>
#include <mutex>
#include <string>
#include <vector>

extern "C" {
int meltype_init(unsigned char* (*clauses)(unsigned char*, unsigned char*),
                 unsigned char* (*candidates)(unsigned char*),
                 int (*is_word)(unsigned char*));
void* meltype_ime_create();
unsigned char* meltype_ime_handle(void* host, unsigned char* json);
void meltype_ime_destroy(void* host);
void meltype_free(unsigned char* ptr);
}

namespace {
std::mutex init_mutex;
std::string data_dir;
JavaVM* vm = nullptr;
jobject callback_instance = nullptr;
jmethodID clauses_method = nullptr;
jmethodID candidates_method = nullptr;

struct CallbackEnvironment {
    JNIEnv* env = nullptr;
    bool attached = false;
    CallbackEnvironment() {
        if (!vm) return;
        if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_EDETACHED) {
            if (vm->AttachCurrentThread(&env, nullptr) == JNI_OK) attached = true;
        }
    }
    ~CallbackEnvironment() { if (attached && vm) vm->DetachCurrentThread(); }
};

jbyteArray new_utf8_array(JNIEnv* env, const unsigned char* text) {
    if (!text) return nullptr;
    const auto size = std::strlen(reinterpret_cast<const char*>(text));
    if (size > static_cast<size_t>(std::numeric_limits<jsize>::max())) return nullptr;
    auto bytes = env->NewByteArray(static_cast<jsize>(size));
    if (bytes && size > 0)
        env->SetByteArrayRegion(bytes, 0, static_cast<jsize>(size), reinterpret_cast<const jbyte*>(text));
    return bytes;
}

unsigned char* malloc_java_bytes(JNIEnv* env, jbyteArray result) {
    if (env->ExceptionCheck()) { env->ExceptionClear(); return nullptr; }
    if (!result) return nullptr;
    const jsize length = env->GetArrayLength(result);
    auto* buffer = reinterpret_cast<unsigned char*>(std::malloc(static_cast<size_t>(length) + 1u));
    if (!buffer) return nullptr;
    if (length) env->GetByteArrayRegion(result, 0, length, reinterpret_cast<jbyte*>(buffer));
    buffer[length] = 0;
    if (env->ExceptionCheck()) { env->ExceptionClear(); std::free(buffer); return nullptr; }
    return buffer;
}

unsigned char* convert_clauses(unsigned char* reading, unsigned char* context) {
    CallbackEnvironment jni;
    if (!jni.env || !callback_instance || !clauses_method) return nullptr;
    auto* env = jni.env;
    const auto r = new_utf8_array(env, reading);
    const auto c = new_utf8_array(env, context);
    if (env->ExceptionCheck() || !r) {
        env->ExceptionClear();
        if (r) env->DeleteLocalRef(r);
        if (c) env->DeleteLocalRef(c);
        return nullptr;
    }
    auto result = static_cast<jbyteArray>(env->CallObjectMethod(callback_instance, clauses_method, r, c));
    auto* converted = malloc_java_bytes(env, result);
    if (result) env->DeleteLocalRef(result);
    env->DeleteLocalRef(r);
    if (c) env->DeleteLocalRef(c);
    return converted;
}

unsigned char* convert_candidates(unsigned char* reading) {
    CallbackEnvironment jni;
    if (!jni.env || !callback_instance || !candidates_method) return nullptr;
    auto* env = jni.env;
    const auto r = new_utf8_array(env, reading);
    if (env->ExceptionCheck() || !r) {
        env->ExceptionClear(); if (r) env->DeleteLocalRef(r);
        return nullptr;
    }
    auto result = static_cast<jbyteArray>(env->CallObjectMethod(callback_instance, candidates_method, r));
    auto* converted = malloc_java_bytes(env, result);
    if (result) env->DeleteLocalRef(result);
    env->DeleteLocalRef(r);
    return converted;
}


// Java Modified UTF-8 is not ordinary UTF-8 for NUL and supplementary planes.
// Byte arrays preserve the exact UTF-8 bytes produced by Kotlin.
std::vector<unsigned char> read_utf8(JNIEnv* env, jbyteArray bytes) {
    if (bytes == nullptr) return {};
    const jsize size = env->GetArrayLength(bytes);
    std::vector<unsigned char> result(static_cast<size_t>(size) + 1u, 0);
    if (size > 0) env->GetByteArrayRegion(bytes, 0, size, reinterpret_cast<jbyte*>(result.data()));
    return result;
}

jbyteArray return_utf8(JNIEnv* env, unsigned char* text) {
    if (!text) return nullptr;
    const size_t size = std::strlen(reinterpret_cast<const char*>(text));
    if (size > static_cast<size_t>(std::numeric_limits<jsize>::max())) {
        meltype_free(text);
        return nullptr;
    }
    jbyteArray result = env->NewByteArray(static_cast<jsize>(size));
    if (result && size > 0) {
        env->SetByteArrayRegion(result, 0, static_cast<jsize>(size),
                                reinterpret_cast<const jbyte*>(text));
    }
    meltype_free(text);
    return result;
}

void* as_handle(jlong handle) { return reinterpret_cast<void*>(static_cast<uintptr_t>(handle)); }
} // namespace

// One process-wide data directory, set before first access to Meltype.Config.AppPaths.
// Never switch the directory after the Core has initialized its static config.
extern "C" JNIEXPORT jboolean JNICALL
Java_io_github_ninjin0802_keyboard_app_core_NativeApi_init(
        JNIEnv* env, jobject, jbyteArray directory, jobject callbacks) {
    if (!directory || !callbacks) return JNI_FALSE;
    const auto path = read_utf8(env, directory);
    if (env->ExceptionCheck() || path.size() <= 1) return JNI_FALSE;
    const std::string requested(reinterpret_cast<const char*>(path.data()), path.size() - 1);
    if (requested.empty() || requested.find('\0') != std::string::npos) return JNI_FALSE;
    std::lock_guard<std::mutex> guard(init_mutex);
    if (!data_dir.empty()) return data_dir == requested ? JNI_TRUE : JNI_FALSE;
    if (setenv("MELTYPE_DATA_DIR", requested.c_str(), 1) != 0) return JNI_FALSE;
    if (env->GetJavaVM(&vm) != JNI_OK) return JNI_FALSE;
    auto clazz = env->GetObjectClass(callbacks);
    if (!clazz) return JNI_FALSE;
    clauses_method = env->GetMethodID(clazz, "clauses", "([B[B)[B");
    if (env->ExceptionCheck() || !clauses_method) {
        env->ExceptionClear();
        env->DeleteLocalRef(clazz);
        return JNI_FALSE;
    }
    candidates_method = env->GetMethodID(clazz, "candidates", "([B)[B");
    if (env->ExceptionCheck() || !candidates_method) {
        env->ExceptionClear();
        env->DeleteLocalRef(clazz);
        return JNI_FALSE;
    }
    env->DeleteLocalRef(clazz);
    callback_instance = env->NewGlobalRef(callbacks);
    if (!callback_instance) return JNI_FALSE;
    if (meltype_init(convert_clauses, convert_candidates, nullptr) != 1) {
        env->DeleteGlobalRef(callback_instance);
        callback_instance = nullptr;
        return JNI_FALSE;
    }
    data_dir = requested;
    return JNI_TRUE;
}

extern "C" JNIEXPORT jlong JNICALL
Java_io_github_ninjin0802_keyboard_app_core_NativeApi_create(
        JNIEnv*, jobject) {
    std::lock_guard<std::mutex> guard(init_mutex);
    if (data_dir.empty()) return 0;
    return static_cast<jlong>(reinterpret_cast<uintptr_t>(meltype_ime_create()));
}

extern "C" JNIEXPORT void JNICALL
Java_io_github_ninjin0802_keyboard_app_core_NativeApi_destroy(
        JNIEnv*, jobject, jlong host) {
    if (host) meltype_ime_destroy(as_handle(host));
}

// One event (ImeEvent JSON) in, one response (ImeOutput JSON) out, both UTF-8.
extern "C" JNIEXPORT jbyteArray JNICALL
Java_io_github_ninjin0802_keyboard_app_core_NativeApi_handle(
        JNIEnv* env, jobject, jlong host, jbyteArray event) {
    if (!host || !event) return nullptr;
    auto json = read_utf8(env, event);
    if (env->ExceptionCheck()) return nullptr;
    return return_utf8(env, meltype_ime_handle(as_handle(host), json.data()));
}
