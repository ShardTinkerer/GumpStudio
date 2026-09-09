# Gump commands and how the editor models them

The client's generic-gump system takes a plain-text **layout string**: a sequence
of `{ resizepic 100 100 3600 300 300 }` commands plus a separate array of text
values. This page maps every command the current classic client accepts onto the
editor's document model.

The command list is taken from `parseGumpDefinition` in the client binary, and
the parameter names from the POL command reference where the two disagree only
on naming. Where they disagree on *meaning*, that is called out below.

## The mapping

Most commands added after GumpStudio 1.8 are not new element types. They are the
same element with a different rule: `gumppicphued` is `gumppic` with a different
tinting rule, `textentrylimited` is `textentry` with a cap, `buttontileart` is a
`button` with a graphic on it. Modelling those as properties keeps the toolbox
short and means an author picks a behaviour rather than picking between two
near-identical entries.

| Command | Modelled as |
|---|---|
| `page` | A `GumpPage` |
| `group` / `endgroup` | `CheckboxElement.GroupId` on the radios; the layout builder opens and closes the run |
| `nomove` / `noclose` / `nodispose` | `GumpProperties.Movable` / `Closable` / `Disposable` |
| `mastergump` | `GumpProperties.MasterGumpId` |
| `toggleupperwordcase` | `GumpProperties.UpperWordCase` |
| `togglecroppedtext` | `GumpProperties.CroppedText` |
| `echandleinput` | `GumpProperties.EnhancedClientInput` |
| `button` | `ButtonElement` |
| `buttontileart` | `ButtonElement` with `TileId` set |
| `checkbox` | `CheckboxElement` |
| `radio` | `RadioElement` |
| `textentry` | `TextEntryElement` |
| `textentrylimited` | `TextEntryElement` with `MaxLength` above zero |
| `text` | `LabelElement` |
| `croppedtext` | `LabelElement` with `Cropped` set |
| `tooltip` | `Element.TooltipClilocId` / `TooltipArguments`, on any element |
| `itemproperty` | `Element.ItemPropertySerial`, on any element |
| `gumppic` | `ImageElement` |
| `gumppichued` | `ImageElement` with a hue |
| `gumppicphued` | `ImageElement` with a hue and `PartialHue` |
| `gumppictiled` | `TiledElement` |
| `tilepic` | `ItemElement` |
| `tilepichue` | `ItemElement` with a hue |
| `tilepicasgumppic` | `TileAsGumpElement` |
| `resizepic` | `BackgroundElement` |
| `checkertrans` | `AlphaElement` |
| `picinpic` | `PicInPicElement` |
| `picinpichued` | `PicInPicElement` with a hue |
| `picinpicphued` | `PicInPicElement` with a hue and `PartialHue` |
| `htmlgump` | `HtmlElement`, content kind `Html` |
| `xmfhtmlgump` | `HtmlElement`, content kind `Localized` |
| `xmfhtmlgumpcolor` | `HtmlElement`, localised, with a `Color` |
| `xmfhtmltok` | `HtmlElement`, localised, with `Arguments` |

Every command in the client's table is covered.

## Details worth knowing

### Full versus partial hue

`gumppichued` replaces the colour of every non-transparent pixel. `gumppicphued`
replaces only the grayscale ones, leaving art that already has colour alone.
Dyeable cloth and armour need the partial form; using the full one flattens the
graphic to a single shade. The same pair exists for `picinpic`.

### A full tint is `gumppichued`, not `gumppic` with a hue

The client's `gumppic` handler reads exactly three positional values and then
treats every remaining token as `key=value`. Its splitter gives up when it finds
no `=`, and only a key of `hue` assigns one, so `gumppic 95 70 1417 22` renders
**untinted** — the hue is skipped without a warning. Both `gumppic … hue=22` and
`gumppichued 95 70 1417 22` work; the editor writes the second, because it needs
no keyword syntax and reads like its `gumppicphued` sibling.

The reader still accepts the bare form, since other tools emit it and the intent
is plain. It just never writes one.

This was found by decompiling `parseGumpDefinition`, after the mapping below had
claimed for some time that the two forms were equivalent. Every dialect that
speaks layout strings was dropping the hue on a fully tinted image.

Sphere is the exception that proves this is a dialect matter rather than a
grammar one. It parses the line and re-emits it, and its `GUMPPIC` handler turns
a trailing token into the `hue=` form itself — while having no `GUMPPICHUED`
key at all. So `sphere` / `056` writes the bare form and the other two write the
explicit command, which is what `LayoutStringOptions.HuedGumpPicCommand`
selects.

### `picinpic` puts its size last, and three cores get it wrong

The client reads `picinpic x y gumpId sx sy width height` — the source offset
before the size. Confirmed against a live shard and the current client:
`picinpic 60 60 2000 20 255 200 60` draws a 200×60 band cropped from (20,255) of
the paperdoll frame, complete with its yellow parchment edge. Had the size come
first it would have drawn a 20×255 column of grey stone from (200,60).

ServUO and ModernUO both send `x y gumpId width height sx sy` — from
`GumpSpriteImage`, and from ModernUO's newer `GumpLayoutBuilder` too — and so
does Sphere's `CDialogDef.cpp`, even though the comment on its own
`GUMPCTL_PICINPIC` enum says the opposite and is the one that is right. UOX3's
`CGump_AddPicInPic` gets it right. A region exported through ServUO's
`AddSpriteImage` therefore draws from the wrong place until a core fixes it,
which is what the `runuo` converter notes beside the call.

Worth recording how *not* to settle a question like this. The parameter names in
a disassembly prove nothing: they are annotations on stack slots the compiler
shares between commands, so `picinpic`'s 6th and 7th arriving in variables called
`width`/`height` is an artefact of `resizepic` using the same slots. The static
argument that did hold up was where the values *go* — `resizepic` and
`gumppictiled` both carry an unambiguous width and height and pass them as
arguments 7 and 8 of the shared gump-element constructor, and `picinpic` passes
its 6th and 7th as those same arguments. Three independent implementations
agreeing against that was still not enough to overturn it, and not enough to
trust it either. One screenshot was.

### `xmfhtmltok` is not `xmfhtmlgumpcolor` plus arguments

Its parameter order is genuinely different: background and scrollbar come
*before* the colour, and the cliloc id comes **last**, after them. Writing it as
though it were the colour form with an argument list appended produces a gump
that renders the wrong string.

### Tooltips attach to the previous element

`tooltip` and `itemproperty` have no coordinates. The client applies them to
whichever element it created most recently. That makes them positional in the
layout string but a plain per-element property everywhere else, so the editor
stores them on the element and the layout builder emits them immediately after
their own element's command. A converter cannot then attach one to the wrong
element by reordering its output.

### A cropped label owns its rectangle

An ordinary label is exactly as wide as its rendered text, so there is nothing to
drag and its size is measured, not stored. `croppedtext` carries its own width and
height and clips the text to them, so a cropped label becomes resizable and the
renderer stops measuring it. The reader has to apply `Cropped` before it applies
the size, or the rectangle is silently discarded.

### The three opaque parameters on `tilepicasgumppic`

The command is `tilepicasgumppic x y tileId a b c`. The client decrements `a`
before use, which is why it is exposed as a one-based `LinkId`. What `b` and `c`
mean is not established even from the binary, so they are exposed as `ParamB` and
`ParamC` and passed through unchanged rather than guessed at. Leave them at zero
unless a server template already sets them.

### `button` slot order

The slots are `quit`, `page-id`, `return-value`:

```
button x y released pressed 0 <page>  0        // switches to a page
button x y released pressed 1 0       <value>  // replies and closes
```

**The 1.8 exporter got all three wrong**, and its source says so — the field
carries a `// TODO: Page or Reply???` comment. It inverted the quit flag, put a
page button's target page in the return-value slot, and a reply button's return
value in the page slot. The result was a page button that closed the gump and a
reply button that jumped to a page numbered after its reply id. This editor emits
the layout above, which is what the POL reference, the client's parser and RunUO
all describe.

### Groups do not span pages

`page` resets the client's current group to zero, so a group id used on one page
has to be declared again on the next. The 1.8 exporter tracked the last group
across the whole document, so a second page whose first radio matched the
previous page's group never got its `group` command and every radio on it
silently fell into group 0.

## Reading the commands back

`LayoutStringParser` understands every command in the table above, which is what
makes importing a gump captured off the wire possible — see
[architecture.md](architecture.md#import-the-same-pipeline-backwards). The
mapping runs in both directions, so a command this page lists is one the editor
can both write and read.

## What each converter can express

Four converters ship. Every command above is modelled in the layout IR
(`GumpStudio.Core.Layout`) regardless, so a gap here is a gap in the *target*,
never in what the editor understood.

| Converter and dialect | Coverage |
|---|---|
| `layout` | Everything. It *is* the client's grammar. |
| `pol` / `layout-strings` | Everything. Raw layout strings. |
| `sphere` / `056` | The commands Sphere has a control for. Not a pass-through: Sphere parses these lines and re-emits its own, so eight of them have no key and are dropped — see below. |
| `pol` / `gump-package` | Everything, through a `GF*` call in every case but one. Needs a current `:gumps:gumps` — see below. |
| `runuo` (either dialect) | Everything except `tilepicasgumppic` and `mastergump`, which no RunUO or ServUO core exposes at all. Tooltip arguments are dropped — ServUO's two-argument `AddTooltip` is commented out in its own source. `AddSpriteImage`, `AddGroup`, `AddECHandleInput` and `AddLabelCropped` need a ServUO-era core. |
| `sphere` / `099` | A fixed set of script functions. See below. |
| `uox3` | Everything except `tilepicasgumppic`, a partial hue, the parser toggles, a hued `picinpic`, and a tooltip's arguments. `mastergump` has a call, but a broken one. No gump position either. |

Where a converter is missing only a *refinement* — a partial hue, a crop
rectangle, a tile overlay on a button — it emits the nearest thing it does have
and notes what was lost. Dropping the whole command instead would delete a
visible element from the gump, which is a far worse answer than drawing it
slightly wrong. A button whose tile overlay cannot be expressed is still a
button; commented out, it is a dialog the player cannot dismiss.

Where nothing comes close, the command is written as a comment rather than as a
call that would not run. That is now true only of `runuo` and `sphere` / `099`.

## How the POL gump package covers the command table

Every command in the table above has a `GF*` function, so the gump-package
dialect emits a call for all of them. Exporting the same document both ways and
comparing the client commands they produce gives 42 identical lines out of 42 —
the one cosmetic difference being that `GFGumpPic` spells a hue with POL's
documented `hue=` keyword where the raw form writes it positionally.

That took work on both sides. Some of the functions had been in the package for
years and the 1.8 exporter simply did not know them: `GFPicTiled`, `GFTextCrop`,
`GFTooltip`, `GFItemProperty`, `GFAddImageTileButton`, `GFTextEntry`'s trailing
`lmt`, and `GFAddHTMLLocalized`'s hue and custom string — which between them
cover all three `xmfhtml` forms, because the package picks the command from the
arguments it is handed rather than making the caller choose.

The rest did not exist and were added: `GFPicInPic`, `GFTilePicAsGumpPic`,
`GFMasterGump`, `GFEndRadioGroup`, `GFToggleUpperWordCase`,
`GFToggleCroppedText`, `GFECHandleInput`, and a trailing `partial` flag on
`GFGumpPic` for `gumppicphued`.

**This dialect therefore needs a `:gumps:gumps` that has those.** An older
package will fail to compile the export on the first unknown function. The
layout-string dialect has no such requirement and is the portable choice.

### The one exception

`GFAddButton`, `GFCheckBox` and `GFRadioButton` all replace a value below one
with the next free id. A button targeting page 0 exported as a call became a jump
to an arbitrary page, and the same document exported as layout strings disagreed
about it. So those three go out through the package's own escape hatch:

```
//GFAddButton would assign an id of its own; written out as a layout string.
XGFAddToLayout(MyGump, "button 20 240 247 248 0 0 0");
```

`textentry` cannot be rescued the same way — it carries text, and this dialect
writes its strings inline with no data array for a layout string to index into —
so a zero entry id is noted in the output instead.

## What the UOX3 gump API can express

UOX3 has the most complete API of the four servers, read off `CGump_Methods` in
`UOXJSMethods.h`. It is the only one that exposes `endgroup` — without which a
radio group does not work on pages above the first — and it covers `picinpic`
with the client's own parameter order, `buttontileart`, `croppedtext`,
`textentrylimited`, `itemproperty` and all three `xmfhtml` forms.

Two parameter orders differ from the layout command and are easy to get wrong.
`AddCroppedText` takes its hue **third**, before the width and height, where the
command puts it last. `AddPicInPic` takes the source offset **before** the size,
which is what the client reads and what the RunUO-family cores transpose.

What it cannot do, and what the export says instead of dropping:

- **A screen position.** `new Gump()` takes no coordinates and `Send()` takes only
  a socket, so the editor's position is reported in a comment.
- **`mastergump`.** `CGump_MasterGump` formats five values from one argument, so
  the command it appends is garbage. The call is not emitted.
- **A tooltip's arguments.** `CGump_AddToolTip` starts its argument loop at index
  two rather than one, so the first argument is skipped and a single-argument
  call emits an empty `@@`. The cliloc goes out alone.
- **A partial hue, a hued `picinpic`, `tilepicasgumppic`, and the three toggles.**
  No call exists.

One quirk shapes the output. UOX3 assigns the text index itself for `AddText`,
`AddCroppedText` and `AddHTMLGump`, from a counter it advances as it goes — but
`AddTextEntry` pushes a string onto the same list *without* advancing that
counter, so an entry shifts every later index by one. The entry's own index is
passed explicitly and is right; where a later text element would be wrong, the
export says so on the line above it and suggests moving the entries below the
labels.

## Deliberately not modelled

- **`gumppic`'s `class=` keyword.** The only known value is `VirtueGump`, which
  enables virtue-icon tooltips in the client's own virtue window. Nothing a shard
  author builds needs it.
- **`tilepic`'s `1234,33` comma-hue form.** The parser accepts it, but
  `tilepichue` says the same thing without relying on an undocumented quirk.
