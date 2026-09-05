# Architecture

Seven projects, all `net10.0`. Two rules shape the layout:

1. **`System.Drawing` appears nowhere.** It is Windows-only on modern .NET, it
   made the old code impossible to test headlessly, and it produced a long tail
   of undisposed GDI handles. `BannedSymbols.txt` enforces this at compile time.
2. **Nothing below the app layer knows what a UI toolkit is.** The old
   `BasePlugin.Load(DesignerForm)` and
   `ElementExtender.AddContextMenus(ref MenuItem, …)` made every plugin a
   WinForms plugin.

```
GumpStudio.Uo            UO client data: .mul + .uop, art, gumps, hues,
                         tiledata, cliloc, ASCII + Unicode fonts.
                         Returns plain pixel buffers — no bitmaps.
        │
GumpStudio.Core          Element model, document model, commands + undo,
        │                XML serialisation, legacy NRBF import,
        │                exporter contract. Headless and testable.
        │
GumpStudio.Rendering     SkiaSharp renderer, art cache, hit-test geometry.
        │                Runs headless, which is what makes golden-image
        │                tests possible.
        │
GumpStudio.Plugins.Abstractions
        │                The plugin contract. No UI types.
        │
GumpStudio.App           Avalonia MVVM shell.
GumpStudio.Cli           Headless tooling: dump art, convert files, run
                         exporters without the UI.
GumpStudio.Plugins.Pol   POL exporter, the only shipped exporter.
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

## Planned: `GumpStudio.Core`

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
  ordering question, and gives plugins a safe way to mutate the document.
- **XML over an explicit DTO layer**, hand-written rather than reflection-based,
  so renaming a class never breaks a saved file.

## Planned: plugin contract

```csharp
public interface IGumpStudioPlugin
{
    PluginInfo Info { get; }
    void Initialize(IPluginHost host);
}

public interface IPluginHost          // no Avalonia types cross this boundary
{
    IGumpDocumentSession Session { get; }
    void RegisterExporter(IGumpExporter exporter);
    void RegisterMenuCommand(MenuCommandDescriptor descriptor);
    void RegisterCanvasLayer(ICanvasLayer layer);
    void RegisterInputFilter(IPointerInputFilter filter);
}
```

Menu contributions are declarative descriptors rendered by the shell, so plugins
never construct UI objects. Loading uses a collectible `AssemblyLoadContext`, so
plugins can be unloaded instead of showing a "restart the app" message box.
Plugin identity is a stable id string rather than value-equality over five
`PluginInfo` fields — in the old code, bumping a version string silently
un-loaded the plugin.
