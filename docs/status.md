# Status and roadmap

Last updated after the snap-to-grid work. Phases 0, 1, 2, 3, 4 and 5 are
complete; Phase 6 is partly done.

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
| Exporters | POL, RunUO and Sphere, in two dialects each. The RunUO *importer* and Wolfpack stay dropped |

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
  Platform, opted into by a `"test"` block in `global.json` **and** by
  `UseMicrosoftTestingPlatformRunner` in `tests/Directory.Build.props` — xunit.v3
  4.0.0 otherwise defaults to its own console runner, which silently ignores
  every MTP flag. `dotnet test` itself is unusable on SDK 10.0.400 and the suite
  runs through `eng/run-tests.ps1`; see [testing.md](testing.md) for why.
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

## Phase 3 — `GumpStudio.Rendering` ✅

Done. 15 tests in `GumpStudio.Rendering.Tests`; 268 across the solution.

- `GumpRenderer` draws a page onto an `SKCanvas` and touches no UI type, so it
  runs headless — which is what makes pixel assertions possible at all. The old
  renderer lived inside the designer form.
- `ElementPainter` is an `IElementVisitor`, so adding an element type breaks the
  build rather than silently drawing nothing.
- `UoArtSource` holds one bounded LRU cache, replacing the eleven unbounded
  ad-hoc bitmap fields the old element classes each carried.
- `NineSlice` implements resize-pic backgrounds from the format's semantics.
  Corner images set the margins, edges tile along their axis, the centre tiles
  both ways, and every band is clipped. It survives being dragged smaller than
  its own borders, which the arithmetic in the original did not obviously do.
- All art is drawn with nearest-neighbour sampling. Skia's default smooths, which
  softens every edge of low-resolution pixel art.
- Group rendering saves and restores the canvas around children, so a throwing
  child cannot leave the transform shifted for everything after it.

Rendering tests use a synthetic art source, so they run on CI with no client
present. Determinism is asserted directly, which is the precondition for
golden-image tests later.

The CLI gained `render` and `sample`, making the whole stack demonstrable:
`sample` writes a document, `render` loads it, resolves art from a real client
and writes a PNG.

## Phase 4 — Avalonia shell ✅

Done. 468 tests across the solution; 6 skip when the single-client
environment variables are unset, and the client-data theories skip entirely when
no installation is configured, so CI stays green.

- `CanvasInteractionController` lives in **Core**, not the UI, so selection,
  dragging, resizing, marquee and nudging are tested directly. In the original
  this was roughly 500 lines of nested pointer handlers inside `DesignerForm`
  that could only be exercised by driving a live window.
- `GumpCanvas` is a thin Avalonia control: it converts events to gump
  coordinates, renders through SkiaSharp into a `WriteableBitmap`, and does
  nothing else.
- The property panel is metadata-driven from `PropertyRow`. Every edit becomes a
  `SetPropertyCommand`, so property changes are undoable and merge while typing.
- Settings are JSON in the platform application-data folder, replacing
  `ApplicationSettingsBase` and its unfindable hashed `user.config`.
- Failures land in the status bar rather than a modal box from twenty-odd catch
  blocks.
- The POL exporter is discovered from `Plugins/` at startup rather than
  referenced, so the plugin path is what actually ships.

Verified by running the application against a 7.0.114.4 client: the canvas draws
real art, the element list and property panel populate, and a document opens from
the command line.

### Art browser and pointer feedback

Added after the first pass, both reported as missing against the original:

- **Art browsers.** Gump id and item id fields have a browse button opening a
  picker that lists only ids with art, shows a thumbnail per row, filters by id
  (decimal or `0x`) or tile name, and previews the selection with its dimensions.
  Enumeration uses a new `IUoFileProvider.Exists` probe that does *not* decode —
  on a UOP client, resolving a gump's dimensions costs an inflate plus a
  Burrows-Wheeler pass, so walking tens of thousands of ids the obvious way would
  take minutes. Thumbnails decode off the UI thread as rows are realised.
- **Resize cursors.** Hovering a resize handle now shows the matching directional
  cursor and the element body shows a move cursor. The hit test asks the same
  `HandleGeometry` the press handler uses, so the cursor cannot disagree with
  what a press will actually do.

Fixed while testing those: selecting an element in the element list left the
property panel showing "Nothing selected". The list rebuilt its item source on
every refresh, and the resulting selection-reset event raced the suppression
flag. It now rebuilds only when the page contents actually change.

### Panels that can be resized, and a scroll bar that stays out of the way

Reported: the element list and the property panel clip their contents, and the
property panel's scroll bar sits on top of the browse button at the end of an id
row.

Both side panels were fixed widths docked in a `DockPanel`, so nothing could be
widened when its contents did not fit — not even by maximising the window, since
the extra space all went to the canvas. They are grid columns with
`GridSplitter`s now, each with a minimum width so a panel cannot be dragged away
entirely, and the element list and property panel have a splitter between them
too. The inspector starts at 340 rather than 300.

The overlap was Fluent's floating scroll bar: it is drawn **over** the content it
scrolls rather than beside it, so it landed on the `…` button of a gump- or
item-id row — the one control in the panel that sits hard against the right
edge. The property scroller sets `AllowAutoHide="False"` and the content carries
a matching right margin, so the bar has a gutter of its own.

Scroll bars are also **full width everywhere**, set once in `App.axaml`. Fluent
draws one as a hairline until the pointer is over it and only then animates it to
size, which is a poor trade in a picker: you scroll far more than you point, and
a two-pixel line is both hard to grab and hard to read as a position.

Forcing it is less obvious than it looks. `ScrollBar.IsExpanded` is exactly the
right property and it is a **read-only direct property** — a style setter for it
compiles happily and then throws `The property IsExpanded is readonly` at
startup. The working route is to give the bar a real width and size its thumb,
which leaves the track and its stepper buttons drawn at all times.

The properties header moved inside the scrolling row rather than occupying a row
of its own: a splitter resizes the rows on either side of it, and an `Auto`
header between them would have been the thing that got resized.

### The art browsers as a tile gallery

Both browsers now open as a grid of thumbnails, with a **Gallery** toggle back to
the one-per-row list. The choice persists.

The grid had to be built by hand. Avalonia 12 ships no virtualizing wrap panel —
only `VirtualizingStackPanel` — and a plain `WrapPanel` would realise all 39,516
item tiles at once. So the same `ListBox` drives both modes and only the shape of
its items differs: one entry per item in list mode, a whole **row** of entries per
item in gallery mode, chunked to fit the current width and re-chunked when the
window is resized. Virtualization then falls out of the vertical panel it already
had.

Because the list's items are rows in gallery mode, its own selection is
meaningless there; the tiles report their own. The highlight is refreshed by
sweeping the realised visuals for tiles tagged with an entry — one screenful of
work, and unlike a map of tile controls it cannot go stale as rows scroll in and
out.

Three traps worth recording, all found by using it rather than by reading it:

- A `FuncDataTemplate<T>` for a reference type is also asked to build when a
  container is being **cleared**, with a null item. Both builders dereferenced
  it, and the browser crashed the application outright the first time a row
  scrolled out of view.
- A row built for that null item measured **zero height**, and the virtualizing
  panel estimates its extent from the rows it has seen — so one such row was
  enough to make it stack later rows on top of each other. Rows carry an explicit
  height now.
- Testing "is this control still on screen?" to discard stale work also discards
  work for a control that is *not on screen yet*, which is every control at the
  moment its template runs. That threw away every thumbnail before it could be
  shown. The tile records the id it wants and clears it on
  `DetachedFromVisualTree` instead, which distinguishes "gone" from "not arrived".

### Tile size, and a tile that would not take a click

The thumbnail size is a setting, adjustable from the browser's own toolbar and
remembered between sessions. It applies to both views and defaults to **144
pixels**, twice what the first version used: large gump art was being scaled down
so far that recognising a piece meant selecting it and reading the preview. The
browser window opens wider to suit, so the bigger tiles still give five or six
columns. Changing the size clears the thumbnail cache, whose bitmaps were scaled
for the old one, and the cache is bounded by a memory budget rather than a count —
at 32 pixels a thousand thumbnails are four megabytes, at 320 they are four
hundred.

Small art is not blown up to fill the tile. Nearest-neighbour upscaling would
look fine, but it would also make a 9x21 gump and a 300x200 one look the same
size, and the browser is where you go to find out which is which.

**A tile only responded to clicks on the artwork itself.** An unselected tile was
painted with no brush at all, and Avalonia does not hit-test a control where
nothing is drawn — so the empty space around a small piece of art, which is most
of the tile, silently swallowed the click. The selected tile worked, because its
highlight gave it something to hit. Unselected tiles are painted
`Brushes.Transparent` now, which is hit-tested. Confirmed with a hit-test probe
before and after: the unselected tile reported zero hits at its corner and one
afterwards.

### The stranded row

Reported: rows of tiles appearing at the wrong column pitch, overlapping the real
grid, most often after changing the tile size — and surviving a switch to the
list and back.

Two causes, both in re-chunking:

- **The item source was replaced directly.** Handing the panel a new list leaves
  it reconciling one set of rows against another, and a container realised for
  the old set could be left parented and visible with nothing to remove it. The
  source is cleared first now.
- **Re-chunking ran inside a layout pass.** The reflow was driven from
  `SizeChanged` and `LayoutUpdated` and rebound inline, so the panel was handed a
  different row list while it was still laying out the previous one. The work is
  posted now, and gated on the panel's *width* rather than only on the resulting
  column count — rebinding changes the content, which can change whether a scroll
  bar is needed, which changes the width, and two widths that disagree about the
  column count would re-chunk each other indefinitely.

Both were confirmed by counting the rows actually parented in the panel after
cycling the tile size through 96, 192, 240, 96 and 160. Before: five rows for a
five-column layout, with `[300..304]` present **twice**. After: four rows, each
consecutive. That duplicate is what reached the screen as a stray row — in the
reported case entries 100 to 104, which is why the same five numbers appeared in
two different screenshots.

Chunking also stopped using `Skip`/`Take`, which walks the list from the start
for every row and on forty thousand entries costs more than decoding the art.

### Decoding a screenful without thrashing

A gallery realises about seventy tiles at once where the list realised fourteen,
and the first version fired that many decodes concurrently. That was much worse
than it looks: the UOP reader memoises exactly **one** decompressed entry, and
reading a gump takes two passes over it — one for its dimensions, one for its
pixels. Run in parallel, every thread evicts every other thread's memo, so each
gump inflated and Burrows-Wheeler-decoded twice instead of once, on a flooded
thread pool. Art arrived slowly, out of order, and sometimes not at all.

Decoding is serialised through a task chain now, results are cached
(downscaled to tile size, several hundred of them, so scrolling back costs
nothing), and a request whose tile has since been recycled is dropped before it
does any work rather than after.

### Aligning a selection, and the text-entry wash

Two things the original had that the rewrite had dropped, both found by
comparing against it in use.

**Arrange**, on the Edit menu and the context menu, with the original's six
alignments — lefts, rights, tops, bottoms, centre horizontally, centre
vertically — plus equalising horizontal and vertical spacing. Two rules are worth
stating because they are easy to get subtly wrong and are not what every editor
does:

- Everything moves **to the anchor**, and the anchor does not move. The anchor is
  whichever element was last pressed or right-clicked, so right-clicking one of a
  group and aligning lines the rest up on that one. The alternative — collapsing
  the whole selection onto its own bounding box — leaves nothing predictable to
  aim at. When no anchor applies, the frontmost selected element is used.
- Spacing evens out the **centres**, not the gaps, and the outermost two do not
  move. With elements of differing sizes even gaps look uneven; even centres do
  not. Spacing needs three elements, and says so rather than appearing broken
  with two.

**A text entry is washed translucent yellow again.** The client draws no frame
for one, so without something the editor draws itself an empty field is
invisible; the rewrite outlined it in grey, the original filled its bounds with
`Color.FromArgb(50, Color.Yellow)`. The original reads better — it says "the
player types here" rather than "there is a box here" — so that is what it does
now.

### Moving elements between pages

**Page ▸ Move selection to page**, and the same submenu on the context menu,
listing every page except the one being edited. The original could not do this at
all: an element placed on the wrong page had to be deleted and rebuilt on the
right one.

Every page is its own coordinate space rooted at the gump's origin, so an element
leaving a group is rebased on the way out — its new location is the absolute
position it had — or it would jump by the group's offset. Undo puts each element
back in its original slot, not on the end.

The editor follows the elements to the destination page. That is deliberate:
pages other than 0 are mutually exclusive, so moving something to one while
looking at another makes it vanish, which reads exactly like a delete.

The submenu is rebuilt each time it opens rather than kept in sync, because pages
come and go while the editor is open and a stale entry would point at a page that
no longer exists. It is filled from the **Page** menu's own opening, not the
submenu's, because a disabled item never opens its submenu and the enabled state
has to be settled one level up.

### Drawing order, ungrouping, and shortcuts that work

Reported after using the editor: there was no way to ungroup, no way to change
drawing order, no context menu, and Ctrl+G did nothing.

**Shortcuts never worked at all.** `InputGesture` on an Avalonia `MenuItem` only
*draws* the shortcut beside the item — it does not bind the key. Every gesture in
the menu bar was decorative: Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Z, Ctrl+Y, Ctrl+A and
Ctrl+G all did nothing. They are real `KeyBindings` on the window now, so a
control that has already handled the key — a text box swallowing Ctrl+A or Delete
while the caret is in it — still wins.

**Drawing order had no UI.** It is the whole of layering in a gump: the last
child of a group draws in front. A background added after an image covered it
permanently, and the only recovery was to delete everything and place it again in
the right sequence. Four operations now exist — bring to front, bring forward,
send backward, send to back — on Ctrl+Shift+Up, Ctrl+Up, Ctrl+Down and
Ctrl+Shift+Down. A multi-element move keeps the selection's own relative order and
steps from the destination end, so the members cannot shuffle past each other.

**Ungroup** is the counterpart to a Group command that had shipped without one.
Children return to the group's own slot in the z-order rather than landing on
top, keep their position on screen, and end up selected so a different subset can
be grouped immediately.

**A context menu** on the canvas and on the element list carries undo, redo,
group, ungroup, the four ordering commands, delete, select all and gump
properties. Undo and redo name what they will undo. Right-clicking an element
selects it first, unless it is already part of a multiple selection — otherwise
"bring these four forward" would collapse to one the moment you reached for the
menu.

Two things that had to be got right for the menu to appear at all: the canvas
returns from its pointer-pressed handler without capturing on a right press, and
does not mark the matching release handled. Marking it handled swallows the
context-menu request Avalonia raises from that release, and the menu silently
never opens.

The element list is also labelled **back → front** now, because the list order
*is* the drawing order and nothing said so.

### Snap to grid, built in

In 1.8 this was `SnapToGrid.dll`: a plugin that reached into the designer form
through mouse and key hooks, drew its grid by writing bytes into a locked bitmap
with `Marshal.WriteByte`, and persisted its own `BinaryFormatter` config file
beside the executable. Aligning elements is core editing behaviour, not an
optional extension, so it lives in the editor now.

`GridSettings` in Core owns spacing, visibility and snapping, and is the single
place rounding is defined. Two details are deliberate:

- **Visibility and snapping are independent toggles.** Wanting to see the grid
  and wanting to be constrained by it are different wishes.
- **Rounding floors rather than truncating.** Integer division truncates toward
  zero, which biases negative coordinates the wrong way — elements dragged left
  of the origin would snap inconsistently with ones dragged right of it.

Behaviour on the canvas:

- Dragging snaps the element the pointer grabbed, then moves the rest of the
  selection by that same corrected delta. Snapping each element independently
  would pull a carefully spaced row together into a single column.
- Resizing snaps each edge on its own, so the edge being dragged lands flush
  with the grid rather than the origin snapping and the far edge staying off it.
  An element can never be snapped down to nothing; one cell is the floor.
- Arrow keys step one grid cell instead of one pixel while snapping is on, so
  the keyboard and the mouse agree about where things can land.

**View ▸ Show grid**, **Snap to grid** and **Grid size…** drive it, and all four
values persist in the settings file. The grid renders in
`GumpRenderer.RenderDocument` beneath the page-0 backdrop, and is skipped
entirely below three screen pixels of spacing so a fine grid at low zoom does not
turn into a grey wash.

### Page 0 is always visible

Page 0 is Ultima Online's shared layer: its contents stay on screen while the
player switches pages. The canvas now draws it beneath whichever page is being
edited, defaulting to on with a **View ▸ Show page 0** toggle, matching the
original's behaviour. The backdrop draws without selection decoration and its
elements are not selectable from another page, since they belong elsewhere.

`GumpRenderer.RenderDocument` owns the rule so the canvas and the CLI cannot
disagree about it. Exporters needed no change: emitting `page 0` and its
elements before `page 1` already expresses the same thing.

**Not built.** Still missing relative to the original:

- A **hue picker**. Hues are still typed as numbers, and a hue is as opaque as a
  gump id. The same browser pattern would suit it — swatches instead of
  thumbnails — and `HueTable` already exposes the colours.
- A **cliloc browser** for the HTML element's localised id.
- Clipboard cut/copy/paste.
- A plugin manager UI for enabling and ordering plugins; discovery currently
  loads everything it finds.
- Drag-to-reorder in the element list. The four ordering commands cover the same
  ground from the keyboard and the context menu.

## Phase 5 — Plugins and the POL exporter ✅

Done ahead of Phase 4, because the plugin contract is UI-agnostic by design and
therefore does not need the shell. Doing it first means the shell can wire up a
real exporter rather than a stub. 43 tests in `GumpStudio.Plugins.Pol.Tests`.

- `IGumpStudioPlugin` / `IPluginHost` carry no UI type at all. Menu
  contributions are declarative descriptors the shell renders, so a plugin never
  constructs a widget. The old `BasePlugin.Load(DesignerForm)` handed plugins the
  WinForms window and its menu items.
- Plugin identity is a stable id string. The original compared all five
  description fields by value, so bumping a version silently un-loaded a plugin.
- `PluginLoader` gives each assembly its own collectible `AssemblyLoadContext`,
  so plugins can be unloaded instead of demanding an application restart. A
  plugin that throws while loading is reported and skipped; the original
  enumerated types outside its try block, so one bad assembly aborted discovery
  for everything after it.
- Plugin mutation goes through `IGumpDocumentSession.Apply`, so a plugin's edits
  are undoable like any other. The old API handed over the live object graph with
  no undo integration.

The POL exporter is ported with both dialects preserved — the `GF*` gump-package
calls and the raw layout-string array — and three corrections:

- Coordinates come from `GetAbsolutePosition()`, fixing the inherited
  nested-group defect. Visible in real output: a child at (5,5) inside a group at
  (150,60) now exports at 155,65.
- Text is escaped before being interpolated into a quoted POL string. The
  original emitted a script that would not compile if any text contained a quote.
- Numbers format invariantly, and the header timestamp is injectable, so two
  exports of the same gump are byte-identical and can be diffed. The original
  stamped `DateTime.Now` into every export.

`SnapToGrid` is not ported as a plugin — it is a built-in feature now, described
under Phase 4. `WallPaper` is not ported either; both were canvas-hook demos for
an API that no longer exists in that shape. The equivalent extension points
(`ICanvasLayer`, `IPointerInputFilter`) are named in the contract but are not
implemented until something needs them.

## Phase 6 — Cleanup ◐ partly done

- ✅ Top-level `README.md` rewritten.
- ✅ Self-contained publishing verified for `win-x64` and `linux-x64`, including
  plugin deployment. Publishing needed its own copy target: the build-time one
  writes to `$(OutDir)`, which is not the publish directory, so a published
  application shipped with an empty `Plugins` folder — the same gap the old
  solution had, reintroduced one layer down.

  ```sh
  dotnet publish source/GumpStudio.App -c Release -r win-x64   --self-contained
  dotnet publish source/GumpStudio.App -c Release -r linux-x64 --self-contained
  ```

- ⬜ **`src/` is deliberately still here.** The plan made deleting it conditional
  on reaching parity, and parity is not reached: the art, hue and cliloc
  browsers, the clipboard and the plugin manager are not built, and `src/` is
  the reference for all of them. Deleting it now would throw away the only
  description of behaviour that has not been ported yet. It should go once the
  gaps below are closed.
- ⬜ No release tagged.

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

## Gump commands added after 1.8

The client gained sixteen layout commands after GumpStudio 1.8 was written, and
none of them were modelled. They are now, and
[gump-commands.md](gump-commands.md) maps every command the client accepts onto
the document model.

Most of them are properties rather than new element types, because that is what
they are to the client too — `gumppicphued` is `gumppic` with a different tinting
rule, `textentrylimited` is `textentry` with a cap. Only `picinpic` and
`tilepicasgumppic` say something the model could not already say, so only those
two became element types (`PicInPicElement`, `TileAsGumpElement`).

`tooltip` and `itemproperty` became properties on the **base** element, since the
client attaches them to whichever element it created last: that makes them
positional in a layout string but per-element everywhere else. Storing them on
the element means an exporter cannot attach one to the wrong element by
reordering its output.

The gump-level parser toggles — `mastergump`, `toggleupperwordcase`,
`togglecroppedtext`, `echandleinput` — became `GumpProperties`, and a
**Gump ▸ Properties…** dialog was added to reach them. Those flags, along with the
movable/closable/disposable ones that existed all along, previously had no editor
at all: they round-tripped through save and load and reached the exporters, but
the only way to change one was to hand-edit the file.

Three defects surfaced while doing this, all confirmed against the client's own
parser, the POL command reference and RunUO, which agree with each other:

- **`button` had all three of its trailing slots wrong.** It inverted the quit
  flag, put a page button's target page in the return-value slot, and a reply
  button's return value in the page slot — so a page button closed the gump and a
  reply button jumped to a page numbered after its reply id. The 1.8 source
  carries a `// TODO: Page or Reply???` comment at exactly that spot.
- **Radio groups leaked across pages.** `page` resets the client's current group,
  but the exporter tracked the last group used for the whole document, so a
  second page whose first radio matched the previous page's group never got its
  `group` command and every radio on it fell into group 0.
- **No `endgroup` was ever emitted**, so any radio placed after the last group on
  a page silently joined it.

## The RunUO and Sphere exporters

Both were dropped from the original plan and put back at the user's request.
They are ported now, as `GumpStudio.Plugins.RunUo` and
`GumpStudio.Plugins.Sphere`, each registering two exporters.

Every exporter now offers both of its dialects as separate menu entries rather
than hiding one behind a modal options dialog the plugin builds itself. That also
closed a gap in the POL plugin: it registered only the gump-package dialect, so
**the layout-string form could not be reached from the application at all**.

Six entries in all: `pol`, `pol-layout`, `runuo`, `runuo-numeric`,
`sphere-056`, `sphere-099`. `gumpstudio export --format <id>` drives the same
set from the command line.

### What the originals got wrong

Each carried the nested-group coordinate defect and locale-sensitive number
formatting, like the POL one. On top of that:

**RunUO**

- **The command form did not compile.** Its static command handler assigned
  `e.Mobile` to an *instance* field, which is a compile error in C#. Anyone who
  ticked "command call" got source that would not build.
- **Text was escaped for the wrong kind of string literal.** It emitted verbatim
  literals (`@"…"`) but escaped quotes as `\"`, which is a syntax error inside
  one. A label containing a quote produced source that would not build.
- **Checkboxes and radios were mixed into the button enum.** Their switch ids
  went into the same `Buttons` enum as button ids and got `case` labels in the
  `info.ButtonID` switch — where a checkbox never appears, and where its id
  could collide with a real button's. Switch ids and button ids are separate
  namespaces and are kept apart now.
- **The named style discarded the author's response ids.** Enum members had no
  values, so a button's `Param` was replaced by the member's ordinal. Members
  carry their `Param` explicitly now.
- **Element names were only stripped of spaces**, so anything else
  non-alphanumeric produced an invalid identifier. Names are reduced to valid
  identifiers and de-duplicated.

**Sphere**

- **Checkboxes and radios had their two graphics the wrong way round.** The
  layout command takes the released id first; the original emitted the checked
  id first, so every checkbox and radio in a generated dialog rendered inverted.
- **Every button closed the dialog.** The quit flag was hard-coded to 1, so a
  page button dismissed the gump instead of switching page.
- **Radio groups leaked across pages**, the same defect the POL exporter had.

## The three 1.8 builds in `external/`

`external/` holds `Gumpstudio1.8r2`, `Gumpstudio1.8r3`, and
`Gumpstudio1.8r3-quinted` — a 2014 community build by staticz. Each was checked
in case the rewrite was tracking a superseded release.

### `-quinted` is what `src/` was decompiled from

Worth stating plainly, because this page previously implied r2 was the reference
for everything. r2 ships `POLExport.dll`: a different, much smaller exporter in
namespace `POLExport` that emits `CreateGump(0,0,0,0)` and targets Hesinde.
Quinted ships **`POLGumpExport.dll`** — namespace `POLGumpExport`, three times the
size, emitting the `GF*` gump-package calls and the layout-string array, with the
"for gump pkg" header, the "Bare gump" option and "Create Default Texts". That is
the exporter `src/Plugins/POLGumpExport/` contains, and therefore the one
`GumpStudio.Plugins.Pol` is a port of. The POL work is already based on the
newest build, not on r2.

### What quinted actually changes

`GumpStudio.exe` and every plugin except the POL one are byte-identical to r2.
`GumpStudioCore.dll` has **no public method signature differences at all**; the
changes are inside bodies, plus two new properties.

| Assembly | Change |
|---|---|
| `UOFont.dll` | ASCII font array was filled from index 1, leaving slot 0 null and every font off by one — fixed to 0-based. Character lookup pinned to code page 1251 instead of the locale's. Unicode fonts extended from 3 files to 13, and their index also made 0-based. Glyph cache grown from 1001 to 1120 entries. |
| `GumpStudioCore.dll` | `LabelElement` gains `Unicode` and `PartialHue`; the font-range check widens from "1 to 3" to "0 to 10 for ansi and upto 12 for unicode"; `LabelElementVersion` goes to 3, and loading an older file **decrements `FontIndex`** to match the new 0-based indexing. Debug tracing removed. |
| `Ultima.dll` | A newer UOSDK snapshot: drops `Animations`, `BodyConverter`, `Map` and `FastBitmap`, adds `Skills`, `ClientProcessHandle` and `NativeMethods`. **Still MUL-only — no UOP, no MegaCliloc.** |

### Where the rewrite already stands

- **Unicode fonts.** Discovered by enumerating `unifont*.mul` rather than hard-coding
  a count, so 3, 7 and 13-font clients all work. Quinted hard-codes 13 and throws
  if a file is absent.
- **Font indexing.** Already 0-based for both families.
- **The glyph-cache landmine.** Quinted only grew the fixed array to 1120 entries;
  it is still indexed by an unbounded code point, so anything at or above U+0460
  still crashes. The rewrite uses a dictionary.
- **`Ultima`.** Far ahead: UOP containers, MegaCliloc, all three tiledata layouts,
  verified against fourteen clients.
- **The POL exporter.** Already the quinted one, with its inherited defects fixed.

### The one genuine gap

The rewrite renders label text with the **Unicode fonts only**. `AsciiFonts` is
loaded but nothing draws with it, so there is no equivalent of quinted's
`Unicode` toggle and no way to preview a label in one of the ten `fonts.mul`
faces. `LabelElement.PartialHue` is likewise absent — label hue is applied
wholesale.

This is a **preview-fidelity gap, not an output one**. The gump protocol's `text`
and `croppedtext` commands carry no font parameter, and neither quinted's
exporter nor any exporter in `src/` ever writes `FontIndex`, `Unicode` or
`PartialHue`. Nothing about the generated script changes.

The related loose end is import: quinted decrements `FontIndex` when reading a
file written before version 3, and `LegacyGumpImporter` does not. A genuine
1.8-era `.gump` therefore previews one font off — again, only in the designer.

### r3

r3 sits between the two and contributes nothing. Only `GumpStudioCore.dll` and
`Ultima.dll` differ from r2, by VB designer field renames and the same
skill-tree additions quinted has. **r2 remains the reference for the element
model; quinted is the reference for the POL exporter.**

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
- **`dotnet test` does not work on SDK 10.0.400.** It reports `Zero tests ran`
  for every project, reproducibly, including for a one-file xunit.v3 project in
  an empty directory. `eng/run-tests.ps1` launches the test applications
  directly instead. Retry `dotnet test` after an SDK bump.
- **No exporter has an options dialog.** Class name, namespace and comment
  settings come from the file name and defaults. Each dialect is a separate menu
  entry, which covers the choice that actually matters, but the rest is not
  reachable from the UI — only from the CLI and the API.
