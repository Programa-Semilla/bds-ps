// location-cascade.js — Spec 025 / FR-002 (generalized from province-canton-cascade.js, Spec 021).
//
// Data-driven dependent <select> cascade. Any <select> tagged
// `data-cascade-source` declares where to fetch its child options and which
// <select> to fill:
//
//   data-cascade-source      — marker (e.g. "province" / "canton")
//   data-cascade-endpoint    — URL to fetch (e.g. /api/cantons, /api/districts)
//   data-cascade-param       — query-string key (e.g. provinceId, cantonId)
//   data-cascade-target      — CSS selector of the dependent <select>
//   data-cascade-placeholder — first (empty) option's label
//
// On change of a source it fetches `{endpoint}?{param}={value}`, replaces the
// target's options (preserving a prior selection via `data-cascade-current`
// when still valid), and dispatches a *bubbling* change so a multi-tier chain
// (province → cantón → distrito) resets every lower tier automatically.
//
// Bound via event delegation on `document` so cascades injected after page
// load — the applicant /Search AJAX partials (_LookupEmpty / _LookupHit) —
// work without re-initialization.

(function () {
    'use strict';

    function repopulate(targetSelect, items, placeholderText) {
        // Preserve the user's prior selection across re-fetches (server seeds it
        // via data-cascade-current on edit forms).
        var previousValue = targetSelect.value || targetSelect.getAttribute('data-cascade-current') || '';
        targetSelect.innerHTML = '';

        var placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = placeholderText || 'Seleccione una opción';
        targetSelect.appendChild(placeholder);

        var matched = false;
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            var opt = document.createElement('option');
            opt.value = String(item.id);
            opt.textContent = item.name;
            if (previousValue && String(item.id) === String(previousValue)) {
                opt.selected = true;
                matched = true;
            }
            targetSelect.appendChild(opt);
        }
        if (!matched) {
            targetSelect.value = '';
            // A fresh, non-matching population invalidates the stale prior
            // selection so a chained reset cannot resurrect it.
            targetSelect.removeAttribute('data-cascade-current');
        }
        // Notify listeners (and any lower-tier cascade source) that the option
        // set changed. Bubbles so the delegated handler re-runs for the chain.
        targetSelect.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function onSourceChange(sourceSelect) {
        var targetSelector = sourceSelect.getAttribute('data-cascade-target');
        if (!targetSelector) return;
        var targetSelect = document.querySelector(targetSelector);
        if (!targetSelect) return;

        var endpoint = sourceSelect.getAttribute('data-cascade-endpoint');
        var param = sourceSelect.getAttribute('data-cascade-param');
        var placeholder = sourceSelect.getAttribute('data-cascade-placeholder');
        if (!endpoint || !param) return;

        var value = sourceSelect.value;
        if (!value) {
            repopulate(targetSelect, [], placeholder);
            return;
        }

        fetch(endpoint + '?' + param + '=' + encodeURIComponent(value), {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        })
            .then(function (response) {
                if (!response.ok) return [];
                return response.json();
            })
            .then(function (items) {
                repopulate(targetSelect, Array.isArray(items) ? items : [], placeholder);
            })
            .catch(function () {
                // Network failure: leave the existing options in place so the
                // user can still attempt to submit with their prior selection.
            });
    }

    // Delegated so both server-rendered and AJAX-injected cascades are handled.
    document.addEventListener('change', function (event) {
        var el = event.target;
        if (el && typeof el.matches === 'function' && el.matches('select[data-cascade-source]')) {
            onSourceChange(el);
        }
    });
})();
