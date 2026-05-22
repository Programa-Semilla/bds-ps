// Spec 024 — styled confirmation modal for destructive actions (FR-006, FR-007, FR-012, NFR-004).
//
// Self-contained: Tabler bundles Bootstrap's data-API but does NOT expose the
// `window.bootstrap` JS namespace, so we drive the modal ourselves (toggle the
// `.show` class + a `.modal-backdrop`, trap focus, handle Esc). This reuses
// Bootstrap's modal CSS without depending on its JS global.
//
// Intercepts [data-confirm] triggers, opens the single shared modal
// (_SharedConfirmModal.cshtml), and submits the *originating* form only on confirm.
//
// Graceful degradation (NFR-004): a migrated trigger keeps its inline native
// confirm() guard (onclick / form onsubmit). This script neutralises that guard
// and installs the modal on load; if the script fails to load, the native
// confirm() still guards the destructive action.
(function () {
    'use strict';

    var MODAL_ID = 'fl-shared-confirm-modal';

    function confirmButtonClass(variant) {
        switch ((variant || '').toLowerCase()) {
            case 'primary': return 'btn btn-primary';
            case 'secondary': return 'btn btn-outline-secondary';
            case 'statelocking': return 'btn btn-warning';
            case 'destructive':
            default: return 'btn btn-danger';
        }
    }

    function init() {
        var modalEl = document.getElementById(MODAL_ID);
        if (!modalEl) {
            return;
        }

        var titleEl = modalEl.querySelector('[data-testid="confirm-title"]');
        var bodyEl = modalEl.querySelector('[data-testid="confirm-rationale"]');
        var confirmBtn = modalEl.querySelector('[data-testid="confirm-button"]');
        var cancelBtn = modalEl.querySelector('[data-testid="cancel-button"]');
        var closeBtn = modalEl.querySelector('.btn-close');

        var pendingForm = null;
        var pendingTrigger = null;
        var returnFocusEl = null;
        var isOpen = false;

        function open(trigger) {
            var form = trigger.closest('form');
            if (!form) {
                return;
            }
            // Surface native field validation (e.g. required inputs) before the modal.
            if (typeof form.reportValidity === 'function' && !form.reportValidity()) {
                return;
            }
            pendingForm = form;
            pendingTrigger = trigger;
            returnFocusEl = trigger;

            var variant = (trigger.getAttribute('data-confirm-variant') || 'destructive');
            titleEl.textContent = trigger.getAttribute('data-confirm-title') || 'Confirmar acción';
            bodyEl.textContent = trigger.getAttribute('data-confirm-body')
                || '¿Deseás continuar? Esta acción no se puede deshacer.';
            confirmBtn.textContent = trigger.getAttribute('data-confirm-label') || 'Confirmar';
            confirmBtn.className = confirmButtonClass(variant) + ' ms-auto';
            cancelBtn.textContent = trigger.getAttribute('data-confirm-cancel') || 'Cancelar';

            modalEl.style.display = 'block';
            modalEl.classList.add('show');
            modalEl.removeAttribute('aria-hidden');
            modalEl.setAttribute('aria-modal', 'true');
            modalEl.setAttribute('role', 'dialog');
            document.body.classList.add('modal-open');

            if (!document.querySelector('.modal-backdrop[data-fl-confirm-backdrop]')) {
                var backdrop = document.createElement('div');
                backdrop.className = 'modal-backdrop fade show';
                backdrop.setAttribute('data-fl-confirm-backdrop', '');
                backdrop.addEventListener('click', function () { close(); });
                document.body.appendChild(backdrop);
            }

            isOpen = true;
            confirmBtn.focus(); // FR-012 — move focus into the dialog.
        }

        // Abort with no side effect (FR-006); restore focus to the trigger (FR-012).
        function close() {
            if (!isOpen) {
                return;
            }
            isOpen = false;
            pendingForm = null;
            pendingTrigger = null;
            modalEl.classList.remove('show');
            modalEl.style.display = 'none';
            modalEl.setAttribute('aria-hidden', 'true');
            modalEl.removeAttribute('aria-modal');
            document.body.classList.remove('modal-open');
            var bd = document.querySelector('.modal-backdrop[data-fl-confirm-backdrop]');
            if (bd) {
                bd.parentNode.removeChild(bd);
            }
            if (returnFocusEl && typeof returnFocusEl.focus === 'function') {
                returnFocusEl.focus();
            }
        }

        function proceed() {
            var form = pendingForm;
            var trigger = pendingTrigger;
            isOpen = false;
            pendingForm = null;
            pendingTrigger = null;
            modalEl.classList.remove('show');
            modalEl.style.display = 'none';
            modalEl.setAttribute('aria-hidden', 'true');
            modalEl.removeAttribute('aria-modal');
            document.body.classList.remove('modal-open');
            var bd = document.querySelector('.modal-backdrop[data-fl-confirm-backdrop]');
            if (bd) {
                bd.parentNode.removeChild(bd);
            }
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
        }

        confirmBtn.addEventListener('click', proceed);
        cancelBtn.addEventListener('click', close);
        if (closeBtn) {
            closeBtn.addEventListener('click', close);
        }
        document.addEventListener('keydown', function (e) {
            if (!isOpen) {
                return;
            }
            if (e.key === 'Escape' || e.key === 'Esc') {
                close();
                return;
            }
            // FR-012 — trap focus within the dialog while open.
            if (e.key === 'Tab') {
                var focusables = Array.prototype.filter.call(
                    modalEl.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'),
                    function (el) { return el.offsetParent !== null && !el.disabled; });
                if (focusables.length === 0) {
                    return;
                }
                var first = focusables[0];
                var last = focusables[focusables.length - 1];
                if (e.shiftKey && document.activeElement === first) {
                    e.preventDefault();
                    last.focus();
                } else if (!e.shiftKey && document.activeElement === last) {
                    e.preventDefault();
                    first.focus();
                }
            }
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
                    open(trigger);
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
