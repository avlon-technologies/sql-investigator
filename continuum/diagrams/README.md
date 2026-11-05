# SQL Investigator architecture diagrams

These diagrams are authored, editable Mermaid sources, based on the current compiled ADL and documented implementation choices. They are **not generated Continuum Atlas artifacts**. The Continuum Atlas C2 diagram was rendered and checked separately (run `1ea2505a-3371-47f1-84f2-2a79d0a5f4e4`, model attestation `sha256:7860e8c92732754ac81e7e642af82c297fc06571396eacf2df5ebf1988c88218`), but its rendered files have not been committed here.

The C2 Atlas diagram passed mechanical geometry and semantic-fidelity checks, with two **inferred** integration edges. The C3 Atlas request failed. Neither result proves the current ADL is complete. Dashed arrows below denote intended or described integrations, not declared ADL relationship edges. Since attestation `sha256:cc76d530…` the ADL declares `deployedAs`, `reads` and `writes`, so service allocation in `02-local-runtime.md` is declared; the Atlas run above predates it.

- [System context](01-system-context.md)
- [Local runtime and components](02-local-runtime.md)
- [Investigation sequence](03-investigation-sequence.md)
- [Credential and data boundaries](04-security-boundaries.md)

Do not claim full architectural conformance until the semantic ADL, bindings and package check are complete.
