using System.Runtime.CompilerServices;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncRepositoryInsertOrderTests
{
    [Theory]
    [InlineData("ExecuteInsertOnlyAsync")]
    [InlineData("ExecuteInsertAndUpdateAsync")]
    public void Execute_inserts_HoSo_before_NguoiLX_GPLX(string methodName)
    {
        var source = File.ReadAllText(FindRepositoryFile());
        var methodBody = ExtractMethodBody(source, $"public async Task<MotoSyncExecuteSummaryDto> {methodName}");

        var nguoiLxIndex = methodBody.IndexOf("BulkInsertMissingNguoiLxAsync", StringComparison.Ordinal);
        var hoSoIndex = methodBody.IndexOf("BulkInsertMissingHoSoAsync", StringComparison.Ordinal);
        var gplxIndex = methodBody.IndexOf("BulkInsertMissingNguoiLXGplxAsync", StringComparison.Ordinal);
        var giayToIndex = methodBody.IndexOf("BulkInsertMissingGiayToAsync", StringComparison.Ordinal);

        Assert.True(nguoiLxIndex >= 0, "Execute path must insert dbo.NguoiLX.");
        Assert.True(hoSoIndex >= 0, "Execute path must insert dbo.NguoiLX_HoSo.");
        Assert.True(gplxIndex >= 0, "Execute path must insert dbo.NguoiLX_GPLX.");
        Assert.True(giayToIndex >= 0, "Execute path must insert dbo.NguoiLXHS_GiayTo.");
        Assert.True(nguoiLxIndex < hoSoIndex, "dbo.NguoiLX must be inserted before dbo.NguoiLX_HoSo.");
        Assert.True(hoSoIndex < gplxIndex, "dbo.NguoiLX_HoSo must be inserted before dbo.NguoiLX_GPLX because real TEST DB has FK GPLX.MaDK -> HoSo.MaDK.");
        Assert.True(gplxIndex < giayToIndex, "dbo.NguoiLX_GPLX must be inserted before dbo.NguoiLXHS_GiayTo in the current execute order.");
    }

    private static string FindRepositoryFile([CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "server",
                "QLHV.Infrastructure",
                "Sync",
                "MotoSyncRepository.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate MotoSyncRepository.cs from test source path.", testFile);
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        var signatureIndex = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Cannot find method signature: {methodSignature}.");

        var openBraceIndex = source.IndexOf('{', signatureIndex);
        Assert.True(openBraceIndex >= 0, $"Cannot find method body for: {methodSignature}.");

        var depth = 0;
        for (var index = openBraceIndex; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(openBraceIndex, index - openBraceIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Cannot parse method body for: {methodSignature}.");
    }
}
