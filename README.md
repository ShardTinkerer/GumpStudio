# Gump Studio

A gump editor for Ultima Online, originally written in VB.NET by Bradley Uffner
in 2004.

This repository holds a rewrite on **.NET 10** with an **Avalonia** UI, running
on Windows, Linux and macOS. It reads both classic `.mul` and modern `.uop`
client data, so it works with clients from 2001 through to current.

> **Status: alpha.** The editor is usable — you can place elements, edit their
> properties, save, and export POL scripts — but several conveniences from the
> old application are not built yet. See [docs/status.md](docs/status.md) for
> exactly what is and is not done.

## Why a rewrite

The original VB.NET source was lost. A previous effort decompiled the shipped
binary and converted it to C#; that port lives in `src/` and is kept as a
reference. It could not move forward:

- The `.gump` file format, the plugin configuration, the art caches and even
  copy/paste all went through `BinaryFormatter`, which is removed from .NET 9+.
- Every piece of behaviour lived in one 1988-line `DesignerForm`, with no
  document model to test or reuse.
- Its Ultima SDK fork was from around 2008 and had no `.uop` support, so it could
  not read any client newer than roughly 7.0.24. Modern clients ship no
  `gumpart.mul` at all.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build GumpStudio.slnx
dotnet test  GumpStudio.slnx
dotnet run --project source/GumpStudio.App
```

On first run the application asks for your Ultima Online folder and remembers
it. Any client from 2001 onward should work; it tells you what is missing if a
folder is not usable.

## Headless tooling

```sh
# What does this client contain?
gumpstudio info --client "C:/path/to/UO"

# Write a gump, an item or a land tile to PNG
gumpstudio dump --client "C:/path/to/UO" --gump 5 --out gump5.png
gumpstudio dump --client "C:/path/to/UO" --item 0x0E75 --hue 33 --out pack.png

# Render a saved document with real client art
gumpstudio render --client "C:/path/to/UO" --in mygump.gump --out mygump.png

# Export a POL script
gumpstudio export --in mygump.gump --name MyGump --style pkg
```

## Repository layout

```
source/          the rewrite
tests/           tests, including a matrix run against real clients
src/             the legacy net48 port — reference only
external/        original 1.8 binaries, used as a behaviour reference
docs/            architecture, status, file formats, testing
```

## Documentation

| Document | What it covers |
|---|---|
| [docs/status.md](docs/status.md) | What is finished, what is not, and the known gaps |
| [docs/architecture.md](docs/architecture.md) | Project layout and the reasoning behind it |
| [docs/uo-file-formats.md](docs/uo-file-formats.md) | Verified notes on the client data formats |
| [docs/testing.md](docs/testing.md) | Running the tests, including against real clients |

The file-format notes are worth a look even if you are not working on this
project: they document the Burrows-Wheeler stage modern `.uop` gump art uses,
and the fact that the MegaCliloc wrapper is the same transform. Both were
verified against retail clients rather than taken from existing documentation,
which turned out to describe a different client build.

## Credits

Gump Studio was designed and written by Bradley Uffner in 2004. It made
extensive use of a modified UOSDK written by Krrios. Artwork was created by
Melanius, and several ideas were contributed by the RunUO community. Thanks go
to DarkStorm of the Wolfpack emulator for help decoding `unifont.mul`.

The POL exporter derives from work by Fernando Rozenblit, itself based on the
Sphere exporter by Francesco Furiani and Mark Chandler.

The `.uop` container and its Burrows-Wheeler stage were understood with
reference to the [ClassicUO](https://github.com/ClassicUO/ClassicUO) project
(BSD-2-Clause).
