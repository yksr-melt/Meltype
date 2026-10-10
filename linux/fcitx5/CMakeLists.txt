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
#   cmake -S linux/fcitx5 -B build -DBUILD_TESTING=ON && cmake --build build && MELTYPE_DIR=<libMeltypeNative.so の場所> ctest --test-dir build
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
