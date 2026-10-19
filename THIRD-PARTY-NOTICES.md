# Third-party notices

## MyKad protocol reference

The JPN application identifier, file offsets and command sequence in
`CardProtocol` and `MyKadDecoder` were adapted from the community reference at
https://github.com/deadboy18/mykad-reader/blob/main/MyKad%20project/mykad.py
(reviewed 2026-10-19). The C# implementation adds response validation, cancellation,
strict decoding and optional-field handling; it does not reproduce the reference's
extra demographic, SOCSO, locality, checksum or automatic file-output behavior.

Copyright (c) 2026 (deadboy18). MIT license, included in
`licenses/mykad-reference-MIT.txt`. No endorsement or hardware compatibility is implied.

Command-specific protocol status facts (`9108` for Set Length and `94xx` for
Select Info / Read Info) were cross-checked against the primary implementation at
https://github.com/JasonHiew/cardreadergui/blob/master/trunk/MyKad.cs .
No source code from that implementation is incorporated.

## Microsoft.Data.Sqlite 10.0.13

Copyright .NET Foundation and Contributors. MIT license in
`licenses/Microsoft.Data.Sqlite-MIT.txt`.
Source: https://github.com/dotnet/efcore

## SQLitePCLRaw 2.1.13

SQLitePCLRaw core, bundle and native-provider components by Eric Sink and
contributors are distributed under Apache License 2.0, included in
`licenses/SQLitePCLRaw-LICENSE.txt`.
Source: https://github.com/ericsink/SQLitePCL.raw

The bundled SQLite engine is public-domain software:
https://www.sqlite.org/copyright.html

## .NET and Windows Desktop runtime

The self-contained distribution includes the .NET and Windows Desktop runtime.
Their license and third-party notices are copied from the resolved Microsoft
runtime packages into the release's `licenses` folder by `build.ps1`.
Sources: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf
