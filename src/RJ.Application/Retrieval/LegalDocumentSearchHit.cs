namespace RJ.Application.Retrieval;

public sealed record LegalDocumentSearchHit(
    LegalDocumentSnapshot Document,
    float Rank);
