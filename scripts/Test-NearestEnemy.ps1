param([int]$WaitSeconds = 120, [int]$SampleSeconds = 20, [switch]$TypeOnce, [string]$OutputPath = 'docs\nearest-enemy-live.json')
# Passive check of screen-position prompt/actor binding. Only -TypeOnce sends input (one prompt).
$ErrorActionPreference = 'Stop'
$diagnosticPath = Join-Path $env:APPDATA 'XIVLauncher\pluginConfigs\XivType\diagnostics'
$statePath = Join-Path $diagnosticPath 'typing-state.json'
$deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
$samples = [Collections.Generic.List[object]]::new()
$started = $null
do {
    Start-Sleep -Milliseconds 250
    try { $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } catch { continue }
    if (!$state.Ready -or @($state.Words).Count -eq 0) { continue }
    if (!$started) {
        $started = [DateTime]::UtcNow
        $deadline = $started.AddSeconds($SampleSeconds)
        Write-Output "Observing prompts (priority=$($state.Priority))."
    }
    $samples.Add([pscustomobject]@{
        Time = [DateTimeOffset]::Now.ToString('O')
        Actors = @($state.ActorScreens)
        Words = @($state.Words | ForEach-Object { [pscustomobject]@{ Node = $_.NodeId; Text = $_.Text; X = $_.ScreenX; Y = $_.ScreenY; Entity = $_.EntityId; Distance = $_.Distance } })
    })
} while ([DateTime]::UtcNow -lt $deadline)

$words = @($samples | ForEach-Object { $_.Words })
$mapped = @($words | Where-Object { $null -ne $_.Distance })
$selection = $null
if ($TypeOnce -and $samples.Count -gt 0) {
    $before = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    Set-Content -LiteralPath (Join-Path $diagnosticPath 'control.txt') -Value 'once'
    $limit = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 50
        try { $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } catch { continue }
        if ($state.target -and !$selection) {
            $candidates = @($before.Words | Where-Object { $null -ne $_.Distance } | Sort-Object Distance)
            $selection = [pscustomobject]@{ Target = $state.target; NearestKnown = $candidates[0].Text; Candidates = $candidates }
        }
    } while ($state.running -and [DateTime]::UtcNow -lt $limit)
    if ($state.running) { Set-Content -LiteralPath (Join-Path $diagnosticPath 'control.txt') -Value 'off' }
}
$report = [pscustomobject]@{
    Version = $state.Version
    Samples = $samples.Count
    PromptObservations = $words.Count
    MappedObservations = $mapped.Count
    MappedRatio = if ($words.Count) { [math]::Round($mapped.Count / $words.Count, 3) } else { 0 }
    Selection = $selection
    SampleData = $samples
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath
if ($samples.Count -eq 0) { Write-Output 'INCOMPLETE: no visible prompts were observed.'; exit 2 }
Write-Output "Mapped $($mapped.Count)/$($words.Count) prompt observations ($($report.MappedRatio))."
$mapped | Group-Object Text | ForEach-Object { '{0} -> actor {1:X8}, {2:0.0} y' -f $_.Name, $_.Group[-1].Entity, $_.Group[-1].Distance } | Select-Object -First 12
if ($selection) { Write-Output "Typed '$($selection.Target)'; nearest mapped beforehand: '$($selection.NearestKnown)'." }
if ($mapped.Count -eq 0) { Write-Output 'FAIL: no prompt was bound to an actor.'; exit 1 }
exit 0
