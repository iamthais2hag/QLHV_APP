using System.Data;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using QLHV.Infrastructure.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeColumnOwnershipPolicyTests
{
    private static readonly string[] ForbiddenDossierColumns =
    [
        "NoiDungSH", "MaBC2", "KetQuaBC2", "MaLyDoTCBC2", "MaKySH",
        "SoBD", "LanSH", "SoQDSH", "NgayQDSH", "KetQua_LyThuyet",
        "NhanXet_LyThuyet", "KetQuaSHM", "NhanXet_MoPhong", "KetQua_Hinh",
        "NhanXet_Hinh", "KetQua_Duong", "NhanXet_Duong", "KetQuaSH",
        "SoQDTT", "NgayQDTT", "NguoiKy", "SoGPLXTmp", "NgayKTBC2",
        "NguoiKTBC2", "MaIn", "KetQuaDoiSanhTW", "GhiChuKQDSTW", "ChuKy",
        "TT_XuLy_Old", "CHON_IN_GPLX", "KetQuaPDSo", "DAT_QDThucHanh",
        "DAT_TGThucHanh", "DAT_KQCuc", "DAT_ThoiGianLayKQ",
        "LyDoTuChoiKQDT",
    ];

    [Theory]
    [InlineData("03")]
    [InlineData("04")]
    [InlineData("09")]
    public void New_dossier_inserts_training_state_and_never_projects_V1_sat_hach_values(
        string state)
    {
        var table = DossierTable();
        var source = AddRow(
            table,
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", state),
            ("HangDaoTao", "B2"),
            ("MaKhoaHoc", "66029K260001"),
            ("MaBC2", "SOURCE-MUST-BE-IGNORED"),
            ("MaKySH", "SOURCE-KY"),
            ("KetQuaBC2", true),
            ("KetQuaSH", "1"),
            ("SoBD", "007"));

        var domain = CsdtRealtimeDomainCatalog.GetRequired("NguoiLX_HoSo");
        var plan = CsdtRealtimeForwardWritePlanner.PlanRow(domain, source, target: null);
        var inserted = CsdtRealtimeForwardWritePlanner.ProjectInsertValues(domain, plan.Row);

        Assert.True(plan.Include);
        Assert.Equal(state, inserted["TT_XuLy"]);
        Assert.Equal("B2", inserted["HangDaoTao"]);
        Assert.Equal("66029K260001", inserted["MaKhoaHoc"]);
        Assert.All(ForbiddenDossierColumns, column => Assert.DoesNotContain(column, inserted.Keys));
    }

    [Fact]
    public void Active_BCII_target_preserves_V1_lifecycle_but_accepts_safe_training_fields()
    {
        var sourceTable = DossierTable();
        var targetTable = DossierTable();
        var source = AddRow(
            sourceTable,
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", "09"),
            ("HangDaoTao", "C"),
            ("MaKhoaHoc", "66029K260099"),
            ("MaBC1", DBNull.Value),
            ("TrangThai", false),
            ("GhiChu", "source-note"),
            ("GiaiTrinh", "source-explanation"),
            ("GiayCNSK", false));
        var target = AddRow(
            targetTable,
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", "11"),
            ("HangDaoTao", "B2"),
            ("MaKhoaHoc", "66029K260001"),
            ("MaBC1", "BCI-LOCKED"),
            ("MaBC2", "BCII-LOCKED"),
            ("KetQuaBC2", true),
            ("TrangThai", true),
            ("GhiChu", "v1-note"),
            ("GiaiTrinh", "v1-explanation"),
            ("GiayCNSK", true));

        var domain = CsdtRealtimeDomainCatalog.GetRequired("NguoiLX_HoSo");
        var plan = CsdtRealtimeForwardWritePlanner.PlanRow(domain, source, target);
        var update = CsdtRealtimeForwardWritePlanner.ProjectUpdateValues(domain, plan.Row);

        Assert.True(plan.Include);
        Assert.Equal("V1_BCII_LIFECYCLE_ACTIVE", plan.Conflict?.Code);
        Assert.Equal("C", update["HangDaoTao"]);
        Assert.Equal("11", update["TT_XuLy"]);
        Assert.Equal("66029K260001", update["MaKhoaHoc"]);
        Assert.Equal("BCI-LOCKED", update["MaBC1"]);
        Assert.Equal(true, update["TrangThai"]);
        Assert.Equal("v1-note", update["GhiChu"]);
        Assert.Equal("v1-explanation", update["GiaiTrinh"]);
        Assert.Equal(true, update["GiayCNSK"]);
        Assert.All(ForbiddenDossierColumns, column => Assert.DoesNotContain(column, update.Keys));
    }

    [Theory]
    [InlineData("03")]
    [InlineData("04")]
    [InlineData("09")]
    public void Inactive_dossier_accepts_only_approved_training_states(string sourceState)
    {
        var source = AddRow(
            DossierTable(),
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", sourceState),
            ("HangDaoTao", "C"));
        var target = AddRow(
            DossierTable(),
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", "03"),
            ("HangDaoTao", "B2"));
        var domain = CsdtRealtimeDomainCatalog.GetRequired("NguoiLX_HoSo");

        var plan = CsdtRealtimeForwardWritePlanner.PlanRow(domain, source, target);
        var update = CsdtRealtimeForwardWritePlanner.ProjectUpdateValues(domain, plan.Row);

        Assert.True(plan.Include);
        Assert.Equal(sourceState, update["TT_XuLy"]);
        Assert.Equal("C", update["HangDaoTao"]);
    }

    [Fact]
    public void Dossier_BCI_relationship_is_preserved_when_target_BCII_links_to_it()
    {
        var source = AddRow(
            DossierTable(),
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", "09"),
            ("HangDaoTao", "C"),
            ("MaKhoaHoc", "KH-NEW"),
            ("MaBC1", "BCI-NEW"));
        var target = AddRow(
            DossierTable(),
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", "09"),
            ("HangDaoTao", "B2"),
            ("MaKhoaHoc", "KH-V1"),
            ("MaBC1", "BCI-V1"));
        var domain = CsdtRealtimeDomainCatalog.GetRequired("NguoiLX_HoSo");

        var plan = CsdtRealtimeForwardWritePlanner.PlanRow(
            domain,
            source,
            target,
            relationshipLocked: true);
        var update = CsdtRealtimeForwardWritePlanner.ProjectUpdateValues(domain, plan.Row);

        Assert.Equal("C", update["HangDaoTao"]);
        Assert.Equal("KH-V1", update["MaKhoaHoc"]);
        Assert.Equal("BCI-V1", update["MaBC1"]);
        Assert.Equal("BCI_RELATION_LOCKED", plan.Conflict?.Code);
    }

    [Fact]
    public void Inactive_target_rejects_source_sat_hach_state_without_failing_safe_training_update()
    {
        var sourceTable = DossierTable();
        var targetTable = DossierTable();
        var source = AddRow(
            sourceTable,
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", "16"),
            ("HangDaoTao", "C"),
            ("MaBC2", "SOURCE-BCII"));
        var target = AddRow(
            targetTable,
            ("MaDK", "66029-20260725-000001"),
            ("TT_XuLy", "04"),
            ("HangDaoTao", "B2"));

        var domain = CsdtRealtimeDomainCatalog.GetRequired("NguoiLX_HoSo");
        var plan = CsdtRealtimeForwardWritePlanner.PlanRow(domain, source, target);
        var update = CsdtRealtimeForwardWritePlanner.ProjectUpdateValues(domain, plan.Row);

        Assert.True(plan.Include);
        Assert.Equal("V1_OWNED_COLUMN", plan.Conflict?.Code);
        Assert.Equal("04", update["TT_XuLy"]);
        Assert.Equal("C", update["HangDaoTao"]);
        Assert.DoesNotContain("MaBC2", update.Keys);
    }

    [Fact]
    public void Dossier_generated_commands_are_explicit_and_exclude_keys_audit_and_V1_columns()
    {
        var metadata = Metadata(
            "NguoiLX_HoSo",
            Column("MaDK", 1, primaryKeyOrdinal: 1),
            Column("TT_XuLy", 2),
            Column("HangDaoTao", 3),
            Column("MaKhoaHoc", 4),
            Column("MaBC2", 5),
            Column("MaKySH", 6),
            Column("KetQuaSH", 7),
            Column("NguoiTao", 8),
            Column("NgayTao", 9, sqlType: "datetime"));
        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired("NguoiLX_HoSo");
        var insertColumns = policy.SelectInsertColumns(metadata);
        var updateColumns = policy.SelectUpdateColumns(metadata);
        var insertSql = CsdtRealtimeTargetWriter.BuildInsertCommandText(metadata, insertColumns);
        var updateSql = CsdtRealtimeTargetWriter.BuildUpdateCommandText(metadata, updateColumns);

        Assert.Contains("[MaDK]", insertSql, StringComparison.Ordinal);
        Assert.Contains("[TT_XuLy]", insertSql, StringComparison.Ordinal);
        Assert.Contains("[HangDaoTao]", updateSql, StringComparison.Ordinal);
        Assert.DoesNotContain("target.[MaDK] =", updateSql, StringComparison.Ordinal);
        Assert.DoesNotContain("[NguoiTao]", insertSql, StringComparison.Ordinal);
        Assert.DoesNotContain("[NgayTao]", insertSql, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "MaBC2", "MaKySH", "KetQuaSH" })
        {
            Assert.DoesNotContain($"[{forbidden}]", insertSql, StringComparison.Ordinal);
            Assert.DoesNotContain($"[{forbidden}]", updateSql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GPLX_is_preserve_only_until_provenance_policy_exists()
    {
        var table = Table(("MaDK", typeof(string)), ("SoGPLX", typeof(string)));
        var source = AddRow(
            table,
            ("MaDK", "66029-20260725-000001"),
            ("SoGPLX", "SOURCE-LICENCE"));
        var target = AddRow(
            table.Clone(),
            ("MaDK", "66029-20260725-000001"),
            ("SoGPLX", "V1-LICENCE"));
        var domain = CsdtRealtimeDomainCatalog.GetRequired("NguoiLX_GPLX");
        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name);
        var metadata = Metadata(
            "NguoiLX_GPLX",
            Column("MaDK", 1, primaryKeyOrdinal: 1),
            Column("SoGPLX", 2));

        var existing = CsdtRealtimeForwardWritePlanner.PlanRow(domain, source, target);
        var missing = CsdtRealtimeForwardWritePlanner.PlanRow(domain, source, target: null);

        Assert.False(policy.AutomaticWritesEnabled);
        Assert.Empty(policy.SelectInsertColumns(metadata));
        Assert.Empty(policy.SelectUpdateColumns(metadata));
        Assert.False(existing.Include);
        Assert.Equal("TARGET_GPLX_EXISTS", existing.Conflict?.Code);
        Assert.False(missing.Include);
        Assert.Equal("GPLX_PROVENANCE_UNCONFIRMED", missing.Conflict?.Code);
    }

    [Fact]
    public void BaoCaoI_ordinary_update_is_allowed_but_locked_relationship_is_preserved()
    {
        var sourceTable = Table(
            ("MaBCI", typeof(string)),
            ("MaKH", typeof(string)),
            ("MaCSDT", typeof(string)),
            ("SoHocSinh", typeof(int)));
        var targetTable = sourceTable.Clone();
        var source = AddRow(
            sourceTable,
            ("MaBCI", "BCI-1"),
            ("MaKH", "KH-NEW"),
            ("MaCSDT", "66029"),
            ("SoHocSinh", 20));
        var target = AddRow(
            targetTable,
            ("MaBCI", "BCI-1"),
            ("MaKH", "KH-OLD"),
            ("MaCSDT", "66030"),
            ("SoHocSinh", 10));
        var domain = CsdtRealtimeDomainCatalog.GetRequired("BaoCaoI");

        var ordinary = CsdtRealtimeForwardWritePlanner.PlanRow(
            domain,
            source,
            target,
            relationshipLocked: false);
        var locked = CsdtRealtimeForwardWritePlanner.PlanRow(
            domain,
            source,
            target,
            relationshipLocked: true);
        var orphanLinkInsert = CsdtRealtimeForwardWritePlanner.PlanRow(
            domain,
            source,
            target: null,
            relationshipLocked: true);
        var ordinaryUpdate = CsdtRealtimeForwardWritePlanner.ProjectUpdateValues(
            domain,
            ordinary.Row);
        var lockedUpdate = CsdtRealtimeForwardWritePlanner.ProjectUpdateValues(
            domain,
            locked.Row);

        Assert.Equal("KH-NEW", ordinaryUpdate["MaKH"]);
        Assert.Equal("66029", ordinaryUpdate["MaCSDT"]);
        Assert.Equal("KH-OLD", lockedUpdate["MaKH"]);
        Assert.Equal("66030", lockedUpdate["MaCSDT"]);
        Assert.Equal(20, lockedUpdate["SoHocSinh"]);
        Assert.Equal("BCI_RELATION_LOCKED", locked.Conflict?.Code);
        Assert.False(orphanLinkInsert.Include);
        Assert.Equal("BCI_RELATION_LOCKED", orphanLinkInsert.Conflict?.Code);
    }

    [Fact]
    public void Locked_course_preserves_training_update_and_document_orphan_is_skipped()
    {
        var courseTable = Table(
            ("MaKH", typeof(string)),
            ("TenKH", typeof(string)),
            ("TrangThai", typeof(bool)));
        var sourceCourse = AddRow(
            courseTable,
            ("MaKH", "KH-1"),
            ("TenKH", "Source"),
            ("TrangThai", false));
        var targetCourse = AddRow(
            courseTable.Clone(),
            ("MaKH", "KH-1"),
            ("TenKH", "Target"),
            ("TrangThai", true));
        var coursePlan = CsdtRealtimeForwardWritePlanner.PlanRow(
            CsdtRealtimeDomainCatalog.GetRequired("KhoaHoc"),
            sourceCourse,
            targetCourse,
            relationshipLocked: true);
        var courseUpdate = CsdtRealtimeForwardWritePlanner.ProjectUpdateValues(
            CsdtRealtimeDomainCatalog.GetRequired("KhoaHoc"),
            coursePlan.Row);

        var documentTable = Table(
            ("MaGT", typeof(int)),
            ("MaDK", typeof(string)),
            ("TenGT", typeof(string)));
        var document = AddRow(
            documentTable,
            ("MaGT", 1),
            ("MaDK", "66029-20260725-000001"),
            ("TenGT", "Giay to"));
        var documentPlan = CsdtRealtimeForwardWritePlanner.PlanRow(
            CsdtRealtimeDomainCatalog.GetRequired("NguoiLXHS_GiayTo"),
            document,
            target: null,
            parentExists: false);

        Assert.Equal("Target", courseUpdate["TenKH"]);
        Assert.Equal(true, courseUpdate["TrangThai"]);
        Assert.Equal("BCI_RELATION_LOCKED", coursePlan.Conflict?.Code);
        Assert.False(documentPlan.Include);
        Assert.Equal("PARENT_DOSSIER_MISSING", documentPlan.Conflict?.Code);
    }

    [Fact]
    public void New_or_unclassified_columns_fail_closed_and_V1_domains_never_enter_catalog()
    {
        var metadata = Metadata(
            "NguoiLX",
            Column("MaDK", 1, primaryKeyOrdinal: 1),
            Column("FutureColumn", 2));
        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired("NguoiLX");

        var exception = Assert.Throws<CsdtRealtimeSchemaException>(
            () => policy.SelectForwardReadColumns(metadata));

        Assert.Contains("UNCLASSIFIED_FORWARD_COLUMN", exception.Message, StringComparison.Ordinal);
        Assert.All(
            CsdtRealtimeColumnOwnershipPolicy.V1OwnedDomains,
            domain => Assert.DoesNotContain(
                CsdtRealtimeDomainCatalog.Ordered,
                item => item.Name == domain));
        Assert.Throws<ArgumentException>(() =>
            CsdtRealtimeDomainCatalog.GetRequired("BaoCaoII"));
    }

    [Fact]
    public void Every_current_V2_reference_schema_column_is_explicitly_classified()
    {
        var schema = File.ReadAllText(FindWorkspaceFile(
            "database",
            "reference",
            "V2_schema_full.sql"));
        foreach (var domain in CsdtRealtimeDomainCatalog.Ordered)
        {
            var table = Regex.Match(
                schema,
                $@"CREATE TABLE \[dbo\]\.\[{Regex.Escape(domain.TableName)}\]\((?<body>.*?)\r?\n\s*CONSTRAINT",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            Assert.True(table.Success, $"Missing V2 reference schema for {domain.TableName}.");
            var columns = Regex.Matches(
                    table.Groups["body"].Value,
                    @"(?m)^\s*\[([^\]]+)\]\s+\[",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Groups[1].Value)
                .ToArray();
            var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name);

            Assert.All(columns, column => Assert.NotNull(policy.GetRequired(column)));
        }
    }

    [Fact]
    public void Forward_regressions_keep_commit_order_tombstones_route_isolation_and_reverse_reader()
    {
        var processor = ReadInfrastructure("CsdtRealtimeStreamProcessor.cs");
        var runtime = ReadInfrastructure("CsdtRealtimeRuntimeRepository.cs");
        var writer = ReadInfrastructure("CsdtRealtimeTargetWriter.cs");
        var reverse = ReadInfrastructure("CsdtReversePlanRepository.cs");
        var completeDomain = Section(
            runtime,
            "internal async Task CompleteDomainAsync",
            "internal async Task CompleteNoChangeDomainAsync");
        var forwardLoop = Section(
            processor,
            "private async Task RunForwardAsync",
            "private async Task RunReverseAsync");
        var targetWriteIndex = forwardLoop.IndexOf(
            "var write = await WriteForwardAsync(",
            StringComparison.Ordinal);
        var checkpointIndex = forwardLoop.IndexOf(
            "await _state.CompleteDomainAsync(",
            targetWriteIndex,
            StringComparison.Ordinal);

        Assert.Contains("await transaction.CommitAsync(cancellationToken);", writer, StringComparison.Ordinal);
        Assert.True(targetWriteIndex >= 0 && checkpointIndex > targetWriteIndex);
        Assert.Contains("PersistSourceIdentitiesAndTombstonesAsync(", completeDomain, StringComparison.Ordinal);
        Assert.Contains(
            "EntityKey = BuildDiagnosticIdentity(conflict.KeyJson)",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains("Columns = conflict.Columns ?? []", runtime, StringComparison.Ordinal);
        Assert.Contains("ownership decision Stream={StreamCode}", processor, StringComparison.Ordinal);
        Assert.Contains("sha256:", processor, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM dbo.[", writer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OTO_V2_TO_V1", ReadApplication("CsdtRealtimeCatalog.cs"), StringComparison.Ordinal);
        Assert.Contains("MOTO_V2_TO_V1", ReadApplication("CsdtRealtimeCatalog.cs"), StringComparison.Ordinal);
        Assert.Contains("_reader.ReadPartitionSnapshotAsync(", reverse, StringComparison.Ordinal);
        Assert.DoesNotContain("_reader.ReadForwardPartitionSnapshotAsync(", reverse, StringComparison.Ordinal);
    }

    private static DataTable DossierTable()
        => Table(
            ("MaDK", typeof(string)),
            ("TT_XuLy", typeof(string)),
            ("HangDaoTao", typeof(string)),
            ("MaKhoaHoc", typeof(string)),
            ("MaBC1", typeof(string)),
            ("MaBC2", typeof(string)),
            ("MaKySH", typeof(string)),
            ("KetQuaBC2", typeof(bool)),
            ("KetQuaSH", typeof(string)),
            ("SoBD", typeof(string)),
            ("TrangThai", typeof(bool)),
            ("GhiChu", typeof(string)),
            ("GiaiTrinh", typeof(string)),
            ("GiayCNSK", typeof(bool)));

    private static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable
        {
            CaseSensitive = true,
            Locale = System.Globalization.CultureInfo.InvariantCulture,
        };
        foreach (var (name, type) in columns)
        {
            table.Columns.Add(name, type);
        }

        return table;
    }

    private static DataRow AddRow(DataTable table, params (string Name, object Value)[] values)
    {
        var row = table.NewRow();
        foreach (var (name, value) in values)
        {
            row[name] = value;
        }

        table.Rows.Add(row);
        return row;
    }

    private static CsdtRealtimeTableMetadata Metadata(
        string domain,
        params CsdtRealtimeColumnMetadata[] columns)
        => new(CsdtRealtimeDomainCatalog.GetRequired(domain), columns);

    private static CsdtRealtimeColumnMetadata Column(
        string name,
        int columnId,
        int? primaryKeyOrdinal = null,
        string sqlType = "varchar")
        => new(
            name,
            sqlType,
            MaxLength: sqlType == "datetime" ? (short)8 : (short)100,
            Precision: 0,
            Scale: 0,
            IsNullable: true,
            IsIdentity: false,
            IsComputed: false,
            HasDefault: false,
            ColumnId: columnId,
            PrimaryKeyOrdinal: primaryKeyOrdinal);

    private static string ReadInfrastructure(
        string file,
        [CallerFilePath] string sourceFile = "")
        => File.ReadAllText(Path.Combine(
            FindServerRoot(sourceFile),
            "QLHV.Infrastructure",
            "Sync",
            "Realtime",
            file));

    private static string ReadApplication(
        string file,
        [CallerFilePath] string sourceFile = "")
        => File.ReadAllText(Path.Combine(
            FindServerRoot(sourceFile),
            "QLHV.Application",
            "Sync",
            "Realtime",
            file));

    private static string FindServerRoot(string sourceFile)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QLHV.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string FindWorkspaceFile(
        params string[] segments)
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

        throw new FileNotFoundException(Path.Combine(segments));
    }

    private static string Section(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex);
        return source[startIndex..endIndex];
    }
}
