# SPDX-License-Identifier: GPL-3.0-or-later
# Versioned installation; personal data and old versions are retained.
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('Inspect','Install','Update','Uninstall','Cleanup')][string]$Action = 'Inspect',
    [string]$Package = $PSScriptRoot,
    [switch]$ReplaceExisting,
    [switch]$SkipCurrentUserStartup
)
$ErrorActionPreference = 'Stop'
$clsid = '{C540BAF6-88B1-5B30-B524-2C8702B034FC}'
function Get-Server([Microsoft.Win32.RegistryView]$View) {
    $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $View)
    try {
        $key = $hive.OpenSubKey("SOFTWARE\Classes\CLSID\$clsid\InprocServer32")
        if ($null -eq $key) { return $null }
        try { return [string]$key.GetValue('') } finally { $key.Dispose() }
    } finally { $hive.Dispose() }
}
function Get-InstallRoot {
    $machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $machine.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion')
        try { $programFiles = [string]$key.GetValue('ProgramFilesDir') } finally { $key.Dispose() }
    } finally { $machine.Dispose() }
    return [IO.Path]::GetFullPath((Join-Path $programFiles 'Meltype-flash'))
}
function Assert-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run in an administrator PowerShell.' }
}
function Assert-Child([string]$Base, [string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Base).TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Path leaves root: $Path" }
    return $full
}
function Enter-InstallLock([string]$Root) {
    $lockPath = Join-Path $Root '.install.lock'
    foreach ($path in @($Root, $lockPath)) {
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse point in installation lock path' }
    }
    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    try { return [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch [IO.IOException] { throw 'Another installation or update is running. Wait for it to finish, then retry.' }
}
function Write-InstallState([string]$Path, $State) {
    if ((Test-Path -LiteralPath $Path) -and ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse point in installation state path' }
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, ($State | ConvertTo-Json), [Text.UTF8Encoding]::new($true))
        # Move-Item -Force can delete the previous state before a failed move.
        # Replace keeps the previous file intact when the replacement fails.
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}
function Test-Package([string]$Root) {
    $manifest = Get-Content -LiteralPath (Join-Path $Root 'SHA256SUMS.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($name in @('meltype-flash.exe','meltype-rs.exe','tip/x64/MeltypeTip.dll','tip/x86/MeltypeTip.dll',
        'tip/x64/meltype-tip-worker.exe','tip/x86/meltype-tip-worker.exe','Manage-Windows.ps1','Start-Portable.ps1')) {
        if ($manifest.PSObject.Properties.Name -notcontains $name) { throw "Missing manifest entry: $name" }
    }
    foreach ($item in $manifest.PSObject.Properties) {
        $path = Assert-Child $Root (Join-Path $Root $item.Name)
        $cursor = Get-Item -LiteralPath $path
        while ($cursor.FullName -ne $Root) {
            if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point: $path" }
            $cursor = Get-Item -LiteralPath (Split-Path -Parent $cursor.FullName)
            if ($null -eq $cursor) { throw "Invalid package parent: $path" }
        }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.Value) { throw "SHA256 mismatch: $($item.Name)" }
    }
    foreach ($arch in 'x64','x86') {
        $dll = Join-Path $Root "tip/$arch/MeltypeTip.dll"
        $hex = [BitConverter]::ToString([IO.File]::ReadAllBytes($dll))
        foreach ($id in @([Guid]$clsid, [Guid]'{6D01DE9C-8AB3-5CFC-A5C6-10893CDF8762}')) {
            if (-not $hex.Contains([BitConverter]::ToString($id.ToByteArray()))) {
                throw "Missing Meltype-FLASH identity: tip/$arch/MeltypeTip.dll. Rebuild the Flash DLL; legacy shared-ID packages cannot be installed."
            }
        }
    }
    return $manifest
}
$packageRoot = (Resolve-Path -LiteralPath $Package).Path.TrimEnd('\')
if ($Action -notin @('Uninstall','Cleanup')) {
    $manifest = Test-Package $packageRoot
    Write-Host "Package hashes verified: $packageRoot"
    if ($Action -eq 'Inspect') { return }
}
$installRoot = Get-InstallRoot
$installationLock = $null
try {
if (-not $WhatIfPreference) {
    Assert-Administrator
    $installationLock = Enter-InstallLock $installRoot
}
$statePath = Assert-Child $installRoot (Join-Path $installRoot 'installed.json')
$state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
$old = @{ x64 = (Get-Server ([Microsoft.Win32.RegistryView]::Registry64)); x86 = (Get-Server ([Microsoft.Win32.RegistryView]::Registry32)) }
if ($Action -eq 'Update' -and $null -eq $state) { throw 'No managed installation. Use Install first.' }
if ($Action -notin @('Uninstall','Cleanup')) {
    foreach ($arch in 'x64','x86') {
        if ($old[$arch] -and -not $old[$arch].StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase) -and -not $ReplaceExisting) {
            throw "Another Meltype-FLASH is registered: $($old[$arch]). Back up its data; use -ReplaceExisting to replace the Meltype-FLASH registration."
        }
    }
}
if (-not $PSCmdlet.ShouldProcess($installRoot, "$Action Meltype-FLASH IME")) { return }
$windows = [Environment]::GetFolderPath('Windows')
$reg64 = Join-Path $windows 'System32\regsvr32.exe'
if (-not [Environment]::Is64BitProcess) { $reg64 = Join-Path $windows 'Sysnative\regsvr32.exe' }
function Register-Server([string]$Arch,[string]$Dll,[switch]$Remove) {
    $exe = if ($Arch -eq 'x64') { $reg64 } else { Join-Path $windows 'SysWOW64\regsvr32.exe' }
    $arguments = @('/s')
    if ($Remove) { $arguments += '/u' }
    $arguments += '"'+$Dll+'"'
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "regsvr32 $Arch failed: $($process.ExitCode)" }
}
function Set-StartMenuShortcut([string]$Exe) {
    $programs = [Environment]::GetFolderPath('CommonPrograms')
    $shortcut = Join-Path $programs 'Meltype-FLASH.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $Exe
    $link.WorkingDirectory = Split-Path -Parent $Exe
    $link.IconLocation = "$Exe,0"
    $link.Description = 'Meltype-FLASH'
    $link.Save()
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Meltype-FLASH.exe'
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name '(default)' -Value $Exe
    Write-Host "Start menu shortcut: $shortcut"
}
function Remove-StartMenuShortcut {
    $shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Meltype-FLASH.lnk'
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Meltype-FLASH.exe'
    if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse -Force }
}
function Invoke-StartupMaintenance([string]$Exe, [ValidateSet('rebind','disable','stop-tray')][string]$Operation) {
    if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "Startup helper missing: $Exe" }
    $report = [IO.Path]::GetTempFileName()
    $process = $null
    try {
        $process = Start-Process -FilePath $Exe -ArgumentList @('--startup-action', $Operation, '--startup-result', ('"'+$report+'"')) -WindowStyle Hidden -PassThru
        $null = $process.Handle
        if (-not $process.WaitForExit(45000)) {
            try { $process.Kill(); $process.WaitForExit() } catch { }
            throw 'Startup helper timed out; no further installation steps were attempted.'
        }
        $result = Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($process.ExitCode -ne 0 -or $result.protocol -ne 1 -or $result.ok -ne $true -or $result.enabled -isnot [bool]) {
            throw "Startup maintenance failed: $($result.error)"
        }
        return $result
    } finally {
        if ($null -ne $process) { $process.Dispose() }
        Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
    }
}
function Assert-NoReparse([string]$Root, [string]$Path) {
    $cursor = Get-Item -LiteralPath $Path -Force
    while ($true) {
        if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point in installation: $($cursor.FullName)" }
        if ($cursor.FullName -eq $Root) { break }
        $parent = Split-Path -Parent $cursor.FullName
        if (-not $parent) { throw 'Installation root not found in parent chain' }
        $cursor = Get-Item -LiteralPath $parent -Force
    }
}
function Remove-ManagedFiles([string]$Root) {
    # Preflight every deletion before touching files. Never traverse a link or
    # recursively remove a directory that might contain user-created files.
    Assert-NoReparse $Root $Root
    $versions = Assert-Child $Root (Join-Path $Root 'versions')
    $plans = @()
    if (Test-Path -LiteralPath $versions) {
        Assert-NoReparse $Root $versions
        foreach ($version in Get-ChildItem -LiteralPath $versions -Directory -Force) {
            $folder = Assert-Child $Root $version.FullName
            Assert-NoReparse $Root $folder
            $manifestPath = Join-Path $folder 'SHA256SUMS.json'
            if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { continue }
            Assert-NoReparse $Root $manifestPath
            $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $files = @(); $directories = @($folder)
            foreach ($item in $manifest.PSObject.Properties) {
                $path = Assert-Child $folder (Join-Path $folder $item.Name)
                if (Test-Path -LiteralPath $path) {
                    Assert-NoReparse $Root $path
                    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Manifest path is not a file: $path" }
                    if ((Get-FileHash -LiteralPath $path).Hash -ne $item.Value) { throw "Modified installed file retained; cleanup stopped: $path" }
                    $files += $path
                }
                $parent = Split-Path -Parent $path
                while ($parent -ne $folder) {
                    $directories += (Assert-Child $folder $parent)
                    $parent = Split-Path -Parent $parent
                }
            }
            $plans += @{ files = $files; manifest = $manifestPath; directories = $directories }
        }
    }
    $pending = @()
    foreach ($plan in $plans) {
        foreach ($path in $plan.files) {
            try { Remove-Item -LiteralPath $path -Force -ErrorAction Stop } catch { $pending += $path }
        }
        # Keep the manifest when locked files remain so Cleanup can retry later.
        if (@($plan.files | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0) {
            try { Remove-Item -LiteralPath $plan.manifest -Force -ErrorAction Stop } catch { $pending += $plan.manifest }
        }
        foreach ($directory in $plan.directories | Sort-Object -Unique | Sort-Object Length -Descending) {
            if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
                try { Remove-Item -LiteralPath $directory -ErrorAction Stop } catch { $pending += $directory }
            }
        }
    }
    if ($pending.Count) { Write-Warning "Locked files retained. Close applications or reboot, then run -Action Cleanup: $($pending -join ', ')" }
    Write-Host 'Removed verified package files. User data and unlisted files were retained.'
}
if ($Action -eq 'Cleanup') {
    if ($null -eq $state -or $state.unregistered -ne $true) { throw 'Uninstall must complete before Cleanup.' }
    Remove-StartMenuShortcut
    foreach ($arch in 'x64','x86') {
        if ($old[$arch] -and $old[$arch].StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Installed IME is still registered; cleanup refused.' }
    }
    Remove-ManagedFiles $installRoot
    return
}
if ($Action -eq 'Uninstall') {
    if ($null -eq $state) { throw 'No managed installation; nothing removed.' }
    foreach ($arch in 'x64','x86') {
        $dll = Assert-Child $installRoot ([string]$state.$arch)
        if ($old[$arch] -and $old[$arch] -ne $dll) { throw "Registration changed externally: $($old[$arch])" }
    }
    if (-not $SkipCurrentUserStartup) {
        if ($state.startupProtocol -ne 1) {
            throw 'This old installation has no startup helper. Update first, or disable startup manually and pass -SkipCurrentUserStartup.'
        }
        $helper = Assert-Child $installRoot (Join-Path ([string]$state.package) 'meltype-flash.exe')
        $null = Invoke-StartupMaintenance $helper 'stop-tray'
        $null = Invoke-StartupMaintenance $helper 'disable'
    }
    foreach ($arch in 'x64','x86') { Register-Server $arch ([string]$state.$arch) -Remove }
    $state | Add-Member -NotePropertyName unregistered -NotePropertyValue $true -Force
    Write-InstallState $statePath $state
    try { Remove-StartMenuShortcut } catch { Write-Warning "Could not remove the Start menu shortcut: $_" }
    Remove-ManagedFiles $installRoot
    Write-Host 'IME unregistered. Current-user tray/startup were processed unless explicitly skipped. User data is retained.'
    return
}
$stamp = (Get-FileHash -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.json') -Algorithm SHA256).Hash.Substring(0,16)
$versionRoot = Assert-Child $installRoot (Join-Path $installRoot "versions\$stamp")
if (Test-Path -LiteralPath (Join-Path $versionRoot 'SHA256SUMS.json')) {
    # A live DLL need not be overwritten when reinstalling the identical package.
    $null = Test-Package $versionRoot
    if ((Get-FileHash -LiteralPath (Join-Path $versionRoot 'SHA256SUMS.json') -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.json') -Algorithm SHA256).Hash) {
        throw 'Existing version folder belongs to a different package.'
    }
} else {
    New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null
    foreach ($item in $manifest.PSObject.Properties) {
        $destination = Assert-Child $versionRoot (Join-Path $versionRoot $item.Name)
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $packageRoot $item.Name) -Destination $destination -Force
    }
    Copy-Item -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.json') -Destination $versionRoot -Force
    $null = Test-Package $versionRoot
}
$attempted = @()
if (-not $SkipCurrentUserStartup) {
    # Use the new helper so older trays produce an explicit manual-close error.
    $null = Invoke-StartupMaintenance (Join-Path $versionRoot 'meltype-flash.exe') 'stop-tray'
}
try {
    foreach ($arch in 'x64','x86') {
        $attempted += $arch
        Register-Server $arch (Join-Path $versionRoot "tip\$arch\MeltypeTip.dll")
    }
    $newState = @{ x64 = (Join-Path $versionRoot 'tip\x64\MeltypeTip.dll'); x86 = (Join-Path $versionRoot 'tip\x86\MeltypeTip.dll'); package = $versionRoot; startupProtocol = 1 }
    Write-InstallState $statePath $newState
} catch {
    $failure = $_
    foreach ($arch in $attempted) {
        try {
            if ($old[$arch] -and (Test-Path -LiteralPath $old[$arch])) { Register-Server $arch $old[$arch] }
            else { Register-Server $arch (Join-Path $versionRoot "tip\$arch\MeltypeTip.dll") -Remove }
        } catch { Write-Warning "Rollback $arch failed: $_" }
    }
    throw $failure
}
if (-not $SkipCurrentUserStartup) {
    try {
        $startup = Invoke-StartupMaintenance (Join-Path $versionRoot 'meltype-flash.exe') 'rebind'
        Write-Host "Current-user startup enabled: $($startup.enabled)"
    } catch {
        throw "IME installation completed, but current-user startup was not updated: $_. Old version files were retained; retry the startup helper from the new folder."
    }
}
try { Set-StartMenuShortcut (Join-Path $versionRoot 'meltype-flash.exe') }
catch { Write-Warning "Could not create the Start menu shortcut: $_" }
Write-Host "Installed: $versionRoot. Select Meltype-FLASH in Windows language settings; restart open applications."
Write-Host "Start tray: & '$versionRoot\meltype-flash.exe' --tray"
} finally {
    if ($null -ne $installationLock) { $installationLock.Dispose() }
}
