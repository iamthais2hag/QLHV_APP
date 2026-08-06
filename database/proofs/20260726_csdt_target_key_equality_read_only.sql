USE [CSDL_OTO_V1];
GO

SET NOCOUNT ON;

SELECT
    DB_NAME() AS DatabaseName,
    targetTable.name AS TableName,
    targetColumn.name AS ColumnName,
    targetType.name AS SqlType,
    targetColumn.max_length AS MaxLengthBytes,
    targetColumn.precision AS [Precision],
    targetColumn.scale AS Scale,
    targetColumn.collation_name AS CollationName,
    targetColumn.is_nullable AS IsNullable,
    targetIndexColumn.key_ordinal AS PkOrder,
    targetIndex.type_desc AS PkIndexType
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
INNER JOIN sys.index_columns AS targetIndexColumn
  ON targetIndexColumn.object_id = targetIndex.object_id
 AND targetIndexColumn.index_id = targetIndex.index_id
 AND targetIndexColumn.column_id = targetColumn.column_id
WHERE
    (targetTable.name = N'DM_DonViGTVT' AND targetColumn.name = N'MaDV')
 OR (targetTable.name = N'GiaoVien' AND targetColumn.name = N'MaGV')
 OR (targetTable.name = N'KhoaHoc' AND targetColumn.name = N'MaKH')
 OR (targetTable.name = N'KhoaHoc_GiaoVien' AND targetColumn.name = N'MaLichLV')
 OR (targetTable.name = N'BaoCaoI' AND targetColumn.name = N'MaBCI')
 OR (targetTable.name = N'NguoiLX' AND targetColumn.name = N'MaDK')
 OR (targetTable.name = N'NguoiLX_HoSo' AND targetColumn.name = N'MaDK')
 OR
    (
        targetTable.name = N'NguoiLXHS_GiayTo'
        AND targetColumn.name IN (N'MaGT', N'MaDK')
    )
ORDER BY targetTable.name, targetIndexColumn.key_ordinal;

WITH EqualityVectors AS
(
    SELECT *
    FROM
    (
        VALUES
            (1, N'same exact bytes', N'Ab1', N'Ab1'),
            (2, N'different case', N'A', N'a'),
            (3, N'different accent', N'e', N'é'),
            (4, N'trailing spaces', N'A', N'A   '),
            (5, N'leading spaces', N'A', N' A'),
            (6, N'embedded spaces', N'A B', N'AB'),
            (7, N'maximum length', REPLICATE(N'X', 25), REPLICATE(N'X', 25)),
            (8, N'empty versus blank', N'', N' '),
            (9, N'delimiter-like content', N'A|B:C', N'A|B:C'),
            (10, N'different values target collation aliases', N'Key', N'key   ')
    ) AS value(SortOrder, TestName, LeftValue, RightValue)
)
SELECT
    TestName,
    CONVERT
    (
        bit,
        CASE
            WHEN CONVERT(varchar(25), LeftValue) =
                 CONVERT(varchar(25), RightValue)
            THEN 1
            ELSE 0
        END
    ) AS TargetEquals,
    CONVERT
    (
        bit,
        CASE
            WHEN CONVERT(varbinary(25), CONVERT(varchar(25), LeftValue)) =
                 CONVERT(varbinary(25), CONVERT(varchar(25), RightValue))
            THEN 1
            ELSE 0
        END
    ) AS BinaryEquals
FROM EqualityVectors
UNION ALL
SELECT
    N'composite key same typed tuple',
    CONVERT(bit, CASE WHEN 7 = 7 AND CONVERT(varchar(25), N'A|B') = CONVERT(varchar(25), N'A|B') THEN 1 ELSE 0 END),
    CONVERT(bit, CASE WHEN CONVERT(varbinary(4), 7) = CONVERT(varbinary(4), 7) AND CONVERT(varbinary(25), CONVERT(varchar(25), N'A|B')) = CONVERT(varbinary(25), CONVERT(varchar(25), N'A|B')) THEN 1 ELSE 0 END)
UNION ALL
SELECT
    N'composite component-order/value swap',
    CONVERT(bit, CASE WHEN 7 = 8 AND CONVERT(varchar(25), N'8') = CONVERT(varchar(25), N'7') THEN 1 ELSE 0 END),
    CONVERT(bit, CASE WHEN CONVERT(varbinary(4), 7) = CONVERT(varbinary(4), 8) AND CONVERT(varbinary(25), CONVERT(varchar(25), N'8')) = CONVERT(varbinary(25), CONVERT(varchar(25), N'7')) THEN 1 ELSE 0 END);
GO

USE [CSDL_MOTO_V1];
GO

SET NOCOUNT ON;

SELECT
    DB_NAME() AS DatabaseName,
    targetTable.name AS TableName,
    targetColumn.name AS ColumnName,
    targetType.name AS SqlType,
    targetColumn.max_length AS MaxLengthBytes,
    targetColumn.precision AS [Precision],
    targetColumn.scale AS Scale,
    targetColumn.collation_name AS CollationName,
    targetColumn.is_nullable AS IsNullable,
    targetIndexColumn.key_ordinal AS PkOrder,
    targetIndex.type_desc AS PkIndexType
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
INNER JOIN sys.index_columns AS targetIndexColumn
  ON targetIndexColumn.object_id = targetIndex.object_id
 AND targetIndexColumn.index_id = targetIndex.index_id
 AND targetIndexColumn.column_id = targetColumn.column_id
WHERE
    (targetTable.name = N'DM_DonViGTVT' AND targetColumn.name = N'MaDV')
 OR (targetTable.name = N'GiaoVien' AND targetColumn.name = N'MaGV')
 OR (targetTable.name = N'KhoaHoc' AND targetColumn.name = N'MaKH')
 OR (targetTable.name = N'KhoaHoc_GiaoVien' AND targetColumn.name = N'MaLichLV')
 OR (targetTable.name = N'BaoCaoI' AND targetColumn.name = N'MaBCI')
 OR (targetTable.name = N'NguoiLX' AND targetColumn.name = N'MaDK')
 OR (targetTable.name = N'NguoiLX_HoSo' AND targetColumn.name = N'MaDK')
 OR
    (
        targetTable.name = N'NguoiLXHS_GiayTo'
        AND targetColumn.name IN (N'MaGT', N'MaDK')
    )
ORDER BY targetTable.name, targetIndexColumn.key_ordinal;

WITH EqualityVectors AS
(
    SELECT *
    FROM
    (
        VALUES
            (1, N'same exact bytes', N'Ab1', N'Ab1'),
            (2, N'different case', N'A', N'a'),
            (3, N'different accent', N'e', N'é'),
            (4, N'trailing spaces', N'A', N'A   '),
            (5, N'leading spaces', N'A', N' A'),
            (6, N'embedded spaces', N'A B', N'AB'),
            (7, N'maximum length', REPLICATE(N'X', 25), REPLICATE(N'X', 25)),
            (8, N'empty versus blank', N'', N' '),
            (9, N'delimiter-like content', N'A|B:C', N'A|B:C'),
            (10, N'different values target collation aliases', N'Key', N'key   ')
    ) AS value(SortOrder, TestName, LeftValue, RightValue)
)
SELECT
    TestName,
    CONVERT
    (
        bit,
        CASE
            WHEN CONVERT(varchar(25), LeftValue) =
                 CONVERT(varchar(25), RightValue)
            THEN 1
            ELSE 0
        END
    ) AS TargetEquals,
    CONVERT
    (
        bit,
        CASE
            WHEN CONVERT(varbinary(25), CONVERT(varchar(25), LeftValue)) =
                 CONVERT(varbinary(25), CONVERT(varchar(25), RightValue))
            THEN 1
            ELSE 0
        END
    ) AS BinaryEquals
FROM EqualityVectors
UNION ALL
SELECT
    N'composite key same typed tuple',
    CONVERT(bit, CASE WHEN 7 = 7 AND CONVERT(varchar(25), N'A|B') = CONVERT(varchar(25), N'A|B') THEN 1 ELSE 0 END),
    CONVERT(bit, CASE WHEN CONVERT(varbinary(4), 7) = CONVERT(varbinary(4), 7) AND CONVERT(varbinary(25), CONVERT(varchar(25), N'A|B')) = CONVERT(varbinary(25), CONVERT(varchar(25), N'A|B')) THEN 1 ELSE 0 END)
UNION ALL
SELECT
    N'composite component-order/value swap',
    CONVERT(bit, CASE WHEN 7 = 8 AND CONVERT(varchar(25), N'8') = CONVERT(varchar(25), N'7') THEN 1 ELSE 0 END),
    CONVERT(bit, CASE WHEN CONVERT(varbinary(4), 7) = CONVERT(varbinary(4), 8) AND CONVERT(varbinary(25), CONVERT(varchar(25), N'8')) = CONVERT(varbinary(25), CONVERT(varchar(25), N'7')) THEN 1 ELSE 0 END);
GO
