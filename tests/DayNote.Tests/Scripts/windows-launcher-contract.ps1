# Runs the production runtime helper with fake Process objects. Never discovers or closes real processes.
param(
    [Parameter(Mandatory = $true)][string]$RepoDir,
    [Parameter(Mandatory = $true)][string]$ScratchDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$module = Import-Module (Join-Path $RepoDir 'scripts/launcher-runtime.psm1') -Force -PassThru
$fixtureRepo = Join-Path $ScratchDir 'repo'
$null = New-Item -ItemType Directory -Path (Join-Path $fixtureRepo '.git') -Force
$devDirectory = Join-Path $fixtureRepo 'src/DayNote/bin/Debug/net10.0'
$null = New-Item -ItemType Directory -Path $devDirectory -Force
$development = Join-Path $devDirectory 'DayNote.exe'
[System.IO.File]::WriteAllText($development, 'development')
$built = Join-Path $fixtureRepo 'publish/DayNote.exe'

& $module {
    param($fixtureRepo, $built, $development)
    function script:Get-Process { $script:fixtureProcesses }
    function script:Assert-Contract {
        param([bool]$Condition, [string]$Message)
        if (-not $Condition) { throw $Message }
    }
    function script:Assert-Throws {
        param([scriptblock]$Action, [string]$Expected)
        try { & $Action } catch {
            if ($_.Exception.Message -notlike "*$Expected*") { throw }
            return
        }
        throw "Expected failure containing: $Expected"
    }
    function script:New-FakeProcess {
        param([string]$Path, [int]$Identity, [string]$Behavior = 'Close')
        $fake = [pscustomobject]@{ Path = $Path; Id = $Identity; HasExited = $false; CloseCalls = 0; Behavior = $Behavior; ExitCode = 7 }
        $fake | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
        $fake | Add-Member -MemberType ScriptMethod -Name CloseMainWindow -Value {
            $this.CloseCalls++
            if ($this.Behavior -eq 'Fail') { throw 'simulated live close failure' }
            if ($this.Behavior -eq 'Refuse') { return $false }
            $this.HasExited = $true
            return $true
        }
        return $fake
    }
    $builtProcess = New-FakeProcess $built.ToUpperInvariant() 101
    $devProcess = New-FakeProcess $development 102
    $outside = New-FakeProcess (Join-Path $fixtureRepo 'elsewhere/DayNote.exe') 103
    $builder = New-FakeProcess (Join-Path $fixtureRepo 'dotnet.exe') 104
    # A launcher/project appearing in arbitrary data grants no process identity.
    $builder | Add-Member NoteProperty CommandLine (Join-Path $fixtureRepo 'scripts/run-dev.ps1')
    $script:fixtureProcesses = @($builtProcess, $devProcess, $outside, $builder)
    Assert-Contract (@(Get-RuntimeProcess -RepoDir $fixtureRepo -BuiltExecutable $built).Count -eq 2) 'Exact runtime selection failed.'
    Assert-Contract (@(Get-RuntimeProcess -RepoDir $fixtureRepo -BuiltExecutable $built -Mode Built)[0].Id -eq 101) 'Built mode selected another mode.'
    Assert-Contract (@(Get-RuntimeProcess -RepoDir $fixtureRepo -BuiltExecutable $built -Mode Development)[0].Id -eq 102) 'Development mode selected another mode.'

    Claim-LauncherRuntime -Token 'first' -RepoDir $fixtureRepo
    Request-RuntimeClose -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'first'
    Assert-Contract ($builtProcess.CloseCalls -eq 1 -and $devProcess.CloseCalls -eq 1) 'Selected apps did not receive ordinary closure.'
    Assert-Contract ($outside.CloseCalls -eq 0 -and $builder.CloseCalls -eq 0) 'An unrelated process received closure.'
    Request-RuntimeClose -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'first'
    Assert-Contract ($builtProcess.CloseCalls -eq 1) 'An exited process received another close request.'

    $refusing = New-FakeProcess $built 105 'Refuse'
    $script:fixtureProcesses = @($refusing)
    Assert-Throws { Request-RuntimeClose -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'first' } 'Close it normally'
    Assert-Contract (-not $refusing.HasExited -and $refusing.CloseCalls -eq 1) 'A refusal was not respected.'
    $failing = New-FakeProcess $built 106 'Fail'
    $script:fixtureProcesses = @($failing)
    Assert-Throws { Request-RuntimeClose -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'first' } 'Could not ask'

    Claim-LauncherRuntime -Token 'newer' -RepoDir $fixtureRepo
    $script:fixtureProcesses = @($refusing)
    Assert-Throws { Request-RuntimeClose -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'first' } 'newer launcher'
    Assert-Contract ($refusing.CloseCalls -eq 1) 'A superseded launcher still closed an app.'
    Assert-Throws { Wait-RuntimeReady -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'first' -Mode Built -TimeoutSeconds 0 } 'newer launcher'
    $script:fixtureProcesses = @(New-FakeProcess $built 107)
    Wait-RuntimeReady -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'newer' -Mode Built -TimeoutSeconds 0
    Assert-Throws { Wait-RuntimeReady -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'newer' -Mode Development -TimeoutSeconds 0 } 'Development mode'
    $exitedChild = [pscustomobject]@{ HasExited = $true; ExitCode = 9 }
    Assert-Throws { Wait-RuntimeReady -Label 'DayNote' -RepoDir $fixtureRepo -BuiltExecutable $built -Token 'newer' -Mode Development -StartedProcess $exitedChild } 'exited before it was ready'

    $publish = Join-Path $fixtureRepo 'published'
    $candidate = Join-Path $fixtureRepo 'candidate'
    $null = New-Item -ItemType Directory -Path $publish, $candidate
    [System.IO.File]::WriteAllText((Join-Path $publish 'marker'), 'old')
    [System.IO.File]::WriteAllText((Join-Path $candidate 'marker'), 'new')
    Publish-RuntimeCandidate -CandidateDir $candidate -PublishDir $publish
    Assert-Contract ([System.IO.File]::ReadAllText((Join-Path $publish 'marker')) -eq 'new') 'Candidate was not published.'
    Assert-Contract (-not (Test-Path -LiteralPath $candidate)) 'Published candidate was left behind.'
    Assert-Contract (@(Get-ChildItem -LiteralPath $fixtureRepo -Filter 'published.previous.*').Count -eq 0) 'Previous output was not cleaned after success.'

    $null = New-Item -ItemType Directory -Path $candidate
    [System.IO.File]::WriteAllText((Join-Path $candidate 'marker'), 'failed candidate')
    $script:failCandidate = $candidate
    function script:Move-Item {
        param([string]$LiteralPath, [string]$Destination, [switch]$Force)
        if ($LiteralPath -eq $script:failCandidate) { throw 'simulated candidate publication failure' }
        Microsoft.PowerShell.Management\Move-Item @PSBoundParameters
    }
    Assert-Throws { Publish-RuntimeCandidate -CandidateDir $candidate -PublishDir $publish } 'simulated candidate publication failure'
    Assert-Contract ([System.IO.File]::ReadAllText((Join-Path $publish 'marker')) -eq 'new') 'Publication failure lost the previous artifact.'
    Assert-Contract (Test-Path -LiteralPath $candidate) 'A failed candidate unexpectedly replaced the previous artifact.'
} $fixtureRepo $built $development

Write-Output 'Windows launcher mocked contracts passed.'
