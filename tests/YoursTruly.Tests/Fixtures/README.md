# Test fixtures

`private/` is gitignored and holds real exports of real people. They carry phone
numbers, e-mail addresses and birthdays for hundreds of them and must never be
committed — nor must anything derived from one. Fixtures that are committed are
synthetic and built in code, by `SyntheticReport`, `SyntheticCallingsReport`,
`SyntheticMemberList` and `SyntheticRoster`.

The tests that need a real export skip themselves when it is absent, so the suite
passes on a machine that has never seen a directory.

They are worth having, and worth more than they used to be. Yours Truly is no longer
told what any of these files are — it works the columns out — so these are what
say that reading them generically costs nothing against the files the reader was
originally written for.

| File | Where it came from |
|------|--------------------|
| `private/manti-singles.pdf`     | a 29-page directory export, 427 people |
| `private/manti-callings.pdf`    | a 9-page export with a second table above the first, 428 people |
| `private/manti-member-list.pdf` | a 5-page export of one unit, 160 people |

Any PDF of a table of people works; the page and person counts the tests assert are
in `RealReportTests`, beside the file each belongs to.

Delete them when you are finished with them.
