# GumpStudio Resurrection — documentation

Built on **.NET 10** with an **Avalonia** cross-platform UI.

| Document | What it covers |
|---|---|
| [architecture.md](architecture.md) | The six projects, the reasoning behind them, and the known gaps |
| [gump-commands.md](gump-commands.md) | The client's layout commands and how the editor models each one |
| [testing.md](testing.md) | Running the tests, including against real UO clients |
| [legacy-gump-format.md](legacy-gump-format.md) | How 1.8 saved `.gump` and `.gumpling`, and how they are read now |
| [uop-format.md](uop-format.md) | The `.uop` container and the MegaCliloc codec inside it |
| [assets.md](assets.md) | The 1.8 artwork, and how it was recovered |

## Quick start

```sh
dotnet build GumpStudio.slnx
pwsh build/run-tests.ps1
```

`dotnet test` is not the way in: it reports "Zero tests ran" on SDK 10.0.400.
[testing.md](testing.md) explains why and what to retry after an SDK bump.

Point the headless tool at a UO installation to check the data layer end to end:

```sh
dotnet run --project source/GumpStudio.Cli -- info --client "C:/path/to/UO"
dotnet run --project source/GumpStudio.Cli -- dump --client "C:/path/to/UO" --gump 5 --out gump5.png
```

## Repository layout

```
source/          the application
tests/           test projects, including the real-client matrix
build/           the scripts CI runs — tests and NativeAOT publishing
docs/            this directory
artifacts/       every build's output, and the only directory a build writes
```

Output is centralised by `UseArtifactsOutput` in the root
`Directory.Build.props`: no project has a `bin` or `obj` beside it, so
`artifacts/` is the whole of what a build leaves behind.

The 2004 binaries this editor's behaviour was checked against are **not** in the
repository — they are not redistributable, and `external/` is in `.gitignore` so
that a local copy under that name can never be committed.
