# Status and roadmap

Last updated after the shell work. Phases 0, 1, 2, 3, 4, 5, 7, 8, 9 and 10 are
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
| Plugins | **Removed.** The three shipped exporters were the only thing it carried |
| Exporters | One layout IR, four converters: raw client layout, POL, RunUO and Sphere. The RunUO *importer* and Wolfpack stay dropped |

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

Done. 484 tests across the solution; 6 skip when the single-client
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

### Cut, copy and paste

On the Edit menu, the context menu and the usual three shortcuts.

Elements travel as **XML text**, written by the same serializer the document
format uses, so a copied element carries everything a saved one does and there
is no second format to keep in step. Text on the clipboard also means a copy
survives between instances, can be read by pasting it into an editor, and cannot
carry anything executable — which the original's `BinaryFormatter` payload could,
and which is a large part of why that format had to go. Text that is not ours is
an ordinary outcome, not an error: the clipboard holds whatever was last copied
anywhere.

Two departures from the original, both deliberate:

- **A paste is offset** by ten pixels, or by one grid cell when snapping is on.
  The original pasted at the original coordinates, so pressing paste twice
  silently buried one copy under another with nothing on screen to say a second
  had appeared. Use **Move to page** when the point is to keep the position.
- **Elements are cloned on the way in.** The original added the clipboard's own
  objects, so a second paste re-parented the first paste's elements instead of
  duplicating them.

Avalonia 12 replaced `SetTextAsync`/`GetTextAsync` with a data-transfer object.
It is handed to the clipboard rather than disposed locally, because the clipboard
takes ownership and may call back into it, and `FlushAsync` is called so a copy
outlives the process on Windows.

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
Ctrl+G all did nothing. They are real `KeyBindings` on the window now.

> **Corrected later.** This section originally claimed that a control which had
> already handled the key still won, so a text box would swallow Ctrl+A or Delete
> while the caret was in it. That is not true of Avalonia 12 — see
> [Phase 9](#phase-9--a-preview-worth-trusting-).

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

- ~~A **hue picker**.~~ Built in [Phase 9](#phase-9--a-preview-worth-trusting-):
  a searchable dropdown of colour ramps on every hue row.
- ~~A **cliloc browser** for the HTML element's localised id.~~ Built in
  [Phase 11](#phase-11--finding-a-cliloc-): a dockable panel that searches
  ~124,000 strings by text or by id, a hover preview on every cliloc field, and
  a language selector the original had but never wired up.
- Drag-to-reorder in the element list. The four ordering commands cover the same
  ground from the keyboard and the context menu.

## Phase 5 — The POL exporter ✅

> **Superseded in part by [Phase 7](#phase-7--one-layout-ir-four-converters-no-plugins-).**
> The plugin contract described here was removed: it existed almost entirely to
> carry the exporters, which are ordinary referenced converters now. The POL
> output itself, and every correction listed below, survives unchanged — the
> goldens prove it.

Done ahead of Phase 4, because the plugin contract is UI-agnostic by design and
therefore does not need the shell. Doing it first means the shell can wire up a
real exporter rather than a stub. 43 POL tests, now in
`GumpStudio.Converters.Tests`.

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

- ✅ **NativeAOT publishing.** `eng/publish-aot.ps1` produces a single native
  binary — 23 MB on `win-x64`, no runtime to install and no managed assemblies
  beside it, just the native Skia, HarfBuzz and ANGLE libraries Avalonia needs.
  Verified running: it opens the editor, reads client art and renders a document
  identically to the ordinary build.

  Nothing in the element model, the UO data layer, the renderer or the Avalonia
  shell produced a single trim or AOT warning. Everything that stood in the way
  came from two places outside them.

  **Plugins cannot be loaded at all.** A plugin is an assembly the build never
  saw; a trimmer has no way to know which of its types matter and a NativeAOT
  image cannot load an assembly whatsoever. `PluginLoader` is annotated
  `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` to say so, which makes
  the restriction propagate to anything that calls it instead of surfacing as
  warnings inside the loader. An AOT build therefore links the three shipped
  exporters in and registers them through the existing
  `RegisterBuiltInPlugins`; ordinary builds are untouched and still discover
  plugins on disk, which is also how a third-party one arrives.

  **`System.Formats.Nrbf` has an annotation defect.** In 10.0.11,
  `SZArrayRecord<T>.Deserialize` overrides a `RequiresDynamicCode` member without
  carrying the attribute, and `IL3051` is raised whether or not the method is
  reachable. The legacy importer never calls it — it walks the records by hand
  with `GetArray` and `GetRawValue`, and deliberately never activates a type — so
  that one id is suppressed for the AOT publish, with the reason recorded beside
  it. Worth removing when the package is fixed.

  On Windows the ILCompiler shells out to `vswhere.exe` to find the MSVC linker,
  and it is not on `PATH` outside a developer prompt: the link step then fails
  with `'vswhere.exe' is not recognized` long after compilation has succeeded,
  which reads like a compiler problem and is not one. The script puts it on
  `PATH` and fails with a clear message if the C++ workload is missing.

- ⬜ **`src/` is deliberately still here.** The plan made deleting it conditional
  on reaching parity, and parity is not reached: the art, hue and cliloc
  browsers, the clipboard and the plugin manager are not built, and `src/` is
  the reference for all of them. Deleting it now would throw away the only
  description of behaviour that has not been ported yet. It should go once the
  gaps below are closed.
- ⬜ No release tagged.

---


## Phase 7 — One layout IR, four converters, no plugins ✅

Each exporter used to walk the document itself. That meant three page loops,
three copies of the radio-group tracker, three text-slot allocators, and three
element `switch`es with a `default` arm — the failure mode `IElementVisitor` was
introduced to prevent, and which no exporter actually used.

The client's layout grammar was hand-written **four** times. `pol-layout` and
`sphere-056` emit it, and the other two dialects build it as well, for the notes
they leave beside commands they cannot express: `PolScriptBuilder` carried eight
private `LayoutXxx` helpers whose only purpose was to feed `Unsupported()`.

Now `GumpDocument` → `GumpLayoutBuilder` → `GumpLayout` → one of four converters.
**The IR carries meaning; a converter owns syntax.** Absolute coordinates, page
boundaries, radio-group scoping, text-slot allocation and tooltip ordering are
decided once. Escaping, flag spelling and identifier rules stay per target, which
is right: the same flag is `NOCLOSE` in Sphere 0.56, `NoClose` in 0.99 and in POL.

A dimension-by-dimension diff of the two raw dialects found the element commands
**byte-identical** — including `xmfhtmltok`, whose parameter order genuinely
differs from the colour form. Only the surrounding policy differed, and that is
now two fields on `LayoutStringOptions`: how `group` is spelled, and whether
`endgroup` is emitted at all.

### How it was kept honest

The whole refactor was done against **byte-exact goldens**, generated before it
started from a fixture that exercises every element type, both button kinds,
every HTML form, nested groups, several pages and a radio group repeated across a
page boundary. Every builder already took an injectable timestamp, so the output
is reproducible and a diff means something. The goldens did not move once.

The ~1,800 lines of existing export tests also passed **unmodified** through the
conversion, because each builder kept its `Build(document, …)` entry point and
only its internals changed. They are in `GumpStudio.Converters.Tests` now, along
with a test that asserts the shared writer reproduces both raw dialects exactly.

### Three things that would have broken it quietly

- **Value equality on commands.** RunUO joins a pre-pass back to individual
  commands to build its `Buttons` enum. Commands are records, so two reply
  buttons with the same name, response id and position compare equal — the map
  would collapse and the generated switch would name a member nothing emits. The
  join is keyed on a stable `Ordinal`.
- **Baking text indices into commands.** The gump package hard-codes index `0` in
  its notes, because that output has no data array for a real index to point
  into. Text slots are resolved by the writer, not by the builder.
- **Turning gump properties into commands.** RunUO needs typed values, emits the
  movable/closable/disposable flags unconditionally where the others emit tokens
  only when false, and emits nothing at all for `toggleupperwordcase`.
  `GumpProperties` stays structured on the layout.

### The plugin system is gone

It existed almost entirely to carry three exporters, and its only other extension
point — `RegisterMenuCommand` — was never called. It cost a project, a
collectible `AssemblyLoadContext`, `RequiresUnreferencedCode` and
`RequiresDynamicCode` annotations that propagated to every caller, a second
registration path behind `#if STATIC_PLUGINS` because a NativeAOT image cannot
load an assembly at all, ~60 lines of MSBuild to deploy the assemblies, and three
near-identical loader test files. A new exporter had to be added in three places
— the plugin's `Initialize`, the `STATIC_PLUGINS` list and the CLI's dictionary —
or it silently went missing from one build.

Converters are ordinary referenced code now, so **an AOT publish and an ordinary
build run the same path**, and adding one means adding it to a single list. The
AOT image still builds clean at 23.4 MB with no trim or AOT warnings, and a
self-contained publish no longer produces an empty `Plugins` folder.

### Four formats, and a dialect you can choose

The export menu had six entries, which read as six unrelated formats rather than
four servers. It has four now — one per converter — and the dialect is asked for
in an **export options dialog**, which also closes a gap listed below: `Namespace`
and `IncludeComments` previously always took their defaults in the editor and were
reachable only from the CLI and the API. The dialog opens *after* the file picker,
because the gump name defaults to the chosen file name.

The new **`layout`** format is the fourth converter: the client's own command
list and the data array it indexes, belonging to no particular server. It is what
shard authors already paste between tools, and it is the quickest way to check a
suspected export defect without reading generated C# or POL.

The CLI takes `--format <converter> [--dialect <id>]`. The six ids earlier
releases used are a published surface, so `pol-layout`, `runuo-numeric`,
`sphere-056` and `sphere-099` still work and select the same converter and
dialect as before.

### Two latent defects, deliberately left alone

Both would have been silently "fixed" by sharing the builder, so both are
recorded rather than changed:

- **RunUO emits one `TextRelay` local per text entry, undeduplicated**, while its
  button `case` labels *are* deduplicated with a comment explaining why. Two text
  entries sharing an `EntryId` produce a duplicate C# local, which does not
  compile.
- **Sphere emits one `ON=` block per reply button**, so two buttons sharing a
  `Param` produce two handlers for the same id.

Deduplicating both is almost certainly right, but it moves the goldens, so it
belongs in its own commit with its own test.


## Phase 8 — Importing a gump captured off the wire ✅

Shard authors capture gumps with a packet sniffer, which dumps the layout string
the server sent. That is the same grammar the `layout` converter writes, so
importing one is the export pipeline run backwards over the same IR:

```
layout text ──► LayoutStringParser ──► GumpLayout ──► GumpLayoutReader ──► GumpDocument
```

Reached from **File ▸ Import gump layout…**, which opens with the clipboard's
contents already in the box when they look like a layout, and from
`gumpstudio import`, which reads a file or standard input.

The dialog keeps the text editable and previews what it would produce — element
and page counts, and the first thing it could not use — before anything is
committed. Captures are routinely truncated mid-command, and being able to delete
the broken line beats being told the paste is unusable.

### What the real capture taught it

Written against a genuine dump of the character-profile gump, checked in as
`tests/GumpStudio.Core.Tests/Captures/wire-capture.txt`. It is the only evidence
available of what these files actually look like, and almost every leniency in
the parser is there because of something in it:

- **Page 0 is opened five separate times**, and the numbers used skip from 2 to
  9. Pages are merged by number rather than appended per command, and the gaps
  are filled with empty pages so page buttons keep pointing where the server
  meant. A ceiling of 255 stops a malformed capture asking for millions.
- **A doubled space after a command name.** Runs of whitespace are one separator.
- **`@0@10` with no closing delimiter**, alongside `@#1027027` and `@@#1072325`.
  Every leading and trailing delimiter is stripped, not just one, and a run of
  them in the middle counts as a single separator — the client tokenises the list
  the way `strtok` does. Reading a doubled `@@` as an empty first argument left
  `<DIV ALIGN=CENTER>~1_TOKEN~</DIV>` substituting nothing, so a quest-offer gump
  previewed with its title missing.
- **A text block holding one empty entry**, indexed by nothing. A gump built
  entirely from clilocs has no strings of its own, so a missing or truncated text
  block is the ordinary case, not corruption.

The braces, the `[layout]` and `[text]` markers and the header comment are all
optional, and several commands may share a line — captures are not written by one
tool. Nothing throws: an unknown command or an out-of-range text reference is
reported and skipped.

The header a capture tool writes — `// Gump 0x1CC at (50, 50) — serial 043CAD798`
— gives the gump id and where it opens, both of which are kept. The serial
belongs to the session that captured it and is dropped.

### Proof it is the inverse of exporting

A round-trip test writes the exhaustive sample document as layout text, reads it
back, and writes it again: the two must be identical. A second test re-exports
the imported real capture and compares every element command against the ones it
came from.

End to end, the capture imports as 140 elements across 11 pages, renders against
a real client, and exports as a compiling RunUO gump.

### Two things that cannot survive, by design

- **Groups.** The client has no notion of one, so an imported document is flat.
  This costs nothing: a group only affects its children's absolute positions, and
  the layout has already resolved those.
- **Element names and comments.** They never reach the wire, so a capture has
  none to recover.

### A gap this made visible

The renderer drew a localised text area as its bare cliloc id, which a captured
gump made glaring. Fixed in
[Phase 9](#phase-9--a-preview-worth-trusting-).


## Phase 9 — A preview worth trusting ✅

Reported from using the editor on an imported gump: shortcuts leaking into text
boxes, cliloc ids instead of text, one font for everything, hues and fonts as bare
numbers, and HTML colours ignored.

### A window shortcut fires even when a text box has handled the key

Copying the cliloc id out of the property panel put the *element's XML* on the
clipboard instead of the number. The cause is worse than the symptom: **an
Avalonia 12 window `KeyBinding` runs even when the focused control has already
marked the key handled.**

Verified rather than assumed, with a headless probe — a `Window` with one
`KeyBinding` and a focused `TextBox`:

| Gesture | TextBox marked handled | Window command still ran |
|---|---|---|
| Ctrl+C, Ctrl+X, Ctrl+V | yes | **yes** |
| Ctrl+A | yes | **yes** |
| Delete | yes | **yes** |
| Ctrl+Z | yes | **yes** |

So every one of them leaked, and **Delete was the dangerous one**: pressing it
while editing a property deleted the selected element. Only the clipboard case
was reported, because it is the only one that announces itself.

The shortcuts that a text box owns — Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+A, Delete,
Ctrl+Z, Ctrl+Y — now check the focused element and do nothing when it is a
`TextBox`. The rest (Ctrl+N, Ctrl+O, Ctrl+S, the grouping and z-order ones) mean
the same thing wherever the keyboard is and are left alone. There is nothing to
opt into that makes bubbling stop, so checking focus is the fix, not a workaround.

This also corrects a claim in Phase 4, which stated the opposite.

### A localised area showed its cliloc id

`#1044017` rather than `MARK ITEM`. Tolerable while gumps were authored by hand
and the author had just typed the id; useless for a gump imported from a wire
capture, which is often built from nothing but clilocs — the character-profile
capture previews as a wall of numbers.

`IGumpArtSource` gained `GetCliloc`, defaulted to null so a source that only
supplies art keeps compiling, and the renderer resolves the id. Falling back to
`#id` is still right when no client is loaded or the id is absent.

Substitution came with it, because `xmfhtmltok` is common in captures:

- `~1_NAME~` placeholders are filled from the argument list, which the client
  numbers from one and separates with `@`.
- **An argument of the form `#1234` is itself a cliloc id.** That is how one
  localised string nests inside another; the captured bulk-order gump uses it to
  name the item, and without it the preview reads `~1_val~`.
- A placeholder with no argument is left as it stands rather than blanked, so a
  missing value stays visible.

The bulk-order capture now previews as real English: "A bulk order", "Amount to
make:", "hammer pick", "Do you want to accept this order?".

### Choosing the preview font

Reported after the cliloc work landed: every text element previewed in the same
ornate face, with no way to change it. It was Unicode font 0 — blackletter, hard
to read at gump sizes, and not what the client uses for body text. Only
`LabelElement` honoured a font at all; localised areas and text entries had it
hard-coded.

`IFontedElement` now carries a family and an index, implemented by
`LabelElement`, `HtmlElement` and `TextEntryElement`, editable in the property
panel and round-tripped through the save format.

**Both families are reachable.** `AsciiFonts` had been loaded since Phase 1 and
never drawn with — the gap this page recorded against the quinted build. A
current client ships 13 Unicode faces and 10 ASCII ones; the Unicode set is worth
knowing, because six of the thirteen are runic and unusable for a menu:

| Unicode | 0 ornate blackletter · 1 plain · 2 tiny · 3 bold sans · 4 large sans · 5 small sans · 6 medium sans · 7-12 runic |
|---|---|
| **ASCII** | the ten `fonts.mul` faces, which are the older UO look |

The default moved from 0 to **1**, the plain face. Nothing an exporter writes
changes — the protocol's text commands carry no font, which the untouched
exporter goldens confirm — so this is purely about the preview being readable.
Labels always stored an explicit `font` attribute, so no saved document changes;
HTML areas and text entries never did, and pick up the new default.

### Picking a hue or a font by looking at it

Reported once the font became settable: both are still numbers, and nobody knows
what hue 1153 or font 4 looks like.

The **Hue** and **Font** rows are searchable dropdowns now. Each row draws what it
means — a hue as its own colour ramp, a font as a line of text set in it — and
typing filters by index or by name, so `blue` finds `1155 — dark blue`,
`1162 — Purpleblue` and the rest, and `ascii` narrows the fonts to that family. A
swatch beside the field shows the current value without opening the list, which
is the state it is in most of the time.

Three defects in the pickers themselves, all reported from use and all confirmed
by driving the real window:

- **The hue picker was off by one.** `Hue.Index` is the position in `hues.mul`,
  but an element and a gump script store the one-based value that
  `HueTable.Get` turns back into that position — the picker offered the raw
  index. So every row was labelled with the wrong number, showed its
  neighbour's colours and name, and wrote a value one lower than the one
  displayed. Measured before and after on hue 1152: the swatch ran
  `140,140,140 → 228,228,228`, a pure grey, where the element on the canvas was
  blue; it now runs `96,249,252 → 18,46,167`, and the name reads `ice_hue`
  rather than `ice_hue_2`. Adjacent hues look alike, which is why this survived a
  glance. `Hue.ScriptValue` names the conversion now so the two cannot be
  confused at a call site, and a client-free test pins the round trip.
- **Clicking a row did not select it**; only typing a value worked. Clicking
  takes focus off the field *before* the selection is reported, so the
  lost-focus handler ran first, found the search term was not a number, and put
  the previous value back — cancelling the click. It is posted now, so the
  selection lands first and there is nothing left to correct.
- **The list only opened once something was typed.** An `AutoCompleteBox` drops
  down when its text *changes*, so clicking a field showed nothing — which is the
  opposite of what a picker is for, and worst for fonts, where the number tells
  you nothing at all. It is opened explicitly on focus now.

Three more details that only showed up the same way:

- **The field's own label is not a search term.** It reads `1152 — ice_hue` when
  idle, so typing into it filtered on that whole string and matched nothing. It
  empties on focus instead.
- **The popup grew and shrank while it was scrolled.** The list virtualises, so a
  row is measured only once it comes into view, and hue names run from `none` to
  `Hue (2054→24191)`. Every row is a fixed width now, with the label trimmed
  rather than allowed to push it wider.
- **Typing a bare number still works**, which matters when there is no client
  loaded and so no rows to match against. That is how the field behaved before it
  became a picker, and how a value copied out of a server script gets in.

The preview beside the field is sized per kind: a hue needs only a swatch, but a
font sample is a line of text and is illegible in the same space.

The font row also replaced the separate family and index rows it had briefly: one
list of every face in both families reads better than two controls that have to
agree.

### An HTML colour is not a hue

`xmfhtmlgumpcolor` and `xmfhtmltok` carry a colour, and the renderer ignored it —
every localised area drew white.

The client mixes two colour encodings, and the decompiled client notes call
confusing them "the most common source of wrong assumptions": a **hue index**
looks up `hues.mul`, an **RGB555** value never touches it. This slot is the second
kind. Servers write it both ways, and one captured gump uses both in the same
definition — `32767` is `0x7FFF`, RGB555 white, and `16777215` is `0xFFFFFF`,
24-bit white.

So a value that fits in fifteen bits is read as RGB555 and anything larger as
24-bit RGB. That resolves both spellings of white, and keeps an ordinary
`#RRGGBB` red from collapsing to near-black, which masking into RGB555 would do.
Five-bit channels expand so that full saturation reaches 255 rather than 248 —
otherwise white comes out faintly grey next to real white.

The reference documents the packing but not which form the gump path accepts, and
its notes describe a different build from the installed ones, so this is a reading
of what servers actually send rather than a transcription.

### Picking a text colour

The colour slot had no editor beyond a raw integer. **Gump ▸ the HTML element's
Color row** now carries a swatch and a picker.

Its channels run **0 to 31, not 0 to 255**, because that is the client's real
resolution — the slot is RGB555, so a picker offering eight-bit channels would
promise sixteen million colours where there are 32,768 and make two nearby picks
identical. There are presets for the dozen colours worth reaching for, and the
dialog states the value it will write.

It writes the **RGB555 spelling**. The reference documents the packing but never
says which form the gump path accepts, so the reading comes from what servers
send: the captured profile gump uses `32767` and `16777215` side by side for
labels that are plainly both white, and `0x7FFF` only means white if the client
masks to fifteen bits — under a 24-bit reading it would be a light blue that no
author would put beside white text. Reading still accepts either spelling, and
the field stays editable so a value copied out of a script goes straight in.

### Ctrl+C in a property field copied nothing

A follow-on from the shortcut fix, and a sharper lesson than the original bug.
Yielding was implemented as an early return *inside* the command — but **a
`KeyBinding` marks the key handled whenever it executes**, so a command that ran
and did nothing still swallowed the keystroke. The window stopped copying the
element, and the text box never got the key either, so Ctrl+C did nothing at all.

Yielding is `CanExecute` now: the binding declines to run, the key is left
unhandled, and it reaches the text box. Verified by injecting real keystrokes and
reading the clipboard — `15` from the field, and the element XML when the canvas
has focus.

### Enhanced Client commands

The same capture carries four `kr_xmfhtmlgump` commands — Enhanced-Client-only,
with a cliloc of -1 and no geometry, ignored by the classic client. They are
named as such rather than reported as unknown, because nothing is wrong with the
capture. Repeated problems also collapse to one warning with a count: a capture
can carry dozens of the same line, and forty near-identical warnings bury the one
that matters.

### Panels that go where you want them

The shell was a `Grid` of fixed columns: toolbox left, canvas centre, element
list and properties stacked on the right, with splitters between them. The
proportions were adjustable and nothing else was — a panel could not be moved,
tabbed with another, torn off onto a second monitor, or collapsed out of the way
while laying out a wide gump.

[Dock](https://github.com/wieslawsoltes/Dock) replaces that grid. The four
panels are dockables inside a `DockControl` declared in `MainWindow.axaml`: the
gump is a document in the centre, and the toolbox, element list and properties
are tools that can be dragged into any edge, tabbed together, pinned to a strip,
or floated into their own window. Dock is MIT-licensed and ships a build for the
Avalonia version this uses.

None of the panels can be closed. Closing a tool hides it, and nothing brings
one back yet, so a stray click on a tab's close button would cost a panel for
the rest of the session.

The panels moved out of `MainWindow.axaml` into four views — `CanvasPanel`,
`ToolboxPanel`, `ElementsPanel` and `PropertiesPanel` — because Dock treats what
a dockable holds in XAML as a template it builds when the tab is first shown.
That puts the controls in their own name scope, created at a time the window
constructor cannot wait for, and `FindControl` for `Canvas` or `ElementList`
found nothing. The constructor now builds each view itself and hands it to its
dockable as the content factory Dock asks for, closing over the one instance, so
the fields it keeps always point at controls that are really on screen.

Reparenting relies on control recycling, registered in `App.axaml` and keyed on
the dockable's id. Without it Dock asks for content again after every move and
gets the same control a second time, which cannot be in two places at once. With
it a tool dragged to another dock — or out into a floating window — carries the
live panel across, selection and scroll position intact.

The NativeAOT publish needed one accommodation. Dock's Fluent theme binds a
dockable's title and close button by name rather than with compiled bindings,
and its container generator reflects over whatever an `ItemsSource` gives it, so
ILC rolls all three Dock assemblies up into `IL2104`/`IL3053` — which
`TreatWarningsAsErrors` then turns into a failed publish. They are suppressed
for the AOT publish only. The published binary was run afterwards: the layout,
the tab titles and the floating windows all behave as they do in a normal build,
which is what those bindings drive.

### Two pipelines, and something to download

CI was one workflow that built and tested on Windows and Ubuntu, triggered by
pushes and pull requests **on `main`**. Since the whole rewrite lives on a
branch, it had never run for any of it. Nothing published: `eng/publish-aot.ps1`
was a hand-run developer script, host RID only, editor only, no version, no
archive.

There are two workflows now, following the shape UOFiddler uses.

`build-pr.yml` runs on a pull request into **any** branch, so work that never
touches `main` is still gated. It tests on both operating systems as before, and
adds a win-x64 AOT publish that keeps nothing. That job exists because of the
Dock upgrade: a dependency that starts emitting trim warnings turns
`TreatWarningsAsErrors` into a failed publish, and that should surface in review
rather than when someone is trying to cut a release.

`build-and-release.yml` runs on pushes to `main` and on version tags. It tests,
then publishes NativeAOT for win-x64 and linux-x64 — one runner each, because
NativeAOT compiles for the machine it runs on and cannot cross-compile — and
archives each as `GumpStudio-<version>-<rid>`. On a tag, and only on a tag, the
archives are attached to a GitHub release. The tag is the version: it is stamped
into the binaries with `-p:Version`, names the archives, and a `-` in it marks
the release as a prerelease. Untagged builds fall back to the `Version` in
`Directory.Build.props`.

Windows gets a zip and Linux a tarball. A zip has nowhere to record the
executable bit, so a Linux download would arrive unrunnable — and it is the one
thing CI cannot catch for itself, since the job that built the archive never
unpacks it.

Publishing both binaries meant the CLI had to survive NativeAOT, which nothing
had ever asked of it. It did not: `System.Formats.Nrbf` produces the same
analysis warnings there as in the editor, and the editor was suppressing them in
its own csproj. The suppression moved to `Directory.Build.props`, behind
`PublishAot`, where it now covers both apps and carries one explanation instead
of two. What it hides is still narrow — an assembly roll-up only covers code
shipped in a package, so this repository's own IL2026 and IL3050 keep failing
the build.

The published layout also stopped shipping its own documentation: with
`GenerateDocumentationFile` on, the XML docs for five projects outweighed the
binaries they described. Symbols and doc files are off for a release publish,
and the output folder is cleared first so a stale binary from a RID no longer
built cannot ride along in the archive.

Still missing: there is no `LICENSE` file in this repository, and these
workflows put binaries in front of the public.

### The Simple theme, and two workarounds it retires

Fluent was never chosen so much as inherited — it is what a new Avalonia project
starts with. This is a tool for laying out a 1997 client's interface, and its own
chrome had drifted a long way from the thing being edited: rounded corners,
animated controls, and generous padding around a canvas that wants the space.

`SimpleTheme` and `DockSimpleTheme` replace `FluentTheme` and `DockFluentTheme`.
Sharper, denser, and closer in spirit to the artwork on screen.

It also deletes work rather than adding it. Two accommodations existed purely
because of how Fluent draws a scroll bar:

- The **floating scroll bar**, drawn over the content it scrolls, landed on the
  `…` button at the end of a gump- or item-id row. That cost the property
  scroller an `AllowAutoHide="False"` and its content a matching right margin.
  The Simple theme's `ScrollViewer` gives the bar a grid column of its own, so
  the rows end where the bar begins and both are gone.
- The **hairline scroll bar** that only animated to full width on hover, which is
  a poor trade in a picker where you scroll far more than you point. Forcing it
  wider took five styles in `App.axaml`, one of them reaching into Fluent's
  template for a `Rectangle#TrackRect` that the Simple theme does not have. The
  Simple bar is a classic bar with line buttons at full width already, so all
  five are gone.

The theme swap touches nothing else. The hard-coded panel colours were picked to
sit against a dark shell and still do, and the AOT suppressions did not move: the
Simple theme binds a dockable's title the same way its Fluent sibling did, and
the published binary was rebuilt to confirm it.

## Phase 10 — The shell catches up ✅

Phases 0 to 9 built the element model, the data layer, the converters and the
importers, and every one of them is covered by tests. The shell was not: it had
no test project at all, two ways to lose work, and it did enough per mouse-move
to be visibly slow at the one thing the application is for.

### A test project for the shell

`Avalonia.Headless` had been pinned in `Directory.Packages.props` since the
Avalonia bump and referenced by nothing, so roughly 3,700 lines of UI —
including the 1,708-line `MainWindow.axaml.cs` — had no regression cover at all.
Every Avalonia-12 behaviour the last two phases worked out by hand was an
unguarded assumption.

`tests/GumpStudio.App.Tests` uses it. `HeadlessUnitTestSession` is started from
the real `App`, not a bare `Application`: the window under test is built out of
Dock's controls and those need the themes `App.axaml` loads.
`OnFrameworkInitializationCompleted` only creates a main window for a classic
desktop lifetime, which a headless session does not provide, so starting the
real application touches nothing.

`MainWindow` and `EditorSession` gained constructors that take what they used to
build themselves, so a test can hand them settings pointing at a scratch file
instead of the real one in the application-data folder.

It earned itself immediately. The first version of the layout restore **crashed
on startup**, and only running the application found it — see below.

### Removing a page destroyed it

`RemovePage` called `GumpDocument.RemovePage` directly. Adding a page did the
same. Neither went through `History`, so removing a page took every element on it
with no way back — the one action in the editor that destroyed work outright, and
the very failure mode the undo history exists to prevent.

`source/GumpStudio.Core/Commands/PageCommands.cs` adds `AddPageCommand`,
`InsertPageCommand`, `RemovePageCommand` and `ClearPageCommand`. A removed page
is held by the command, so undo restores the same instance with its elements in
their original order. `GumpDocument.InsertPage` already existed with no menu item
behind it, so *Page ▸ Insert here* and *Page ▸ Clear* came almost free.

Two details are deliberate. `AddPageCommand` records the index it inserted at
rather than assuming undo will find its page last, and
`RemovePageCommand.ActiveIndexAfterRemoval` is computed once in the constructor —
read after `Execute` it would otherwise have counted a page that was already
gone.

### Nothing knew the document was unsaved

`EditorSession` had no notion of modification, so *New*, *Open*, both importers
and *Exit* discarded the document silently, and the title bar was the constant
string `GumpStudio`.

`IsModified` is derived from the undo history rather than a flag each mutation
sets, so undoing back to the saved point reports clean again. The undo cursor
alone cannot answer that question: undo one step, apply a different change, and
the cursor returns to the number it had at save time over a document that no
longer matches it. `UndoHistory` therefore stamps each retained command with a
sequence number and exposes `StateId`, the stamp at the cursor. That also
survives the trimming `Capacity` forces, which shifts every index.

Deriving it this way is only honest because every change now goes through the
history — which is why the page commands above had to come first.

`ConfirmWindow` asks before a discard. The project has no message-box
abstraction on purpose, errors going to the status bar instead, so this follows
the shape of every other dialog here: a modal with a result property read after
it closes. `OnClosing` asks as well, for the window's own close button, and
cancels the close to await the answer because a dialog cannot be awaited inside a
synchronous `Closing` handler.

### Choosing a client folder reset every other preference

`AppSettings.Save` writes the whole file, and one call site passed a freshly
constructed instance:

```csharp
AppSettings.Save(new AppSettings { ClientPath = path });
```

So picking a client folder silently reverted the grid size, grid visibility,
snapping, both art-browser preferences and every remembered export dialect. The
other eight call sites did load-modify-save.

The line was not the defect; the shape was. Settings were re-read from disk at
**eight** separate places across `MainWindow` and `ArtBrowserWindow`, so any of
them could have grown the same bug. `EditorSession` now holds one instance and
hands it to the art browser, and `AppSettings` carries the path it was read from
so `Save` writes back where it came from — which is also what lets a test work
against a scratch file.

Adding a property to `AppSettings` then exposed an older trap. With
`init`-only properties the JSON source generator assigns every member it knows
about while constructing the object, so a settings file written before a property
existed deserialises it as **null** and the initialiser never survives. Every
settings file already on disk lacked `Layout`, so the first run crashed on it.
`ExportDialects` had exactly the same shape and the same latent
`NullReferenceException`, waiting for anyone whose file predated it. Both setters
now refuse null.

### `.gumpling` was offered and could only fail

The import picker had advertised `*.gumpling` from the start, but every chosen
file went to `LegacyGumpImporter.ImportDocument`, which only understands a whole
document. `ImportGumpling` existed with **zero callers**.

They are not the same operation: a gump replaces the document, a gumpling is one
saved group added to the page that is open. The import now dispatches on the
extension, adds the group through the undo history, and asks about discarding
only for the case that discards something — after the file has been chosen, so
cancelling the picker costs no question. `LegacyGumpFixture` gained a gumpling
builder, which is what proved the importer works: it had never been executed.

The full gumpling *library* — the docked tree, the folders, export — is still
absent.

### Dragging an element rebuilt the inspector, per mouse-move

`CanvasInteractionController.PointerMoved` raises `Changed` on every pointer
move. The window wired that to `RefreshSelection`, which begins
`_propertyPanel.Children.Clear()` and then allocates a `Grid`, its column
definitions, a label, a tooltip and an editor control with one to three handler
closures — for every property of the selected element. Around fifteen controls
built and thrown away per mouse-move event, on top of a full re-render.

Mid-gesture the shape of the panel cannot change, only the numbers in it, so the
values are pushed into the editors already on screen. The selection is still
compared, because pressing a different element begins a drag and changes the
selection in the same gesture. A focused field is left alone: it holds what is
being typed, which the element does not have yet.

### The grid was thirty thousand draw calls a frame

`DrawGrid` drew a one-pixel rectangle per intersection. At the default 5×5
spacing over the design surface that is about 31,500 `DrawRect` calls, every
frame, and the existing comment acknowledged the cost while only guarding
against spacings below three pixels.

It is now one cell in a small bitmap, tiled through an `SKShader` with repeat
mode, filled as a single rectangle. Measured over sixty frames at 1024×768:

| | per frame |
|---|---|
| A rectangle per dot | 7.01 ms |
| One tiled fill | 0.56 ms |

Seven milliseconds a frame was the grid alone, before anything else was drawn.

`GridRenderingTests` walks every pixel at four spacings and asserts a dot exactly
where the nested loop put one and nowhere else, so the rewrite is pinned to the
placement it replaced rather than to a screenshot.

### Everything else the canvas did every frame

- A `SolidColorBrush` for a constant backdrop colour, a `GumpRenderer`, and three
  `RenderOptions` records — one built and two more from `with` expressions.
  Hoisted; the options are rebuilt only when something in them moves.
- An `SKPaint` per element per frame in `ElementPainter` for the alpha wash, the
  text-entry wash, the missing-art marker and the group outline, plus an
  `SKImage[9]` per nine-sliced element. All now belong to the painter, which
  lives for one page render, and it is `IDisposable` so the native objects go
  with it.
- `page.Descendants().Where(e => e.IsSelected)` per frame — an iterator chain and
  a closure over the whole tree to find the one or two selected elements.
  Replaced with a plain recursive walk.
- **A `Cursor` allocated on every pointer-move.** `CursorFor` returned `new(...)`
  and `UpdateCursor` assigned it unconditionally, even when the mode had not
  changed. A `Cursor` owns a platform handle and none of these were ever
  disposed. Five shared instances now, assigned only when the mode changes.

### The art cache scanned a list on every hit

`UoArtSource` kept a `LinkedList` for recency and looked nodes up by value, so
`LinkedList<T>.Remove(item)` walked up to 512 nodes **on every cache hit**, under
the lock, for every art draw of every frame. It keeps a node dictionary now.

Its bound was also a count, not a size. Entries here differ enormously — a
nine-slice corner is a few hundred bytes and a full-window background is
megabytes — so 512 entries either wasted the cache on small art or held hundreds
of megabytes of large art. It is a 64 MB budget now, the shape the art browser's
own thumbnail cache already used.

### Measuring and painting disagreed about what to fetch

`MeasureContentSizes` asked the art source for text without a hue or a font
family while `ElementPainter` asked with both, so every hued or ASCII label
produced two distinct cache keys: two full text renders and two cache slots for
one element. The same split applied to images and items.

For an ASCII label it was not merely wasteful. The measure pass sized it with a
Unicode face and the paint pass drew it with an ASCII one, so the box was the
wrong size for the glyphs in it.

Gump-backed elements are now measured through `TryGetGumpSize`, which answers
from the index where the container allows it and adds no image to the cache at
all, and a button is measured from the face its state will actually draw.

### Panels that can be hidden, and a layout that survives a restart

Every dockable carried `CanClose="False"`, with the reason in the markup: closing
a tool hid it and nothing brought one back.

Dock's `CloseDockable` removes a dockable from its owner outright, so
`RestoreDockable` would find nothing — enabling the tab's close button really
would cost a panel. `HideDockable` parks it on the root's hidden list instead,
where restore can find it. So the View menu gained a checkable item per panel
and a *Reset panel layout*, and the tabs stay unclosable deliberately.

The layout is remembered in `AppSettings` through the existing source-generated
JSON. Not through Dock's own serialiser: `IDockSerializer` is a contract only,
its implementations ship in separate packages that reflect over the dock model,
and this application is published with NativeAOT and already suppresses Dock's
`IL2104`/`IL3053`. Reintroducing reflective serialisation over the same model
would widen a suppression the CI publish job exists to police.

What is stored is a constrained snapshot: each pane's proportion, which panels
are hidden, and the window's own size, position and maximised state. Tearing a
panel off into its own window, or re-tabbing one beside another, is **not**
remembered and falls back to the declared layout. Every part is validated
independently — a proportion outside a usable range is rejected rather than
clamped, since either extreme collapses a panel to nothing, and a stored position
is only applied if it still lands on an attached monitor.

The window bounds are restored in the constructor and the panels once the window
is open, because the bounds are wanted before the window is first shown while the
dock model is only reliably built by then. Getting that split wrong is what
crashed the first version.

### Zoom, which neither version ever had

The canvas was a fixed 1024×768 at one art pixel to one screen pixel, with
`ToGump` a bare `(int)` cast and nowhere for a factor to go. A gump wider than
the window could not be seen whole, and fine placement meant typing coordinates.

`Zoom` scales the surface and the render transform, and `ToGump` divides by it,
flooring rather than truncating so a press anywhere within a gump pixel belongs
to it. The ladder the menu steps through is fixed rather than multiplicative, and
every step below 1 is the reciprocal of a whole number: gump art is pixel art, so
an arbitrary factor resamples it into a blur while these land art pixels on whole
screen pixels.

Resize handles have to stay the same size under the pointer at every zoom, which
means growing them in gump units as the view shrinks — and the drawing and the
hit testing have to agree on the number, or a handle would not be where it looks.
`HandleGeometry` takes a handle size, `CanvasInteractionController` carries one,
and the canvas sets it from the zoom, rounded up and forced odd so a handle still
centres on its corner under integer halving. The grab margin around an element
grows with it.

### DPI

`EnsureSurface` allocated the backing bitmap at the logical size and stamped it
96 dpi regardless of the display, so everything was under-sampled at any scaling
above 100%. It is allocated in device pixels now, with its dpi scaled to match,
and the drawing scaled once so everything below still works in gump units.

### Opening a client froze the window

`EnsureClientAsync` was `async Task` with no `await` before the blocking work.
`UoDataContext.Open` reads the hue table, the tile data, the cliloc table — twice
on a modern client, since it tries a plain parse before decompressing — and up to
thirteen font files, and walks every index of every UOP container. All of it ran
on the UI thread, including at startup before anything had been drawn.

`OpenClientAsync` moves the reading to the pool and adopts the result back on the
caller's context, so nothing that listens has to think about threads. A full open
of a retail client measures about 320 ms warm; that is now 320 ms of a responsive
window rather than a frozen one.

### The UOP payload memo held one entry

Decoding a UOP entry is an inflate plus, for compression flag 3, a
Burrows-Wheeler pass. A memo of the single most recent payload collapsed the
usual `GetEntry`-then-`Read` pair to one decode — but only while requests arrived
in that order, one at a time. An art browser interleaves indices, and on a
container that keeps its dimensions inside the payload every entry lookup is
itself a full decode, so revisiting an entry meant inflating it again.

It is a byte-budgeted window of 32 MB now, least-recently-used, with a node
dictionary so a hit costs no scan. `UopPayloadWindowTests` covers re-reads,
interleaved reads, reads under `Parallel.For`, and reads of a container larger
than the budget so eviction genuinely turns over — the invariant being that
widening the window changed nothing about what comes back.

Verified against a retail UOP client as well: all 65 real-client tests pass, and
the AOT-published CLI reads 5,571 gumps, 39,516 items and 123,785 cliloc strings
out of it.

### Two more allocation sinks

`UopHash.ComputeForIndex` built two strings per call —
`string.Format(...).ToLowerInvariant()` — and a client open hashes every possible
index of every container, over 140,000 of them, for roughly 295,000 string
allocations. It formats into a `stackalloc` buffer and lowercases as it copies,
`Compute` having always taken a span.

Worth recording honestly: this made **no measurable difference** to wall-clock
client-open time, which stayed around 320 ms. The allocations were real but they
were cheap Gen0 ones, and the cost of opening a client is dominated by I/O and
decompression. It is a GC-pressure fix, not a speed-up. The known-answer tests
against a real package pin the rewrite to identical hashes.

The art-browser filter allocated an `int.ToString()` **per entry per keystroke** —
some forty thousand for the item browser — then rebuilt the match list and
re-chunked every gallery row, with no debounce. It formats into a stack buffer
now, behind a 150 ms debounce.

### Every thumbnail was PNG-encoded and immediately PNG-decoded

Crossing from Skia to Avalonia went through `Encode(SKEncodedImageFormat.Png,
100)` into a `MemoryStream` and back out as a `Bitmap` — a full deflate and
inflate, per art-browser thumbnail and per font sample in a dropdown, for data
already sitting in memory as raw BGRA. The font picker had its own copy of the
same code.

`SkiaBitmap.ToAvalonia` is a pixel copy through a locked framebuffer, the pattern
`GumpCanvas` already used, and both callers share it.

The round-trip had been premultiplying alpha as a side effect of how the two
libraries store pixels, so the copy has to do it deliberately — gump art is full
of soft-edged glyph masks and translucent regions, and getting it wrong shows as
haloes rather than as an error. That conversion is pinned in plain Skia rather
than through Avalonia, because the headless platform substitutes its own bitmap
and reports the platform's format regardless of what was asked for, which makes
it the wrong place to assert a pixel layout. Finding that out took a diagnostic
run: the first attempt at those tests was asserting against the stub.

### The thumbnail cache was first-in-first-out, and never released

Insertion-order eviction discards exactly what is about to be wanted again,
because scrolling down and back up asks for the earliest entries last. It is
least-recently-used now.

`OnClosed` released only the preview. The cached thumbnails each own unmanaged
pixel memory and a browser is constructed afresh on every browse click, so up to
several hundred bitmaps per visit were left to their finalizers. They are
disposed now — except on a tile-size change, where the cache is invalidated but
tiles on screen still hold their bitmaps as an `Image.Source`. Those are retired
and released when the window closes, rather than disposed under a live reference.

### Smaller things

- `Delete` and `Ctrl+A` were handled by both the canvas and the window. An
  Avalonia window `KeyBinding` fires even over a key an inner control has marked
  handled — the quirk Phase 9 documented — so each ran twice. The canvas keeps
  only what is its own: arrow-key nudge and `Escape`.
- The Edit menu never updated. Every item was permanently enabled whatever was
  selected, and undo and redo never named what they would reverse, though the
  context menu had always done both. It refreshes on `SubmenuOpened`, which is
  the only moment the state is about to be read.

### A second pass, after the first one shipped

Most of the above landed in one pass. Five things did not, and one of them
was found by a user pressing a key rather than by anything here. What
follows is that second pass, and its own honest account of the item it
declined.

### The shortcuts that were only painted on

The zoom items shipped with `InputGesture="Ctrl+OemPlus"` and its two siblings on
the menu items and **no bindings behind them**, so the menu entries worked and the
keys did nothing. A user found it by pressing them.

`InputGesture` on a `MenuItem` only draws the shortcut beside the label; it binds
nothing. That is written down a few lines above `BindShortcuts`, because Phase 9
discovered that *every* gesture in this menu bar was decorative and bound them
all by hand. Adding three more painted labels reintroduced exactly the defect
that section exists to describe.

So the shortcuts are bound now, and to more gestures than the menu paints. A
label has to name one gesture, but the key people reach for to zoom in is Ctrl
and `+` — a shifted `OemPlus` — and the numeric keypad has its own key codes
entirely, so binding only the printed form leaves the shortcut working for
nobody who presses the obvious key. Zoom in takes `Ctrl+OemPlus`,
`Ctrl+Shift+OemPlus` and `Ctrl+Add`; zoom out takes `Ctrl+OemMinus` and
`Ctrl+Subtract`; actual size takes `Ctrl+D0` and `Ctrl+NumPad0`.

**The class of bug is closed rather than the instance.**
`ShortcutBindingTests` walks the menus, collects every painted `InputGesture` and
asserts each has a matching `KeyBinding` on the window. Run against the code as
shipped it names exactly three items and no others:

```
MenuZoomIn paints Ctrl+OemPlus with no binding
MenuZoomOut paints Ctrl+OemMinus with no binding
MenuZoomReset paints Ctrl+D0 with no binding
```

It also asserts no gesture is bound twice, since two bindings for one gesture
both fire.

### Ctrl+N and File ▸ New disagreed about unsaved work

The same pass that added the discard prompt wired it to the menu item and left
the shortcut alone: `MenuNew` went through `NewAsync`, which asks, while
`Ctrl+N` called straight through to `NewDocument`, which does not. Two paths for
one action, one of them still silently destroying the document — the cost of
registering every action three times over, realised.

`Ctrl+N` goes through `NewAsync` now.

### The keyboard table, finally tested

Phase 10 listed this as a deliverable and did not write it, which left the one
gap in the area the test project was built for.

`KeyboardYieldTests` drives real keystrokes through
`HeadlessWindowExtensions.KeyPress`, so the table is pinned to what the
application does rather than to how it is written: Ctrl+A on the canvas selects
every element, Ctrl+A with the caret in a text box selects none, and the same for
Delete and Ctrl+Z. The clipboard gestures cannot be driven headlessly, so those
are asserted through `CanExecute` — which is the mechanism that makes them yield,
and the reason yielding is expressed that way rather than as an early return.

### Opening a client no longer reads what it does not need

Phase 10 moved the whole client open onto the thread pool but left it eager, so
first paint still waited for all of it. The cliloc table — around 124,000
strings, parsed twice on a modern client because a plain parse is tried before
decompressing — plus `fonts.mul` and up to thirteen `unifont*.mul` files are now
read on first use. None of the three is needed to draw a gump's first frame.

Measured on a retail UOP client, release build, warm cache:

| | |
|---|---|
| Open: art indexes, hues, tiledata | **89 ms** |
| Cliloc and both font families, now deferred | 152 ms |

So the blocking part of a client open went from about 241 ms to 89 ms, and the
rest happens the first time something renders text.

Two constraints shaped it. `Lazy<T>`'s default `ExecutionAndPublication` thread
safety is load-bearing rather than incidental, because text is rendered from
background art decodes as well as from the UI thread. And `UnicodeFonts` is
`IDisposable`, so `Dispose` checks `IsValueCreated` — reading the property there
would open thirteen font files purely in order to release them again. The paths
are still resolved eagerly in `Open`, so which files an installation is missing
is decided exactly when it was before; only the reading moved.

### The art browser stopped decoding on the UI thread

`UpdatePreview` ran a full inflate, Burrows-Wheeler pass and RLE decode of
**full-size** art inline, and it is reached from every click and every arrow-key
move through the list — a stall per keypress, contending with the background
thumbnail decoder for the same container.

The decode moved to the pool. Arrow-keying produces a burst, so each request
carries a generation number and only the newest result is allowed to land. The
title and the id appear immediately; only the image and its dimensions wait. The
previous bitmap is released *after* the new one is assigned, so the pane never
blanks between two selections and nothing disposes a bitmap still on screen.

### Thumbnail decoding is no longer single file

Every request used to be chained into one strictly serial task, and the comment
explaining why named the one-entry UOP payload memo: parallel readers evicted
each other's memo, so each gump inflated twice instead of once. Phase 10
replaced that memo with a 32 MB least-recently-used window, which retired the
reason.

Three permits now, through a `SemaphoreSlim`, with a `CancellationTokenSource`
reset on rebind and cancelled on close. Cancellation is deliberately tied to
**rebind, not scrolling**: the visible set changes continuously while scrolling
and cancelling there would discard work about to be wanted, which is what the
per-tile tag protocol in `BuildThumbnail` already handles.

Concurrency introduced one hazard that had to be closed with it. Two tiles can
ask for the same id, and `Remember` disposed whatever bitmap it replaced — which
may already be a realised tile's `Image.Source`. So decodes are now deduplicated
by id through an in-flight map, and `Remember` *retires* a replaced bitmap into
the same list a tile-size change uses rather than disposing it. Both dictionaries
stay UI-thread-only because every await resumes on the dispatcher, which is what
makes them safe without a lock.

The window also became `IDisposable`, following `MainWindow`: it owns a semaphore
and a token source now, and the analyzer said so — `CA1001` is promoted to an
error here, and the comment it replaced had noted that a chained `Task`, unlike a
semaphore, was not something the window had to dispose.

### A cancelled decode abandoned its tile

Making thumbnail decoding concurrent introduced a worse defect than the one it
cured, and a user found it: thumbnails appeared only after scrolling away and
coming back, and the browser felt slower than the serial version it replaced.

Decodes are shared by id so two tiles wanting the same art decode it once.
Rebinding the list cancels the current generation, which completes those shared
tasks as cancelled — but the map of running decodes was left populated. A tile
realised immediately afterwards asked for the same id, was handed the cancelled
task, caught the cancellation and **gave up permanently**. Nothing retried, so
the tile stayed empty until its row happened to be realised again. Opening the
browser reflows once as the real width arrives, so the very first screenful was
usually the one abandoned.

`CancelDecoding` empties the map now, and `CanJoinDecode` refuses to join a task
that has already completed unsuccessfully. That predicate is `internal` rather
than private so it can be tested directly: the browser cannot be driven without
a client, and the decision is the part worth pinning.

**What the measurements said, including where they contradicted a guess.**

Decoding was never the bottleneck. A screenful of twenty-eight gumps from a
retail UOP client decodes in **31 ms cold, 1.1 ms each**, against a symptom
measured in seconds — so the cost was all in the plumbing around it.

The first suspicion was that the permit handshake ran on the dispatcher: a tile
acquired its slot, hopped back, decoded, hopped back and released, so a screenful
advanced roughly one dispatcher turn at a time while the dispatcher was busy
laying out the very scroll that realised those tiles. Those awaits now run
`ConfigureAwait(false)`, which is right on its own terms — nothing in that scope
touches a control or the cache — but an A/B against a headless probe measured
**124 ms versus 127 ms**, i.e. no difference. The probe pumps an idle dispatcher
in a tight loop, so it cannot reproduce contention with layout. The change is
kept as a correctness-of-intent improvement, not as a fix, because there is no
evidence it fixed anything.

### The bitmap type went backwards

Replacing the PNG round-trip was a clear win for decoding and a quiet loss for
drawing. The round-trip produced an `Avalonia.Media.Imaging.Bitmap`, which is
immutable; the replacement handed back a `WriteableBitmap`, which is for content
that changes. A render backend has to assume a writeable bitmap's pixels may
differ between frames, so it cannot keep the uploaded texture the way it does for
one declared never to change — and a gallery is static art redrawn on every
scroll frame, which is the worst case for that. It also fits the report that item
art felt better than gump art, item tiles being much the smaller upload.

`SkiaBitmap.ToAvalonia` now builds an immutable `Bitmap` from the premultiplied
intermediate's pointer, so the decode stays a pixel copy and the type goes back
to what it was. This one is reasoned rather than measured: the cost it removes is
per-frame texture upload, which needs a GPU, and the headless platform has none.

The headless platform is worth a warning for anyone testing this area. Its
bitmap stub reports a fabricated `1x1` size and the platform's own pixel format
regardless of what the constructor was given, so assertions about a bitmap's
geometry or format there test the stub rather than the code. The premultiply
behaviour is pinned in plain Skia for that reason, and the geometry is asserted
on the intermediate.

### The window that would not close

Closing a gallery that had been scrolled left the editor unable to close at all.
The cause was the concurrency bound itself.

`SemaphoreSlim.Dispose` must not be called while anything is still waiting on
the semaphore, and closing the browser did exactly that. The queued decodes'
pending `Release` calls then threw `ObjectDisposedException` from inside a
`finally`; that faulted the decode task; its awaiter resumed on the dispatcher
with `ConfigureAwait(true)` and rethrew there; and the handler only caught
`OperationCanceledException`. An unhandled exception in a dispatcher
continuation takes the dispatcher with it, which is why the symptom was a
main window that stopped responding rather than an error.

The semaphore is gone. The bound is a queue and a counter on the UI thread —
nothing to dispose, no handshake, and the bookkeeping is confined to the same
thread as the cache it feeds. It was never buying much: a decode is about a
millisecond and the UOP reader serialises on one lock regardless.

Two rules came out of it, both now written into the code:

- **Nothing started without being awaited may let an exception escape.**
  `ShowWhenDecodedAsync` catches cancellation, and the IO and disposal faults
  that a torn-down browser can produce, because there is no caller to receive
  them — only the dispatcher.
- **`Dispose` has to be idempotent.** `OnClosed` calls it and so does anything
  holding the window in a `using`; cancelling an already-disposed token source
  throws. The first run of the new tests failed on exactly this, which is a fair
  demonstration that they detect what they are for.

`BrowserTeardownTests` covers it against a real client, in both view modes:
open, let a screenful start decoding, scroll to abandon it, close, then pump the
dispatcher and require that it still runs work — and separately that the editor
itself still closes after browsing art. A headless dispatcher is pumped by hand,
so a continuation that throws surfaces out of `RunJobs` and fails the test
outright.

### Evicting a thumbnail must not dispose it

The thumbnail cache carries this instruction on the field itself:

> Entries are dropped rather than disposed: an evicted bitmap may still be on
> screen, and disposing one out from under a realised `Image` tears a hole in
> the panel.

Turning the cache from insertion order into least-recently-used disposed the
evicted entry, breaking that rule in the very field that states it. Eviction
picks the least recently used, and during a long scroll that bitmap can still be
the source of a realised `Image` the virtualiser is holding — so the render pass
was being handed disposed bitmaps, which is consistent with the report that
reading and rendering thumbnails had become poor.

Eviction drops the reference again and lets the collector decide, because
whether anything still holds it is precisely what this cache cannot know. The
bitmaps that genuinely can be released are the ones still held when the window
closes, which is what `DisposeThumbnails` is for and what the original leak
actually was.

### A preview pane that can be given room

The art browser docked its preview at a fixed 260 pixels and drew the image at
native size, so a gump wider than that was cropped with no way to see the rest:
the pane could not be widened, and the window being resizable did not help
because the preview never took any of the extra space.

The list and the preview now share a grid with a `GridSplitter` between them.
Which of the two needs the room depends entirely on what is being looked at —
gump art runs to several hundred pixels across — so it is not something to
hard-code either way. The width is remembered in `AppSettings` and saved as the
drag finishes rather than on close, so it survives the window being dismissed
with Escape and matches how the tile size and the view mode are already kept the
moment they change.

The image is `Stretch="Uniform"` with `StretchDirection="DownOnly"`, which is the
other half of the problem. At native size anything wider than the pane was simply
cropped; now art that already fits is still drawn pixel for pixel and only
oversized art is scaled, with nearest-neighbour sampling so it does not turn into
a blur. That is the same trade the thumbnails have always made.

A stored width is clamped rather than rejected — unlike a dock proportion, any
width within range is usable, so the nearest one is the right answer. The floor
exists because a narrower pane shows nothing useful and reads as broken rather
than collapsed, and the ceiling so a width stored on a wide screen cannot leave
the art list with no room on a smaller one.

### Gallery rows are still not recycled, and here is why

The gallery template is still built with `supportsRecycling: false`, so every row
scrolled into view constructs a fresh `StackPanel` and, per tile, a `Border`, a
`StackPanel`, an `Image`, a `TextBlock`, a tooltip and two handler closures —
around forty controls per row, discarded on the way out.

It was attempted and deliberately abandoned. `BuildTile` closes over its entry
for the tag, the tooltip, the highlight and both pointer handlers, and a
recycling template hands the *same* control a *different* item — so every one of
those captures goes stale and tiles select the wrong art. That is a correctness
bug, not a cosmetic one, and fixing it properly means a row control driven by its
`DataContext`, which then has to interleave correctly with three mechanisms whose
reasoning is written down: the tag-based thumbnail staleness protocol, including a
`DetachedFromVisualTree` handler that clears the tag exactly when recycling
detaches and reattaches; the `RepaintTiles` visual sweep that drives selection
highlighting; and the explicit row height the extent estimation depends on.

Against that: the cost is control allocation, not a stall — the expensive part,
the decode, is cached — and there is no way to verify it. The gallery cannot be
driven headlessly without a client, and a screenful of tiles showing the wrong
art is the kind of defect only a person looking at it would catch.

Recycling it is worth doing behind a proper seam: lift `ArtEntry` and the row out
of `ArtBrowserWindow` as internal types, so the row's data-change behaviour can
be unit-tested directly without scrolling a virtualising list. That is a piece of
work in its own right rather than a tail end of this one.

### What was deliberately not done

- **No GPU path.** Drawing into Avalonia's own Skia canvas through
  `ISkiaSharpApiLease`, instead of a private `WriteableBitmap`, would remove the
  CPU raster and the 3 MB per-frame texture upload. It is **blocked**:
  `Avalonia.Skia` 12.1.2 is compiled against **SkiaSharp 3.119.4** while this
  repository pins **4.151.2**, and central transitive pinning unifies that to
  4.151.2. It works today precisely *because* the application never exchanges
  Skia objects with Avalonia — the `WriteableBitmap` is what insulates the two.
  Taking an `SKCanvas` across that boundary means matching Avalonia's SkiaSharp
  major version. **This skew is a standing risk for the next Avalonia bump**, and
  is the first thing to check if rendering breaks after one.
- **No display-list caching.** Recording the static content into an `SKPicture`
  and replaying it would help selection changes and marquee drags, but during an
  element drag the content changes every frame, which is the case that matters.
  The seam exists — `RenderDocument` already separates decorated from undecorated
  passes — so this stays available.
- **Clipping to the scroll viewport.** The full surface is still rasterised
  whatever is scrolled into view. It belongs with the canvas sizing work rather
  than bolted beside it.
- **No MVVM refactor.** `MainWindow.axaml.cs` is still imperative, and every
  action is still registered three times over — menu, shortcut, context menu.
  `Click`/`ClickAsync` also silently do nothing on a misspelled control name, so
  a typo yields a dead menu item with no error.
- **Gallery rows are still built without recycling**, so about forty controls
  are allocated and discarded per row scrolled into view. Attempted and
  abandoned deliberately — see *Gallery rows are still not recycled*
  above for the correctness hazard and the seam it needs.
- `UoArtSource` still has no tests of its own: it takes a concrete
  `UoDataContext`, so its cache and eviction cannot be exercised without a
  client.
- Page **rename** is still not possible. `GumpPage.Name` is shown in the
  move-to-page menu and nothing can set it.
- The splash graphic is a **JPEG**, so it carries 2004 compression artefacts
  around the lettering. It is kept byte-for-byte rather than cleaned up; a
  redrawn or vectorised version would be new artwork, not recovered artwork.
- `BwtDecoder` mis-decompresses at least one real file — the reference client's
  `Cliloc.deu` — so that language reports no strings. Found by
  [Phase 11](#phase-11--finding-a-cliloc-) reading more than English for the
  first time; the other seven languages in that client are fine.

---

## Phase 11 — Finding a cliloc ✅

Phase 9 made the canvas resolve clilocs, so a localised area finally previewed
as words rather than as `#1044017`. What it did not give anyone was a way to
*find* the number in the first place. Both cliloc fields — `Cliloc id` on an
HTML area and `Tooltip cliloc`, which every element has — were bare integer text
boxes, and the number in them said nothing about what a player would read.

The 1.8 editor did ship a browser, `src/GumpStudioCore/Forms/ClilocBrowser.cs`,
and it is worth being precise about what it was: a `ListBox` with **no search
box at all**. It added all ~124,000 owner-drawn rows one at a time and left you
to scroll. It also had a language combo, populated by globbing `Cliloc.*` in the
client folder — and then it always loaded `new StringList("enu")` regardless, so
the selector was decorative. Its `DrawItem` and `SelectedIndexChanged` handlers
still carry the original author's `// TODO` comments, and the selection handler
read from a cache field that aliased the very `ListBox` it was meant to back up.

### The substitution rules moved out of the renderer

`ElementPainter` held `Localized`, `Substitute` and `Value` privately. The
properties editor now previews the same string on hover, and two copies of the
`~1_THING~` grammar would drift, so they became
`GumpStudio.Uo.Data.ClilocFormatter` — beside `ClilocTable`, because the
placeholder grammar and the nested-`#1234` rule are properties of the cliloc
*format*, not of the document model. The lookup arrives as a
`Func<int, string?>`, so the renderer passes `IGumpArtSource.GetCliloc`, the
editor passes the table's `GetText`, and a test passes a dictionary.

The extraction was verified by the six facts in `ClilocPreviewTests` continuing
to pass untouched. `ArgumentCount` is new, and reports the **highest** ordinal
rather than how many placeholders there are: a string using only `~2_VAL~` still
needs two `@`-separated values.

### A language that is actually read

`UoDataContext.Open` hard-coded `cliloc.enu`. A German- or Russian-only shard
install therefore resolved nothing at all, with no explanation. Now one
directory pass builds a code-to-file map, `ClilocLanguages` reports what the
installation ships (ENU first, then ordinally), and `UseClilocLanguage` swaps the
deferred table in place rather than reopening the client — reopening would
re-index every UOP container to change one string table.

The codes are **file extensions, not language names**. `docs/uo-file-formats.md`
records a client in the test matrix shipping Italian text under `.enu`, so
claiming to know the language would be a lie for exactly the installations that
need this. The selection is remembered in `AppSettings.ClilocLanguage` and
applied in `EditorSession.Adopt`, before anything can read the table; a
remembered code the next client happens not to ship is ignored rather than
honoured, so it can never be what stops an installation's strings appearing.

`UseClilocLanguage` returns `false` for an unknown code rather than throwing,
because the usual caller is a persisted setting and that is ordinary operation.
The language and its table are one immutable `ClilocSelection` published through
a `volatile` field: reads stay lock-free, which matters because `GetCliloc` runs
once per localised element per repaint *and* from background art decodes.

Exports are deliberately unaffected — every converter emits the numeric id, so a
script exported under DEU is byte-identical to one exported under ENU.

### The panel

`Controls/ClilocPanel` is a Dock tool under the canvas, in a new vertical
`CenterPane` beside the existing toolbox and right-hand column. Under, not
beside: the browser is a wide, short list — an id and a sentence — and in the
right-hand column it would either crush the gump or truncate every string.

It takes `IReadOnlyList<ClilocEntry>` rather than a `UoDataContext`, which is
what lets its filter, its count and its hand-off be tested with no Ultima
installation present — something the art browser's own tests cannot do, since
all of them skip without one.

The search rule lives in `Controls/ClilocFilter` so it can be tested as a rule,
with no dispatcher: an **id matches on a prefix** and **text matches anywhere**,
both ordinal and case-insensitive, and a run of digits tries both — so `1044017`
finds the id while `vendor` finds the text, with no mode to switch. Unlike the
art browsers there is no `0x` form, because cliloc ids are decimal everywhere.
Filtering is debounced 150 ms, as the art browser's is, and an empty query
reuses the same list rather than copying 124,000 entries to say "all of them".

### Visible by default, without the eager parse

`ClilocTable.Entries` used to be `_entries.Values.OrderBy(e => e.Id)`, which
re-sorted 124,000 entries on **every** enumeration — once per keystroke for a
list that filters as you type. It is now a cached ordered snapshot, pinned by a
test asserting the same reference comes back twice.

The panel is on screen at first run, but reading the table is a Burrows-Wheeler
decompress and ~124,000 strings, which is precisely what Phase 9 deferred. So
visibility and population are separate: the panel says "Reading cliloc strings…"
while `EditorSession.LoadClilocsAsync` reads it on a thread pool thread, caching
its own in-flight task so a panel filling itself and a tooltip opening at the
same moment share one parse.

While that was open, the trial parse got cheaper. `TryParsePlain` built the
entire dictionary — UTF-8 decoding included — before deciding a buffer was not a
plain record stream, so a modern client allocated megabytes of garbage strings
and threw them away on every load. It now walks the record headers first and
decodes nothing. Language switching pays that trial again, which turned it from a
one-off at startup into a recurring cost.

### The hover card

Hovering a cliloc id shows the id, the language, the raw string and — when the
sibling arguments property is non-empty — the substituted form, resolved through
the same `ClilocFormatter` the canvas uses, so the two cannot disagree. It
distinguishes "no client", "0 means none" and "not in this client's cliloc file
— the canvas shows #1044017" rather than showing nothing.

The card is built as the tooltip opens, via `ToolTip.AddToolTipOpeningHandler`,
not when the row is built. That is the only version that is never stale — the id,
the arguments, the language and whether the table has been read all move
independently — and it is the cheap way round, since the property panel is
rebuilt on every selection change. Avalonia raises no opening event for a control
whose tip is unset, so a placeholder string is set first and replaced in the
handler; a test raises the event by hand to pin that framework behaviour across
an Avalonia bump.

### The hand-off

`PropertyEditorKind.Cliloc` covers both fields, and the row carries a
`ReadArguments` accessor so the card can find the arguments without matching on a
row's name. The `…` button reveals the panel and seeds its filter instead of
opening a dialog, and the window remembers the `(Element, PropertyRow)` pair —
never the `TextBox`, which every refresh replaces.

With no browse click, the panel writes to a localised area's `Cliloc id` when one
is selected, and is disabled otherwise. Nothing is guessed for `Tooltip cliloc`:
every element has one, and choosing between it and `Cliloc id` on the author's
behalf is the kind of surprise a browse button exists to avoid. Every choice goes
through `ApplyProperty`, so it is one undoable command like every other edit.

### What this retires

- The `_clilocCache` defect in the [defect inventory](#resource-and-performance):
  an instance field on a per-edit form, which also aliased the `ListBox` it was
  meant to back up, so the multi-megabyte file reloaded on every open.
- The decorative language combo, which is now the feature it looked like.
- `ClilocTable` had no non-client coverage at all, because its constructor is
  private and no fixture wrote cliloc bytes. `ClilocFixture` writes the record
  stream, so the tolerated 16-byte tail, the truncated tail, the UTF-8 length
  rule and a garbage buffer are all pinned now. The MegaCliloc path still needs a
  real client — that would take a BWT *encoder*.

### What reading eight languages found

Only `cliloc.enu` had ever been read, so nothing had exercised the rest. Every
one of the eight files in the reference client is MegaCliloc-wrapped, and two of
them fooled the trial parse: `Cliloc.cht`, two megabytes of it, walks as a valid
record stream and lands inside the tolerated tail, yielding 345 entries of
replacement characters. The walk now also rejects a stream whose records average
more than 512 bytes — real cliloc records average well under 150, these average
thousands — and the result is judged as text before it is accepted.

That recovered six of the eight. `Cliloc.deu` does not decompress correctly at
all, which is a defect in `BwtDecoder` rather than in this reading of it, and it
is the reason `Parse` now returns an empty table when neither reading looks like
text: a caller can render `#1044017` for a string it does not have, but it
cannot tell that a string it was handed is nonsense. The real-client test asserts
that contract — clean text or nothing — rather than a minimum entry count, since
a shipped translation is often partial (this client's `Cliloc.chs` is 46 KB
beside a 5 MB `Cliloc.enu`).

---

## Phase 12 — The 1.8 artwork comes back ✅

The rewrite had no icon and no identity: Windows drew the generic .NET
placeholder in the title bar and the taskbar, and there was nothing anywhere that
said who wrote the editor this one reproduces. Both of those were sitting in the
2004 build the whole time — the splash graphic base64-encoded inside a WinForms
`.resx`, the icon in the executable's resource directory.

`docs/assets.md` records how each was extracted and why it is byte-for-byte
rather than re-encoded. Two things worth repeating here: 1.8 embedded the *same*
JPEG in both its splash form and its about box, so there is one asset and both
windows share it; and there are **two** original icons — the "Gump Studio"
wordmark on the executable and a `GUMP` document sheet in the about form's own
resources — which are different images that happen to be the same byte length.
The wordmark is the application's identity, so that is the one that ships.

### The splash

`SplashWindow` is the original's behaviour: borderless, centred, always on top,
gone after two seconds or on a click. The graphic is shown at its native 454x158
and the window is sized to match, because it is a JPEG of a Photoshop
composition — interpolation smears the lettering and nearest-neighbour makes the
marbling blocky, so it is scaled by neither.

The original ran its splash on a second thread and pumped
`Application.DoEvents()` in a sleep loop to keep it painting. Here it is an
ordinary window on the UI thread with a `DispatcherTimer`.

**The editor is not constructed until the splash closes**, and that ordering is
the whole design. Building `MainWindow` is synchronous and takes long enough to
matter, and the dispatcher cannot paint while it runs — so constructing it first
made the splash appear at the same instant as the window it was meant to precede.
That is the one thing a splash screen must not do, and it is exactly what the
first attempt did.

Deferring it costs a `ShutdownMode`: while only the splash is open there is no
main window, so the lifetime would read the splash closing as the last window
closing and shut down mid-startup. It is held at `OnExplicitShutdown` until the
editor exists and then handed over to it, which is also what keeps closing the
editor exiting the process.

### The about box

454 wide because the graphic is, sat full-bleed across the top exactly as the
1.8 about box had it. Below that the version and runtime, a short note on what
this rewrite changed, and the 1.8 credits kept as they were written: Bradley
Uffner, artwork by Melanius, Krrios' UOSDK, DarkStorm on decoding `unifont.mul`,
and the RunUO community.

The original linked `gumpstudio.com` and — when clicked — opened
`orbsydia.net`. Neither resolves any more, so neither is repeated: a dead link in
an about box is worse than no link, and a test asserts that neither address has
crept back in.

### The icon

Four sizes, from one. The executable shipped 32x32 alone, which Windows
smooth-scales into mush wherever it wants something bigger, so 64, 128 and 256
were added as **exact integer nearest-neighbour multiples** — every original
pixel becomes a clean block, and nothing is invented that was not in the 32x32.
The 32x32 payload itself is copied through untouched.

Building that file caught a defect in the extraction: `GRPICONDIRENTRY` (14
bytes, ending in a resource id) and `ICONDIRENTRY` (16, ending in a file offset)
share only their first 12 bytes, and copying 12 *and then* rewriting the size
field yields 20-byte entries and an icon every decoder rejects — while still
writing a plausible-looking file. `ArtworkTests` now parses the shipped
directory and checks that each entry's offset and length lie inside the file.

### What the tests can and cannot see

The assets are covered by reading the shipped bytes and parsing the JPEG and ICO
headers, not by decoding them: `Avalonia.Headless` stubs drawing, so a decoded
bitmap reports a 1x1 placeholder and a size assertion against it would pass
whatever the file held.

Nothing asserts what the artwork looks like, and the splash resists automation
from outside as well — `PrintWindow` with `PW_RENDERFULLCONTENT` captures a
borderless topmost Avalonia window as solid black, and a screen read races its
two seconds. It was confirmed by eye instead, and the startup *sequence* was
confirmed by polling the process's visible windows, which shows only the splash
until it closes and only the editor afterwards.

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
They live in `GumpStudio.Converters` now, alongside POL and the raw layout
format.

Adding them closed a gap in the POL exporter: it offered only the gump-package
dialect, so **the layout-string form could not be reached from the application at
all**. Every dialect became a separate menu entry, and in Phase 7 that collapsed
back to four entries with the dialect chosen in the export dialog.

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
`PolScriptBuilder` is a port of. The POL work is already based on the
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

~~The rewrite renders label text with the **Unicode fonts only**.~~ Closed in
[Phase 9](#phase-9--a-preview-worth-trusting-):
every text element chooses a family and an index, so both the 13 Unicode faces
and the 10 `fonts.mul` ones are reachable. `LabelElement.PartialHue` is still
absent — label hue is applied wholesale.

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

- ~~**The dock layout is not remembered between sessions.**~~ Built in
  [Phase 10](#phase-10--the-shell-catches-up-): pane proportions, hidden panels
  and the window's own placement are stored, and a *Reset panel layout* command
  returns to the declared one. **Tearing a panel off into its own window, or
  re-tabbing one beside another, is still not remembered** — that needs Dock's
  own serialiser, which is a reflective dependency this repository publishes
  with NativeAOT and so declined to adopt.
- **The SkiaSharp version skew.** `Avalonia.Skia` 12.1.2 is built against
  SkiaSharp 3.119.4; this repository pins 4.151.2 and central transitive pinning
  unifies to it. Nothing breaks because the application never hands a Skia
  object to Avalonia — the canvas blits through a `WriteableBitmap`. It is the
  first thing to check if rendering misbehaves after an Avalonia bump, and it is
  what blocks moving the canvas onto `ISkiaSharpApiLease`.
- **The BWT decoder is only covered by real-client tests**, so CI does not
  exercise it. Closing this needs either a BWT encoder written purely for test
  fixtures, or a small captured byte pair checked in.
- **Pre-2002 cliloc is unsupported** — numbered `clilocNN.enu` chunks in an IFF
  `FORM`/`DATA` container. Everything else in such a client reads fine.
- **The 1996 pre-alpha is unsupported** — it ships `GUMPS.MUL` with no index.
  `UoDataContext.Validate` reports what is missing, and a test asserts it does.
- **Resolving a UOP gump's dimensions requires decoding it**, because the size
  lives inside the compressed payload. An art browser must therefore virtualise
  and resolve lazily rather than measuring everything up front. Phase 10 widened
  the payload memo into a 32 MB least-recently-used window, so a revisited entry
  is no longer inflated again, but the first look at one still costs a decode.
- **`dotnet test` does not work on SDK 10.0.400.** It reports `Zero tests ran`
  for every project, reproducibly, including for a one-file xunit.v3 project in
  an empty directory. `eng/run-tests.ps1` launches the test applications
  directly instead. Retry `dotnet test` after an SDK bump.
- ~~**No exporter has an options dialog.**~~ Built in Phase 7: dialect, name,
  namespace and comments are all chosen in the export dialog.
