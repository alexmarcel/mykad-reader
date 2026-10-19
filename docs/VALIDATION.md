# Validation results — 2026-10-19

- Release build: passed, zero warnings and errors.
- Executable test suite: 18 grouped tests passed, including database and in-process WPF workflows.
- Dependency restore with NuGet audit and warnings as errors: passed after updating
  Microsoft.Data.Sqlite to 10.0.13 (SQLitePCLRaw 2.1.13).
- Direct Windows PC/SC enumeration: `Alcorlink USB Smart Card Reader 0` detected;
  inserted card subsequently read successfully after the user indicated readiness.
- Real-card retrieval: number, name, date of birth, gender, address and a decodable
  photo all returned. Only availability flags were printed; no identity values
  or images were printed or saved. Visual correctness comparison remains pending.
- Portable executable startup and clean shutdown: passed on this development machine.
- Runtime inspection: `hostfxr.dll` and `coreclr.dll` loaded from the portable
  release folder, not the system runtime directory.
- Startup/read-only browsing created no Data directory or database.
- Identity view and full window content rendered and inspected with fictional data.

Not verified: visual correctness of the retrieved identity/portrait, clean Windows
without .NET, physical USB-drive use, full folder transfer to a second PC,
disk-full/disconnected storage, or real reader unplug/reinsert timing. These
remain explicit acceptance items in ACCEPTANCE.md. Automated removal/cancellation
tests use a controlled reader and are not substitutes for these hardware checks.
