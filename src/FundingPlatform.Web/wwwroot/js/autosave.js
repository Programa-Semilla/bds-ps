// autosave.js — Spec 021 / T063 / R-5 / FR-016.
//
// Per-field blur-driven autosave for the applicant draft editor. Each
// `[data-autosave-field]` inside `[data-autosave-form="applications"]` POSTs
// `{ fieldKey, value, etag }` to `/api/applications/{publicCode}/autosave`.
//
// Response handling (R-5):
//   - 200       → saved   state, updates `<time data-saved-at>` + new etag
//   - 409       → conflict, reloads the page so the applicant sees fresh state
//   - 422       → failed  state, retry button rebinds to last attempted save
//   - network   → failed  state, retry button rebinds
//
// Debounced 300ms per the contract in applicant-routes.md.

(function () {
    'use strict';

    var DEBOUNCE_MS = 300;

    function findIndicator(field) {
        // Look in the field's row first, then walk up to the form.
        var scope = field.closest('[data-autosave-row]') || field.closest('[data-autosave-form]');
        return scope ? scope.querySelector('[data-autosave-indicator]') : null;
    }

    function setState(indicator, state, savedAt) {
        if (!indicator) return;
        indicator.setAttribute('data-autosave-state', state);
        var nodes = indicator.querySelectorAll('[data-state-for]');
        for (var i = 0; i < nodes.length; i++) {
            var match = nodes[i].getAttribute('data-state-for') === state;
            nodes[i].classList.toggle('d-none', !match);
        }
        if (state === 'saved' && savedAt) {
            var time = indicator.querySelector('time[data-saved-at]');
            if (time) {
                var d = new Date(savedAt);
                if (!isNaN(d.getTime())) {
                    time.setAttribute('datetime', d.toISOString());
                    time.textContent = d.toLocaleTimeString('es-CR', { hour: '2-digit', minute: '2-digit' });
                }
            }
        }
    }

    function postSave(form, field) {
        var publicCode = form.getAttribute('data-public-code');
        if (!publicCode) return;
        var url = '/api/applications/' + encodeURIComponent(publicCode) + '/autosave';
        var fieldKey = field.getAttribute('data-autosave-field');
        var etagInput = form.querySelector('input[name="__autosaveEtag"]');
        var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
        var indicator = findIndicator(field);

        var payload = {
            fieldKey: fieldKey,
            value: field.value,
            etag: etagInput ? etagInput.value : null
        };
        var headers = {
            'Content-Type': 'application/json',
            'Accept': 'application/json'
        };
        if (tokenInput && tokenInput.value) {
            headers['RequestVerificationToken'] = tokenInput.value;
        }

        setState(indicator, 'saving');

        fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: headers,
            body: JSON.stringify(payload)
        })
            .then(function (response) {
                if (response.status === 409) {
                    // Stale etag — refresh so the user can re-edit against
                    // the authoritative server-side state.
                    window.location.reload();
                    return null;
                }
                if (response.status === 422) {
                    setState(indicator, 'failed');
                    return null;
                }
                if (!response.ok) {
                    setState(indicator, 'failed');
                    return null;
                }
                return response.json();
            })
            .then(function (body) {
                if (!body) return;
                if (etagInput && body.etag) {
                    etagInput.value = body.etag;
                }
                setState(indicator, 'saved', body.savedAt || new Date().toISOString());
            })
            .catch(function () {
                setState(indicator, 'failed');
            });
    }

    function wireField(form, field) {
        var timer = null;
        function schedule() {
            if (timer) clearTimeout(timer);
            timer = setTimeout(function () { postSave(form, field); }, DEBOUNCE_MS);
        }
        field.addEventListener('blur', schedule);

        var indicator = findIndicator(field);
        if (indicator) {
            var retry = indicator.querySelector('[data-autosave-retry]');
            if (retry) {
                retry.addEventListener('click', function (e) {
                    e.preventDefault();
                    postSave(form, field);
                });
            }
        }
    }

    function init() {
        var forms = document.querySelectorAll('[data-autosave-form="applications"]');
        for (var i = 0; i < forms.length; i++) {
            var form = forms[i];
            var fields = form.querySelectorAll('[data-autosave-field]');
            for (var j = 0; j < fields.length; j++) {
                wireField(form, fields[j]);
            }
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
