#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes GumpStudio as self-contained NativeAOT executables.

.DESCRIPTION
    Produces native binaries with no .NET runtime to install and no managed
    assemblies beside them, next to the native SkiaSharp, HarfBuzz and ANGLE
    libraries Avalonia needs. PublishAot implies a self-contained publish, so
    --self-contained is not passed and would add nothing.

    Both the editor and the headless CLI are published into the same folder:
    that folder is what a release archive is made from, and the README documents
    the CLI, so shipping one without the other makes those docs a lie. They
    share the native payload, so the second publish rewrites identical files.

    NativeAOT cannot cross-compile. Runtime has to match the machine this runs
    on, which is why the release workflow has one runner per RID.

    Symbols and XML documentation are turned off rather than deleted afterwards.
    Left on, the doc files for five projects outweigh the binaries they describe.

.PARAMETER Runtime
    Runtime identifier, for example win-x64, linux-x64 or osx-arm64. Defaults to
    the current machine's.

.PARAMETER Version
    Version to stamp into the binaries, normally the release tag. Defaults to
    the repository's own version from Directory.Build.props.

.PARAMETER Output
    Where to write the published files.
#>
[CmdletBinding()]
param(
    [string] $Runtime,
    [string] $Version,
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
else {
    # The same trap on the other side: ILC links through clang, and without it
    # the failure arrives as a bare non-zero exit from a tool nobody named.
    if (-not (Get-Command clang -ErrorAction SilentlyContinue)) {
        throw 'clang was not found. NativeAOT needs a native toolchain: ' +
              'install clang and the zlib development headers (on Debian and ' +
              'Ubuntu, clang and zlib1g-dev).'
    }
}

# Anything left from an earlier run would end up in the archive. A stale binary
# from a RID that is no longer published is the one that would go unnoticed.
if (Test-Path $Output) {
    Remove-Item $Output -Recurse -Force
}

$common = @(
    '--configuration', 'Release'
    '--runtime', $Runtime
    '--output', $Output
    '--nologo'
    '-p:PublishAot=true'
    '-p:DebugType=none'
    '-p:GenerateDocumentationFile=false'
)

if ($Version) {
    $common += "-p:Version=$Version"
}

$projects = 'source/GumpStudio.App', 'source/GumpStudio.Cli'

$label = if ($Version) { "$Version " } else { '' }

Write-Host "Publishing NativeAOT $label($Runtime) to $Output" -ForegroundColor Cyan

foreach ($project in $projects) {
    dotnet publish (Join-Path $repoRoot $project) @common

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

# ILC emits a native symbol file whatever DebugType says.
Get-ChildItem $Output -Filter '*.pdb' -File -Recurse | Remove-Item -Force

# Wrapped, because one match is a bare FileInfo and no match is $null, and
# Set-StrictMode turns reading .Count off the latter into an error.
$binaries = @(Get-ChildItem $Output -File |
    Where-Object { $_.Name -in 'GumpStudio.App', 'GumpStudio.App.exe', 'gumpstudio', 'gumpstudio.exe' })

foreach ($binary in $binaries) {
    Write-Host ("Native binary: {0} ({1:N1} MB)" -f $binary.Name, ($binary.Length / 1MB)) -ForegroundColor Green
}

if ($binaries.Count -ne $projects.Count) {
    throw "Expected $($projects.Count) native binaries in $Output, found $($binaries.Count)."
}
