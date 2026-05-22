// Spec 024 — unified in-app toast notifications (FR-001..FR-005, FR-011, FR-013).
//
// Self-contained: Tabler bundles Bootstrap's data-API but does NOT expose the
// `window.bootstrap` JS namespace, so we drive the toast lifecycle ourselves
// (add/remove the `.show` class, own auto-dismiss timer, own close handler).
// No new dependency, no CDN.
//
// Public API:
//   window.Notify.toast({ variant, message, sticky? })
//   window.Notify.success(msg) / .error(msg) / .warning(msg) / .info(msg)
//
// success/info auto-dismiss after AUTO_DELAY; warning/error are sticky.
// On load it also activates any server-rendered toasts emitted by the
// TempData→toast bridge (_NotificationToasts.cshtml).
(function () {
    'use strict';

    var AUTO_DELAY = 5000; // FR-004 — success/info auto-dismiss interval.

    function isAssertive(variant) {
        return variant === 'error' || variant === 'warning';
    }

    function variantClass(variant) {
        // text-bg-* drives the visible style. The bare alert-* marker class is
        // visually inert (no .alert base) but preserves legacy ".alert-success"
        // / ".alert-danger" E2E selectors.
        switch (variant) {
            case 'success': return 'text-bg-success alert-success';
            case 'error': return 'text-bg-danger alert-danger';
            case 'warning': return 'text-bg-warning alert-warning';
            case 'info': return 'text-bg-info alert-info';
            default: return 'text-bg-secondary';
        }
    }

    function closeClass(variant) {
        return variant === 'warning' ? 'btn-close' : 'btn-close btn-close-white';
    }

    function getContainer() {
        var el = document.querySelector('[data-testid="toast-container"]');
        if (!el) {
            el = document.createElement('div');
            el.className = 'toast-container position-fixed top-0 end-0 p-3';
            el.setAttribute('data-testid', 'toast-container');
            el.style.zIndex = '1090';
            document.body.appendChild(el);
        }
        return el;
    }

    function dismiss(el) {
        if (el._flDismissed) {
            return;
        }
        el._flDismissed = true;
        el.classList.remove('show');
        // Brief delay lets the opacity transition run before removal (FR-005).
        window.setTimeout(function () {
            if (el.parentNode) {
                el.parentNode.removeChild(el);
            }
        }, 200);
    }

    // Reveal a toast element (server-rendered or freshly built) and wire its
    // close button + auto-dismiss timer.
    function reveal(el, sticky) {
        if (el._flRevealed) {
            return;
        }
        el._flRevealed = true;
        el.classList.add('show'); // .toast is display:none until .show.
        var close = el.querySelector('[data-bs-dismiss="toast"], .btn-close');
        if (close) {
            close.addEventListener('click', function () { dismiss(el); });
        }
        if (!sticky) {
            window.setTimeout(function () { dismiss(el); }, AUTO_DELAY);
        }
    }

    function buildToastElement(opts) {
        var variant = opts.variant || 'info';
        var toast = document.createElement('div');
        toast.className = 'toast fl-toast border-0 ' + variantClass(variant);
        toast.setAttribute('role', isAssertive(variant) ? 'alert' : 'status');
        toast.setAttribute('aria-live', isAssertive(variant) ? 'assertive' : 'polite');
        toast.setAttribute('aria-atomic', 'true');
        toast.setAttribute('data-toast-variant', variant);
        if (opts.testid) {
            toast.setAttribute('data-testid', opts.testid);
        }

        var flex = document.createElement('div');
        flex.className = 'd-flex';

        var body = document.createElement('div');
        body.className = 'toast-body';
        body.textContent = opts.message || ''; // textContent — never inject HTML.

        var close = document.createElement('button');
        close.type = 'button';
        close.className = closeClass(variant) + ' me-2 m-auto';
        close.setAttribute('data-bs-dismiss', 'toast');
        close.setAttribute('aria-label', 'Cerrar');

        flex.appendChild(body);
        flex.appendChild(close);
        toast.appendChild(flex);
        return toast;
    }

    function show(opts) {
        var variant = opts.variant || 'info';
        var sticky = (typeof opts.sticky === 'boolean') ? opts.sticky : isAssertive(variant);
        var el = buildToastElement(opts);
        getContainer().appendChild(el);
        reveal(el, sticky);
        return el;
    }

    window.Notify = {
        toast: function (opts) { return show(opts || {}); },
        success: function (message) { return show({ variant: 'success', message: message }); },
        error: function (message) { return show({ variant: 'error', message: message }); },
        warning: function (message) { return show({ variant: 'warning', message: message }); },
        info: function (message) { return show({ variant: 'info', message: message }); }
    };

    // FR-002/FR-011 — activate server-rendered toasts (TempData bridge) present at load.
    function initServerRenderedToasts() {
        var nodes = document.querySelectorAll('[data-testid="toast-container"] .toast[data-toast-variant]');
        for (var i = 0; i < nodes.length; i++) {
            var el = nodes[i];
            var sticky = el.getAttribute('data-toast-sticky') === 'true';
            reveal(el, sticky);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initServerRenderedToasts);
    } else {
        initServerRenderedToasts();
    }
})();
