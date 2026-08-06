# RT03 unsupported-drift recovery

## Code/contract resolution

- Photo-only mapped differences are now `MULTI_FIELD_PHOTO_DRIFT` / `PHOTO_MANUAL_REVIEW`, never update-eligible and never learner mutations.
- A CT event classifier distinguishes historical photo masks, no-mapped-change `TT_XuLy` events, and unknown unsafe events.
- The cycle processor reads the exact next CT batch, seals one CT version, revalidates identity/target state, writes an auditable manual-review marker for the photo event, and advances the checkpoint only after the control-plane commit and verification.
- Source delete, target-only, conflict, unexpected masks, and unsafe ownership remain fail-closed.
- Concurrent learner entry is treated as `RT03_SOURCE_CHANGED_DURING_PLAN`: bounded retry, no checkpoint mutation and no durable worker block. Counts and fingerprints are never assumed permanent.

## Controlled production recovery

Auto Sync was first disabled in effective production configuration (`Enabled=false`, `RunOnServerStartup=false`, polling OFF). The API was restarted and readiness proved no polling activity. The reviewed worker package was then deployed and its Windows service started under `NT SERVICE\QLHV_APP_RealtimeWorker`.

OTO replayed CT versions `18` through `25` in order. Each event produced one manual-review/control-plane record with `TargetRetainedActive=1`, `TargetMutated=0`, `InsertedRows=0`, and `UpdatedRows=0`. There are eight recovery manual-review rows. OTO checkpoint progressed from `17` to `25`; MOTO remained `0`. No checkpoint was edited manually.

The recovery made `0` learner-data mutations. The eight earlier target photo updates were historical Auto Sync activity that occurred before Auto Sync was disabled; they were not repeated or synthesized.

## Verification

- Focused incident/state/UI tests: `64` passed.
- Broad backend regression after the final SCM/UI correction: `1235` passed, `2` explicit opt-in tests skipped (`1237` total).
- SCM process-state regression tests: `6` passed.
- Final production read-only probe: `1` passed with three stable samples per profile.
- Client lint and production build: PASS.
- Release publish/build: PASS, with the pre-existing `NU1902` Magick.NET advisory warning recorded.

After replay, the worker repeatedly returned `HEALTHY_NO_CHANGE`, durable state became `HEALTHY`, cycle active became `0`, error code cleared, and the mutex remained owned by realtime.
