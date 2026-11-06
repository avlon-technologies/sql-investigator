# Fixture manifest (Phase D1)

Two disposable SQL Server databases with **deterministic synthetic data**: no real customer or production data, and every email under `example.test`. They ran in the PC's throwaway container `sqlinvestigator-mssql` (SQL Server 2022, `127.0.0.1,1433`). Each database has its own least-privilege login, and neither login is mapped in the other database.

| Database | Tables | Login | Rights |
|---|---|---|---|
| `EvalCommerce` | `Customers` (31), `Orders` (120), `OrderLines`, `Payments` (119) | `eval_commerce_ro` | SELECT on those four tables only |
| `EvalFulfillment` | `ExternalOrderReferences` (119), `Shipments` (118), `ShipmentEvents` (463), `Returns` (3) | `eval_fulfillment_ro` | SELECT on those four tables only |

The passwords were random, generated at seeding time, passed to `sqlcmd` as variables, and never committed.

## Files

- [`evidence/fixture-seed.sql`](evidence/fixture-seed.sql): creates both databases, the data and the logins (`$(COMMERCE_PW)`, `$(FULFILLMENT_PW)` are sqlcmd variables).
- [`evidence/fixture-truth.sql`](evidence/fixture-truth.sql): derives the truth from the data, run as sa. Its cross-database join is the grader's, not the product's.
- [`evidence/fixture-truth-output.txt`](evidence/fixture-truth-output.txt): its output.
- [`evidence/TRUTH-AND-RUBRIC.md`](evidence/TRUTH-AND-RUBRIC.md): the answer key and the scoring rubric.

## Kept apart from the evaluator

All four files lived in the implementation session's scratchpad. The evaluator's read access to it was denied, and the transcript audit shows no access. They were committed only **after** the evaluator submitted `REPORT.md`.

They were hash-locked at 2026-11-06T11:37:10Z, before the evaluator was launched:

```
c8cc9146f4a7e06dcda5c6a34b511bbaa361900cd3f0882809d31f2be6dd1176  TRUTH-AND-RUBRIC.md
509bfa68d07e9f825db391b367db7187f5f8280994ab7f8e60cd84365a704587  seed.sql
3057e638c3f9757fcc6cf838ac083252277bd0f006ee6a82dfc91431766bd853  truth.sql
be5fac247afc837b53314198d431e8bedac27125fbd13040a66caeebb7734314  truth-output.txt
```

`sha256sum` of the committed copies in `evidence/` gives the same hashes, so the rubric was not changed after the result was seen.

## Seeded situations (summary; the full list is in TRUTH-AND-RUBRIC.md)

| Kind | Count | Examples |
|---|---|---|
| Orders without shipments, though commerce says shipped/delivered or paid and overdue | 6 | SO-100012, SO-100033, SO-100058; SO-100041, SO-100077, SO-100096 |
| Shipments with no matching order | 2 commerce, plus 3 marketplace | SO-100990, SO-100991; MP-55001..3 |
| Duplicate references | 3 double-fulfilled, 1 duplicate row | SO-100021/064/089; SO-100102 |
| Delays | 5 shipped late, 2 stuck in transit | SO-100008/036/070/083/111; SO-100047/092 |
| Incomplete payments on fulfilled orders | 4 | SO-100014/039/066/099 |
| Return not reflected in commerce | 1 | SO-100054 |
| Ambiguous identifiers | 3 | truncated `SO-10006`; NULL reference with a shared email; a duplicate customer |
| Traps (look wrong, are fine) | 6 groups | cancelled orders; paid within the promise; normalizable reference formats; this week's in-transit orders; a replacement after a damaged return; a refunded return |
