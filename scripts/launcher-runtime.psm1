# Shared by run-dev, run-built and rebuild: one DayNote per repository. The launcher that starts DayNote
# records its process ID and executable under .git; the next launcher asks that process to close only
# while it still runs that executable, and stops with a message rather than force-closing it when it
# stays open, for example while it asks about unsaved work. A DayNote started any other way is left to
# its single-instance behavior, which brings its window forward instead of opening a second one.

$singleInstanceNote = 'If DayNote was already running outside these launchers, its window is brought forward instead.'

function Get-RuntimeRecordFile {
    param([Parameter(Mandatory = $true)][string]$RepoDir)
    return Join-Path (Join-Path $RepoDir '.git') 'launcher-runtime-process'
}

function Save-RuntimeRecord {
    param([string]$RepoDir, [int]$ProcessId, [string]$Executable)
    [System.IO.File]::WriteAllText((Get-RuntimeRecordFile -RepoDir $RepoDir), "$ProcessId`n$Executable`n", [System.Text.UTF8Encoding]::new($false))
}

# The recorded process ID, returned only while that process still runs the recorded executable.
function Get-RecordedRuntimeId {
    param([Parameter(Mandatory = $true)][string]$RepoDir)
    $recordFile = Get-RuntimeRecordFile -RepoDir $RepoDir
    if (-not (Test-Path -LiteralPath $recordFile)) { return $null }
    $lines = @(Get-Content -LiteralPath $recordFile)
    if ($lines.Count -lt 2 -or $lines[0] -notmatch '^\d+$' -or -not $lines[1]) { return $null }
    $process = Get-Process -Id ([int]$lines[0]) -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    try {
        if ($process.Path -and $process.Path -ieq $lines[1]) { return $process.Id }
        return $null
    } finally {
        $process.Dispose()
    }
}

# Asks the recorded DayNote to close as a user would, so it can still finish or ask about unsaved work.
function Stop-RecordedRuntime {
    param([Parameter(Mandatory = $true)][string]$RepoDir)
    $id = Get-RecordedRuntimeId -RepoDir $RepoDir
    if ($null -eq $id) { return }
    Write-Host "Asking the running DayNote (pid $id) to close."
    $process = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        try { $null = $process.CloseMainWindow() } finally { $process.Dispose() }
    }
    $elapsed = [System.Diagnostics.Stopwatch]::StartNew()
    while ($elapsed.Elapsed.TotalSeconds -lt 5) {
        if ((Get-RecordedRuntimeId -RepoDir $RepoDir) -ne $id) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'DayNote is still open, perhaps finishing or asking about unsaved work. Close it, then run this launcher again.'
}

function Start-RecordedRuntime {
    param([Parameter(Mandatory = $true)][string]$RepoDir, [Parameter(Mandatory = $true)][string]$Executable)
    $fullPath = [System.IO.Path]::GetFullPath($Executable)
    $process = Start-Process -FilePath $fullPath -PassThru
    try { Save-RuntimeRecord -RepoDir $RepoDir -ProcessId $process.Id -Executable $fullPath } finally { $process.Dispose() }
    Write-Host $singleInstanceNote
}

# Records the DayNote that `dotnet run` (the wrapper) starts from the development build, once it
# appears. Fails when the wrapper ends first, as it does when the build fails.
function Save-WrappedRuntime {
    param([Parameter(Mandatory = $true)][string]$RepoDir, [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Wrapper)
    $developmentBin = [System.IO.Path]::GetFullPath((Join-Path $RepoDir 'src/DayNote/bin')) + [System.IO.Path]::DirectorySeparatorChar
    while ($true) {
        if ($Wrapper.HasExited) { throw 'The DayNote development runtime ended before the app started.' }
        $processes = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, ExecutablePath)
        $descendants = [System.Collections.Generic.HashSet[int]]::new()
        $null = $descendants.Add($Wrapper.Id)
        do {
            $grown = $false
            foreach ($item in $processes) {
                if ($descendants.Contains([int]$item.ParentProcessId) -and $descendants.Add([int]$item.ProcessId)) { $grown = $true }
            }
        } while ($grown)
        foreach ($item in $processes) {
            $path = $item.ExecutablePath
            if ($descendants.Contains([int]$item.ProcessId) -and $path -and
                $path.StartsWith($developmentBin, [System.StringComparison]::OrdinalIgnoreCase) -and
                [System.IO.Path]::GetFileName($path) -ieq 'DayNote.exe') {
                Save-RuntimeRecord -RepoDir $RepoDir -ProcessId $item.ProcessId -Executable $path
                Write-Host $singleInstanceNote
                return
            }
        }
        Start-Sleep -Milliseconds 200
    }
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

Export-ModuleMember -Function Stop-RecordedRuntime, Start-RecordedRuntime, Save-WrappedRuntime, Publish-RuntimeCandidate
