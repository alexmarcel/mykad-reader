# Acceptance checklist

Automated tests use fictional data. They do not establish real-card support.

## Automated checks (run with build.ps1)

- Personal/address decoding; invalid dates, text, numbers and truncated layouts.
- APDU chunking, rejection handling, cancellation, optional-field failure.
- JPEG framing and WPF image decoding/rendering.
- No database creation from browsing; explicit save and normalized unique number.
- Reopening and photo round-trip, duplicate update, missing fields replacing old values.
- Creation/update timestamps; literal search, pagination, deletion.
- Locked-write failure without partial update; read-only viewing; corrupt/newer-schema preservation.
- Copying a closed database to a path containing spaces and non-ASCII characters.
- WPF cancellation, subsequent read, Clear, and no implicit save.

## Required hardware checks — NOT YET VERIFIED

Use a consenting cardholder's card. Do not attach personal records or screenshots
to source control or issue reports.

- [x] Direct PC/SC enumeration detects `Alcorlink USB Smart Card Reader 0`.
- [x] Actual card insertion is detected.
- [x] Real-card read returns number, name, date of birth, gender, address and a decodable portrait without saving or printing identity values.
- [ ] Identity fields match the physical card; portrait is the correct person.
- [ ] Save, restart, search and reopen the same identity and portrait.
- [ ] Read another card and save; neither display nor database mixes identities.
- [ ] Remove card during reading; no stale result or Save action remains enabled.
- [ ] Remove and reinsert/switch cards quickly; change counter invalidates prior result.
- [ ] Unplug reader while idle/reading; reconnect and retry.
- [ ] Cancel during reading, then read again; no late result replaces the new state.
- [ ] Try an unsupported card and confirm a clear error without persisted data.

## Required deployment checks — NOT YET VERIFIED

- [ ] Clean Windows 11 x64 with no .NET installation; standard user; offline launch.
- [ ] Actual reader works with an already available Windows driver (no app-side install).
- [ ] Run from a USB drive and a folder with non-ASCII characters/spaces.
- [ ] Unwritable folder disables mutations while reading/viewing remain usable.
- [ ] Disk-full/disconnected storage reports save failure without reporting success.
- [ ] Move the closed full application folder to a second PC; view existing records/photos.
- [ ] Start a second instance from the same folder; only the first remains open.
- [ ] Update application files while retaining Data; existing records remain readable.

If real-card reads fail, capture the failing stage and status code only. Resolve
the protocol/SDK choice before marking hardware support complete. The protocol
is based on public community references. One real card has now returned all
requested fields and a decodable portrait; this does not establish compatibility
with every card layout or verify visual correctness against the physical card.
