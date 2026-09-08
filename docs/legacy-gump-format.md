# The legacy `.gump` and `.gumpling` formats

How GumpStudio 1.8 saved its documents, and how this rewrite reads them without
re-enabling `BinaryFormatter`. Nothing here is a client format — these are the
2004 editor's own files, and the only reason they are documented is that the
importer has to keep reading them.

## What is in the file

Not a container, a header or a version stamp: just serialiser output, written
straight to disk.

| Extension | Contents |
|---|---|
| `.gump` | two consecutive `BinaryFormatter` payloads — an `ArrayList` of `GroupElement` pages, then a `GumpProperties` |
| `.gumpling` | one `BinaryFormatter` payload holding a single `GroupElement` |

A gumpling is therefore a fragment rather than a document, which is why the two
imports are different operations: opening a `.gump` **replaces** the document,
while opening a `.gumpling` **inserts** one group into the current one.

## Member names

Recovered by decompiling the original binary, since the VB source is lost. Every
type wrote its own version number as an ordinary field, so the version is
readable only after you already know the layout:

```
BaseElement     : BaseElementVersion (2), Name, Location, Size, Parent, Comment
GumpProperties  : Version (1), Location, Moveable, Closeable, Disposeable, Type
```

Each element subclass adds its own `<Type>ElementVersion` followed by its own
fields. `LabelElement` is the one to watch: 1.8r3-quinted raised
`LabelElementVersion` to 3 and made the font index 0-based, and it **decrements
`FontIndex`** when loading anything older. `LegacyGumpImporter` does not, so a
genuine 1.8-era file previews one font off — in the designer only, since the
gump protocol's `text` and `croppedtext` commands carry no font parameter and no
exporter has ever written one.

## Reading it safely

`BinaryFormatter` is removed from .NET 9+, and `BannedSymbols.txt` fails the
build on any reference to it. The importer uses **`System.Formats.Nrbf`**,
in-box since .NET 9, which decodes the record stream **without ever activating a
type**: it walks the records by hand with `GetArray` and `GetRawValue` and maps
the field names above onto the rewrite's own element model.

That is what makes the import read-only by construction. There is no path by
which a crafted `.gump` can cause a type to be constructed, because nothing in
the importer asks for one.

Saving is unrelated and shares no code: this editor writes versioned XML,
decoupled from CLR type names, so a renamed class cannot break an existing file.
