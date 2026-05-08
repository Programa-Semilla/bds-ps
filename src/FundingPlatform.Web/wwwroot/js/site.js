// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Spec 015 / T411 — initialise Bootstrap tooltips so the
// ConversionIndicatorViewComponent's data-bs-toggle="tooltip" attribute renders
// on hover. Tabler bundles Bootstrap; the global `bootstrap.Tooltip` constructor
// is available. Idempotent: safe to call again on partial swaps.
(function initTooltips() {
    function init() {
        if (typeof window === 'undefined' || !window.bootstrap || !window.bootstrap.Tooltip) {
            return;
        }
        var triggers = document.querySelectorAll('[data-bs-toggle="tooltip"]');
        for (var i = 0; i < triggers.length; i++) {
            var el = triggers[i];
            // Avoid double-init on re-runs (e.g. async panel loads).
            if (el._bsTooltipInited) continue;
            el._bsTooltipInited = true;
            try {
                new window.bootstrap.Tooltip(el);
            } catch (_e) {
                // Defensive: never let a single broken tooltip break page interactivity.
            }
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
