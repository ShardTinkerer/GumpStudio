#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes GumpStudio as a self-contained NativeAOT executable.

.DESCRIPTION
    Produces a single native binary with no .NET runtime to install and no
    managed assemblies beside it, next to the native SkiaSharp, HarfBuzz and
    ANGLE libraries Avalonia needs. PublishAot implies a self-contained
    publish, so --self-contained is not passed and would add nothing.

    This runs the same code as an ordinary build. It did not always: the
    exporters used to arrive as plugin assemblies, which a NativeAOT image
    cannot load at all, so that build needed a second registration path behind
    an #if. The converters are referenced normally now and there is no
    difference left to accommodate.

.PARAMETER Runtime
    Runtime identifier, for example win-x64, linux-x64 or osx-arm64. Defaults to
    the current machine's.

.PARAMETER Output
    Where to write the published files.
#>
[CmdletBinding()]
param(
    [string] $Runtime,
    [string] $Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Runtime) {
    $Runtime = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
}

if (-not $Output) {
    $Output = Join-Path $repoRoot "artifacts/aot/$Runtime"
}

if ($IsWindows) {
    # The ILCompiler shells out to vswhere to locate the MSVC linker. It lives in
    # a fixed place that is not on PATH outside a developer prompt, and without
    # it the link step fails with "'vswhere.exe' is not recognized" long after
    # compilation has succeeded.
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'

    if ((Test-Path (Join-Path $installer 'vswhere.exe')) -and $env:PATH -notlike "*$installer*") {
        $env:PATH = "$installer;$env:PATH"
    }

    if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
        throw 'vswhere.exe was not found. NativeAOT on Windows needs the MSVC ' +
              'toolchain: install the "Desktop development with C++" workload.'
    }
}

Write-Host "Publishing NativeAOT for $Runtime to $Output" -ForegroundColor Cyan

dotnet publish (Join-Path $repoRoot 'source/GumpStudio.App') `
    --configuration Release `
    --runtime $Runtime `
    -p:PublishAot=true `
    --output $Output `
    --nologo

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$binary = Get-ChildItem $Output -File |
    Where-Object { $_.Name -like 'GumpStudio.App*' -and $_.Extension -in '', '.exe' } |
    Select-Object -First 1

if ($binary) {
    Write-Host ("Native binary: {0} ({1:N1} MB)" -f $binary.Name, ($binary.Length / 1MB)) -ForegroundColor Green
}
