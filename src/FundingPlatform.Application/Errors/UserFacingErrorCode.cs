namespace FundingPlatform.Application.Errors;

/// <summary>
/// Spec 012 / FR-014 — Application-layer user-facing failure reasons.
///
/// Application services raise these codes (instead of inline English sentinel
/// strings) at the Application/Web boundary. The Web layer maps each code to
/// a Spanish (es-CR) string via <c>IUserFacingErrorTranslator</c> before the
/// message reaches the user via TempData / ModelState.
///
/// NFR-001 invariant: all Application-layer code, logs, and exception
/// messages stay English. Only the Web layer translates these codes into
/// Spanish for end-user surfaces.
/// </summary>
public enum UserFacingErrorCode
{
    /// <summary>Catch-all for a domain rule rejection whose original English
    /// message we still want to surface verbatim (e.g. <c>InvalidOperationException</c>
    /// thrown by domain entities). The Web layer renders the generic Spanish
    /// equivalent; the original English detail is logged but not displayed.</summary>
    OperationRejected,

    // Application aggregate
    ApplicationNotFound,
    ApplicationNotUnderReview,
    ApplicationItemNotFound,
    ApplicationNotOwnedByApplicant,
    SupplierRequiredOnApprove,
    InvalidReviewDecision,
    ConcurrentApplicationModification,

    // Appeal aggregate
    AppealAccessDenied,
    NoOpenAppealForMessage,
    UnknownAppealResolution,
    ConcurrentAppealModification,

    // Funding-agreement aggregate
    AgreementGenerationPreconditionsNotMet,
    AgreementRegenerationPreconditionsNotMet,
    AgreementPdfRenderingFailed,
    AgreementGenerationFailed,
    ConcurrentAgreementModification,

    // Signed upload (resource not found / authz)
    SignedUploadResourceNotFound,
    ConcurrentSignedUploadModification,

    // Signed upload (validation)
    SignedUploadStaleAgreementVersion,
    SignedUploadAlreadyPending,
    SignedUploadNoPendingToReplace,
    SignedUploadWrongPendingId,
    SignedUploadNoPendingToWithdraw,
    SignedUploadStalePendingId,
    SignedUploadNoPendingToApprove,
    SignedUploadNoPendingToReject,
    SignedUploadRejectionCommentRequired,

    // Signed upload (intake validation)
    SignedUploadUnsupportedContentType,
    SignedUploadFileEmpty,
    SignedUploadFileTooLarge,
    SignedUploadContentUnreadable,
    SignedUploadNotAPdf,
    SignedUploadMissingPdfHeader,

    // Spec 015 — multi-currency quotes
    /// <summary>FR-018 — applicant tried to convert/save a non-CRC quotation but no
    /// reference rate has been published yet.</summary>
    MissingExchangeRate,
    /// <summary>Applicant tried to save a quotation in a currency that an admin has disabled.</summary>
    CurrencyDisabled,
    /// <summary>FR-008 — admin tried to edit a rate that has already been snapshotted by a quotation.
    /// The rate must be superseded by publishing a new row.</summary>
    RateImmutableUseSupersede,
    /// <summary>FR-007 — admin tried to publish a rate whose (source, target, effectiveAt) tuple
    /// already exists.</summary>
    DuplicateRateTimestamp,
    /// <summary>FR-007a — admin tried to publish a rate with a future-dated EffectiveAtUtc.</summary>
    FutureDatedRateRejected,
}
