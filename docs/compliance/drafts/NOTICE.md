# Third-Party Software Notices and Attributions

<!--
DRAFT — proposed for adoption as /NOTICE (and shipped inside the installer and Docker image).
See ./README.md. Verify with an automated licence scanner before each release; this list was
compiled by hand on 2026-09-30 against the csproj files and must be regenerated when
dependencies change.
-->

**[PRODUCT NAME]** · Copyright (c) [YEAR] [LEGAL ENTITY NAME]

This product includes, or interoperates with, third-party software. The components and their
licences are listed below. Each component remains subject to its own licence terms, which
take precedence over the product's End-User Licence Agreement with respect to that component.

Full licence texts are distributed alongside this file in `licenses/`.

---

## 1. Trademark and affiliation disclaimer

**This product is not produced by, endorsed by, affiliated with, or supported by Telegram
Messenger Inc. or any of its affiliates.**

"Telegram" is a trademark of Telegram Messenger Inc. Any reference to it in documentation is
solely to describe interoperability. This product uses the publicly documented Telegram API
and requires you to supply your own API credentials obtained from <https://my.telegram.org>.

Other trademarks referenced in documentation — including YouTube, Instagram, TikTok, X,
Reddit, Facebook, Synology, Unraid, TrueNAS, Docker, and Microsoft — are the property of
their respective owners and are used for identification purposes only.

---

## 2. Managed (.NET) components

All components in this section are licensed under permissive terms (MIT or Apache-2.0). None
imposes a source-disclosure obligation.

### MIT-licensed components

| Component | Version | Copyright | Project |
|---|---|---|---|
| WTelegramClient | 4.4.2 | © 2021-2026 Olivier Marcoux | <https://github.com/wiz0u/WTelegramClient> |
| MonoTorrent | 3.0.2 | © Alan McGovern and contributors | <https://github.com/alanmcgovern/monotorrent> |
| Ytdlp.NET | 3.0.4 | © Manojbabu (ManuHub) | <https://github.com/manusoft/Ytdlp.NET> |
| Newtonsoft.Json | 13.0.3 | © James Newton-King | <https://github.com/JamesNK/Newtonsoft.Json> |
| MahApps.Metro | 2.4.11 | © MahApps contributors | <https://github.com/MahApps/MahApps.Metro> |
| QRCoder / QRCoder.Xaml | 1.7.0 | © Raffael Herrmann | <https://github.com/codebude/QRCoder> |
| dotenv.net | 3.2.0 | © Bolorunduro Winner-Timothy | <https://github.com/bolorundurowb/dotenv.net> |
| Microsoft.Data.Sqlite | 8.0.10 | © Microsoft Corporation | <https://github.com/dotnet/efcore> |
| .NET Runtime & ASP.NET Core | 8.0 | © Microsoft Corporation | <https://github.com/dotnet/runtime> |

The MIT licence text applies to each of the above:

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this
software and associated documentation files (the "Software"), to deal in the Software
without restriction, including without limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

### Apache-2.0-licensed components

| Component | Version | Copyright | Project |
|---|---|---|---|
| Serilog | 4.2.0 | © Serilog Contributors | <https://github.com/serilog/serilog> |
| Serilog.Sinks.File | 6.0.0 | © Serilog Contributors | <https://github.com/serilog/serilog-sinks-file> |
| Serilog.Sinks.Console | 6.0.0 | © Serilog Contributors | <https://github.com/serilog/serilog-sinks-console> |
| Serilog.Sinks.Seq | 8.0.0 | © Datalust and Serilog Contributors | <https://github.com/datalust/serilog-sinks-seq> |
| Serilog.AspNetCore | 8.0.3 | © Serilog Contributors | <https://github.com/serilog/serilog-aspnetcore> |
| Dapper | 2.1.35 | © Sam Saffron, Marc Gravell, Nick Craver | <https://github.com/DapperLib/Dapper> |

Licensed under the Apache License, Version 2.0 (the "License"); you may not use these files
except in compliance with the License. You may obtain a copy of the License at
<http://www.apache.org/licenses/LICENSE-2.0>. Unless required by applicable law or agreed to
in writing, software distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

> **Note on Apache-2.0 §4(d):** where an upstream project ships its own `NOTICE` file, that
> file's contents must be reproduced in our distribution. The full texts are included in
> `licenses/`.

### Development and test-only components — not distributed

These are used to build and test the product and are **not** included in any shipped artifact.
Listed for completeness and supply-chain transparency.

| Component | Version | Licence |
|---|---|---|
| xunit | 2.7.0 | Apache-2.0 |
| xunit.runner.visualstudio | 2.5.7 | Apache-2.0 |
| FluentAssertions | 6.12.0 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.9.0 | MIT |
| RichardSzalay.MockHttp | 7.0.0 | MIT |
| FlaUI.UIA3 | 4.0.0 | MIT |

> **Internal build note (remove before publication):** FluentAssertions **v8 and later are
> under a paid Xceed commercial licence**; v6/v7 remain Apache-2.0. Pin the version range to
> `[6.12.0,8.0.0)` in both test projects. See `../06-compliance.md` §1.3, Caveat A.

---

## 3. External executables

The product invokes the following programs as **separate operating-system processes**. It does
not link against them, does not load them into its own address space, and does not
incorporate their code.

### yt-dlp

- Project: <https://github.com/yt-dlp/yt-dlp>
- Licence of the yt-dlp source: **The Unlicense** (public domain dedication)

```
This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or distribute this software,
either in source code form or as a compiled binary, for any purpose, commercial or
non-commercial, and by any means.

In jurisdictions that recognize copyright laws, the author or authors of this software
dedicate any and all copyright interest in the software to the public domain. We make this
dedication for the benefit of the public at large and to the detriment of our heirs and
successors. We intend this dedication to be an overt act of relinquishment in perpetuity of
all present and future rights to this software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES
OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

For more information, please refer to <http://unlicense.org/>
```

**Important — the licence of yt-dlp *binaries* differs from the licence of its source.**
As stated by the yt-dlp project: *"While yt-dlp is licensed under the Unlicense, many of the
release files contain code from other projects with different licenses. Most notably, the
PyInstaller-bundled executables include GPLv3+ licensed code, and as such the combined work is
licensed under GPLv3+."*

| Distribution | What is obtained | Effective licence |
|---|---|---|
| Windows | `yt-dlp.exe` (PyInstaller bundle), downloaded from the yt-dlp GitHub releases **by the end user's own installation at first run** | **GPLv3+** |
| Docker image | Debian's `yt-dlp` package (Python, not a PyInstaller bundle) | **Unlicense** |

Third-party licence texts for the components inside the PyInstaller bundle are published by the
yt-dlp project at
<https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt>.

### FFmpeg

- Project: <https://ffmpeg.org/> · Source: <https://github.com/FFmpeg/FFmpeg>
- Licence: **Varies by build configuration.** Most of FFmpeg is LGPL-2.1-or-later, but builds
  configured with `--enable-gpl` — which is required to link GPL-licensed codec libraries such
  as libx264 and libx265 — are licensed **GPL-2.0-or-later**.

| Distribution | Source of the binary | Effective licence |
|---|---|---|
| **Docker image** | Debian package, installed via `apt-get install ffmpeg` | **GPL-2.0-or-later** |
| Windows | `ffmpeg-master-latest-win64-gpl.zip` from yt-dlp/FFmpeg-Builds, downloaded **by the end user's own installation at first run** | **GPL-2.0-or-later** |

Debian's own copyright record for the `ffmpeg` package states: *"For building the default
Debian packages some of the GPL licensed files are used, so the resulting binaries are
licensed under GPL v2+."*

#### Written offer for corresponding source (GPL-2 §3(b))

> **Include this section ONLY if the Docker image continues to ship ffmpeg.**
> `../06-compliance.md` §1.4 recommends removing ffmpeg from the image instead, which
> eliminates this obligation entirely. Legal Question 4 asks counsel to confirm whether
> pointing to Debian's archives is sufficient or whether the vendor must mirror the source.

The Docker image distributed by [LEGAL ENTITY NAME] contains FFmpeg binaries licensed under the
GNU General Public License, version 2 or later. The complete corresponding source code for
those binaries, and for the GPL-licensed libraries they link, is available as follows:

- Upstream Debian source packages for the exact versions shipped, obtainable via
  `apt-get source ffmpeg` inside the image, or from <https://snapshot.debian.org/>.
- Alternatively, for a period of **three (3) years** from the date you received this image,
  [LEGAL ENTITY NAME] will provide, on written request to [LEGAL EMAIL], a complete
  machine-readable copy of the corresponding source code for a charge no more than the cost
  of physically performing the distribution.

The full text of the GNU General Public License version 2 is included in
`licenses/GPL-2.0.txt`, and version 3 in `licenses/GPL-3.0.txt`.

#### Statement on the relationship between this product and FFmpeg/yt-dlp

[PRODUCT NAME] is an independent work. It communicates with `yt-dlp` and `ffmpeg` only by
launching them as separate executable programs and reading their standard output. It does not
link to, statically or dynamically embed, or incorporate any part of their code. Accordingly,
[PRODUCT NAME] is not a derivative work of either program, and the GPL and LGPL do not extend
to [PRODUCT NAME]'s own source code.

### SQLite

The Docker distribution embeds SQLite via `Microsoft.Data.Sqlite`. SQLite itself is in the
**public domain**. <https://www.sqlite.org/copyright.html>

---

## 4. Regeneration

This file must be regenerated whenever dependencies change. Recommended tooling:

```
# Managed dependency licences
dotnet tool install --global nuget-license
nuget-license -i RunnerApp.sln -t -o JsonPretty

# Container SBOM, including apt-installed packages such as ffmpeg
syft <image>:<tag> -o spdx-json
```

The container SBOM is the authoritative record for the GPL-2+ ffmpeg obligation, because
`apt` pulls in transitive dependencies that hand-maintained lists miss.

---

*Compiled 2026-09-30 from all 11 project files in the repository. Verify before each release.*
