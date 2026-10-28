param([int]$Seconds = 8, [int]$WaitSeconds = 30, [string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
$statePath = Join-Path $env:APPDATA 'XIVLauncher\pluginConfigs\XivType\diagnostics\typing-state.json'
$waitDeadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
do {
    $baseline = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if (!$baseline.GameForeground -and $baseline.running -and $baseline.BackgroundTyping -and $baseline.Ready) { break }
    Start-Sleep -Milliseconds 150
} while ([DateTime]::UtcNow -lt $waitDeadline)
if ($baseline.GameForeground -or !$baseline.running -or !$baseline.BackgroundTyping -or !$baseline.Ready) {
    Write-Output "PRECONDITION: Need active background typing with GameForeground=false. Status: $($baseline.Status)"
    exit 2
}
$samples = [Collections.Generic.List[object]]::new()
$deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
do {
    Start-Sleep -Milliseconds 200
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $samples.Add([pscustomobject]@{
        Time = [DateTimeOffset]::Now.ToString('O')
        Foreground = $state.GameForeground
        Running = $state.running
        Submissions = $state.submissions
        Score = $state.Score
        SystemKeyEvents = $state.SystemKeyEvents
    })
    if ($state.GameForeground -or !$state.running -or $state.Version -ne $baseline.Version) {
        Write-Output "FAIL: Game focus, running state or plugin version changed. $($state.Status)"
        exit 1
    }
} while ([DateTime]::UtcNow -lt $deadline)
$result = [pscustomobject]@{
    Version = $state.Version
    DurationSeconds = $Seconds
    AllSamplesBackground = @($samples | Where-Object Foreground).Count -eq 0
    StartingSubmissions = $baseline.submissions
    EndingSubmissions = $state.submissions
    StartingScore = $baseline.Score
    EndingScore = $state.Score
    SystemKeyEvents = $state.SystemKeyEvents - $baseline.SystemKeyEvents
    Passed = $state.submissions -gt $baseline.submissions -and $state.SystemKeyEvents -eq $baseline.SystemKeyEvents
    Samples = $samples
}
if ($OutputPath) { $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath }
if (!$result.Passed) { Write-Output 'FAIL: No acknowledged submission while continuously unfocused.'; exit 1 }
Write-Output "PASS: $($state.submissions - $baseline.submissions) acknowledged submissions while continuously unfocused; score $($baseline.Score) -> $($state.Score)."
