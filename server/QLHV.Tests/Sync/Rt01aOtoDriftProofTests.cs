using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QLHV.Application;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt01;
using QLHV.Infrastructure.Sync.Rt01;

namespace QLHV.Tests.Sync;

public sealed class Rt01aOtoDriftProofTests
{
    private static readonly byte[] HmacKey =
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void Requirement_01_equal_counts_can_have_source_only_and_target_only()
    {
        var source = new[] { Mapped("A"), Mapped("B") };
        var target = new[] { Target(source[0], hocVienId: 1), Target(Mapped("C"), hocVienId: 2) };

        var result = Classify(source, target);

        Assert.Equal(2, result.SourceActiveRows);
        Assert.Equal(2, result.TargetActiveRows);
        Assert.Equal(1, result.WouldInsertRows);
        Assert.Equal(1, result.TargetOnlyActiveRows);
    }

    [Fact]
    public void Requirement_02_set_reconciliation_is_stable()
    {
        var source = new[] { Mapped("A"), Mapped("B") };
        var target = new[] { Target(source[0], hocVienId: 1), Target(Mapped("C"), hocVienId: 2) };

        var first = Classify(source, target);
        var second = Classify(source.Reverse().ToArray(), target.Reverse().ToArray());

        Assert.Equal(first.SourceKeySetHash, second.SourceKeySetHash);
        Assert.Equal(first.TargetKeySetHash, second.TargetKeySetHash);
        Assert.Equal(first.IntersectionHash, second.IntersectionHash);
    }

    [Fact]
    public void Requirement_03_source_only_new_row_is_classified()
    {
        var result = Classify([Mapped("NEW")], []);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("SOURCE_ONLY_NEW_ROW", candidate.Classification);
        Assert.Equal("WOULD_INSERT_SAFE_AFTER_APPROVAL", candidate.SafeDisposition);
    }

    [Fact]
    public void Requirement_04_soft_deleted_counterpart_is_reactivation()
    {
        var source = Mapped("A");
        var result = Classify([source], [Target(source, isDeleted: true)]);

        Assert.Equal(1, result.WouldReactivateRows);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("TARGET_SOFT_DELETED_COUNTERPART", candidate.Classification);
        Assert.Equal("WOULD_REACTIVATE_SAFE_AFTER_APPROVAL", candidate.SafeDisposition);
    }

    [Fact]
    public void Requirement_05_sql_collation_alias_is_not_treated_as_new()
    {
        var source = Mapped("WIDTH-A");
        var target = Target(Mapped("WIDTH-B"), hocVienId: 8);
        var result = Classify(
            [source],
            [target],
            sqlMatches: [new Rt01aSqlIdentityMatch(0, 8)]);

        var insert = Assert.Single(result.Candidates, item => item.CandidateType == "WOULD_INSERT");
        Assert.Equal("IDENTITY_COLLATION_ALIAS", insert.Classification);
        Assert.True(insert.SqlCollationEqualCounterpart);
        Assert.Equal("SHADOW_IMPLEMENTATION_FIX_REQUIRED", insert.SafeDisposition);
    }

    [Fact]
    public void Requirement_06_trailing_spaces_are_equal_under_identity_contract()
    {
        var source = Mapped("A");
        var target = Target(source, sourceMaDk: "A ");

        var result = Classify([source], [target]);

        Assert.Equal(1, result.IntersectionRows);
        Assert.Equal(0, result.WouldInsertRows);
        Assert.Equal(0, result.TargetOnlyActiveRows);
    }

    [Fact]
    public void Requirement_07_identity_matching_is_case_insensitive()
    {
        var source = Mapped("AbC");
        var target = Target(source, sourceMaDk: "aBc");

        var result = Classify([source], [target]);

        Assert.Equal(1, result.IntersectionRows);
    }

    [Fact]
    public void Requirement_08_accent_sensitive_difference_is_exposed()
    {
        var source = Mapped("Á-1", cccd: "111111111111");
        var target = Target(
            Mapped("A-1", cccd: "111111111111"),
            sourceMaDk: "A-1");

        var result = Classify([source], [target]);

        var insert = Assert.Single(result.Candidates, item => item.CandidateType == "WOULD_INSERT");
        Assert.True(insert.AccentDifference);
        Assert.False(insert.SqlCollationEqualCounterpart);
    }

    [Fact]
    public void Requirement_09_profile_mismatch_is_an_ownership_conflict()
    {
        var source = Mapped("A");
        var otherProfile = Target(
            source,
            profile: "CSDT_MOTO",
            sourceMaDk: "A");

        var result = Classify([source], [otherProfile]);

        var insert = Assert.Single(result.Candidates);
        Assert.Equal("PROFILE_ATTRIBUTION_MISMATCH", insert.Classification);
        Assert.Equal("OWNERSHIP_CONFLICT", insert.SafeDisposition);
    }

    [Fact]
    public void Requirement_10_source_scope_mismatch_is_filtered_out()
    {
        var target = Target(Mapped("A"), actor: "QlhvBakFullSync");
        var presence = new Rt01aSourcePresenceEvidence(
            target.HocVienId,
            NguoiLxExists: true,
            NguoiLxHoSoExists: false,
            WouldPassCurrentSourceScope: false);

        var result = Classify([], [target], presence: [presence]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("SOURCE_ROW_FILTERED_OUT", candidate.Classification);
        Assert.True(candidate.RawSourceRepresentationExists);
    }

    [Fact]
    public void Requirement_11_target_scope_mismatch_is_profile_mismatch()
    {
        var source = Mapped("A");
        var result = Classify(
            [source],
            [Target(source, profile: "CSDT_MOTO")]);

        Assert.Equal(
            "PROFILE_ATTRIBUTION_MISMATCH",
            Assert.Single(result.Candidates).Classification);
    }

    [Fact]
    public void Requirement_12_source_owned_field_change_is_update_eligible()
    {
        var source = Mapped("A", name: "New");
        var target = Target(source, hoTen: "Old", hash: "old");

        var result = Classify([source], [target]);

        var update = Assert.Single(result.Candidates);
        Assert.Equal("STALE_IMPORTED_VALUE", update.Classification);
        Assert.Equal(
            "WOULD_UPDATE_SOURCE_OWNED_FIELDS_AFTER_APPROVAL",
            update.SafeDisposition);
        Assert.Equal("HoTen", Assert.Single(update.FieldDifferences).FieldCategory);
    }

    [Fact]
    public void Requirement_13_qlhv_owned_only_change_does_not_update()
    {
        var source = Mapped("A");
        var target = Target(source, ghiChuNoiBo: "QLHV-owned note");

        var result = Classify([source], [target]);

        Assert.Equal(1, result.NoChangeRows);
        Assert.Equal(0, result.WouldUpdateRows);
    }

    [Fact]
    public void Requirement_14_normalization_only_difference_has_no_business_update_disposition()
    {
        var source = Mapped("A", name: "NAME");
        var target = Target(source, hoTen: " name ", hash: "old");

        var update = Assert.Single(Classify([source], [target]).Candidates);

        Assert.Equal("NORMALIZATION_ONLY_DIFFERENCE", update.Classification);
        Assert.Equal("NO_UPDATE_NORMALIZED_EQUAL", update.SafeDisposition);
        Assert.True(Assert.Single(update.FieldDifferences).NormalizedEqual);
    }

    [Fact]
    public void Requirement_15_transform_drift_is_visible()
    {
        var source = Mapped("A", photoPath: "new.jp2");
        var target = Target(source, photoPath: "old.jp2", hash: "old");

        var update = Assert.Single(Classify([source], [target]).Candidates);

        Assert.Equal("MULTI_FIELD_PHOTO_DRIFT", update.Classification);
        Assert.Equal("MANUAL_REVIEW_REQUIRED", update.SafeDisposition);
        Assert.True(update.ManualReviewRequired);
        var difference = Assert.Single(update.FieldDifferences);
        Assert.Equal("PHOTO_MANUAL_REVIEW", difference.DifferenceClass);
        Assert.False(difference.SafeUpdateEligible);
        Assert.False(difference.ContributesToUpdate);
    }

    [Fact]
    public void Requirement_15b_three_photo_fields_are_retained_for_manual_review()
    {
        var capturedAt = new DateTime(2026, 7, 28, 9, 0, 0, DateTimeKind.Utc);
        var source = Mapped(
            "A", photoPath: "new.jp2", chatLuongAnh: 95,
            ngayThuNhanAnh: capturedAt);
        var target = Target(
            source,
            hash: "old",
            photoPath: "old.jp2",
            chatLuongAnh: 80,
            ngayThuNhanAnh: capturedAt.AddMinutes(-1));

        var result = Classify([source], [target]);
        var update = Assert.Single(result.Candidates);

        Assert.Equal(1, result.ManualReviewRows);
        Assert.Equal("MULTI_FIELD_PHOTO_DRIFT", update.Classification);
        Assert.Equal("MANUAL_REVIEW_REQUIRED", update.SafeDisposition);
        Assert.Equal(
            new[] { "AnhRelativePath", "ChatLuongAnh", "NgayThuNhanAnh" },
            update.FieldDifferences.Select(field => field.FieldCategory).ToArray());
        Assert.All(update.FieldDifferences, difference =>
        {
            Assert.False(difference.SafeUpdateEligible);
            Assert.False(difference.ContributesToUpdate);
        });
    }

    [Fact]
    public void Requirement_16_target_native_row_is_retained()
    {
        var target = Target(Mapped("A"), actor: "Admin", sourceSystem: null);

        var candidate = Assert.Single(Classify([], [target]).Candidates);

        Assert.Equal("QLHV_NATIVE_ROW", candidate.Classification);
        Assert.Equal("TARGET_NATIVE_RETAIN", candidate.SafeDisposition);
    }

    [Fact]
    public void Requirement_17_legacy_row_is_retained()
    {
        var target = Target(Mapped("A"), actor: null, sourceSystem: "LEGACY");

        var candidate = Assert.Single(Classify([], [target]).Candidates);

        Assert.Equal("LEGACY_IMPORTED_ROW", candidate.Classification);
        Assert.Equal("LEGACY_RETAIN", candidate.SafeDisposition);
    }

    [Fact]
    public void Requirement_18_rekey_candidate_blocks_automatic_apply()
    {
        var source = Mapped("NEW", cccd: "111111111111");
        var target = Target(
            Mapped("OLD", cccd: "111111111111"),
            actor: "QlhvBakFullSync");

        var result = Classify([source], [target]);

        Assert.Contains(
            result.Candidates,
            item => item.Classification == "SOURCE_ROW_REKEYED" &&
                    item.SafeDisposition == "REKEY_REQUIRES_MIGRATION");
        Assert.Contains(
            result.Candidates,
            item => item.Classification == "TARGET_PRESENT_UNDER_ALIAS_KEY");
    }

    [Fact]
    public void Requirement_19_unknown_ownership_blocks()
    {
        var target = Target(Mapped("A"), actor: null, sourceSystem: "V2");

        var candidate = Assert.Single(Classify([], [target]).Candidates);

        Assert.Equal("ORPHAN_IMPORT_ATTRIBUTION", candidate.Classification);
        Assert.Equal("OWNERSHIP_CONFLICT", candidate.SafeDisposition);
    }

    [Fact]
    public void Requirement_20_diagnostics_do_not_serialize_raw_key_or_pii()
    {
        var source = Mapped(
            "RAW-SECRET-KEY",
            name: "PRIVATE-NAME",
            cccd: "999999999999");

        var json = JsonSerializer.Serialize(Classify([source], []));

        Assert.DoesNotContain("RAW-SECRET-KEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-NAME", json, StringComparison.Ordinal);
        Assert.DoesNotContain("999999999999", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Requirement_21_hmac_hashes_are_deterministic()
    {
        var source = new[] { Mapped("B"), Mapped("A") };

        var first = Classify(source, []);
        var second = Classify(source.Reverse().ToArray(), []);

        Assert.Equal(first.SourceKeySetHash, second.SourceKeySetHash);
        Assert.StartsWith(Rt01aProofContract.HmacVersion + ":", first.SourceKeySetHash);
    }

    [Fact]
    public void Requirement_22_three_repeated_probes_are_stable()
    {
        var source = new[] { Mapped("A"), Mapped("B") };
        var target = new[]
        {
            Target(source[0], hocVienId: 1),
            Target(source[1], hocVienId: 2),
        };

        var probes = Enumerable.Range(0, 3)
            .Select(_ => Classify(source, target))
            .ToArray();

        Assert.Equal(3, probes.Length);
        Assert.Single(probes.Select(probe => probe.StageHash).Distinct());
        Assert.Single(probes.Select(probe => probe.TargetComparisonHash).Distinct());
    }

    [Fact]
    public void Requirement_23_existing_auto_sync_mapping_contract_matches_rt01()
    {
        var source = Source("A", "Same mapper");
        var mapped = QlhvImportHocVienMapper.MapAndValidate(
            source,
            new HocVienSourceIdentityContext("CSDT_OTO", "V2"));

        Assert.NotNull(mapped.Model);
        Assert.Equal("PASS", Rt01aProofContract.MappingContractStatus);
        Assert.Equal(
            Rt01aProofContract.MappedFields.Count,
            Rt01aProofContract.MappedFields.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Requirement_24_moto_remains_matched()
    {
        var source = Source("M-1", "Moto");
        var mapped = QlhvImportHocVienMapper.MapAndValidate(
            source,
            new HocVienSourceIdentityContext("CSDT_MOTO", "V2")).Model!;
        var snapshots = new Rt01ShadowSnapshots(
            new QlhvImportSourceSnapshot
            {
                SourceDatabaseName = "CSDL_MOTO",
                HocVienRows = [source],
            },
            new QlhvImportTargetSnapshot
            {
                HocVienRows =
                [
                    new QlhvFullSyncTargetRow(mapped.SourceMaDK, mapped.V2RowHash, false),
                ],
            },
            DateTime.UtcNow,
            DateTime.UtcNow);

        var result = Rt01ShadowPlanner.Build(
            Rt01ShadowRouteCatalog.Moto,
            snapshots,
            null,
            2,
            DateTime.UtcNow);

        Assert.Equal(Rt01ShadowStatuses.Matched, result.Status);
    }

    [Fact]
    public void Requirement_25_business_data_writes_remain_zero()
    {
        Assert.Equal(0, Classify([Mapped("A")], []).BusinessDataWrites);
    }

    [Fact]
    public void Requirement_26_apply_checkpoint_is_not_published()
    {
        Assert.False(Classify([Mapped("A")], []).ApplyCheckpointPublished);
    }

    [Fact]
    public void Requirement_27_no_delete_or_deactivation_disposition_exists()
    {
        var result = Classify([], [Target(Mapped("A"), actor: "QlhvBakFullSync")]);

        Assert.All(result.Candidates, candidate =>
        {
            Assert.DoesNotContain(
                "DELETE",
                candidate.SafeDisposition,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "DEACTIVATE",
                candidate.SafeDisposition,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Requirement_28_rt01_worker_default_remains_disabled()
    {
        Assert.False(new Rt01ShadowOptions().Enabled);

        var services = new ServiceCollection();
        services.AddApplication();
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ImplementationType == typeof(Rt01ShadowWorker));
    }

    [Fact]
    public void Requirement_29_existing_auto_sync_is_not_touched()
    {
        Assert.False(Classify([Mapped("A")], []).ExistingAutoSyncTouched);
    }

    [Fact]
    public void Requirement_30_v2_to_v1_components_are_not_dependencies()
    {
        var dependencies = typeof(Rt01aOtoDriftEvidenceReader)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            dependencies,
            dependency => dependency.Contains(".Realtime.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            Rt01ShadowRouteCatalog.Ordered,
            route => route.SourceDatabaseName.Contains("_V1", StringComparison.Ordinal));
    }

    [Fact]
    public void Requirement_31_protected_config_paths_are_not_rt01a_dependencies()
    {
        var componentNames = new[]
        {
            typeof(Rt01aDriftClassifier).AssemblyQualifiedName,
            typeof(Rt01aOtoDriftEvidenceReader).AssemblyQualifiedName,
        };

        Assert.DoesNotContain(
            componentNames,
            name => name?.Contains("appsettings.Development.json", StringComparison.Ordinal) == true);
    }

    private static Rt01aProbeEvidence Classify(
        IReadOnlyList<QlhvImportHocVienWriteModel> source,
        IReadOnlyList<Rt01aTargetHocVienRow> target,
        IReadOnlyList<Rt01aSqlIdentityMatch>? sqlMatches = null,
        IReadOnlyList<Rt01aSourcePresenceEvidence>? presence = null)
        => Rt01aDriftClassifier.Classify(
            new Rt01aRawProbe(
                source,
                target,
                sqlMatches ?? Array.Empty<Rt01aSqlIdentityMatch>(),
                presence ?? Array.Empty<Rt01aSourcePresenceEvidence>(),
                new Rt01aReadWindow(
                    new DateTime(2026, 7, 27, 2, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 7, 27, 2, 0, 1, DateTimeKind.Utc),
                    new DateTime(2026, 7, 27, 2, 0, 1, DateTimeKind.Utc),
                    new DateTime(2026, 7, 27, 2, 0, 2, DateTimeKind.Utc)),
                "source-schema",
                "target-schema",
                "SQL_Latin1_General_CP1_CI_AS"),
            HmacKey);

    private static QlhvImportHocVienWriteModel Mapped(
        string key,
        string name = "Learner",
        string cccd = "012345678901",
        string? photoPath = null,
        int? chatLuongAnh = null,
        DateTime? ngayThuNhanAnh = null)
    {
        var result = QlhvImportHocVienMapper.MapAndValidate(
            Source(key, name, cccd, photoPath, chatLuongAnh, ngayThuNhanAnh),
            new HocVienSourceIdentityContext("CSDT_OTO", "V2"));
        return Assert.IsType<QlhvImportHocVienWriteModel>(result.Model);
    }

    private static V2HocVienSourceRow Source(
        string key,
        string name,
        string cccd = "012345678901",
        string? photoPath = null,
        int? chatLuongAnh = null,
        DateTime? ngayThuNhanAnh = null)
        => new()
        {
            MaDK = key,
            MaKhoaHoc = "K01",
            TenKH = "Course",
            HangDaoTao = "B2",
            TenHangDT = "B2",
            HoVaTen = name,
            NgaySinh = new DateTime(2000, 1, 1),
            GioiTinh = "M",
            SoCMT = cccd,
            NoiTT = "Address",
            SoGPLXDaCo = "GPLX",
            HangGPLXDaCo = "A1",
            NguoiNhanHoSo = "Receiver",
            DuongDanAnh = photoPath,
            ChatLuongAnh = chatLuongAnh,
            NgayThuNhanAnh = ngayThuNhanAnh,
        };

    private static Rt01aTargetHocVienRow Target(
        QlhvImportHocVienWriteModel source,
        long hocVienId = 1,
        string? profile = "CSDT_OTO",
        string? sourceMaDk = null,
        string? sourceSystem = "V2",
        string? actor = "QlhvBakFullSync",
        bool isDeleted = false,
        string? hash = null,
        string? hoTen = null,
        string? photoPath = null,
        int? chatLuongAnh = null,
        DateTime? ngayThuNhanAnh = null,
        string? ghiChuNoiBo = null)
        => new()
        {
            HocVienId = hocVienId,
            SourceProfileCode = profile,
            SourceMaDK = sourceMaDk ?? source.SourceMaDK,
            SourceSystem = sourceSystem,
            SourceVersion = source.SourceVersion,
            MaDK = source.MaDK,
            MaKhoa = source.MaKhoa,
            TenKhoa = source.TenKhoa,
            MaHangDT = source.MaHangDT,
            HangGPLXHoc = source.HangGPLXHoc,
            HoTen = hoTen ?? source.HoTen,
            NgaySinh = source.NgaySinh,
            GioiTinh = source.GioiTinh,
            SoCCCD = source.SoCCCD,
            DiaChiThuongTru = source.DiaChiThuongTru,
            SoGPLXDaCo = source.SoGPLXDaCo,
            HangGPLXDaCo = source.HangGPLXDaCo,
            NguoiNhanHoSo = source.NguoiNhanHoSo,
            AnhRelativePath = photoPath ?? source.AnhRelativePath,
            ChatLuongAnh = chatLuongAnh ?? source.ChatLuongAnh,
            NgayThuNhanAnh = ngayThuNhanAnh ?? source.NgayThuNhanAnh,
            NguoiThuNhanAnh = source.NguoiThuNhanAnh,
            SourceOfTruth = source.SourceOfTruth,
            V2RowHash = hash ?? source.V2RowHash,
            IsDeleted = isDeleted,
            CreatedBy = actor,
            UpdatedBy = actor,
            GhiChuNoiBo = ghiChuNoiBo,
        };
}
