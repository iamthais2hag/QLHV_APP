using QLHV.Application.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeIdentityAndOptionsTests
{
    [Theory]
    [InlineData("66029")]
    [InlineData("66030")]
    public void MaCsdt_is_accepted_only_verbatim(string value)
    {
        Assert.True(CsdtRealtimeIdentityRules.IsCurrentMaCsdt(value));
        Assert.False(CsdtRealtimeIdentityRules.IsCurrentMaCsdt(" " + value));
        Assert.False(CsdtRealtimeIdentityRules.IsCurrentMaCsdt(value + " "));
    }

    [Theory]
    [InlineData("66029", "66029K260001", "66029-20260722-000001")]
    [InlineData("66030", "66030K260001", "66030-20260722-000001")]
    public void Current_course_and_student_codes_preserve_all_identity_characters(
        string center,
        string course,
        string student)
    {
        Assert.True(CsdtRealtimeIdentityRules.IsCurrentCourseCode(course, center));
        Assert.True(CsdtRealtimeIdentityRules.IsCurrentStudentCode(student, center));

        Assert.False(CsdtRealtimeIdentityRules.IsCurrentCourseCode(
            course.Replace('K', 'k'),
            center));
        Assert.False(CsdtRealtimeIdentityRules.IsCurrentCourseCode(
            course.Replace("K", string.Empty, StringComparison.Ordinal),
            center));
        Assert.False(CsdtRealtimeIdentityRules.IsCurrentStudentCode(
            student.Replace("-", string.Empty, StringComparison.Ordinal),
            center));
        Assert.False(CsdtRealtimeIdentityRules.IsCurrentStudentCode(" " + student, center));
    }

    [Theory]
    [InlineData("B")]
    [InlineData("B2")]
    [InlineData("C1E")]
    public void Completion_certificate_must_equal_raw_student_plus_raw_class(string trainingClass)
    {
        const string maDk = "66029-20260722-000001";
        var certificate = maDk + "-" + trainingClass;

        Assert.True(CsdtRealtimeIdentityRules.IsExactCompletionCertificate(
            certificate,
            maDk,
            trainingClass));
        Assert.False(CsdtRealtimeIdentityRules.IsExactCompletionCertificate(
            certificate + " ",
            maDk,
            trainingClass));
        Assert.False(CsdtRealtimeIdentityRules.IsExactCompletionCertificate(
            certificate.ToLowerInvariant(),
            maDk,
            trainingClass));
    }

    [Fact]
    public void Legacy_codes_are_readable_but_never_trimmed_into_valid_values()
    {
        Assert.True(CsdtRealtimeIdentityRules.IsRawCourseCodeOrStorableLegacy("LEGACY-KH-01"));
        Assert.False(CsdtRealtimeIdentityRules.IsRawCourseCodeOrStorableLegacy(" LEGACY-KH-01"));
        Assert.False(CsdtRealtimeIdentityRules.IsRawCourseCodeOrStorableLegacy("LEGACY-KH-01 "));
        Assert.True(CsdtRealtimeIdentityRules.IsRawCourseCodeOrStorableLegacy("66029K260000"));
        Assert.True(CsdtRealtimeIdentityRules.IsCurrentStudentCode("66029-99999999-000000", "66029"));
    }

    [Fact]
    public void Default_live_options_are_valid_and_resolve_only_fixed_databases()
    {
        var options = new CsdtRealtimeSyncOptions();
        var result = new CsdtRealtimeSyncOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
        var routes = CsdtRealtimeStreamCatalog.GetConfiguredRoutes(options);
        Assert.Collection(
            routes,
            route =>
            {
                Assert.Equal(CsdtRealtimeProfileCodes.OtoV2, route.SourceProfileCode);
                Assert.Equal(CsdtRealtimeDatabaseNames.OtoV2, route.SourceDatabaseName);
                Assert.Equal(CsdtRealtimeProfileCodes.OtoV1, route.TargetProfileCode);
                Assert.Equal(CsdtRealtimeDatabaseNames.OtoV1, route.TargetDatabaseName);
                Assert.False(route.IsBackup);
            },
            route =>
            {
                Assert.Equal(CsdtRealtimeProfileCodes.MotoV2, route.SourceProfileCode);
                Assert.Equal(CsdtRealtimeDatabaseNames.MotoV2, route.SourceDatabaseName);
                Assert.Equal(CsdtRealtimeProfileCodes.MotoV1, route.TargetProfileCode);
                Assert.Equal(CsdtRealtimeDatabaseNames.MotoV1, route.TargetDatabaseName);
                Assert.False(route.IsBackup);
            });
    }

    [Fact]
    public void Backup_mode_requires_both_fixed_backup_profile_pairs()
    {
        var options = BackupOptions();
        var result = new CsdtRealtimeSyncOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.All(
            CsdtRealtimeStreamCatalog.GetConfiguredRoutes(options),
            route => Assert.True(route.IsBackup));

        options.Streams.Oto.SourceProfile = CsdtRealtimeProfileCodes.OtoV2;
        options.Streams.Oto.TargetProfile = CsdtRealtimeProfileCodes.OtoV1;
        result = new CsdtRealtimeSyncOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("BAK", StringComparison.Ordinal));
    }

    [Fact]
    public void Configured_routes_keep_oto_and_moto_isolated_in_live_and_backup_modes()
    {
        var liveRoutes = CsdtRealtimeStreamCatalog.GetConfiguredRoutes(new CsdtRealtimeSyncOptions());
        var backupRoutes = CsdtRealtimeStreamCatalog.GetConfiguredRoutes(BackupOptions());

        Assert.Collection(
            liveRoutes,
            oto =>
            {
                Assert.Equal(CsdtRealtimeStreamCodes.OtoV2ToV1, oto.StreamCode);
                Assert.Equal(CsdtRealtimeVehicleTypes.Oto, oto.VehicleType);
                Assert.Equal(CsdtRealtimeProfileCodes.OtoV2, oto.SourceProfileCode);
                Assert.Equal(CsdtRealtimeProfileCodes.OtoV1, oto.TargetProfileCode);
                Assert.Equal("66029", oto.MaCSDT);
            },
            moto =>
            {
                Assert.Equal(CsdtRealtimeStreamCodes.MotoV2ToV1, moto.StreamCode);
                Assert.Equal(CsdtRealtimeVehicleTypes.Moto, moto.VehicleType);
                Assert.Equal(CsdtRealtimeProfileCodes.MotoV2, moto.SourceProfileCode);
                Assert.Equal(CsdtRealtimeProfileCodes.MotoV1, moto.TargetProfileCode);
                Assert.Equal("66030", moto.MaCSDT);
            });
        Assert.Collection(
            backupRoutes,
            oto =>
            {
                Assert.Equal(CsdtRealtimeStreamCodes.OtoV2ToV1, oto.StreamCode);
                Assert.Equal(CsdtRealtimeVehicleTypes.Oto, oto.VehicleType);
                Assert.Equal(CsdtRealtimeProfileCodes.OtoV2Bak, oto.SourceProfileCode);
                Assert.Equal(CsdtRealtimeProfileCodes.OtoV1Bak, oto.TargetProfileCode);
            },
            moto =>
            {
                Assert.Equal(CsdtRealtimeStreamCodes.MotoV2ToV1, moto.StreamCode);
                Assert.Equal(CsdtRealtimeVehicleTypes.Moto, moto.VehicleType);
                Assert.Equal(CsdtRealtimeProfileCodes.MotoV2Bak, moto.SourceProfileCode);
                Assert.Equal(CsdtRealtimeProfileCodes.MotoV1Bak, moto.TargetProfileCode);
            });
    }
    [Fact]
    public void Cross_vehicle_or_case_changed_profiles_are_rejected()
    {
        var options = new CsdtRealtimeSyncOptions();
        options.Streams.Oto.TargetProfile = CsdtRealtimeProfileCodes.MotoV1;
        var result = new CsdtRealtimeSyncOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);

        options = new CsdtRealtimeSyncOptions();
        options.Streams.Oto.SourceProfile = "oto_v2";
        result = new CsdtRealtimeSyncOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Realtime_domain_catalog_keeps_teacher_domains_optional_without_blocking_mandatory_domains()
    {
        var catalog = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "Realtime",
            "CsdtRealtimeDomainCatalog.cs"));

        Assert.Contains("\"DM_DonViGTVT\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"KhoaHoc\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"BaoCaoI\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"NguoiLX\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"NguoiLX_HoSo\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"NguoiLX_GPLX\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"NguoiLXHS_GiayTo\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"GiaoVien\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"KhoaHoc_GiaoVien\"", catalog, StringComparison.Ordinal);

        Assert.Equal(2, CountOccurrences(catalog, "IsOptional: true"));
    }

    [Fact]
    public void Realtime_worker_records_optional_schema_skip_without_marking_mandatory_failure()
    {
        var processor = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "Realtime",
            "CsdtRealtimeStreamProcessor.cs"));
        var stateRepository = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "Realtime",
            "CsdtRealtimeWorkerStateRepository.cs"));
        var skipOptional = Section(
            stateRepository,
            "internal async Task SkipOptionalDomainAsync",
            "internal async Task RecordStreamFailureAsync");

        Assert.Contains("currentDomainState.IsOptional &&", processor, StringComparison.Ordinal);
        Assert.Contains("exception is CsdtRealtimeSchemaException", processor, StringComparison.Ordinal);
        Assert.Contains("SkipOptionalDomainAsync", processor, StringComparison.Ordinal);
        Assert.Contains("mandatoryFailed |= !currentDomainState.IsOptional", processor, StringComparison.Ordinal);
        Assert.Contains("DomainStatus = N'SKIPPED'", skipOptional, StringComparison.Ordinal);
        Assert.Contains("LastErrorCode = N'SKIPPED_UNSUPPORTED_SCHEMA'", skipOptional, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO dbo.App_CsdtRealtimeRunDomain", skipOptional, StringComparison.Ordinal);
        Assert.DoesNotContain("LastSuccessfulVersion = @ToVersion", skipOptional, StringComparison.Ordinal);
    }
    [Fact]
    public void Identity_guard_source_contains_no_legacy_generator_or_normalization_call()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Application",
            "Sync",
            "Realtime",
            "CsdtRealtimeIdentityRules.cs"));

        Assert.DoesNotContain("CreateNewMa", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Trim(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToUpper", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToLower", source, StringComparison.Ordinal);
    }

    private static CsdtRealtimeSyncOptions BackupOptions()
        => new()
        {
            Enabled = true,
            UseBackupProfiles = true,
            Streams = new CsdtRealtimeStreamsOptions
            {
                Oto = new CsdtRealtimeStreamOptions
                {
                    Enabled = true,
                    StreamCode = CsdtRealtimeStreamCodes.OtoV2ToV1,
                    SourceProfile = CsdtRealtimeProfileCodes.OtoV2Bak,
                    TargetProfile = CsdtRealtimeProfileCodes.OtoV1Bak,
                    MaCSDT = "66029",
                },
                Moto = new CsdtRealtimeStreamOptions
                {
                    Enabled = true,
                    StreamCode = CsdtRealtimeStreamCodes.MotoV2ToV1,
                    SourceProfile = CsdtRealtimeProfileCodes.MotoV2Bak,
                    TargetProfile = CsdtRealtimeProfileCodes.MotoV1Bak,
                    MaCSDT = "66030",
                },
            },
        };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string Section(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source[startIndex..endIndex];
    }

    private static string FindWorkspaceFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
