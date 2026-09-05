# Status and roadmap

Last updated at the end of Phase 1.

## Why the rewrite exists

`src/` holds a decompiled-VB.NET-to-C# port of GumpStudio 1.8 (.NET Framework
4.8, WinForms, ~18k lines). It is structurally stuck:

- **It cannot move to modern .NET as-is.** The `.gump` and `.gumpling` formats,
  the plugin load-order file, the art-browser caches and even copy/paste all go
  through `BinaryFormatter` (11 call sites), which is removed from .NET 9+.
- **All behaviour lives in one 1988-line `DesignerForm`** — drag, resize,
  selection, undo, clipboard, plugin loading, save/load, toolbox. There is no
  non-UI document model to reuse or test.
- **The Ultima data layer is a ~2008 SDK fork with no `.uop` support**, so it
  cannot read any client newer than roughly 7.0.24. About 70% of it (maps,
  animations, sounds, process-memory reading) is dead weight for a gump editor.

The original VB source is lost. `external/Gumpstudio1.8r2` holds the shipped
binaries, which are intact, unobfuscated VB.NET and decompile cleanly — so they
serve as the behaviour reference where the existing port looks wrong.

## Decisions

| Decision | Choice |
|---|---|
| Runtime | .NET 10 |
| UI | Avalonia 12, SkiaSharp rendering, Windows / Linux / macOS |
| Fidelity | Behaviour parity on the element set and POL output; internals, UI and file format redesigned freely |
| Save format | XML, versioned, decoupled from CLR type names |
| Legacy `.gump` | Read-only importer via `System.Formats.Nrbf`; `BinaryFormatter` is never enabled |
| Client data | `.mul` **and** `.uop` |
| Plugins | External-DLL model kept, but the contract is UI-agnostic |
| Exporters | **POL only.** RunUO exporter, RunUO importer, Sphere and Wolfpack are dropped |

## Phase 0 — Foundation ✅

`GumpStudio.slnx` at the repository root; new code in `source/` and `tests/`,
legacy `src/` untouched and shielded. Central package management,
`.editorconfig`, analyzers, CI on Windows and Ubuntu, seven projects and five
test projects, all building with `TreatWarningsAsErrors`.

Two additions beyond the original plan:

- **`BannedSymbols.txt` + BannedApiAnalyzers** makes the rewrite's rules build
  errors. `System.Drawing.*`, `BinaryFormatter`, and culture-sensitive
  `Parse`/`ToLower`/`ToUpper` all fail the build. Verified by deliberately
  writing each violation and watching `RS0030` fire.
- **Plugin deployment is wired up**, which the old solution never did — a fresh
  clone of it produced an app with an empty `Plugins` folder.

Toolchain notes worth keeping:

- Avalonia's current stable is **12.x**, not 11.
- `System.Text.Encoding.CodePages` is in-box on .NET 10; referencing it is an
  `NU1510` error.
- .NET 10 dropped the VSTest bridge. xunit.v3 runs on Microsoft.Testing
  Platform, opted into by a `"test"` block in `global.json`. Test projects are
  `OutputType=Exe`; CI uses `--report-trx --coverage`.
- The legacy tree needs two shields: an empty `src/Directory.Build.props`, plus
  `ImportDirectoryPackagesProps=false` because NuGet re-sets
  `ManagePackageVersionsCentrally` after that file is imported.

## Phase 1 — `GumpStudio.Uo` data layer ✅

Written from scratch rather than ported. Reads art, gumps, hues, tiledata,
clilocs and both font families from `.mul` and `.uop` containers, returning
plain pixel buffers with no imaging-framework dependency.

**Verified against 14 real client installations spanning 2001 to current.**
201 tests pass in ~42s; with no client configured, 34 pass and 17 skip so CI
stays green.

The format work is written up in [uo-file-formats.md](uo-file-formats.md). The
headline findings:

- UOP compression flag **3 is zlib followed by a Burrows-Wheeler transform**.
  Retail `gumpartLegacyMUL.uop` uses it for every entry, and modern clients ship
  no `gumpart.mul` at all — so without this stage gump art is unreadable on any
  current client.
- The **MegaCliloc** wrapper on modern cliloc files is *the same* transform, so
  one decoder serves both.
- The `client-decomp` docs describe a different client build and are wrong for
  the installed ones on three counts (hash algorithm, path extension,
  compression flags). Every claim was re-verified empirically.

Proof the pipeline is correct end to end: gump 5 dumped from a MUL client and
from a UOP client produces **byte-identical PNGs**.

## Phase 2 — `GumpStudio.Core` document model ✅

Done. 54 tests in `GumpStudio.Core.Tests`; 254 across the solution.

- `GumpDocument` / `GumpPage` / `Element` hierarchy replacing the untyped
  `ArrayList Stacks`. Elements are data-only: no rendering, no context menus, no
  reaching back into a global form reference.
- `HandleGeometry` is the single source of truth for handle placement, hit
  testing and resizing — replacing two divergent sets of magic numbers plus a
  260-line eight-case switch with one anchor-relative function.
- `Element.GetAbsolutePosition()` is the only way position is read.
- **Command-based undo** with merge support, so a drag or a run of arrow-key
  nudges is one entry. `Undo`/`Redo` are safe at both ends of the history rather
  than relying on menu state.
- **XML serialization** over an explicit name mapping. Type names on disk are
  stable `TypeName` values, so renaming a class cannot break a saved file;
  unknown element types are skipped rather than fatal.
- **Legacy importer** using `System.Formats.Nrbf`, which decodes the old
  `BinaryFormatter` payloads without ever activating a type.

Notes:

- `System.Formats.Nrbf` ships only in the WindowsDesktop shared framework, so
  the cross-platform NuGet package is referenced to keep Core platform-neutral.
- No real 1.8 `.gump` file could be found anywhere on the machine, so
  `NrbfFixtureWriter` in the test-support project writes MS-NRBF payloads
  directly. The importer is therefore tested against genuine NRBF bytes decoded
  by the framework's own decoder — what remains unverified is only whether the
  member names and ordering match a real file, which were recovered by
  decompiling the shipped binary.

## Phase 3 — `GumpStudio.Rendering` ⬜

SkiaSharp renderer for all eleven element types, one art cache with eviction
replacing eleven ad-hoc per-element bitmap fields, and the nine-slice
`ResizePic` background rewritten from the format's semantics rather than
transcribed from decompiled loop residue. Golden-image tests.

## Phase 4 — Avalonia shell ⬜

Canvas plus a testable `CanvasInteractionController` in Core, element tree,
hand-rolled metadata-driven property panel with six custom editors (gump id,
item id, hue, font, cliloc, large text), art/hue/cliloc browsers, page tabs,
undo UI, save/load/import.

## Phase 5 — Plugins ⬜

`Plugins.Abstractions` with a UI-agnostic contract, a collectible
`AssemblyLoadContext` host so plugins can actually be unloaded, then the POL
exporter, SnapToGrid and WallPaper.

## Phase 6 — Cleanup ⬜

Delete `src/`, rewrite the top-level `README.md`, publish self-contained builds.

---

## Defect inventory

Verified findings from the audit of `src/`, kept as a regression checklist.
Anything already handled by the rewrite is marked.

### Correctness

- ✅ **Nested-group elements export at the wrong coordinates.**
  `BaseElement.GetAbsolutePosition()` is documented as "Export plugins should use
  this" and is **called from nowhere in the entire solution**. Every exporter
  flattens with `GetElementsRecursive()` then emits parent-relative `X`/`Y`.
  Confirmed by decompilation that original 1.8 has the same bug, so this is
  inherited, not a port regression. *Fixed by design in Phase 2/5.*
- `Redo()` guards `_currentUndoPoint < _undoPoints.Count`; it must be
  `Count - 1`. `Undo()` has no lower bound at all. Both rely on menu `.Enabled`
  state as their only real guard.
- `ICloneable.Clone()` on `BaseElement` returns `null`.
- `GroupElement.Elements` returns `null`, not empty, for an empty group; its
  constructor iterates the property instead of its `elements` parameter.
- A failed `LoadFrom` leaves `Stacks` empty while `ElementStack` still points at
  the old document; the next paint throws.
- `SaveTo` detaches events without `try`/`finally`.
- Page delete always activates `selectedIndex - 1`.
- `ElementExtender`s live in one **static** list on `BaseElement`, so every
  extender contributes menus to every element type.
- `AppSettings.ArrowKeyAccelerationRate` and `UsePixelPerfectSelection` are never
  read; the acceleration factor is hard-coded `1.06m` in four places.

### Data layer — all fixed in Phase 1

- ✅ `UnicodeFonts.UniCache` is a 1120-entry array indexed by the raw character,
  so **any code point at or above U+0460 crashed label rendering**. The array was
  never even read from.
- ✅ ASCII glyphs drew every pixel unconditionally, making the background opaque
  white, then tried to key it out with a bottom-left-pixel guess that fails when
  a descender lands there.
- ✅ Code page 1251 hard-coded for ASCII glyph lookup; the mapping is a plain
  8-bit one needing no encoding.
- ✅ Unicode font count hard-coded to 7; current clients ship 13.
- ✅ Unsafe RLE decoders took `xOffset`/`xRun` straight from the file with no
  bounds check against the pinned buffer.
- ✅ `Gumps.GetGump` did not validate width/height before allocating.
- ✅ `Art.Cache` was a `static Bitmap[0x14000]`, never cleared, never disposed.
- ✅ Everything was static-constructor-initialised with no invalidation, which is
  why changing the data path told the user to restart.
- ✅ Startup validated only `art.mul`; a partial data folder crashed later inside
  a static constructor.
- ❌ **Retracted:** "cliloc is decoded as UTF-8 but the files are ANSI" — this was
  wrong. Cliloc genuinely is UTF-8 and the old code was right. The real gap was
  the MegaCliloc wrapper.

### Resource and performance

- `CheckboxElement.RefreshCache` leaks `Image2Cache`; `ItemIdPropEditor` leaks
  the browser form on cancel; `Render` leaks a `Graphics` on any exception and
  `_canvas` is never disposed; `SolidBrush` allocated in a 32-iteration loop and
  never disposed.
- Undo deep-clones every page on every action.
- `BackgroundElement.GumpId` validates by loading and disposing nine bitmaps per
  keystroke.
- `ClilocBrowser._clilocCache` is an instance field, so the multi-megabyte cliloc
  file reloads on every property-editor open.

### Other

- 20 `catch` blocks, most `MessageBox.Show(ex.Message)`-and-continue.
- Splash thread is not STA; no DPI awareness; no single-instance guard.
- `Program.cs` sets `PrivateBinPath` on an already-created AppDomain (a no-op).

## Known gaps

- **The BWT decoder is only covered by real-client tests**, so CI does not
  exercise it. Closing this needs either a BWT encoder written purely for test
  fixtures, or a small captured byte pair checked in.
- **Pre-2002 cliloc is unsupported** — numbered `clilocNN.enu` chunks in an IFF
  `FORM`/`DATA` container. Everything else in such a client reads fine.
- **The 1996 pre-alpha is unsupported** — it ships `GUMPS.MUL` with no index.
  `UoDataContext.Validate` reports what is missing, and a test asserts it does.
- **Resolving a UOP gump's dimensions requires decoding it**, because the size
  lives inside the compressed payload. An art browser must therefore virtualise
  and resolve lazily rather than measuring everything up front.
