# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

# ソースからビルドしたインストーラーで Meltype を入れる。配布するものと同じインストーラー (Build-Package.ps1 → packaging\Meltype.iss) を使う。
# アンインストールは、ふつうに入れたときと同じ (トレイの「アンインストール...」か、Windows の「設定」→「アプリ」)。
# -NoIme: Meltype IME を新しく登録しない (変換ボックスで入力する方式だけ。管理者権限が要らない)。登録済みなら、インストーラーが更新する
param([switch]$NoIme)

$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'Build-Package.ps1')
$setup = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'dist') -Filter 'Meltype-test-*-setup.exe' -File | Sort-Object LastWriteTime | Select-Object -Last 1
$arguments = @('/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
if ($NoIme) { $arguments += '/MERGETASKS="!meltypeime"' }
# インストーラーが起動した Meltype は残り続ける。Start-Process -Wait は子孫のプロセスも待つので使わない
$process = Start-Process -FilePath $setup.FullName -ArgumentList $arguments -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "インストールに失敗しました (exit code $($process.ExitCode))。記録: $env:TEMP\Setup Log *.txt" }
Write-Host 'Meltype をインストールして起動しました。タスクトレイのアイコンから設定・ログ・一時停止 (Ctrl+半角/全角) ができます。'
