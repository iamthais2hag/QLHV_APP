using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.Realtime.ControlPlane;

public enum CanonicalKeyComponentType : byte
{
    Utf8String = 1,
    Int32 = 2,
    Int64 = 3,
    Guid = 4,
    Binary = 5,
}

public sealed class CanonicalKeyComponent
{
    private readonly byte[] _payload;

    private CanonicalKeyComponent(CanonicalKeyComponentType type, byte[] payload)
    {
        Type = type;
        _payload = payload;
    }

    public CanonicalKeyComponentType Type { get; }

    internal ReadOnlySpan<byte> Payload => _payload;

    public static CanonicalKeyComponent FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CanonicalKeyComponent(
            CanonicalKeyComponentType.Utf8String,
            Encoding.UTF8.GetBytes(value));
    }

    public static CanonicalKeyComponent FromInt32(int value)
    {
        var payload = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(payload, value);
        return new CanonicalKeyComponent(CanonicalKeyComponentType.Int32, payload);
    }

    public static CanonicalKeyComponent FromInt64(long value)
    {
        var payload = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(payload, value);
        return new CanonicalKeyComponent(CanonicalKeyComponentType.Int64, payload);
    }

    public static CanonicalKeyComponent FromGuid(Guid value)
    {
        var text = value.ToString("D");
        return new CanonicalKeyComponent(
            CanonicalKeyComponentType.Guid,
            Encoding.ASCII.GetBytes(text));
    }

    public static CanonicalKeyComponent FromBinary(ReadOnlySpan<byte> value)
        => new(CanonicalKeyComponentType.Binary, value.ToArray());

    public override string ToString() => $"CanonicalKeyComponent(Type={Type}, Redacted=true)";
}

public sealed class CanonicalBusinessKey : IEquatable<CanonicalBusinessKey>
{
    private readonly byte[] _bytes;

    internal CanonicalBusinessKey(ushort schemaVersion, int componentCount, byte[] bytes)
    {
        SchemaVersion = schemaVersion;
        ComponentCount = componentCount;
        _bytes = bytes;
    }

    public ushort SchemaVersion { get; }

    public int ComponentCount { get; }

    public int Length => _bytes.Length;

    public byte[] ToArray() => _bytes.ToArray();

    public static CanonicalBusinessKey FromEncoded(ReadOnlySpan<byte> encoded)
        => CanonicalBusinessKeyEncoder.Decode(encoded);

    public bool Equals(CanonicalBusinessKey? other)
        => other is not null &&
           SchemaVersion == other.SchemaVersion &&
           CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);

    public override bool Equals(object? obj) => Equals(obj as CanonicalBusinessKey);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(_bytes.Length);
        if (_bytes.Length > 0)
        {
            hash.Add(_bytes[0]);
            hash.Add(_bytes[^1]);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
        => $"CanonicalBusinessKey(Version={SchemaVersion}, Components={ComponentCount}, Bytes={Length}, Redacted=true)";
}

public static class CanonicalBusinessKeyEncoder
{
    private static readonly byte[] Magic = "QLHV"u8.ToArray();

    public static CanonicalBusinessKey Encode(
        ushort schemaVersion,
        params CanonicalKeyComponent[] components)
    {
        if (schemaVersion == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                "Canonical key schema version must be positive.");
        }

        ArgumentNullException.ThrowIfNull(components);
        if (components.Length == 0 || components.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(components),
                "A canonical key must contain between 1 and 65535 components.");
        }

        if (components.Any(component => component is null))
        {
            throw new ArgumentException(
                "Canonical key components cannot contain null.",
                nameof(components));
        }

        var totalLength = checked(
            Magic.Length +
            sizeof(ushort) +
            sizeof(ushort) +
            components.Sum(component => 1 + sizeof(int) + component.Payload.Length));
        var buffer = new byte[totalLength];
        var offset = 0;
        Magic.CopyTo(buffer, offset);
        offset += Magic.Length;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, sizeof(ushort)), schemaVersion);
        offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(
            buffer.AsSpan(offset, sizeof(ushort)),
            checked((ushort)components.Length));
        offset += sizeof(ushort);

        foreach (var component in components)
        {
            buffer[offset++] = (byte)component.Type;
            BinaryPrimitives.WriteInt32BigEndian(
                buffer.AsSpan(offset, sizeof(int)),
                component.Payload.Length);
            offset += sizeof(int);
            component.Payload.CopyTo(buffer.AsSpan(offset));
            offset += component.Payload.Length;
        }

        return new CanonicalBusinessKey(schemaVersion, components.Length, buffer);
    }

    public static CanonicalBusinessKey Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < Magic.Length + sizeof(ushort) + sizeof(ushort) ||
            !encoded[..Magic.Length].SequenceEqual(Magic))
        {
            throw new ArgumentException(
                "Encoded canonical key has an invalid fixed header.",
                nameof(encoded));
        }

        var offset = Magic.Length;
        var schemaVersion = BinaryPrimitives.ReadUInt16BigEndian(
            encoded.Slice(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        var componentCount = BinaryPrimitives.ReadUInt16BigEndian(
            encoded.Slice(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        if (schemaVersion == 0 || componentCount == 0)
        {
            throw new ArgumentException(
                "Encoded canonical key has an invalid schema or component count.",
                nameof(encoded));
        }

        for (var index = 0; index < componentCount; index++)
        {
            if (offset > encoded.Length - 1 - sizeof(int))
            {
                throw new ArgumentException(
                    "Encoded canonical key is truncated.",
                    nameof(encoded));
            }

            var componentType = (CanonicalKeyComponentType)encoded[offset++];
            if (!Enum.IsDefined(componentType))
            {
                throw new ArgumentException(
                    "Encoded canonical key contains an unknown component type.",
                    nameof(encoded));
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(
                encoded.Slice(offset, sizeof(int)));
            offset += sizeof(int);
            if (length < 0 || offset > encoded.Length - length)
            {
                throw new ArgumentException(
                    "Encoded canonical key contains an invalid component length.",
                    nameof(encoded));
            }

            offset += length;
        }

        if (offset != encoded.Length)
        {
            throw new ArgumentException(
                "Encoded canonical key contains trailing data.",
                nameof(encoded));
        }

        return new CanonicalBusinessKey(
            schemaVersion,
            componentCount,
            encoded.ToArray());
    }
}

public enum TargetEqualityProofStatus
{
    Pending,
    TypedClaim,
}

public sealed class TargetEqualityKey
{
    private readonly byte[] _bytes;

    private TargetEqualityKey(
        byte[] bytes,
        TargetEqualityProofStatus proofStatus,
        string? proofId)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("Target equality key cannot be empty.", nameof(bytes));
        }

        _bytes = bytes;
        ProofStatus = proofStatus;
        ProofId = proofId;
    }

    public TargetEqualityProofStatus ProofStatus { get; }

    public string? ProofId { get; }

    public int Length => _bytes.Length;

    public static TargetEqualityKey Pending(ReadOnlySpan<byte> bytes)
        => new(bytes.ToArray(), TargetEqualityProofStatus.Pending, null);

    public static TargetEqualityKey ForTypedOwnershipClaim(ReadOnlySpan<byte> bytes)
        => new(
            bytes.ToArray(),
            TargetEqualityProofStatus.TypedClaim,
            TargetEqualityProof.ProofId);

    public byte[] ToArray() => _bytes.ToArray();

    public void EnsureTypedClaimForMutation()
    {
        if (ProofStatus != TargetEqualityProofStatus.TypedClaim ||
            !string.Equals(ProofId, TargetEqualityProof.ProofId, StringComparison.Ordinal))
        {
            throw new TargetEqualityNotVerifiedException();
        }
    }

    public override string ToString()
        => $"TargetEqualityKey(Status={ProofStatus}, Bytes={Length}, Redacted=true)";
}

public sealed class TargetEqualityNotVerifiedException : InvalidOperationException
{
    public TargetEqualityNotVerifiedException()
        : base("Target equality requires the approved typed SQL ownership-claim contract.")
    {
    }
}

public sealed record DiagnosticHmacKeyMaterial
{
    private readonly byte[] _key;

    public DiagnosticHmacKeyMaterial(int version, ReadOnlySpan<byte> key)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (key.Length < 32)
        {
            throw new ArgumentException(
                "Diagnostic HMAC key must contain at least 256 bits.",
                nameof(key));
        }

        Version = version;
        _key = key.ToArray();
    }

    public int Version { get; }

    internal byte[] CopyKey() => _key.ToArray();

    public override string ToString()
        => $"DiagnosticHmacKeyMaterial(Version={Version}, Redacted=true)";
}

public interface IDiagnosticHmacKeyProvider
{
    ValueTask<DiagnosticHmacKeyMaterial> GetCurrentKeyAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DiagnosticKeyHash
{
    private readonly byte[] _bytes;

    public DiagnosticKeyHash(int keyVersion, ReadOnlySpan<byte> bytes)
    {
        if (keyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyVersion));
        }

        if (bytes.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("Diagnostic HMAC must be 32 bytes.", nameof(bytes));
        }

        KeyVersion = keyVersion;
        _bytes = bytes.ToArray();
    }

    public int KeyVersion { get; }

    public byte[] ToArray() => _bytes.ToArray();

    public override string ToString()
        => $"DiagnosticKeyHash(KeyVersion={KeyVersion}, HmacSha256={Convert.ToHexString(_bytes)})";
}

public sealed class HmacSha256DiagnosticKeyHasher
{
    private readonly IDiagnosticHmacKeyProvider _keyProvider;

    public HmacSha256DiagnosticKeyHasher(IDiagnosticHmacKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public async ValueTask<DiagnosticKeyHash> ComputeAsync(
        CanonicalBusinessKey canonicalKey,
        CancellationToken cancellationToken = default)
        => await ComputeAsync(
            context: null,
            canonicalKey,
            cancellationToken);

    public async ValueTask<DiagnosticKeyHash> ComputeAsync(
        MembershipRoute route,
        CanonicalBusinessKey canonicalKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        CsdtControlPlaneCatalog.ValidateRoute(route);
        return await ComputeAsync(
            string.Join(
                "\u001f",
                route.TargetProfile,
                route.SourceProfile,
                route.StreamCode,
                route.MaCsdt,
                route.TableName,
                canonicalKey.SchemaVersion),
            canonicalKey,
            cancellationToken);
    }

    private async ValueTask<DiagnosticKeyHash> ComputeAsync(
        string? context,
        CanonicalBusinessKey canonicalKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalKey);
        var material = await _keyProvider.GetCurrentKeyAsync(cancellationToken);
        var key = material.CopyKey();
        var canonical = canonicalKey.ToArray();
        var contextBytes = context is null
            ? []
            : Encoding.UTF8.GetBytes(context);
        var payload = new byte[
            sizeof(int) + contextBytes.Length + sizeof(int) + canonical.Length];
        BinaryPrimitives.WriteInt32BigEndian(
            payload.AsSpan(0, sizeof(int)),
            contextBytes.Length);
        contextBytes.CopyTo(payload, sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(
            payload.AsSpan(sizeof(int) + contextBytes.Length, sizeof(int)),
            canonical.Length);
        canonical.CopyTo(
            payload,
            sizeof(int) + contextBytes.Length + sizeof(int));
        try
        {
            using var hmac = new HMACSHA256(key);
            return new DiagnosticKeyHash(
                material.Version,
                hmac.ComputeHash(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(canonical);
            CryptographicOperations.ZeroMemory(contextBytes);
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
