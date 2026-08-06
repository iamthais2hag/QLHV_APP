USE [CSDL_OTO_V1_BAK];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() <> N'CSDL_OTO_V1_BAK'
    THROW 527600, 'Wrong database. Expected CSDL_OTO_V1_BAK.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID()
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
)
    THROW 527601, 'CSDL_OTO_V1_BAK must be ONLINE and read-write.', 1;

DECLARE @ExpectedTargetKeyShape table
(
    TableName sysname NOT NULL,
    ColumnName sysname NOT NULL,
    TypeName sysname NOT NULL,
    MaxLength smallint NOT NULL,
    [Precision] tinyint NOT NULL,
    Scale tinyint NOT NULL,
    CollationName sysname NULL,
    IsNullable bit NOT NULL,
    PkOrder tinyint NOT NULL
);

INSERT INTO @ExpectedTargetKeyShape
    (TableName, ColumnName, TypeName, MaxLength, [Precision], Scale,
     CollationName, IsNullable, PkOrder)
VALUES
    (N'DM_DonViGTVT', N'MaDV', N'varchar', 6, 0, 0, N'SQL_Latin1_General_CP1_CI_AS', 0, 1),
    (N'GiaoVien', N'MaGV', N'varchar', 8, 0, 0, N'SQL_Latin1_General_CP1_CI_AS', 0, 1),
    (N'KhoaHoc', N'MaKH', N'varchar', 13, 0, 0, N'SQL_Latin1_General_CP1_CI_AS', 0, 1),
    (N'KhoaHoc_GiaoVien', N'MaLichLV', N'int', 4, 10, 0, NULL, 0, 1),
    (N'BaoCaoI', N'MaBCI', N'varchar', 18, 0, 0, N'SQL_Latin1_General_CP1_CI_AS', 0, 1),
    (N'NguoiLX', N'MaDK', N'varchar', 25, 0, 0, N'SQL_Latin1_General_CP1_CI_AS', 0, 1),
    (N'NguoiLX_HoSo', N'MaDK', N'varchar', 25, 0, 0, N'SQL_Latin1_General_CP1_CI_AS', 0, 1),
    (N'NguoiLXHS_GiayTo', N'MaGT', N'int', 4, 10, 0, NULL, 0, 1),
    (N'NguoiLXHS_GiayTo', N'MaDK', N'varchar', 25, 0, 0, N'SQL_Latin1_General_CP1_CI_AS', 0, 2);

IF EXISTS
(
    SELECT
        expected.TableName, expected.ColumnName, expected.TypeName,
        expected.MaxLength, expected.[Precision], expected.Scale,
        expected.CollationName, expected.IsNullable, expected.PkOrder
    FROM @ExpectedTargetKeyShape AS expected
    EXCEPT
    SELECT
        targetTable.name, targetColumn.name, targetType.name,
        targetColumn.max_length, targetColumn.precision, targetColumn.scale,
        targetColumn.collation_name, targetColumn.is_nullable,
        CONVERT(tinyint, targetIndexColumn.key_ordinal)
    FROM sys.tables AS targetTable
    INNER JOIN sys.schemas AS targetSchema
      ON targetSchema.schema_id = targetTable.schema_id
     AND targetSchema.name = N'dbo'
    INNER JOIN sys.columns AS targetColumn
      ON targetColumn.object_id = targetTable.object_id
    INNER JOIN sys.types AS targetType
      ON targetType.user_type_id = targetColumn.user_type_id
    INNER JOIN sys.indexes AS targetIndex
      ON targetIndex.object_id = targetTable.object_id
     AND targetIndex.is_primary_key = 1
     AND targetIndex.type = 1
     AND targetIndex.is_disabled = 0
     AND targetIndex.is_hypothetical = 0
    INNER JOIN sys.index_columns AS targetIndexColumn
      ON targetIndexColumn.object_id = targetIndex.object_id
     AND targetIndexColumn.index_id = targetIndex.index_id
     AND targetIndexColumn.column_id = targetColumn.column_id
    INNER JOIN @ExpectedTargetKeyShape AS selected
      ON selected.TableName = targetTable.name
     AND selected.ColumnName = targetColumn.name
)
OR EXISTS
(
    SELECT targetTable.name
    FROM sys.tables AS targetTable
    INNER JOIN sys.schemas AS targetSchema
      ON targetSchema.schema_id = targetTable.schema_id
     AND targetSchema.name = N'dbo'
    INNER JOIN sys.indexes AS targetIndex
      ON targetIndex.object_id = targetTable.object_id
     AND targetIndex.is_primary_key = 1
    INNER JOIN sys.index_columns AS targetIndexColumn
      ON targetIndexColumn.object_id = targetIndex.object_id
     AND targetIndexColumn.index_id = targetIndex.index_id
     AND targetIndexColumn.key_ordinal > 0
    WHERE targetTable.name IN
    (
        SELECT DISTINCT TableName
        FROM @ExpectedTargetKeyShape
    )
    GROUP BY targetTable.name
    HAVING COUNT(*) <>
    (
        SELECT COUNT(*)
        FROM @ExpectedTargetKeyShape AS expected
        WHERE expected.TableName = targetTable.name
    )
)
    THROW 527618, 'Target key type, collation, nullability, or PK order is outside the typed-claim contract.', 1;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.QLHV_CsdtRealtimeCycle', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.QLHV_CsdtRealtimeCycle
        (
            CycleId uniqueidentifier NOT NULL,
            TargetProfile varchar(32) NOT NULL,
            SourceProfile varchar(32) NOT NULL,
            StreamCode varchar(40) NOT NULL,
            MaCSDT varchar(10) NOT NULL,
            StartSourceVersion bigint NOT NULL,
            EndSourceVersion bigint NOT NULL,
            EnabledDomainCount smallint NOT NULL,
            MappingFingerprint binary(32) NOT NULL,
            RouteFingerprint binary(32) NOT NULL,
            SourceSchemaFingerprint binary(32) NOT NULL,
            TargetSchemaFingerprint binary(32) NOT NULL,
            StagedKeySetHash binary(32) NULL,
            CycleStatus varchar(24) NOT NULL,
            StartedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_QLHV_CsdtRealtimeCycle_Started DEFAULT (SYSUTCDATETIME()),
            StagedAtUtc datetime2(7) NULL,
            ValidatedAtUtc datetime2(7) NULL,
            TargetCommittedAtUtc datetime2(7) NULL,
            CheckpointPublishedAtUtc datetime2(7) NULL,
            CompletedAtUtc datetime2(7) NULL,
            ErrorCode varchar(48) NULL,
            RowVersion rowversion NOT NULL,
            CONSTRAINT PK_QLHV_CsdtRealtimeCycle
                PRIMARY KEY CLUSTERED (CycleId)
        );
    END;

    IF OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.QLHV_CsdtRealtimeSourceMembership
        (
            MembershipId bigint IDENTITY(1,1) NOT NULL,
            TargetProfile varchar(32) NOT NULL,
            SourceProfile varchar(32) NOT NULL,
            StreamCode varchar(40) NOT NULL,
            MaCSDT varchar(10) NOT NULL,
            TableName varchar(40) NOT NULL,
            KeySchemaVersion smallint NOT NULL,
            CanonicalBusinessKey varbinary(512) NOT NULL,
            TargetEqualityKey varbinary(512) NOT NULL,
            TargetEqualityProofStatus varchar(16) NOT NULL,
            TargetEqualityProofId varchar(100) NOT NULL,
            CanonicalBusinessKeyHash binary(32) NOT NULL,
            HashKeyVersion int NOT NULL,
            IsActive bit NOT NULL,
            ClaimsTargetKey bit NOT NULL,
            OwnershipReserved bit NOT NULL,
            MembershipStatus varchar(24) NOT NULL,
            TargetAction varchar(24) NOT NULL,
            LastObservedSourceVersion bigint NOT NULL,
            AppliedSourceVersion bigint NULL,
            DeletedAtSourceVersion bigint NULL,
            ReactivatedAtSourceVersion bigint NULL,
            FirstSeenCycleId uniqueidentifier NOT NULL,
            LastSeenCycleId uniqueidentifier NOT NULL,
            LastAppliedCycleId uniqueidentifier NULL,
            ReasonCode varchar(48) NOT NULL,
            MappingFingerprint binary(32) NOT NULL,
            RouteFingerprint binary(32) NOT NULL,
            OwnershipEpoch int NOT NULL,
            CreatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_QLHV_CsdtRealtimeMembership_Created DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_QLHV_CsdtRealtimeMembership_Updated DEFAULT (SYSUTCDATETIME()),
            DeactivatedAtUtc datetime2(7) NULL,
            ReactivatedAtUtc datetime2(7) NULL,
            RowVersion rowversion NOT NULL,
            CONSTRAINT PK_QLHV_CsdtRealtimeSourceMembership
                PRIMARY KEY CLUSTERED (MembershipId)
        );
    END;

    IF OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.QLHV_CsdtRealtimeOwnershipClaim
        (
            OwnershipClaimId bigint IDENTITY(1,1) NOT NULL,
            MembershipId bigint NOT NULL,
            TargetProfile varchar(32)
                COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            TableName varchar(40)
                COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            ProofVersion smallint NOT NULL,
            ProofId varchar(100)
                COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            DmDonViGtvtMaDV varchar(6)
                COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            GiaoVienMaGV varchar(8)
                COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            KhoaHocMaKH varchar(13)
                COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            KhoaHocGiaoVienMaLichLV int NULL,
            BaoCaoIMaBCI varchar(18)
                COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            NguoiLXMaDK varchar(25)
                COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            NguoiLXHoSoMaDK varchar(25)
                COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            GiayToMaGT int NULL,
            GiayToMaDK varchar(25)
                COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            ClaimedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_QLHV_CsdtRealtimeOwnershipClaim_Claimed
                DEFAULT (SYSUTCDATETIME()),
            RowVersion rowversion NOT NULL,
            CONSTRAINT PK_QLHV_CsdtRealtimeOwnershipClaim
                PRIMARY KEY CLUSTERED (OwnershipClaimId),
            CONSTRAINT FK_QLHV_CsdtRealtimeOwnershipClaim_Membership
                FOREIGN KEY (MembershipId)
                REFERENCES dbo.QLHV_CsdtRealtimeSourceMembership (MembershipId)
        );
    END;

    IF OBJECT_ID(N'dbo.QLHV_CsdtRealtimeMembershipJournal', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.QLHV_CsdtRealtimeMembershipJournal
        (
            JournalId bigint IDENTITY(1,1) NOT NULL,
            MembershipId bigint NOT NULL,
            CycleId uniqueidentifier NOT NULL,
            BeforeStatus varchar(24) NOT NULL,
            AfterStatus varchar(24) NOT NULL,
            SourceVersion bigint NOT NULL,
            ReasonCode varchar(48) NOT NULL,
            TargetAction varchar(24) NOT NULL,
            MappingFingerprint binary(32) NOT NULL,
            RouteFingerprint binary(32) NOT NULL,
            OccurredAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_QLHV_CsdtRealtimeJournal_Occurred DEFAULT (SYSUTCDATETIME()),
            DiagnosticKeyHash binary(32) NOT NULL,
            HashKeyVersion int NOT NULL,
            CONSTRAINT PK_QLHV_CsdtRealtimeMembershipJournal
                PRIMARY KEY CLUSTERED (JournalId),
            CONSTRAINT FK_QLHV_CsdtRealtimeJournal_Membership
                FOREIGN KEY (MembershipId)
                REFERENCES dbo.QLHV_CsdtRealtimeSourceMembership (MembershipId),
            CONSTRAINT FK_QLHV_CsdtRealtimeJournal_Cycle
                FOREIGN KEY (CycleId)
                REFERENCES dbo.QLHV_CsdtRealtimeCycle (CycleId)
        );
    END;

    IF OBJECT_ID(N'dbo.QLHV_CsdtRealtimeCycleDomain', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.QLHV_CsdtRealtimeCycleDomain
        (
            CycleId uniqueidentifier NOT NULL,
            DomainName varchar(40) NOT NULL,
            DomainStatus varchar(24) NOT NULL,
            SourceRowCount bigint NOT NULL,
            InsertCount bigint NOT NULL,
            UpdateCount bigint NOT NULL,
            DeleteCount bigint NOT NULL,
            PreservedExcludedCount bigint NOT NULL,
            ConflictCount bigint NOT NULL,
            SourceKeySetHash binary(32) NOT NULL,
            ResultHash binary(32) NULL,
            ErrorCode varchar(48) NULL,
            StartedAtUtc datetime2(7) NOT NULL,
            CompletedAtUtc datetime2(7) NULL,
            RowVersion rowversion NOT NULL,
            CONSTRAINT PK_QLHV_CsdtRealtimeCycleDomain
                PRIMARY KEY CLUSTERED (CycleId, DomainName),
            CONSTRAINT FK_QLHV_CsdtRealtimeCycleDomain_Cycle
                FOREIGN KEY (CycleId)
                REFERENCES dbo.QLHV_CsdtRealtimeCycle (CycleId)
        );
    END;

    IF OBJECT_ID(N'dbo.QLHV_CsdtRealtimeStreamCoverage', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.QLHV_CsdtRealtimeStreamCoverage
        (
            TargetProfile varchar(32) NOT NULL,
            SourceProfile varchar(32) NOT NULL,
            StreamCode varchar(40) NOT NULL,
            MaCSDT varchar(10) NOT NULL,
            TableName varchar(40) NOT NULL,
            BaselineSourceVersion bigint NOT NULL,
            MappingFingerprint binary(32) NOT NULL,
            RouteFingerprint binary(32) NOT NULL,
            SourceSchemaFingerprint binary(32) NOT NULL,
            TargetSchemaFingerprint binary(32) NOT NULL,
            SourceKeySetHash binary(32) NOT NULL,
            MembershipCount bigint NOT NULL,
            IsComplete bit NOT NULL,
            CompletedCycleId uniqueidentifier NULL,
            CompletedAtUtc datetime2(7) NULL,
            RowVersion rowversion NOT NULL,
            CONSTRAINT PK_QLHV_CsdtRealtimeStreamCoverage
                PRIMARY KEY CLUSTERED
                (TargetProfile, SourceProfile, StreamCode, TableName)
        );
    END;

    IF OBJECT_ID(N'dbo.QLHV_CsdtRealtimeCheckpoint', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.QLHV_CsdtRealtimeCheckpoint
        (
            CheckpointId bigint IDENTITY(1,1) NOT NULL,
            TargetProfile varchar(32) NOT NULL,
            SourceProfile varchar(32) NOT NULL,
            StreamCode varchar(40) NOT NULL,
            AppliedSourceVersion bigint NOT NULL,
            CommittedCycleId uniqueidentifier NOT NULL,
            MappingFingerprint binary(32) NOT NULL,
            RouteFingerprint binary(32) NOT NULL,
            SourceSchemaFingerprint binary(32) NOT NULL,
            TargetSchemaFingerprint binary(32) NOT NULL,
            StagedKeySetHash binary(32) NOT NULL,
            CheckpointStatus varchar(16) NOT NULL,
            PublishedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_QLHV_CsdtRealtimeCheckpoint_Published
                DEFAULT (SYSUTCDATETIME()),
            VerifiedAtUtc datetime2(7) NULL,
            RowVersion rowversion NOT NULL,
            CONSTRAINT PK_QLHV_CsdtRealtimeCheckpoint
                PRIMARY KEY CLUSTERED (CheckpointId)
        );
    END;

    DECLARE @RequiredColumns table
    (
        ContractOrdinal int IDENTITY(1,1) NOT NULL,
        TableName sysname NOT NULL,
        ColumnName sysname NOT NULL,
        TypeName sysname NOT NULL,
        MaxLength smallint NOT NULL,
        IsNullable bit NOT NULL,
        IsIdentity bit NOT NULL
    );

    INSERT INTO @RequiredColumns
        (TableName, ColumnName, TypeName, MaxLength, IsNullable, IsIdentity)
    VALUES
        (N'QLHV_CsdtRealtimeCycle', N'CycleId', N'uniqueidentifier', 16, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'TargetProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'SourceProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'StreamCode', N'varchar', 40, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'MaCSDT', N'varchar', 10, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'StartSourceVersion', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'EndSourceVersion', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'EnabledDomainCount', N'smallint', 2, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'MappingFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'RouteFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'SourceSchemaFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'TargetSchemaFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'StagedKeySetHash', N'binary', 32, 1, 0),
        (N'QLHV_CsdtRealtimeCycle', N'CycleStatus', N'varchar', 24, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'StartedAtUtc', N'datetime2', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycle', N'StagedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeCycle', N'ValidatedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeCycle', N'TargetCommittedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeCycle', N'CheckpointPublishedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeCycle', N'CompletedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeCycle', N'ErrorCode', N'varchar', 48, 1, 0),
        (N'QLHV_CsdtRealtimeCycle', N'RowVersion', N'timestamp', 8, 0, 0),

        (N'QLHV_CsdtRealtimeSourceMembership', N'MembershipId', N'bigint', 8, 0, 1),
        (N'QLHV_CsdtRealtimeSourceMembership', N'TargetProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'SourceProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'StreamCode', N'varchar', 40, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'MaCSDT', N'varchar', 10, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'TableName', N'varchar', 40, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'KeySchemaVersion', N'smallint', 2, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'CanonicalBusinessKey', N'varbinary', 512, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'TargetEqualityKey', N'varbinary', 512, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'TargetEqualityProofStatus', N'varchar', 16, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'TargetEqualityProofId', N'varchar', 100, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'CanonicalBusinessKeyHash', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'HashKeyVersion', N'int', 4, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'IsActive', N'bit', 1, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'ClaimsTargetKey', N'bit', 1, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'OwnershipReserved', N'bit', 1, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'MembershipStatus', N'varchar', 24, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'TargetAction', N'varchar', 24, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'LastObservedSourceVersion', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'AppliedSourceVersion', N'bigint', 8, 1, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'DeletedAtSourceVersion', N'bigint', 8, 1, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'ReactivatedAtSourceVersion', N'bigint', 8, 1, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'FirstSeenCycleId', N'uniqueidentifier', 16, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'LastSeenCycleId', N'uniqueidentifier', 16, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'LastAppliedCycleId', N'uniqueidentifier', 16, 1, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'ReasonCode', N'varchar', 48, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'MappingFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'RouteFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'OwnershipEpoch', N'int', 4, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'CreatedAtUtc', N'datetime2', 8, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'UpdatedAtUtc', N'datetime2', 8, 0, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'DeactivatedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'ReactivatedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeSourceMembership', N'RowVersion', N'timestamp', 8, 0, 0),

        (N'QLHV_CsdtRealtimeOwnershipClaim', N'OwnershipClaimId', N'bigint', 8, 0, 1),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'MembershipId', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'TargetProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'TableName', N'varchar', 40, 0, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'ProofVersion', N'smallint', 2, 0, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'ProofId', N'varchar', 100, 0, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'DmDonViGtvtMaDV', N'varchar', 6, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'GiaoVienMaGV', N'varchar', 8, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'KhoaHocMaKH', N'varchar', 13, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'KhoaHocGiaoVienMaLichLV', N'int', 4, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'BaoCaoIMaBCI', N'varchar', 18, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'NguoiLXMaDK', N'varchar', 25, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'NguoiLXHoSoMaDK', N'varchar', 25, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'GiayToMaGT', N'int', 4, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'GiayToMaDK', N'varchar', 25, 1, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'ClaimedAtUtc', N'datetime2', 8, 0, 0),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'RowVersion', N'timestamp', 8, 0, 0),

        (N'QLHV_CsdtRealtimeMembershipJournal', N'JournalId', N'bigint', 8, 0, 1),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'MembershipId', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'CycleId', N'uniqueidentifier', 16, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'BeforeStatus', N'varchar', 24, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'AfterStatus', N'varchar', 24, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'SourceVersion', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'ReasonCode', N'varchar', 48, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'TargetAction', N'varchar', 24, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'MappingFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'RouteFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'OccurredAtUtc', N'datetime2', 8, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'DiagnosticKeyHash', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'HashKeyVersion', N'int', 4, 0, 0),

        (N'QLHV_CsdtRealtimeCycleDomain', N'CycleId', N'uniqueidentifier', 16, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'DomainName', N'varchar', 40, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'DomainStatus', N'varchar', 24, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'SourceRowCount', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'InsertCount', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'UpdateCount', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'DeleteCount', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'PreservedExcludedCount', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'ConflictCount', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'SourceKeySetHash', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'ResultHash', N'binary', 32, 1, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'ErrorCode', N'varchar', 48, 1, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'StartedAtUtc', N'datetime2', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'CompletedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeCycleDomain', N'RowVersion', N'timestamp', 8, 0, 0),

        (N'QLHV_CsdtRealtimeStreamCoverage', N'TargetProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'SourceProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'StreamCode', N'varchar', 40, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'MaCSDT', N'varchar', 10, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'TableName', N'varchar', 40, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'BaselineSourceVersion', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'MappingFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'RouteFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'SourceSchemaFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'TargetSchemaFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'SourceKeySetHash', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'MembershipCount', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'IsComplete', N'bit', 1, 0, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'CompletedCycleId', N'uniqueidentifier', 16, 1, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'CompletedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'RowVersion', N'timestamp', 8, 0, 0),

        (N'QLHV_CsdtRealtimeCheckpoint', N'CheckpointId', N'bigint', 8, 0, 1),
        (N'QLHV_CsdtRealtimeCheckpoint', N'TargetProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'SourceProfile', N'varchar', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'StreamCode', N'varchar', 40, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'AppliedSourceVersion', N'bigint', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'CommittedCycleId', N'uniqueidentifier', 16, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'MappingFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'RouteFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'SourceSchemaFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'TargetSchemaFingerprint', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'StagedKeySetHash', N'binary', 32, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'CheckpointStatus', N'varchar', 16, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'PublishedAtUtc', N'datetime2', 8, 0, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'VerifiedAtUtc', N'datetime2', 8, 1, 0),
        (N'QLHV_CsdtRealtimeCheckpoint', N'RowVersion', N'timestamp', 8, 0, 0);

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            SELECT
                required.*,
                ROW_NUMBER() OVER
                (
                    PARTITION BY required.TableName
                    ORDER BY required.ContractOrdinal
                ) AS ExpectedColumnOrdinal
            FROM @RequiredColumns AS required
        ) AS required
        LEFT JOIN sys.tables AS tableMetadata
          ON tableMetadata.name = required.TableName
         AND tableMetadata.schema_id = SCHEMA_ID(N'dbo')
        LEFT JOIN sys.columns AS columnMetadata
          ON columnMetadata.object_id = tableMetadata.object_id
         AND columnMetadata.name = required.ColumnName
        LEFT JOIN sys.types AS typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.column_id IS NULL
           OR columnMetadata.column_id <> required.ExpectedColumnOrdinal
           OR typeMetadata.name <> required.TypeName
           OR columnMetadata.max_length <> required.MaxLength
           OR columnMetadata.precision <>
              CASE required.TypeName
                  WHEN N'bigint' THEN 19
                  WHEN N'int' THEN 10
                  WHEN N'smallint' THEN 5
                  WHEN N'bit' THEN 1
                  WHEN N'datetime2' THEN 27
                  ELSE 0
              END
           OR columnMetadata.scale <>
              CASE WHEN required.TypeName = N'datetime2' THEN 7 ELSE 0 END
           OR columnMetadata.is_nullable <> required.IsNullable
           OR columnMetadata.is_identity <> required.IsIdentity
           OR columnMetadata.is_computed <> 0
           OR (required.TypeName = N'timestamp' AND columnMetadata.system_type_id <> 189)
           OR (required.TypeName <> N'timestamp' AND columnMetadata.system_type_id = 189)
           OR
           (
               required.TableName = N'QLHV_CsdtRealtimeOwnershipClaim'
               AND required.TypeName = N'varchar'
               AND columnMetadata.collation_name <> N'SQL_Latin1_General_CP1_CI_AS'
           )
    )
    OR EXISTS
    (
        SELECT 1
        FROM
        (
            SELECT TableName, COUNT(*) AS ExpectedColumnCount
            FROM @RequiredColumns
            GROUP BY TableName
        ) AS expected
        LEFT JOIN sys.tables AS tableMetadata
          ON tableMetadata.name = expected.TableName
         AND tableMetadata.schema_id = SCHEMA_ID(N'dbo')
        OUTER APPLY
        (
            SELECT COUNT(*) AS ActualColumnCount
            FROM sys.columns AS columnMetadata
            WHERE columnMetadata.object_id = tableMetadata.object_id
        ) AS actual
        WHERE tableMetadata.object_id IS NULL
           OR actual.ActualColumnCount <> expected.ExpectedColumnCount
    )
        THROW 527602, 'An existing CSDT control-plane table has an incompatible column shape.', 1;

    DECLARE @RequiredDefaults table
    (
        TableName sysname NOT NULL,
        ColumnName sysname NOT NULL,
        ConstraintName sysname NOT NULL,
        NormalizedDefinition nvarchar(100) NOT NULL
    );

    INSERT INTO @RequiredDefaults
        (TableName, ColumnName, ConstraintName, NormalizedDefinition)
    VALUES
        (N'QLHV_CsdtRealtimeCycle', N'StartedAtUtc',
         N'DF_QLHV_CsdtRealtimeCycle_Started', N'sysutcdatetime'),
        (N'QLHV_CsdtRealtimeSourceMembership', N'CreatedAtUtc',
         N'DF_QLHV_CsdtRealtimeMembership_Created', N'sysutcdatetime'),
        (N'QLHV_CsdtRealtimeSourceMembership', N'UpdatedAtUtc',
         N'DF_QLHV_CsdtRealtimeMembership_Updated', N'sysutcdatetime'),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'ClaimedAtUtc',
         N'DF_QLHV_CsdtRealtimeOwnershipClaim_Claimed', N'sysutcdatetime'),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'OccurredAtUtc',
         N'DF_QLHV_CsdtRealtimeJournal_Occurred', N'sysutcdatetime'),
        (N'QLHV_CsdtRealtimeCheckpoint', N'PublishedAtUtc',
         N'DF_QLHV_CsdtRealtimeCheckpoint_Published', N'sysutcdatetime');

    IF EXISTS
    (
        SELECT 1
        FROM @RequiredDefaults AS required
        LEFT JOIN sys.tables AS tableMetadata
          ON tableMetadata.name = required.TableName
         AND tableMetadata.schema_id = SCHEMA_ID(N'dbo')
        LEFT JOIN sys.columns AS columnMetadata
          ON columnMetadata.object_id = tableMetadata.object_id
         AND columnMetadata.name = required.ColumnName
        LEFT JOIN sys.default_constraints AS defaultMetadata
          ON defaultMetadata.parent_object_id = tableMetadata.object_id
         AND defaultMetadata.parent_column_id = columnMetadata.column_id
        WHERE defaultMetadata.object_id IS NULL
           OR defaultMetadata.name <> required.ConstraintName
           OR LOWER
              (
                  REPLACE
                  (
                      REPLACE
                      (
                          REPLACE(defaultMetadata.definition, N'(', N''),
                          N')',
                          N''
                      ),
                      N' ',
                      N''
                  )
              ) <> required.NormalizedDefinition
    )
    OR
    (
        SELECT COUNT(*)
        FROM sys.default_constraints AS defaultMetadata
        WHERE defaultMetadata.parent_object_id IN
        (
            SELECT tableMetadata.object_id
            FROM sys.tables AS tableMetadata
            INNER JOIN
            (
                SELECT DISTINCT TableName
                FROM @RequiredColumns
            ) AS selected
              ON selected.TableName = tableMetadata.name
            WHERE tableMetadata.schema_id = SCHEMA_ID(N'dbo')
        )
    ) <> (SELECT COUNT(*) FROM @RequiredDefaults)
        THROW 527620, 'An existing CSDT control-plane default constraint has an incompatible shape.', 1;

    DECLARE @RequiredPrimaryKeyColumns table
    (
        TableName sysname NOT NULL,
        ConstraintName sysname NOT NULL,
        IndexType tinyint NOT NULL,
        KeyOrdinal tinyint NOT NULL,
        ColumnName sysname NOT NULL
    );

    INSERT INTO @RequiredPrimaryKeyColumns
        (TableName, ConstraintName, IndexType, KeyOrdinal, ColumnName)
    VALUES
        (N'QLHV_CsdtRealtimeCycle', N'PK_QLHV_CsdtRealtimeCycle', 1, 1, N'CycleId'),
        (N'QLHV_CsdtRealtimeSourceMembership', N'PK_QLHV_CsdtRealtimeSourceMembership', 1, 1, N'MembershipId'),
        (N'QLHV_CsdtRealtimeOwnershipClaim', N'PK_QLHV_CsdtRealtimeOwnershipClaim', 1, 1, N'OwnershipClaimId'),
        (N'QLHV_CsdtRealtimeMembershipJournal', N'PK_QLHV_CsdtRealtimeMembershipJournal', 1, 1, N'JournalId'),
        (N'QLHV_CsdtRealtimeCycleDomain', N'PK_QLHV_CsdtRealtimeCycleDomain', 1, 1, N'CycleId'),
        (N'QLHV_CsdtRealtimeCycleDomain', N'PK_QLHV_CsdtRealtimeCycleDomain', 1, 2, N'DomainName'),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'PK_QLHV_CsdtRealtimeStreamCoverage', 1, 1, N'TargetProfile'),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'PK_QLHV_CsdtRealtimeStreamCoverage', 1, 2, N'SourceProfile'),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'PK_QLHV_CsdtRealtimeStreamCoverage', 1, 3, N'StreamCode'),
        (N'QLHV_CsdtRealtimeStreamCoverage', N'PK_QLHV_CsdtRealtimeStreamCoverage', 1, 4, N'TableName'),
        (N'QLHV_CsdtRealtimeCheckpoint', N'PK_QLHV_CsdtRealtimeCheckpoint', 1, 1, N'CheckpointId');

    IF EXISTS
    (
        SELECT TableName, ConstraintName, IndexType, KeyOrdinal, ColumnName
        FROM @RequiredPrimaryKeyColumns
        EXCEPT
        SELECT
            tableMetadata.name,
            keyConstraint.name,
            indexMetadata.type,
            CONVERT(tinyint, indexColumn.key_ordinal),
            columnMetadata.name
        FROM sys.key_constraints AS keyConstraint
        INNER JOIN sys.tables AS tableMetadata
          ON tableMetadata.object_id = keyConstraint.parent_object_id
         AND tableMetadata.schema_id = SCHEMA_ID(N'dbo')
        INNER JOIN sys.indexes AS indexMetadata
          ON indexMetadata.object_id = keyConstraint.parent_object_id
         AND indexMetadata.index_id = keyConstraint.unique_index_id
        INNER JOIN sys.index_columns AS indexColumn
          ON indexColumn.object_id = indexMetadata.object_id
         AND indexColumn.index_id = indexMetadata.index_id
         AND indexColumn.key_ordinal > 0
        INNER JOIN sys.columns AS columnMetadata
          ON columnMetadata.object_id = indexColumn.object_id
         AND columnMetadata.column_id = indexColumn.column_id
        WHERE keyConstraint.type = N'PK'
          AND tableMetadata.name IN
          (
              SELECT DISTINCT TableName
              FROM @RequiredPrimaryKeyColumns
          )
    )
    OR EXISTS
    (
        SELECT
            tableMetadata.name,
            keyConstraint.name,
            indexMetadata.type,
            CONVERT(tinyint, indexColumn.key_ordinal),
            columnMetadata.name
        FROM sys.key_constraints AS keyConstraint
        INNER JOIN sys.tables AS tableMetadata
          ON tableMetadata.object_id = keyConstraint.parent_object_id
         AND tableMetadata.schema_id = SCHEMA_ID(N'dbo')
        INNER JOIN sys.indexes AS indexMetadata
          ON indexMetadata.object_id = keyConstraint.parent_object_id
         AND indexMetadata.index_id = keyConstraint.unique_index_id
        INNER JOIN sys.index_columns AS indexColumn
          ON indexColumn.object_id = indexMetadata.object_id
         AND indexColumn.index_id = indexMetadata.index_id
         AND indexColumn.key_ordinal > 0
        INNER JOIN sys.columns AS columnMetadata
          ON columnMetadata.object_id = indexColumn.object_id
         AND columnMetadata.column_id = indexColumn.column_id
        WHERE keyConstraint.type = N'PK'
          AND tableMetadata.name IN
          (
              SELECT DISTINCT TableName
              FROM @RequiredPrimaryKeyColumns
          )
        EXCEPT
        SELECT TableName, ConstraintName, IndexType, KeyOrdinal, ColumnName
        FROM @RequiredPrimaryKeyColumns
    )
    OR EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexMetadata
        INNER JOIN @RequiredPrimaryKeyColumns AS required
          ON indexMetadata.object_id = OBJECT_ID(N'dbo.' + required.TableName, N'U')
         AND indexMetadata.name = required.ConstraintName
        WHERE indexMetadata.is_primary_key = 0
           OR indexMetadata.is_unique = 0
           OR indexMetadata.is_disabled = 1
           OR indexMetadata.is_hypothetical = 1
    )
        THROW 527608, 'An existing CSDT control-plane primary key has an incompatible table, type, column, or order.', 1;

    IF OBJECT_ID(N'dbo.FK_QLHV_CsdtRealtimeMembership_FirstSeenCycle', N'F') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT FK_QLHV_CsdtRealtimeMembership_FirstSeenCycle
            FOREIGN KEY (FirstSeenCycleId)
            REFERENCES dbo.QLHV_CsdtRealtimeCycle (CycleId);

    IF OBJECT_ID(N'dbo.FK_QLHV_CsdtRealtimeMembership_LastSeenCycle', N'F') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT FK_QLHV_CsdtRealtimeMembership_LastSeenCycle
            FOREIGN KEY (LastSeenCycleId)
            REFERENCES dbo.QLHV_CsdtRealtimeCycle (CycleId);

    IF OBJECT_ID(N'dbo.FK_QLHV_CsdtRealtimeMembership_LastAppliedCycle', N'F') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT FK_QLHV_CsdtRealtimeMembership_LastAppliedCycle
            FOREIGN KEY (LastAppliedCycleId)
            REFERENCES dbo.QLHV_CsdtRealtimeCycle (CycleId);

    IF OBJECT_ID(N'dbo.FK_QLHV_CsdtRealtimeCoverage_CompletedCycle', N'F') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeStreamCoverage WITH CHECK
        ADD CONSTRAINT FK_QLHV_CsdtRealtimeCoverage_CompletedCycle
            FOREIGN KEY (CompletedCycleId)
            REFERENCES dbo.QLHV_CsdtRealtimeCycle (CycleId);

    IF OBJECT_ID(N'dbo.FK_QLHV_CsdtRealtimeCheckpoint_CommittedCycle', N'F') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCheckpoint WITH CHECK
        ADD CONSTRAINT FK_QLHV_CsdtRealtimeCheckpoint_CommittedCycle
            FOREIGN KEY (CommittedCycleId)
            REFERENCES dbo.QLHV_CsdtRealtimeCycle (CycleId);

    DECLARE @RequiredForeignKeyColumns table
    (
        ConstraintName sysname NOT NULL,
        ChildTable sysname NOT NULL,
        ChildColumn sysname NOT NULL,
        ParentTable sysname NOT NULL,
        ParentColumn sysname NOT NULL,
        ColumnOrder tinyint NOT NULL,
        DeleteAction tinyint NOT NULL,
        UpdateAction tinyint NOT NULL
    );

    INSERT INTO @RequiredForeignKeyColumns
        (ConstraintName, ChildTable, ChildColumn, ParentTable, ParentColumn,
         ColumnOrder, DeleteAction, UpdateAction)
    VALUES
        (N'FK_QLHV_CsdtRealtimeOwnershipClaim_Membership',
         N'QLHV_CsdtRealtimeOwnershipClaim', N'MembershipId',
         N'QLHV_CsdtRealtimeSourceMembership', N'MembershipId', 1, 0, 0),
        (N'FK_QLHV_CsdtRealtimeJournal_Membership',
         N'QLHV_CsdtRealtimeMembershipJournal', N'MembershipId',
         N'QLHV_CsdtRealtimeSourceMembership', N'MembershipId', 1, 0, 0),
        (N'FK_QLHV_CsdtRealtimeJournal_Cycle',
         N'QLHV_CsdtRealtimeMembershipJournal', N'CycleId',
         N'QLHV_CsdtRealtimeCycle', N'CycleId', 1, 0, 0),
        (N'FK_QLHV_CsdtRealtimeCycleDomain_Cycle',
         N'QLHV_CsdtRealtimeCycleDomain', N'CycleId',
         N'QLHV_CsdtRealtimeCycle', N'CycleId', 1, 0, 0),
        (N'FK_QLHV_CsdtRealtimeMembership_FirstSeenCycle',
         N'QLHV_CsdtRealtimeSourceMembership', N'FirstSeenCycleId',
         N'QLHV_CsdtRealtimeCycle', N'CycleId', 1, 0, 0),
        (N'FK_QLHV_CsdtRealtimeMembership_LastSeenCycle',
         N'QLHV_CsdtRealtimeSourceMembership', N'LastSeenCycleId',
         N'QLHV_CsdtRealtimeCycle', N'CycleId', 1, 0, 0),
        (N'FK_QLHV_CsdtRealtimeMembership_LastAppliedCycle',
         N'QLHV_CsdtRealtimeSourceMembership', N'LastAppliedCycleId',
         N'QLHV_CsdtRealtimeCycle', N'CycleId', 1, 0, 0),
        (N'FK_QLHV_CsdtRealtimeCoverage_CompletedCycle',
         N'QLHV_CsdtRealtimeStreamCoverage', N'CompletedCycleId',
         N'QLHV_CsdtRealtimeCycle', N'CycleId', 1, 0, 0),
        (N'FK_QLHV_CsdtRealtimeCheckpoint_CommittedCycle',
         N'QLHV_CsdtRealtimeCheckpoint', N'CommittedCycleId',
         N'QLHV_CsdtRealtimeCycle', N'CycleId', 1, 0, 0);

    IF EXISTS
    (
        SELECT
            ConstraintName, ChildTable, ChildColumn, ParentTable,
            ParentColumn, ColumnOrder, DeleteAction, UpdateAction
        FROM @RequiredForeignKeyColumns
        EXCEPT
        SELECT
            foreignKey.name,
            childTable.name,
            childColumn.name,
            parentTable.name,
            parentColumn.name,
            CONVERT(tinyint, foreignKeyColumn.constraint_column_id),
            foreignKey.delete_referential_action,
            foreignKey.update_referential_action
        FROM sys.foreign_keys AS foreignKey
        INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
          ON foreignKeyColumn.constraint_object_id = foreignKey.object_id
        INNER JOIN sys.tables AS childTable
          ON childTable.object_id = foreignKey.parent_object_id
        INNER JOIN sys.columns AS childColumn
          ON childColumn.object_id = childTable.object_id
         AND childColumn.column_id = foreignKeyColumn.parent_column_id
        INNER JOIN sys.tables AS parentTable
          ON parentTable.object_id = foreignKey.referenced_object_id
        INNER JOIN sys.columns AS parentColumn
          ON parentColumn.object_id = parentTable.object_id
         AND parentColumn.column_id = foreignKeyColumn.referenced_column_id
        WHERE foreignKey.name IN
        (
            SELECT ConstraintName
            FROM @RequiredForeignKeyColumns
        )
          AND foreignKey.is_disabled = 0
          AND foreignKey.is_not_trusted = 0
    )
    OR EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS foreignKey
        INNER JOIN @RequiredForeignKeyColumns AS required
          ON required.ConstraintName = foreignKey.name
        WHERE foreignKey.is_disabled = 1
           OR foreignKey.is_not_trusted = 1
           OR
           (
               SELECT COUNT(*)
               FROM sys.foreign_key_columns AS foreignKeyColumn
               WHERE foreignKeyColumn.constraint_object_id = foreignKey.object_id
           ) <>
           (
               SELECT COUNT(*)
               FROM @RequiredForeignKeyColumns AS expected
               WHERE expected.ConstraintName = foreignKey.name
           )
    )
        THROW 527609, 'An existing CSDT control-plane foreign key has an incompatible child, parent, column, order, action, enabled, or trust shape.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS foreignKey
        WHERE foreignKey.parent_object_id IN
        (
            SELECT OBJECT_ID(N'dbo.' + requiredTable.TableName, N'U')
            FROM
            (
                SELECT DISTINCT TableName
                FROM @RequiredColumns
            ) AS requiredTable
        )
          AND foreignKey.name NOT IN
          (
              SELECT ConstraintName
              FROM @RequiredForeignKeyColumns
          )
    )
        THROW 527609, 'An existing CSDT control-plane table has an unexpected foreign key.', 1;

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeMembership_Route', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_Route CHECK
        (
            TargetProfile = 'OTO_V1_BAK'
            AND SourceProfile = 'OTO_V2_BAK'
            AND StreamCode = 'OTO_V2_TO_V1'
            AND MaCSDT = '66029'
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeMembership_Table', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_Table CHECK
        (
            TableName IN
            (
                'DM_DonViGTVT', 'GiaoVien', 'KhoaHoc', 'KhoaHoc_GiaoVien',
                'BaoCaoI', 'NguoiLX', 'NguoiLX_HoSo', 'NguoiLXHS_GiayTo'
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeMembership_Status', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_Status CHECK
        (
            MembershipStatus IN
            (
                'INSERT_PENDING', 'ACTIVE', 'DELETE_PENDING',
                'INACTIVE', 'REACTIVATE_PENDING', 'CONFLICT'
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeMembership_Action', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_Action CHECK
        (
            TargetAction IN
            ('NONE', 'UPSERTED', 'EXISTING_VERIFIED', 'HARD_DELETED', 'PRESERVED_EXCLUDED')
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeMembership_Reason', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_Reason CHECK
        (
            ReasonCode IN
            (
                'NONE', 'SOURCE_PRESENT', 'SOURCE_DELETE', 'FULL_RECONCILE_ABSENT',
                'REACTIVATED_AT_SOURCE', 'TARGET_ACTION_APPLIED', 'DUPLICATE_REPLAY',
                'LATE_SOURCE_EVENT', 'STREAM_OWNERSHIP_CONFLICT',
                'MAPPING_FINGERPRINT_MISMATCH', 'ROUTE_FINGERPRINT_MISMATCH',
                'TARGET_EQUALITY_UNPROVEN', 'BOOTSTRAP_INCOMPLETE',
                'UNOWNED_DELETE_KEY', 'BLOCK_DELETE_CONFLICT', 'MANUAL_CONFLICT',
                'BOOTSTRAP_MEMBERSHIP_CREATED', 'BOOTSTRAP_MEMBERSHIP_VERIFIED',
                'SOURCE_ROW_OBSERVED', 'CT_DELETE_OBSERVED',
                'DELETE_PENDING_NOT_APPLIED', 'REACTIVATION_CANDIDATE',
                'TARGET_ONLY_UNCLASSIFIED', 'OWNERSHIP_CONFLICT',
                'COVERAGE_COMPLETE', 'CHECKPOINT_CONFLICT',
                'BOOTSTRAP_PARENT_MISSING'
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeMembership_State', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_State CHECK
        (
            OwnershipReserved = 1
            AND
            (
                (MembershipStatus = 'ACTIVE' AND IsActive = 1 AND ClaimsTargetKey = 1)
                OR
                (
                    MembershipStatus IN ('INSERT_PENDING', 'REACTIVATE_PENDING')
                    AND IsActive = 0
                    AND ClaimsTargetKey = 1
                )
                OR
                (
                    MembershipStatus IN ('DELETE_PENDING', 'INACTIVE', 'CONFLICT')
                    AND IsActive = 0
                    AND ClaimsTargetKey = 0
                )
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeMembership_Versions', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_Versions CHECK
        (
            KeySchemaVersion > 0
            AND HashKeyVersion > 0
            AND OwnershipEpoch > 0
            AND LastObservedSourceVersion >= 0
            AND (AppliedSourceVersion IS NULL OR AppliedSourceVersion >= 0)
            AND (DeletedAtSourceVersion IS NULL OR DeletedAtSourceVersion >= 0)
            AND (ReactivatedAtSourceVersion IS NULL OR ReactivatedAtSourceVersion >= 0)
            AND (AppliedSourceVersion IS NULL OR AppliedSourceVersion <= LastObservedSourceVersion)
            AND (DeletedAtSourceVersion IS NULL OR DeletedAtSourceVersion <= LastObservedSourceVersion)
            AND (ReactivatedAtSourceVersion IS NULL OR ReactivatedAtSourceVersion <= LastObservedSourceVersion)
            AND
            (
                (MembershipStatus = 'INSERT_PENDING' AND AppliedSourceVersion IS NULL AND DeletedAtSourceVersion IS NULL)
                OR (MembershipStatus = 'ACTIVE' AND AppliedSourceVersion IS NOT NULL AND DeletedAtSourceVersion IS NULL)
                OR (MembershipStatus = 'DELETE_PENDING' AND DeletedAtSourceVersion IS NULL)
                OR
                (
                    MembershipStatus IN ('INACTIVE', 'REACTIVATE_PENDING')
                    AND AppliedSourceVersion IS NOT NULL
                    AND DeletedAtSourceVersion IS NOT NULL
                )
                OR MembershipStatus = 'CONFLICT'
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeMembership_TargetEquality', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeSourceMembership WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_TargetEquality CHECK
        (
            TargetEqualityProofStatus = 'TYPED_CLAIM'
            AND TargetEqualityProofId =
                'TYPED_OWNER_SQLSERVER_SQL_LATIN1_GENERAL_CP1_CI_AS_V1'
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeOwnershipClaim_Shape', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeOwnershipClaim WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeOwnershipClaim_Shape CHECK
        (
            TargetProfile = 'OTO_V1_BAK'
            AND ProofVersion = 1
            AND ProofId =
                'TYPED_OWNER_SQLSERVER_SQL_LATIN1_GENERAL_CP1_CI_AS_V1'
            AND
            (
                (
                    TableName = 'DM_DonViGTVT'
                    AND DmDonViGtvtMaDV IS NOT NULL
                    AND GiaoVienMaGV IS NULL
                    AND KhoaHocMaKH IS NULL
                    AND KhoaHocGiaoVienMaLichLV IS NULL
                    AND BaoCaoIMaBCI IS NULL
                    AND NguoiLXMaDK IS NULL
                    AND NguoiLXHoSoMaDK IS NULL
                    AND GiayToMaGT IS NULL
                    AND GiayToMaDK IS NULL
                )
                OR
                (
                    TableName = 'GiaoVien'
                    AND DmDonViGtvtMaDV IS NULL
                    AND GiaoVienMaGV IS NOT NULL
                    AND KhoaHocMaKH IS NULL
                    AND KhoaHocGiaoVienMaLichLV IS NULL
                    AND BaoCaoIMaBCI IS NULL
                    AND NguoiLXMaDK IS NULL
                    AND NguoiLXHoSoMaDK IS NULL
                    AND GiayToMaGT IS NULL
                    AND GiayToMaDK IS NULL
                )
                OR
                (
                    TableName = 'KhoaHoc'
                    AND DmDonViGtvtMaDV IS NULL
                    AND GiaoVienMaGV IS NULL
                    AND KhoaHocMaKH IS NOT NULL
                    AND KhoaHocGiaoVienMaLichLV IS NULL
                    AND BaoCaoIMaBCI IS NULL
                    AND NguoiLXMaDK IS NULL
                    AND NguoiLXHoSoMaDK IS NULL
                    AND GiayToMaGT IS NULL
                    AND GiayToMaDK IS NULL
                )
                OR
                (
                    TableName = 'KhoaHoc_GiaoVien'
                    AND DmDonViGtvtMaDV IS NULL
                    AND GiaoVienMaGV IS NULL
                    AND KhoaHocMaKH IS NULL
                    AND KhoaHocGiaoVienMaLichLV IS NOT NULL
                    AND BaoCaoIMaBCI IS NULL
                    AND NguoiLXMaDK IS NULL
                    AND NguoiLXHoSoMaDK IS NULL
                    AND GiayToMaGT IS NULL
                    AND GiayToMaDK IS NULL
                )
                OR
                (
                    TableName = 'BaoCaoI'
                    AND DmDonViGtvtMaDV IS NULL
                    AND GiaoVienMaGV IS NULL
                    AND KhoaHocMaKH IS NULL
                    AND KhoaHocGiaoVienMaLichLV IS NULL
                    AND BaoCaoIMaBCI IS NOT NULL
                    AND NguoiLXMaDK IS NULL
                    AND NguoiLXHoSoMaDK IS NULL
                    AND GiayToMaGT IS NULL
                    AND GiayToMaDK IS NULL
                )
                OR
                (
                    TableName = 'NguoiLX'
                    AND DmDonViGtvtMaDV IS NULL
                    AND GiaoVienMaGV IS NULL
                    AND KhoaHocMaKH IS NULL
                    AND KhoaHocGiaoVienMaLichLV IS NULL
                    AND BaoCaoIMaBCI IS NULL
                    AND NguoiLXMaDK IS NOT NULL
                    AND NguoiLXHoSoMaDK IS NULL
                    AND GiayToMaGT IS NULL
                    AND GiayToMaDK IS NULL
                )
                OR
                (
                    TableName = 'NguoiLX_HoSo'
                    AND DmDonViGtvtMaDV IS NULL
                    AND GiaoVienMaGV IS NULL
                    AND KhoaHocMaKH IS NULL
                    AND KhoaHocGiaoVienMaLichLV IS NULL
                    AND BaoCaoIMaBCI IS NULL
                    AND NguoiLXMaDK IS NULL
                    AND NguoiLXHoSoMaDK IS NOT NULL
                    AND GiayToMaGT IS NULL
                    AND GiayToMaDK IS NULL
                )
                OR
                (
                    TableName = 'NguoiLXHS_GiayTo'
                    AND DmDonViGtvtMaDV IS NULL
                    AND GiaoVienMaGV IS NULL
                    AND KhoaHocMaKH IS NULL
                    AND KhoaHocGiaoVienMaLichLV IS NULL
                    AND BaoCaoIMaBCI IS NULL
                    AND NguoiLXMaDK IS NULL
                    AND NguoiLXHoSoMaDK IS NULL
                    AND GiayToMaGT IS NOT NULL
                    AND GiayToMaDK IS NOT NULL
                )
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCycle_Route', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCycle WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCycle_Route CHECK
        (
            TargetProfile = 'OTO_V1_BAK'
            AND SourceProfile = 'OTO_V2_BAK'
            AND StreamCode = 'OTO_V2_TO_V1'
            AND MaCSDT = '66029'
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCycle_Status', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCycle WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCycle_Status CHECK
        (
            CycleStatus IN
            (
                'PREPARING', 'STAGED', 'VALIDATED', 'TARGET_COMMITTING',
                'TARGET_COMMITTED', 'CHECKPOINT_PUBLISHED', 'COMPLETE',
                'FAILED', 'CONFLICT'
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCycle_Versions', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCycle WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCycle_Versions CHECK
        (
            StartSourceVersion >= 0
            AND EndSourceVersion >= StartSourceVersion
            AND EnabledDomainCount BETWEEN 1 AND 8
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCycle_Timestamps', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCycle WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCycle_Timestamps CHECK
        (
            (StagedAtUtc IS NULL OR StagedAtUtc >= StartedAtUtc)
            AND (ValidatedAtUtc IS NULL OR (StagedAtUtc IS NOT NULL AND ValidatedAtUtc >= StagedAtUtc))
            AND
            (
                TargetCommittedAtUtc IS NULL
                OR (ValidatedAtUtc IS NOT NULL AND TargetCommittedAtUtc >= ValidatedAtUtc)
            )
            AND
            (
                CheckpointPublishedAtUtc IS NULL
                OR
                (
                    TargetCommittedAtUtc IS NOT NULL
                    AND CheckpointPublishedAtUtc >= TargetCommittedAtUtc
                )
            )
            AND
            (
                CompletedAtUtc IS NULL
                OR
                (
                    CompletedAtUtc >= StartedAtUtc
                    AND (StagedAtUtc IS NULL OR CompletedAtUtc >= StagedAtUtc)
                    AND (ValidatedAtUtc IS NULL OR CompletedAtUtc >= ValidatedAtUtc)
                    AND
                    (
                        TargetCommittedAtUtc IS NULL
                        OR CompletedAtUtc >= TargetCommittedAtUtc
                    )
                    AND
                    (
                        CheckpointPublishedAtUtc IS NULL
                        OR CompletedAtUtc >= CheckpointPublishedAtUtc
                    )
                )
            )
            AND
            (CycleStatus IN ('PREPARING', 'FAILED', 'CONFLICT') OR StagedAtUtc IS NOT NULL)
            AND
            (
                CycleStatus IN ('PREPARING', 'STAGED', 'FAILED', 'CONFLICT')
                OR ValidatedAtUtc IS NOT NULL
            )
            AND
            (
                CycleStatus IN
                (
                    'PREPARING', 'STAGED', 'VALIDATED', 'TARGET_COMMITTING',
                    'FAILED', 'CONFLICT'
                )
                OR TargetCommittedAtUtc IS NOT NULL
            )
            AND
            (
                CycleStatus IN
                (
                    'PREPARING', 'STAGED', 'VALIDATED', 'TARGET_COMMITTING',
                    'TARGET_COMMITTED', 'FAILED', 'CONFLICT'
                )
                OR CheckpointPublishedAtUtc IS NOT NULL
            )
            AND
            (
                CycleStatus NOT IN ('COMPLETE', 'FAILED', 'CONFLICT')
                OR CompletedAtUtc IS NOT NULL
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCycle_Error', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCycle WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCycle_Error CHECK
        (
            (
                CycleStatus IN ('FAILED', 'CONFLICT')
                AND ErrorCode IN
                (
                    'CYCLE_FAILED', 'CYCLE_CONFLICT', 'BOOTSTRAP_INCOMPLETE',
                    'DOMAIN_INCOMPLETE', 'MAPPING_FINGERPRINT_MISMATCH',
                    'ROUTE_FINGERPRINT_MISMATCH', 'TARGET_EQUALITY_UNPROVEN',
                    'SOURCE_VERSION_REGRESSION', 'TARGET_COMMIT_NOT_VERIFIED',
                    'DELETE_EXECUTION_NOT_ENABLED', 'TARGET_LOCK_TIMEOUT',
                    'COVERAGE_INCOMPLETE', 'CHECKPOINT_CONFLICT',
                    'BOOTSTRAP_PARENT_MISSING'
                )
            )
            OR
            (
                CycleStatus NOT IN ('FAILED', 'CONFLICT')
                AND ErrorCode IS NULL
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeDomain_Name', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCycleDomain WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeDomain_Name CHECK
        (
            DomainName IN
            (
                'DM_DonViGTVT', 'GiaoVien', 'KhoaHoc', 'KhoaHoc_GiaoVien',
                'BaoCaoI', 'NguoiLX', 'NguoiLX_HoSo', 'NguoiLXHS_GiayTo'
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeDomain_Status', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCycleDomain WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeDomain_Status CHECK
        (
            DomainStatus IN
            ('PENDING', 'STAGED', 'VALIDATED', 'COMMITTED', 'FAILED', 'CONFLICT', 'SKIPPED')
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeDomain_Counts', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCycleDomain WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeDomain_Counts CHECK
        (
            SourceRowCount >= 0
            AND InsertCount >= 0
            AND UpdateCount >= 0
            AND DeleteCount >= 0
            AND PreservedExcludedCount >= 0
            AND ConflictCount >= 0
            AND
            (
                (
                    DomainStatus IN ('PENDING', 'STAGED', 'VALIDATED')
                    AND ResultHash IS NULL
                    AND ErrorCode IS NULL
                    AND CompletedAtUtc IS NULL
                )
                OR
                (
                    DomainStatus = 'COMMITTED'
                    AND ResultHash IS NOT NULL
                    AND ErrorCode IS NULL
                    AND CompletedAtUtc IS NOT NULL
                    AND CompletedAtUtc >= StartedAtUtc
                )
                OR
                (
                    DomainStatus IN ('FAILED', 'CONFLICT')
                    AND ResultHash IS NULL
                    AND ErrorCode IN
                    (
                        'CYCLE_FAILED', 'CYCLE_CONFLICT', 'BOOTSTRAP_INCOMPLETE',
                        'DOMAIN_INCOMPLETE', 'MAPPING_FINGERPRINT_MISMATCH',
                        'ROUTE_FINGERPRINT_MISMATCH', 'TARGET_EQUALITY_UNPROVEN',
                        'SOURCE_VERSION_REGRESSION', 'TARGET_COMMIT_NOT_VERIFIED',
                        'DELETE_EXECUTION_NOT_ENABLED', 'TARGET_LOCK_TIMEOUT',
                        'COVERAGE_INCOMPLETE', 'CHECKPOINT_CONFLICT',
                        'BOOTSTRAP_PARENT_MISSING'
                    )
                    AND CompletedAtUtc IS NOT NULL
                    AND CompletedAtUtc >= StartedAtUtc
                )
                OR
                (
                    DomainStatus = 'SKIPPED'
                    AND ResultHash IS NULL
                    AND ErrorCode IS NULL
                    AND CompletedAtUtc IS NOT NULL
                    AND CompletedAtUtc >= StartedAtUtc
                )
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeJournal_Status', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeMembershipJournal WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeJournal_Status CHECK
        (
            BeforeStatus IN
            (
                'ABSENT', 'INSERT_PENDING', 'ACTIVE', 'DELETE_PENDING',
                'INACTIVE', 'REACTIVATE_PENDING', 'CONFLICT'
            )
            AND AfterStatus IN
            (
                'INSERT_PENDING', 'ACTIVE', 'DELETE_PENDING',
                'INACTIVE', 'REACTIVATE_PENDING', 'CONFLICT'
            )
            AND SourceVersion >= 0
            AND HashKeyVersion > 0
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeJournal_Action', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeMembershipJournal WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeJournal_Action CHECK
        (
            TargetAction IN
            ('NONE', 'UPSERTED', 'EXISTING_VERIFIED', 'HARD_DELETED', 'PRESERVED_EXCLUDED')
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeJournal_Reason', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeMembershipJournal WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeJournal_Reason CHECK
        (
            ReasonCode IN
            (
                'NONE', 'SOURCE_PRESENT', 'SOURCE_DELETE', 'FULL_RECONCILE_ABSENT',
                'REACTIVATED_AT_SOURCE', 'TARGET_ACTION_APPLIED', 'DUPLICATE_REPLAY',
                'LATE_SOURCE_EVENT', 'STREAM_OWNERSHIP_CONFLICT',
                'MAPPING_FINGERPRINT_MISMATCH', 'ROUTE_FINGERPRINT_MISMATCH',
                'TARGET_EQUALITY_UNPROVEN', 'BOOTSTRAP_INCOMPLETE',
                'UNOWNED_DELETE_KEY', 'BLOCK_DELETE_CONFLICT', 'MANUAL_CONFLICT',
                'BOOTSTRAP_MEMBERSHIP_CREATED', 'BOOTSTRAP_MEMBERSHIP_VERIFIED',
                'SOURCE_ROW_OBSERVED', 'CT_DELETE_OBSERVED',
                'DELETE_PENDING_NOT_APPLIED', 'REACTIVATION_CANDIDATE',
                'TARGET_ONLY_UNCLASSIFIED', 'OWNERSHIP_CONFLICT',
                'COVERAGE_COMPLETE', 'CHECKPOINT_CONFLICT',
                'BOOTSTRAP_PARENT_MISSING'
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCoverage_Route', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeStreamCoverage WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCoverage_Route CHECK
        (
            TargetProfile = 'OTO_V1_BAK'
            AND SourceProfile = 'OTO_V2_BAK'
            AND StreamCode = 'OTO_V2_TO_V1'
            AND MaCSDT = '66029'
            AND TableName IN
            (
                'DM_DonViGTVT', 'GiaoVien', 'KhoaHoc', 'KhoaHoc_GiaoVien',
                'BaoCaoI', 'NguoiLX', 'NguoiLX_HoSo', 'NguoiLXHS_GiayTo'
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCoverage_State', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeStreamCoverage WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCoverage_State CHECK
        (
            BaselineSourceVersion >= 0
            AND MembershipCount >= 0
            AND
            (
                (IsComplete = 0 AND CompletedCycleId IS NULL AND CompletedAtUtc IS NULL)
                OR
                (IsComplete = 1 AND CompletedCycleId IS NOT NULL AND CompletedAtUtc IS NOT NULL)
            )
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCheckpoint_Route', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCheckpoint WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCheckpoint_Route CHECK
        (
            TargetProfile = 'OTO_V1_BAK'
            AND SourceProfile = 'OTO_V2_BAK'
            AND StreamCode = 'OTO_V2_TO_V1'
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCheckpoint_Status', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCheckpoint WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCheckpoint_Status CHECK
        (
            CheckpointStatus IN ('ACTIVE', 'CONFLICT', 'DISABLED')
        );

    IF OBJECT_ID(N'dbo.CK_QLHV_CsdtRealtimeCheckpoint_State', N'C') IS NULL
        ALTER TABLE dbo.QLHV_CsdtRealtimeCheckpoint WITH CHECK
        ADD CONSTRAINT CK_QLHV_CsdtRealtimeCheckpoint_State CHECK
        (
            AppliedSourceVersion >= 0
            AND
            (
                VerifiedAtUtc IS NULL
                OR VerifiedAtUtc >= PublishedAtUtc
            )
        );

    DECLARE @RequiredCheckContracts table
    (
        ConstraintName sysname NOT NULL,
        ParentTable sysname NOT NULL,
        ExpectedLiteralCount smallint NOT NULL,
        RequiredLiterals nvarchar(max) NOT NULL,
        RequiredColumns nvarchar(max) NOT NULL
    );

    INSERT INTO @RequiredCheckContracts
        (ConstraintName, ParentTable, ExpectedLiteralCount,
         RequiredLiterals, RequiredColumns)
    VALUES
        (N'CK_QLHV_CsdtRealtimeMembership_Route',
         N'QLHV_CsdtRealtimeSourceMembership', 4,
         N'OTO_V1_BAK|OTO_V2_BAK|OTO_V2_TO_V1|66029',
         N'TargetProfile|SourceProfile|StreamCode|MaCSDT'),
        (N'CK_QLHV_CsdtRealtimeMembership_Table',
         N'QLHV_CsdtRealtimeSourceMembership', 8,
         N'DM_DonViGTVT|GiaoVien|KhoaHoc|KhoaHoc_GiaoVien|BaoCaoI|NguoiLX|NguoiLX_HoSo|NguoiLXHS_GiayTo',
         N'TableName'),
        (N'CK_QLHV_CsdtRealtimeMembership_Status',
         N'QLHV_CsdtRealtimeSourceMembership', 6,
         N'INSERT_PENDING|ACTIVE|DELETE_PENDING|INACTIVE|REACTIVATE_PENDING|CONFLICT',
         N'MembershipStatus'),
        (N'CK_QLHV_CsdtRealtimeMembership_Action',
         N'QLHV_CsdtRealtimeSourceMembership', 5,
         N'NONE|UPSERTED|EXISTING_VERIFIED|HARD_DELETED|PRESERVED_EXCLUDED',
         N'TargetAction'),
        (N'CK_QLHV_CsdtRealtimeMembership_Reason',
         N'QLHV_CsdtRealtimeSourceMembership', 27,
         N'NONE|SOURCE_PRESENT|SOURCE_DELETE|FULL_RECONCILE_ABSENT|REACTIVATED_AT_SOURCE|TARGET_ACTION_APPLIED|DUPLICATE_REPLAY|LATE_SOURCE_EVENT|STREAM_OWNERSHIP_CONFLICT|MAPPING_FINGERPRINT_MISMATCH|ROUTE_FINGERPRINT_MISMATCH|TARGET_EQUALITY_UNPROVEN|BOOTSTRAP_INCOMPLETE|UNOWNED_DELETE_KEY|BLOCK_DELETE_CONFLICT|MANUAL_CONFLICT|BOOTSTRAP_MEMBERSHIP_CREATED|BOOTSTRAP_MEMBERSHIP_VERIFIED|SOURCE_ROW_OBSERVED|CT_DELETE_OBSERVED|DELETE_PENDING_NOT_APPLIED|REACTIVATION_CANDIDATE|TARGET_ONLY_UNCLASSIFIED|OWNERSHIP_CONFLICT|COVERAGE_COMPLETE|CHECKPOINT_CONFLICT|BOOTSTRAP_PARENT_MISSING',
         N'ReasonCode'),
        (N'CK_QLHV_CsdtRealtimeMembership_State',
         N'QLHV_CsdtRealtimeSourceMembership', 6,
         N'ACTIVE|INSERT_PENDING|REACTIVATE_PENDING|DELETE_PENDING|INACTIVE|CONFLICT',
         N'OwnershipReserved|MembershipStatus|IsActive|ClaimsTargetKey'),
        (N'CK_QLHV_CsdtRealtimeMembership_Versions',
         N'QLHV_CsdtRealtimeSourceMembership', 6,
         N'INSERT_PENDING|ACTIVE|DELETE_PENDING|INACTIVE|REACTIVATE_PENDING|CONFLICT',
         N'KeySchemaVersion|HashKeyVersion|OwnershipEpoch|LastObservedSourceVersion|AppliedSourceVersion|DeletedAtSourceVersion|ReactivatedAtSourceVersion|MembershipStatus'),
        (N'CK_QLHV_CsdtRealtimeMembership_TargetEquality',
         N'QLHV_CsdtRealtimeSourceMembership', 2,
         N'TYPED_CLAIM|TYPED_OWNER_SQLSERVER_SQL_LATIN1_GENERAL_CP1_CI_AS_V1',
         N'TargetEqualityProofStatus|TargetEqualityProofId'),
        (N'CK_QLHV_CsdtRealtimeOwnershipClaim_Shape',
         N'QLHV_CsdtRealtimeOwnershipClaim', 10,
         N'OTO_V1_BAK|TYPED_OWNER_SQLSERVER_SQL_LATIN1_GENERAL_CP1_CI_AS_V1|DM_DonViGTVT|GiaoVien|KhoaHoc|KhoaHoc_GiaoVien|BaoCaoI|NguoiLX|NguoiLX_HoSo|NguoiLXHS_GiayTo',
         N'TargetProfile|TableName|ProofVersion|ProofId|DmDonViGtvtMaDV|GiaoVienMaGV|KhoaHocMaKH|KhoaHocGiaoVienMaLichLV|BaoCaoIMaBCI|NguoiLXMaDK|NguoiLXHoSoMaDK|GiayToMaGT|GiayToMaDK'),
        (N'CK_QLHV_CsdtRealtimeCycle_Route',
         N'QLHV_CsdtRealtimeCycle', 4,
         N'OTO_V1_BAK|OTO_V2_BAK|OTO_V2_TO_V1|66029',
         N'TargetProfile|SourceProfile|StreamCode|MaCSDT'),
        (N'CK_QLHV_CsdtRealtimeCycle_Status',
         N'QLHV_CsdtRealtimeCycle', 9,
         N'PREPARING|STAGED|VALIDATED|TARGET_COMMITTING|TARGET_COMMITTED|CHECKPOINT_PUBLISHED|COMPLETE|FAILED|CONFLICT',
         N'CycleStatus'),
        (N'CK_QLHV_CsdtRealtimeCycle_Versions',
         N'QLHV_CsdtRealtimeCycle', 0, N'',
         N'StartSourceVersion|EndSourceVersion|EnabledDomainCount'),
        (N'CK_QLHV_CsdtRealtimeCycle_Timestamps',
         N'QLHV_CsdtRealtimeCycle', 23,
         N'PREPARING|STAGED|VALIDATED|TARGET_COMMITTING|TARGET_COMMITTED|CHECKPOINT_PUBLISHED|COMPLETE|FAILED|CONFLICT',
         N'CycleStatus|StartedAtUtc|StagedAtUtc|ValidatedAtUtc|TargetCommittedAtUtc|CheckpointPublishedAtUtc|CompletedAtUtc'),
        (N'CK_QLHV_CsdtRealtimeCycle_Error',
         N'QLHV_CsdtRealtimeCycle', 18,
         N'FAILED|CONFLICT|CYCLE_FAILED|CYCLE_CONFLICT|BOOTSTRAP_INCOMPLETE|DOMAIN_INCOMPLETE|MAPPING_FINGERPRINT_MISMATCH|ROUTE_FINGERPRINT_MISMATCH|TARGET_EQUALITY_UNPROVEN|SOURCE_VERSION_REGRESSION|TARGET_COMMIT_NOT_VERIFIED|DELETE_EXECUTION_NOT_ENABLED|TARGET_LOCK_TIMEOUT|COVERAGE_INCOMPLETE|CHECKPOINT_CONFLICT|BOOTSTRAP_PARENT_MISSING',
         N'CycleStatus|ErrorCode'),
        (N'CK_QLHV_CsdtRealtimeDomain_Name',
         N'QLHV_CsdtRealtimeCycleDomain', 8,
         N'DM_DonViGTVT|GiaoVien|KhoaHoc|KhoaHoc_GiaoVien|BaoCaoI|NguoiLX|NguoiLX_HoSo|NguoiLXHS_GiayTo',
         N'DomainName'),
        (N'CK_QLHV_CsdtRealtimeDomain_Status',
         N'QLHV_CsdtRealtimeCycleDomain', 7,
         N'PENDING|STAGED|VALIDATED|COMMITTED|FAILED|CONFLICT|SKIPPED',
         N'DomainStatus'),
        (N'CK_QLHV_CsdtRealtimeDomain_Counts',
         N'QLHV_CsdtRealtimeCycleDomain', 21,
         N'PENDING|STAGED|VALIDATED|COMMITTED|FAILED|CONFLICT|SKIPPED|CYCLE_FAILED|CYCLE_CONFLICT|BOOTSTRAP_INCOMPLETE|DOMAIN_INCOMPLETE|MAPPING_FINGERPRINT_MISMATCH|ROUTE_FINGERPRINT_MISMATCH|TARGET_EQUALITY_UNPROVEN|SOURCE_VERSION_REGRESSION|TARGET_COMMIT_NOT_VERIFIED|DELETE_EXECUTION_NOT_ENABLED|TARGET_LOCK_TIMEOUT|COVERAGE_INCOMPLETE|CHECKPOINT_CONFLICT|BOOTSTRAP_PARENT_MISSING',
         N'DomainStatus|SourceRowCount|InsertCount|UpdateCount|DeleteCount|PreservedExcludedCount|ConflictCount|ResultHash|ErrorCode|StartedAtUtc|CompletedAtUtc'),
        (N'CK_QLHV_CsdtRealtimeJournal_Status',
         N'QLHV_CsdtRealtimeMembershipJournal', 13,
         N'ABSENT|INSERT_PENDING|ACTIVE|DELETE_PENDING|INACTIVE|REACTIVATE_PENDING|CONFLICT',
         N'BeforeStatus|AfterStatus|SourceVersion|HashKeyVersion'),
        (N'CK_QLHV_CsdtRealtimeJournal_Action',
         N'QLHV_CsdtRealtimeMembershipJournal', 5,
         N'NONE|UPSERTED|EXISTING_VERIFIED|HARD_DELETED|PRESERVED_EXCLUDED',
         N'TargetAction'),
        (N'CK_QLHV_CsdtRealtimeJournal_Reason',
         N'QLHV_CsdtRealtimeMembershipJournal', 27,
         N'NONE|SOURCE_PRESENT|SOURCE_DELETE|FULL_RECONCILE_ABSENT|REACTIVATED_AT_SOURCE|TARGET_ACTION_APPLIED|DUPLICATE_REPLAY|LATE_SOURCE_EVENT|STREAM_OWNERSHIP_CONFLICT|MAPPING_FINGERPRINT_MISMATCH|ROUTE_FINGERPRINT_MISMATCH|TARGET_EQUALITY_UNPROVEN|BOOTSTRAP_INCOMPLETE|UNOWNED_DELETE_KEY|BLOCK_DELETE_CONFLICT|MANUAL_CONFLICT|BOOTSTRAP_MEMBERSHIP_CREATED|BOOTSTRAP_MEMBERSHIP_VERIFIED|SOURCE_ROW_OBSERVED|CT_DELETE_OBSERVED|DELETE_PENDING_NOT_APPLIED|REACTIVATION_CANDIDATE|TARGET_ONLY_UNCLASSIFIED|OWNERSHIP_CONFLICT|COVERAGE_COMPLETE|CHECKPOINT_CONFLICT|BOOTSTRAP_PARENT_MISSING',
         N'ReasonCode'),
        (N'CK_QLHV_CsdtRealtimeCoverage_Route',
         N'QLHV_CsdtRealtimeStreamCoverage', 12,
         N'OTO_V1_BAK|OTO_V2_BAK|OTO_V2_TO_V1|66029|DM_DonViGTVT|GiaoVien|KhoaHoc|KhoaHoc_GiaoVien|BaoCaoI|NguoiLX|NguoiLX_HoSo|NguoiLXHS_GiayTo',
         N'TargetProfile|SourceProfile|StreamCode|MaCSDT|TableName'),
        (N'CK_QLHV_CsdtRealtimeCoverage_State',
         N'QLHV_CsdtRealtimeStreamCoverage', 0, N'',
         N'BaselineSourceVersion|MembershipCount|IsComplete|CompletedCycleId|CompletedAtUtc'),
        (N'CK_QLHV_CsdtRealtimeCheckpoint_Route',
         N'QLHV_CsdtRealtimeCheckpoint', 3,
         N'OTO_V1_BAK|OTO_V2_BAK|OTO_V2_TO_V1',
         N'TargetProfile|SourceProfile|StreamCode'),
        (N'CK_QLHV_CsdtRealtimeCheckpoint_Status',
         N'QLHV_CsdtRealtimeCheckpoint', 3,
         N'ACTIVE|CONFLICT|DISABLED',
         N'CheckpointStatus'),
        (N'CK_QLHV_CsdtRealtimeCheckpoint_State',
         N'QLHV_CsdtRealtimeCheckpoint', 0, N'',
         N'AppliedSourceVersion|PublishedAtUtc|VerifiedAtUtc');

    IF EXISTS
    (
        SELECT 1
        FROM @RequiredCheckContracts AS required
        LEFT JOIN sys.check_constraints AS checkConstraint
          ON checkConstraint.name = required.ConstraintName
         AND checkConstraint.parent_object_id =
             OBJECT_ID(N'dbo.' + required.ParentTable, N'U')
        OUTER APPLY
        (
            SELECT LOWER
            (
                REPLACE
                (
                    REPLACE
                    (
                        REPLACE
                        (
                            REPLACE
                            (
                                REPLACE(checkConstraint.definition, N'[', N''),
                                N']',
                                N''
                            ),
                            N' ',
                            N''
                        ),
                        CHAR(9),
                        N''
                    ),
                    CHAR(10),
                    N''
                )
            ) AS NormalizedDefinition
        ) AS normalized
        WHERE checkConstraint.object_id IS NULL
           OR checkConstraint.is_disabled = 1
           OR checkConstraint.is_not_trusted = 1
           OR
           (
               LEN(checkConstraint.definition) -
               LEN(REPLACE(checkConstraint.definition, N'''', N''))
           ) / 2 <> required.ExpectedLiteralCount
           OR EXISTS
              (
                  SELECT 1
                  FROM STRING_SPLIT(required.RequiredLiterals, N'|') AS literal
                  WHERE required.RequiredLiterals <> N''
                    AND checkConstraint.definition NOT LIKE
                        N'%''' + literal.value + N'''%'
              )
           OR EXISTS
              (
                  SELECT 1
                  FROM STRING_SPLIT(required.RequiredColumns, N'|') AS requiredColumn
                  WHERE normalized.NormalizedDefinition NOT LIKE
                        N'%' + LOWER(requiredColumn.value) + N'%'
              )
    )
    OR
    (
        SELECT COUNT(*)
        FROM sys.check_constraints AS checkConstraint
        WHERE checkConstraint.name IN
        (
            SELECT ConstraintName
            FROM @RequiredCheckContracts
        )
    ) <> (SELECT COUNT(*) FROM @RequiredCheckContracts)
        THROW 527617, 'An existing CSDT control-plane check constraint has an incompatible parent, literal set, column set, enabled, or trust shape.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS checkConstraint
        WHERE checkConstraint.parent_object_id IN
        (
            SELECT OBJECT_ID(N'dbo.' + requiredTable.TableName, N'U')
            FROM
            (
                SELECT DISTINCT TableName
                FROM @RequiredColumns
            ) AS requiredTable
        )
          AND checkConstraint.name NOT IN
          (
              SELECT ConstraintName
              FROM @RequiredCheckContracts
          )
    )
        THROW 527617, 'An existing CSDT control-plane table has an unexpected check constraint.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeMembership_RouteKey'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeMembership_RouteKey
        ON dbo.QLHV_CsdtRealtimeSourceMembership
        (
            TargetProfile, SourceProfile, StreamCode, TableName,
            KeySchemaVersion, CanonicalBusinessKey
        );

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U')
          AND name = N'IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken'
    )
        CREATE NONCLUSTERED INDEX IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken
        ON dbo.QLHV_CsdtRealtimeSourceMembership
        (TargetProfile, TableName, KeySchemaVersion, TargetEqualityKey)
        INCLUDE (MembershipId, OwnershipReserved);

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U')
          AND name = N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup'
    )
        CREATE NONCLUSTERED INDEX IX_QLHV_CsdtRealtimeMembership_ActiveLookup
        ON dbo.QLHV_CsdtRealtimeSourceMembership
        (
            TargetProfile, SourceProfile, StreamCode, TableName,
            IsActive, MembershipStatus, KeySchemaVersion, CanonicalBusinessKey
        )
        INCLUDE
        (
            MembershipId, ClaimsTargetKey, OwnershipReserved,
            LastObservedSourceVersion, AppliedSourceVersion
        );

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_Membership'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_Membership
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (MembershipId);

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_DmDonViGtvt'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_DmDonViGtvt
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (DmDonViGtvtMaDV)
        WHERE DmDonViGtvtMaDV IS NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_GiaoVien'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_GiaoVien
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (GiaoVienMaGV)
        WHERE GiaoVienMaGV IS NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_KhoaHoc'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_KhoaHoc
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (KhoaHocMaKH)
        WHERE KhoaHocMaKH IS NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_KhoaHocGiaoVien'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_KhoaHocGiaoVien
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (KhoaHocGiaoVienMaLichLV)
        WHERE KhoaHocGiaoVienMaLichLV IS NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_BaoCaoI'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_BaoCaoI
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (BaoCaoIMaBCI)
        WHERE BaoCaoIMaBCI IS NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_NguoiLX'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_NguoiLX
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (NguoiLXMaDK)
        WHERE NguoiLXMaDK IS NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_NguoiLXHoSo'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_NguoiLXHoSo
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (NguoiLXHoSoMaDK)
        WHERE NguoiLXHoSoMaDK IS NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeOwnershipClaim_GiayTo'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeOwnershipClaim_GiayTo
        ON dbo.QLHV_CsdtRealtimeOwnershipClaim (GiayToMaGT, GiayToMaDK)
        WHERE GiayToMaGT IS NOT NULL AND GiayToMaDK IS NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeMembershipJournal', N'U')
          AND name = N'IX_QLHV_CsdtRealtimeJournal_Membership'
    )
        CREATE NONCLUSTERED INDEX IX_QLHV_CsdtRealtimeJournal_Membership
        ON dbo.QLHV_CsdtRealtimeMembershipJournal
        (MembershipId, JournalId);

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeMembershipJournal', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeJournal_Event'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeJournal_Event
        ON dbo.QLHV_CsdtRealtimeMembershipJournal
        (
            MembershipId, CycleId, BeforeStatus, AfterStatus,
            SourceVersion, ReasonCode, TargetAction
        );

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeMembershipJournal', N'U')
          AND name = N'IX_QLHV_CsdtRealtimeJournal_Cycle'
    )
        CREATE NONCLUSTERED INDEX IX_QLHV_CsdtRealtimeJournal_Cycle
        ON dbo.QLHV_CsdtRealtimeMembershipJournal
        (CycleId, JournalId);

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeCheckpoint', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeCheckpoint_Stream'
    )
        CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeCheckpoint_Stream
        ON dbo.QLHV_CsdtRealtimeCheckpoint
        (TargetProfile, SourceProfile, StreamCode);

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U')
          AND name IN
          (
              N'UX_QLHV_CsdtRealtimeMembership_RouteKey'
          )
          AND is_unique = 0
    )
        THROW 527603, 'An existing CSDT control-plane unique index is incompatible.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexMetadata
        WHERE indexMetadata.object_id =
              OBJECT_ID(N'dbo.QLHV_CsdtRealtimeMembershipJournal', N'U')
          AND indexMetadata.name = N'UX_QLHV_CsdtRealtimeJournal_Event'
          AND indexMetadata.is_unique = 1
          AND indexMetadata.has_filter = 0
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeMembershipJournal',
                  indexMetadata.index_id,
                  1
              ) = N'MembershipId'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeMembershipJournal',
                  indexMetadata.index_id,
                  2
              ) = N'CycleId'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeMembershipJournal',
                  indexMetadata.index_id,
                  7
              ) = N'TargetAction'
          AND
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexMetadata.object_id
                AND indexColumn.index_id = indexMetadata.index_id
                AND indexColumn.key_ordinal > 0
          ) = 7
    )
        THROW 527616, 'Membership journal idempotency index has an incompatible shape.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexMetadata
        WHERE indexMetadata.object_id =
              OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U')
          AND indexMetadata.name = N'UX_QLHV_CsdtRealtimeMembership_RouteKey'
          AND indexMetadata.is_unique = 1
          AND indexMetadata.has_filter = 0
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  1
              ) = N'TargetProfile'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  2
              ) = N'SourceProfile'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  3
              ) = N'StreamCode'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  4
              ) = N'TableName'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  5
              ) = N'KeySchemaVersion'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  6
              ) = N'CanonicalBusinessKey'
          AND
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexMetadata.object_id
                AND indexColumn.index_id = indexMetadata.index_id
                AND indexColumn.key_ordinal > 0
          ) = 6
    )
        THROW 527605, 'Route membership uniqueness index has an incompatible shape.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexMetadata
        WHERE indexMetadata.object_id =
              OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U')
          AND indexMetadata.name = N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup'
          AND indexMetadata.is_unique = 0
          AND indexMetadata.has_filter = 0
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  1
              ) = N'TargetProfile'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  2
              ) = N'SourceProfile'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  3
              ) = N'StreamCode'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  4
              ) = N'TableName'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  5
              ) = N'IsActive'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  6
              ) = N'MembershipStatus'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  7
              ) = N'KeySchemaVersion'
          AND INDEX_COL
              (
                  N'dbo.QLHV_CsdtRealtimeSourceMembership',
                  indexMetadata.index_id,
                  8
              ) = N'CanonicalBusinessKey'
    )
        THROW 527607, 'Active membership lookup index has an incompatible shape.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U')
          AND name = N'UX_QLHV_CsdtRealtimeMembership_TargetOwner'
    )
        THROW 527606, 'Legacy varbinary target-owner uniqueness is not authoritative; typed ownership migration is required.', 1;

    DECLARE @RequiredIndexes table
    (
        IndexName sysname NOT NULL,
        TableName sysname NOT NULL,
        IsUnique bit NOT NULL,
        IndexType tinyint NOT NULL,
        HasFilter bit NOT NULL,
        NormalizedFilter nvarchar(300) NULL
    );

    INSERT INTO @RequiredIndexes
        (IndexName, TableName, IsUnique, IndexType, HasFilter, NormalizedFilter)
    VALUES
        (N'UX_QLHV_CsdtRealtimeMembership_RouteKey',
         N'QLHV_CsdtRealtimeSourceMembership', 1, 2, 0, NULL),
        (N'IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken',
         N'QLHV_CsdtRealtimeSourceMembership', 0, 2, 0, NULL),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup',
         N'QLHV_CsdtRealtimeSourceMembership', 0, 2, 0, NULL),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_Membership',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 0, NULL),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_DmDonViGtvt',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 1,
         N'dmdonvigtvtmadvisnotnull'),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_GiaoVien',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 1,
         N'giaovienmagvisnotnull'),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_KhoaHoc',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 1,
         N'khoahocmakhisnotnull'),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_KhoaHocGiaoVien',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 1,
         N'khoahocgiaovienmalichlvisnotnull'),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_BaoCaoI',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 1,
         N'baocaoimabciisnotnull'),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_NguoiLX',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 1,
         N'nguoilxmadkisnotnull'),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_NguoiLXHoSo',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 1,
         N'nguoilxhosomadkisnotnull'),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_GiayTo',
         N'QLHV_CsdtRealtimeOwnershipClaim', 1, 2, 1,
         N'giaytomagtisnotnullandgiaytomadkisnotnull'),
        (N'IX_QLHV_CsdtRealtimeJournal_Membership',
         N'QLHV_CsdtRealtimeMembershipJournal', 0, 2, 0, NULL),
        (N'UX_QLHV_CsdtRealtimeJournal_Event',
         N'QLHV_CsdtRealtimeMembershipJournal', 1, 2, 0, NULL),
        (N'IX_QLHV_CsdtRealtimeJournal_Cycle',
         N'QLHV_CsdtRealtimeMembershipJournal', 0, 2, 0, NULL),
        (N'UX_QLHV_CsdtRealtimeCheckpoint_Stream',
         N'QLHV_CsdtRealtimeCheckpoint', 1, 2, 0, NULL);

    DECLARE @RequiredIndexColumns table
    (
        IndexName sysname NOT NULL,
        KeyOrdinal tinyint NOT NULL,
        ColumnName sysname NOT NULL,
        IsIncluded bit NOT NULL,
        IsDescending bit NOT NULL
    );

    INSERT INTO @RequiredIndexColumns
        (IndexName, KeyOrdinal, ColumnName, IsIncluded, IsDescending)
    VALUES
        (N'UX_QLHV_CsdtRealtimeMembership_RouteKey', 1, N'TargetProfile', 0, 0),
        (N'UX_QLHV_CsdtRealtimeMembership_RouteKey', 2, N'SourceProfile', 0, 0),
        (N'UX_QLHV_CsdtRealtimeMembership_RouteKey', 3, N'StreamCode', 0, 0),
        (N'UX_QLHV_CsdtRealtimeMembership_RouteKey', 4, N'TableName', 0, 0),
        (N'UX_QLHV_CsdtRealtimeMembership_RouteKey', 5, N'KeySchemaVersion', 0, 0),
        (N'UX_QLHV_CsdtRealtimeMembership_RouteKey', 6, N'CanonicalBusinessKey', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken', 1, N'TargetProfile', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken', 2, N'TableName', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken', 3, N'KeySchemaVersion', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken', 4, N'TargetEqualityKey', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken', 0, N'MembershipId', 1, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken', 0, N'OwnershipReserved', 1, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 1, N'TargetProfile', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 2, N'SourceProfile', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 3, N'StreamCode', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 4, N'TableName', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 5, N'IsActive', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 6, N'MembershipStatus', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 7, N'KeySchemaVersion', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 8, N'CanonicalBusinessKey', 0, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 0, N'MembershipId', 1, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 0, N'ClaimsTargetKey', 1, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 0, N'OwnershipReserved', 1, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 0, N'LastObservedSourceVersion', 1, 0),
        (N'IX_QLHV_CsdtRealtimeMembership_ActiveLookup', 0, N'AppliedSourceVersion', 1, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_Membership', 1, N'MembershipId', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_DmDonViGtvt', 1, N'DmDonViGtvtMaDV', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_GiaoVien', 1, N'GiaoVienMaGV', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_KhoaHoc', 1, N'KhoaHocMaKH', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_KhoaHocGiaoVien', 1, N'KhoaHocGiaoVienMaLichLV', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_BaoCaoI', 1, N'BaoCaoIMaBCI', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_NguoiLX', 1, N'NguoiLXMaDK', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_NguoiLXHoSo', 1, N'NguoiLXHoSoMaDK', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_GiayTo', 1, N'GiayToMaGT', 0, 0),
        (N'UX_QLHV_CsdtRealtimeOwnershipClaim_GiayTo', 2, N'GiayToMaDK', 0, 0),
        (N'IX_QLHV_CsdtRealtimeJournal_Membership', 1, N'MembershipId', 0, 0),
        (N'IX_QLHV_CsdtRealtimeJournal_Membership', 2, N'JournalId', 0, 0),
        (N'UX_QLHV_CsdtRealtimeJournal_Event', 1, N'MembershipId', 0, 0),
        (N'UX_QLHV_CsdtRealtimeJournal_Event', 2, N'CycleId', 0, 0),
        (N'UX_QLHV_CsdtRealtimeJournal_Event', 3, N'BeforeStatus', 0, 0),
        (N'UX_QLHV_CsdtRealtimeJournal_Event', 4, N'AfterStatus', 0, 0),
        (N'UX_QLHV_CsdtRealtimeJournal_Event', 5, N'SourceVersion', 0, 0),
        (N'UX_QLHV_CsdtRealtimeJournal_Event', 6, N'ReasonCode', 0, 0),
        (N'UX_QLHV_CsdtRealtimeJournal_Event', 7, N'TargetAction', 0, 0),
        (N'IX_QLHV_CsdtRealtimeJournal_Cycle', 1, N'CycleId', 0, 0),
        (N'IX_QLHV_CsdtRealtimeJournal_Cycle', 2, N'JournalId', 0, 0),
        (N'UX_QLHV_CsdtRealtimeCheckpoint_Stream', 1, N'TargetProfile', 0, 0),
        (N'UX_QLHV_CsdtRealtimeCheckpoint_Stream', 2, N'SourceProfile', 0, 0),
        (N'UX_QLHV_CsdtRealtimeCheckpoint_Stream', 3, N'StreamCode', 0, 0);

    IF EXISTS
    (
        SELECT 1
        FROM @RequiredIndexes AS required
        LEFT JOIN sys.indexes AS indexMetadata
          ON indexMetadata.object_id =
             OBJECT_ID(N'dbo.' + required.TableName, N'U')
         AND indexMetadata.name = required.IndexName
        OUTER APPLY
        (
            SELECT LOWER
            (
                REPLACE
                (
                    REPLACE
                    (
                        REPLACE
                        (
                            REPLACE
                            (
                                REPLACE(indexMetadata.filter_definition, N'[', N''),
                                N']',
                                N''
                            ),
                            N'(',
                            N''
                        ),
                        N')',
                        N''
                    ),
                    N' ',
                    N''
                )
            ) AS NormalizedFilter
        ) AS normalized
        WHERE indexMetadata.index_id IS NULL
           OR indexMetadata.is_unique <> required.IsUnique
           OR indexMetadata.type <> required.IndexType
           OR indexMetadata.has_filter <> required.HasFilter
           OR indexMetadata.is_disabled = 1
           OR indexMetadata.is_hypothetical = 1
           OR ISNULL(normalized.NormalizedFilter, N'') <>
              ISNULL(required.NormalizedFilter, N'')
    )
    OR EXISTS
    (
        SELECT
            required.IndexName,
            required.KeyOrdinal,
            required.ColumnName,
            required.IsIncluded,
            required.IsDescending
        FROM @RequiredIndexColumns AS required
        EXCEPT
        SELECT
            indexMetadata.name,
            CONVERT(tinyint, indexColumn.key_ordinal),
            columnMetadata.name,
            indexColumn.is_included_column,
            indexColumn.is_descending_key
        FROM sys.indexes AS indexMetadata
        INNER JOIN @RequiredIndexes AS requiredIndex
          ON requiredIndex.IndexName = indexMetadata.name
         AND indexMetadata.object_id =
             OBJECT_ID(N'dbo.' + requiredIndex.TableName, N'U')
        INNER JOIN sys.index_columns AS indexColumn
          ON indexColumn.object_id = indexMetadata.object_id
         AND indexColumn.index_id = indexMetadata.index_id
        INNER JOIN sys.columns AS columnMetadata
          ON columnMetadata.object_id = indexColumn.object_id
         AND columnMetadata.column_id = indexColumn.column_id
    )
    OR EXISTS
    (
        SELECT
            indexMetadata.name,
            CONVERT(tinyint, indexColumn.key_ordinal),
            columnMetadata.name,
            indexColumn.is_included_column,
            indexColumn.is_descending_key
        FROM sys.indexes AS indexMetadata
        INNER JOIN @RequiredIndexes AS requiredIndex
          ON requiredIndex.IndexName = indexMetadata.name
         AND indexMetadata.object_id =
             OBJECT_ID(N'dbo.' + requiredIndex.TableName, N'U')
        INNER JOIN sys.index_columns AS indexColumn
          ON indexColumn.object_id = indexMetadata.object_id
         AND indexColumn.index_id = indexMetadata.index_id
        INNER JOIN sys.columns AS columnMetadata
          ON columnMetadata.object_id = indexColumn.object_id
         AND columnMetadata.column_id = indexColumn.column_id
        EXCEPT
        SELECT
            required.IndexName,
            required.KeyOrdinal,
            required.ColumnName,
            required.IsIncluded,
            required.IsDescending
        FROM @RequiredIndexColumns AS required
    )
        THROW 527621, 'An existing CSDT control-plane index has incompatible uniqueness, type, keys, order, INCLUDE, filter, disabled, or hypothetical shape.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexMetadata
        WHERE indexMetadata.object_id IN
        (
            SELECT OBJECT_ID(N'dbo.' + requiredTable.TableName, N'U')
            FROM
            (
                SELECT DISTINCT TableName
                FROM @RequiredColumns
            ) AS requiredTable
        )
          AND indexMetadata.index_id > 0
          AND indexMetadata.is_primary_key = 0
          AND indexMetadata.name NOT IN
          (
              SELECT IndexName
              FROM @RequiredIndexes
          )
    )
        THROW 527621, 'An existing CSDT control-plane table has an unexpected non-primary index.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'dbo.QLHV_CsdtRealtimeSourceMembership', N'U'),
            OBJECT_ID(N'dbo.QLHV_CsdtRealtimeOwnershipClaim', N'U'),
            OBJECT_ID(N'dbo.QLHV_CsdtRealtimeMembershipJournal', N'U'),
            OBJECT_ID(N'dbo.QLHV_CsdtRealtimeCycle', N'U'),
            OBJECT_ID(N'dbo.QLHV_CsdtRealtimeCycleDomain', N'U'),
            OBJECT_ID(N'dbo.QLHV_CsdtRealtimeStreamCoverage', N'U'),
            OBJECT_ID(N'dbo.QLHV_CsdtRealtimeCheckpoint', N'U')
        )
          AND name LIKE N'CK_QLHV_CsdtRealtime%'
          AND (is_disabled = 1 OR is_not_trusted = 1)
    )
        THROW 527604, 'A CSDT control-plane check constraint is disabled or untrusted.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
