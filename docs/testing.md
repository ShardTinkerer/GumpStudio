# Testing

```sh
dotnet test GumpStudio.slnx
```

Tests run on **Microsoft.Testing Platform** (xunit.v3). .NET 10 removed the
VSTest bridge, so the opt-in lives in `global.json`:

```json
"test": { "runner": "Microsoft.Testing.Platform" }
```

CI uses `--report-trx --coverage`, not the old `--logger` / `--collect` flags.

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
dotnet test GumpStudio.slnx
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
