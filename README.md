# GumpStudio Resurrection

A gump editor for Ultima Online, originally written in VB.NET by Bradley Uffner
in 2004.

Built on **.NET 10** with an **Avalonia** UI, running on Windows, Linux and
macOS. It reads both classic `.mul` and modern `.uop` client data, so it works
with clients from 2001 through to current. You can place elements, edit their
properties, save, import a gump captured off the wire, and export raw client
layout, POL, RunUO and Sphere.

> Not everything the 2004 application could do is here yet.
> [docs/architecture.md](docs/architecture.md) lists the known gaps.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build GumpStudio.slnx
pwsh build/run-tests.ps1
```

`dotnet test` reports "Zero tests ran" on SDK 10.0.400, which is a toolchain
defect rather than anything about this repository. `build/run-tests.ps1`
launches the test applications directly instead — see
[docs/testing.md](docs/testing.md).

## Running

```sh
dotnet run --project source/GumpStudio.App
```

On first run the application asks for your Ultima Online folder and remembers
it. Any client from 2001 onward should work; it tells you what is missing if a
folder is not usable.

There is also a headless CLI for the same data layer — inspecting a client,
writing art to PNG, rendering a saved document, and running the four export
converters without the UI:

```sh
dotnet run --project source/GumpStudio.Cli -- info --client "C:/path/to/UO"
```

Run it with no arguments for the full usage, including the export formats and
their dialects.

## Releases

Published on the [releases page](https://github.com/ShardTinkerer/GumpStudio/releases)
as one archive per platform. Each holds the editor and the `gumpstudio` CLI as
NativeAOT binaries: unpack and run, with no .NET runtime to install.

A release is cut by pushing a version tag — `2.0.0-alpha.1`, `2.1.0`, no `v`
prefix. The tag is the version: it is stamped into the binaries and names the
archives, and a tag containing `-` publishes as a prerelease.

To build one locally:

```sh
pwsh build/publish-aot.ps1 -Runtime win-x64
```

NativeAOT cannot cross-compile, so each platform is built on its own machine and
needs a native toolchain: on Windows the MSVC "Desktop development with C++"
workload, elsewhere clang and zlib's headers.

## Repository layout

```
source/          the application
tests/           tests, including a matrix run against real clients
build/           the scripts CI runs — tests and NativeAOT publishing
docs/            architecture, testing, file format, assets
artifacts/       every build's output, and the only directory a build writes
```

`artifacts/` is the .NET SDK's `UseArtifactsOutput` root, set in
`Directory.Build.props`. There is no `bin` or `obj` beside a project, so
deleting that one directory cleans the repository completely.

## Documentation

| Document | What it covers |
|---|---|
| [docs/architecture.md](docs/architecture.md) | The six projects, the reasoning behind them, and the known gaps |
| [docs/gump-commands.md](docs/gump-commands.md) | The client's layout commands and how the editor models each one |
| [docs/testing.md](docs/testing.md) | Running the tests, including against real clients |
| [docs/legacy-gump-format.md](docs/legacy-gump-format.md) | How 1.8 saved `.gump` and `.gumpling`, and how they are read now |
| [docs/uop-format.md](docs/uop-format.md) | The `.uop` container and the MegaCliloc codec inside it |
| [docs/assets.md](docs/assets.md) | The 1.8 artwork, and how it was recovered |

## Credits

Gump Studio was designed and written by Bradley Uffner in 2004. It made
extensive use of a modified UOSDK written by Krrios. Artwork was created by
Melanius, and several ideas were contributed by the RunUO community. Thanks go
to DarkStorm of the Wolfpack emulator for help decoding `unifont.mul`.

The POL exporter derives from work by Fernando Rozenblit, itself based on the
Sphere exporter by Francesco Furiani and Mark Chandler. The Sphere exporter is a
port of Francesco Furiani's, and the RunUO exporter of roadmaster / Mark
Sweetman's, itself based on Daegon / Eric Brown's.

The `.uop` container and its MegaCliloc compression stage were reverse
engineered from the client binary and checked against retail files;
[docs/uop-format.md](docs/uop-format.md) records the format and how each part of
it was confirmed.

## Licence

MIT, in [LICENSE](LICENSE). It covers the code in this repository: the 2004
original carried no stated licence and its source is lost, so nothing here can
be a grant on its author's behalf. The two recovered artwork assets and the exporters' lineage are
recorded in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), which ships in every release
archive.
