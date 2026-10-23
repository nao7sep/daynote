Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$scriptExitCode = 0

# run-dev: run the app from source with live reload, in its loosest configuration.
# For active coding and debugging. The strict, production-faithful launchers are
# run-built (launch the existing packaged app bundle without rebuilding) and
# rebuild (build and package a fresh bundle, then launch).

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
$builtExecutable = Join-Path $repoDir "bin/Release/net10.0/win-x64/publish/DayNote.exe"
$runtimeToken = [guid]::NewGuid().ToString("N")
$devProcess = $null

try {
    Set-Utf8Console
    Import-Module (Join-Path $scriptDir "launcher-runtime.psm1") -Force
    Require-Command dotnet

    Set-Location $repoDir

    Claim-LauncherRuntime -Token $runtimeToken -RepoDir $repoDir
    Write-Step "Restoring packages required for launch"
    Invoke-Native -FilePath "dotnet" -ArgumentList @("restore", $projectFile)
    Write-Step "Requesting closure of the existing DayNote runtime"
    Request-RuntimeClose -Label "DayNote" -RepoDir $repoDir -BuiltExecutable $builtExecutable -Token $runtimeToken
    # The running development app can lock the existing build output on Windows.
    Write-Step "Building DayNote (Debug)"
    Invoke-Native -FilePath "dotnet" -ArgumentList @("build", $projectFile, "--no-restore")

    Write-Step "Starting DayNote (Debug, from source)"
    Assert-LauncherRuntimeOwner -Token $runtimeToken -RepoDir $repoDir
    $devProcess = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--no-build", "--no-restore", "--project", "`"$projectFile`"") -NoNewWindow -PassThru
    Wait-RuntimeReady -Label "DayNote" -RepoDir $repoDir -BuiltExecutable $builtExecutable -Token $runtimeToken -Mode Development -StartedProcess $devProcess -TimeoutSeconds 120
    Write-Step "DayNote is ready"
    $devProcess.WaitForExit()
    if ($devProcess.ExitCode -notin @(0, 130, -1073741510)) {
        throw "DayNote development runtime failed with exit code $($devProcess.ExitCode)."
    }
}
catch {
    Write-Host ""
    Write-Host "daynote run-dev failed: $($_.Exception.Message)" -ForegroundColor Red
    $scriptExitCode = 1
}
finally {
    if ($null -ne $devProcess) { $devProcess.Dispose() }
    if (Get-Command Release-LauncherRuntime -ErrorAction SilentlyContinue) {
        Release-LauncherRuntime -Token $runtimeToken -RepoDir $repoDir
    }
    Read-Host "Press Enter to close" | Out-Null
}

exit $scriptExitCode
