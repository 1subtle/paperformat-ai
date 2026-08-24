# Venue coverage policy

PaperFormat separates three claims that must not be conflated.

## Verified deterministic capability

For DOCX/DOTX, PaperFormat can extract supported page, paragraph, character,
style, caption, and table-text rules from an exact Word template; check a
manuscript; apply allow-listed repairs to a copy; and run package, content,
rendered-page, and visual gates. The controlled IEEE-like fixture is generated
at test time in a Git-ignored directory and is the current end-to-end release
baseline.

## Template-driven coverage

Other journals and conferences are covered through their exact current
official template or a user-reviewed RulePackage. This includes major
publisher and society ecosystems and the named conference families in
`venue-catalog.json`. A catalog entry is a source-discovery route, not a claim
that every property of every venue edition has been pre-encoded or certified.

The public `assets/templates/` registry is empty and the repository tracks no
document or template binary. If an explicitly redistributable asset is added
later, `template-library.md` requires a complete identity match or exact asset
selection, and supported rules are derived from that artifact at runtime. A
bundled family reference is never a fallback for another venue, year, track,
or stage.

## Conservative LaTeX coverage

For LaTeX-first venues, the Skill resolves the exact official class and style
package, protects a compilable baseline, limits edits to a reviewed migration,
rebuilds, checks references and content inventories, and visually reviews the
PDF. The .NET engine does not mutate LaTeX and no deterministic OOXML integrity
claim applies to that route.

## Claim boundary

Never say "all venues are supported" or "submission compliant" solely because
the resolver recognized a name. State instead:

- which exact venue edition and stage were resolved;
- which official artifacts were used;
- which rules were extracted or explicitly encoded;
- which operations and validation gates are supported;
- which requirements remain unresolved, unsupported, or visually uncertain.

When a venue changes its author kit, the current official artifact supersedes
the catalog's routing hints and every prior task's cached assumptions.
