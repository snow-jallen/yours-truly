# Sample files

Six PDFs to try Yours Truly on before pointing it at anybody real. Open the app, go to
**People → Import a file**, and pick one.

Every one holds the same kind of thing — people with names, email addresses and phone
numbers — printed a completely different way. None of the people is real: the names are
invented and every address is on `example.com`, which
[exists to be used this way](https://www.rfc-editor.org/rfc/rfc2606.html) and can never
receive mail. The phone numbers are in the 555-01xx range reserved for fiction.

| File | What it is | How the app reads it |
| --- | --- | --- |
| `01-operations-table.pdf` | A wide table, no heading row | as records |
| `02-profile-cards.pdf` | A grid of cards, two or three to a row | as records |
| `03-affiliation-directory.pdf` | A directory in two columns, grouped under headings | as records |
| `04-intake-forms.pdf` | Filled-in forms, one block per person | as records |
| `05-mainframe-export.pdf` | `KEY=VALUE` stanzas from an old system | as records |
| `06-system-report.pdf` | A nine-column report with a proper heading row | as a table |

The last one is the easy case and the other five are the interesting ones. A heading row
tells the app what its columns *mean*, which no amount of looking at the page can work
out, so `06` is read as a table and its own headings are offered on the import screen.
The other five name nothing, so the app finds the records by their contact details and
says on screen that the columns are its invention rather than the file's.

## These are also the test fixtures

`VariedLayoutTests` reads these exact files and checks that each of the fifteen people
comes back with the name that belongs to their address. They live here rather than under
`tests/` so that there is one copy, and so a sample that stops working fails the build.

Which means: **do not add a reader for a layout.** None of these is taught to the app,
and that is the point. If a seventh file turns up and fails, the general rule is wrong
and the fix goes there — not into a seventh special case. See `docs/architecture.md`.
