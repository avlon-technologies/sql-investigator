# SQL Investigator architecture diagrams

There are two kinds of diagram here. Keep them apart.

## Generated semantic views (`atlas/`)

Continuum Atlas rendered these from the compiled model. Every run is pinned to the same imported snapshot `snp_f809011046d39cc36ccf8c90206f05a6`:

- attestation `sha256:fb4565be760be1bbc9cca34f73d6fe88cc3bb326c1a0bd26138f9f083328237b`
- changeState `sha256:579b1036…e48e4`
- core `adl/1.6`

The SVGs are editable reconstructions. The PNGs are their raster previews.

**The model has moved on since these renders.** The current state is attestation `sha256:df885cefc7aba2905bf00c676755cd0ec8b3ae6170da605b600ee44c2c7d3a77` (changeState `sha256:def71c2d…6a3a`). `continuum compile <previous> --diff <current>` returns a verified change of exactly three `modify description` operations: ConnectionCatalog, ReadOnly and SelectOnlyAllowlist each gained the approved object/column scope wording. No element or relationship was added, removed or re-pointed, so nothing drawn here changed. Re-render the views after the next structural change.

| File | Run | Kind | Fidelity check |
|---|---|---|---|
| `c1-context.svg/.png` | `6fa8443b-b0d6-4a2f-930c-410d075830f9` | deterministic C1 projection | 7/7 declared; geometry PASS; semantic fidelity SUPPORTED |
| `c2-containers.svg/.png` | `8ddc19b4-1e2d-4245-aadd-718424278592` (render v2) | deterministic C2 projection | 7 declared + 1 graded **speculative** (LocalMCPServer → SQLServer, drawn dotted, because the edge is lifted from the services onto their deployable); PASS; SUPPORTED |
| `c3-contexts.svg/.png` | `3e5e6d73-1bfd-4060-ab86-b4c43cf95519` | deterministic C3 projection at bounded-context altitude | 9/9 declared; PASS; SUPPORTED |
| `security-boundaries.svg/.png` | `bd193d43-0ae1-441a-a804-7b83063711d2` | composed security/trust view | 29 declared + 2 **inferred** (the dashed provisioning-console routes); geometry PASS; SUPPORTED |

There is only one bounded context (`QueryExecution`), so C3 shows the context, not the three services inside it. The editorial `02-local-runtime.md` gives the service-level detail.

A composed C3 attempt (run `873e3879-d496-41d0-abf6-3ce526d6beb5`) was semantically SUPPORTED, but its geometry check failed: one edge was drawn twice. It is not committed.

None of these checks is a visual acceptance or evidence of implementation. They show only that nothing is drawn stronger than the model declares.

## Editorial diagrams (Mermaid, hand-authored)

These are readable supplements. Each one says which of its arrows are declared in `model/architecture.adl` and which are not. Where they disagree with the model or the Atlas views, the model wins.

- [System context](01-system-context.md)
- [Local runtime and service allocation](02-local-runtime.md)
- [Investigation sequence (illustrative order, not declared)](03-investigation-sequence.md)
- [Credential and data boundaries](04-security-boundaries.md)

## Regenerating

Import `model/architecture.adl-manifest.json` and `model/architecture.adl` through the Continuum Diagram MCP (`diagram_model_import`), then use `diagram_project` with view `c1`, `c2` or `c3`, and `diagram_request` for composed views. When the attestation changes, the views above are stale until they are re-rendered.
