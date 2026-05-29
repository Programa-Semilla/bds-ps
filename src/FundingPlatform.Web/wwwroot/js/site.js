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

// Sidebar accordion (collapsable admin groups): only one section open at a time.
// Expanding/collapsing a single section is Bootstrap's collapse data-API; this
// listener enforces the accordion by collapsing any sibling section that is open
// when another begins to expand. It relies on the bubbling `show.bs.collapse`
// DOM event (dispatched by Tabler's bundled collapse plugin) and therefore does
// NOT depend on the window.bootstrap global, which Tabler does not expose.
(function sidebarAccordion() {
    function init() {
        var accordion = document.getElementById('sidebar-accordion');
        if (!accordion) return;
        accordion.addEventListener('show.bs.collapse', function (e) {
            var opening = e.target;
            if (!opening || !opening.classList || !opening.classList.contains('collapse')) return;
            var open = accordion.querySelectorAll('.collapse.show');
            for (var i = 0; i < open.length; i++) {
                var panel = open[i];
                if (panel === opening) continue;
                // Click the open sibling's header → Bootstrap collapses it (fires
                // hide.bs.collapse, not show, so there is no recursion).
                var header = accordion.querySelector('[aria-controls="' + panel.id + '"]');
                if (header) header.click();
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
