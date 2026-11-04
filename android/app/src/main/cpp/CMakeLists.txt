# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 KoheiTakada
cmake_minimum_required(VERSION 3.22.1)
project(meltype_android_bridge LANGUAGES C CXX)

# Generated outside the source tree by android/tools/build-meltype-native.sh.
# AGP packages IMPORTED shared libraries linked by a native target.
set(MELTYPE_AOT_LIBRARY "${CMAKE_CURRENT_SOURCE_DIR}/../../../prebuilt/${ANDROID_ABI}/libMeltypeNative.so")
if(NOT EXISTS "${MELTYPE_AOT_LIBRARY}")
    message(FATAL_ERROR "Meltype NativeAOT library not found: ${MELTYPE_AOT_LIBRARY}. Run tools/build-meltype-native.sh first")
endif()
add_library(MeltypeNative SHARED IMPORTED)
set_target_properties(MeltypeNative PROPERTIES IMPORTED_LOCATION "${MELTYPE_AOT_LIBRARY}")

add_library(meltype_jni SHARED meltype_jni.cpp)
target_compile_features(meltype_jni PRIVATE cxx_std_17)
# Android devices with 16 KiB memory pages require every PT_LOAD segment
# of the JNI bridge to be 16 KiB aligned. Imported NativeAOT and Mozc libs
# already use 0x4000 alignment; the NDK-created bridge previously used 0x1000.
target_link_options(meltype_jni PRIVATE
    "LINKER:-z,max-page-size=16384"
    "LINKER:-z,common-page-size=16384")
target_link_libraries(meltype_jni PRIVATE MeltypeNative)
