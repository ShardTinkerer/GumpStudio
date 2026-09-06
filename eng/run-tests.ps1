#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs every test project's Microsoft.Testing.Platform executable.

.DESCRIPTION
    xunit.v3 test projects are self-executing MTP applications, so they are
    invoked directly rather than through `dotnet test`.

    This is not merely a preference. On SDK 10.0.400 the `dotnet test` MTP
    integration reports "Zero tests ran" (exit 5) for every project while the
    same executables discover and run their tests correctly when launched
    directly. The failure reproduces with a one-file xunit.v3 project outside
    this repository, so it is a toolchain defect, not a project misconfiguration.

.PARAMETER Configuration
    Build configuration to run. Defaults to Debug.

.PARAMETER ResultsDirectory
    Where to write TRX reports. Omit to skip reporting.

.PARAMETER Coverage
    Also collect code coverage.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [string] $ResultsDirectory,
    [switch] $Coverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$testsRoot = Join-Path $repoRoot 'tests'

$projects = Get-ChildItem -Path $testsRoot -Filter '*.Tests.csproj' -Recurse -File

if ($projects.Count -eq 0) {
    throw "No test projects found under $testsRoot."
}

$suffix = $IsWindows ? '.exe' : ''
$failed = @()

foreach ($project in $projects | Sort-Object Name) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project.Name)
    $exe = Join-Path $project.DirectoryName "bin/$Configuration/net10.0/$name$suffix"

    if (-not (Test-Path $exe)) {
        throw "Missing test executable '$exe'. Build the solution first."
    }

    $arguments = @()

    if ($ResultsDirectory) {
        $arguments += @('--report-trx', '--results-directory', $ResultsDirectory)
    }

    if ($Coverage) {
        $arguments += '--coverage'
    }

    Write-Host "==> $name" -ForegroundColor Cyan

    & $exe @arguments

    # 0 = all passed, 8 = every test was skipped or filtered out. Both are green
    # here, because the client-data theories skip when no UO installation is
    # configured, which is always the case on CI.
    if ($LASTEXITCODE -notin 0, 8) {
        $failed += "$name (exit $LASTEXITCODE)"
    }
}

if ($failed.Count -gt 0) {
    Write-Host ''
    Write-Host "Failed: $($failed -join ', ')" -ForegroundColor Red

    exit 1
}

Write-Host ''
Write-Host 'All test projects passed.' -ForegroundColor Green
