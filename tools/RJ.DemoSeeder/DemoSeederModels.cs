using System.Text.Json;

namespace RJ.DemoSeeder;

public sealed record DemoCase(
    string CaseId,
    string Cnj,
    string SourceName,
    JsonElement Process,
    IReadOnlyList<DemoDocument> Documents);

public sealed record DemoDocument(string DocumentId, string Content);
