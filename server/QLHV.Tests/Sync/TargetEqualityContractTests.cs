using System.Text;
using System.Text.RegularExpressions;
using QLHV.Application.Sync.Realtime;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Tests.Sync;

public sealed class TargetEqualityContractTests
{
    public static IEnumerable<object[]> SqlEqualityVectors()
    {
        yield return ["same exact bytes", "Ab1", "Ab1", true, true];
        yield return ["different case", "A", "a", true, false];
        yield return ["different accent", "e", "é", false, false];
        yield return ["trailing spaces", "A", "A   ", true, false];
        yield return ["leading spaces", "A", " A", false, false];
        yield return ["embedded spaces", "A B", "AB", false, false];
        yield return ["maximum length", new string('X', 25), new string('X', 25), true, true];
        yield return ["empty versus blank", "", " ", true, false];
        yield return ["delimiter-like same value", "A|B:C", "A|B:C", true, true];
        yield return ["case and trailing alias", "Key", "key   ", true, false];
    }

    [Theory]
    [MemberData(nameof(SqlEqualityVectors))]
    public void Live_sql_vectors_prove_raw_bytes_are_not_target_identity(
        string vector,
        string left,
        string right,
        bool targetEquals,
        bool binaryEquals)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        Assert.False(string.IsNullOrWhiteSpace(vector));
        Assert.Equal(binaryEquals, leftBytes.SequenceEqual(rightBytes));
        if (targetEquals && !binaryEquals)
        {
            Assert.NotEqual(leftBytes, rightBytes);
        }
    }

    [Fact]
    public void Typed_claim_contract_has_a_fixed_version_proof_and_collation()
    {
        Assert.Equal((ushort)1, TargetEqualityProof.Version);
        Assert.Equal(
            "TYPED_OWNER_SQLSERVER_SQL_LATIN1_GENERAL_CP1_CI_AS_V1",
            TargetEqualityProof.ProofId);
        Assert.Equal("TYPED_CLAIM", TargetEqualityProof.ProofStatus);
        Assert.Equal("SQL_Latin1_General_CP1_CI_AS", TargetEqualityProof.Collation);
    }

    [Fact]
    public void Composite_key_order_and_types_are_explicit()
    {
        var claim = TypedTargetKeyClaim.ForNguoiLxHsGiayTo(7, "66029-000001");

        Assert.Equal("NguoiLXHS_GiayTo", claim.TableName);
        Assert.Equal(7, claim.GiayToMaGt);
        Assert.Equal("66029-000001", claim.GiayToMaDk);
        Assert.Null(claim.NguoiLxMaDk);
        Assert.Null(claim.NguoiLxHoSoMaDk);
    }

    [Fact]
    public void Typed_claim_shape_cannot_be_reused_for_another_table()
    {
        var claim = TypedTargetKeyClaim.ForNguoiLx("66029-000001");
        var route = new MembershipRoute(
            "OTO_V1",
            "OTO_V2",
            "OTO_V2_TO_V1",
            "66029",
            "NguoiLX_HoSo");

        Assert.Throws<ArgumentException>(() => claim.ValidateForRoute(route));
    }

    [Fact]
    public void Typed_claim_rejects_overlength_and_undefined_nul_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TypedTargetKeyClaim.ForDmDonViGtvt("1234567"));
        Assert.Throws<ArgumentException>(() =>
            TypedTargetKeyClaim.ForNguoiLx("66029\0-000001"));
    }

    [Fact]
    public void Supporting_token_requires_the_typed_claim_proof()
    {
        var pending = TargetEqualityKey.Pending([0x01]);
        var supporting = TargetEqualityKey.ForTypedOwnershipClaim([0x01]);

        Assert.Throws<TargetEqualityNotVerifiedException>(
            pending.EnsureTypedClaimForMutation);
        supporting.EnsureTypedClaimForMutation();
        Assert.Equal(TargetEqualityProof.ProofId, supporting.ProofId);
    }

    [Fact]
    public void Raw_typed_and_supporting_keys_are_redacted()
    {
        const string raw = "66029-sensitive-key";
        var claim = TypedTargetKeyClaim.ForNguoiLx(raw);
        var supporting = TargetEqualityKey.ForTypedOwnershipClaim(
            Encoding.UTF8.GetBytes(raw));

        Assert.DoesNotContain(raw, claim.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(raw, supporting.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Backup_routes_are_fixed_operational_profile_pairs()
    {
        Assert.Collection(
            CsdtRealtimeStreamCatalog.BackupRoutes,
            oto =>
            {
                Assert.Equal("OTO_V2_BAK", oto.SourceProfileCode);
                Assert.Equal("OTO_V1_BAK", oto.TargetProfileCode);
                Assert.Equal("CSDL_OTO_V1_BAK", oto.TargetDatabaseName);
                Assert.Equal("OTO_V2_TO_V1", oto.StreamCode);
                Assert.True(oto.IsBackup);
            },
            moto =>
            {
                Assert.Equal("MOTO_V2_BAK", moto.SourceProfileCode);
                Assert.Equal("MOTO_V1_BAK", moto.TargetProfileCode);
                Assert.Equal("CSDL_MOTO_V1_BAK", moto.TargetDatabaseName);
                Assert.Equal("MOTO_V2_TO_V1", moto.StreamCode);
                Assert.True(moto.IsBackup);
            });
    }

    [Fact]
    public void Sql_proof_spec_has_both_exact_use_batches_and_only_read_operations()
    {
        var proof = File.ReadAllText(FindWorkspaceFile(
            "database",
            "proofs",
            "20260726_csdt_target_key_equality_read_only.sql"));

        Assert.Contains("USE [CSDL_OTO_V1];\nGO", NormalizeNewLines(proof), StringComparison.Ordinal);
        Assert.Contains("USE [CSDL_MOTO_V1];\nGO", NormalizeNewLines(proof), StringComparison.Ordinal);
        foreach (var vector in new[]
                 {
                     "same exact bytes",
                     "different case",
                     "different accent",
                     "trailing spaces",
                     "leading spaces",
                     "embedded spaces",
                     "maximum length",
                     "empty versus blank",
                     "delimiter-like content",
                     "composite key same typed tuple",
                     "composite component-order/value swap",
                     "different values target collation aliases",
                 })
        {
            Assert.Contains(vector, proof, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(
            new Regex(
                @"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            proof);
    }

    [Fact]
    public void Invented_backup_profile_pair_fails_closed()
    {
        var options = new CsdtRealtimeSyncOptions
        {
            UseBackupProfiles = true,
            Streams = new CsdtRealtimeStreamsOptions
            {
                Oto = new CsdtRealtimeStreamOptions
                {
                    Enabled = true,
                    StreamCode = "OTO_V2_TO_V1",
                    SourceProfile = "OTO_V2_BAK",
                    TargetProfile = "OTO_V1_BAK_INVENTED",
                    MaCSDT = "66029",
                },
                Moto = new CsdtRealtimeStreamOptions
                {
                    Enabled = true,
                    StreamCode = "MOTO_V2_TO_V1",
                    SourceProfile = "MOTO_V2_BAK",
                    TargetProfile = "MOTO_V1_BAK",
                    MaCSDT = "66030",
                },
            },
        };

        var result = new CsdtRealtimeSyncOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    private static string NormalizeNewLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindWorkspaceFile(
        string firstPathPart,
        params string[] remainingPathParts)
    {
        var pathParts = new[] { firstPathPart }.Concat(remainingPathParts).ToArray();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.", Path.Combine(pathParts));
    }
}
