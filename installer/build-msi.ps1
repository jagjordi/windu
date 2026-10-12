<#
.SYNOPSIS
    Builds the windu MSI installer.

.DESCRIPTION
    Publishes a self-contained single-file windu.exe, converts LICENSE into the
    RTF the installer UI needs, and packages both with WiX. Used by the release
    workflow and runnable locally to reproduce exactly what CI produces.

.EXAMPLE
    ./installer/build-msi.ps1 -Version 1.1.0 -Runtime win-x64 -Output dist
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Output = 'dist'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

# WiX 5 is the last release under the MS-RL; version 7 requires accepting the
# Open Source Maintenance Fee EULA, which we deliberately do not depend on.
$wixVersion = '5.0.2'

Step "Checking for WiX $wixVersion"
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    Step 'Installing the WiX tool'
    dotnet tool install --global wix --version $wixVersion
    $env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:PATH"
}
wix extension add -g "WixToolset.UI.wixext/$wixVersion" 2>&1 | Out-Null

$publish = Join-Path $repo "artifacts/msi-publish/$Runtime"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

Step "Publishing windu.exe ($Runtime, self-contained)"
dotnet publish (Join-Path $repo 'src/Windu/Windu.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:Version=$Version `
    --output $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Step 'Generating the license RTF'
# The WiX UI needs RTF, and RTF is 7-bit: anything above ASCII has to be
# written as a \'hh escape or it renders as a replacement character.
$license = Get-Content (Join-Path $repo 'LICENSE') -Raw
$escaped = $license -replace '\\', '\\\\' -replace '\{', '\{' -replace '\}', '\}'

$sb = [System.Text.StringBuilder]::new()
$cp1252 = [System.Text.Encoding]::GetEncoding(1252)
foreach ($ch in $escaped.ToCharArray()) {
    if ([int]$ch -lt 128) {
        [void]$sb.Append($ch)
    }
    else {
        foreach ($byte in $cp1252.GetBytes([string]$ch)) {
            [void]$sb.Append("\'" + $byte.ToString('x2'))
        }
    }
}

$body = ($sb.ToString() -split "`r?`n" |
    ForEach-Object { if ($_.Trim() -eq '') { '\par' } else { "$_\par" } }) -join "`r`n"

$rtfPath = Join-Path $repo 'artifacts/License.rtf'
New-Item -ItemType Directory -Force (Split-Path $rtfPath) | Out-Null
$rtf = "{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}`r`n\viewkind4\uc1\pard\f0\fs18`r`n$body`r`n}"
Set-Content -Path $rtfPath -Value $rtf -Encoding ASCII -NoNewline

Step "Building the MSI"
$arch = if ($Runtime -eq 'win-arm64') { 'arm64' } else { 'x64' }
$outDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repo $Output }
New-Item -ItemType Directory -Force $outDir | Out-Null
$msi = Join-Path $outDir "windu-$Version-$Runtime.msi"

# Build the argument list up front: passing (Join-Path …) inline to a native
# command lets PowerShell split the result into separate arguments.
$exePath     = Join-Path $publish 'windu.exe'
$iconPath    = Join-Path $PSScriptRoot 'windu.ico'
$readmePath  = Join-Path $repo 'README.md'
$licensePath = Join-Path $repo 'LICENSE'
$wxs         = Join-Path $PSScriptRoot 'windu.wxs'

$wixArgs = @(
    'build', $wxs
    '-ext', 'WixToolset.UI.wixext'
    '-arch', $arch
    '-d', "ProductVersion=$Version"
    '-d', "ExePath=$exePath"
    '-d', "IconPath=$iconPath"
    '-d', "ReadmePath=$readmePath"
    '-d', "LicensePath=$licensePath"
    '-d', "LicenseRtfPath=$rtfPath"
    '-o', $msi
)
& wix @wixArgs
if ($LASTEXITCODE -ne 0) { throw "wix build failed with exit code $LASTEXITCODE" }

$size = (Get-Item $msi).Length
Step ("Built {0} ({1:N0} bytes)" -f (Split-Path $msi -Leaf), $size)
$msi
