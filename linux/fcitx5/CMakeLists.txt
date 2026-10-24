# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype の fcitx5 のアドオン。linux/build.sh が fcitx5 の開発用のパッケージ (fcitx5-modules-dev) があるときにビルドする。
#   cmake -S linux/fcitx5 -B build/fcitx5 -DMELTYPE_DIR=/opt/meltype && cmake --build build/fcitx5
cmake_minimum_required(VERSION 3.16)
project(fcitx5-meltype LANGUAGES CXX)

find_package(Fcitx5Core 5.0 REQUIRED)
find_package(Fcitx5Utils 5.0 REQUIRED)

# 本体 (libMeltypeNative.so) と Mozc のヘルパーを入れた場所 (install.sh と同じ)
set(MELTYPE_DIR "/opt/meltype" CACHE PATH "libMeltypeNative.so を入れた場所")

add_library(meltype MODULE meltype.cpp)
set_target_properties(meltype PROPERTIES PREFIX "" CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON)
# fcitx5 5.1.16 以降の fcitx-utils/log.h は std::source_location を使うため、C++20 が要る
if(Fcitx5Core_VERSION VERSION_GREATER_EQUAL 5.1.16)
    set_target_properties(meltype PROPERTIES CXX_STANDARD 20)
endif()
target_compile_definitions(meltype PRIVATE MELTYPE_DIR="${MELTYPE_DIR}")
target_link_libraries(meltype PRIVATE Fcitx5::Core Fcitx5::Utils ${CMAKE_DL_LIBS})

# テスト (fcitx5 のテスト用のフロントエンドでキーを送り、確定した文字を確かめる)。
# Native は Mozc ヘルパーを置かないテスト専用ディレクトリに libMeltypeNative.so として用意する。
#   cmake -S linux/fcitx5 -B build -DBUILD_TESTING=ON -DMELTYPE_DIR=/absolute/path/to/current-native
#   cmake --build build && cmake -E chdir build ctest --output-on-failure
# contextHint 未対応の旧 Native は別のディレクトリに用意し、明示指定した場合だけ互換テストを追加する。
#   cmake -S linux/fcitx5 -B build -DMELTYPE_LEGACY_NATIVE_DIR=/absolute/path/to/legacy-native
#   cmake -E chdir build ctest -R '^testpopup-legacy$' --output-on-failure
# 通常のテストの Native は変更しない。追加したテストを外す場合は、同じビルドに -DMELTYPE_LEGACY_NATIVE_DIR= を指定する。
option(BUILD_TESTING "テストをビルドする" OFF)
if(BUILD_TESTING)
    enable_testing()
    find_package(Fcitx5ModuleTestFrontend REQUIRED)
    # fcitx5 のデータの場所 (build/test/addon、build/test/inputmethod) に設定ファイルを置く
    configure_file(meltype-addon.conf ${CMAKE_CURRENT_BINARY_DIR}/test/addon/meltype.conf COPYONLY)
    configure_file(test/testfrontend.conf ${CMAKE_CURRENT_BINARY_DIR}/test/addon/testfrontend.conf COPYONLY)
    configure_file(test/testui.conf ${CMAKE_CURRENT_BINARY_DIR}/test/addon/testui.conf COPYONLY)
    configure_file(test/testim.conf ${CMAKE_CURRENT_BINARY_DIR}/test/addon/testim.conf COPYONLY)
    file(READ meltype.conf MELTYPE_IM_CONF)
    string(REPLACE "@DIR@" "${MELTYPE_DIR}" MELTYPE_IM_CONF "${MELTYPE_IM_CONF}")
    file(WRITE ${CMAKE_CURRENT_BINARY_DIR}/test/inputmethod/meltype.conf "${MELTYPE_IM_CONF}")
    file(COPY /usr/share/fcitx5/addon/keyboard.conf DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/test/addon)
    get_target_property(FCITX5_CORE_LOCATION Fcitx5::Core LOCATION)
    get_filename_component(FCITX5_LIB_DIR ${FCITX5_CORE_LOCATION} DIRECTORY)
    add_executable(testpopup test/testpopup.cpp)
    set_target_properties(testpopup PROPERTIES CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON)
    if(Fcitx5Core_VERSION VERSION_GREATER_EQUAL 5.1.16)
        set_target_properties(testpopup PROPERTIES CXX_STANDARD 20)
    endif()
    target_compile_definitions(testpopup PRIVATE
        FCITX5_MELTYPE_BINARY_DIR="${CMAKE_CURRENT_BINARY_DIR}"
        FCITX5_SYSTEM_ADDON_DIR="${FCITX5_LIB_DIR}/fcitx5")
    target_link_libraries(testpopup PRIVATE Fcitx5::Core Fcitx5::Utils Fcitx5::Module::TestFrontend)
    add_dependencies(testpopup meltype)
    add_test(NAME testpopup-manual COMMAND testpopup manual)
    add_test(NAME testpopup-spelling COMMAND testpopup spelling)

    set(MELTYPE_LEGACY_NATIVE_DIR "" CACHE PATH "旧 Native の互換テスト用ディレクトリ（未指定なら登録しない）")
    if(NOT "${MELTYPE_LEGACY_NATIVE_DIR}" STREQUAL "")
        get_filename_component(MELTYPE_LEGACY_NATIVE_PATH "${MELTYPE_LEGACY_NATIVE_DIR}" ABSOLUTE)
        if(NOT EXISTS "${MELTYPE_LEGACY_NATIVE_PATH}/libMeltypeNative.so")
            message(FATAL_ERROR "MELTYPE_LEGACY_NATIVE_DIR に libMeltypeNative.so がありません: ${MELTYPE_LEGACY_NATIVE_PATH}")
        endif()
        add_test(NAME testpopup-legacy COMMAND testpopup legacy)
        set_tests_properties(testpopup-legacy PROPERTIES
            ENVIRONMENT "MELTYPE_DIR=${MELTYPE_LEGACY_NATIVE_PATH}")
    endif()

    add_executable(testmeltype test/testmeltype.cpp)
    set_target_properties(testmeltype PROPERTIES CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON)
    # fcitx5 5.1.16 以降の fcitx-utils/log.h は std::source_location を使うため、C++20 が要る
    if(Fcitx5Core_VERSION VERSION_GREATER_EQUAL 5.1.16)
        set_target_properties(testmeltype PROPERTIES CXX_STANDARD 20)
    endif()
    target_compile_definitions(testmeltype PRIVATE
        FCITX5_MELTYPE_BINARY_DIR="${CMAKE_CURRENT_BINARY_DIR}"
        FCITX5_SYSTEM_ADDON_DIR="${FCITX5_LIB_DIR}/fcitx5")
    target_link_libraries(testmeltype PRIVATE Fcitx5::Core Fcitx5::Utils Fcitx5::Module::TestFrontend)
    add_dependencies(testmeltype meltype)
    add_test(NAME testmeltype COMMAND testmeltype)
endif()
