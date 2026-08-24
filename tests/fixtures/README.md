# DOCX fixtures

No DOCX fixture is tracked by Git. The generator source is public; generated
documents are written to the ignored `tests/fixtures/generated/` directory and
may be deleted at any time. CI uses them only inside its ephemeral runner and
does not upload them as downloadable artifacts.

Run the fixture generator from the `PaperFormat/` repository root:

```bash
./scripts/dotnet.sh run --project tools/PaperFormat.Fixtures -- \
  --output tests/fixtures/generated
```

The output contains:

- `valid-ieee-like.docx`: a small US Letter, portrait, two-column,
  Times New Roman manuscript with explicit styles for title, authors,
  affiliation, abstract, keywords, headings 1–3, body, captions, and table
  text.
- `wrong-format.docx`: the same semantic structure with real OOXML deviations,
  including A4 landscape paper, one column, enlarged margins, wrong
  fonts/sizes, paragraph spacing, alignment, caption styling, and table text
  formatting.
- `integrity-rich.docx`: a content-integrity baseline containing a PNG,
  external hyperlink, bookmark and REF field, footnote, endnote, OMML
  equation, table, header, and footer.

The valid fixture also contains the integrity-bearing objects so parser tests
can assert that normal format analysis preserves unsupported content.

## Determinism

The generator fixes package metadata, relationship identifiers, content order,
ZIP entry order, and ZIP timestamps. Re-running it with the same SDK/runtime
produces byte-stable files. Tests should additionally compare parsed semantic
snapshots so a runtime compression change cannot hide an OOXML regression.

These files are synthetic temporary outputs. Never commit them, substitute a
user manuscript, or edit their binary packages by hand.
