using QLHV.Application.Common;

namespace QLHV.Tests.Sync;

public sealed class CsdtCodeFormatRulesTests
{
    [Fact]
    public void Current_center_code_is_recognized()
    {
        Assert.Equal(
            CsdtCodeFormatKind.Current,
            CsdtCodeFormatRules.ClassifyMaCsdt("66029"));
    }

    [Fact]
    public void Current_course_code_is_valid_for_matching_center_and_year()
    {
        var result = CsdtCodeFormatRules.ValidateCourseCode(
            "66029K260001",
            "66029",
            new DateOnly(2026, 7, 1));

        Assert.True(result.IsValid);
        Assert.Equal(CsdtCodeFormatKind.Current, result.Format);
    }

    [Theory]
    [InlineData("66029K260000", "So thu tu")]
    [InlineData("66030K260001", "MaCSDT")]
    [InlineData("66029K250001", "ngay khai giang")]
    public void Invalid_current_course_code_is_rejected(string code, string expectedError)
    {
        var result = CsdtCodeFormatRules.ValidateCourseCode(
            code,
            "66029",
            new DateOnly(2026, 7, 1));

        Assert.False(result.IsValid);
        Assert.Contains(expectedError, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Current_student_code_is_valid()
    {
        var result = CsdtCodeFormatRules.ValidateStudentCode(
            "66029-20260722-000001",
            "66029");

        Assert.True(result.IsValid);
        Assert.Equal(CsdtCodeFormatKind.Current, result.Format);
    }

    [Theory]
    [InlineData("66029-20260230-000001", "Ngay")]
    [InlineData("66029-20260722-000000", "So thu tu")]
    [InlineData("66030-20260722-000001", "MaCSDT")]
    public void Invalid_current_student_code_is_rejected(string code, string expectedError)
    {
        var result = CsdtCodeFormatRules.ValidateStudentCode(code, "66029");

        Assert.False(result.IsValid);
        Assert.Contains(expectedError, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Completion_certificate_uses_student_code_and_one_to_three_character_class()
    {
        var result = CsdtCodeFormatRules.ValidateCompletionCertificateNumber(
            "66029-20260722-000001-B2",
            "66029-20260722-000001");

        Assert.True(result.IsValid);
        Assert.Equal(CsdtCodeFormatKind.Current, result.Format);
    }

    [Theory]
    [InlineData(
        "66029-20260230-000001-B2",
        null,
        "Ma hoc vien")]
    [InlineData(
        "66030-20260722-000001-B2",
        "66029-20260722-000001",
        "khong khop")]
    public void Invalid_current_completion_certificate_is_rejected(
        string certificate,
        string? studentCode,
        string expectedError)
    {
        var result = CsdtCodeFormatRules.ValidateCompletionCertificateNumber(
            certificate,
            studentCode);

        Assert.False(result.IsValid);
        Assert.Contains(expectedError, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_identifiers_remain_supported_without_rewriting()
    {
        Assert.Equal(
            CsdtCodeFormatKind.Legacy,
            CsdtCodeFormatRules.ValidateCourseCode("KHOA-A1-2024").Format);
        Assert.Equal(
            CsdtCodeFormatKind.Legacy,
            CsdtCodeFormatRules.ValidateStudentCode("660290001234").Format);
        Assert.Equal(
            CsdtCodeFormatKind.Legacy,
            CsdtCodeFormatRules.ValidateCompletionCertificateNumber(
                "660290001234-B2",
                "660290001234").Format);
    }
}
