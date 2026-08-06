# RT03_UNSUPPORTED_DRIFT root cause

## Exact blocking event

The first blocking event was OTO CT version `18`, operation `U`, table `dbo.NguoiLX_HoSo`, after checkpoint `17`. The changed-column mask contained `TT_XuLy`, `DuongDanAnh`, `ChatLuongAnh`, `NgayThuNhanAnh`, and `NguoiThuNhanAnh`. The privacy-safe event identity was `91963407834E9CD9`. The source row still existed, the mapped target row existed and was active, and no delete, target-only state, duplicate, or ownership conflict was present.

Versions `19` through `25` were seven more distinct updates with the same historical photo-update shape. Their masked identities were `71E045F53F246ADA`, `50923BB9C1C80E52`, `757C9007E5E5BAA3`, `A9B60EBBB8DA713E`, `CD24D00ED9F07117`, `E03341BFB6B5D871`, and `9AD6559CB6FA052E`.

## Classification defect

`Rt01aDriftClassifier` treated differences in `AnhRelativePath`, `ChatLuongAnh`, and `NgayThuNhanAnh` as ordinary stale imported values and made them appear writable. `Rt03ProductionRealtimeCycleProcessor` allowed only a source-only insert or its exact supported update shape, so the photo shape fell through to `RT03_UNSUPPORTED_DRIFT`.

The worker blocked before building/applying a learner plan, before its transaction, and before checkpoint publication. Realtime therefore produced `0` learner mutations and left OTO checkpoint at `17` at the moment of the block.

## Production history correction

The incident audit disproved the prior assumption that Auto Sync was already OFF. The old production API had `Enabled=true` and `RunOnServerStartup=true`; after realtime blocked, old Auto Sync runs propagated the source photo values to the target. That history explains why the source and target rows later compared equal. It is separate from the realtime recovery and was preserved, not rewritten.

The exact incident classification is `MULTI_FIELD_PHOTO_DRIFT`, not a supported learner update and not a source delete, ownership conflict, schema drift, or CT-window loss.
