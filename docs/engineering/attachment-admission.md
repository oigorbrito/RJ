# ATT-001 attachment binary and extraction admission

Status: executable admission boundary implemented; binary acquisition/parser/OCR fidelity remains BLOCKED until an authorized real attachment binary plus independently verifiable extracted-text evidence is supplied.

## Purpose

ATT-001 closes the gap between attachment metadata and admissible attachment content. Metadata alone never authorizes claims about file contents.

The boundary separates four roles:

1. canonical process attachment metadata;
2. acquired binary artifact;
3. extraction provenance (parser/OCR method and versions);
4. extracted UTF-8 text admitted for chunking/context.

## Existing contracts reused

Wave B already provided:

- `ProcessAttachmentContent`;
- `ProcessAttachmentContentAdmission`;
- `ProcessAttachmentChunker` with the frozen 800–1200 token / 150 overlap contract.

Wave H does not replace these contracts. It provides the missing binary-to-content admission evidence in front of them.

## Binary artifact contract

`AttachmentBinaryArtifact` records:

- canonical case id;
- attachment id;
- immutable binary source reference;
- file name;
- media type;
- extension;
- exact SHA-256;
- exact byte length;
- acquisition timestamp.

The binary bytes must be re-read and match both hash and length before content is admitted.

## Extraction evidence

`AttachmentExtractionEvidence` records:

- case/attachment identity;
- input binary SHA-256;
- extractor id/version;
- extraction method;
- immutable extracted-text reference;
- exact extracted-text SHA-256 over the UTF-8 bytes;
- exact .NET `string.Length` (UTF-16 code-unit length) after strict UTF-8 decoding;
- extraction timestamp;
- whether OCR was used;
- OCR engine id/version when OCR was used.

OCR metadata is mandatory when `ocrUsed=true` and prohibited when OCR was not used.

No parser or OCR provider is selected by this wave. Provider choice remains a project/empirical decision after real evidence exists.

## Derivation provenance

A successful admission also produces `AttachmentDerivationProvenance`, preserving the immutable chain:

`binary source reference + binary SHA-256 -> extractor id/version/method -> optional OCR engine/version -> extracted-text reference + extracted-text SHA-256`.

This derivation record is separate from `ProcessAttachmentContent`. The existing content/chunking contract remains unchanged, while the binary-to-text derivation remains auditable.

## Admission rules

`AttachmentAdmissionService` fails closed unless:

- case id matches the canonical `LegalCase`;
- attachment id exists in observed process metadata;
- binary extension matches observed attachment metadata;
- binary and extraction evidence identify the same binary hash;
- binary bytes match expected length/hash;
- extracted text bytes are strict UTF-8;
- extracted text length/hash match the frozen evidence;
- extracted text is non-empty;
- resulting `ProcessAttachmentContent` is accepted by the existing metadata/content admission rule;
- derivation provenance can be constructed from the verified evidence.

Only after all gates pass are both `ProcessAttachmentContent` and its derivation provenance produced.

## Frozen manifest and canonical case identity

`AttachmentAdmissionManifest` freezes the canonical source reference/hash/timestamp, binary evidence and extraction evidence. Because the existing `JuditProcessSourceAdapter` derives `LegalCaseId` from the canonical source reference file name, the manifest requires `Path.GetFileNameWithoutExtension(canonicalSourceReference) == binary.caseId`. This converts an existing adapter convention into an explicit admission rule rather than a hidden verifier dependency.

## External verifier

`tools/RJ.AttachmentAdmissionVerifier` validates a manifest plus external artifacts and reconstructs the canonical process through the existing `JuditProcessSourceAdapter`.

Usage:

```powershell
dotnet run --project tools/RJ.AttachmentAdmissionVerifier/RJ.AttachmentAdmissionVerifier.csproj -- `
  <manifest.json> `
  <manifest-sha256> `
  <artifact-root> `
  <canonical-source.json>
```

The exact manifest bytes and canonical source bytes are SHA-256 checked before admission.

Exit codes:

- `0`: all supplied attachment admission gates pass;
- `2`: invocation/precondition/external evidence missing;
- `3`: manifest, hash, metadata, binary or extraction verification failure.

Artifact references are lexically confined to the supplied root with OS-appropriate path case semantics. This is not a symlink/reparse-point sandbox guarantee.

## Canonical gate

```powershell
.\scripts\test-rjudi-wave-h.ps1
```

The gate first executes work independent of the real attachment:

1. solution build;
2. new attachment admission tests plus existing content/chunker tests;
3. `git diff --check`.

Only then does it require:

- `RJ_ATT_MANIFEST_PATH`;
- `RJ_ATT_MANIFEST_SHA256`;
- `RJ_ATT_ARTIFACT_ROOT`;
- `RJ_ATT_CANONICAL_SOURCE_PATH`.

If those artifacts are absent, the gate returns `BLOCKED ATT-001` with exit code `2` after executable local contract work has run.

## Required real-evidence handoff

To unblock ATT-001, supply:

- authorized immutable attachment binary;
- canonical source fixture containing the corresponding attachment metadata;
- canonical source reference whose file-name stem equals the case id;
- binary SHA-256 and byte length;
- extracted UTF-8 text artifact;
- independently reviewable expected extracted-text SHA-256 and .NET string length;
- extractor id/version/method;
- OCR engine id/version if OCR was used;
- acquisition/extraction timestamps;
- frozen manifest SHA-256.

## Explicit nonclaims

Wave H does not demonstrate:

- acquisition from the upstream attachment provider;
- PDF/HTML/image parser correctness on a real judicial attachment;
- OCR fidelity;
- superiority of any parser/OCR engine;
- production malware scanning/content-type sniffing;
- all attachment formats;
- binary retention policy;
- symlink-safe filesystem sandboxing;
- attachment content correctness until a real authorized binary and reference extraction are verified.
