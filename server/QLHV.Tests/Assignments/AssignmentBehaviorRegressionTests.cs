using System.Reflection;
using QLHV.Application.Assignments;
using QLHV.Infrastructure.Assignments;

namespace QLHV.Tests.Assignments;

public sealed class AssignmentBehaviorRegressionTests
{
    [Theory]
    [InlineData("IDS", 1, AssignmentOperation.StudentOverride, false)]
    [InlineData("IDS", 2, AssignmentOperation.StudentOverride, true)]
    [InlineData("FILTER", 0, AssignmentOperation.StudentOverride, true)]
    [InlineData("IDS", 1, AssignmentOperation.BulkAssign, true)]
    [InlineData("IDS", 1, AssignmentOperation.PutInGroup, true)]
    [Trait("Category", "AssignmentFocused")]
    public void Bulk_permission_requirement_is_derived_from_the_sealed_request(
        string selectionMode,
        int learnerCount,
        string operation,
        bool expected)
    {
        var request = new AssignmentPreviewRequest
        {
            Selection = new AssignmentSelectionRequest
            {
                Mode = selectionMode,
                HocVienIds = Enumerable.Range(1, learnerCount).Select(value => (long)value).ToArray(),
            },
            Operation = operation,
        };

        Assert.Equal(expected, AssignmentRules.RequiresBulkPermission(request));
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public async Task Bulk_confirm_rechecks_permission_from_sealed_plan()
    {
        var repository = DispatchProxy.Create<IAssignmentRepository, RepositoryProxy>();
        var proxy = (RepositoryProxy)(object)repository;
        var store = new AssignmentPreviewStore();
        var plan = new AssignmentMutationPlan(
            42,
            "OTO-K001",
            "CSDT_OTO",
            [1, 2, 3, 4, 5, 6, 7, 8],
            AssignmentOperation.BulkAssign,
            "BULK",
            RequiresBulkPermission: true,
            Targets: [],
            Warnings: []);
        var (token, _) = store.Put("ASSIGNMENT", "operator-a", plan);
        var service = new AssignmentService(repository, store);
        var request = new ConfirmPreviewRequest
        {
            PreviewToken = token,
            IdempotencyKey = "bulk-confirm-001",
            Reason = "Xác nhận kiểm thử",
        };

        var denied = await Assert.ThrowsAsync<AssignmentDomainException>(() =>
            service.ConfirmAssignmentAsync(request, "operator-a", canBulkAssign: false, CancellationToken.None));

        Assert.Equal("FORBIDDEN", denied.Code);
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal(0, proxy.ConfirmCalls);

        var confirmed = await service.ConfirmAssignmentAsync(
            request,
            "operator-a",
            canBulkAssign: true,
            CancellationToken.None);
        Assert.Equal("confirmed", confirmed.OperationId);
        Assert.Equal(1, proxy.ConfirmCalls);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public async Task Import_confirm_replays_same_logical_plan_but_rejects_cross_profile_key_reuse()
    {
        var repository = DispatchProxy.Create<IAssignmentRepository, RepositoryProxy>();
        var proxy = (RepositoryProxy)(object)repository;
        var store = new AssignmentPreviewStore();
        var plan = new AssignmentImportPlan(
            42,
            "OTO-K001",
            "CSDT_OTO",
            [1, 2, 3, 4, 5, 6, 7, 8],
            "assignment.xlsx",
            new string('A', 64),
            []);
        var (token, _) = store.Put("IMPORT", "operator-a", plan);
        var service = new AssignmentService(repository, store);
        var request = new ConfirmPreviewRequest
        {
            PreviewToken = token,
            IdempotencyKey = "import-confirm-001",
            Reason = "Import confirmation regression test",
        };

        var first = await service.ConfirmImportAsync(42, request, "operator-a", CancellationToken.None);
        var sameTokenRetry = await service.ConfirmImportAsync(42, request, "operator-a", CancellationToken.None);

        var (equivalentToken, _) = store.Put("IMPORT", "operator-a", plan with { });
        var equivalentRetry = await service.ConfirmImportAsync(
            42,
            new ConfirmPreviewRequest
            {
                PreviewToken = equivalentToken,
                IdempotencyKey = request.IdempotencyKey,
                Reason = request.Reason,
            },
            "operator-a",
            CancellationToken.None);

        Assert.Equal(first, sameTokenRetry);
        Assert.Equal(first, equivalentRetry);
        Assert.Equal(1, proxy.ImportConfirmCalls);

        var crossProfile = plan with { SourceProfileCode = "CSDT_MOTO" };
        var (crossProfileToken, _) = store.Put("IMPORT", "operator-a", crossProfile);
        var conflict = await Assert.ThrowsAsync<AssignmentDomainException>(() =>
            service.ConfirmImportAsync(
                42,
                new ConfirmPreviewRequest
                {
                    PreviewToken = crossProfileToken,
                    IdempotencyKey = request.IdempotencyKey,
                    Reason = request.Reason,
                },
                "operator-a",
                CancellationToken.None));
        Assert.Equal("CONFLICT", conflict.Code);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(1, proxy.ImportConfirmCalls);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public async Task Durable_assignment_replay_survives_process_restart_and_rechecks_bulk_permission()
    {
        var repository = DispatchProxy.Create<IAssignmentRepository, RepositoryProxy>();
        var proxy = (RepositoryProxy)(object)repository;
        var replayResult = new AssignmentConfirmResult(
            "durable-operation",
            12,
            3,
            new DateTime(2026, 7, 30, 9, 0, 0, DateTimeKind.Utc));
        proxy.DurableAssignmentReplay = new AssignmentConfirmReplay(
            replayResult,
            RequiresBulkPermission: true);
        var service = new AssignmentService(repository, new AssignmentPreviewStore());
        var request = new ConfirmPreviewRequest
        {
            PreviewToken = "token-from-previous-process",
            IdempotencyKey = "durable-replay-001",
            Reason = "Retry after process restart",
        };

        var denied = await Assert.ThrowsAsync<AssignmentDomainException>(() =>
            service.ConfirmAssignmentAsync(
                request,
                "operator-a",
                canBulkAssign: false,
                CancellationToken.None));
        Assert.Equal("FORBIDDEN", denied.Code);

        var replay = await service.ConfirmAssignmentAsync(
            request,
            "operator-a",
            canBulkAssign: true,
            CancellationToken.None);

        Assert.Equal(replayResult, replay);
        Assert.Equal(0, proxy.ConfirmCalls);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public async Task Durable_import_replay_survives_process_restart_without_in_memory_preview()
    {
        var repository = DispatchProxy.Create<IAssignmentRepository, RepositoryProxy>();
        var proxy = (RepositoryProxy)(object)repository;
        var replayResult = new AssignmentImportConfirmResult(
            91,
            "durable-import-operation",
            8,
            2,
            new DateTime(2026, 7, 30, 9, 5, 0, DateTimeKind.Utc));
        proxy.DurableImportReplay = replayResult;
        var service = new AssignmentService(repository, new AssignmentPreviewStore());

        var replay = await service.ConfirmImportAsync(
            42,
            new ConfirmPreviewRequest
            {
                PreviewToken = "import-token-from-previous-process",
                IdempotencyKey = "durable-import-replay-001",
                Reason = "Retry import after process restart",
            },
            "operator-a",
            CancellationToken.None);

        Assert.Equal(replayResult, replay);
        Assert.Equal(0, proxy.ImportConfirmCalls);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Putting_student_in_group_copies_all_defaults_and_resets_overrides()
    {
        var before = new AssignmentSnapshot(10, 20, 30, 40, 50, true, true, true);
        var group = CreateGroupDefaults(groupId: 11, teacherId: 31, vehicleId: 41, figure10Id: 51);

        var after = InvokeBuildDesiredSnapshot(
            AssignmentOperation.PutInGroup,
            before,
            group,
            fields: null,
            requestedGroupId: 11);

        Assert.Equal(new AssignmentSnapshot(11, 20, 31, 41, 51, false, false, false), after);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Unrelated_student_edit_rebases_inherited_fields_after_no_current_change_defaults_update()
    {
        var before = new AssignmentSnapshot(11, 20, 30, 40, 50, false, false, false);
        var newDefaults = CreateGroupDefaults(groupId: 11, teacherId: 31, vehicleId: 41, figure10Id: 51);
        var fields = new AssignmentFieldsRequest
        {
            DossierReceiver = new FieldActionRequest { Action = AssignmentAction.Set, Id = 21 },
        };

        var after = InvokeBuildDesiredSnapshot(
            AssignmentOperation.StudentOverride,
            before,
            newDefaults,
            fields,
            requestedGroupId: null);

        Assert.Equal(new AssignmentSnapshot(11, 21, 31, 41, 51, false, false, false), after);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Repository_contract_requires_business_key_ambiguity_before_optional_id_cross_check()
    {
        var source = Repository("SqlAssignmentRepository.HistoryExcel.cs");

        Assert.DoesNotContain("(@HocVienId IS NULL OR h.HocVienId=@HocVienId)", source, StringComparison.Ordinal);
        Assert.Matches(
            @"(?s)if\s*\(matches\.Length>1\).*?AMBIGUOUS.*?row\.HocVienId.*?learner\.HocVienId",
            source);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Confirm_rechecks_business_key_uniqueness_and_rejects_a_post_preview_duplicate()
    {
        var source = Repository("SqlAssignmentRepository.Mutations.cs");
        var method = AssignmentSourceTestHelper.Section(
            source,
            "private async Task<CurrentAssignmentRow?> LockAndValidateTargetAsync",
            "private async Task ValidateSealedGroupsAsync");

        Assert.Contains("SELECT TOP(2) HocVienId,MaDK,MaKhoa,SourceProfileCode,RowVersion", method, StringComparison.Ordinal);
        Assert.Contains("MaDK=@RegistrationCode AND MaKhoa=@CourseCode", method, StringComparison.Ordinal);
        Assert.Contains("SourceProfileCode=@SourceProfileCode", method, StringComparison.Ordinal);
        Assert.Contains("learners.Length!=1", method, StringComparison.Ordinal);
        Assert.Contains("learners[0].HocVienId!=target.HocVienId", method, StringComparison.Ordinal);
        Assert.Contains(",409", method, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Every_genuine_no_change_target_is_locked_and_revalidated_at_confirm()
    {
        var mutation = Repository("SqlAssignmentRepository.Mutations.cs");
        var import = Repository("SqlAssignmentRepository.HistoryExcel.cs");
        var setBased = Repository("SqlAssignmentRepository.SetBased.cs");

        Assert.Contains("target.Status is Ready or NoChange", mutation, StringComparison.Ordinal);
        Assert.Contains("LockAndValidateTargetsAsync(connection,transaction,course,plan.Targets", mutation, StringComparison.Ordinal);
        Assert.Contains("LockAndValidateTargetAsync", mutation, StringComparison.Ordinal);
        Assert.Contains("StageAndValidateImportTargetsSetBasedAsync", import, StringComparison.Ordinal);
        Assert.Contains("foreach(var row in plan.Rows)", setBased, StringComparison.Ordinal);
        Assert.Contains("row.Status is not (Ready or NoChange)", setBased, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)", setBased, StringComparison.Ordinal);
        Assert.Contains("Target=null", import, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_keep_can_retain_an_unchanged_inactive_group()
    {
        var source = Repository("SqlAssignmentRepository.HistoryExcel.cs");
        var method = AssignmentSourceTestHelper.Section(
            source,
            "private static GroupDefaultsRow? ResolveImportGroup",
            "private static long? ResolveImportCode");

        Assert.Contains("AssignmentAction.Set", method, StringComparison.Ordinal);
        Assert.Contains("requiresActive", method, StringComparison.Ordinal);
        Assert.Contains("if(requiresActive && !string.Equals(group.TrangThai", method, StringComparison.Ordinal);
        Assert.Contains("catalog.GroupsById.TryGetValue", method, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Unchanged_inactive_references_are_preserved_but_new_inactive_references_are_rejected()
    {
        var source = Repository("SqlAssignmentRepository.Mutations.cs");
        var method = AssignmentSourceTestHelper.Section(
            source,
            "private async Task ValidateSnapshotReferencesAsync",
            "private static AssignmentSnapshot? BuildDesiredSnapshot");

        Assert.Contains("(@RequireGroupActive=0 OR n.TrangThai='ACTIVE')", method, StringComparison.Ordinal);
        foreach (var parameter in new[]
                 {
                     "RequireDossierActive", "RequireTeacherActive", "RequireVehicleActive",
                     "RequireFigure10Active",
                 })
        {
            Assert.Contains($"@{parameter}=0 OR EXISTS", method, StringComparison.Ordinal);
        }
        Assert.Contains("RequireGroupActive=before?.GroupId!=snapshot.GroupId", method, StringComparison.Ordinal);
        Assert.Contains("RequireDossierActive=before?.DossierReceiverId!=snapshot.DossierReceiverId", method, StringComparison.Ordinal);
        Assert.Contains("RequireTeacherActive=before?.ClassTeacherId!=snapshot.ClassTeacherId", method, StringComparison.Ordinal);
        Assert.Contains("RequireVehicleActive=before?.TrainingVehicleId!=snapshot.TrainingVehicleId", method, StringComparison.Ordinal);
        Assert.Contains("RequireFigure10Active=before?.Figure10VehicleId!=snapshot.Figure10VehicleId", method, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Confirm_seals_group_defaults_and_current_membership()
    {
        var source = Repository("SqlAssignmentRepository.Mutations.cs");
        var groupConfirm = AssignmentSourceTestHelper.Section(
            source,
            "public async Task<AssignmentConfirmResult> ConfirmGroupDefaultsPlanAsync",
            "private async Task ApplyTargetAsync");
        var sealedGroups = AssignmentSourceTestHelper.Section(
            source,
            "private async Task ValidateSealedGroupsAsync",
            "private static bool SealedGroupEquals");
        var currentMatch = AssignmentSourceTestHelper.Section(
            source,
            "private static bool CurrentMatchesPlan",
            "private static void EnsureConfirmable");

        Assert.Contains("current.RowVersion.SequenceEqual(expected.RowVersion)", sealedGroups, StringComparison.Ordinal);
        Assert.Contains("current.GiaoVienDungLopId!=expected.ClassTeacherId", sealedGroups, StringComparison.Ordinal);
        Assert.Contains("current.XeTapId!=expected.TrainingVehicleId", sealedGroups, StringComparison.Ordinal);
        Assert.Contains("current.XeBaiSo10Id!=expected.Figure10VehicleId", sealedGroups, StringComparison.Ordinal);
        Assert.Contains("current.TrangThai,expected.Status", sealedGroups, StringComparison.Ordinal);
        Assert.Contains("SnapshotsEqual(current.ToSnapshot(),target.Before)", currentMatch, StringComparison.Ordinal);
        Assert.Contains("plan.Mode != GroupPropagationMode.NoCurrentChange", groupConfirm, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentGroupAssignmentsAsync", groupConfirm, StringComparison.Ordinal);
        Assert.Contains("transaction, lockForUpdate: true", groupConfirm, StringComparison.Ordinal);
        Assert.Contains("plan.Targets.Select(target => target.HocVienId).OrderBy", groupConfirm, StringComparison.Ordinal);
        Assert.Contains("currentMembers.Select(member => member.HocVienId).OrderBy", groupConfirm, StringComparison.Ordinal);
        Assert.Contains("!sealedMemberIds.SequenceEqual(currentMemberIds)", groupConfirm, StringComparison.Ordinal);
        Assert.Contains("WHERE pc.IsCurrent=1 AND pc.NhomDaoTaoId=@GroupId", source, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Assignment_and_group_default_confirms_take_course_then_group_locks()
    {
        var source = Repository("SqlAssignmentRepository.Mutations.cs");
        var assignmentConfirm = AssignmentSourceTestHelper.Section(
            source,
            "public async Task<AssignmentConfirmResult> ConfirmAssignmentPlanAsync",
            "public async Task<AssignmentConfirmResult> ConfirmGroupDefaultsPlanAsync");
        var defaultsConfirm = AssignmentSourceTestHelper.Section(
            source,
            "public async Task<AssignmentConfirmResult> ConfirmGroupDefaultsPlanAsync",
            "private async Task ApplyTargetAsync");

        AssertCourseThenGroup(assignmentConfirm, "LoadCourseIdentityLockedAsync", "ValidateSealedGroupsAsync");
        AssertCourseThenGroup(defaultsConfirm, "LoadCourseIdentity", "LoadGroupDefaultsAsync");
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Five_thousand_row_import_plan_has_no_per_row_database_round_trips()
    {
        var source = Repository("SqlAssignmentRepository.HistoryExcel.cs");
        var rowLoop = AssignmentSourceTestHelper.Section(
            source,
            "foreach(var row in rows)",
            "foreach(var duplicate in planRows");

        Assert.DoesNotContain("await ", rowLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryAsync", rowLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("QuerySingle", rowLoop, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_sessions_bind_result_and_idempotency_to_course_and_profile()
    {
        var source = Repository("SqlAssignmentRepository.HistoryExcel.cs");
        var ledger = Repository("SqlAssignmentRepository.Idempotency.cs");

        Assert.Contains("JSON_VALUE", source, StringComparison.Ordinal);
        Assert.Contains("$.SourceProfileCode", source, StringComparison.Ordinal);
        Assert.Contains("$.CourseId", source, StringComparison.Ordinal);
        Assert.Contains("plan.CourseId", ledger, StringComparison.Ordinal);
        Assert.Contains("plan.SourceProfileCode", ledger, StringComparison.Ordinal);
        Assert.Contains("plan.FileSha256", ledger, StringComparison.Ordinal);
        Assert.Contains("EnsureLedgerScope", ledger, StringComparison.Ordinal);
        Assert.Matches(@"(?s)GetImportResultAsync.*?SourceProfileCode", source);
    }

    private static AssignmentSnapshot? InvokeBuildDesiredSnapshot(
        string operation,
        AssignmentSnapshot? before,
        object group,
        AssignmentFieldsRequest? fields,
        long? requestedGroupId)
    {
        var method = typeof(SqlAssignmentRepository).GetMethod(
            "BuildDesiredSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildDesiredSnapshot not found.");
        return (AssignmentSnapshot?)method.Invoke(
            null,
            [operation, before, group, fields, requestedGroupId]);
    }

    private static object CreateGroupDefaults(
        long groupId,
        long? teacherId,
        long? vehicleId,
        long? figure10Id)
    {
        var type = typeof(SqlAssignmentRepository).GetNestedType(
            "GroupDefaultsRow",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GroupDefaultsRow not found.");
        return Activator.CreateInstance(
                   type,
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null,
                   args: [groupId, 42L, teacherId, vehicleId, figure10Id, "ACTIVE", new byte[8]],
                   culture: null)
               ?? throw new InvalidOperationException("GroupDefaultsRow could not be created.");
    }

    private static string Repository(string fileName) => AssignmentSourceTestHelper.Read(
        "server", "QLHV.Infrastructure", "Assignments", fileName);

    private static void AssertCourseThenGroup(string method, string courseMarker, string groupMarker)
    {
        var course = method.IndexOf(courseMarker, StringComparison.Ordinal);
        var group = method.IndexOf(groupMarker, StringComparison.Ordinal);
        Assert.True(course >= 0, $"Missing course lock marker: {courseMarker}");
        Assert.True(group >= 0, $"Missing group lock marker: {groupMarker}");
        Assert.True(course < group, "Lock order must be course then group.");
    }

    public class RepositoryProxy : DispatchProxy
    {
        public int ConfirmCalls { get; private set; }
        public int ImportConfirmCalls { get; private set; }
        public AssignmentConfirmReplay? DurableAssignmentReplay { get; set; }
        public AssignmentImportConfirmResult? DurableImportReplay { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IAssignmentRepository.ConfirmAssignmentPlanAsync))
            {
                ConfirmCalls++;
                return Task.FromResult(new AssignmentConfirmResult("confirmed", 0, 0, DateTime.UtcNow));
            }

            if (targetMethod?.Name == nameof(IAssignmentRepository.TryReplayAssignmentConfirmAsync))
            {
                return Task.FromResult(DurableAssignmentReplay);
            }

            if (targetMethod?.Name == nameof(IAssignmentRepository.ConfirmImportPlanAsync))
            {
                ImportConfirmCalls++;
                return Task.FromResult(new AssignmentImportConfirmResult(
                    77,
                    "import-confirmed",
                    1,
                    0,
                    new DateTime(2026, 7, 30, 8, 0, 0, DateTimeKind.Utc)));
            }

            if (targetMethod?.Name == nameof(IAssignmentRepository.TryReplayImportConfirmAsync))
            {
                return Task.FromResult(DurableImportReplay);
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
