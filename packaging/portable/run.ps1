# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 rankoi

$ErrorActionPreference = 'Stop'

try {
    $expanded = Join-Path $env:MELTYPE_BUNDLE_WORK 'files'
    Expand-Archive -LiteralPath $env:MELTYPE_BUNDLE_PAYLOAD -DestinationPath $expanded -Force
    $app = Join-Path $expanded 'app\Meltype.exe'
    if (-not (Test-Path -LiteralPath $app)) { throw 'Application executable was not found.' }
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $app) 'Meltype.portable'))) {
        throw 'Portable mode marker is missing; refusing to launch this package.'
    }
    $process = Start-Process -FilePath $app -ArgumentList '--gui' -WorkingDirectory (Split-Path -Parent $app) -PassThru -Wait
    exit $process.ExitCode
}
catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "Meltype portable edition could not start.`r`n`r`n$($_.Exception.Message)",
        'Meltype portable edition', 'OK', 'Error') | Out-Null
    exit 1
}
finally {
    Remove-Item -LiteralPath $env:MELTYPE_BUNDLE_WORK -Recurse -Force -ErrorAction SilentlyContinue
}
