// input-masks.js — Spec 021 / T059 / FR-013.
//
// Two masks, applied on DOMContentLoaded to all matching inputs:
//   - CR phone (`data-mask="phone-cr"`): enforces `8888-8888` shape — digits
//     only, auto-insert hyphen after the 4th digit, max length 9 incl. hyphen.
//   - RFC email (`data-mask="email"` / `data-mask="rfc"`): on blur validates
//     against a lax RFC-5322 regex and surfaces an inline `.invalid-feedback`
//     Tabler error sibling.
//
// Vanilla JS. No managed deps. Honors existing site.js IIFE pattern.

(function () {
    'use strict';

    var EMAIL_RE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

    function applyPhoneMask(input) {
        function format(raw) {
            var digits = (raw || '').replace(/\D/g, '').slice(0, 8);
            if (digits.length <= 4) return digits;
            return digits.slice(0, 4) + '-' + digits.slice(4);
        }
        input.addEventListener('input', function () {
            var formatted = format(input.value);
            if (formatted !== input.value) {
                input.value = formatted;
            }
        });
        // Format any server-rendered value once on init.
        if (input.value) {
            input.value = format(input.value);
        }
        // Belt-and-braces: cap maxlength so paste of long strings still ends
        // up bounded at "8888-8888" (9 chars incl. hyphen).
        input.setAttribute('maxlength', '9');
    }

    function setEmailValidity(input, isValid) {
        // Tabler bootstrap-style validation classes.
        input.classList.toggle('is-invalid', !isValid);
        // Look for an adjacent .invalid-feedback element; create one if absent.
        var feedback = input.parentElement && input.parentElement.querySelector('.invalid-feedback.fl-email-mask-feedback');
        if (!isValid) {
            if (!feedback && input.parentElement) {
                feedback = document.createElement('div');
                feedback.className = 'invalid-feedback fl-email-mask-feedback';
                feedback.textContent = 'Ingrese un correo electrónico válido.';
                input.parentElement.appendChild(feedback);
            }
        } else if (feedback) {
            feedback.remove();
        }
    }

    function applyEmailMask(input) {
        input.addEventListener('blur', function () {
            var value = (input.value || '').trim();
            if (value.length === 0) {
                // Empty: let Required validators (if any) own the message.
                setEmailValidity(input, true);
                return;
            }
            setEmailValidity(input, EMAIL_RE.test(value));
        });
    }

    function init() {
        var phones = document.querySelectorAll('input[data-mask="phone-cr"]');
        for (var i = 0; i < phones.length; i++) {
            applyPhoneMask(phones[i]);
        }
        var emails = document.querySelectorAll('input[data-mask="email"], input[data-mask="rfc"]');
        for (var j = 0; j < emails.length; j++) {
            applyEmailMask(emails[j]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
