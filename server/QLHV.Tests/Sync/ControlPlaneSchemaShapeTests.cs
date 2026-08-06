namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeControlPlaneSchemaShapeTests
{
    [Fact]
    public void Existing_table_with_an_extra_column_fails_exact_shape_validation()
    {
        var expected = new TableShape(
            [
                new ColumnShape("MembershipId", 1, "bigint", 8, 19, 0, false, true),
                new ColumnShape("TargetProfile", 2, "varchar", 32, 0, 0, false, false),
            ]);
        var actual = expected with
        {
            Columns =
            [
                .. expected.Columns,
                new ColumnShape("Unexpected", 3, "int", 4, 10, 0, true, false),
            ],
        };

        Assert.Throws<InvalidOperationException>(() =>
            ExactShapeValidator.ValidateTable(expected, actual));
    }

    [Fact]
    public void Primary_key_with_wrong_key_or_order_fails()
    {
        var expected = new PrimaryKeyShape(
            "PK_Expected",
            true,
            ["CycleId", "DomainName"]);
        var wrongOrder = expected with { KeyColumns = ["DomainName", "CycleId"] };
        var wrongKey = expected with { KeyColumns = ["CycleId", "Other"] };

        Assert.Throws<InvalidOperationException>(() =>
            ExactShapeValidator.ValidatePrimaryKey(expected, wrongOrder));
        Assert.Throws<InvalidOperationException>(() =>
            ExactShapeValidator.ValidatePrimaryKey(expected, wrongKey));
    }

    [Fact]
    public void Foreign_key_with_wrong_child_column_fails()
    {
        var expected = new ForeignKeyShape(
            "FK_Journal_Cycle",
            "Journal",
            ["CycleId"],
            "Cycle",
            ["CycleId"],
            0,
            0,
            true,
            true);
        var actual = expected with { ChildColumns = ["MembershipId"] };

        Assert.Throws<InvalidOperationException>(() =>
            ExactShapeValidator.ValidateForeignKey(expected, actual));
    }

    [Fact]
    public void Index_with_wrong_include_or_filter_fails()
    {
        var expected = new IndexShape(
            "IX_Membership",
            false,
            ["TargetProfile", "TableName"],
            ["MembershipId", "OwnershipReserved"],
            null,
            false,
            false);
        var wrongInclude = expected with { IncludedColumns = ["MembershipId"] };
        var wrongFilter = expected with { Filter = "OwnershipReserved = 1" };

        Assert.Throws<InvalidOperationException>(() =>
            ExactShapeValidator.ValidateIndex(expected, wrongInclude));
        Assert.Throws<InvalidOperationException>(() =>
            ExactShapeValidator.ValidateIndex(expected, wrongFilter));
    }

    [Fact]
    public void Check_constraint_with_wrong_route_or_status_fails()
    {
        var expectedRoute = new CheckShape(
            "CK_Route",
            ["OTO_V1", "OTO_V2", "OTO_V2_TO_V1", "66029"],
            ["TargetProfile", "SourceProfile", "StreamCode", "MaCSDT"]);
        var wrongRoute = expectedRoute with
        {
            Literals = ["OTO_V1", "OTO_V2", "MOTO_V2_TO_V1", "66029"],
        };
        var expectedStatus = new CheckShape(
            "CK_Status",
            ["ACTIVE", "INACTIVE", "CONFLICT"],
            ["MembershipStatus"]);
        var wrongStatus = expectedStatus with
        {
            Literals = ["ACTIVE", "INACTIVE", "COMPLETE"],
        };

        Assert.Throws<InvalidOperationException>(() =>
            ExactShapeValidator.ValidateCheck(expectedRoute, wrongRoute));
        Assert.Throws<InvalidOperationException>(() =>
            ExactShapeValidator.ValidateCheck(expectedStatus, wrongStatus));
    }

    private sealed record ColumnShape(
        string Name,
        int Ordinal,
        string Type,
        int MaxLength,
        int Precision,
        int Scale,
        bool Nullable,
        bool Identity);

    private sealed record TableShape(IReadOnlyList<ColumnShape> Columns);

    private sealed record PrimaryKeyShape(
        string Name,
        bool Clustered,
        IReadOnlyList<string> KeyColumns);

    private sealed record ForeignKeyShape(
        string Name,
        string ChildTable,
        IReadOnlyList<string> ChildColumns,
        string ParentTable,
        IReadOnlyList<string> ParentColumns,
        int DeleteAction,
        int UpdateAction,
        bool Enabled,
        bool Trusted);

    private sealed record IndexShape(
        string Name,
        bool Unique,
        IReadOnlyList<string> KeyColumns,
        IReadOnlyList<string> IncludedColumns,
        string? Filter,
        bool Disabled,
        bool Hypothetical);

    private sealed record CheckShape(
        string Name,
        IReadOnlyList<string> Literals,
        IReadOnlyList<string> ReferencedColumns);

    private static class ExactShapeValidator
    {
        public static void ValidateTable(TableShape expected, TableShape actual)
            => RequireEqual(expected.Columns, actual.Columns, "table");

        public static void ValidatePrimaryKey(
            PrimaryKeyShape expected,
            PrimaryKeyShape actual)
        {
            if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal) ||
                expected.Clustered != actual.Clustered ||
                !expected.KeyColumns.SequenceEqual(actual.KeyColumns, StringComparer.Ordinal))
            {
                ThrowMismatch("primary key");
            }
        }

        public static void ValidateForeignKey(
            ForeignKeyShape expected,
            ForeignKeyShape actual)
        {
            if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal) ||
                !string.Equals(expected.ChildTable, actual.ChildTable, StringComparison.Ordinal) ||
                !expected.ChildColumns.SequenceEqual(actual.ChildColumns, StringComparer.Ordinal) ||
                !string.Equals(expected.ParentTable, actual.ParentTable, StringComparison.Ordinal) ||
                !expected.ParentColumns.SequenceEqual(actual.ParentColumns, StringComparer.Ordinal) ||
                expected.DeleteAction != actual.DeleteAction ||
                expected.UpdateAction != actual.UpdateAction ||
                expected.Enabled != actual.Enabled ||
                expected.Trusted != actual.Trusted)
            {
                ThrowMismatch("foreign key");
            }
        }

        public static void ValidateIndex(IndexShape expected, IndexShape actual)
        {
            if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal) ||
                expected.Unique != actual.Unique ||
                !expected.KeyColumns.SequenceEqual(actual.KeyColumns, StringComparer.Ordinal) ||
                !expected.IncludedColumns
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(
                        actual.IncludedColumns.Order(StringComparer.Ordinal),
                        StringComparer.Ordinal) ||
                !string.Equals(expected.Filter, actual.Filter, StringComparison.Ordinal) ||
                expected.Disabled != actual.Disabled ||
                expected.Hypothetical != actual.Hypothetical)
            {
                ThrowMismatch("index");
            }
        }

        public static void ValidateCheck(CheckShape expected, CheckShape actual)
        {
            if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal) ||
                !expected.Literals
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(
                        actual.Literals.Order(StringComparer.Ordinal),
                        StringComparer.Ordinal) ||
                !expected.ReferencedColumns
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(
                        actual.ReferencedColumns.Order(StringComparer.Ordinal),
                        StringComparer.Ordinal))
            {
                ThrowMismatch("check constraint");
            }
        }

        private static void ThrowMismatch(string objectType)
        {
            throw new InvalidOperationException(
                $"Existing {objectType} does not match the approved exact shape.");
        }

        private static void RequireEqual<T>(
            IReadOnlyList<T> expected,
            IReadOnlyList<T> actual,
            string objectType)
        {
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException(
                    $"Existing {objectType} does not match the approved exact shape.");
            }
        }
    }
}
