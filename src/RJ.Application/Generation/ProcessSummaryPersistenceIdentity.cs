using System.Security.Cryptography;
using System.Text;
using RJ.Application.Security;

namespace RJ.Application.Generation;

public static class ProcessSummaryPersistenceIdentity
{
    public static string ScopedIdempotencyKey(CallerContext caller, string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var key = Require(idempotencyKey, nameof(idempotencyKey));
        return string.Join(':', Hash(caller.TenantId), Hash(caller.SubjectId), Hash(key));
    }

    public static string TenantIdHash(CallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return Hash(caller.TenantId);
    }

    public static string SubjectIdHash(CallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return Hash(caller.SubjectId);
    }

    public static string Hash(string value)
    {
        var normalized = Require(value, nameof(value));
        var bytes = Encoding.UTF8.GetBytes(normalized);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Persistence identity value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
