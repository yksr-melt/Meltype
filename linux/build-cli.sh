#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hrmcngs
#
# WSL / Linux の CLI から Meltype をビルドするための入口。
#   linux/build-cli.sh --setup --test --package
#   linux/build-cli.sh --install
# From any directory in Ubuntu/WSL:
#   bash "$HOME/Meltype/linux/build-cli.sh" --setup --all
# Subsequent builds:
#   bash "$HOME/Meltype/linux/build-cli.sh" --build
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
export PATH="$HOME/.dotnet:$HOME/.local/bin:$PATH"

setup=0
test_core=0
build_mozc=0
package=0
install=0
all=1
mozc_source="${HOME}/mozc"
bazel_cache=""

usage() {
    cat <<'EOF'
Usage: linux/build-cli.sh [options]

Options:
  --setup                 Install Ubuntu/WSL build dependencies with apt.
  --test                  Run Meltype.Core.Tests.
  --mozc                  Build the Linux Mozc helper.
  --build                 Build the Mozc helper and Linux package.
  --package               Build linux/build/Meltype-linux.
  --install               Install the built IBus engine into Linux.
  --all                   Run --test --mozc --package. This is the default.
  --mozc-source DIR       Mozc checkout directory. Default: ~/mozc.
  --bazel-cache DIR       Optional Bazel disk cache directory.
  -h, --help              Show this help.

Examples:
  linux/build-cli.sh --setup --all
  linux/build-cli.sh --package
  linux/build-cli.sh --install
EOF
}

need() {
    command -v "$1" >/dev/null 2>&1 || {
        echo "Missing command: $1" >&2
        echo "Run linux/build-cli.sh --setup, or install the dependency manually." >&2
        exit 1
    }
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --setup) setup=1; all=0 ;;
        --test) test_core=1; all=0 ;;
        --mozc) build_mozc=1; all=0 ;;
        --build) build_mozc=1; package=1; all=0 ;;
        --package) package=1; all=0 ;;
        --install) install=1; all=0 ;;
        --all) all=1 ;;
        --mozc-source)
            [[ $# -ge 2 ]] || { echo "--mozc-source needs a directory" >&2; exit 1; }
            mozc_source="$2"
            shift
            ;;
        --bazel-cache)
            [[ $# -ge 2 ]] || { echo "--bazel-cache needs a directory" >&2; exit 1; }
            bazel_cache="$2"
            shift
            ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
    esac
    shift
done

if [[ $all -eq 1 ]]; then
    test_core=1
    build_mozc=1
    package=1
fi

if [[ "$(uname -s)" != Linux ]]; then
    echo "Run this script inside Linux/WSL. For macOS use mac/build-cli.sh." >&2
    exit 1
fi

if [[ $setup -eq 1 ]]; then
    if ! command -v apt-get >/dev/null 2>&1; then
        echo "--setup supports apt-based distros such as Ubuntu on WSL." >&2
        exit 1
    fi
    sudo apt-get update
    sudo apt-get install -y \
        build-essential clang lld curl git ibus libibus-1.0-dev pkg-config python3 python3-gi \
        gir1.2-ibus-1.0 zlib1g-dev libdbus-1-dev libglib2.0-dev libgtk-3-dev \
        libxcb-xfixes0-dev qt6-base-dev
    if ! command -v dotnet >/dev/null 2>&1; then
        # /tmp の固定名は別ユーザーに差し替えられるので、自分専用の一時ディレクトリに置く。
        dotnet_tmp="$(mktemp -d)"
        trap 'rm -rf "$dotnet_tmp"' EXIT
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$dotnet_tmp/dotnet-install.sh"
        bash "$dotnet_tmp/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet"
        rm -rf "$dotnet_tmp"
        trap - EXIT
        echo 'Add this to your shell profile if dotnet is still not found:'
        echo '  export PATH="$HOME/.dotnet:$PATH"'
        export PATH="$HOME/.dotnet:$PATH"
    fi
    if ! command -v bazel >/dev/null 2>&1; then
        case "$(uname -m)" in
            x86_64) bazelisk_arch=amd64 ;;
            aarch64|arm64) bazelisk_arch=arm64 ;;
            *) echo "Unsupported CPU for automatic Bazelisk install: $(uname -m)" >&2; exit 1 ;;
        esac
        mkdir -p "$HOME/.local/bin"
        curl -fsSL "https://github.com/bazelbuild/bazelisk/releases/latest/download/bazelisk-linux-${bazelisk_arch}" \
            -o "$HOME/.local/bin/bazel"
        chmod +x "$HOME/.local/bin/bazel"
        export PATH="$HOME/.local/bin:$PATH"
        echo 'Add this to your shell profile if bazel is still not found:'
        echo '  export PATH="$HOME/.local/bin:$PATH"'
    fi
fi

if [[ $test_core -eq 1 ]]; then
    need dotnet
    dotnet run --project "$root/src/Meltype.Core.Tests"
fi

if [[ $build_mozc -eq 1 ]]; then
    need git
    need clang
    need bazel
    if [[ -n "$bazel_cache" ]]; then
        bash "$root/native/mozc/build-mozc-helper.sh" "$mozc_source" "$bazel_cache"
    else
        bash "$root/native/mozc/build-mozc-helper.sh" "$mozc_source"
    fi
fi

if [[ $package -eq 1 ]]; then
    need dotnet
    need clang
    bash "$root/linux/build.sh"
fi

if [[ $install -eq 1 ]]; then
    if [[ ! -f "$root/linux/build/Meltype-linux/install.sh" ]]; then
        echo "Build output is missing. Run linux/build-cli.sh --package first." >&2
        exit 1
    fi
    (cd "$root/linux/build/Meltype-linux" && bash install.sh)
fi
