using System.Buffers.Binary;

namespace QLHV.Application.Auth;

public static class AppPasswordHashFormat
{
    private const byte IdentityV2Marker = 0x00;
    private const byte IdentityV3Marker = 0x01;
    private const int IdentityV2PayloadLength = 1 + 16 + 32;
    private const int IdentityV3HeaderLength = 13;
    private const int MinimumSaltLength = 16;
    private const int MinimumSubkeyLength = 16;

    public static bool IsSupported(string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(passwordHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length == 0)
        {
            return false;
        }

        return decoded[0] switch
        {
            IdentityV2Marker => decoded.Length == IdentityV2PayloadLength,
            IdentityV3Marker => IsSupportedIdentityV3(decoded),
            _ => false,
        };
    }

    private static bool IsSupportedIdentityV3(ReadOnlySpan<byte> decoded)
    {
        if (decoded.Length < IdentityV3HeaderLength)
        {
            return false;
        }

        var prf = BinaryPrimitives.ReadUInt32BigEndian(decoded[1..5]);
        var iterationCount = BinaryPrimitives.ReadUInt32BigEndian(decoded[5..9]);
        var saltLength = BinaryPrimitives.ReadUInt32BigEndian(decoded[9..13]);
        if (prf > 2 ||
            iterationCount is 0 or > int.MaxValue ||
            saltLength < MinimumSaltLength ||
            saltLength > int.MaxValue)
        {
            return false;
        }

        return decoded.Length >= IdentityV3HeaderLength + MinimumSubkeyLength &&
            saltLength <=
                decoded.Length - IdentityV3HeaderLength - MinimumSubkeyLength;
    }
}
