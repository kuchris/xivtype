param([ValidateSet('probe', 'once')][string]$Mode = 'probe', [int]$TimeoutSeconds = 10)
$ErrorActionPreference = 'Stop'
$diagnosticPath = Join-Path $env:APPDATA 'XIVLauncher\pluginConfigs\XivType\diagnostics'
$statePath = Join-Path $diagnosticPath 'typing-state.json'
$baseline = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
Set-Content -LiteralPath (Join-Path $diagnosticPath 'control.txt') -Value $Mode
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 150
    try { $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } catch { continue }
    $success = if ($Mode -eq 'probe') { $state.accepted -gt $baseline.accepted } else { $state.submissions -gt $baseline.submissions }
    if ($success -and !$state.running) {
        Write-Output "PASS: $($state.Status)"
        exit 0
    }
    if (!$state.running -and $state.Status -ne $baseline.Status) {
        Write-Output "FAIL: $($state.Status)"
        exit 1
    }
} while ([DateTime]::UtcNow -lt $deadline)
Set-Content -LiteralPath (Join-Path $diagnosticPath 'control.txt') -Value 'off'
Write-Output "TIMEOUT: $($state.Status); input='$($state.Input)'; ready=$($state.Ready)"
exit 2
