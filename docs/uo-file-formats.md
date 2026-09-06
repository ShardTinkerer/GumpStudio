# Ultima Online client data formats

Notes on the formats `GumpStudio.Uo` reads. Everything here was verified against
real client installations; where a claim could not be verified it says so.

> **On other sources.** The `client-decomp` documentation describes a *different*
> client build from the ones tested here and is wrong for them on three counts:
> it gives the UOP hash as FNV-1a, the path template extension as `.dat`, and the
> compression flags as 0/1 only. On the installed clients all three are wrong.
> ClassicUO's loaders were the reliable reference. Verify before trusting either.

## Containers

### MUL — `<name>idx.mul` + `<name>.mul`

An index file of 12-byte records paired with a data file.

```
struct IndexRecord {   // 12 bytes, little-endian
    i32 lookup;        // byte offset into the .mul, or negative when the slot is empty
    i32 length;        // bytes of data
    i32 extra;         // format-specific; for gumps this packs width<<16 | height
};
```

The index length determines the entry count, so the table size must be derived
from the file rather than hard-coded — that is what lets High Seas-era clients
with larger tables work unchanged.

### Verdata patching — `verdata.mul`

```
i32 count;
struct Patch { i32 file; i32 index; i32 lookup; i32 length; i32 extra; } patches[count];
```

`file` names the container being patched: **4 = art, 12 = gumps, 30 = tiledata,
32 = hues**. A matching patch replaces that index entry, and the data is then
read from `verdata.mul` instead of the primary file. Modern clients generally do
not ship the file at all.

### UOP — Mythic package

```
struct Header {          // 28 bytes used
    u32 magic;           // "MYP\0" == 0x0050594D
    u32 version;         // 4 or 5 observed
    u32 signature;       // 0xFD23EC43
    i64 firstBlock;      // offset of the first entry-table block
    u32 blockCapacity;   // 100 for v4, 1000 for v5
    u32 fileCount;
};

struct BlockHeader { u32 entriesInBlock; i64 nextBlock; };   // 12 bytes

struct Entry {           // 34 bytes
    i64 dataOffset;      // 0 marks an unused slot
    i32 headerLength;    // skip this many bytes before the payload
    i32 compressedSize;
    i32 decompressedSize;
    u64 pathHash;
    u32 dataHash;        // Adler-32, not verified by this reader
    i16 compression;     // see below
};
```

Entries carry no file names — they are keyed by a hash of the original build
path, so a reader hashes the expected path for each index it wants.

**Path templates** (lowercase, forward slashes):

| Package | Template |
|---|---|
| `gumpartLegacyMUL.uop` | `build/gumpartlegacymul/{0:D8}.tga` |
| `artLegacyMUL.uop` | `build/artlegacymul/{0:D8}.tga` |

**The hash is a Jenkins lookup2 variant**, not FNV-1a. It is implemented in
`UopHash` and pinned by known-answer tests using values read out of a retail
package, so the algorithm stays covered even with no client present.

### UOP compression — flag 3 is the important one

| Flag | Meaning |
|---|---|
| 0 | stored verbatim |
| 1 | zlib |
| **3** | **zlib, then a Burrows-Wheeler transform** |

Retail `gumpartLegacyMUL.uop` uses **flag 3 for every entry**. Inflating alone
produces plausible-looking but meaningless bytes, which makes this an easy thing
to get subtly wrong: the payload decompresses to exactly the declared length and
still decodes to nothing recognisable.

`artLegacyMUL.uop`, by contrast, stores entries with flag 0 — the payload is the
raw MUL art chunk verbatim, with no prefix of any kind.

`BwtDecoder` implements the second stage: a move-to-front pass followed by an
inverse Burrows-Wheeler driven by a 1024-byte symbol-count header. Its
correctness is demonstrated by decoding a gump from a modern UOP client and
getting **byte-identical output** to the same gump in a legacy `gumpart.mul`.

### UOP gump dimension prefix

Gump payloads have no self-describing size — in MUL the dimensions come from the
index's `extra` field, which UOP has no equivalent of. Instead the **fully
decoded** payload begins with two little-endian `i32`s holding width and height.
"Fully decoded" matters: for a flag-3 entry the prefix is inside the
Burrows-Wheeler output, not the compressed bytes.

Because reading a gump's size therefore costs a full decode, `UopFileProvider`
resolves dimensions lazily and caches only those four bytes per entry, with a
one-entry memo for the payload. Decoding all 5579 gumps eagerly cost minutes and
hundreds of megabytes.

## Images

All art is **ARGB1555**: one alpha bit and three five-bit channels. Hueing is
defined in terms of the five-bit red channel, so hues must be applied before the
image is widened to eight-bit channels.

Channel expansion uses `(c << 3) | (c >> 2)`, so 31 maps to 255. The original SDK
used a bare `<< 3`, which maps 31 to 248 and renders white art as light grey.

### Gump art

```
i32 rowOffsets[height];    // dword offsets from the start of the payload
// then, per row, (u16 colour, u16 runLength) pairs until the row is full
```

Colour 0 means a transparent run. A zero run length is malformed and must be
guarded against — the original decoder would spin forever.

### Static item art

```
i32 unknown;
i16 width, height;
u16 rowOffsets[height];    // word offsets from the end of this table
// then, per row, (u16 xOffset, u16 runLength, u16 pixels[runLength]) chunks,
// terminated when xOffset and runLength are both zero
```

### Land art

A fixed 44×44 diamond with no header: 1012 pixels, written as two triangular
halves whose row widths run 2, 4, … 44, 44, … 4, 2.

## `hues.mul`

375 groups of 8 hues, 3000 total. Each group is a 4-byte tag (read and discarded)
followed by eight 88-byte records:

```
struct Hue { u16 colors[32]; u16 tableStart; u16 tableEnd; char name[20]; };
```

Bit 15 of each colour is reserved on disk and forced on when loaded.

**Hueing** replaces a pixel's colour with `colors[red5]`. Transparent pixels stay
transparent. A "partial hue" recolours only pixels where all three channels are
equal, leaving already-coloured detail alone.

Hues are **one-based** everywhere they are referenced by elements and scripts:
hue 1 is `colors[0]`, and 0 means "no hue". The two high bits are flags and are
masked off.

## `tiledata.mul`

Two sections, land then statics, each divided into groups of 32 tiles preceded by
a 4-byte tag.

| Layout | Land record | Static record | Detected by |
|---|---|---|---|
| Legacy (32-bit flags) | 26 bytes | 37 bytes | file smaller than the High Seas size |
| High Seas (64-bit flags) | 30 bytes | 41 bytes | file at least 3 188 736 bytes |

Static-record fields, in order: flags, weight, quality, four unused bytes,
animId, two unused bytes, lightId, height, then a 20-byte NUL-padded ASCII name.

The static section length varies independently of the layout — clients in the
test matrix ship 16 384, 32 768 and 65 536 statics — so the group count is
derived from the remaining file length rather than assumed.

**A client's art container routinely holds more items than its tiledata
describes.** One shard client in use has 20 796 items with art against a
16 384-entry tiledata table, so roughly a fifth of its art has no tile record at
all. Anything that enumerates art will therefore ask about ids the table has
never heard of; those lookups are normal and must return an empty name, not a
null one.

## `cliloc.<lang>`

```
u32 version;             // 0 and 2 both observed in shipped files
u16 unused;
// then, until EOF:
struct Entry { u32 id; u8 flag; u16 length; char text[length]; };   // text is UTF-8
```

The text really is **UTF-8**. This is worth stating because it looks like a
classic ANSI-versus-UTF-8 bug and is not one.

**Modern clients wrap the whole file in the "MegaCliloc" codec**, which turns out
to be the same Burrows-Wheeler transform as UOP compression flag 3 — so one
decoder handles both. Detection is by trial parse rather than by sniffing the
header: version 0 is legitimate, so "is the version small?" wrongly rejects real
files.

Content is not a reliable assertion target. Shard clients rewrite these freely;
one client in the test matrix ships Italian text under a `.enu` extension.

**Unsupported:** clients older than roughly 2002 ship numbered `clilocNN.enu`
chunks inside an IFF `FORM`/`DATA` container. That format is not read.

## Fonts

### `fonts.mul` — ASCII

Up to 25 font slots, each a leading flag byte followed by 224 glyphs covering
characters 0x20–0xFF, so a character maps directly to `glyphs[ch - 0x20]`.

```
struct Glyph { u8 width; u8 height; i8 metric; u16 pixels[width * height]; };
```

Pixel value 0 is **transparent**. The old implementation drew every pixel
unconditionally and then tried to key out the background using whatever colour
happened to sit in the bottom-left corner.

### `unifont*.mul` — Unicode

Slot 0 is `unifont.mul`, slots 1–12 are `unifont1.mul` … `unifont12.mul`. Clients
in the test matrix ship 3, 7 or 13 of them, so the count must be discovered by
probing rather than hard-coded.

```
u32 glyphOffsets[65536];   // 0 means the font has no glyph for that code point
struct Glyph {
    i8 xOffset; i8 yOffset; u8 width; u8 height;
    u8 bits[ceil(width / 8) * height];   // 1bpp mask, MSB first, rows byte-padded
};
```

The offset table is 65536 entries because any code point may have a glyph. The
old implementation cached glyphs in a 1120-entry array indexed by the raw
character, so anything at or above U+0460 threw.

## Gump pages

A gump is a list of pages, and the client shows one at a time — except **page 0,
which is always visible**. Whatever page 0 contains stays on screen while the
player switches between pages 1, 2 and so on, which is how a shared frame,
title bar or close button is built.

Two consequences:

- An editor must draw page 0 beneath whichever page is being edited, or the
  preview does not match what the player sees. `GumpRenderer.RenderDocument`
  encodes this; `Render` on a single page deliberately does not, so the rule
  lives in one place.
- Exporters need no special handling: emitting `page 0`, its elements, then
  `page 1` and its elements already expresses it.

## Legacy `.gump` files

Not a client format, but the same territory: GumpStudio 1.8 saved documents as
two consecutive `BinaryFormatter` payloads — an `ArrayList` of `GroupElement`
pages, then a `GumpProperties`. `.gumpling` files hold a single `GroupElement`.

Member names, recovered by decompiling the original binary:

```
BaseElement     : BaseElementVersion (2), Name, Location, Size, Parent, Comment
GumpProperties  : Version (1), Location, Moveable, Closeable, Disposeable, Type
```

Each subclass adds its own `<Type>ElementVersion` plus its own fields.

`System.Formats.Nrbf`, in-box since .NET 9, decodes these safely without ever
activating a type, which is how the importer avoids re-enabling
`BinaryFormatter`.
