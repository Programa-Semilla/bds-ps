// Spec 024 — styled confirmation modal for destructive actions (FR-006, FR-007, FR-012, NFR-004).
// Intercepts elements carrying [data-confirm], opens the single shared modal
// (_SharedConfirmModal.cshtml), and submits the *originating* form only on confirm.
//
// Graceful degradation (NFR-004): a migrated trigger keeps its inline native
// confirm() guard (e.g. <form onsubmit="return confirm(...)">). This script only
// neutralises that native guard and installs the modal when bootstrap.Modal is
// actually available — so if the JS/modal fails to load, the destructive action
// is still guarded by the native confirm().
(function () {
    'use strict';

    var MODAL_ID = 'fl-shared-confirm-modal';

    function confirmButtonClass(variant) {
        switch (variant) {
            case 'primary': return 'btn btn-primary';
            case 'secondary': return 'btn btn-outline-secondary';
            case 'statelocking': return 'btn btn-warning';
            case 'destructive':
            default: return 'btn btn-danger';
        }
    }

    function bootstrapAvailable() {
        return typeof window !== 'undefined' && window.bootstrap && window.bootstrap.Modal;
    }

    function init() {
        if (!bootstrapAvailable()) {
            // Leave native confirm() guards intact — never strip the fallback.
            return;
        }
        var modalEl = document.getElementById(MODAL_ID);
        if (!modalEl) {
            return;
        }

        var titleEl = modalEl.querySelector('[data-testid="confirm-title"]');
        var bodyEl = modalEl.querySelector('[data-testid="confirm-rationale"]');
        var confirmBtn = modalEl.querySelector('[data-testid="confirm-button"]');
        var cancelBtn = modalEl.querySelector('[data-testid="cancel-button"]');
        var modal = window.bootstrap.Modal.getOrCreateInstance(modalEl);

        var pendingForm = null;
        var pendingTrigger = null;

        function openFor(trigger) {
            var form = trigger.closest('form');
            if (!form) {
                return false;
            }
            // Surface native field validation (e.g. required inputs) before the modal,
            // matching the original onsubmit-after-validation order.
            if (typeof form.reportValidity === 'function' && !form.reportValidity()) {
                return false;
            }
            pendingForm = form;
            pendingTrigger = trigger;

            var variant = (trigger.getAttribute('data-confirm-variant') || 'destructive').toLowerCase();
            titleEl.textContent = trigger.getAttribute('data-confirm-title') || 'Confirmar acción';
            bodyEl.textContent = trigger.getAttribute('data-confirm-body')
                || '¿Deseás continuar? Esta acción no se puede deshacer.';
            confirmBtn.textContent = trigger.getAttribute('data-confirm-label') || 'Confirmar';
            confirmBtn.className = confirmButtonClass(variant) + ' ms-auto';
            cancelBtn.textContent = trigger.getAttribute('data-confirm-cancel') || 'Cancelar';

            modal.show();
            return true;
        }

        // Confirm → submit the originating form after the modal has closed,
        // so focus has returned to the trigger (FR-012) before navigation.
        confirmBtn.addEventListener('click', function () {
            var form = pendingForm;
            var trigger = pendingTrigger;
            pendingForm = null;
            pendingTrigger = null;
            modalEl.addEventListener('hidden.bs.modal', function once() {
                modalEl.removeEventListener('hidden.bs.modal', once);
                if (!form) {
                    return;
                }
                // requestSubmit(trigger) preserves the submitter's formaction/formmethod
                // (e.g. a Delete button using formaction in a multi-submit form).
                if (typeof form.requestSubmit === 'function') {
                    form.requestSubmit(trigger && trigger.type === 'submit' ? trigger : undefined);
                } else {
                    form.submit();
                }
            });
            modal.hide();
        });

        // Cancel/Esc/backdrop → abort with no side effect (FR-006).
        modalEl.addEventListener('hide.bs.modal', function () {
            // pendingForm is cleared on confirm; if still set here, user cancelled.
        });

        var triggers = document.querySelectorAll('[data-confirm]');
        for (var i = 0; i < triggers.length; i++) {
            (function (trigger) {
                if (trigger._flConfirmBound) {
                    return;
                }
                trigger._flConfirmBound = true;

                // Neutralise the inline native-confirm fallback now that the modal is wired.
                var form = trigger.closest('form');
                if (form) {
                    form.onsubmit = null;
                    form.removeAttribute('onsubmit');
                }
                trigger.onclick = null;
                trigger.removeAttribute('onclick');

                trigger.addEventListener('click', function (e) {
                    e.preventDefault();
                    openFor(trigger);
                });
            })(triggers[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
