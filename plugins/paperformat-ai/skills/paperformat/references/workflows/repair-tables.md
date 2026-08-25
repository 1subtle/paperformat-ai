# Handle tables and Algorithms

Distinguish text formatting from table geometry.

Allow only exact character-level table-text operations currently emitted by
PaperFormat: font family, size, bold, and italic. Scope approval to one concrete
table.

Preserve or report:

- borders and three-line-table rules;
- column widths and row heights;
- merged cells;
- table positioning and wrapping;
- Algorithm layout and numbering structure;
- cross-column or full-width placement.

Inspect every affected table before and after rendering. Fail the visual review
if borders, geometry, content order, caption relationship, or page placement
changes without a separately supported and approved operation.
