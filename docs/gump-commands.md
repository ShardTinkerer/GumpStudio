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
| `group` / `endgroup` | `CheckboxElement.GroupId` on the radios; the exporter opens and closes the run |
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

### `xmfhtmltok` is not `xmfhtmlgumpcolor` plus arguments

Its parameter order is genuinely different: background and scrollbar come
*before* the colour, and the cliloc id comes **last**, after them. Writing it as
though it were the colour form with an argument list appended produces a gump
that renders the wrong string.

### Tooltips attach to the previous element

`tooltip` and `itemproperty` have no coordinates. The client applies them to
whichever element it created most recently. That makes them positional in the
layout string but a plain per-element property everywhere else, so the editor
stores them on the element and the exporter emits them immediately after their
own element's command. An exporter cannot then attach one to the wrong element by
reordering its output.

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
reply button that jumped to a page numbered after its reply id. The rewrite emits
the layout above, which is what the POL reference, the client's parser and RunUO
all describe.

### Groups do not span pages

`page` resets the client's current group to zero, so a group id used on one page
has to be declared again on the next. The 1.8 exporter tracked the last group
across the whole document, so a second page whose first radio matched the
previous page's group never got its `group` command and every radio on it
silently fell into group 0.

## What the POL gump package cannot express

The `:gumps:gumps` distro package has a `GF*` function for the original element
set only. For everything else the exporter emits the layout-string form commented
out, beside the closest call it does have — which is what the original did for
`gumppictiled`:

```
//Gump package does not support picinpic
//picinpic 20 85 1417 10 20 60 24
```

The layout-string dialect has no such gap and emits every command directly.

## Deliberately not modelled

- **`gumppic`'s `class=` keyword.** The only known value is `VirtueGump`, which
  enables virtue-icon tooltips in the client's own virtue window. Nothing a shard
  author builds needs it.
- **`tilepic`'s `1234,33` comma-hue form.** The parser accepts it, but
  `tilepichue` says the same thing without relying on an undocumented quirk.
