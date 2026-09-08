# Architecture

Six projects, all `net10.0`. Two rules shape the layout:

1. **`System.Drawing` appears nowhere.** It is Windows-only on modern .NET, it
   made the old code impossible to test headlessly, and it produced a long tail
   of undisposed GDI handles. `BannedSymbols.txt` enforces this at compile time.
2. **Nothing below the app layer knows what a UI toolkit is.** The old
   `BasePlugin.Load(DesignerForm)` and
   `ElementExtender.AddContextMenus(ref MenuItem, …)` made every plugin a
   WinForms plugin.

```
GumpStudio.Uo            UO client data: .mul + .uop, art, gumps, hues,
                         tiledata, cliloc + its ~1_X~ substitution rules,
                         ASCII + Unicode fonts.
                         Returns plain pixel buffers — no bitmaps.
        │
GumpStudio.Core          Element model, document model, commands + undo,
        │                XML serialisation, legacy NRBF import, the layout
        │                IR and the converter contract. Headless and testable.
        │
GumpStudio.Rendering     SkiaSharp renderer, art cache, hit-test geometry.
        │                Runs headless, which is what makes golden-image
        │                tests possible.
        │
GumpStudio.Converters    Raw client layout, POL, RunUO and Sphere. Each one
        │                reads the layout IR and knows only its own syntax.
        │
GumpStudio.App           Avalonia shell. The panels around the canvas are
                         Dock dockables, so the layout is the user's to
                         arrange.
GumpStudio.Cli           Headless tooling: dump art, convert files, run
                         converters without the UI.
```

Tests live in `tests/`, with `GumpStudio.TestSupport` holding the fixtures and
the real-client discovery described in [testing.md](testing.md).

## The image pipeline

The single most important structural change. The old code returned
`System.Drawing.Bitmap` from every loader, which forced Windows, forced GDI
lifetime management on callers, and made hue application depend on a specific
`PixelFormat`.

```
container  →  decode           →  hue            →  widen        →  render
.mul/.uop     Argb1555Image       ApplyTo(span)     UoImage         SKBitmap
              (ushort[])                            (BGRA byte[])
```

`Argb1555Image` exists because hueing is defined in terms of the five-bit red
channel — once the image is widened to eight-bit channels that index is gone.
`UoImage` is BGRA8888, which is exactly `SKColorType.Bgra8888` on little-endian,
so handing it to Skia is a straight memory copy.

Neither type owns an OS resource, so there is nothing to leak and nothing to
dispose.

## `UoDataContext`

One disposable object owns everything loaded from one installation. The old SDK
initialised every loader in a static constructor keyed off a global directory
list, which is why changing the data path made the application tell the user to
restart. Here, pointing at a different client means disposing one context and
opening another.

`UoDataContext.Validate` checks the whole required file set up front and reports
what is missing, rather than failing later inside a static constructor with no
indication of why.

Container choice is per file: a `.uop` package is preferred when present and the
legacy `.mul` pair is the fallback, because modern clients ship no
`gumpart.mul` while older ones ship no UOP.

## `GumpStudio.Core`

- `GumpDocument` { properties, pages } replacing the untyped `ArrayList Stacks`.
- Elements hold data only. Resize-handle layout and hit-test rectangles come
  from **one** `HandleGeometry` helper, replacing two divergent sets of magic
  numbers plus a 260-line eight-case resize switch in the old form.
- `Element.GetAbsolutePosition()` becomes the only way position is read, which
  is what fixes the nested-group export defect.
- Elements raise change notifications rather than reaching back into a global
  form; `GlobalObjects` does not survive.
- **Command-based undo**, replacing whole-document deep-clone snapshots. This
  turns "undo points are created at the wrong time" from a guessing game into an
  ordering question.
- **XML over an explicit DTO layer**, hand-written rather than reflection-based,
  so renaming a class never breaks a saved file.

## Export: one layout, four converters

Exporting is two steps, and the split between them is the point.

```
GumpDocument ──► GumpLayoutBuilder ──► GumpLayout ──┬─► LayoutConverter
                 the only caller of                 ├─► PolConverter
                 GetAbsolutePosition()               ├─► RunUoConverter
                                                     └─► SphereConverter
```

**`GumpLayout` carries meaning; a converter owns syntax.** The IR is the client's
own command table — `resizepic`, `gumppic`, `button`, `tooltip` and the rest —
with absolute coordinates, page boundaries, radio-group scoping, text-slot
allocation and tooltip ordering already settled. A converter decides only how its
target spells that: POL escapes for quoted strings, RunUO for C# verbatim
literals, Sphere for line-based tokens, and the same flag is `NOCLOSE` in Sphere
0.56, `NoClose` in 0.99 and in POL.

Before this, each exporter walked the document itself. That meant three page
loops, three radio-group trackers, three text-slot allocators, and three element
`switch`es with a `default` arm that silently skipped anything new — and the
client's layout grammar hand-written **four** times, because the two dialects that
do not emit it still build it for the notes they leave beside commands they
cannot express. The same defects then had to be found and fixed once per
exporter: the nested-group coordinate bug, the locale-sensitive number
formatting and the cross-page radio-group leak were each repaired three times
over.

`GumpLayoutBuilder` is the second real implementation of `IElementVisitor`, so a
new element type is now a compile error in every converter rather than a gump
with the element missing — which is what that interface was introduced for, and
what no exporter actually did.

Two details are load-bearing:

- **Commands carry a stable `Ordinal`.** Converters run pre-passes over the
  stream — RunUO builds its `Buttons` enum that way — and join the results back
  per command. Keying that on the command value would collapse two genuinely
  distinct commands that happen to be identical.
- **Text slots are late-bound.** The same string reaches output as a table index,
  as an inline literal, or as a literal `0` in the gump package's notes, which
  have no data array for an index to point into.

## Import: the same pipeline backwards

```
layout text ──► LayoutStringParser ──► GumpLayout ──► GumpLayoutReader ──► GumpDocument
```

Packet-sniffing tools dump the layout string a server sent, which is the same
grammar the `layout` converter writes — so importing a captured gump is the
export pipeline run in reverse, over the same IR. A round-trip test asserts the
two directions are inverses: write a document as layout text, read it back, write
it again, and the text must be identical.

The parser is deliberately lenient, because captures are not written by one tool
and are often truncated. Braces around each command are optional, the `[layout]`
and `[text]` markers are optional, several commands may share a line, and runs of
whitespace inside a command are one separator — the real capture checked in under
`tests/GumpStudio.Core.Tests/Captures/` contains all of these. Nothing throws:
unknown commands and out-of-range text references are reported as warnings and
skipped, because refusing an entire paste over one bad line is useless to
someone holding a truncated dump.

Two things do not survive, and cannot:

- **Groups.** The client has no notion of one, so an imported document is flat.
  That costs nothing, because a group only affects its children's absolute
  positions and the layout has already resolved those.
- **Sparse page numbers.** Pages are a contiguous list here but a bare number in
  the layout, and a capture may use 0, 1, 2, 9, 10 — so the gaps are filled with
  empty pages, which keeps every page button pointing where the server meant.

## No plugin system

There was one, and it existed almost entirely to carry three exporters. It cost a
project, a collectible `AssemblyLoadContext`, `RequiresUnreferencedCode` and
`RequiresDynamicCode` annotations that propagated to every caller, a second
registration path behind `#if STATIC_PLUGINS` because a NativeAOT image cannot
load an assembly at all, about sixty lines of MSBuild to deploy the assemblies,
and three near-identical loader test files. A new exporter had to be registered in
three places or it went missing from one of the builds.

The converters are ordinary referenced code now, so an AOT publish and an ordinary
build run the same path, and adding one means adding it to a single list.

## Known gaps and standing risks

Things a reader should know before changing the relevant code, rather than a
to-do list.

- **The SkiaSharp version skew — check this first if rendering breaks after an
  Avalonia bump.** `Avalonia.Skia` 12.1.2 is built against SkiaSharp **3.119.4**;
  this repository pins **4.151.2** and central transitive pinning unifies to it.
  Nothing breaks today precisely *because* the application never hands a Skia
  object to Avalonia — the canvas blits through a `WriteableBitmap`, and that is
  what insulates the two. It is also what blocks the obvious optimisation:
  drawing into Avalonia's own canvas through `ISkiaSharpApiLease` would remove
  the CPU raster and the 3 MB per-frame texture upload, but taking an `SKCanvas`
  across that boundary means matching Avalonia's SkiaSharp major version.
- **`ClilocTable.Parse` reports an empty table rather than garbage** when neither
  the plain nor the decoded reading looks like text. A caller can render
  `#1044017` for a string it does not have, but it cannot tell that a string it
  was handed is wrong. Every cliloc in the test matrix decodes today, so an empty
  table is now a regression signal and
  `UoDataContextTests.ReadsEveryClilocLanguageTheClientShips` asserts on it.
- **Resolving a UOP gump's dimensions requires decoding it**, because the size
  lives inside the compressed payload. An art browser must therefore virtualise
  and resolve lazily rather than measuring everything up front. The payload memo
  is a 32 MB least-recently-used window, so a revisited entry is not inflated
  twice, but the first look at one still costs a decode.
- **`dotnet test` does not work on SDK 10.0.400** — `Zero tests ran` for every
  project, reproducibly, including for a one-file xunit.v3 project in an empty
  directory. `build/run-tests.ps1` launches the test applications directly
  instead. See [testing.md](testing.md), and retry `dotnet test` after an SDK
  bump.
- **Two client eras are unsupported.** Pre-2002 cliloc is numbered
  `clilocNN.enu` chunks in an IFF `FORM`/`DATA` container; the 1996 pre-alpha
  ships `GUMPS.MUL` with no index. `UoDataContext.Validate` reports what is
  missing in both cases, and a test asserts it does. Everything else in a
  pre-2002 client reads fine.
- **Reflection is not available.** Both applications publish NativeAOT, so
  string bindings (`new Binding("Id")`), reflective serialisers and anything
  needing `RequiresDynamicCode` fail the publish rather than warning. Dock's own
  layout serialiser was declined for this reason, which is why tearing a panel
  into its own window is not remembered between sessions even though pane sizes
  and hidden panels are.
- **The shell is imperative.** `MainWindow.axaml.cs` has no view models, and
  every action is registered three times over — menu, shortcut, context menu.
  Avalonia's `Click`/`ClickAsync` also bind silently to nothing on a misspelled
  control name, so a typo yields a dead menu item with no error anywhere.
- **Smaller known limitations.** Art-gallery rows are built without recycling,
  so roughly forty controls are allocated per row scrolled into view; the canvas
  rasterises its whole surface rather than clipping to the scroll viewport;
  `UoArtSource` has no tests of its own because it takes a concrete
  `UoDataContext`; a page cannot be renamed, though `GumpPage.Name` is shown in
  the move-to-page menu; and the splash graphic is a 2004 JPEG, so it carries
  compression artefacts around the lettering that only new artwork could remove.
