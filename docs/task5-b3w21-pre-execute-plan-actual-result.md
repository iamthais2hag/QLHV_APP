# Task 5 B3W21 - HocVien pre-execute plan actual result

## Scope

Endpoint read-only:

```http
GET /api/dong-bo-v2/hoc-vien/pre-execute-plan
```

Safety status:

- Does not write `dbo.App_HocVien`.
- Does not write `dbo.App_DongBoLog`.
- Does not call `/execute`.
- Does not require `EnableTargetWrites=true`.
- Must only be used against local/test databases before any execute decision.

## B3W20 actual result

Actual read-only result from the current DATA_V2 test state:

| Field | Value |
| --- | --- |
| `isDryRun` | `true` |
| `sourceProfileCode` | `DATA_V2` |
| `sourceSystem` | `V2` |
| `sourceRowsRead` | `1970` |
| `plannedInsert` | `0` |
| `plannedUpdate` | `0` |
| `plannedSkip` | `1970` |
| `errors` | `[]` |
| `warningCount` | `1` |

Warning recorded:

- `MaDK 66016-20260513-115000413` has a `SoCCCD` length warning and needs manual data review. The sync must not pad or auto-correct this value.

## Readability improvement

Preview items now keep the existing numeric `action` value and also expose `actionName` for manual review.

Example shape:

```json
{
  "maDK": "66016-20260513-115000413",
  "action": 3,
  "actionName": "Skip",
  "warnings": []
}
```

Current action mapping:

| Numeric value | `actionName` | Meaning |
| --- | --- | --- |
| `1` | `Insert` | Source identity is not present in target. |
| `2` | `Update` | Source identity exists, but the row hash differs. |
| `3` | `Skip` | Source identity exists and the row hash matches, or the row is intentionally skipped by planning rules. |

## Conclusion

For the current `DATA_V2` state, the pre-execute plan says no write is needed:

- `plannedInsert = 0`
- `plannedUpdate = 0`
- `plannedSkip = 1970`

This means QLHV_APP_TEST is already aligned with the current DATA_V2 source rows by the multi-source identity rule `SourceProfileCode + SourceMaDK`.

Do not execute sync based only on this note. Before any future execute test, re-run the full read-only checklist and stop if:

- `errors` is not empty.
- `sourceRowsRead` changes unexpectedly.
- `plannedInsert` or `plannedUpdate` is not expected.
- warning count or warning type changes without review.
- `EnableTargetWrites` is still false or `Sync:DryRun` is true.

