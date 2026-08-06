using System.Text.RegularExpressions;

namespace QLHV.Tests.Assignments;

public sealed class AssignmentSqlContractTests
{
    private static readonly string[] SourceOwnedTables =
    [
        "App_HocVien",
        "App_GiaoVien",
        "App_XeTap",
        "App_KhoaHoc",
        "App_KhoaHoc_GiaoVien",
        "App_KhoaHoc_XeTap",
    ];

    private static readonly string[] AssignmentOwnedTables =
    [
        "App_GiaoVien_hs",
        "App_KhoaHoc_NhomDaoTao",
        "App_HocVien_PhanCong",
    ];

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Assignment_repository_never_mutates_source_owned_masters()
    {
        var source = AssignmentSourceTestHelper.ReadAll(
            "server/QLHV.Infrastructure/Assignments");

        AssignmentSourceTestHelper.AssertNoSqlMutation(source, SourceOwnedTables);
        Assert.Contains("INSERT dbo.App_HocVien_PhanCong", source, StringComparison.Ordinal);
        Assert.Contains("UPDATE dbo.App_KhoaHoc_NhomDaoTao", source, StringComparison.Ordinal);
        Assert.Contains("INSERT dbo.App_GiaoVien_hs", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Course_catalog_reads_remain_available_before_assignment_schema_is_deployed()
    {
        var catalog = ReadRepository("SqlAssignmentRepository.cs");
        var detail = ReadRepository("SqlAssignmentRepository.CatalogWrites.cs");

        Assert.Contains("HasAssignmentReadSchemaAsync", catalog, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'dbo.App_HocVien_PhanCong',N'U') IS NOT NULL", catalog, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao',N'U') IS NOT NULL", catalog, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'dbo.App_GiaoVien_hs',N'U') IS NOT NULL", catalog, StringComparison.Ordinal);
        Assert.Contains("assignmentReadSchemaAvailable", catalog, StringComparison.Ordinal);
        Assert.Contains("CONVERT(int,COUNT_BIG(DISTINCT h.HocVienId)) AS UnassignedCount", catalog, StringComparison.Ordinal);
        Assert.Contains("@TrangThai=N'ACTIVE' AND COALESCE(k.TrangThaiNguon,1)=1", catalog, StringComparison.Ordinal);
        Assert.Contains("@TrangThai=N'INACTIVE' AND COALESCE(k.TrangThaiNguon,1)=0", catalog, StringComparison.Ordinal);
        Assert.Contains("@TrangThai=N'MANUAL_REVIEW'", catalog, StringComparison.Ordinal);
        Assert.Contains("@TrangThai=N'ACTIVE' AND COALESCE(g.TrangThaiNguon,1)=1", catalog, StringComparison.Ordinal);
        Assert.Contains("CONVERT(int,0) AS StudentUsageCount", catalog, StringComparison.Ordinal);
        Assert.Contains("GetCourseDetailWithoutAssignmentSchemaAsync", detail, StringComparison.Ordinal);
        Assert.Contains("LoadSourceOwnedLookupsAsync", detail, StringComparison.Ordinal);
        Assert.Contains("teachers.Select(ToLookup).ToArray()", detail, StringComparison.Ordinal);
        Assert.Contains("new CourseAssignmentSummary(learnerCount, 0, learnerCount, 0)", detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Realtime_writers_have_no_dml_path_to_assignment_owned_tables()
    {
        var realtime = AssignmentSourceTestHelper.ReadAll(
            "server/QLHV.Infrastructure/Sync/Realtime");
        var vehicleRealtime = AssignmentSourceTestHelper.ReadAll(
            "server/QLHV.Infrastructure/Sync/VehicleRealtime");

        AssignmentSourceTestHelper.AssertNoSqlMutation(
            realtime + "\n" + vehicleRealtime,
            AssignmentOwnedTables);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Preview_and_confirm_scope_every_learner_by_exact_course_profile_and_identity()
    {
        var mutation = ReadRepository("SqlAssignmentRepository.Mutations.cs");
        var import = ReadRepository("SqlAssignmentRepository.HistoryExcel.cs");

        Assert.Contains(
            "WHERE h.IsDeleted=0 AND h.MaKhoa=@MaKhoa AND h.SourceProfileCode=@SourceProfileCode",
            mutation,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE IsDeleted=0 AND MaDK=@RegistrationCode AND MaKhoa=@CourseCode",
            mutation,
            StringComparison.Ordinal);
        Assert.Contains(
            "AND SourceProfileCode=@SourceProfileCode",
            mutation,
            StringComparison.Ordinal);
        Assert.Contains(
            "learners.Length!=1 || learners[0].HocVienId!=target.HocVienId",
            mutation,
            StringComparison.Ordinal);
        Assert.Contains(
            "!learners[0].RowVersion.SequenceEqual(target.LearnerRowVersion)",
            mutation,
            StringComparison.Ordinal);

        Assert.Contains("WHERE h.IsDeleted=0 AND h.MaKhoa=@CourseCode", import, StringComparison.Ordinal);
        Assert.Contains("h.SourceProfileCode=@SourceProfileCode", import, StringComparison.Ordinal);
        Assert.DoesNotContain("(@HocVienId IS NULL OR h.HocVienId=@HocVienId)", import, StringComparison.Ordinal);
        Assert.Contains("catalog.Learners[row.RegistrationCode].Take(2)", import, StringComparison.Ordinal);
        Assert.Contains("matches.Length>1", import, StringComparison.Ordinal);
        Assert.Contains("\"AMBIGUOUS\"", import, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Continuous_new_learner_rows_are_not_a_fixed_count_guard()
    {
        var source = ReadRepository("SqlAssignmentRepository.Mutations.cs");
        var confirm = AssignmentSourceTestHelper.Section(
            source,
            "public async Task<AssignmentConfirmResult> ConfirmAssignmentPlanAsync",
            "public async Task<AssignmentConfirmResult> ConfirmGroupDefaultsPlanAsync");

        Assert.Contains("plan.Targets.Where(target => target.Status == Ready)", confirm, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)SELECT\s+COUNT", confirm);
        Assert.DoesNotContain("LearnerCount", confirm, StringComparison.Ordinal);
        Assert.Contains("TOP (@Limit)", source, StringComparison.Ordinal);
        Assert.Contains("ORDER BY h.HocVienId", source, StringComparison.Ordinal);
        Assert.Contains("MaxImportRows+1", Compact(source), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Confirm_is_serializable_revalidates_rowversions_and_rolls_back_as_one_unit()
    {
        var mutation = ReadRepository("SqlAssignmentRepository.Mutations.cs");
        var import = ReadRepository("SqlAssignmentRepository.HistoryExcel.cs");
        var setBased = ReadRepository("SqlAssignmentRepository.SetBased.cs");

        foreach (var source in new[] { mutation, import })
        {
            Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
            Assert.Contains("transaction.CommitAsync", source, StringComparison.Ordinal);
            Assert.Contains("transaction.RollbackAsync(CancellationToken.None)", source, StringComparison.Ordinal);
        }

        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)", mutation, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)", setBased, StringComparison.Ordinal);
        Assert.Contains("RowVersion=@RowVersion", mutation, StringComparison.Ordinal);
        Assert.Contains("AssertExactlyOne(closed", mutation, StringComparison.Ordinal);
        Assert.Contains("AssertExactlyOne(inserted", mutation, StringComparison.Ordinal);
        Assert.Contains("EnsureConfirmable(plan.Targets)", mutation, StringComparison.Ordinal);
        Assert.Contains("không ghi dòng nào", import, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Snapshot_change_closes_current_and_inserts_full_new_state_without_overwrite_or_empty_row()
    {
        var source = ReadRepository("SqlAssignmentRepository.Mutations.cs");
        var apply = AssignmentSourceTestHelper.Section(
            source,
            "private async Task ApplyTargetAsync",
            "private async Task<CourseIdentityRow> LoadCourseIdentityLockedAsync");

        Assert.Contains("SET IsCurrent=0,NgayHetHieuLuc=@EffectiveAt", apply, StringComparison.Ordinal);
        Assert.Contains("AND IsCurrent=1 AND RowVersion=@RowVersion", apply, StringComparison.Ordinal);
        Assert.Contains("target.After is not null && target.After.HasAnyValue", apply, StringComparison.Ordinal);
        Assert.Contains("INSERT dbo.App_HocVien_PhanCong", apply, StringComparison.Ordinal);
        Assert.Contains("@GroupId,@DossierReceiverId,@ClassTeacherId,@TrainingVehicleId", apply, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)DELETE\s+FROM\s+dbo\.App_HocVien_PhanCong", apply);

        var closeIndex = apply.IndexOf("SET IsCurrent=0", StringComparison.Ordinal);
        var insertIndex = apply.IndexOf("INSERT dbo.App_HocVien_PhanCong", StringComparison.Ordinal);
        Assert.True(closeIndex >= 0 && insertIndex > closeIndex);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Group_defaults_support_all_modes_and_preserve_or_replace_overrides_explicitly()
    {
        var source = ReadRepository("SqlAssignmentRepository.Mutations.cs");

        Assert.Contains("GroupPropagationMode.UnoverriddenOnly", source, StringComparison.Ordinal);
        Assert.Contains("GroupPropagationMode.ReplaceAll", source, StringComparison.Ordinal);
        Assert.Contains("GroupPropagationMode.NoCurrentChange", source, StringComparison.Ordinal);
        Assert.Contains("before.OverrideClassTeacher", source, StringComparison.Ordinal);
        Assert.Contains("before.OverrideTrainingVehicle", source, StringComparison.Ordinal);
        Assert.Contains("before.OverrideFigure10Vehicle", source, StringComparison.Ordinal);
        Assert.Contains("OverrideClassTeacher = false", source, StringComparison.Ordinal);
        Assert.Contains("OverrideTrainingVehicle = false", source, StringComparison.Ordinal);
        Assert.Contains("OverrideFigure10Vehicle = false", source, StringComparison.Ordinal);
        Assert.Contains("mode != GroupPropagationMode.NoCurrentChange", source, StringComparison.Ordinal);
        Assert.Contains("INHERIT", source, StringComparison.Ordinal);
        Assert.Contains("group is null", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_is_all_or_nothing_idempotent_and_does_not_create_master_data()
    {
        var source = ReadRepository("SqlAssignmentRepository.HistoryExcel.cs");
        var constants = ReadRepository("SqlAssignmentRepository.cs");

        foreach (var status in new[] { "READY", "NO_CHANGE", "INVALID", "CONFLICT" })
        {
            Assert.Contains($"= \"{status}\"", constants, StringComparison.Ordinal);
        }
        foreach (var status in new[] { "NOT_FOUND", "AMBIGUOUS", "INACTIVE_REFERENCE" })
        {
            Assert.Contains(status, source, StringComparison.Ordinal);
        }

        Assert.Contains("AcquireAssignmentOperationLockAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryReplaySealedImportConfirmAsync", source, StringComparison.Ordinal);
        Assert.Contains("ComputeImportPayloadSha256", source, StringComparison.Ordinal);
        Assert.Contains("WriteAssignmentOperationLedgerAsync", source, StringComparison.Ordinal);
        Assert.Contains("FileSha256", source, StringComparison.Ordinal);
        Assert.Contains("SuggestedCourseCode", source, StringComparison.Ordinal);
        Assert.Contains("row.Status is not (Ready or NoChange)", source, StringComparison.Ordinal);
        Assert.Contains("ResolveImportCode", source, StringComparison.Ordinal);
        Assert.Contains("LoadImportResolutionCatalogAsync", source, StringComparison.Ordinal);
        Assert.Contains("INACTIVE_REFERENCE", source, StringComparison.Ordinal);
        AssignmentSourceTestHelper.AssertNoSqlMutation(
            source,
            "App_GiaoVien",
            "App_XeTap",
            "App_KhoaHoc",
            "App_HocVien");
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Confirm_idempotency_is_global_persistent_payload_scoped_and_transactional()
    {
        var ledger = ReadRepository("SqlAssignmentRepository.Idempotency.cs");
        var mutation = ReadRepository("SqlAssignmentRepository.Mutations.cs");
        var import = ReadRepository("SqlAssignmentRepository.HistoryExcel.cs");

        Assert.Contains("FROM dbo.App_AssignmentOperation", ledger, StringComparison.Ordinal);
        Assert.Contains("INSERT dbo.App_AssignmentOperation", ledger, StringComparison.Ordinal);
        Assert.Contains("sp_getapplock", ledger, StringComparison.Ordinal);
        Assert.Contains("@LockOwner=N'Transaction'", ledger, StringComparison.Ordinal);
        Assert.Contains("[\"IDEMPOTENCY_KEY_V1\", idempotencyKey]", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("[actor,idempotencyKey]", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("(IdempotencyKey,OperationType", ledger, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKeySha256", ledger, StringComparison.Ordinal);
        Assert.Contains("PayloadSha256", ledger, StringComparison.Ordinal);
        Assert.Contains("PreviewTokenSha256", ledger, StringComparison.Ordinal);
        Assert.Contains("RetainUntilUtc", ledger, StringComparison.Ordinal);
        Assert.Contains("OperationType", ledger, StringComparison.Ordinal);
        Assert.Contains("SourceProfileCode", ledger, StringComparison.Ordinal);
        Assert.Contains("row.ScopeId != scopeId", ledger, StringComparison.Ordinal);
        Assert.Contains("row.Actor, actor", ledger, StringComparison.Ordinal);
        Assert.Contains("TryReplaySealedAssignmentConfirmAsync", mutation, StringComparison.Ordinal);
        Assert.Contains("TryReplaySealedImportConfirmAsync", import, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)WriteAssignmentOperationLedgerAsync\(.*?CommitAsync",
            mutation);
        Assert.Matches(
            @"(?s)WriteAssignmentOperationLedgerAsync\(.*?CommitAsync",
            import);
        Assert.Matches(
            @"(?s)catch\s*\{.*?RollbackAsync",
            mutation);
        Assert.Matches(
            @"(?s)catch\s*\{.*?RollbackAsync",
            import);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Confirm_revalidates_genuine_no_change_rows_before_any_mixed_plan_commits()
    {
        var mutation = ReadRepository("SqlAssignmentRepository.Mutations.cs");
        var import = ReadRepository("SqlAssignmentRepository.HistoryExcel.cs");
        var setBased = ReadRepository("SqlAssignmentRepository.SetBased.cs");

        Assert.True(
            Regex.Matches(
                mutation,
                @"LockAndValidateTargetsAsync\(connection,transaction,course,plan\.Targets",
                RegexOptions.CultureInvariant).Count >= 2);
        Assert.Contains("target.Status is Ready or NoChange", mutation,
            StringComparison.Ordinal);
        Assert.Contains("LockAndValidateTargetAsync(connection,transaction,course,target", mutation,
            StringComparison.Ordinal);
        Assert.Contains("StageAndValidateImportTargetsSetBasedAsync", import,
            StringComparison.Ordinal);
        Assert.Contains("foreach(var row in plan.Rows)", setBased,
            StringComparison.Ordinal);
        Assert.Contains("row.Status is not (Ready or NoChange)", setBased,
            StringComparison.Ordinal);
        Assert.Contains("WHERE t.Status='READY'", setBased,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Group_set_copies_defaults_and_later_snapshots_rebase_only_inherited_fields()
    {
        var mutation = ReadRepository("SqlAssignmentRepository.Mutations.cs");
        var import = ReadRepository("SqlAssignmentRepository.HistoryExcel.cs");

        Assert.Contains("ClassTeacherId=selectedGroup.GiaoVienDungLopId", mutation, StringComparison.Ordinal);
        Assert.Contains("TrainingVehicleId=selectedGroup.XeTapId", mutation, StringComparison.Ordinal);
        Assert.Contains("Figure10VehicleId=selectedGroup.XeBaiSo10Id", mutation, StringComparison.Ordinal);
        Assert.Contains("ClassTeacherId=group.GiaoVienDungLopId", import, StringComparison.Ordinal);
        Assert.Contains("TrainingVehicleId=group.XeTapId", import, StringComparison.Ordinal);
        Assert.Contains("Figure10VehicleId=group.XeBaiSo10Id", import, StringComparison.Ordinal);
        Assert.Contains("state=RebaseInheritedFields(state,selectedGroup)", mutation, StringComparison.Ordinal);
        Assert.Contains("state.OverrideClassTeacher ? state.ClassTeacherId : group.GiaoVienDungLopId", mutation,
            StringComparison.Ordinal);
        Assert.Contains("else state=RebaseInheritedFields(state,group)", import, StringComparison.Ordinal);
        Assert.Contains("OverrideClassTeacher=false", import, StringComparison.Ordinal);
        Assert.Contains("OverrideTrainingVehicle=false", import, StringComparison.Ordinal);
        Assert.Contains("OverrideFigure10Vehicle=false", import, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Vehicle_selection_requires_both_active_lifecycle_and_active_source_status()
    {
        var catalog = ReadRepository("SqlAssignmentRepository.CatalogWrites.cs");
        var mutation = ReadRepository("SqlAssignmentRepository.Mutations.cs");
        var import = ReadRepository("SqlAssignmentRepository.HistoryExcel.cs");

        Assert.Contains("SourceLifecycle='ACTIVE' AND COALESCE(SourceTrangThai,1)=1", catalog,
            StringComparison.Ordinal);
        Assert.True(Regex.Matches(mutation, @"COALESCE\(x\.SourceTrangThai,1\)=1").Count >= 2);
        Assert.Contains("SourceLifecycle='ACTIVE' AND COALESCE(SourceTrangThai,1)=1", import,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Migration_denies_realtime_dml_and_enforces_one_current_snapshot_and_no_action_fks()
    {
        var sql = AssignmentSourceTestHelper.Read(
            "database",
            "patches",
            "20260730_add_integrated_course_assignment.sql");

        Assert.Contains("UX_App_HVPC_OneCurrentPerHocVien", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE IsCurrent = 1", sql, StringComparison.Ordinal);
        Assert.Equal(
            Regex.Matches(sql, @"(?i)\bFOREIGN\s+KEY\s*\(").Count,
            Regex.Matches(sql, @"(?i)\bON\s+DELETE\s+NO\s+ACTION\b").Count);
        foreach (var table in AssignmentOwnedTables)
        {
            Assert.Contains($"ON dbo.{table}", sql, StringComparison.Ordinal);
        }
        Assert.Contains("DENY INSERT, UPDATE, DELETE", sql, StringComparison.Ordinal);
        Assert.Contains("NT SERVICE\\QLHV_APP_RealtimeWorker", sql, StringComparison.Ordinal);
    }

    private static string ReadRepository(string fileName) => AssignmentSourceTestHelper.Read(
        "server", "QLHV.Infrastructure", "Assignments", fileName);

    private static string Compact(string value) => Regex.Replace(value, @"\s+", string.Empty);
}
