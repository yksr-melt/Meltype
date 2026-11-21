# SPDX-License-Identifier: GPL-3.0-or-later
# Validate Cargo argument boundaries without invoking compilers or registering an IME.
$ErrorActionPreference = 'Stop'
$global:meltypeBuildArgumentCalls = [Collections.Generic.List[object]]::new()
function rustup { $global:LASTEXITCODE = 0 }
function cargo { $global:meltypeBuildArgumentCalls.Add(@($args)); $global:LASTEXITCODE = 0 }
try {
    & "$PSScriptRoot/build-windows.ps1"
    if ($global:meltypeBuildArgumentCalls.Count -ne 3) { throw 'Expected two target builds and the x64 TTY build' }
    $first = $global:meltypeBuildArgumentCalls[0]
    if ($first -notcontains '--workspace' -or $first -contains '-') { throw 'Workspace option was split into characters' }
    $second = $global:meltypeBuildArgumentCalls[1]
    if ($second -contains '--workspace' -or $second -notcontains 'meltype-tip-worker') { throw 'Incorrect x86 packages' }
    $tty = $global:meltypeBuildArgumentCalls[2]
    if ($tty -notcontains 'meltype-core/bundled-mozc' -or $tty -notcontains 'meltype-cli' -or $tty -notcontains 'x86_64-pc-windows-msvc') { throw 'Incorrect TTY build' }
    $global:meltypeBuildArgumentCalls.Clear()
    & "$PSScriptRoot/build-windows.ps1" -NativeOnly
    foreach ($call in $global:meltypeBuildArgumentCalls) {
        if ($call -contains '--workspace' -or $call -notcontains 'meltype-native' -or $call -notcontains 'meltype-tip-worker') { throw 'Incorrect native packages' }
    }
    Write-Host 'PASS: full workspace and native-only Cargo argument boundaries'
} finally {
    Remove-Item Function:rustup, Function:cargo
    Remove-Variable -Name meltypeBuildArgumentCalls -Scope Global
}
