function Get-LauncherOwnerFile {
    param([Parameter(Mandatory = $true)][string]$RepoDir)
    return Join-Path (Join-Path $RepoDir '.git') 'launcher-runtime-owner'
}

function Claim-LauncherRuntime {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$RepoDir
    )
    $ownerFile = Get-LauncherOwnerFile -RepoDir $RepoDir
    $temporaryFile = "$ownerFile.$PID.$([guid]::NewGuid().ToString('N'))"
    [System.IO.File]::WriteAllText($temporaryFile, "$Token`n", [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryFile -Destination $ownerFile -Force
}

function Test-LauncherRuntimeOwner {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$RepoDir
    )
    $ownerFile = Get-LauncherOwnerFile -RepoDir $RepoDir
    return (Test-Path -LiteralPath $ownerFile) -and
        ((Get-Content -LiteralPath $ownerFile -Raw).Trim() -ceq $Token)
}

function Release-LauncherRuntime {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$RepoDir
    )
    if (Test-LauncherRuntimeOwner -Token $Token -RepoDir $RepoDir) {
        Remove-Item -LiteralPath (Get-LauncherOwnerFile -RepoDir $RepoDir) -Force -ErrorAction SilentlyContinue
    }
}

function Assert-LauncherRuntimeOwner {
    param([string]$Token, [string]$RepoDir)
    if (-not (Test-LauncherRuntimeOwner -Token $Token -RepoDir $RepoDir)) {
        throw 'A newer launcher has taken over. This launcher will not close or start DayNote; use the newer launcher.'
    }
}

function Get-RuntimeProcess {
    param(
        [Parameter(Mandatory = $true)][string]$RepoDir,
        [Parameter(Mandatory = $true)][string]$BuiltExecutable,
        [ValidateSet('Any', 'Built', 'Development')][string]$Mode = 'Any'
    )
    $paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if ($Mode -ne 'Development') {
        $null = $paths.Add([System.IO.Path]::GetFullPath($BuiltExecutable))
    }
    if ($Mode -ne 'Built') {
        $developmentBin = Join-Path $RepoDir 'src/DayNote/bin'
        if (Test-Path -LiteralPath $developmentBin -PathType Container) {
            foreach ($file in Get-ChildItem -LiteralPath $developmentBin -Filter 'DayNote.exe' -File -Recurse) {
                $null = $paths.Add([System.IO.Path]::GetFullPath($file.FullName))
            }
        }
    }
    foreach ($process in Get-Process) {
        $selected = $false
        try {
            try {
                $path = $process.Path
            } catch [System.ComponentModel.Win32Exception] {
                # An inaccessible process has no proven app identity and is never selected.
                continue
            } catch [System.InvalidOperationException] {
                # A process can exit while its executable identity is being read.
                continue
            }
            if ($path -and $paths.Contains([System.IO.Path]::GetFullPath($path))) {
                $selected = $true
                $process
            }
        } finally {
            if (-not $selected) { $process.Dispose() }
        }
    }
}

function Request-RuntimeClose {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$RepoDir,
        [Parameter(Mandatory = $true)][string]$BuiltExecutable,
        [Parameter(Mandatory = $true)][string]$Token
    )
    $selected = @(Get-RuntimeProcess -RepoDir $RepoDir -BuiltExecutable $BuiltExecutable)
    try {
        Assert-LauncherRuntimeOwner -Token $Token -RepoDir $RepoDir
        if ($selected.Count -eq 0) { return }
        Write-Host "Requesting ordinary closure of $Label (pid $($selected.Id -join ', '))."
        $elapsed = [System.Diagnostics.Stopwatch]::StartNew()
        foreach ($process in $selected) {
            if ($process.HasExited) { continue }
            try {
                $requested = $process.CloseMainWindow()
            } catch {
                if ($process.HasExited) { continue }
                throw "Could not ask $Label to close. Close it normally and run this launcher again. $($_.Exception.Message)"
            }
            if (-not $requested -and -not $process.HasExited) {
                throw "$Label could not accept a close request. Close it normally and run this launcher again."
            }
        }
        do {
            $remaining = @($selected | Where-Object { -not $_.HasExited })
            if ($remaining.Count -eq 0) { return }
            Start-Sleep -Milliseconds 100
        } while ($elapsed.Elapsed.TotalSeconds -lt 5)
        throw "$Label is still open, possibly waiting for an answer or unfinished work. Finish closing it normally and run this launcher again. Nothing new was launched."
    } finally {
        foreach ($process in $selected) { $process.Dispose() }
    }
}

function Wait-RuntimeReady {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$RepoDir,
        [Parameter(Mandatory = $true)][string]$BuiltExecutable,
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][ValidateSet('Built', 'Development')][string]$Mode,
        [object]$StartedProcess,
        [int]$TimeoutSeconds = 30
    )
    $elapsed = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-LauncherRuntimeOwner -Token $Token -RepoDir $RepoDir
        if ($null -ne $StartedProcess -and $StartedProcess.HasExited) {
            throw "$Label exited before it was ready (exit code $($StartedProcess.ExitCode)). Check the launch output and try again."
        }
        $selected = @(Get-RuntimeProcess -RepoDir $RepoDir -BuiltExecutable $BuiltExecutable -Mode $Mode)
        try {
            if (@($selected | Where-Object { -not $_.HasExited }).Count -gt 0) { return }
        } finally {
            foreach ($process in $selected) { $process.Dispose() }
        }
        Start-Sleep -Milliseconds 100
    } while ($elapsed.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw "$Label did not start in $Mode mode within ${TimeoutSeconds}s."
}

function Publish-RuntimeCandidate {
    param([string]$CandidateDir, [string]$PublishDir)
    $previous = "$PublishDir.previous.$([guid]::NewGuid().ToString('N'))"
    $hadPrevious = Test-Path -LiteralPath $PublishDir
    if ($hadPrevious) {
        Move-Item -LiteralPath $PublishDir -Destination $previous
    }
    try {
        Move-Item -LiteralPath $CandidateDir -Destination $PublishDir
    } catch {
        if ($hadPrevious) {
            Move-Item -LiteralPath $previous -Destination $PublishDir
        }
        throw
    }
    if ($hadPrevious) {
        Remove-Item -LiteralPath $previous -Recurse -Force
    }
}

Export-ModuleMember -Function Claim-LauncherRuntime, Test-LauncherRuntimeOwner, Assert-LauncherRuntimeOwner, Release-LauncherRuntime, Get-RuntimeProcess, Request-RuntimeClose, Wait-RuntimeReady, Publish-RuntimeCandidate
