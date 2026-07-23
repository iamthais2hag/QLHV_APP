# QLHV Auto Sync and photo deployment checklist

Complete the following 17 steps in order. Record the operator, UTC/local time,
evidence, and result for each step. Installation and launch scripts do not
apply SQL patches.

1. **Backup QLHV_APP.** Create and verify a restorable full backup of the
   `QLHV_APP` database before changing schema or runtime files. Record the
   backup path and restore-verification result.

2. **Backup Production Local config.** Make a protected copy of
   `D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json`. Confirm the
   copy contains the current connection settings and secrets, restrict access,
   and do not add it to Git.

3. **Review all three SQL patches.** Review, approve, and checksum these exact
   files: `20260723_add_app_data_version.sql`,
   `20260723_add_qlhv_auto_sync.sql`, and
   `20260723_add_hocvien_photo_processing.sql`. Confirm they target only
   `QLHV_APP`, are transactional/idempotent, and contain no source-database
   backup/restore or full-sync execution.

4. **Apply patches to QLHV_APP.** In a separately authorized maintenance
   action, apply the three reviewed patches in the order listed above. Verify
   tables, constraints, indexes, and trusted/enabled state with read-only
   checks. Stop on the first error.

5. **Publish Release.** From the reviewed source commit, run the documented
   Release publish. Record the commit, SDK, package-lock state, build/test
   result, and publish artifact checksum.

6. **Deploy runtime.** Use the reviewed installer/updater to deploy the
   published application to `D:\QLHV_APP_RUNTIME\app`. Verify the runtime
   executable, file manifest, version endpoint, and that one update does not
   leave mixed old/new binaries.

7. **Check Production Local.** Verify the runtime loads
   `D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json` after the
   base Production configuration and before environment/command-line
   overrides. Confirm `Sync:DryRun=false`,
   `SyncExecution:EnableTargetWrites=true`, Auto Sync is enabled in OTO→MOTO
   order, and both photo flags remain `false`. Confirm existing connection
   strings/secrets were preserved and a protected pre-edit backup exists.

8. **Check launcher and shortcut.** Verify the Desktop shortcut target,
   working directory, runtime executable, port 8088, and launcher version.
   Confirm an already-running healthy server keeps the same PID and a stopped
   server starts exactly one process.

9. **Check filesystem permissions.** For the runtime service/user identity,
   confirm `D:\IM_GPLX` is read-only; `D:\QLHV_APP\IM_GPLX` is writable for
   derived photos but cannot overwrite the source root; and
   `D:\QLHV_APP_RUNTIME\logs` is writable. Confirm model/config files are
   readable only by appropriate identities and secrets are not exposed.

10. **Open the Desktop icon.** Start one user session from the installed icon.
    Do not manually restart ASP.NET Core. Record whether the launcher reused
    or started the server and the observed PID.

11. **Observe the launcher sequence.** Confirm it first performs
    `GET session-start-sync/status`, evaluates `NeedSync`, joins an existing
    compatible run or starts one session operation only when required, then
    displays OTO refresh/sync followed by MOTO refresh/sync, waits for the
    exact run to reach a terminal state, and finally opens/focuses the browser.
    A failed operation must still open the app and display the failing source,
    error, and last successful data time.

12. **Check DataVersion.** Confirm version values increase only after the
    corresponding database transaction commits. Verify the browser refetches
    status and business data after completion without F5 and does not serve
    stale cached business responses.

13. **Compare Live, BAK, and QLHV_APP counts.** For OTO and MOTO separately,
    compare reviewed counts/snapshot tokens for Live→BAK and BAK→application.
    Confirm `NeedSync=false` only after the expected tokens and partition
    counts agree.

14. **Check partition isolation.** Prove an OTO refresh/full sync changes only
    `CSDT_OTO` application rows and an MOTO refresh/full sync changes only
    `CSDT_MOTO` rows. Check insert, update, reactivate, soft-delete, rerun
    idempotency, duplicate guards, durable active slot, transaction, and
    `sp_getapplock`; confirm there is no physical delete or x2/x3 duplication.

15. **Check card printing.** With photo processing still disabled, confirm
    cards continue to use the original-photo fallback. In a non-production
    approved fixture test, confirm derived photos are printable only when
    `APPROVED`, or `SUCCEEDED` above threshold with no review flag; missing,
    failed, and `REVIEW_REQUIRED` output must not replace the original.

16. **Enable PhotoProcessing only after model and benchmark approval.** Follow
    `qlhv-photo-model-and-benchmark.md`; install the reviewed model and license
    manifest outside Git, verify both SHA-256 values and `READY`, run the real
    read-only-source benchmark, complete human visual review, and approve
    latency/memory/confidence results. Pilot with
    `AutoProcessAfterSync=false`; do not enable automatic processing until the
    pilot passes.

17. **Use the rollback procedure if deployment fails.** Stop new session
    operations; let/mark the active operation reach a known terminal state;
    set both photo flags to `false`; restore the protected pre-edit Production
    Local config and the prior atomic runtime release; restart only as an
    authorized rollback action; verify health/read-only status and data
    versions. If schema/data rollback is required, use the approved database
    backup/restore plan—do not improvise reverse SQL or refresh/sync from an
    unverified BAK. Record the failure, last successful sync, restored versions,
    and validation evidence before reopening access.
