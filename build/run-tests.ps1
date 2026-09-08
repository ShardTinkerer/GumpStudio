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

$failed = @()

foreach ($project in $projects | Sort-Object Name) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project.Name)

    # Asked of MSBuild rather than assembled from a convention. The output path
    # is UseArtifactsOutput's to decide and the target framework lives in
    # Directory.Build.props, so a hardcoded path couples this script to both and
    # breaks with a "missing executable" long after either one moves.
    $targetPath = & dotnet msbuild $project.FullName -getProperty:TargetPath `
        -p:Configuration=$Configuration -nologo

    if ($LASTEXITCODE -ne 0) {
        throw "Could not read TargetPath for $name. Restore the solution first."
    }

    # Rebuilt from the parts rather than through ChangeExtension: binding $null
    # to its [string] parameter makes PowerShell pass an empty string, which
    # strips the ".dll" but leaves the dot, and the executable is extensionless
    # everywhere except Windows.
    $target = $targetPath.Trim()

    $exe = Join-Path ([IO.Path]::GetDirectoryName($target)) (
        [IO.Path]::GetFileNameWithoutExtension($target) + ($IsWindows ? '.exe' : ''))

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
