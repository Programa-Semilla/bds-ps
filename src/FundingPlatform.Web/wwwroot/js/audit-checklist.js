// Spec 040 / FR-007 — live reactivity for the auditor checklist actions.
// The "Aprobar auditoría" / "Devolver al revisor" buttons are server-rendered from the
// SAVED checklist state; this script makes them reflect the auditor's CURRENT selection
// immediately so the available action matches what is on screen before they submit.
//
// Rules (mirror the server gate in AuditWorkflowService):
//   * Any item marked «No conforme»            -> Approve disabled.
//   * At least one «No conforme» WITH a reason  -> Return enabled (else disabled).
(function () {
    'use strict';

    function update(form) {
        var items = form.querySelectorAll('[data-testid="audit-checklist-item"]');
        var hasNonCompliant = false;
        var hasNonCompliantWithReason = false;

        items.forEach(function (item) {
            var notCompliant = item.querySelector('[data-testid="audit-mark-noncompliant"]');
            var reason = item.querySelector('[data-testid="audit-mark-reason"]');
            if (notCompliant && notCompliant.checked) {
                hasNonCompliant = true;
                if (reason && reason.value.trim().length > 0) {
                    hasNonCompliantWithReason = true;
                }
            }
        });

        var approve = form.querySelector('[data-testid="audit-approve"]');
        var ret = form.querySelector('[data-testid="audit-return"]');
        if (approve) approve.disabled = hasNonCompliant;
        if (ret) ret.disabled = !hasNonCompliantWithReason;
    }

    function init() {
        var form = document.querySelector('[data-testid="audit-checklist-form"]');
        if (!form) return;
        form.addEventListener('change', function () { update(form); });
        form.addEventListener('input', function () { update(form); });
        update(form);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
