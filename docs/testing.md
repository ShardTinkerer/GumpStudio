# Testing

```sh
dotnet build GumpStudio.slnx
pwsh eng/run-tests.ps1
```

Tests run on **Microsoft.Testing Platform** (xunit.v3). .NET 10 removed the
VSTest bridge, so the opt-in lives in `global.json`:

```json
"test": { "runner": "Microsoft.Testing.Platform" }
```

## Why not `dotnet test`

MTP test projects are self-executing applications, and `eng/run-tests.ps1`
launches each one directly. That is a workaround, not a style choice:

> On SDK **10.0.400**, `dotnet test` reports `Zero tests ran` (exit code 5) for
> every project in this solution, in about 150 ms - the child never gets as far
> as discovery. The same executables run their full suites correctly when
> launched directly. The failure reproduces with a **one-file xunit.v3 project
> in an empty directory**, on both xunit.v3 4.0.0 and 3.1.0, so it is a
> toolchain defect rather than anything about this repository.

The script forwards `--report-trx`, `--results-directory` and `--coverage`, so
CI keeps the same artifacts it had before. Re-test `dotnet test` after an SDK
bump; if it starts working, the script can go.

Exit code 8 (every test skipped or filtered out) is treated as success, since
that is what a client-data-only project would return on a machine with no UO
installation.

## Testing against real UO clients

Client files are not redistributable, so they are never checked in and CI never
has them. Tests that need one are **skipped, not failed**.

| Variable | Purpose |
|---|---|
| `GUMPSTUDIO_TEST_CLIENT` | A classic MUL-era installation |
| `GUMPSTUDIO_TEST_CLIENT_UOP` | A UOP-era installation |
| `GUMPSTUDIO_TEST_CLIENT_ROOT` | A folder holding many client versions, each discovered automatically |

```powershell
$env:GUMPSTUDIO_TEST_CLIENT     = "C:\...\Ultima Online Mondain's Legacy"
$env:GUMPSTUDIO_TEST_CLIENT_UOP = "C:\...\Ultima Online Classic"
$env:GUMPSTUDIO_TEST_CLIENT_ROOT= "F:\UOClients"
pwsh eng/run-tests.ps1
```

A variable pointing at a nonexistent directory throws rather than silently
skipping, so a typo does not quietly disable coverage.

Discovery searches two levels deep for a directory containing `art.mul` or
`artLegacyMUL.uop`, then filters to those that pass `UoDataContext.Validate`.
Installations that fail validation are still used — by a test asserting that the
missing pieces are reported clearly.

### Why a matrix

The formats changed repeatedly between 2001 and now. Two clients are not enough:
testing against 14 caught two defects that a MUL/UOP pair did not.

Coverage worth having spans MUL-only → mixed → UOP-only gumps, all three
tiledata sizes in both flag layouts, 3 / 7 / 13 unicode font files, and plain
versus MegaCliloc-wrapped clilocs.

Note that results depend on the contents of that folder. Adding a client that is
broken in a new way will surface as a test failure — which is the point, but it
does mean the suite is not hermetic when the root variable is set.

## What is covered without a client

Synthetic fixtures in `GumpStudio.TestSupport` build byte-exact `.idx`/`.mul`
pairs, `verdata.mul` patch sets and `.uop` packages, covering container parsing,
missing and truncated entries, verdata overrides, block-chain walking,
compression and the gump dimension prefix.

The UOP path hash is covered by known-answer tests using values read out of a
retail package, so the algorithm stays pinned with no client present. This
matters because a synthetic package built by our own writer would share any
mistake in the hash and still round-trip perfectly.

**Gap:** the Burrows-Wheeler decoder is exercised only by real-client tests.
Closing it needs either a BWT encoder written purely for fixtures, or a small
captured input/output pair checked in.

## Manual end-to-end check

```sh
dotnet run --project source/GumpStudio.Cli -- info --client "<path>"
dotnet run --project source/GumpStudio.Cli -- dump --client "<path>" --gump 5 --out gump5.png
dotnet run --project source/GumpStudio.Cli -- dump --client "<path>" --item 0x0E75 --hue 33 --out pack.png
```

`info` prints what an installation contains and which tiledata layout it uses.
Dumping the same gump id from a MUL client and a UOP client should produce
byte-identical PNGs; that single check exercises the container, codec, decoder
and colour pipeline together.
