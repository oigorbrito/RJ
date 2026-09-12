using System.Security.Cryptography;
using System.Text;

namespace RJ.Application.Sources;

public static class ProcessSourceDocumentHash
{
    public static string RawContentSha256(this ProcessSourceDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var bytes = Encoding.UTF8.GetBytes(source.RawContent);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
