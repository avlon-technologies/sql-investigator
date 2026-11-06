# IMPLEMENTATION DELTA — avlon.sqlinvestigator

> **Derived by `continuum icp materialize`. Never authority: the `.adl-change`, the binding delta and the acceptance delta ARE the transition; if this file disagrees with them it is regenerated.**

| | |
|---|---|
| transitionId | `sha256:5b00b33401cea2fa973f70ea95c6a46aeea4888919b587b6c7260a5007525fbf` |
| base | changeState `sha256:def71c2de556c87d6fcde76ced19b541be633f83f7a19d97098a90742d156a3a` |
| result | changeState `sha256:ff4a0287e6407cf45d4b81383093fd7bbeb122aa1b7c0eff88acdea2320b0031`, attestation `sha256:28a88f436b9ce5349783975e4a495199b25cebcd317d764ba6ce299d808c4005` |
| delta | semantic changed · bindings changed · acceptance changed |

## What the implementation must change

- **Semantic:** apply the architecture in the materialized package (`result/IMPLEMENTATION.md`); the change `changes/macos-support.adl-change` names exactly which elements were added, modified, moved or removed — nothing else changed.
- **Bindings:** `bindings/avlon.bindings.localdotnet.vocabulary-claims.json`, `bindings/local-development.adl-binding.json` replace the same-named documents of the base; every other binding is carried over unchanged (re-pinned to the result attestation).
- **Acceptance:** added none; changed `b01-connection-provisioning`; removed none. The COMPLETE resulting suite (12 scenarios) is the contract, not only the delta.

Work from the materialized package, not from this file. Done means the complete suite passes on your adapter with `MODEL_PROVIDER=fake`.
