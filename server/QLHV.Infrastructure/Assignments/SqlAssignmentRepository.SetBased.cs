using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Assignments;

namespace QLHV.Infrastructure.Assignments;

public sealed partial class SqlAssignmentRepository
{
    /// <summary>
    /// Stages the sealed Excel plan into a connection-local table and acquires all
    /// business-identity, current-snapshot, group and reference locks in a fixed
    /// order.  The caller owns a SERIALIZABLE transaction, so successful return
    /// means every READY and genuine NO_CHANGE target remains stable until commit.
    /// </summary>
    private async Task StageAndValidateImportTargetsSetBasedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseIdentityRow course,
        AssignmentImportPlan plan,
        CancellationToken cancellationToken)
    {
        var targets=ValidateImportPlanShape(plan,course);
        await connection.ExecuteAsync(Command(CreateImportTargetStageSql,null,
            cancellationToken,transaction));

        using(var table=CreateImportTargetDataTable(targets))
        using(var bulkCopy=new SqlBulkCopy(
                  connection,
                  SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock,
                  transaction))
        {
            bulkCopy.DestinationTableName="#AssignmentImportTarget";
            bulkCopy.BatchSize=Math.Min(1_000,Math.Max(1,targets.Count));
            bulkCopy.BulkCopyTimeout=_options.TimeoutSeconds;
            bulkCopy.EnableStreaming=true;
            foreach(DataColumn column in table.Columns)
                bulkCopy.ColumnMappings.Add(column.ColumnName,column.ColumnName);
            await bulkCopy.WriteToServerAsync(table,cancellationToken);
        }

        var failure=await connection.QuerySingleOrDefaultAsync<SetBasedGuardFailure>(Command(
            ValidateImportTargetStageSql,
            new
            {
                CourseId=course.KhoaHocId,
                CourseCode=course.MaKhoa,
                course.SourceProfileCode,
                ExpectedCount=targets.Count,
            },
            cancellationToken,
            transaction));
        if(failure is not null)
            throw new AssignmentDomainException(failure.Code,failure.Message,409);
    }

    /// <summary>
    /// Applies every READY Excel target after the set-based guard has succeeded.
    /// Closing current rows, inserting replacement snapshots and writing one
    /// non-PII audit record per learner are performed by one SQL batch.
    /// </summary>
    private async Task<int> ApplyImportTargetsSetBasedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseIdentityRow course,
        long importSessionId,
        string actor,
        string reason,
        string operationId,
        DateTime transactionAtUtc,
        CancellationToken cancellationToken)
    {
        var changed=await connection.ExecuteScalarAsync<int>(Command(
            ApplyImportTargetsSql,
            new
            {
                CourseId=course.KhoaHocId,
                course.SourceProfileCode,
                ImportSessionId=importSessionId,
                Actor=actor,
                Reason=reason,
                OperationId=operationId,
                TransactionAtUtc=transactionAtUtc,
            },
            cancellationToken,
            transaction));
        return changed;
    }

    private static IReadOnlyList<AssignmentMutationTarget> ValidateImportPlanShape(
        AssignmentImportPlan plan,
        CourseIdentityRow course)
    {
        if(plan.Rows.Count is <1 or >AssignmentRules.MaxImportRows)
            throw new AssignmentDomainException(Conflict,"Sealed import plan has an invalid row count.",409);
        if(plan.CourseId!=course.KhoaHocId ||
           !string.Equals(plan.CourseCode,course.MaKhoa,StringComparison.Ordinal) ||
           !string.Equals(plan.SourceProfileCode,course.SourceProfileCode,StringComparison.Ordinal))
            throw new AssignmentDomainException(Conflict,"Sealed import scope no longer matches the locked course.",409);

        var targets=new List<AssignmentMutationTarget>(plan.Rows.Count);
        foreach(var row in plan.Rows)
        {
            if(row.Status is not (Ready or NoChange))
                throw new AssignmentDomainException(Conflict,
                    $"Import row {row.RowNumber} is {row.Status}; no learner row was written.",409);
            if(row.Target is null)
            {
                if(row.Status==Ready)
                    throw new AssignmentDomainException(Conflict,
                        $"READY import row {row.RowNumber} has no sealed target.",409);
                continue;
            }

            var target=row.Target;
            if(target.Status!=row.Status ||
               !string.Equals(target.RegistrationCode,row.RegistrationCode,StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(target.CourseCode,course.MaKhoa,StringComparison.Ordinal) ||
               !string.Equals(target.SourceProfileCode,course.SourceProfileCode,StringComparison.Ordinal) ||
               target.HocVienId<=0 || string.IsNullOrWhiteSpace(target.RegistrationCode) ||
               target.RegistrationCode.Length>50 || target.LearnerRowVersion.Length!=8)
                throw new AssignmentDomainException(Conflict,
                    $"Import row {row.RowNumber} has an invalid sealed identity.",409);

            var hasCurrent=target.CurrentAssignmentId.HasValue;
            if(hasCurrent!=(target.CurrentAssignmentRowVersion is not null) ||
               hasCurrent!=(target.Before is not null) ||
               target.CurrentAssignmentRowVersion is { Length: not 8 })
                throw new AssignmentDomainException(Conflict,
                    $"Import row {row.RowNumber} has an inconsistent sealed current snapshot.",409);
            if(target.After is not null && !target.After.HasAnyValue)
                throw new AssignmentDomainException(Conflict,
                    $"Import row {row.RowNumber} contains an empty replacement snapshot.",409);
            if(row.Status==Ready && SnapshotsEqual(target.Before,target.After) ||
               row.Status==NoChange && !SnapshotsEqual(target.Before,target.After))
                throw new AssignmentDomainException(Conflict,
                    $"Import row {row.RowNumber} status does not match its sealed snapshots.",409);

            ValidateImportTargetGroupShape(target,row.RowNumber,course.KhoaHocId);
            targets.Add(target);
        }

        if(targets.Count==0)
            throw new AssignmentDomainException(Conflict,"Sealed import plan contains no revalidatable target.",409);
        if(targets.GroupBy(target=>target.HocVienId).Any(group=>group.Count()!=1) ||
           targets.GroupBy(target=>target.RegistrationCode,StringComparer.OrdinalIgnoreCase)
               .Any(group=>group.Count()!=1))
            throw new AssignmentDomainException(Conflict,
                "Sealed import plan contains duplicate learner identities.",409);

        var targetRegistrationCodes=targets.Select(target=>target.RegistrationCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(plan.Rows.Any(row=>row.Target is null &&
                              !targetRegistrationCodes.Contains(row.RegistrationCode)))
            throw new AssignmentDomainException(Conflict,
                "A targetless NO_CHANGE row is not an identical duplicate of a sealed learner identity.",409);

        foreach(var group in targets.Where(target=>target.GroupDefaults is not null)
                    .Select(target=>target.GroupDefaults!).GroupBy(item=>item.GroupId))
        {
            var expected=group.First();
            if(group.Skip(1).Any(item=>!SealedGroupEquals(item,expected)))
                throw new AssignmentDomainException(Conflict,
                    $"Sealed import plan contains inconsistent defaults for group {group.Key}.",409);
        }
        return targets.OrderBy(target=>target.HocVienId).ToArray();
    }

    private static void ValidateImportTargetGroupShape(
        AssignmentMutationTarget target,
        int rowNumber,
        long courseId)
    {
        var after=target.After;
        if(after is null)
        {
            if(target.GroupDefaults is not null)
                throw new AssignmentDomainException(Conflict,
                    $"Import row {rowNumber} seals group defaults without an after snapshot.",409);
            return;
        }
        if(!after.GroupId.HasValue)
        {
            if(target.GroupDefaults is not null || !after.OverrideClassTeacher ||
               !after.OverrideTrainingVehicle || !after.OverrideFigure10Vehicle)
                throw new AssignmentDomainException(Conflict,
                    $"Import row {rowNumber} has an invalid no-group override state.",409);
            return;
        }

        var group=target.GroupDefaults;
        if(group is null || group.GroupId!=after.GroupId.Value || group.CourseId!=courseId ||
           group.RowVersion.Length!=8 || string.IsNullOrWhiteSpace(group.Status))
            throw new AssignmentDomainException(Conflict,
                $"Import row {rowNumber} does not carry the exact sealed group defaults.",409);
        if(!after.OverrideClassTeacher && after.ClassTeacherId!=group.ClassTeacherId ||
           !after.OverrideTrainingVehicle && after.TrainingVehicleId!=group.TrainingVehicleId ||
           !after.OverrideFigure10Vehicle && after.Figure10VehicleId!=group.Figure10VehicleId)
            throw new AssignmentDomainException(Conflict,
                $"Import row {rowNumber} inherited values differ from its sealed group defaults.",409);
    }

    private static DataTable CreateImportTargetDataTable(
        IReadOnlyList<AssignmentMutationTarget> targets)
    {
        var table=new DataTable { Locale=System.Globalization.CultureInfo.InvariantCulture };
        AddColumn<int>(table,"Ordinal");
        AddColumn<long>(table,"HocVienId");
        AddColumn<string>(table,"RegistrationCode");
        AddColumn<string>(table,"TargetCourseCode");
        AddColumn<string>(table,"TargetSourceProfileCode");
        AddColumn<byte[]>(table,"LearnerRowVersion");
        AddColumn<long>(table,"CurrentAssignmentId");
        AddColumn<byte[]>(table,"CurrentAssignmentRowVersion");
        AddColumn<bool>(table,"BeforePresent");
        AddSnapshotColumns(table,"Before");
        AddColumn<bool>(table,"AfterPresent");
        AddSnapshotColumns(table,"After");
        AddColumn<bool>(table,"SealedGroupPresent");
        AddColumn<long>(table,"SealedGroupId");
        AddColumn<long>(table,"SealedGroupCourseId");
        AddColumn<long>(table,"SealedGroupTeacherId");
        AddColumn<long>(table,"SealedGroupVehicleId");
        AddColumn<long>(table,"SealedGroupFigure10Id");
        AddColumn<string>(table,"SealedGroupStatus");
        AddColumn<byte[]>(table,"SealedGroupRowVersion");
        AddColumn<string>(table,"Status");

        for(var index=0;index<targets.Count;index++)
        {
            var target=targets[index];
            var row=table.NewRow();
            row["Ordinal"]=index+1;
            row["HocVienId"]=target.HocVienId;
            row["RegistrationCode"]=target.RegistrationCode;
            row["TargetCourseCode"]=target.CourseCode;
            row["TargetSourceProfileCode"]=target.SourceProfileCode;
            row["LearnerRowVersion"]=target.LearnerRowVersion;
            row["CurrentAssignmentId"]=DbValue(target.CurrentAssignmentId);
            row["CurrentAssignmentRowVersion"]=DbValue(target.CurrentAssignmentRowVersion);
            row["BeforePresent"]=target.Before is not null;
            SetSnapshotValues(row,"Before",target.Before);
            row["AfterPresent"]=target.After is not null;
            SetSnapshotValues(row,"After",target.After);
            row["SealedGroupPresent"]=target.GroupDefaults is not null;
            row["SealedGroupId"]=DbValue(target.GroupDefaults?.GroupId);
            row["SealedGroupCourseId"]=DbValue(target.GroupDefaults?.CourseId);
            row["SealedGroupTeacherId"]=DbValue(target.GroupDefaults?.ClassTeacherId);
            row["SealedGroupVehicleId"]=DbValue(target.GroupDefaults?.TrainingVehicleId);
            row["SealedGroupFigure10Id"]=DbValue(target.GroupDefaults?.Figure10VehicleId);
            row["SealedGroupStatus"]=DbValue(target.GroupDefaults?.Status);
            row["SealedGroupRowVersion"]=DbValue(target.GroupDefaults?.RowVersion);
            row["Status"]=target.Status;
            table.Rows.Add(row);
        }
        return table;
    }

    private static void AddSnapshotColumns(DataTable table,string prefix)
    {
        AddColumn<long>(table,$"{prefix}GroupId");
        AddColumn<long>(table,$"{prefix}DossierId");
        AddColumn<long>(table,$"{prefix}TeacherId");
        AddColumn<long>(table,$"{prefix}VehicleId");
        AddColumn<long>(table,$"{prefix}Figure10Id");
        AddColumn<bool>(table,$"{prefix}TeacherOverride");
        AddColumn<bool>(table,$"{prefix}VehicleOverride");
        AddColumn<bool>(table,$"{prefix}Figure10Override");
    }

    private static void SetSnapshotValues(DataRow row,string prefix,AssignmentSnapshot? snapshot)
    {
        row[$"{prefix}GroupId"]=DbValue(snapshot?.GroupId);
        row[$"{prefix}DossierId"]=DbValue(snapshot?.DossierReceiverId);
        row[$"{prefix}TeacherId"]=DbValue(snapshot?.ClassTeacherId);
        row[$"{prefix}VehicleId"]=DbValue(snapshot?.TrainingVehicleId);
        row[$"{prefix}Figure10Id"]=DbValue(snapshot?.Figure10VehicleId);
        row[$"{prefix}TeacherOverride"]=DbValue(snapshot?.OverrideClassTeacher);
        row[$"{prefix}VehicleOverride"]=DbValue(snapshot?.OverrideTrainingVehicle);
        row[$"{prefix}Figure10Override"]=DbValue(snapshot?.OverrideFigure10Vehicle);
    }

    private static void AddColumn<T>(DataTable table,string name) =>
        table.Columns.Add(new DataColumn(name,typeof(T)) { AllowDBNull=true });

    private static object DbValue(object? value)=>value ?? DBNull.Value;

    private const string CreateImportTargetStageSql="""
        CREATE TABLE #AssignmentImportTarget
        (
            Ordinal INT NOT NULL PRIMARY KEY CLUSTERED,
            HocVienId BIGINT NOT NULL,
            RegistrationCode NVARCHAR(50) NOT NULL,
            TargetCourseCode NVARCHAR(50) NOT NULL,
            TargetSourceProfileCode NVARCHAR(50) NOT NULL,
            LearnerRowVersion BINARY(8) NOT NULL,
            CurrentAssignmentId BIGINT NULL,
            CurrentAssignmentRowVersion BINARY(8) NULL,
            BeforePresent BIT NOT NULL,
            BeforeGroupId BIGINT NULL,BeforeDossierId BIGINT NULL,BeforeTeacherId BIGINT NULL,
            BeforeVehicleId BIGINT NULL,BeforeFigure10Id BIGINT NULL,
            BeforeTeacherOverride BIT NULL,BeforeVehicleOverride BIT NULL,BeforeFigure10Override BIT NULL,
            AfterPresent BIT NOT NULL,
            AfterGroupId BIGINT NULL,AfterDossierId BIGINT NULL,AfterTeacherId BIGINT NULL,
            AfterVehicleId BIGINT NULL,AfterFigure10Id BIGINT NULL,
            AfterTeacherOverride BIT NULL,AfterVehicleOverride BIT NULL,AfterFigure10Override BIT NULL,
            SealedGroupPresent BIT NOT NULL,
            SealedGroupId BIGINT NULL,SealedGroupCourseId BIGINT NULL,
            SealedGroupTeacherId BIGINT NULL,SealedGroupVehicleId BIGINT NULL,
            SealedGroupFigure10Id BIGINT NULL,SealedGroupStatus VARCHAR(20) NULL,
            SealedGroupRowVersion BINARY(8) NULL,
            Status VARCHAR(20) NOT NULL
        );
        CREATE INDEX IX_AssignmentImportTarget_BusinessIdentity
            ON #AssignmentImportTarget(RegistrationCode,HocVienId);
        CREATE INDEX IX_AssignmentImportTarget_Group
            ON #AssignmentImportTarget(SealedGroupId,Ordinal)
            WHERE SealedGroupPresent=1;
        """;

    private const string ValidateImportTargetStageSql="""
        SET NOCOUNT ON;

        /* Fixed acquisition order matches the manual confirm path: groups,
           learner business keys, current snapshots, dossier receivers, teachers,
           then vehicles. MAXDOP 1 and the
           ordered TOP materializations keep key acquisition deterministic. */
        CREATE TABLE #LockedGroup
        (
            GroupId BIGINT NOT NULL,KhoaHocId BIGINT NOT NULL,GiaoVienDungLopId BIGINT NULL,
            XeTapId BIGINT NULL,XeBaiSo10Id BIGINT NULL,TrangThai VARCHAR(20) NOT NULL,
            RowVersion BINARY(8) NOT NULL
        );
        INSERT #LockedGroup
        SELECT TOP (2147483647) n.NhomDaoTaoId,n.KhoaHocId,n.GiaoVienDungLopId,
               n.XeTapId,n.XeBaiSo10Id,n.TrangThai,n.RowVersion
        FROM (SELECT DISTINCT SealedGroupId AS GroupId FROM #AssignmentImportTarget
              WHERE SealedGroupPresent=1) AS requested
        INNER LOOP JOIN dbo.App_KhoaHoc_NhomDaoTao AS n WITH (UPDLOCK,HOLDLOCK)
          ON n.NhomDaoTaoId=requested.GroupId
        ORDER BY n.NhomDaoTaoId
        OPTION (FORCE ORDER,MAXDOP 1);
        CREATE UNIQUE CLUSTERED INDEX IX_LockedGroup_Id ON #LockedGroup(GroupId);

        CREATE TABLE #LockedLearner
        (
            Ordinal INT NOT NULL,HocVienId BIGINT NOT NULL,MaDK NVARCHAR(50) NOT NULL,
            MaKhoa NVARCHAR(50) NOT NULL,SourceProfileCode NVARCHAR(50) NOT NULL,
            RowVersion BINARY(8) NOT NULL
        );
        INSERT #LockedLearner(Ordinal,HocVienId,MaDK,MaKhoa,SourceProfileCode,RowVersion)
        SELECT TOP (2147483647) t.Ordinal,h.HocVienId,h.MaDK,h.MaKhoa,h.SourceProfileCode,h.RowVersion
        FROM #AssignmentImportTarget AS t
        INNER LOOP JOIN dbo.App_HocVien AS h WITH (UPDLOCK,HOLDLOCK)
          ON h.IsDeleted=0 AND h.MaDK=t.RegistrationCode
         AND h.MaKhoa=@CourseCode AND h.SourceProfileCode=@SourceProfileCode
        ORDER BY t.RegistrationCode,h.HocVienId,t.Ordinal
        OPTION (FORCE ORDER,MAXDOP 1);
        CREATE INDEX IX_LockedLearner_Ordinal ON #LockedLearner(Ordinal,HocVienId);

        CREATE TABLE #LockedCurrent
        (
            Ordinal INT NOT NULL,PhanCongId BIGINT NOT NULL,HocVienId BIGINT NOT NULL,
            NhomDaoTaoId BIGINT NULL,GiaoVienHoSoId BIGINT NULL,GiaoVienDungLopId BIGINT NULL,
            XeTapId BIGINT NULL,XeBaiSo10Id BIGINT NULL,
            IsGiaoVienDungLopOverride BIT NOT NULL,IsXeTapOverride BIT NOT NULL,
            IsXeBaiSo10Override BIT NOT NULL,NgayHieuLuc DATETIME2(7) NOT NULL,
            RowVersion BINARY(8) NOT NULL
        );
        INSERT #LockedCurrent
            (Ordinal,PhanCongId,HocVienId,NhomDaoTaoId,GiaoVienHoSoId,GiaoVienDungLopId,
             XeTapId,XeBaiSo10Id,IsGiaoVienDungLopOverride,IsXeTapOverride,
             IsXeBaiSo10Override,NgayHieuLuc,RowVersion)
        SELECT TOP (2147483647) t.Ordinal,pc.PhanCongId,pc.HocVienId,pc.NhomDaoTaoId,
               pc.GiaoVienHoSoId,pc.GiaoVienDungLopId,pc.XeTapId,pc.XeBaiSo10Id,
               pc.IsGiaoVienDungLopOverride,pc.IsXeTapOverride,pc.IsXeBaiSo10Override,
               pc.NgayHieuLuc,pc.RowVersion
        FROM #AssignmentImportTarget AS t
        INNER LOOP JOIN dbo.App_HocVien_PhanCong AS pc WITH (UPDLOCK,HOLDLOCK)
          ON pc.HocVienId=t.HocVienId AND pc.IsCurrent=1
        ORDER BY t.HocVienId,pc.PhanCongId
        OPTION (FORCE ORDER,MAXDOP 1);
        CREATE INDEX IX_LockedCurrent_Ordinal ON #LockedCurrent(Ordinal,PhanCongId);

        CREATE TABLE #LockedDossier
            (Id BIGINT NOT NULL,TrangThai VARCHAR(20) NOT NULL,IsDeleted BIT NOT NULL);
        INSERT #LockedDossier
        SELECT TOP (2147483647) gh.GiaoVienHsId,gh.TrangThai,gh.IsDeleted
        FROM (SELECT DISTINCT AfterDossierId AS Id FROM #AssignmentImportTarget
              WHERE AfterPresent=1 AND AfterDossierId IS NOT NULL) AS requested
        INNER LOOP JOIN dbo.App_GiaoVien_hs AS gh WITH (UPDLOCK,HOLDLOCK)
          ON gh.GiaoVienHsId=requested.Id
        ORDER BY gh.GiaoVienHsId
        OPTION (FORCE ORDER,MAXDOP 1);
        CREATE UNIQUE CLUSTERED INDEX IX_LockedDossier_Id ON #LockedDossier(Id);

        CREATE TABLE #LockedTeacher
            (Id BIGINT NOT NULL,SourceProfileCode NVARCHAR(50) NULL,IsDeleted BIT NOT NULL,SourceActive BIT NOT NULL);
        INSERT #LockedTeacher
        SELECT TOP (2147483647) g.GiaoVienId,g.SourceProfileCode,g.IsDeleted,
               CONVERT(bit,COALESCE(g.TrangThaiNguon,1))
        FROM (SELECT DISTINCT AfterTeacherId AS Id FROM #AssignmentImportTarget
              WHERE AfterPresent=1 AND AfterTeacherId IS NOT NULL) AS requested
        INNER LOOP JOIN dbo.App_GiaoVien AS g WITH (UPDLOCK,HOLDLOCK)
          ON g.GiaoVienId=requested.Id
        ORDER BY g.GiaoVienId
        OPTION (FORCE ORDER,MAXDOP 1);
        CREATE UNIQUE CLUSTERED INDEX IX_LockedTeacher_Id ON #LockedTeacher(Id);

        CREATE TABLE #LockedVehicle
        (
            Id BIGINT NOT NULL,SourceProfileCode NVARCHAR(50) NULL,IsDeleted BIT NOT NULL,
            SourceLifecycle VARCHAR(20) NULL,SourceActive BIT NOT NULL
        );
        INSERT #LockedVehicle
        SELECT TOP (2147483647) x.XeTapId,x.SourceProfileCode,x.IsDeleted,x.SourceLifecycle,
               CONVERT(bit,COALESCE(x.SourceTrangThai,1))
        FROM
        (
            SELECT AfterVehicleId AS Id FROM #AssignmentImportTarget
            WHERE AfterPresent=1 AND AfterVehicleId IS NOT NULL
            UNION
            SELECT AfterFigure10Id FROM #AssignmentImportTarget
            WHERE AfterPresent=1 AND AfterFigure10Id IS NOT NULL
        ) AS requested
        INNER LOOP JOIN dbo.App_XeTap AS x WITH (UPDLOCK,HOLDLOCK)
          ON x.XeTapId=requested.Id
        ORDER BY x.XeTapId
        OPTION (FORCE ORDER,MAXDOP 1);
        CREATE UNIQUE CLUSTERED INDEX IX_LockedVehicle_Id ON #LockedVehicle(Id);

        SELECT TOP (1) violation.Code,violation.Message
        FROM
        (
            SELECT 10 AS Priority,N'CONFLICT' AS Code,
                   N'Set-based stage row count differs from the sealed plan.' AS Message
            WHERE (SELECT COUNT_BIG(1) FROM #AssignmentImportTarget)<>@ExpectedCount

            UNION ALL
            SELECT 20,N'CONFLICT',N'Sealed import targets contain duplicate learner identities.'
            WHERE EXISTS(SELECT 1 FROM #AssignmentImportTarget GROUP BY HocVienId HAVING COUNT_BIG(1)>1)
               OR EXISTS(SELECT 1 FROM #AssignmentImportTarget GROUP BY RegistrationCode HAVING COUNT_BIG(1)>1)

            UNION ALL
            SELECT 30,N'CONFLICT',N'A sealed import target has the wrong course or source profile.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget
                WHERE TargetCourseCode<>@CourseCode
                   OR TargetSourceProfileCode COLLATE Latin1_General_100_BIN2
                      <> @SourceProfileCode COLLATE Latin1_General_100_BIN2
            )

            UNION ALL
            SELECT 40,N'CONFLICT',N'A learner business identity changed or became ambiguous after preview.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget AS t
                WHERE (SELECT COUNT_BIG(1) FROM #LockedLearner AS l WHERE l.Ordinal=t.Ordinal)<>1
                   OR NOT EXISTS
                   (
                       SELECT 1 FROM #LockedLearner AS l
                       WHERE l.Ordinal=t.Ordinal AND l.HocVienId=t.HocVienId
                         AND l.RowVersion=t.LearnerRowVersion
                         AND l.MaKhoa=@CourseCode
                         AND l.SourceProfileCode COLLATE Latin1_General_100_BIN2
                            =@SourceProfileCode COLLATE Latin1_General_100_BIN2
                   )
            )

            UNION ALL
            SELECT 50,N'CONFLICT',N'A current learner assignment changed or became ambiguous after preview.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget AS t
                WHERE
                  (t.CurrentAssignmentId IS NULL AND EXISTS(
                      SELECT 1 FROM #LockedCurrent AS c WHERE c.Ordinal=t.Ordinal))
                  OR
                  (t.CurrentAssignmentId IS NOT NULL AND
                   (
                       (SELECT COUNT_BIG(1) FROM #LockedCurrent AS c WHERE c.Ordinal=t.Ordinal)<>1
                       OR NOT EXISTS
                       (
                           SELECT 1 FROM #LockedCurrent AS c
                           WHERE c.Ordinal=t.Ordinal
                             AND c.PhanCongId=t.CurrentAssignmentId
                             AND c.RowVersion=t.CurrentAssignmentRowVersion
                             AND (c.NhomDaoTaoId=t.BeforeGroupId OR (c.NhomDaoTaoId IS NULL AND t.BeforeGroupId IS NULL))
                             AND (c.GiaoVienHoSoId=t.BeforeDossierId OR (c.GiaoVienHoSoId IS NULL AND t.BeforeDossierId IS NULL))
                             AND (c.GiaoVienDungLopId=t.BeforeTeacherId OR (c.GiaoVienDungLopId IS NULL AND t.BeforeTeacherId IS NULL))
                             AND (c.XeTapId=t.BeforeVehicleId OR (c.XeTapId IS NULL AND t.BeforeVehicleId IS NULL))
                             AND (c.XeBaiSo10Id=t.BeforeFigure10Id OR (c.XeBaiSo10Id IS NULL AND t.BeforeFigure10Id IS NULL))
                             AND c.IsGiaoVienDungLopOverride=t.BeforeTeacherOverride
                             AND c.IsXeTapOverride=t.BeforeVehicleOverride
                             AND c.IsXeBaiSo10Override=t.BeforeFigure10Override
                       )
                   ))
            )

            UNION ALL
            SELECT 60,N'CONFLICT',N'Group defaults changed after preview or no longer belong to the course.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget AS t
                LEFT JOIN #LockedGroup AS g ON g.GroupId=t.SealedGroupId
                WHERE t.SealedGroupPresent=1 AND
                (
                    g.GroupId IS NULL OR g.KhoaHocId<>@CourseId OR
                    g.RowVersion<>t.SealedGroupRowVersion OR
                    g.TrangThai<>t.SealedGroupStatus OR
                    NOT(g.GiaoVienDungLopId=t.SealedGroupTeacherId OR
                        (g.GiaoVienDungLopId IS NULL AND t.SealedGroupTeacherId IS NULL)) OR
                    NOT(g.XeTapId=t.SealedGroupVehicleId OR
                        (g.XeTapId IS NULL AND t.SealedGroupVehicleId IS NULL)) OR
                    NOT(g.XeBaiSo10Id=t.SealedGroupFigure10Id OR
                        (g.XeBaiSo10Id IS NULL AND t.SealedGroupFigure10Id IS NULL))
                )
            )

            UNION ALL
            SELECT 70,N'INACTIVE_REFERENCE',N'A group required by SET or an inheritance transition is not active.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget AS t
                LEFT JOIN #LockedGroup AS g ON g.GroupId=t.AfterGroupId
                WHERE t.AfterPresent=1 AND t.AfterGroupId IS NOT NULL AND
                (
                    g.GroupId IS NULL OR g.KhoaHocId<>@CourseId OR
                    (
                        t.BeforePresent=0 OR t.BeforeGroupId IS NULL OR t.BeforeGroupId<>t.AfterGroupId OR
                        (t.AfterTeacherOverride=0 AND COALESCE(t.BeforeTeacherOverride,1)<>0) OR
                        (t.AfterVehicleOverride=0 AND COALESCE(t.BeforeVehicleOverride,1)<>0) OR
                        (t.AfterFigure10Override=0 AND COALESCE(t.BeforeFigure10Override,1)<>0)
                    ) AND g.TrangThai<>'ACTIVE'
                )
            )

            UNION ALL
            SELECT 80,N'INACTIVE_REFERENCE',N'A changed dossier receiver is no longer active.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget AS t
                LEFT JOIN #LockedDossier AS d ON d.Id=t.AfterDossierId
                WHERE t.AfterPresent=1 AND t.AfterDossierId IS NOT NULL
                  AND (t.BeforePresent=0 OR t.BeforeDossierId IS NULL OR t.BeforeDossierId<>t.AfterDossierId)
                  AND (d.Id IS NULL OR d.IsDeleted=1 OR d.TrangThai<>'ACTIVE')
            )

            UNION ALL
            SELECT 90,N'INACTIVE_REFERENCE',N'A changed or newly inherited teacher is inactive or belongs to another source profile.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget AS t
                LEFT JOIN #LockedTeacher AS g ON g.Id=t.AfterTeacherId
                WHERE t.AfterPresent=1 AND t.AfterTeacherId IS NOT NULL
                  AND
                  (
                      t.BeforePresent=0 OR t.BeforeTeacherId IS NULL OR t.BeforeTeacherId<>t.AfterTeacherId OR
                      (t.AfterTeacherOverride=0 AND
                       (COALESCE(t.BeforeTeacherOverride,1)<>0 OR t.BeforeGroupId IS NULL OR
                        t.BeforeGroupId<>t.AfterGroupId))
                  )
                  AND (g.Id IS NULL OR g.IsDeleted=1 OR g.SourceActive<>1 OR
                       g.SourceProfileCode IS NULL OR
                       g.SourceProfileCode COLLATE Latin1_General_100_BIN2
                           <>@SourceProfileCode COLLATE Latin1_General_100_BIN2)
            )

            UNION ALL
            SELECT 100,N'INACTIVE_REFERENCE',N'A changed or newly inherited training vehicle is inactive or belongs to another source profile.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget AS t
                LEFT JOIN #LockedVehicle AS x ON x.Id=t.AfterVehicleId
                WHERE t.AfterPresent=1 AND t.AfterVehicleId IS NOT NULL
                  AND
                  (
                      t.BeforePresent=0 OR t.BeforeVehicleId IS NULL OR t.BeforeVehicleId<>t.AfterVehicleId OR
                      (t.AfterVehicleOverride=0 AND
                       (COALESCE(t.BeforeVehicleOverride,1)<>0 OR t.BeforeGroupId IS NULL OR
                        t.BeforeGroupId<>t.AfterGroupId))
                  )
                  AND (x.Id IS NULL OR x.IsDeleted=1 OR x.SourceActive<>1 OR x.SourceLifecycle IS NULL OR
                       x.SourceLifecycle<>'ACTIVE' OR x.SourceProfileCode IS NULL OR
                       x.SourceProfileCode COLLATE Latin1_General_100_BIN2
                           <>@SourceProfileCode COLLATE Latin1_General_100_BIN2)
            )

            UNION ALL
            SELECT 110,N'INACTIVE_REFERENCE',N'A changed or newly inherited figure-10 vehicle is inactive or belongs to another source profile.'
            WHERE EXISTS
            (
                SELECT 1 FROM #AssignmentImportTarget AS t
                LEFT JOIN #LockedVehicle AS x ON x.Id=t.AfterFigure10Id
                WHERE t.AfterPresent=1 AND t.AfterFigure10Id IS NOT NULL
                  AND
                  (
                      t.BeforePresent=0 OR t.BeforeFigure10Id IS NULL OR t.BeforeFigure10Id<>t.AfterFigure10Id OR
                      (t.AfterFigure10Override=0 AND
                       (COALESCE(t.BeforeFigure10Override,1)<>0 OR t.BeforeGroupId IS NULL OR
                        t.BeforeGroupId<>t.AfterGroupId))
                  )
                  AND (x.Id IS NULL OR x.IsDeleted=1 OR x.SourceActive<>1 OR x.SourceLifecycle IS NULL OR
                       x.SourceLifecycle<>'ACTIVE' OR x.SourceProfileCode IS NULL OR
                       x.SourceProfileCode COLLATE Latin1_General_100_BIN2
                           <>@SourceProfileCode COLLATE Latin1_General_100_BIN2)
            )
        ) AS violation
        ORDER BY violation.Priority;
        """;

    private const string ApplyImportTargetsSql="""
        SET NOCOUNT ON;
        DECLARE @Now DATETIME2(7)=@TransactionAtUtc;

        CREATE TABLE #Changed
        (
            Ordinal INT NOT NULL PRIMARY KEY,HocVienId BIGINT NOT NULL,
            CurrentAssignmentId BIGINT NULL,CurrentAssignmentRowVersion BINARY(8) NULL,
            BeforePresent BIT NOT NULL,BeforeGroupId BIGINT NULL,BeforeDossierId BIGINT NULL,
            BeforeTeacherId BIGINT NULL,BeforeVehicleId BIGINT NULL,BeforeFigure10Id BIGINT NULL,
            BeforeTeacherOverride BIT NULL,BeforeVehicleOverride BIT NULL,BeforeFigure10Override BIT NULL,
            AfterPresent BIT NOT NULL,AfterGroupId BIGINT NULL,AfterDossierId BIGINT NULL,
            AfterTeacherId BIGINT NULL,AfterVehicleId BIGINT NULL,AfterFigure10Id BIGINT NULL,
            AfterTeacherOverride BIT NULL,AfterVehicleOverride BIT NULL,AfterFigure10Override BIT NULL,
            EffectiveAt DATETIME2(7) NOT NULL
        );
        INSERT #Changed
        SELECT t.Ordinal,t.HocVienId,t.CurrentAssignmentId,t.CurrentAssignmentRowVersion,
               t.BeforePresent,t.BeforeGroupId,t.BeforeDossierId,t.BeforeTeacherId,
               t.BeforeVehicleId,t.BeforeFigure10Id,t.BeforeTeacherOverride,
               t.BeforeVehicleOverride,t.BeforeFigure10Override,
               t.AfterPresent,t.AfterGroupId,t.AfterDossierId,t.AfterTeacherId,
               t.AfterVehicleId,t.AfterFigure10Id,t.AfterTeacherOverride,
               t.AfterVehicleOverride,t.AfterFigure10Override,
               CONVERT(datetime2(7),CASE WHEN c.NgayHieuLuc>=@Now
                    THEN DATEADD(NANOSECOND,100,c.NgayHieuLuc) ELSE @Now END)
        FROM #AssignmentImportTarget AS t
        LEFT JOIN #LockedCurrent AS c ON c.Ordinal=t.Ordinal
        WHERE t.Status='READY';

        DECLARE @ExpectedCloseCount INT=
            (SELECT COUNT(1) FROM #Changed WHERE CurrentAssignmentId IS NOT NULL);
        UPDATE pc
        SET IsCurrent=0,NgayHetHieuLuc=c.EffectiveAt,
            UpdatedAt=c.EffectiveAt,UpdatedBy=@Actor
        FROM dbo.App_HocVien_PhanCong AS pc
        JOIN #Changed AS c ON c.CurrentAssignmentId=pc.PhanCongId
        WHERE pc.IsCurrent=1 AND pc.RowVersion=c.CurrentAssignmentRowVersion;
        DECLARE @ClosedCount INT=@@ROWCOUNT;
        IF @ClosedCount<>@ExpectedCloseCount
            THROW 529381,'A current assignment changed after set-based validation.',1;

        CREATE TABLE #Inserted(HocVienId BIGINT NOT NULL PRIMARY KEY,AssignmentId BIGINT NOT NULL);
        INSERT dbo.App_HocVien_PhanCong
            (HocVienId,NhomDaoTaoId,GiaoVienHoSoId,GiaoVienDungLopId,XeTapId,XeBaiSo10Id,
             IsGiaoVienDungLopOverride,IsXeTapOverride,IsXeBaiSo10Override,NguonGan,
             ImportSessionId,NgayHieuLuc,NgayHetHieuLuc,IsCurrent,GhiChu,CreatedAt,CreatedBy)
        OUTPUT INSERTED.HocVienId,INSERTED.PhanCongId INTO #Inserted(HocVienId,AssignmentId)
        SELECT TOP (2147483647) c.HocVienId,c.AfterGroupId,c.AfterDossierId,c.AfterTeacherId,
               c.AfterVehicleId,c.AfterFigure10Id,c.AfterTeacherOverride,c.AfterVehicleOverride,
               c.AfterFigure10Override,'EXCEL',@ImportSessionId,c.EffectiveAt,NULL,1,
               @Reason,c.EffectiveAt,@Actor
        FROM #Changed AS c
        WHERE c.AfterPresent=1
        ORDER BY c.HocVienId;
        DECLARE @InsertedCount INT=@@ROWCOUNT;
        IF @InsertedCount<>(SELECT COUNT(1) FROM #Changed WHERE AfterPresent=1)
            THROW 529382,'A replacement assignment snapshot could not be inserted.',1;

        DECLARE @AuditAt DATETIME2(7)=@Now;
        INSERT dbo.App_AuditLog
            (ChucNang,HanhDong,EntityType,EntityId,EntityKey,DuLieuTruoc,DuLieuSau,
             KetQua,Loi,CreatedAt,CreatedBy,ClientIp,UserAgent)
        SELECT TOP (2147483647)
               N'PHAN_CONG_HOC_VIEN',N'SNAPSHOT',N'App_HocVien_PhanCong',
               CONVERT(nvarchar(100),c.HocVienId),@OperationId,
               (
                   SELECT @CourseId AS [courseId],@SourceProfileCode AS [sourceProfileCode],
                          @OperationId AS [operationId],@Reason AS [reason],c.HocVienId AS [hocVienId],
                          JSON_QUERY(CASE WHEN c.BeforePresent=1 THEN
                          (
                              SELECT c.CurrentAssignmentId AS [assignmentId],
                                     c.BeforeGroupId AS [groupId],c.BeforeDossierId AS [dossierReceiverId],
                                     c.BeforeTeacherId AS [classTeacherId],c.BeforeVehicleId AS [trainingVehicleId],
                                     c.BeforeFigure10Id AS [figure10VehicleId],
                                     c.BeforeTeacherOverride AS [overrideClassTeacher],
                                     c.BeforeVehicleOverride AS [overrideTrainingVehicle],
                                     c.BeforeFigure10Override AS [overrideFigure10Vehicle]
                              FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES
                          ) END) AS [assignment]
                   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES
               ),
               (
                   SELECT @CourseId AS [courseId],@SourceProfileCode AS [sourceProfileCode],
                          @OperationId AS [operationId],@Reason AS [reason],c.HocVienId AS [hocVienId],
                          JSON_QUERY(CASE WHEN c.AfterPresent=1 THEN
                          (
                              SELECT i.AssignmentId AS [assignmentId],
                                     c.AfterGroupId AS [groupId],c.AfterDossierId AS [dossierReceiverId],
                                     c.AfterTeacherId AS [classTeacherId],c.AfterVehicleId AS [trainingVehicleId],
                                     c.AfterFigure10Id AS [figure10VehicleId],
                                     c.AfterTeacherOverride AS [overrideClassTeacher],
                                     c.AfterVehicleOverride AS [overrideTrainingVehicle],
                                     c.AfterFigure10Override AS [overrideFigure10Vehicle]
                              FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES
                          ) END) AS [assignment]
                   FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES
               ),
               N'SUCCESS',NULL,@AuditAt,@Actor,NULL,NULL
        FROM #Changed AS c
        LEFT JOIN #Inserted AS i ON i.HocVienId=c.HocVienId
        ORDER BY c.HocVienId;
        IF @@ROWCOUNT<>(SELECT COUNT(1) FROM #Changed)
            THROW 529383,'Per-target assignment audit insertion was incomplete.',1;

        SELECT CONVERT(int,COUNT_BIG(1)) FROM #Changed;
        """;

    private sealed record SetBasedGuardFailure(string Code,string Message);
}
