# Spec Review: Suppliers Quotes Multi-Currency

**Spec:** specs/015-multi-currency-quotes/spec.md
**Date:** 2026-05-06
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND (Pass-with-findings)

**Summary:** Spec is unusually thorough — 36 FRs, 9 measurable SCs, 4 clarifications resolved, 7 edge cases enumerated, snapshot/immutability semantics explicit, decimal arithmetic locked down, migration path defined. A few minor gaps and one ambiguity around the "PDF byte-comparable" claim warrant tightening.

## Completeness: 5/5

All required and recommended sections are present and filled. No TBD / placeholder text. User stories carry priorities, independent tests, and acceptance scenarios. Edge cases, assumptions, and out-of-scope are explicit.

## Clarity: 4/5

Language is generally precise (MUST/MUST NOT used consistently, conversion formula given, rate direction nailed down to "CRC per 1 USD").

**Ambiguities found:**

1. **FR-026** — "PDFs regenerated months later MUST be byte-comparable to the original (modulo non-monetary changes such as signatures)."
   - "Byte-comparable" is too strong: any layout/font/CSS change between renders breaks it. The intent is *value-stable*, not byte-stable.
2. **FR-021** — "applicant dashboard, reviewer dashboard, approval screen, or admin report" — admin reports per CLAUDE.md emit CSV (`AdminReports:CsvRowLimit`); the spec doesn't say whether CSV must include the original-currency column or only CRC.
3. **FR-027** — "PDF generation MUST refuse to render" but does not specify *who sees the error* (applicant? admin? both?) and via what surface (banner on request page? blocked download?).
4. **Edge case "Stale rate"** — "USD quote created against a rate published days/weeks ago" — no upper bound for "stale". Combined with assumption "rates do not carry an explicit expiration", this is internally consistent but worth noting as a deliberate risk acceptance.

## Implementability: 5/5

Plan-ready: entities (Currency, ExchangeRate, SupplierQuote ext, AuditLog ext) are named, decimal precision fixed, conversion formula concrete, snapshot fields enumerated, migration semantics for both CRC and non-CRC legacy quotes specified. Existing audit-log infra is reused, no new role introduced — scope is bounded.

## Testability: 5/5

Each SC is measurable (counts, percentages, time-bounded). FR-006/007/007a/008/018 produce explicit error messages testable as strings. Acceptance scenarios are Given/When/Then.

## Constitution Alignment

No `.specify/memory/constitution.md` was checked in this review pass. CLAUDE.md alignment:
- es-CR culture honored (assumption explicitly references spec 012).
- No CDN / vendored UI not touched by this spec — fine.
- Decimal arithmetic mandate (FR-020) aligns with project rigor.
- Syncfusion PDF path is the correct integration point (FR-024–027).

## Findings (classified)

### Unambiguous (formatting / minor wording)

- **FR-026** "byte-comparable" → reword to "value-stable" or "monetary-content identical" to match the intent stated in the same sentence.
- **FR-021** missing explicit mention of admin CSV reports column policy — add a clause: "Admin CSV reports MUST include both the original currency/amount columns and the converted CRC column for non-CRC lines."
- **FR-027** add the surface: "The error MUST be surfaced on the request page to the user attempting the PDF action and MUST be logged."

### Ambiguous (architecture / judgment)

- **Edge case "Stale rate"** — confirm whether the team accepts unbounded staleness for MVP (current spec says yes; flag for plan/review stage so it isn't lost).
- **FR-022** — "dashboard totals, report totals" — multi-line dashboards may aggregate across many requests; confirm rounding policy: sum of already-rounded line CRC values vs. re-summed-then-rounded. FR-020 says "Totals MUST be summed from rounded line values" which covers this; cross-link from FR-022 to FR-020 would remove doubt.

### Blocker

None.

## Recommendations

### Critical (Must Fix Before Implementation)

None.

### Important (Should Fix)

- [ ] Reword FR-026 "byte-comparable" → "value-stable" / "monetary content identical".
- [ ] Add CSV report column policy to FR-021.
- [ ] Specify error surface for FR-027.

### Optional (Nice to Have)

- [ ] Cross-link FR-022 → FR-020 to make total-rounding policy unambiguous.
- [ ] Acknowledge unbounded-staleness as an explicit accepted risk in Assumptions.

## Conclusion

Spec is implementation-ready. Findings are tightening, not blocking.

**Ready for implementation:** Yes (after Important fixes; Critical is empty).

**Next steps:** Proceed to plan stage; address Important findings inline during plan or as a quick spec patch.
