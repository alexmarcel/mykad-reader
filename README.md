# MyKad Reader — portable Windows prototype

Extract `MyKadReader-portable-win-x64.zip` into a folder you can write to, then
double-click `MyKadReader.exe`. The .NET runtime and SQLite engine are included.
No application installation, database server, administrator privileges, or
internet connection is required to run it. Target: Windows 11 x64.

## Read and optionally save

1. Connect the Alcorlink USB Smart Card Reader. Windows must recognize the reader
   and its Smart Card service must be available. The app does not install drivers
   or change Windows services.
2. Select the reader, insert a MyKad, and click **Read card**.
3. Inspect the name, MyKad number, date of birth, gender, address, and portrait.
   Unsupported/missing fields are shown as unavailable.
4. Click **Save to database** only if you want to keep the displayed record.
   Reading a card never automatically saves it.
5. Under **Saved records**, search by name or MyKad number, select a row to view
   details, or explicitly delete it after confirmation.

One record is retained per 12-digit MyKad number. Saving again replaces all
fields and the portrait with the latest read, including unavailable fields;
it preserves the original creation timestamp. There is no history or manual editing.
Card removal, changing readers, or Clear removes the current display, not saved records.
Cancel discards the pending result; a native reader command already in progress
must return before another read can start. A 30-second cancellation deadline is
checked between commands; it cannot forcibly interrupt a hung reader driver.

## Portable data and updates

The first explicit save creates `Data/mykad.db` beside the executable. It contains
identity fields and photo bytes in an **unencrypted SQLite file without an app
password**. Anyone who can access that file can read its records. The app writes
no card details to diagnostic logs or separate image files.

- Run from a local folder or USB drive. Saving requires write access and free space.
- If storage is unwritable, card reading and accessible saved-record viewing still work.
  Move the entire app folder to writable storage and restart, or use Search to recheck permissions.
- **Backup/move:** close the app, then copy the entire `Data` folder. Do not unplug
  a USB drive while the app is saving. SQLite may briefly create a transaction journal.
- **Update:** close the app, back up `Data`, and extract the new release over the
  application files while preserving `Data`. Release ZIPs contain no database.
- **Remove:** delete the application folder. Keep a backup of `Data` if records are needed.
- Only one app instance per folder is allowed within a Windows session. Shared
  network databases and concurrent multi-user use are not supported.
- Deletion removes the database record; it is not a secure disk-erasure feature.

## Compatibility status

The direct PC/SC reader uses a community JPN application layout (personal file 1,
address file 4, JPEG photo file 2). It checks response status and lengths and
rejects unsupported responses rather than fabricating fields. It does not
derive date of birth or gender from the printed number, access fingerprints,
write to cards, or verify card authenticity.

**An actual card read succeeded on `Alcorlink USB Smart Card Reader 0`.** The
number, name, date of birth, gender, address and a decodable photo were returned.
The verification printed only availability flags and saved no identity data.
Visual comparison against the physical card remains pending. The implementation
is a prototype, not a claim of support for every card generation. Newer or
different cards may need an approved vendor SDK or a different protocol.
If selection or reading fails, note only the stage and numeric PC/SC/card status
shown by the app; do not share raw personal card data for diagnostics.

See `docs/ACCEPTANCE.md` for remaining real-hardware and clean-machine checks.

## Build and test

Install a .NET 10 SDK on the **development** machine, or put a local SDK under
`.tools/dotnet`. End users do not need it. From the repository root:

```powershell
./build.ps1
```

If PowerShell blocks local scripts, run `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1`.
This applies to that build process only and does not change the machine's policy.

This restores dependencies, builds and runs the executable test suite, then
publishes a self-contained release and ZIP under `artifacts`. No simulator or
test database is included in the ZIP. Build scripts disable .NET CLI telemetry
for their process and keep the NuGet cache under `.tools/packages`.

For an explicitly selected fictional development reader:

```powershell
dotnet run --project src/MyKadReader -c Debug -- --simulate
```

Simulation is accepted only by Debug builds, visibly labelled, and uses a
separate `Data/simulator.db`. Never use it as evidence of hardware compatibility.
The Release app ignores this switch and always uses the real reader.

To enumerate PC/SC readers without reading identity data:

```powershell
./tests/MyKadReader.Tests/bin/Release/net10.0-windows/win-x64/MyKadReader.Tests.exe --probe-reader
```

The test suite uses fictional fixtures and temporary SQLite files. It also
renders a WPF preview into its build-output folder. It never reads a real card.

## Structure

- `src/MyKadReader.Core`: decoder, bounded APDU protocol, PC/SC transport,
  repository interfaces, and parameterized SQLite storage.
- `src/MyKadReader`: WPF reader/saved-record screens, cancellation and storage handling.
- `tests/MyKadReader.Tests`: executable assertions covering protocol, decoder,
  database behavior and in-process WPF checks.

Protocol reference and dependency licenses are in `THIRD-PARTY-NOTICES.md` and
`licenses`. The application has no cloud, export, clipboard, OCR or login feature.
