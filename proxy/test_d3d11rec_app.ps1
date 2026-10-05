<#
.SYNOPSIS
End-to-end check of the DirectX 11 recorder through the real command line, on a stand-in game (FakeGame11.exe, proxy\fakegame11.cpp).

Needs dist\SCSFix (build\publish.ps1) and proxy\build\Release\FakeGame11.exe (the proxy's CMake build). It adds the stand-in as a game
the user added (SCSFix's data folder, manual-games.json, restored afterwards), then: the status says Needs recording; `record install`
puts d3d11.dll next to it (the only recorder: it never runs on DirectX 12); the game is run once, the proxy records its shaders;
the status says Ready; `compile` replays them on the GPU (every item compiled, none failed); `record uninstall` leaves the folder as
it was. Run it with no game of yours running: the compile warms under the stand-in's exe name only.
#>
param([string]$Dist = (Join-Path $PSScriptRoot "..\dist\SCSFix"), [string]$Build = (Join-Path $PSScriptRoot "build\Release"))
$ErrorActionPreference = "Stop"
$cli = Join-Path $Dist "cli\scsfix.exe"
$fakeExe = Join-Path $Build "FakeGame11.exe"
foreach ($f in $cli, $fakeExe, (Join-Path $Dist "native\d3d11.dll")) { if (-not (Test-Path $f)) { throw "missing $f" } }

$root = Join-Path ([IO.Path]::GetTempPath()) ("scsfix-d3d11rec-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$game = Join-Path $root "Game"
New-Item -ItemType Directory -Force $game | Out-Null
Copy-Item $fakeExe $game
$exe = Join-Path $game "FakeGame11.exe"
$data = Join-Path $env:LOCALAPPDATA "SCSFix"
$manual = Join-Path $data "manual-games.json"
$kept = if (Test-Path $manual) { Get-Content $manual -Raw } else { $null }
$failures = @()
function Check([bool]$ok, [string]$what) { if ($ok) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failures += $what } }
function Cli { & $cli @args 2>&1 | ForEach-Object { "$_" } }

try {
    New-Item -ItemType Directory -Force $data | Out-Null
    $entry = [ordered]@{ Exe = $exe; InstallDir = $game; Name = "FakeGame11"; Confirmed = $true }
    $all = @(if ($kept) { $kept | ConvertFrom-Json }) + [pscustomobject]$entry
    ConvertTo-Json -InputObject @($all) | Set-Content $manual -Encoding utf8

    $s = (Cli status FakeGame11) -join "`n"
    Check ($s -match "status\s+NeedsRecording") "unreadable DirectX 11 game: Needs recording"
    Cli record install FakeGame11 | Out-Null
    Check ((Test-Path "$game\d3d11.dll") -and -not (Test-Path "$game\d3d12.dll")) "the recorder is d3d11.dll alone"
    Check (Test-Path "$game\scsfix.armed") "armed after a clean check"

    & $exe 1 | Out-Null
    Check ((Test-Path "$game\scsfix.db") -and (Get-Item "$game\scsfix.db").Length -gt 1000) "the game's run was recorded"
    $s = (Cli status FakeGame11) -join "`n"
    Check ($s -match "status\s+Ready") "recorded: Ready"

    $c = (Cli compile FakeGame11) -join "`n"
    Check ($c -match "Done 4/4 \(0 failed\)") "compile replays 3 shader items + the HS+DS pair, none failed"
    $s = (Cli status FakeGame11) -join "`n"
    Check ($s -match "status\s+Warmed") "Warmed afterwards"

    Cli record uninstall FakeGame11 | Out-Null
    Check ((@(Get-ChildItem $game | ForEach-Object Name) -join ",") -eq "FakeGame11.exe") "uninstall leaves only the game's own files"
}
finally {
    if ($null -ne $kept) { Set-Content $manual $kept -Encoding utf8 } else { Remove-Item $manual -ErrorAction SilentlyContinue }
    Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failures) { throw "failed: $($failures -join '; ')" }
Write-Host "PASS d3d11rec (app)"
