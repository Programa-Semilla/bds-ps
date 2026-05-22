// province-canton-cascade.js — Spec 021 / T060 / R-4 / FR-013.
//
// On Province <select> change, fetches `/api/cantons?provinceId={id}`,
// replaces the linked Cantón <select>'s options, and preserves any
// previously-selected Cantón id if it still appears in the returned set.
//
// Sources are tagged with `data-cascade-source="province"`; the target
// selector is read from `data-cascade-target` on the source.

(function () {
    'use strict';

    function replaceCantons(targetSelect, cantons) {
        // R-4 — preserve the user's prior selection across re-fetches.
        var previousValue = targetSelect.value || targetSelect.getAttribute('data-cascade-current') || '';
        targetSelect.innerHTML = '';

        var placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = 'Seleccione un cantón';
        targetSelect.appendChild(placeholder);

        var matched = false;
        for (var i = 0; i < cantons.length; i++) {
            var c = cantons[i];
            var opt = document.createElement('option');
            opt.value = String(c.id);
            opt.textContent = c.name;
            if (previousValue && String(c.id) === String(previousValue)) {
                opt.selected = true;
                matched = true;
            }
            targetSelect.appendChild(opt);
        }
        if (!matched) {
            targetSelect.value = '';
        }
        // Notify listeners that the cantón set changed (e.g. validation refresh).
        targetSelect.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function onProvinceChange(sourceSelect) {
        var targetSelector = sourceSelect.getAttribute('data-cascade-target');
        if (!targetSelector) return;
        var targetSelect = document.querySelector(targetSelector);
        if (!targetSelect) return;

        var provinceId = sourceSelect.value;
        if (!provinceId) {
            replaceCantons(targetSelect, []);
            return;
        }

        fetch('/api/cantons?provinceId=' + encodeURIComponent(provinceId), {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        })
            .then(function (response) {
                if (!response.ok) return [];
                return response.json();
            })
            .then(function (cantons) {
                replaceCantons(targetSelect, Array.isArray(cantons) ? cantons : []);
            })
            .catch(function () {
                // Network failure: leave the existing options in place so the
                // user can still attempt to submit with their prior selection.
            });
    }

    function init() {
        var sources = document.querySelectorAll('select[data-cascade-source="province"]');
        for (var i = 0; i < sources.length; i++) {
            (function (sourceSelect) {
                sourceSelect.addEventListener('change', function () {
                    onProvinceChange(sourceSelect);
                });
            })(sources[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
