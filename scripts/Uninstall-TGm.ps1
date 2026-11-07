[CmdletBinding()]
param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'
$taskName = 'TGm Sensor Host'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$targetUser = $identity.Name
$targetSid = $identity.User.Value
$hashAlgorithm = [Security.Cryptography.SHA256]::Create()
try {
    $hash = $hashAlgorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($targetSid))
} finally {
    $hashAlgorithm.Dispose()
}
$pipeName = 'TGm.SensorHost.' + ([Convert]::ToHexString($hash[0..7]))
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$logPath = Join-Path $localAppData 'TGm\Logs\sensor-host-setup.log'
$sensorHost = Join-Path $PSScriptRoot 'TGm.SensorHost.exe'

$taskExists = $null -ne (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)
if ($taskExists) {
    if (!(Test-Path -LiteralPath $sensorHost)) {
        throw 'TGm.SensorHost.exe is required to remove the elevated sensor task.'
    }

    function Quote-ProcessArgument([string]$Value) {
        return '"' + $Value.Replace('"', '\"') + '"'
    }

    $arguments = @(
        '--uninstall',
        '--target-user', (Quote-ProcessArgument $targetUser),
        '--target-sid', (Quote-ProcessArgument $targetSid),
        '--pipe-name', (Quote-ProcessArgument $pipeName),
        '--log-path', (Quote-ProcessArgument $logPath)
    ) -join ' '
    $process = Start-Process -FilePath $sensorHost -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "The CPU sensor task could not be removed (exit code $($process.ExitCode))."
    }
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -LiteralPath $runKey -Name 'TGm' -ErrorAction SilentlyContinue
& cmdkey.exe '/delete:TGm/TimesGateMonitor/LocalToken' 2>$null | Out-Null

if ($RemoveUserData) {
    $targets = @(
        (Join-Path $localAppData 'TGm'),
        (Join-Path $localAppData 'TimesGateMonitor')
    )
    $localRoot = [IO.Path]::GetFullPath($localAppData).TrimEnd('\') + '\'
    foreach ($target in $targets) {
        $resolvedTarget = [IO.Path]::GetFullPath($target)
        if (!$resolvedTarget.StartsWith($localRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove an unexpected path: $resolvedTarget"
        }
        if (Test-Path -LiteralPath $resolvedTarget) {
            Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
        }
    }
}

Write-Host 'TGm autostart, saved LocalToken and optional sensor task were removed.'
if (!$RemoveUserData) {
    Write-Host 'Layouts, logs and imported media were kept. Use -RemoveUserData to delete them.'
}
