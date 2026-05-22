// password-strength-legend.js — Spec 021 / T062 / FR-027.
//
// On `input` of every password input carrying `data-strength-legend` (value =
// CSS selector of the legend element), evaluates four rules and toggles
// `.ok` on the matching `<li data-rule="...">` items inside the legend.
//
// Rules:
//   - min8:    length >= 8
//   - upper:   contains an uppercase letter
//   - digit:   contains a digit
//   - special: contains a non-alphanumeric character

(function () {
    'use strict';

    function evaluate(value) {
        return {
            min8:    (value || '').length >= 8,
            upper:   /[A-ZÁÉÍÓÚÑ]/.test(value || ''),
            digit:   /\d/.test(value || ''),
            special: /[^A-Za-z0-9ÁÉÍÓÚÑáéíóúñ]/.test(value || ''),
        };
    }

    function applyRules(legend, results) {
        var items = legend.querySelectorAll('li[data-rule]');
        for (var i = 0; i < items.length; i++) {
            var li = items[i];
            var rule = li.getAttribute('data-rule');
            var ok = !!results[rule];
            li.classList.toggle('ok', ok);
            li.classList.toggle('text-success', ok);
            var icon = li.querySelector('i.ti');
            if (icon) {
                icon.classList.toggle('ti-circle', !ok);
                icon.classList.toggle('ti-circle-check', ok);
            }
        }
    }

    function wire(input) {
        var legendSelector = input.getAttribute('data-strength-legend');
        if (!legendSelector) return;
        var legend = document.querySelector(legendSelector);
        if (!legend) return;
        function update() {
            applyRules(legend, evaluate(input.value));
        }
        input.addEventListener('input', update);
        // Initial evaluation in case the field has a pre-filled value.
        update();
    }

    function init() {
        var inputs = document.querySelectorAll('input[data-strength-legend]');
        for (var i = 0; i < inputs.length; i++) {
            wire(inputs[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
