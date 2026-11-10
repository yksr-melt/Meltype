#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hrmcngs
#
# Mac の CLI から Meltype をテスト・ビルド・インストールする入口。
#   mac/build-cli.sh --setup --test --install
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"

setup=0
test_core=0
build_only=0
install=0
all=1

usage() {
    cat <<'EOF'
Usage: mac/build-cli.sh [options]

Options:
  --setup       Install .NET 10 SDK into ~/.dotnet if dotnet is missing.
  --test        Run Meltype.Core.Tests and the Mac shortcut tests.
  --build       Build mac/build/Meltype.app without installing it.
  --install     Build and install Meltype.app into ~/Library/Input Methods.
  --all         Run --test --install. This is the default.
  -h, --help    Show this help.

Examples:
  mac/build-cli.sh --setup --all
  mac/build-cli.sh --build
  mac/build-cli.sh --install
EOF
}

need() {
    command -v "$1" >/dev/null 2>&1 || {
        echo "Missing command: $1" >&2
        echo "Run mac/build-cli.sh --setup, or install the dependency manually." >&2
        exit 1
    }
}

ensure_dotnet_path() {
    if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
        export PATH="$HOME/.dotnet:$PATH"
    fi
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --setup) setup=1; all=0 ;;
        --test) test_core=1; all=0 ;;
        --build) build_only=1; all=0 ;;
        --install) install=1; all=0 ;;
        --all) all=1 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
    esac
    shift
done

if [[ $all -eq 1 ]]; then
    test_core=1
    install=1
fi

if [[ $setup -eq 1 ]]; then
    if [[ "$(uname)" != "Darwin" ]]; then
        echo "--setup for this script only supports macOS." >&2
        exit 1
    fi
    xcode-select -p >/dev/null
    need swift
    ensure_dotnet_path
    if ! command -v dotnet >/dev/null 2>&1; then
        # /tmp の固定名は別ユーザーに差し替えられるので、自分専用の一時ディレクトリに置く。
        dotnet_tmp="$(mktemp -d)"
        trap 'rm -rf "$dotnet_tmp"' EXIT
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$dotnet_tmp/dotnet-install.sh"
        bash "$dotnet_tmp/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet"
        rm -rf "$dotnet_tmp"
        trap - EXIT
        export PATH="$HOME/.dotnet:$PATH"
        echo 'Add this to your shell profile if dotnet is still not found:'
        echo '  export PATH="$HOME/.dotnet:$PATH"'
    fi
fi

ensure_dotnet_path

if [[ $test_core -eq 1 ]]; then
    need dotnet
    dotnet run --project "$root/src/Meltype.Core.Tests"
    need swift
    swift test --package-path "$here"
fi

if [[ $build_only -eq 1 ]]; then
    need dotnet
    need swift
    (cd "$here" && ./build.sh --no-install)
fi

if [[ $install -eq 1 ]]; then
    need dotnet
    need swift
    (cd "$here" && ./build.sh)
fi
