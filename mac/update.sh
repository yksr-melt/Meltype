#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hrmcngs

# Run from any directory:
#   bash "$HOME/Documents/github/other/Meltype/mac/update.sh"
# Show help without starting the input method:
#   bash "$HOME/Documents/github/other/Meltype/mac/update.sh" --help
# This updates and starts Meltype. Avoid running while investigating crashes.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
app="$HOME/Library/Input Methods/Meltype.app"

case "${1:-}" in
    -h|--help)
        echo "Usage: bash mac/update.sh"
        echo "Test, build, install, verify, and select the local Mac input method."
        exit 0 ;;
    "") ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
esac
[[ $# -le 1 ]] || { echo "Too many arguments" >&2; exit 1; }
[[ "$(uname)" == Darwin ]] || { echo "This updater requires macOS." >&2; exit 1; }

# File replacement restores the old session itself if it fails.
bash "$here/build-cli.sh" --test --install

# Check the fixed expectations with default settings and no learned data.
(
    check_data="$(mktemp -d "${TMPDIR:-/tmp}/meltype-input-check.XXXXXX")"
    trap 'rm -rf "$check_data"' EXIT
    MELTYPE_DATA_DIR="$check_data" "$app/Contents/MacOS/Meltype" --check-inputs "$here/Resources/InputChecks.tsv"
)
swift "$here/select-input-source.swift"
echo "Meltype update and input checks completed."
