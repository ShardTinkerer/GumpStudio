# The `.uop` container, and MegaCliloc

Two formats a modern client needs and the 2004 original never saw. Both were
read out of the client binary — an x86 PE at image base `0x00400000` — and then
checked against shipped files. Addresses below are that binary's, kept so a
claim here can be traced back rather than taken on trust.

Neither format is documented by its author, so this file exists to make the
reasoning reviewable. No third-party source was copied to produce any of it.

## Why it matters here

Modern clients stopped shipping `gumpart.mul`. Gump art now lives in
`gumpartLegacyMUL.uop`, where **every** entry is zlib followed by MegaCliloc, so
an editor that cannot decode both reads nothing at all. `cliloc.*` moved the same
way around the 7.0.104 era: the file is MegaCliloc end to end, with no zlib
layer.

## Container

The header is 28 bytes, followed by a linked list of blocks of entry
descriptors. Entries carry no names — each is keyed by a hash of the build path
it was packed from, so a reader hashes the path it wants and looks it up.

| Offset | Type | Field |
|---|---|---|
| 0 | `u32` | magic, `"MYP\0"` = `0x0050594D` |
| 4 | `u32` | version, 3 to 5 |
| 8 | `u32` | signature `0xFD23EC43`, an endianness probe |
| 12 | `i64` | offset of the first block |
| 20 | `u32` | entries per block |
| 24 | `i32` | total entry count |

A block is `i32` entries used, `i64` offset of the next block, then that many
34-byte descriptors:

| Offset | Type | Field |
|---|---|---|
| 0 | `i64` | offset of the entry's data block |
| 8 | `i32` | length of the data block header |
| 12 | `i32` | compressed length |
| 16 | `i32` | decompressed length |
| 20 | `u64` | path hash |
| 28 | `u32` | Adler-32 of the payload |
| 32 | `u16` | compression flag |

The Adler-32 is read past rather than checked. `UopFileProvider` treats a
payload it cannot decode as absent, which is the same outcome a failed checksum
would produce, so verifying it would cost a pass over every byte to reach a
conclusion already reached.

### Compression flags

| Flag | Pipeline | Seen in |
|---|---|---|
| 0 | stored | — |
| 1 | zlib | `tileart.uop`, `MultiCollection.uop`, `AnimationSequence.uop` |
| 3 | zlib, then MegaCliloc | `gumpartLegacyMUL.uop` |

Flag 3 is the layering, not an alternative: inflate first, and the MegaCliloc
payload — including its own four-byte header — is what comes out.

### The path hash

Bob Jenkins' `lookup3` in its `hashlittle2` two-word form, at `0x0042C9B2`.
Every character contributes its full 16 bits, the seed is
`length + 0xDEADBEEF`, input is consumed twelve at a time, and the 64-bit result
packs the two output words as `(b << 32) | c`. Paths are lowercase, shaped like
`build/gumpartlegacymul/00000123.tga`.

Jenkins placed `lookup3` in the public domain. `UopHash` is written in his form;
`UopHashTests` pins it to hashes read out of the entry table of a retail
`gumpartLegacyMUL.uop`, which is the check a synthetic package cannot give — one
built by our own writer would share any mistake and still round-trip.

## MegaCliloc

The client's own name for it, from the error string `"Error (MegaCliloc) :
StringId Not Found : "` at `0x006C0800`. It is widely described as a
Burrows-Wheeler transform. It is not one: there is no block sort and no rotation
index anywhere in it. Stage one is a move-to-front cipher; stage two is a
frequency-driven move-to-front expander. The mistake is harmless right up until
it suggests the parts of the format that do not fit a BWT can be skipped.

Entry points: `Cliloc_decodeFile @ 0058E280` and `MegaCliloc_decode @ 0058DFB0`
for cliloc files, `UopAsset_decodeMegaCliloc @ 00428D1D` and
`UopAsset_megaClilocCore @ 00428BDC` for UOP entries. Stage one is
`MegaCliloc_mtfStage @ 0058DD50`, stage two `MegaCliloc_freqExpand @ 004289E7`.

### Header

Four bytes: the decoded length, masked.

```
outputSize = u32 at offset 0  XOR  0x8E2C9A3D
```

Confirmed rather than assumed. A 7.0.114.4 client is the only one in the test
matrix shipping compressed clilocs; three of its languages were also shipped
uncompressed by a 7.0.50.0 client, and the mask reproduces their sizes exactly:

| File | Header | Decodes to | Uncompressed in 7.0.50.0 |
|---|---|---|---|
| `Cliloc.deu` | `99 5D 26 8E` | 706,468 | 706,468 |
| `Cliloc.fra` | `05 9D 27 8E` | 722,744 | 722,744 |
| `Cliloc.esp` | `7F C3 26 8E` | 678,210 | 678,210 |

Decoding those three now yields byte-identical files to the 7.0.50.0 originals,
which checks the whole codec end to end and not just the header.

### Stage one — move-to-front cipher

Over everything after the header. Start with a table of `0..255`; for each input
byte, emit `table[byte]` and move that entry to the front.

The client holds a 65536-entry table and keeps only the low byte of each entry,
but every index it looks up comes from a byte, so entries above 255 are never
reached and the table starts as the identity either way. A 256-byte table is
exactly equivalent.

### Stage two — frequency expander

Stage one's output is a 1024-byte frequency table followed by a code stream:

```
+0x000  u32 freq[256]   one occurrence count per byte value; they sum to outputSize
+0x400  u8  codes[]     one byte per decoded byte
```

Symbols are ordered by descending count, ties going to the lower byte value
(`sortBytesByDescFreq @ 0042898E` is a find-max-and-zero pass, which is what
produces that tie-break). Each symbol owns a run of the code stream in that
order. The byte at the head of a run seeds the symbol table; the rest are move
codes, one per occurrence bar the last.

Decoding walks the output emitting the current symbol, then consumes one code
from that symbol's run: `0` repeats the symbol, which is how runs are coded, and
`k` reinserts it at rank `k` and takes the new front. When a run is spent the
symbol is dropped from the alphabet instead.

Put another way, the table always holds the symbols still to come, ordered by
which appears next — which is also the whole of what a MegaCliloc encoder needs
to know. `MegaClilocFixture` builds one from that observation in a single
backward pass, so the decoder is covered without any client file.

### Two things worth knowing

**The frequency sum is a real check.** It must equal the header's length. Files
that fail it are not merely unusual; the length is the one part of the format
stated twice, so disagreeing means one of the two was misread. Decoding without
that check yields a plausible-looking buffer of the wrong size, which a caller
cannot distinguish from a good one.

**The code stream is followed by padding.** It is exactly `outputSize` bytes, and
every shipped cliloc carries several kilobytes beyond it — 15,368 in the
7.0.114.4 files. Size the work from the header, not from the file: a decoder
that runs to the end of the buffer is reading the encoder's slack, and one that
loses a byte at the end will not notice, because the byte it lost was padding.
