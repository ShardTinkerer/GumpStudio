# GumpStudio rewrite — documentation

The rewrite targets **.NET 10** with an **Avalonia** cross-platform UI, replacing
the decompiled .NET Framework 4.8 WinForms port that lives in `src/`.

| Document | What it covers |
|---|---|
| [status.md](status.md) | What is finished, what is next, and the known gaps |
| [architecture.md](architecture.md) | Project layout and the reasoning behind it |
| [uo-file-formats.md](uo-file-formats.md) | Verified notes on the client data formats |
| [testing.md](testing.md) | Running the tests, including against real UO clients |

## Quick start

```sh
dotnet build GumpStudio.slnx
dotnet test  GumpStudio.slnx
```

Point the headless tool at a UO installation to check the data layer end to end:

```sh
dotnet run --project source/GumpStudio.Cli -- info --client "C:/path/to/UO"
dotnet run --project source/GumpStudio.Cli -- dump --client "C:/path/to/UO" --gump 5 --out gump5.png
```

## Repository layout

```
source/          the rewrite
tests/           test projects, including the real-client matrix
src/             the legacy net48 port — reference only, deleted at the end
external/        original 1.8 binaries, used as a behaviour reference (untracked)
docs/            this directory
```

`src/` is shielded from the root build configuration by an empty
`src/Directory.Build.props`, so the two trees can coexist until the rewrite
reaches parity.
