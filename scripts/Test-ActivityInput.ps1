param([int]$Pulses = 3, [int]$IntervalSeconds = 30, [ValidateSet('shift', 'f13')][string]$Key = 'shift', [string]$OutputPath = 'docs\activity-input-sequence-2026-10-28.json')
$ErrorActionPreference = 'Stop'
$diag = Join-Path $env:APPDATA 'XIVLauncher\pluginConfigs\XivType\diagnostics'
$probePath = Join-Path $diag 'activity-probe.json'
$results = [Collections.Generic.List[object]]::new()
$began = [DateTime]::UtcNow
for ($index = 0; $index -lt $Pulses; $index++) {
    $due = $began.AddSeconds($index * $IntervalSeconds)
    while ([DateTime]::UtcNow -lt $due) { Start-Sleep -Milliseconds 200 }
    $state = Get-Content -LiteralPath (Join-Path $diag 'typing-state.json') -Raw | ConvertFrom-Json
    if ($state.running -or $state.InputTimers.Territory -ne 1360) {
        throw 'Lobby diagnostic requires stopped automation and territory 1360.'
    }
    $stamp = [DateTime]::Now
    Set-Content -LiteralPath (Join-Path $diag 'control.txt') -Value $(if ($Key -eq 'shift') { 'activity-probe' } else { "activity-probe $Key" })
    $deadline = [DateTime]::UtcNow.AddSeconds(6)
    $fresh = $false
    do {
        Start-Sleep -Milliseconds 150
        if ((Test-Path -LiteralPath $probePath) -and (Get-Item -LiteralPath $probePath).LastWriteTime -gt $stamp) { $fresh = $true; break }
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$fresh) { throw 'No fresh diagnostic result; no further messages will be sent.' }
    $probe = Get-Content -LiteralPath $probePath -Raw | ConvertFrom-Json
    $results.Add([pscustomobject]@{ Pulse = $index + 1; At = [DateTimeOffset]::Now.ToString('O'); Result = $probe })
    $summary = [pscustomobject]@{
        Version = $state.Version
        DurationSeconds = ([DateTime]::UtcNow - $began).TotalSeconds
        Completed = $results.Count
        Requested = $Pulses
        Passed = @($results | Where-Object { !$_.Result.Passed }).Count -eq 0 -and $results.Count -eq $Pulses
        Pulses = $results
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath
    Write-Output "Pulse $($index + 1)/${Pulses}: passed=$($probe.Passed); background=$($probe.AllBackground); minimized=$($probe.AllMinimized); keyboard=$($probe.SawShift); natural reset=$($probe.SawNaturalTimerReset); before=$($probe.BeforeInputTimer)s"
    if (!$probe.Passed) { exit 1 }
}
Write-Output 'PASS: Repeated game-targeted keyboard activity observed. Duty/server AFK protection is still unverified.'
