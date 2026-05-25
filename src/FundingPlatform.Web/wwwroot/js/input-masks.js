// input-masks.js — Spec 026 (generalises the Spec 021 hand-rolled masks).
//
// Data-driven mask REGISTRY, event-delegated on `document` so masks also cover
// AJAX-injected nodes (supplier lookup partials) with zero re-init. Adding a
// structured field = one MASKS[name] entry + a `data-mask="name"` attribute
// (and, if type-switchable, a `data-mask-for` option). See
// specs/026-input-masks/contracts/mask-registry.md.
//
//   <input data-mask="cedula">                         — static mask
//   <select data-mask-controller="g1">                 — type selector …
//     <option data-mask-for="cedula">Cédula física</option> …
//   <input data-mask-group="g1" data-mask="cedula">    — … rebinds this input's mask
//
// Vanilla JS, no managed deps. Mirrors the event-delegation pattern of
// location-cascade.js.

(function () {
    'use strict';

    // ---- format helpers -------------------------------------------------

    function onlyDigits(raw) {
        return (raw || '').replace(/\D/g, '');
    }

    // Strip to digits, regroup into the given group sizes joined by '-',
    // capping at the total digit count. Partial input groups as far as typed.
    function digitGroups(sizes) {
        var total = sizes.reduce(function (a, b) { return a + b; }, 0);
        return function (raw) {
            var d = onlyDigits(raw).slice(0, total);
            var parts = [];
            var offset = 0;
            for (var i = 0; i < sizes.length && offset < d.length; i++) {
                parts.push(d.slice(offset, offset + sizes[i]));
                offset += sizes[i];
            }
            return parts.join('-');
        };
    }

    function digitsOnly(max) {
        return function (raw) { return onlyDigits(raw).slice(0, max); };
    }

    function upperAlnum(max) {
        return function (raw) {
            return (raw || '').replace(/[^A-Za-z0-9]/g, '').toUpperCase().slice(0, max);
        };
    }

    var EMAIL_RE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

    // ---- registry -------------------------------------------------------

    var MASKS = {
        'email': {
            mode: 'soft',
            maxLength: 256,
            format: null, // no reshaping; blur-validated only
            validate: function (v) { return EMAIL_RE.test(v); },
            message: 'Ingrese un correo electrónico válido.'
        },
        'phone-cr': {
            mode: 'strict',
            maxLength: 9,
            format: digitGroups([4, 4]),
            validate: function (v) { return /^\d{4}-\d{4}$/.test(v); }
        },
        'cedula': {
            mode: 'strict',
            maxLength: 11,
            format: digitGroups([1, 4, 4]),
            validate: function (v) { return /^\d-\d{4}-\d{4}$/.test(v); }
        },
        'cedula-jur': {
            mode: 'strict',
            maxLength: 12,
            format: digitGroups([1, 3, 6]),
            validate: function (v) { return /^\d-\d{3}-\d{6}$/.test(v); }
        },
        'dimex': {
            mode: 'strict',
            maxLength: 12,
            format: digitsOnly(12),
            validate: function (v) { return /^\d{11,12}$/.test(v); }
        },
        'nite': {
            mode: 'strict',
            maxLength: 12,
            format: digitGroups([1, 3, 6]),
            validate: function (v) { return /^\d-\d{3}-\d{6}$/.test(v); }
        },
        'pasaporte': {
            mode: 'soft',
            maxLength: 20,
            format: upperAlnum(20),
            validate: function (v) { return /^[A-Z0-9]{1,20}$/.test(v); },
            message: 'La identificación no tiene el formato de Pasaporte.'
        }
    };

    // Backward-compat alias for the Spec 021 attribute value.
    MASKS['rfc'] = MASKS['email'];

    function maskFor(input) {
        var name = input.getAttribute('data-mask');
        return name ? MASKS[name] : null;
    }

    // ---- validity feedback (Tabler/bootstrap classes) -------------------

    function setValidity(input, isValid, message) {
        input.classList.toggle('is-invalid', !isValid);
        if (isValid) {
            input.removeAttribute('aria-invalid');
        } else {
            input.setAttribute('aria-invalid', 'true');
        }
        var parent = input.parentElement;
        if (!parent) { return; }
        var feedback = parent.querySelector('.invalid-feedback.fl-mask-feedback');
        if (!isValid) {
            if (!feedback) {
                feedback = document.createElement('div');
                feedback.className = 'invalid-feedback fl-mask-feedback';
                parent.appendChild(feedback);
            }
            feedback.textContent = message || 'Valor no válido.';
        } else if (feedback) {
            feedback.remove();
        }
    }

    // ---- apply format / validate ---------------------------------------

    function applyFormat(input) {
        var mask = maskFor(input);
        if (!mask) { return; }
        if (mask.maxLength) {
            input.setAttribute('maxlength', String(mask.maxLength));
        }
        if (typeof mask.format === 'function') {
            var formatted = mask.format(input.value);
            if (formatted !== input.value) {
                input.value = formatted;
            }
        }
    }

    function runBlurValidation(input) {
        var mask = maskFor(input);
        if (!mask) { return; }
        var value = (input.value || '').trim();
        if (value.length === 0) {
            // Empty defers to any Required validator.
            setValidity(input, true);
            return;
        }
        // Soft masks own their inline feedback; strict masks are formatted as
        // typed and validated authoritatively on the server.
        if (mask.mode === 'soft' && typeof mask.validate === 'function') {
            setValidity(input, mask.validate(value), mask.message);
        }
    }

    // ---- type-selector controller --------------------------------------

    function groupedInput(controller) {
        var groupId = controller.getAttribute('data-mask-controller');
        if (!groupId) { return null; }
        return document.querySelector('[data-mask-group="' + groupId + '"]');
    }

    function selectedMaskName(controller) {
        var opt = controller.options[controller.selectedIndex];
        return opt ? opt.getAttribute('data-mask-for') : null;
    }

    function bindControllerMask(controller, reformat) {
        var input = groupedInput(controller);
        if (!input) { return; }
        var maskName = selectedMaskName(controller);
        if (!maskName) { return; }
        input.setAttribute('data-mask', maskName);
        // Reformat the current value through the newly-selected mask; digits that
        // fit are preserved, the incompatible remainder is dropped by the format
        // (and flagged server-side). Then re-validate.
        if (reformat) {
            applyFormat(input);
            runBlurValidation(input);
        } else {
            // initial bind: just format any server-rendered value once
            applyFormat(input);
        }
    }

    // ---- delegated events ----------------------------------------------

    document.addEventListener('input', function (event) {
        var el = event.target;
        if (el && typeof el.matches === 'function' && el.matches('[data-mask]')) {
            applyFormat(el);
        }
    });

    document.addEventListener('focusout', function (event) {
        var el = event.target;
        if (el && typeof el.matches === 'function' && el.matches('[data-mask]')) {
            runBlurValidation(el);
        }
    });

    document.addEventListener('change', function (event) {
        var el = event.target;
        if (el && typeof el.matches === 'function' && el.matches('select[data-mask-controller]')) {
            bindControllerMask(el, true);
        }
    });

    // ---- one-time scan of a (sub)tree -----------------------------------

    function scan(root) {
        if (!root || typeof root.querySelectorAll !== 'function') { return; }
        // Controllers first so the grouped input's mask is correct before format.
        var controllers = root.querySelectorAll('select[data-mask-controller]');
        for (var i = 0; i < controllers.length; i++) {
            bindControllerMask(controllers[i], false);
        }
        // Then any free-standing masked inputs (those not driven by a controller
        // were skipped above; format them and any server-rendered value once).
        var inputs = root.querySelectorAll('[data-mask]');
        for (var j = 0; j < inputs.length; j++) {
            applyFormat(inputs[j]);
        }
    }

    function init() {
        scan(document);
        // Cover server-rendered + AJAX-injected nodes added after load.
        if (typeof MutationObserver === 'function') {
            var observer = new MutationObserver(function (mutations) {
                for (var i = 0; i < mutations.length; i++) {
                    var added = mutations[i].addedNodes;
                    for (var j = 0; j < added.length; j++) {
                        var node = added[j];
                        if (node.nodeType === 1) { // element
                            scan(node);
                        }
                    }
                }
            });
            observer.observe(document.body, { childList: true, subtree: true });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Expose the registry for future extension / tests.
    window.FLInputMasks = { MASKS: MASKS, scan: scan };
})();
