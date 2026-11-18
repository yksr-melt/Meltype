# SPDX-License-Identifier: GPL-3.0-or-later
# Actual package-manager control flow over isolated files. Only OS registration,
# elevation and the startup process boundary are replaced; no real IME changes.
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('meltype-lifecycle-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$global:MeltypeLifecycleTest = @{
    install = (Join-Path $root 'installed'); registration = @{}; enabled = $false
    calls = [Collections.Generic.List[string]]::new(); failArch = ''; failStartup = $false; startupExe = ''; failStop = $false
}
function Assert-That([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Package([string]$PackageLabel) {
    $folder = Join-Path $root $PackageLabel
    $manifest = @{}
    foreach ($name in @('meltype-flash.exe','meltype-rs.exe','tip/x64/MeltypeTip.dll','tip/x86/MeltypeTip.dll',
        'tip/x64/meltype-tip-worker.exe','tip/x86/meltype-tip-worker.exe','Manage-Windows.ps1','Start-Portable.ps1')) {
        $path = Join-Path $folder $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, $PackageLabel + ':' + $name)
        if ($name.EndsWith('MeltypeTip.dll')) {
            $identityBytes = [IO.File]::ReadAllBytes($path) + ([Guid]'C540BAF6-88B1-5B30-B524-2C8702B034FC').ToByteArray() + ([Guid]'6D01DE9C-8AB3-5CFC-A5C6-10893CDF8762').ToByteArray()
            [IO.File]::WriteAllBytes($path, [byte[]]$identityBytes)
        }
        $manifest[$name] = (Get-FileHash -LiteralPath $path).Hash
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.json') -Encoding UTF8
    return $folder
}
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Manage-Windows.ps1') -Raw
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
Assert-That ($errors.Count -eq 0) 'Invalid manager syntax'
$replacements = @{
    'Get-InstallRoot' = 'function Get-InstallRoot { return $global:MeltypeLifecycleTest.install }'
    'Assert-Administrator' = 'function Assert-Administrator { $global:MeltypeLifecycleTest.calls.Add("admin") }'
    'Set-StartMenuShortcut' = 'function Set-StartMenuShortcut($Exe) { $global:MeltypeLifecycleTest.calls.Add("shortcut:set") }'
    'Remove-StartMenuShortcut' = 'function Remove-StartMenuShortcut { $global:MeltypeLifecycleTest.calls.Add("shortcut:remove") }'
    'Get-Server' = @'
function Get-Server($View) {
    $arch = if ($View -eq [Microsoft.Win32.RegistryView]::Registry64) { 'x64' } else { 'x86' }
    return $global:MeltypeLifecycleTest.registration[$arch]
}
'@
    'Register-Server' = @'
function Register-Server([string]$Arch,[string]$Dll,[switch]$Remove) {
    $state = $global:MeltypeLifecycleTest
    $state.calls.Add("register:${Arch}:$Remove")
    if ($Remove) { $state.registration.Remove($Arch) } else {
        $state.registration[$Arch] = $Dll
        if ($state.failArch -eq $Arch) { $state.failArch = ''; throw 'controlled registration failure' }
    }
}
'@
    'Invoke-StartupMaintenance' = @'
function Invoke-StartupMaintenance([string]$Exe,[string]$Operation) {
    $state = $global:MeltypeLifecycleTest
    $state.calls.Add("startup:$Operation")
    if ($state.failStop -and $Operation -eq 'stop-tray') { throw 'controlled stop failure' }
    if ($state.failStartup -and $Operation -ne 'stop-tray') { throw 'controlled startup failure' }
    if ($Operation -eq 'disable') { $state.enabled = $false }
    elseif ($state.enabled) { $state.startupExe = $Exe }
    return @{ protocol = 1; ok = $true; enabled = $state.enabled }
}
'@
}
$functions = @($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $replacements.ContainsKey($node.Name)}, $true))
Assert-That ($functions.Count -eq 7) 'OS seam definitions changed; update the harness'
foreach ($function in $functions | Sort-Object {$_.Extent.StartOffset} -Descending) {
    $source = $source.Substring(0,$function.Extent.StartOffset) + $replacements[$function.Name] + $source.Substring($function.Extent.EndOffset)
}
$driver = [ScriptBlock]::Create($source)
function Reject([scriptblock]$Run, [string]$Message) {
    $failed = $false
    try { & $Run } catch {
        if ($_.Exception.Message -notlike $Message) { throw }
        $failed = $true
    }
    Assert-That $failed "Expected failure: $Message"
}
$one = Package 'one'; $two = Package 'two'; $three = Package 'three'
$data = Join-Path $root 'user-data.json'; [IO.File]::WriteAllText($data, 'keep learning and settings')
$state = $global:MeltypeLifecycleTest
$receipt = Join-Path $state.install 'installed.json'
& $driver -Action Install -Package $one -WhatIf
Assert-That (-not (Test-Path -LiteralPath $state.install)) 'WhatIf created an install folder'
Assert-That ($state.calls.Count -eq 0) 'WhatIf reached a mutating OS boundary'
& $driver -Action Install -Package $one
Assert-That (-not $state.enabled) 'Installation enabled opted-out startup'
$first = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
Assert-That (Test-Path -LiteralPath $first.x64) 'Installed DLL missing'
$state.enabled = $true
& $driver -Action Update -Package $two
$second = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
Assert-That ($first.package -ne $second.package) 'New package reused an old version folder'
Assert-That ($state.startupExe -eq (Join-Path $second.package 'meltype-flash.exe')) 'Startup still points to old version'
Assert-That (Test-Path -LiteralPath $first.x64) 'Update removed old live DLL'
$locked = [IO.File]::Open($second.x64, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { & $driver -Action Update -Package $two } finally { $locked.Dispose() }
$before = Get-Content -LiteralPath $receipt -Raw
$state.failStop = $true
Reject { & $driver -Action Update -Package $three } '*controlled stop failure*'
Assert-That ((Get-Content -LiteralPath $receipt -Raw) -eq $before) 'Stop failure published new state'
Assert-That ($state.registration.x64 -eq $second.x64) 'Stop failure changed registration'
$state.failStop = $false
Reject { & $driver -Action Cleanup -Package $three } '*Uninstall must complete*'
$state.failArch = 'x86'
Reject { & $driver -Action Update -Package $three } '*controlled registration failure*'
Assert-That ((Get-Content -LiteralPath $receipt -Raw) -eq $before) 'Failed registration published new state'
Assert-That ($state.registration.x64 -eq $second.x64 -and $state.registration.x86 -eq $second.x86) 'Both registrations were not restored'
$state.failStartup = $true
Reject { & $driver -Action Update -Package $three } '*installation completed*startup was not updated*'
$third = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
Assert-That ($third.package -ne $second.package -and $state.registration.x64 -eq $third.x64) 'Partial completion status was inaccurate'
Assert-That (Test-Path -LiteralPath $second.x64) 'Startup failure removed previous version'
$state.failStartup = $false
& $driver -Action Update -Package $three
$count = @($state.calls | Where-Object {$_ -like 'startup:*'}).Count
$state.registration.x86 = Join-Path $root 'externally-registered.dll'
Reject { & $driver -Action Uninstall -Package $three } '*Registration changed externally*'
Assert-That (@($state.calls | Where-Object {$_ -like 'startup:*'}).Count -eq $count) 'Ownership failure changed startup'
$state.registration.x86 = $third.x86
$state.failStartup = $true
Reject { & $driver -Action Uninstall -Package $three } '*controlled startup failure*'
Assert-That ($state.registration.x64 -eq $third.x64) 'Startup failure should stop before unregistering'
$state.failStartup = $false; $state.calls.Clear()
$unlisted = Join-Path $third.package 'my-notes.txt'
[IO.File]::WriteAllText($unlisted, 'preserve unlisted file')
$lockedPath = $third.x64
$locked = [IO.File]::Open($lockedPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { & $driver -Action Uninstall -Package $three } finally { $locked.Dispose() }
Assert-That (Test-Path -LiteralPath $lockedPath) 'Locked DLL was unexpectedly removed'
Assert-That (Test-Path -LiteralPath (Join-Path $third.package 'SHA256SUMS.json')) 'Retry manifest was removed'
Assert-That (-not (Test-Path -LiteralPath $third.x86)) 'Unlocked DLL was not removed'
Assert-That ((Get-Content -LiteralPath $unlisted -Raw) -eq 'preserve unlisted file') 'Unlisted file was removed'
& $driver -Action Cleanup -Package $three
Assert-That (-not (Test-Path -LiteralPath $lockedPath)) 'Cleanup did not remove previously locked DLL'
Assert-That (Test-Path -LiteralPath $unlisted) 'Cleanup removed unlisted file'
Assert-That (-not $state.enabled -and $state.registration.Count -eq 0) 'Uninstall left startup or IME registrations'
Assert-That (($state.calls -join ',') -eq 'admin,startup:stop-tray,startup:disable,register:x64:True,register:x86:True,shortcut:remove,admin,shortcut:remove') 'Uninstall did not disable startup first'
Assert-That ((Get-Content -LiteralPath $data -Raw) -eq 'keep learning and settings') 'User data changed'
$badFolder = Join-Path $state.install 'versions/modified'
New-Item -ItemType Directory -Path $badFolder -Force | Out-Null
$badFile = Join-Path $badFolder 'owned.txt'
[IO.File]::WriteAllText($badFile, 'original')
$digest = (Get-FileHash -LiteralPath $badFile).Hash
$badManifest = Join-Path $badFolder 'SHA256SUMS.json'
@{ 'owned.txt' = $digest } | ConvertTo-Json | Set-Content -LiteralPath $badManifest -Encoding UTF8
[IO.File]::WriteAllText($badFile, 'user modified')
Reject { & $driver -Action Cleanup -Package $three } '*Modified installed file retained*'
Assert-That ((Get-Content -LiteralPath $badFile -Raw) -eq 'user modified') 'Modified file deleted'
@{ '../outside.txt' = $digest } | ConvertTo-Json | Set-Content -LiteralPath $badManifest -Encoding UTF8
Reject { & $driver -Action Cleanup -Package $three } '*Path leaves root*'
Assert-That (Test-Path -LiteralPath $unlisted) 'Preflight touched unrelated files'
Write-Host "PASS: isolated install/update/idempotency/rollback/startup failure/ownership/uninstall/WhatIf. Files: $root"
Remove-Variable -Name MeltypeLifecycleTest -Scope Global
