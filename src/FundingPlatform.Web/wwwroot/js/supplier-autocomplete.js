// supplier-autocomplete.js — Spec 021 / T065 / FR-009.
//
// Vanilla autocomplete on `input[data-supplier-autocomplete]`.
//   - Debounce 200ms after `input` events
//   - GET `/api/suppliers/search?q={term}` (the endpoint lands in US3; until
//     then it 404s and we render an empty list silently)
//   - Render up to 25 results in a `<ul>` floated below the input
//   - Click selects: writes the supplier id into the input's `data-supplier-id`,
//     fills the input value with the supplier's display label, fires `change`.
//
// Markup contract (host pages):
//   <input data-supplier-autocomplete data-supplier-id-target="#hiddenId" />
//   <ul   data-supplier-autocomplete-results></ul>   (optional — auto-created if absent)
//
// API contract (US3): response = [{ id, name, cedulaJuridica }] capped at 25.

(function () {
    'use strict';

    var DEBOUNCE_MS = 200;
    var MAX_RESULTS = 25;

    function getOrCreateList(input) {
        var explicit = input.parentElement && input.parentElement.querySelector('[data-supplier-autocomplete-results]');
        if (explicit) return explicit;
        var list = document.createElement('ul');
        list.className = 'list-group fl-supplier-autocomplete-results';
        list.setAttribute('data-supplier-autocomplete-results', '');
        list.hidden = true;
        if (input.parentElement) {
            input.parentElement.appendChild(list);
        }
        return list;
    }

    function clearList(list) {
        list.innerHTML = '';
        list.hidden = true;
    }

    function renderResults(input, list, results) {
        list.innerHTML = '';
        if (!results || results.length === 0) {
            list.hidden = true;
            return;
        }
        var cap = Math.min(results.length, MAX_RESULTS);
        for (var i = 0; i < cap; i++) {
            (function (item) {
                var li = document.createElement('li');
                li.className = 'list-group-item list-group-item-action fl-supplier-autocomplete-item';
                li.setAttribute('data-supplier-id', String(item.id));
                var label = item.name || '';
                if (item.cedulaJuridica) {
                    label += '  ·  ' + item.cedulaJuridica;
                }
                li.textContent = label;
                li.addEventListener('mousedown', function (e) {
                    // mousedown beats blur — keeps the click from being eaten
                    // by the input's blur handler.
                    e.preventDefault();
                    selectItem(input, item);
                    clearList(list);
                });
                list.appendChild(li);
            })(results[i]);
        }
        list.hidden = false;
    }

    function selectItem(input, item) {
        var idTargetSel = input.getAttribute('data-supplier-id-target');
        if (idTargetSel) {
            var target = document.querySelector(idTargetSel);
            if (target) {
                target.value = String(item.id);
                target.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }
        var label = item.name || '';
        if (item.cedulaJuridica) {
            label += '  ·  ' + item.cedulaJuridica;
        }
        input.value = label;
        input.setAttribute('data-supplier-id', String(item.id));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function fetchResults(term) {
        return fetch('/api/suppliers/search?q=' + encodeURIComponent(term), {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        })
            .then(function (response) {
                if (!response.ok) return [];
                return response.json();
            })
            .then(function (body) {
                return Array.isArray(body) ? body : [];
            })
            .catch(function () { return []; });
    }

    function wire(input) {
        var list = getOrCreateList(input);
        var timer = null;

        function schedule() {
            if (timer) clearTimeout(timer);
            var term = (input.value || '').trim();
            if (term.length < 2) {
                clearList(list);
                return;
            }
            timer = setTimeout(function () {
                fetchResults(term).then(function (results) {
                    renderResults(input, list, results);
                });
            }, DEBOUNCE_MS);
        }
        input.addEventListener('input', schedule);
        input.addEventListener('blur', function () {
            // Slight delay so list mousedown can register before we tear down.
            setTimeout(function () { clearList(list); }, 150);
        });
    }

    function init() {
        var inputs = document.querySelectorAll('input[data-supplier-autocomplete]');
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
