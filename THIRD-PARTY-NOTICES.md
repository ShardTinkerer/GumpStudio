# Third-party notices

This repository is licensed under the MIT terms in [LICENSE](LICENSE). MIT
carries no notice-retention text of its own, so where a dependency's licence
requires its notice to travel with the software, that notice is reproduced here
and this file ships in every release archive.

## GumpStudio 1.8 — Bradley Uffner, 2004

The application this one reproduces was designed and written by **Bradley
Uffner** in 2004. It carried no stated licence, and its original VB.NET source
is lost. The MIT grant in `LICENSE` therefore covers **this codebase only** —
it is not, and cannot be, a licence granted on the original author's behalf.

Two pieces of artwork are the 2004 build's, recovered byte-for-byte rather than
redrawn, and are not this project's work:

| File | Credit |
|---|---|
| `source/GumpStudio.App/Assets/splash.jpg` | artwork by **Melanius** |
| `source/GumpStudio.App/Assets/gumpstudio.ico` | the original executable's icon |

[docs/assets.md](docs/assets.md) records how each was extracted and why it is
kept unmodified. The original also made extensive use of a modified **UOSDK**
written by **Krrios**, and several ideas were contributed by the RunUO
community. Thanks go to **DarkStorm** of the Wolfpack emulator for help decoding
`unifont.mul`.

## The exporters

Each converter is a port of earlier community work, none of which carried a
stated licence. They are credited here for the same reason the README credits
them: the lineage is real and worth recording.

| Converter | Lineage |
|---|---|
| POL | derives from work by **Fernando Rozenblit**, itself based on the Sphere exporter by **Francesco Furiani** and **Mark Chandler** |
| Sphere | a port of **Francesco Furiani**'s exporter |
| RunUO | a port of **roadmaster / Mark Sweetman**'s, itself based on **Daegon / Eric Brown**'s |

## NuGet dependencies

Every package version is pinned in
[Directory.Packages.props](Directory.Packages.props); each package carries its
own licence metadata, which `dotnet list package --include-transitive` will
enumerate. None of them require a notice to be reproduced here.
