using System.Text.RegularExpressions;

namespace QLHV.Tests.Assignments;

public sealed class AssignmentSetBasedImportContractTests
{
    private static string ReadSetBasedRepository()=>AssignmentSourceTestHelper.Read(
        "server","QLHV.Infrastructure","Assignments","SqlAssignmentRepository.SetBased.cs");

    [Fact]
    [Trait("Category","AssignmentFocused")]
    public void Excel_confirm_stages_at_most_5000_rows_with_bulk_copy_and_bounded_database_calls()
    {
        var source=ReadSetBasedRepository();
        var stage=AssignmentSourceTestHelper.Section(
            source,
            "private async Task StageAndValidateImportTargetsSetBasedAsync",
            "private async Task<int> ApplyImportTargetsSetBasedAsync");

        Assert.Contains("AssignmentRules.MaxImportRows",source,StringComparison.Ordinal);
        Assert.Contains("new SqlBulkCopy(",stage,StringComparison.Ordinal);
        Assert.Contains("#AssignmentImportTarget",stage,StringComparison.Ordinal);
        Assert.Contains("WriteToServerAsync",stage,StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyTargetAsync",stage,StringComparison.Ordinal);
        Assert.Single(Regex.Matches(
            stage,
            @"QuerySingleOrDefaultAsync<SetBasedGuardFailure>").Cast<Match>());
    }

    [Fact]
    [Trait("Category","AssignmentFocused")]
    public void Id_selection_uses_one_sorted_json_parameter_for_the_5000_row_limit()
    {
        var source=AssignmentSourceTestHelper.Read(
            "server","QLHV.Infrastructure","Assignments","SqlAssignmentRepository.Mutations.cs");
        var selection=AssignmentSourceTestHelper.Section(
            source,
            "private async Task<IReadOnlyList<AssignmentLearnerRow>> LoadSelectedLearnersAsync",
            "private async Task<IReadOnlyList<AssignmentLearnerRow>> LoadCurrentGroupAssignmentsAsync");

        Assert.Contains("Distinct().OrderBy(id=>id)",selection,StringComparison.Ordinal);
        Assert.Contains("Take(AssignmentRules.MaxImportRows + 1)",selection,StringComparison.Ordinal);
        Assert.Contains("IdsJson=JsonSerializer.Serialize(ids)",selection,StringComparison.Ordinal);
        Assert.Contains("FROM OPENJSON(@IdsJson)",selection,StringComparison.Ordinal);
        Assert.Contains("WITH (HocVienId BIGINT '$')",selection,StringComparison.Ordinal);
        Assert.DoesNotContain("IN @Ids",selection,StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category","AssignmentFocused")]
    public void Set_based_guard_locks_and_revalidates_all_sealed_identity_and_snapshot_state()
    {
        var source=ReadSetBasedRepository();

        Assert.Contains("row.Status is not (Ready or NoChange)",source,StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)",source,StringComparison.Ordinal);
        Assert.Contains("OPTION (FORCE ORDER,MAXDOP 1)",source,StringComparison.Ordinal);
        Assert.Contains("COUNT_BIG(1) FROM #LockedLearner",source,StringComparison.Ordinal);
        Assert.Contains("COUNT_BIG(1) FROM #LockedCurrent",source,StringComparison.Ordinal);
        Assert.Contains("l.RowVersion=t.LearnerRowVersion",source,StringComparison.Ordinal);
        Assert.Contains("c.RowVersion=t.CurrentAssignmentRowVersion",source,StringComparison.Ordinal);
        Assert.Contains("SourceProfileCode COLLATE Latin1_General_100_BIN2",source,StringComparison.Ordinal);
        Assert.Contains("Group defaults changed after preview",source,StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category","AssignmentFocused")]
    public void Set_based_reference_guards_cover_active_profile_and_inheritance_transitions()
    {
        var source=ReadSetBasedRepository();

        Assert.Contains("AfterTeacherOverride=0",source,StringComparison.Ordinal);
        Assert.Contains("AfterVehicleOverride=0",source,StringComparison.Ordinal);
        Assert.Contains("AfterFigure10Override=0",source,StringComparison.Ordinal);
        Assert.Contains("SourceLifecycle<>'ACTIVE'",source,StringComparison.Ordinal);
        Assert.Contains("SourceActive<>1",source,StringComparison.Ordinal);
        Assert.Contains("g.TrangThai<>'ACTIVE'",source,StringComparison.Ordinal);
        Assert.Contains("d.TrangThai<>'ACTIVE'",source,StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category","AssignmentFocused")]
    public void Set_based_apply_closes_then_inserts_full_snapshots_and_stable_per_target_audit()
    {
        var source=ReadSetBasedRepository();
        var apply=AssignmentSourceTestHelper.Section(
            source,
            "private const string ApplyImportTargetsSql",
            "private sealed record SetBasedGuardFailure");

        var closeIndex=apply.IndexOf("SET IsCurrent=0",StringComparison.Ordinal);
        var insertIndex=apply.IndexOf("INSERT dbo.App_HocVien_PhanCong",StringComparison.Ordinal);
        var auditIndex=apply.IndexOf("INSERT dbo.App_AuditLog",StringComparison.Ordinal);
        Assert.True(closeIndex>=0 && insertIndex>closeIndex && auditIndex>insertIndex);
        Assert.Contains("OUTPUT INSERTED.HocVienId,INSERTED.PhanCongId",apply,StringComparison.Ordinal);
        Assert.Contains("@CourseId AS [courseId]",apply,StringComparison.Ordinal);
        Assert.Contains("@SourceProfileCode AS [sourceProfileCode]",apply,StringComparison.Ordinal);
        Assert.Contains("@OperationId AS [operationId]",apply,StringComparison.Ordinal);
        Assert.Contains("@Reason AS [reason]",apply,StringComparison.Ordinal);
        Assert.Contains("c.BeforeGroupId AS [groupId]",apply,StringComparison.Ordinal);
        Assert.Contains("c.AfterGroupId AS [groupId]",apply,StringComparison.Ordinal);
        Assert.DoesNotContain("LearnerName",apply,StringComparison.Ordinal);
        Assert.DoesNotContain("RegistrationCode",apply,StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category","AssignmentFocused")]
    public void Import_confirm_uses_set_based_guard_before_session_and_set_based_apply_after_session()
    {
        var source=AssignmentSourceTestHelper.Read(
            "server","QLHV.Infrastructure","Assignments","SqlAssignmentRepository.HistoryExcel.cs");
        var confirm=AssignmentSourceTestHelper.Section(
            source,
            "public async Task<AssignmentImportConfirmResult> ConfirmImportPlanAsync",
            "public async Task<AssignmentExportData> GetImportResultAsync");

        var guardIndex=confirm.IndexOf("StageAndValidateImportTargetsSetBasedAsync",StringComparison.Ordinal);
        var sessionIndex=confirm.IndexOf("INSERT dbo.App_ImportBatch",StringComparison.Ordinal);
        var applyIndex=confirm.IndexOf("ApplyImportTargetsSetBasedAsync",StringComparison.Ordinal);
        Assert.True(guardIndex>=0 && sessionIndex>guardIndex && applyIndex>sessionIndex);
        Assert.DoesNotContain("ApplyTargetAsync",confirm,StringComparison.Ordinal);
        Assert.DoesNotContain("foreach",confirm,StringComparison.Ordinal);
    }
}
