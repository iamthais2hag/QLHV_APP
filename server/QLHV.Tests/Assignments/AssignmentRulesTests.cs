using QLHV.Application.Assignments;

namespace QLHV.Tests.Assignments;

public sealed class AssignmentRulesTests
{
    [Theory]
    [InlineData("KEEP", false, true)]
    [InlineData("SET", false, true)]
    [InlineData("CLEAR", false, true)]
    [InlineData("INHERIT", false, false)]
    [InlineData("INHERIT", true, true)]
    [InlineData("UNKNOWN", true, false)]
    [Trait("Category", "AssignmentFocused")]
    public void Field_actions_enforce_explicit_keep_set_clear_and_group_only_inherit(
        string action,
        bool inheritAllowed,
        bool expected)
    {
        Assert.Equal(expected, AssignmentAction.IsValid(action, inheritAllowed));
    }

    [Theory]
    [InlineData("CSDT_OTO", "CSDT_OTO")]
    [InlineData(" csdt_oto ", "CSDT_OTO")]
    [InlineData("CSDT_MOTO", "CSDT_MOTO")]
    [Trait("Category", "AssignmentFocused")]
    public void Profile_normalization_is_explicit_and_case_safe(string value, string expected)
    {
        Assert.Equal(expected, AssignmentRules.NormalizeProfile(value, required: true));
    }

    [Theory]
    [InlineData("OTO")]
    [InlineData("MOTO")]
    [InlineData("AUTO")]
    [InlineData("")]
    [Trait("Category", "AssignmentFocused")]
    public void Unknown_or_missing_profile_fails_closed(string value)
    {
        Assert.Throws<AssignmentDomainException>(() =>
            AssignmentRules.NormalizeProfile(value, required: true));
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Rowversion_requires_exactly_eight_bytes()
    {
        var value = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        Assert.Equal(value, AssignmentRules.ParseRowVersion(Convert.ToBase64String(value)));
        Assert.Throws<AssignmentDomainException>(() => AssignmentRules.ParseRowVersion(null));
        Assert.Throws<AssignmentDomainException>(() =>
            AssignmentRules.ParseRowVersion(Convert.ToBase64String([1, 2, 3])));
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Fingerprint_is_stable_for_identical_ordered_targets_and_sensitive_to_identity()
    {
        var first = AssignmentRules.ComputeFingerprint(["42|CSDT_OTO|DK01", "43|CSDT_OTO|DK02"]);
        var second = AssignmentRules.ComputeFingerprint(["42|CSDT_OTO|DK01", "43|CSDT_OTO|DK02"]);
        var otherProfile = AssignmentRules.ComputeFingerprint(["42|CSDT_MOTO|DK01", "43|CSDT_OTO|DK02"]);

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherProfile);
        Assert.Matches("^[0-9A-F]{64}$", first);
    }

    [Theory]
    [InlineData("51A-123.45", "51A12345")]
    [InlineData(" 51a 12345 ", "51A12345")]
    [Trait("Category", "AssignmentFocused")]
    public void Vehicle_plate_normalization_is_separator_and_case_safe(string value, string expected)
    {
        Assert.Equal(expected, AssignmentRules.NormalizeVehiclePlate(value));
    }
}
