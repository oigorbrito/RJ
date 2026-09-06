namespace RJ.Application.Rag;

public sealed record LocalRagChunk(
    string ChunkId,
    string CaseId,
    string ResponseId,
    string ResponseType,
    string SourcePath,
    string Section,
    string Content,
    IReadOnlyDictionary<string, string> Metadata);
