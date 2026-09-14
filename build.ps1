# Builds dist\Offline2FA.exe with the C# compiler that ships with .NET Framework 4.x (no SDK needed).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe from .NET Framework 4.x was not found.' }

$versionMatch = Select-String -Path src\Version.cs -Pattern 'const string Number = "([0-9.]+)"'
if (-not $versionMatch) { throw 'Could not read the version from src\Version.cs' }
$version = $versionMatch.Matches[0].Groups[1].Value
Write-Host "Offline 2FA v$version"

New-Item -ItemType Directory -Force build, dist, assets, release | Out-Null
$common = @('/nologo', '/codepage:65001', '/optimize+', '/warnaserror-')

function Invoke-Csc([string[]]$arguments) {
    & $csc @common @arguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $($arguments -join ' ')" }
}

if (-not (Test-Path assets\app.ico)) {
    Write-Host 'Generating icon...'
    Invoke-Csc @('/target:exe', '/out:build\MakeIcon.exe', '/r:System.Drawing.dll', 'tools\MakeIcon.cs')
    & build\MakeIcon.exe assets\app.ico
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
}

Write-Host 'Running self-tests...'
Invoke-Csc @('/target:exe', '/out:build\SelfTest.exe', '/nowarn:649', '/r:System.Runtime.Serialization.dll',
    'src\Totp.cs', 'src\Vault.cs', 'tests\SelfTest.cs')
& build\SelfTest.exe
if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed.' }

Write-Host 'Building Offline2FA.exe...'
Invoke-Csc @('/target:winexe', '/platform:anycpu', '/out:dist\Offline2FA.exe',
    '/win32icon:assets\app.ico', '/win32manifest:src\app.manifest', '/resource:assets\app.ico,Offline2FA.app.ico',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Runtime.Serialization.dll',
    'src\*.cs')

$releaseFile = "release\Offline2FA-v$version.exe"
Copy-Item dist\Offline2FA.exe $releaseFile -Force
Write-Host "Done: $(Resolve-Path dist\Offline2FA.exe)"
Write-Host "Release: $(Resolve-Path $releaseFile)"
