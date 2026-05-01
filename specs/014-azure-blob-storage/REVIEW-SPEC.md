# Spec Review: 014-azure-blob-storage

**Spec:** specs/014-azure-blob-storage/spec.md
**Date:** 2026-05-01
**Reviewer:** speckit-spex-gates-review-spec (autonomous)

## Overall Assessment

**Status:** PASS-WITH-FINDINGS

Spec is dense, well-clarified (5 Q/A in Clarifications), thorough on edge cases, and aligned with project constitution (real-DB tests, vendored-first, locale-aware). Two ambiguities and one wording fix surfaced; no blockers.

## Auto-Fixes Applied

1. SC-006: replaced "twice the configured streaming buffer size" (term not defined in FRs) with "a small constant multiple (≤ 4×) of the configured streaming threshold from FR-020" to bind to the actual configured value.

## Findings

### Unambiguous (auto-fixed)
- **SC-006 wording**: undefined "streaming buffer size" — fixed to reference FR-020 streaming threshold.

### Ambiguous (judgment needed; left as-is)
- **FR-021**: states "Caps for other categories MUST have explicit defaults documented" but only the signed-PDF cap (20 MiB) is given. Defaults for supplier-catalog-imports, application-attachments, generated-artifacts are not stated. Needs operator/PM judgment.
- **FR-017 vs Edge Case "Local-mode parity gaps"**: spec says local provider may not support time-limited URLs, but no current category uses URLs and FR-017 is silent on local-mode behaviour. Minor — resolve when a URL-served category appears.
- **FR-008 vs User Story 3 AC#3**: FR-008 says tests "MAY fall back to LocalFilesystem only when explicitly configured"; AC#3 mentions a warning-logged fallback. Tiny tension on whether fallback is automatic-with-warning or strictly opt-in.

### Blockers
- None.

## Dimension Scores

- Completeness: 4/5 (FR-021 default values gap)
- Clarity: 4/5 (minor local-mode parity ambiguity)
- Implementability: 5/5 (configuration surface keys, container names, key format all concrete)
- Testability: 5/5 (SC-001..SC-009 all measurable; AzureBlob/Azurite/LocalFilesystem matrix is exercisable)

## Constitution Alignment

- Real-DB / real-backend testing: respected (Azurite, not mocks). 
- Vendored-first: respects "new managed dependency requires spec approval" via Assumptions.
- es-CR locale: not impacted; spec calls out localized error for oversize uploads.
- Configuration-contract naming (`Storage:Provider`, `Storage:LocalFilesystem:RootPath`, `SignedUpload:MaxSizeBytes`) is acceptable per project guidance — these are contracts, not framework choices.
- No leakage of framework SDK names (e.g. `Azure.Storage.Blobs`) — spec stays at WHAT level. Cloud-service names ("Azure Blob Storage", "Azurite", "managed identity") are part of the configuration contract and are fine.

## Recommendation

**Ready for plan stage.** Pipeline may proceed. The two ambiguous findings can be resolved during /speckit-plan or surfaced via /speckit-clarify if the operator wants concrete defaults for FR-021 before planning.
