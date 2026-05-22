// submit-gate.js — Spec 021 / FR-017 — applicant draft submit gating.
//
// The draft editor's submit control is a real <button data-submit-gate>,
// rendered `disabled`. This module recomputes draft completeness and toggles
// the button: enabled only when the Application has Impact defined, >= 1 Item,
// and every required field filled. While disabled, the button's `title`
// enumerates exactly what is missing (US2 Acceptance #1). An enabled click
// routes to the PublicCode-bound /review page.
//
// Server-rendered facts arrive as data-* attributes on the button:
//   data-item-count   — current Item count
//   data-impact-set   — "true" once the Impact step is complete
//   data-review-url   — target /review URL
// Required free-text fields are discovered via [data-required="true"].

(function () {
    'use strict';

    function evaluate(button) {
        var missing = [];

        if (button.getAttribute('data-impact-set') !== 'true') {
            missing.push('Impacto');
        }

        var itemCount = parseInt(button.getAttribute('data-item-count') || '0', 10);
        if (isNaN(itemCount) || itemCount < 1) {
            missing.push('Al menos un ítem');
        }

        var required = document.querySelectorAll('[data-required="true"]');
        for (var i = 0; i < required.length; i++) {
            var field = required[i];
            if (!field.value || !field.value.trim()) {
                missing.push(field.getAttribute('data-field-label')
                    || field.getAttribute('name')
                    || 'Campo requerido');
            }
        }

        if (missing.length === 0) {
            button.disabled = false;
            button.title = 'Revise antes de enviar';
        } else {
            button.disabled = true;
            button.title = 'Faltan: ' + missing.join(', ');
        }
    }

    function init() {
        var button = document.querySelector('[data-submit-gate]');
        if (!button) return;

        button.addEventListener('click', function () {
            if (button.disabled) return;
            var url = button.getAttribute('data-review-url');
            if (url) window.location.href = url;
        });

        var recompute = function () { evaluate(button); };
        document.addEventListener('input', recompute);
        document.addEventListener('blur', recompute, true);
        evaluate(button);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
