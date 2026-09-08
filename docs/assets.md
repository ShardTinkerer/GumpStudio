# Assets recovered from GumpStudio 1.8

Two pieces of artwork in `source/GumpStudio.App/Assets/` came out of the 2004
build rather than being drawn for the rewrite. Neither can be regenerated from
this repository's source, so how they were obtained is written down here.

| File | Origin |
|---|---|
| `splash.jpg` | `$this.BackgroundImage` in `src/GumpStudioCore/Forms/SplashForm.resx` |
| `gumpstudio.ico` | the icon resource on `external/Gumpstudio1.8r3/GumpStudio.exe` |

The original's own about text credits the artwork to **Melanius**; the editor it
belongs to was written by **Bradley Uffner**. `AboutWindow` repeats those credits
verbatim, which is the point of keeping them.

## The splash graphic

454x158, 24-bit JPEG, 55,173 bytes, copied **byte for byte** — not re-encoded.
Re-saving a JPEG re-quantises it, and this one is already lossy over lettering,
which is where the artefacts show worst.

1.8 embedded the same image twice, in `SplashForm.resx` and in
`AboutBoxForm.resx` (`PictureBox1.Image`). Both blobs hash identically
(`956a6b78…`), so there is only one asset here and both windows share it.

Extraction is plain base64. The entries are
`mimetype="application/x-microsoft.net.object.bytearray.base64"`, which for a
`System.Drawing.Bitmap` is the image file itself — so no `BinaryFormatter` is
involved, which matters because `BannedSymbols.txt` forbids it:

```python
import base64, re, xml.etree.ElementTree as ET

data = ET.parse("src/GumpStudioCore/Forms/SplashForm.resx").getroot()
blob = data.find('data[@name="$this.BackgroundImage"]/value').text
open("splash.jpg", "wb").write(base64.b64decode(re.sub(r"\s+", "", blob)))
```

The sibling `Bitmap1` entry in both files *is* `binary.base64`, i.e. a
`BinaryFormatter` payload. It is a designer leftover that nothing referenced, and
it was not extracted.

## The icon

There are **two** icons in the original, and they are different images that
happen to be the same byte length:

- The icon on `GumpStudio.exe` — "Gump Studio" in light-blue pixel lettering over
  overlapping coloured rectangles. This is what the taskbar and title bar showed,
  so it is the application's identity and it is the one shipped here.
- `$this.Icon` in `AboutBoxForm.resx` — a document sheet with a folded corner, a
  red `GUMP` label and four rectangles. It reads as a file type. Not used.

All three builds in `external/` carry the same executable icon: one 32x32 image,
256 colours, 2,216 bytes.

`gumpstudio.ico` holds four entries:

| Size | Format | Source |
|---|---|---|
| 32x32 | BMP | the 2004 payload, unmodified |
| 64x64 | PNG | nearest-neighbour x2 |
| 128x128 | PNG | nearest-neighbour x4 |
| 256x256 | PNG | nearest-neighbour x8 |

Only 32x32 existed. Windows wants larger sizes for the taskbar, Alt-Tab and file
dialogs, and smooth-scales what it is given, which turns pixel lettering to mush.
The upscales are **exact integer multiples with nearest-neighbour sampling**, so
every original pixel becomes a clean square block: sharp, and with nothing
invented that was not in the 32x32.

### Two traps worth recording

Pulling an icon out of a PE file is not the same as reading a `.ico`:

- `Icon.ExtractAssociatedIcon` returns one rendered size, not the resource. The
  authentic image has to come from `RT_GROUP_ICON` plus the `RT_ICON` entries it
  names (`LoadLibraryEx` with `LOAD_LIBRARY_AS_DATAFILE`, then
  `EnumResourceNames` / `FindResource`).
- `GRPICONDIRENTRY` is 14 bytes and ends with a 2-byte resource id, where
  `ICONDIRENTRY` is 16 and ends with a 4-byte file offset. They share only their
  first 12 bytes. Copying 12 bytes *and then* rewriting the size field produces
  20-byte entries and an icon that every decoder rejects — silently, since the
  file still writes. `ArtworkTests` parses the shipped directory and checks each
  entry's offset and length actually lie inside the file, which is what catches
  this.

`System.Drawing.Icon` will hand back the 128x128 image when asked for 256x256.
That is a limitation of that API, not a defect in the file; the shell and
Avalonia both pick the 256 entry.

## How they are loaded

`Assets/**` is an `AvaloniaResource`, so the files are embedded in the assembly
and a single-file or NativeAOT publish carries them. `Controls/Artwork.cs` opens
`avares://GumpStudio.App/Assets/splash.jpg` once and holds the decoded bitmap for
the life of the process — both windows that show it are transient, and an
`Image` does not own its source.

`<ApplicationIcon>` puts the icon on the executable; `MainWindow.axaml` sets the
same file as the window icon.

## What is not covered

`ArtworkTests` reads the shipped bytes and parses the JPEG and ICO headers rather
than decoding through `Bitmap`. The headless platform stubs drawing, so a decoded
bitmap reports a 1x1 placeholder and any size assertion against it would pass
whatever the asset contained. Nothing asserts what the images *look like* — that
is a job for eyes, and the checklist is in `docs/status.md`.
