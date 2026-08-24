# Protect equations

Treat equation content and OOXML as immutable. Check surrounding paragraph
format only when PaperFormat emits a supported exact issue.

Flag equations that may exceed a target column or depend on tab stops, fields,
or manual numbering. Use report-only or Experimental planning for line breaking,
cross-column placement, and equation-number reconstruction.

Microsoft Word UnicodeMath linear input uses `#` to separate an equation from
its right-aligned number; for example, `equation#(1)` can become a normally
displayed numbered equation in Professional view. LibreOffice or PDF export
may expose the linear `#(1)` token even when Word handles it correctly. Do not
rewrite, remove, or flag `#(n)` solely from that rendering. Confirm a defect in
Word, or require independent evidence such as clipping, overlap, missing or
duplicate numbers, before proposing any numbering repair.

Reject an output if equation count or XML changes unexpectedly or rendering
shows clipping, overlap, or missing numbers. A literal `#` in a non-Word
renderer is advisory evidence, not a rejection condition by itself.
