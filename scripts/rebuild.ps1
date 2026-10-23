Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$scriptExitCode = 0

# rebuild: produce a fresh self-contained Release build for Windows, then launch
# it. This is the slow launcher, run after changing source. There is no codesign
# step on Windows. run-built remains the no-build fast path after this.

function Set-Utf8Console {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [Console]::InputEncoding = $utf8NoBom
    [Console]::OutputEncoding = $utf8NoBom
    $global:OutputEncoding = $utf8NoBom
    if (Get-Command chcp.com -ErrorAction SilentlyContinue) {
        & chcp.com 65001 > $null
        $null = $LASTEXITCODE
    }
}

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing required command: $Name"
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [int[]]$AllowedExitCodes = @(0)
    )

    & $FilePath @ArgumentList
    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    if ($AllowedExitCodes -notcontains $exitCode) {
        throw "Command failed with exit code ${exitCode}: $FilePath $($ArgumentList -join ' ')"
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoDir = Split-Path -Parent $scriptDir
$projectFile = Join-Path $repoDir "src/DayNote/DayNote.csproj"
$publishDir = Join-Path $repoDir "bin/Release/net10.0/win-x64/publish"
$exePath = Join-Path $publishDir "DayNote.exe"
$runtimeToken = [guid]::NewGuid().ToString("N")
$candidateDir = "$publishDir.candidate.$runtimeToken"

try {
    Set-Utf8Console
    Import-Module (Join-Path $scriptDir "launcher-runtime.psm1") -Force
    Require-Command dotnet

    Set-Location $repoDir

    Claim-LauncherRuntime -Token $runtimeToken -RepoDir $repoDir
    Write-Step "Publishing self-contained win-x64 candidate"
    Invoke-Native -FilePath "dotnet" -ArgumentList @(
        "publish", $projectFile,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-o", $candidateDir
    )
    if (-not (Test-Path -LiteralPath (Join-Path $candidateDir 'DayNote.exe') -PathType Leaf)) {
        throw 'The build did not produce DayNote.exe. The previous published build is unchanged.'
    }

    Write-Step "Requesting closure of the existing DayNote runtime"
    Request-RuntimeClose -Label "DayNote" -RepoDir $repoDir -BuiltExecutable $exePath -Token $runtimeToken
    Assert-LauncherRuntimeOwner -Token $runtimeToken -RepoDir $repoDir
    Write-Step "Publishing the validated build"
    Publish-RuntimeCandidate -CandidateDir $candidateDir -PublishDir $publishDir

    Write-Step "Launching DayNote"
    Assert-LauncherRuntimeOwner -Token $runtimeToken -RepoDir $repoDir
    Start-Process -FilePath $exePath | Out-Null
    Wait-RuntimeReady -Label "DayNote" -RepoDir $repoDir -BuiltExecutable $exePath -Token $runtimeToken -Mode Built -TimeoutSeconds 30

}
catch {
    Write-Host ""
    Write-Host "daynote rebuild failed: $($_.Exception.Message)" -ForegroundColor Red
    $scriptExitCode = 1
}
finally {
    if (Test-Path -LiteralPath $candidateDir) {
        Remove-Item -LiteralPath $candidateDir -Recurse -Force
    }
    if (Get-Command Release-LauncherRuntime -ErrorAction SilentlyContinue) {
        Release-LauncherRuntime -Token $runtimeToken -RepoDir $repoDir
    }
    Read-Host "Press Enter to close" | Out-Null
}

exit $scriptExitCode
