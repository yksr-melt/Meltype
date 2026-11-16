# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 aster-void

# 1.6.0 以前の Meltype の自動更新用 (Build-Package.ps1 の「互換の zip」)。前の版の Meltype は、リリースの Meltype-<版>-windows.zip を
# 展開してこのスクリプトを実行する。同じフォルダーのインストーラーを、今の版の自動更新 (Updater.Apply) と同じ引数で実行する。
# 前の版の Meltype は -UpdateLog にログの場所を渡す。
param([string]$UpdateLog)

$ErrorActionPreference = 'Stop'
$setup = Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'Meltype-*-setup.exe' -File | Select-Object -First 1
if (-not $setup) { throw "インストーラーがありません: $PSScriptRoot" }
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
if ($UpdateLog) { $arguments += "/LOG=`"$UpdateLog`"" }
# インストーラーが起動した Meltype は残り続ける。Start-Process -Wait は子孫のプロセスも待つので使わない
$process = Start-Process -FilePath $setup.FullName -ArgumentList $arguments -PassThru
$null = $process.Handle # Windows PowerShell 5.1 では、取っておかないと終了後に ExitCode が $null になる (exit $null は 0)
$process.WaitForExit()
exit $process.ExitCode
