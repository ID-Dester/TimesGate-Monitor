[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-preview.1',

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [string]$OutputDirectory = 'release',

    [switch]$FrameworkDependent,

    [switch]$SkipRestore,

    [switch]$DisableNuGetAudit
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$rootWithSeparator = $projectRoot.TrimEnd('\') + '\'
if (!$outputRoot.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must resolve inside the TGm repository.'
}

$packageSuffix = if ($FrameworkDependent) { "$Runtime-framework-dependent" } else { $Runtime }
$packageName = "TGm-$Version-$packageSuffix"
$packageDirectory = Join-Path $outputRoot $packageName
$appPublish = Join-Path $outputRoot '.app-publish'
$hostPublish = Join-Path $outputRoot '.host-publish'

foreach ($path in @($packageDirectory, $appPublish, $hostPublish)) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if (!$fullPath.StartsWith($outputRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an unexpected path: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$commonPublishArguments = @(
    '-c', 'Release',
    '--no-restore',
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

$restoreArguments = @(
    (Join-Path $projectRoot 'TimesGateMonitor.sln'),
    '--configfile', (Join-Path $projectRoot 'NuGet.Config'),
    '--ignore-failed-sources',
    '--locked-mode'
)
if ($DisableNuGetAudit) {
    $restoreArguments += '-p:NuGetAudit=false'
}

if ($FrameworkDependent) {
    $commonPublishArguments += @('--self-contained', 'false')
} else {
    $commonPublishArguments += @('-r', $Runtime, '--self-contained', 'true')
    $restoreArguments += @('-r', $Runtime)
}

if (!$SkipRestore) {
    dotnet restore @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
}

dotnet publish (Join-Path $projectRoot 'src\TimesGateMonitor.App\TimesGateMonitor.App.csproj') @commonPublishArguments -o $appPublish
if ($LASTEXITCODE -ne 0) { throw 'TGm publish failed.' }

dotnet publish (Join-Path $projectRoot 'src\TimesGateMonitor.SensorHost\TimesGateMonitor.SensorHost.csproj') @commonPublishArguments -o $hostPublish
if ($LASTEXITCODE -ne 0) { throw 'TGm.SensorHost publish failed.' }

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $appPublish '*') -Destination $packageDirectory -Recurse -Force
Copy-Item -Path (Join-Path $hostPublish '*') -Destination $packageDirectory -Recurse -Force

Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\Uninstall-TGm.ps1') -Destination $packageDirectory
$rebootInvestigation = Join-Path $projectRoot 'docs\TIMES-GATE-REBOOT-INVESTIGATION-RU.txt'
if (Test-Path -LiteralPath $rebootInvestigation) {
    Copy-Item -LiteralPath $rebootInvestigation -Destination $packageDirectory
}
$knownIssues = Join-Path $projectRoot 'docs\KNOWN-ISSUES.md'
if (Test-Path -LiteralPath $knownIssues) {
    Copy-Item -LiteralPath $knownIssues -Destination $packageDirectory
}

$runtimeNote = if ($FrameworkDependent) {
    'This package requires the .NET 8 Desktop Runtime for Windows x64.'
} else {
    'This package includes the required .NET runtime.'
}
$releaseReadme = @"
TGm $Version ($Runtime)

Unofficial monitor for Divoom Times Gate.
This project is not affiliated with or endorsed by Divoom.

Run TGm.exe as a normal Windows user. Do not run the main interface as administrator.
$runtimeNote
Extended CPU sensors can be enabled later from the gear menu > CPU SENSOR ACCESS.

KNOWN PREVIEW LIMITATION:
During some long Network Text sessions, Times Gate has returned to its stock
clock face after roughly 35,000 requests recorded by TGm's local server. The
exact threshold and root cause are not proven. TGm attempts automatic recovery
but cannot prevent this device-side event. Read KNOWN-ISSUES.md before use.

Portable removal:
1. Exit TGm from its tray menu.
2. Run Uninstall-TGm.ps1 to remove autostart, the saved LocalToken and the optional sensor task.
3. Delete this folder. Add -RemoveUserData only if you also want to delete layouts, logs and imported media.

Project license: LICENSE
Dependency notices: THIRD-PARTY-NOTICES.md and licenses\
"@
Set-Content -LiteralPath (Join-Path $packageDirectory 'README.txt') -Value $releaseReadme -Encoding utf8NoBOM

$licensesDirectory = Join-Path $packageDirectory 'licenses'
New-Item -ItemType Directory -Path $licensesDirectory -Force | Out-Null
$dotnetCommand = Get-Command 'dotnet' -ErrorAction Stop
$dotnetRoot = Split-Path -Parent $dotnetCommand.Source
foreach ($dotnetLegalFile in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $source = Join-Path $dotnetRoot $dotnetLegalFile
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $licensesDirectory "dotnet-$dotnetLegalFile")
    }
}

$repositoryNuGetRoot = Join-Path $projectRoot '.nuget-packages'
$nugetRoot = if ($env:NUGET_PACKAGES) {
    [IO.Path]::GetFullPath($env:NUGET_PACKAGES)
} elseif (Test-Path -LiteralPath $repositoryNuGetRoot) {
    $repositoryNuGetRoot
} else {
    Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
}

$hidSharpLicense = Join-Path $nugetRoot 'hidsharp\2.6.4\LICENSE.txt'
if (!(Test-Path -LiteralPath $hidSharpLicense)) {
    throw "HidSharp license file is missing from the NuGet cache: $hidSharpLicense"
}
Copy-Item -LiteralPath $hidSharpLicense -Destination (Join-Path $licensesDirectory 'HidSharp-2.6.4-Apache-2.0.txt')

$dotnetNoticePackages = @(
    'system.codedom\10.0.2',
    'system.io.ports\10.0.3',
    'system.management\10.0.2',
    'system.threading.accesscontrol\10.0.3'
)
foreach ($package in $dotnetNoticePackages) {
    $notice = Join-Path (Join-Path $nugetRoot $package) 'THIRD-PARTY-NOTICES.TXT'
    if (Test-Path -LiteralPath $notice) {
        $safeName = $package.Replace('\', '-') + '-THIRD-PARTY-NOTICES.txt'
        Copy-Item -LiteralPath $notice -Destination (Join-Path $licensesDirectory $safeName)
    }
}

$mplNotice = @'
The distributed LibreHardwareMonitorLib, BlackSharp.Core, DiskInfoToolkit and
RAMSPDToolkit-NDD assemblies are covered by the Mozilla Public License 2.0.
The license text is available at https://www.mozilla.org/MPL/2.0/
The corresponding source locations and exact package versions are listed in
THIRD-PARTY-NOTICES.md. TGm does not modify their source files.
'@
Set-Content -LiteralPath (Join-Path $licensesDirectory 'MPL-2.0-SOURCE-NOTICE.txt') -Value $mplNotice -Encoding utf8NoBOM

$archivePath = Join-Path $outputRoot "$packageName.zip"
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashLine = "$hash  $([IO.Path]::GetFileName($archivePath))"
Set-Content -LiteralPath "$archivePath.sha256" -Value $hashLine -Encoding ascii

Remove-Item -LiteralPath $appPublish -Recurse -Force
Remove-Item -LiteralPath $hostPublish -Recurse -Force

Write-Host "Release package: $archivePath"
Write-Host "SHA-256: $hash"
