# Third-party notices

This repository is licensed under the MIT terms in [LICENSE](LICENSE). MIT
carries no notice-retention text of its own, so where a dependency's licence
requires its notice to travel with the software, that notice is reproduced here
and this file ships in every release archive.

## GumpStudio 1.8 — Bradley Uffner, 2004

The application this one reproduces was designed and written by **Bradley
Uffner** in 2004. It carried no stated licence, and its original VB.NET source
is lost. The MIT grant in `LICENSE` therefore covers **this rewrite only** — it
is not, and cannot be, a licence granted on the original author's behalf.

Two pieces of artwork are the 2004 build's, recovered byte-for-byte rather than
redrawn, and are not the rewrite's work:

| File | Credit |
|---|---|
| `source/GumpStudio.App/Assets/splash.jpg` | artwork by **Melanius** |
| `source/GumpStudio.App/Assets/gumpstudio.ico` | the original executable's icon |

[docs/assets.md](docs/assets.md) records how each was extracted and why it is
kept unmodified. The original also made extensive use of a modified **UOSDK**
written by **Krrios**, and several ideas were contributed by the RunUO
community. Thanks go to **DarkStorm** of the Wolfpack emulator for help decoding
`unifont.mul`.

## ClassicUO — BSD-2-Clause

The `.uop` container format and its Burrows-Wheeler stage were understood with
reference to [ClassicUO](https://github.com/ClassicUO/ClassicUO). No ClassicUO
code is included here; the reference was to its implementation. Its licence
requires the notice below to be retained either way.

```
BSD 2-Clause License

Copyright (c) 2025, andreakarasho
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

- Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

- Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

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
