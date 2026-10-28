param([int]$WaitSeconds = 90)
$ErrorActionPreference = 'Stop'
$statePath = Join-Path $env:APPDATA 'XIVLauncher\pluginConfigs\XivType\diagnostics\typing-state.json'
$deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
$baseline = $null
$samples = [Collections.Generic.List[object]]::new()
$blocked = $false
$resumed = $false
do {
    Start-Sleep -Milliseconds 100
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if (!$baseline) {
        if (!$state.EnemyJammerActive) { continue }
        $baseline = $state
        $deadline = [DateTime]::UtcNow.AddSeconds(25)
        Write-Output ('Observed jammer: '+($state.EnemyCastLabels -join ', '))
    }
    $samples.Add([pscustomobject]@{
        Time = [DateTimeOffset]::Now.ToString('O')
        EnemyJammerActive = $state.EnemyJammerActive
        InputAllowed = $state.InputAllowed
        Edits = $state.EditOperations
        Submits = $state.SubmitOperations
        ActivityKeys = $state.AfkKeysSent
        Reason = $state.InputBlockReason
    })
    if (!$state.InputAllowed) {
        $blocked = $true
        if ($state.EditOperations -ne $baseline.EditOperations -or $state.SubmitOperations -ne $baseline.SubmitOperations -or $state.AfkKeysSent -ne $baseline.AfkKeysSent) {
            Write-Output 'FAIL: An input operation occurred during the prohibited interval.'
            $samples | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath docs\jammer-pause-failure.json
            exit 1
        }
    } elseif ($blocked) {
        if ($state.EditOperations -gt $baseline.EditOperations -or $state.SubmitOperations -gt $baseline.SubmitOperations) {
            $resumed = $true
            break
        }
    }
} while ([DateTime]::UtcNow -lt $deadline)
$report = [pscustomobject]@{
    Version = $state.Version
    Observed = $null -ne $baseline
    Blocked = $blocked
    Resumed = $resumed
    Passed = $blocked -and $resumed
    Samples = $samples
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath docs\jammer-pause-live.json
if ($report.Passed) { Write-Output 'PASS: Jammer prevented edits, submits and activity keys; input resumed after permission returned.'; exit 0 }
Write-Output "INCOMPLETE: observed=$($report.Observed), blocked=$blocked, resumed=$resumed; status=$($state.Status)"
exit 2
