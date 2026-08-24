# Template asset library

The Skill may bundle versioned template artifacts under `assets/templates/`
only when their redistribution terms permit public distribution.
Assets are files used as target inputs. Format requirements, task procedures,
and reasoning guidance belong under `references/`, not `assets/`, so Codex
loads instructions only when the current task needs them.

## Selection precedence

1. Prefer an exact official template supplied by the user for the named venue,
   year, track or article type, and submission stage.
2. Use a bundled template only when its registry identity matches every target
   field or the user explicitly selects its exact asset ID and confirms that it
   is the intended target.
3. Otherwise obtain the current exact official author artifact and record its
   provenance in the task workspace.
4. If only explicit prose requirements are available, use a reviewed
   RulePackage.
5. Without authoritative target evidence, remain check-only.

Publisher-family or venue-family recognition is never sufficient. Do not pick
the nearest template, a previous-year asset, a review template for a final
submission, or a bundled IEEE reference for an unrelated IEEE venue.

## Registry and deterministic verification

`assets/templates/index.json` is the machine-readable registry. Each entry
records:

- exact target identity and source format;
- relative artifact path and SHA-256;
- original filename and provenance;
- selection policy, distribution scope, and license-review state;
- whether an online freshness check is required;
- `derive-at-runtime` rule policy.

The registry stores no copied numeric venue rules. The DOCX engine derives
supported rules from the exact selected template on every task, preventing a
stale JSON profile from silently overriding the artifact.

Verify all bundled files before use:

```bash
skills/paperformat/scripts/resolve_template.py --verify
```

List sanitized metadata:

```bash
skills/paperformat/scripts/resolve_template.py --list
```

The public registry is deliberately empty. The repository and its published
Git history track no DOCX, DOTX, PDF, archive, manuscript, or template binary.
Synthetic DOCX regression files are generated at test time under the ignored
`tests/fixtures/generated/` directory and are never template candidates.

If a future redistributable asset is added, resolving it by ID uses:

```bash
skills/paperformat/scripts/resolve_template.py --id "exact-asset-id"
```

For a real task, automatic identity lookup requires all fields, not a partial
query:

```bash
skills/paperformat/scripts/resolve_template.py \
  --venue "Exact venue name" \
  --publisher "Exact publisher or society" \
  --year "Exact year or volume" \
  --track "Exact track or article type" \
  --stage "submission or camera-ready" \
  --template-version "Exact template version" \
  --format docx
```

Use the returned absolute `artifactPath` with the deterministic engine's
`--template` option. A resolver success identifies an immutable local file; it
does not by itself prove that the asset is still the venue's current official
template.

## Asset governance

- Never overwrite an existing asset ID or binary silently. Add a new version.
- Record the exact hash before committing or distributing an artifact.
- Keep source URL, retrieval date, venue identity, stage, and license-review
  state with the asset.
- Bundle third-party templates only when redistribution is explicitly allowed.
- If redistribution is not permitted, keep only source-routing metadata and
  acquire the artifact into the user's task workspace.
- Never add a user manuscript, user-supplied template, rendered page, export,
  or generated test fixture to the public repository or registry.
- Mark superseded assets explicitly; never make them automatic fallbacks.
- Validate a new asset with real template derivation and at least one fixture
  before describing it as a deterministic target.

The public repository deliberately contains no document or template binary.
An official artifact remains private to its task unless its owner explicitly
permits redistribution and a separate public-release review approves it.
