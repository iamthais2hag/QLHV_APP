using System.Globalization;
using QLHV.Application.CourseCompletion;
using QLHV.Application.Runtime;

namespace QLHV.Tests.CourseCompletion;

public sealed class CourseCompletionV1Tests
{
    private readonly CourseCompletionCanonicalSnapshotBuilder _builder = new();

    [Theory]
    [InlineData("09", "PASSED")]
    [InlineData("10", "FAILED")]
    public void Final_training_results_are_ready(string status, string classification)
    {
        var result = Build(Learner(status));
        Assert.True(result.CanConfirm);
        Assert.Equal(classification, result.Learners.Single().Classification);
    }

    [Fact]
    public void Mixed_pass_and_fail_is_ready()
    {
        var result = Build(Learner("09", "A"), Learner("10", "B"));
        Assert.True(result.CanConfirm);
        Assert.Equal(1, result.PassedCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Theory]
    [InlineData("11")]
    [InlineData("12")]
    [InlineData("13")]
    [InlineData("14")]
    [InlineData("15")]
    [InlineData("16")]
    [InlineData("17")]
    [InlineData("18")]
    [InlineData("19")]
    public void Downstream_states_are_ready_read_only(string status)
    {
        var result = Build(Learner(status, complete: false));
        Assert.True(result.CanConfirm);
        Assert.Equal("DOWNSTREAM", result.Learners.Single().Classification);
        Assert.Equal("READ_ONLY_DOWNSTREAM", result.Learners.Single().ResultCompleteness);
    }

    [Theory]
    [InlineData("01")]
    [InlineData("02")]
    [InlineData("03")]
    [InlineData("04")]
    [InlineData("05")]
    [InlineData("06")]
    [InlineData("07")]
    [InlineData("08")]
    [InlineData("90")]
    [InlineData("")]
    [InlineData("XX")]
    public void Non_final_states_are_blocked(string status)
    {
        var result = Build(Learner(status));
        Assert.False(result.CanConfirm);
        Assert.Contains(CourseCompletionCodes.StudentStatusInvalid, result.Blockers);
    }

    [Fact]
    public void Null_status_is_blocked() =>
        Assert.Contains(CourseCompletionCodes.StudentStatusInvalid, Build(Learner(null)).Blockers);

    [Fact]
    public void Incomplete_final_result_is_blocked()
    {
        var result = Build(Learner("09", complete: false));
        Assert.Contains(CourseCompletionCodes.StudentResultIncomplete, result.Blockers);
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("A1m")]
    [InlineData("B1m")]
    public void Legacy_reduced_field_classes_accept_core_result(string trainingClass)
    {
        var result = _builder.Build(Scope([Learner("09", complete: false, coreComplete: true)], trainingClass));
        Assert.True(result.CanConfirm);
    }

    [Theory]
    [InlineData("B2")]
    [InlineData("C")]
    [InlineData("D")]
    public void Non_legacy_classes_require_all_numeric_result_fields(string trainingClass)
    {
        var result = _builder.Build(Scope([Learner("09", complete: false, coreComplete: true)], trainingClass));
        Assert.Contains(CourseCompletionCodes.StudentResultIncomplete, result.Blockers);
    }

    [Fact]
    public void Empty_course_is_blocked() =>
        Assert.Contains(CourseCompletionCodes.EmptyCourse, _builder.Build(Scope([])).Blockers);

    [Fact]
    public void Duplicate_registration_is_blocked()
    {
        var result = Build(Learner("09", "DUP"), Learner("10", "DUP"));
        Assert.Contains(CourseCompletionCodes.DuplicateIdentity, result.Blockers);
    }

    [Fact]
    public void Orphan_v1_identity_is_blocked()
    {
        var result = Build(Learner("13", isV1Orphan: true));
        Assert.Contains(CourseCompletionCodes.AmbiguousIdentity, result.Blockers);
    }

    [Fact]
    public void Wrong_course_identity_is_blocked()
    {
        var result = Build(Learner("09") with { CourseKey = "OTHER" });
        Assert.Contains(CourseCompletionCodes.AmbiguousIdentity, result.Blockers);
    }

    [Fact]
    public void Course_diagnostics_are_warning_only()
    {
        var course = Course() with
        {
            HasReportI = false, HasTeacher = false, HasVehicle = false,
            HasProgram = false, TrainingForm = null, StartDate = null, EndDate = null,
        };
        var result = _builder.Build(new(course, [Learner("09")], []));
        Assert.True(result.CanConfirm);
        Assert.Contains("REPORT_I_MISSING", result.Warnings);
        Assert.Contains("COURSE_TEACHER_MISSING", result.Warnings);
        Assert.Contains("COURSE_VEHICLE_MISSING", result.Warnings);
        Assert.Contains("COURSE_PROGRAM_INCOMPLETE", result.Warnings);
    }

    [Fact]
    public void Missing_v1_course_without_v1_learners_is_a_warning_not_a_blocker()
    {
        var scope = Scope([Learner("09")]);
        var result = _builder.Build(new CourseCompletionSourceScope(
            scope.Course, scope.Learners, ["V1_COURSE_MISSING"]));

        Assert.True(result.CanConfirm);
        Assert.Contains("V1_COURSE_MISSING", result.Warnings);
    }

    [Fact]
    public void V1_downstream_status_has_safe_precedence()
    {
        var result = Build(Learner("09") with { V1Status = "17", HasExamLifecycle = true });
        Assert.Equal("17", result.Learners.Single().Status);
        Assert.Equal("EXAM_ACTIVE", result.Learners.Single().DownstreamClassification);
    }

    [Fact]
    public void V1_training_status_does_not_override_v2()
    {
        var result = Build(Learner("10") with { V1Status = "03" });
        Assert.Equal("10", result.Learners.Single().Status);
    }

    [Fact]
    public void Canonical_hash_is_independent_of_input_order()
    {
        var a = _builder.Build(Scope([Learner("09", "A"), Learner("10", "B")]));
        var b = _builder.Build(Scope([Learner("10", "B"), Learner("09", "A")]));
        Assert.Equal(a.SnapshotHash, b.SnapshotHash);
    }

    [Fact]
    public void Canonical_hash_normalizes_sql_trailing_spaces()
    {
        var a = Build(Learner("09", "A"));
        var b = Build(Learner("09", "A   ") with { CourseKey = "KH01   " });
        Assert.Equal(a.SnapshotHash, b.SnapshotHash);
    }

    [Fact]
    public void Canonical_hash_is_locale_independent()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("vi-VN");
            var vi = Build(Learner("09"));
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var en = Build(Learner("09"));
            Assert.Equal(vi.SnapshotHash, en.SnapshotHash);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Source_result_change_changes_hash()
    {
        var a = Build(Learner("09"));
        var b = Build(Learner("09") with { PracticeScore = "99" });
        Assert.NotEqual(a.SnapshotHash, b.SnapshotHash);
    }

    [Fact]
    public void Learner_add_remove_changes_hash()
    {
        var a = Build(Learner("09", "A"));
        var b = Build(Learner("09", "A"), Learner("10", "B"));
        Assert.NotEqual(a.SnapshotHash, b.SnapshotHash);
    }

    [Fact]
    public void Snapshot_builder_handles_5000_learners_setwise()
    {
        var rows = Enumerable.Range(1, 5000).Select(i => Learner(i % 2 == 0 ? "09" : "10", $"R{i:00000}")).ToArray();
        var result = _builder.Build(Scope(rows));
        Assert.True(result.CanConfirm);
        Assert.Equal(5000, result.LearnerCount);
        Assert.Equal(5000, result.Learners.Select(x => x.ProtectedIdentity).Distinct().Count());
    }

    [Fact]
    public void Protected_snapshot_does_not_expose_registration_code()
    {
        const string registration = "SECRET-MADK";
        var learner = Build(Learner("09", registration)).Learners.Single();
        Assert.DoesNotContain(registration, learner.ProtectedIdentity, StringComparison.Ordinal);
        Assert.Equal(64, learner.ProtectedIdentity.Length);
    }

    [Fact]
    public void Preview_token_is_actor_and_course_bound()
    {
        var store = new CourseCompletionPreviewStore();
        var snapshot = Build(Learner("09"));
        var preview = new SealedCourseCompletionPreview("admin", 1, "CSDT_OTO", "KH01", "1.0", snapshot.SnapshotHash, 1, [], [], snapshot);
        var token = store.Put(preview).Token;
        Assert.Throws<CourseCompletionDomainException>(() => store.Get(token, "other", 1));
        Assert.Throws<CourseCompletionDomainException>(() => store.Get(token, "admin", 2));
        Assert.Same(preview, store.Get(token, "admin", 1));
    }

    [Fact]
    public void Preview_token_expires_fail_closed()
    {
        var clock = new AdjustableTimeProvider();
        var store = new CourseCompletionPreviewStore(clock);
        var snapshot = Build(Learner("09"));
        var token = store.Put(new("admin", 1, "CSDT_OTO", "KH01", "1.0", snapshot.SnapshotHash, 1, [], [], snapshot)).Token;
        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(CourseCompletionCodes.Conflict,
            Assert.Throws<CourseCompletionDomainException>(() => store.Get(token, "admin", 1)).Code);
    }

    [Fact]
    public async Task Ready_preview_confirms_completed()
    {
        var repository = new FakeRepository(Scope([Learner("09")]));
        var service = Service(repository);
        var preview = await service.PreviewAsync(1, new(), "admin", default);
        var result = await service.ConfirmAsync(1, new()
        {
            PreviewToken = preview.PreviewToken,
            IdempotencyKey = "key-1",
            CompletionBusinessDate = new DateOnly(2026, 8, 1),
            Reason = "Kết quả đã được kiểm tra",
        }, "admin", default);
        Assert.Equal(CourseCompletionCodes.Completed, result.ResultCode);
        Assert.NotNull(repository.LastConfirm);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Invalid_business_date_or_reason_fails_closed(bool hasDate, bool hasReason)
    {
        var service = Service(new FakeRepository(Scope([Learner("09")])));
        var preview = await service.PreviewAsync(1, new(), "admin", default);
        var error = await Assert.ThrowsAsync<CourseCompletionDomainException>(() => service.ConfirmAsync(1, new()
        {
            PreviewToken = preview.PreviewToken,
            IdempotencyKey = "key",
            CompletionBusinessDate = hasDate ? new DateOnly(2026, 8, 1) : null,
            Reason = hasReason ? "reason" : "",
        }, "admin", default));
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Sql_clock_unavailable_blocks_confirm()
    {
        var repository = new FakeRepository(Scope([Learner("09")]));
        var service = Service(repository, databaseClockAvailable: false);
        var preview = await service.PreviewAsync(1, new(), "admin", default);
        var error = await Assert.ThrowsAsync<CourseCompletionDomainException>(() => service.ConfirmAsync(1, new()
        {
            PreviewToken = preview.PreviewToken,
            IdempotencyKey = "key",
            CompletionBusinessDate = new DateOnly(2026, 8, 1),
            Reason = "reason",
        }, "admin", default));
        Assert.Equal(CourseCompletionCodes.TimeAuthorityBlocked, error.Code);
        Assert.Null(repository.LastConfirm);
    }

    [Fact]
    public async Task Status_reports_completed_when_hash_matches()
    {
        var scope = Scope([Learner("09")]);
        var snapshot = _builder.Build(scope);
        var repository = new FakeRepository(scope) { Marker = Marker(snapshot) };
        var status = await Service(repository).GetStatusAsync(1, default);
        Assert.Equal(CourseCompletionCodes.Completed, status.Status);
    }

    [Fact]
    public async Task Status_not_completed_returns_exact_source_identity_without_source_snapshot()
    {
        var repository = new FakeRepository(Scope([Learner("01")])) { Marker = null };

        var status = await Service(repository).GetStatusAsync(1, default);

        Assert.Equal(CourseCompletionCodes.NotCompleted, status.Status);
        Assert.Equal("CSDT_OTO", status.SourceProfileCode);
        Assert.Equal("KH01", status.SourceCourseKey);
        Assert.Equal(1, repository.IdentityReadCount);
        Assert.Equal(0, repository.SourceScopeReadCount);
    }

    [Fact]
    public async Task Status_reports_correction_without_mutation_when_source_drifts()
    {
        var oldScope = Scope([Learner("09")]);
        var marker = Marker(_builder.Build(oldScope));
        var repository = new FakeRepository(Scope([Learner("10")])) { Marker = marker };
        var status = await Service(repository).GetStatusAsync(1, default);
        Assert.Equal(CourseCompletionCodes.CorrectionRequired, status.Status);
        Assert.NotNull(status.Drift);
        Assert.Null(repository.LastConfirm);
    }

    [Fact]
    public async Task Status_duplicate_source_identity_reports_correction_instead_of_throwing()
    {
        var original = Build(Learner("09"));
        var repository = new FakeRepository(Scope([
            Learner("09", "REG01"),
            Learner("10", "REG01"),
        ])) { Marker = Marker(original) };

        var status = await Service(repository).GetStatusAsync(1, default);

        Assert.Equal(CourseCompletionCodes.CorrectionRequired, status.Status);
        Assert.NotNull(status.Drift);
        Assert.True(status.Drift.ChangedLearners >= 1);
        Assert.Null(repository.LastConfirm);
    }

    [Fact]
    public void Sql_source_batches_are_select_only()
    {
        var file = Read("server", "QLHV.Infrastructure", "CourseCompletion", "SqlCourseCompletionRepository.cs");
        var sourceBatches = Between(file, "internal const string V2ReadSql", "private sealed record CourseIdentity");
        Assert.DoesNotMatch(@"(?i)\b(INSERT|UPDATE|DELETE|MERGE|EXEC(?:UTE)?|TRUNCATE|ALTER|DROP|CREATE)\b", sourceBatches);
        Assert.Contains("FROM dbo.NguoiLX_HoSo", sourceBatches, StringComparison.Ordinal);
        Assert.Contains("ApplicationIntent.ReadOnly", file, StringComparison.Ordinal);
        Assert.Contains("TRY_CONVERT(datetime2(7)", sourceBatches, StringComparison.Ordinal);
        Assert.Contains("IsV1Orphan = candidates is { Length: > 1 }", file, StringComparison.Ordinal);
        Assert.Contains("GetSourceConnectionAsync(fallbackSystem", file, StringComparison.Ordinal);
        Assert.Contains("fixed database-family allowlist", file, StringComparison.Ordinal);
        Assert.Contains("fallbackBuilder.InitialCatalog = expectedDatabase", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_date_columns_use_datetime_bridge_before_dateonly_conversion()
    {
        var file = Read("server", "QLHV.Infrastructure", "CourseCompletion", "SqlCourseCompletionRepository.cs");

        Assert.Equal(1, Count(file, "CONVERT(datetime2(7),CompletionBusinessDate) AS CompletionBusinessDate"));
        Assert.Equal(1, Count(file, "CONVERT(datetime2(7),c.CompletionBusinessDate) AS CompletionBusinessDate"));
        Assert.Equal(2, Count(file, "DateOnly.FromDateTime(row.CompletionBusinessDate"));
        Assert.DoesNotContain("DateOnly CompletionBusinessDate", file, StringComparison.Ordinal);
        Assert.DoesNotContain("DateOnly? CompletionBusinessDate", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_learner_reads_use_parameterless_database_row_before_domain_mapping()
    {
        var file = Read("server", "QLHV.Infrastructure", "CourseCompletion", "SqlCourseCompletionRepository.cs");

        Assert.Equal(2, Count(file, "grid.ReadAsync<CourseCompletionLearnerDbRow>()"));
        Assert.Contains("private sealed class CourseCompletionLearnerDbRow", file, StringComparison.Ordinal);
        Assert.Contains("public CourseCompletionLearnerSource ToSource()", file, StringComparison.Ordinal);
        Assert.DoesNotContain("grid.ReadAsync<CourseCompletionLearnerSource>()", file, StringComparison.Ordinal);
    }

    [Fact]
    public void V1_course_absence_is_stable_warning_but_ambiguous_v1_state_is_blocked()
    {
        var file = Read("server", "QLHV.Infrastructure", "CourseCompletion", "SqlCourseCompletionRepository.cs");

        Assert.Contains("if (courseCount > 1)", file, StringComparison.Ordinal);
        Assert.Contains("if (courseCount == 0 && learners.Length != 0)", file, StringComparison.Ordinal);
        Assert.Contains("sourceDiagnostics.Add(\"V1_COURSE_MISSING\")", file, StringComparison.Ordinal);
        Assert.Contains("scope.CourseCount + \"\\n\"", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirm_is_one_qlhv_transaction_with_xact_abort_and_application_lock()
    {
        var source = Read("server", "QLHV.Infrastructure", "CourseCompletion", "SqlCourseCompletionRepository.cs");
        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON", source, StringComparison.Ordinal);
        Assert.Contains("sp_getapplock", source, StringComparison.Ordinal);
        Assert.Contains("@LockOwner=N'Transaction'", source, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Learner_snapshots_are_inserted_set_based_not_n_plus_one()
    {
        var source = Read("server", "QLHV.Infrastructure", "CourseCompletion", "SqlCourseCompletionRepository.cs");
        Assert.Contains("FROM OPENJSON(@SnapshotJson)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var learner in snapshot.Learners)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Durable_timestamps_are_sql_authoritative()
    {
        var source = Read("server", "QLHV.Infrastructure", "CourseCompletion", "SqlCourseCompletionRepository.cs");
        Assert.Contains("SYSUTCDATETIME()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_reopen_route_capability_or_button_exists()
    {
        var files = new[]
        {
            Read("server", "QLHV.Api", "Controllers", "CourseCompletionController.cs"),
            Read("server", "QLHV.Application", "CourseCompletion", "CourseCompletionPolicies.cs"),
            Read("client", "src", "features", "course-completion", "CourseCompletionPanel.tsx"),
        };
        foreach (var source in files)
        {
            Assert.DoesNotContain("[HttpPost(\"reopen", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("const string Reopen", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(">Mở lại", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Completion_code_does_not_call_report_xml_or_realtime_workflows()
    {
        var roots = new[] { "QLHV.Application", "QLHV.Infrastructure", "QLHV.Api" };
        var source = string.Join('\n', roots.SelectMany(project =>
                Directory.GetFiles(Path.Combine(Root(), "server", project), "*.cs", SearchOption.AllDirectories))
            .Where(path => path.Contains("CourseCompletion", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        Assert.DoesNotContain("BaoCaoII", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RunOnce", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IntegrityScan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkpoint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_is_additive_no_cascade_and_rollback_fails_with_data()
    {
        var forward = Read("database", "patches", "20260801_add_course_completion_v1.sql");
        var rollback = Read("database", "patches", "20260801_rollback_course_completion_v1.sql");
        Assert.Contains("ON DELETE NO ACTION", forward, StringComparison.Ordinal);
        Assert.Contains("COURSE_COMPLETION_V1_PARTIAL_SCHEMA_DRIFT", forward, StringComparison.Ordinal);
        Assert.Contains("COURSE_COMPLETION_V1_ROLLBACK_REFUSED_DATA_EXISTS", rollback, StringComparison.Ordinal);
        Assert.DoesNotContain("CASCADE", forward, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_does_not_touch_realtime_assignment_or_source_tables()
    {
        var forward = Read("database", "patches", "20260801_add_course_completion_v1.sql");
        Assert.DoesNotContain("App_QlhvDirectRealtime", forward, StringComparison.Ordinal);
        Assert.DoesNotContain("App_CsdtRealtime", forward, StringComparison.Ordinal);
        Assert.DoesNotContain("App_HocVien_PhanCong", forward, StringComparison.Ordinal);
        Assert.DoesNotContain("NguoiLX_HoSo", forward, StringComparison.Ordinal);
        Assert.DoesNotContain("KhoaHoc.TrangThai", forward, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_policies_are_admin_only_for_preview_and_complete()
    {
        var program = Read("server", "QLHV.Api", "Program.cs");
        Assert.Contains("CourseCompletionPolicies.Preview", program, StringComparison.Ordinal);
        Assert.Contains("CourseCompletionPolicies.Complete", program, StringComparison.Ordinal);
        var previewTail = program[program.IndexOf("CourseCompletionPolicies.Preview", StringComparison.Ordinal)..];
        Assert.StartsWith("CourseCompletionPolicies.Preview", previewTail);
        Assert.Contains("RequireRole(AppRoles.Admin)", previewTail, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_requires_must_change_password_false_for_all_completion_policies()
    {
        var program = Read("server", "QLHV.Api", "Program.cs");
        var block = Between(program, "CourseCompletionPolicies.ViewStatus", "// In-memory cache");
        Assert.Equal(3, Count(block, "RequireClaim(AppClaimTypes.MustChangePassword"));
    }

    [Fact]
    public void Client_has_no_confirm_bypass_and_disables_blocked_preview()
    {
        var panel = Read("client", "src", "features", "course-completion", "CourseCompletionPanel.tsx");
        Assert.Contains("!preview.canConfirm", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("override", panel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Completion_panel_is_not_coupled_to_assignment_schema_readiness()
    {
        var detail = Read("client", "src", "features", "course-assignment", "CourseDetailPage.tsx");
        var panel = Read("client", "src", "features", "course-completion", "CourseCompletionPanel.tsx");
        Assert.Contains("{activeSection === 'completion' && (", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("{detail && activeSection === 'completion' && (", detail, StringComparison.Ordinal);
        Assert.Contains("detail?.course.sourceProfileCode ?? ''", detail, StringComparison.Ordinal);
        Assert.Contains("status?.sourceProfileCode?.trim()", panel, StringComparison.Ordinal);
    }

    private CourseCompletionCanonicalSnapshot Build(params CourseCompletionLearnerSource[] learners) =>
        _builder.Build(Scope(learners));

    private static CourseCompletionSourceScope Scope(
        IReadOnlyList<CourseCompletionLearnerSource> learners,
        string trainingClass = "B2") => new(Course(trainingClass), learners, []);

    private static CourseCompletionCourseSource Course(string trainingClass = "B2") => new(
        1, "CSDT_OTO", "KH01", "66029", "66", trainingClass, "TAP_TRUNG",
        new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), true, true, true, true);

    private static CourseCompletionLearnerSource Learner(
        string? status, string registration = "REG01", bool complete = true,
        bool coreComplete = true, bool isV1Orphan = false) => new(
            registration, "KH01", status, null,
            coreComplete ? "DAT" : null,
            coreComplete ? new DateTime(2026, 1, 1, 8, 0, 0) : null,
            coreComplete ? new DateTime(2026, 6, 1, 17, 0, 0) : null,
            complete ? "1" : null, complete ? "1" : null,
            complete ? "8.5" : null, complete ? "9" : null,
            complete ? "20" : null, complete ? "40" : null,
            complete ? "100" : null, complete ? "1000" : null,
            false, false, false, isV1Orphan);

    private CourseCompletionStoredMarker Marker(CourseCompletionCanonicalSnapshot snapshot) => new(
        7, 1, snapshot.SourceProfileCode, snapshot.SourceCourseKey, snapshot.ContractVersion,
        new DateOnly(2026, 8, 1), snapshot.SnapshotHash, snapshot.LearnerCount,
        new DateTime(2026, 8, 1, 1, 0, 0, DateTimeKind.Utc), "admin", "reason",
        snapshot.Learners.Select(x => new CourseCompletionStoredLearner(
            x.ProtectedIdentity, x.Status, x.Classification, x.ResultCompleteness, x.CanonicalRowHash)).ToArray());

    private CourseCompletionService Service(FakeRepository repository, bool databaseClockAvailable = true)
    {
        var time = new TimeHealthDto
        {
            DatabaseClockAvailable = databaseClockAvailable,
            DatabaseUtcNow = databaseClockAvailable ? DateTimeOffset.UtcNow : null,
            WritesAllowed = databaseClockAvailable,
        };
        return new(repository, _builder, new CourseCompletionPreviewStore(), new StubTimeAuthority(time));
    }

    private static string Root()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !(Directory.Exists(Path.Combine(current.FullName, "database")) &&
                 Directory.Exists(Path.Combine(current.FullName, "server")) &&
                 Directory.Exists(Path.Combine(current.FullName, "client"))))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));
    private static string Between(string value, string start, string end)
    {
        var from = value.IndexOf(start, StringComparison.Ordinal);
        var to = value.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from);
        return value[from..to];
    }
    private static int Count(string value, string needle) => (value.Length - value.Replace(needle, "", StringComparison.Ordinal).Length) / needle.Length;

    private sealed class FakeRepository(CourseCompletionSourceScope scope) : ICourseCompletionRepository
    {
        public CourseCompletionStoredMarker? Marker { get; set; }
        public CourseCompletionConfirmCommand? LastConfirm { get; private set; }
        public int IdentityReadCount { get; private set; }
        public int SourceScopeReadCount { get; private set; }
        public Task<CourseCompletionCourseIdentity> ReadCourseIdentityAsync(long courseId, CancellationToken cancellationToken)
        {
            IdentityReadCount++;
            return Task.FromResult(new CourseCompletionCourseIdentity(
                courseId, scope.Course.SourceProfileCode, scope.Course.SourceCourseKey));
        }
        public Task<CourseCompletionSourceScope> ReadSourceScopeAsync(long courseId, string? requiredProfile, CancellationToken cancellationToken)
        {
            SourceScopeReadCount++;
            return Task.FromResult(scope);
        }
        public Task<CourseCompletionStoredMarker?> ReadMarkerAsync(long courseId, CancellationToken cancellationToken) => Task.FromResult(Marker);
        public Task<CourseCompletionConfirmResult> ConfirmAsync(CourseCompletionConfirmCommand command, CancellationToken cancellationToken)
        {
            LastConfirm = command;
            return Task.FromResult(new CourseCompletionConfirmResult(command.OperationId, 1, CourseCompletionCodes.Completed,
                command.CompletionBusinessDate, new DateTime(2026, 8, 1, 1, 0, 0, DateTimeKind.Utc),
                command.Actor, command.Preview.LearnerCount, command.Preview.ContractVersion, command.Preview.SnapshotHash));
        }
    }

    private sealed class StubTimeAuthority(TimeHealthDto health) : ITimeAuthorityService
    {
        public Task<TimeHealthDto> GetHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(health);
        public Task<TimeHealthDto> GetWriteAuthorizationAsync(CancellationToken cancellationToken = default) => Task.FromResult(health);
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed) { _now += elapsed; _timestamp += elapsed.Ticks; }
    }
}
