# Five layouts, one set of people

Fifteen invented people — Star Wars characters at example.com — printed five
different ways: a table, profile cards, a two-up directory grouped by
affiliation, intake forms, and a mainframe dump of `KEY=VALUE` stanzas.

These are PDFs rather than built in code, which is the one place this repository
departs from its own rule. The rule exists to keep real people out of version
control, and these hold none: every name is fictional and every address is at
`example.com`. Re-drawing them in C# would also be worse evidence, because a
fixture written by the same hand as the reader tends to encode the reader's
assumptions and pass for the wrong reason.

They are here because they are the files that showed the importer was wrong. Read
by looking for addresses line by line — which is what it used to do — they came
back as fifteen people called `COMLINK MESSAGE ADDRESS`, and two of the five
silently lost a third to two-thirds of their people.

Every one of the fifteen is named in the email address, so the tests check the
names they read against the addresses they came with, rather than against a list
written out by hand.
