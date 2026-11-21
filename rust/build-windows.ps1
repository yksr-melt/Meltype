# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
param([switch]$WithTip, [switch]$NativeOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Push-Location $root
try {
    foreach ($target in 'x86_64-pc-windows-msvc', 'i686-pc-windows-msvc') {
        rustup target add $target
        if ($LASTEXITCODE -ne 0) { throw "rustup failed: $target" }
        # The TSF bridge needs both DLL architectures; the settings UI runs as x64.
        # CI already builds the host workspace before packaging the bridge.
        [string[]]$packages = if ($NativeOnly -or $target -eq 'i686-pc-windows-msvc') { @('-p', 'meltype-native', '-p', 'meltype-tip-worker') } else { @('--workspace') }
        cargo build --locked --release @packages --target $target --target-dir "$root/target"
        if ($LASTEXITCODE -ne 0) { throw "cargo failed: $target" }
    }
    if (-not $NativeOnly) {
        cargo build --locked --release -p meltype-cli --features meltype-core/bundled-mozc --target x86_64-pc-windows-msvc --target-dir "$root/target"
        if ($LASTEXITCODE -ne 0) { throw "TTY build failed" }
    }
    if ($WithTip) {
        & "$root/native/tip/build.ps1" -RustCore
        if ($LASTEXITCODE -ne 0) { throw 'TSF build failed' }
        Copy-Item -LiteralPath "$root/target/x86_64-pc-windows-msvc/release/meltype_native.dll" -Destination "$root/native/tip/bin/rust-x64/"
        Copy-Item -LiteralPath "$root/target/i686-pc-windows-msvc/release/meltype_native.dll" -Destination "$root/native/tip/bin/rust-x86/"
        Copy-Item -LiteralPath "$root/target/x86_64-pc-windows-msvc/release/meltype-tip-worker.exe" -Destination "$root/native/tip/bin/rust-x64/"
        Copy-Item -LiteralPath "$root/target/i686-pc-windows-msvc/release/meltype-tip-worker.exe" -Destination "$root/native/tip/bin/rust-x86/"
        Copy-Item -LiteralPath "$root/LICENSE", "$root/THIRD-PARTY-NOTICES.md" -Destination "$root/native/tip/bin/rust-x64/"
        Copy-Item -LiteralPath "$root/LICENSE", "$root/THIRD-PARTY-NOTICES.md" -Destination "$root/native/tip/bin/rust-x86/"
    }
} finally { Pop-Location }
