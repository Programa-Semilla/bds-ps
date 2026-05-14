// password-eye-toggle.js — Spec 021 / T061 / FR-026.
//
// On click of `.password-eye-toggle`, swaps the target input's `type` between
// `password` and `text`. Target selector lives on `data-target`. Toggles the
// Tabler eye / eye-off icon inside the button's child `[data-eye-icon]`.

(function () {
    'use strict';

    function onToggle(button) {
        var targetSel = button.getAttribute('data-target');
        if (!targetSel) return;
        var input = document.querySelector(targetSel);
        if (!input) return;

        var nextType = input.getAttribute('type') === 'password' ? 'text' : 'password';
        input.setAttribute('type', nextType);

        var icon = button.querySelector('[data-eye-icon]');
        if (icon) {
            icon.classList.toggle('ti-eye', nextType === 'password');
            icon.classList.toggle('ti-eye-off', nextType === 'text');
        }
    }

    function init() {
        var buttons = document.querySelectorAll('.password-eye-toggle');
        for (var i = 0; i < buttons.length; i++) {
            (function (btn) {
                btn.addEventListener('click', function (e) {
                    e.preventDefault();
                    onToggle(btn);
                });
            })(buttons[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
