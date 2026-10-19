# Personal data and repository publishing

Keep real MyKad numbers, names, addresses, dates of birth, portraits, raw card
responses, and database exports out of this repository and GitHub issues.
Use clearly fictional identities for tests and examples. The `000101000000`
fixture is synthetic; it is not a record read from a card.

The app saves records only after an explicit Save action. Its local SQLite
database is unencrypted. Keep the entire `Data` folder private. Hardware
verification must report only success/availability flags, never actual values.

`.gitignore` excludes database files and sidecars, credentials, local tools,
build outputs, photos/screenshots, exports, backups, archives and crash dumps.
These rules do not remove files already committed, prevent forced additions,
or detect personal information pasted into ordinary source or Markdown files.

Before publishing, inspect `git diff --cached` and `git diff --cached --name-only`.
Stage source, project files, fictional tests, documentation and license notices
only. Do not attach databases, real-card screenshots or raw card logs to issues
or releases. A release ZIP must contain application files and documentation only.

If a credential has already been published, revoke/rotate it. Removing a file
from the latest commit does not remove it from history. Coordinate any history
cleanup with repository collaborators; do not assume `.gitignore` cleans history.
