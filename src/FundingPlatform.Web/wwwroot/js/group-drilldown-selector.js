// Spec 029 — Fondo → Proceso → Grupo drill-down group selector for the admin
// Create/Edit user forms (replaces the flat spec-016 multi-select). The Fund and
// Process <select>s are cascading filters; the Group level is a checkbox list
// scoped to the chosen Process. Checked groups accumulate as removable chips,
// preserved even when the Fund/Process filter changes — so a user can hold groups
// across several processes/funds (multi-group membership, spec 016 preserved).
//
// The canonical posted state is a set of hidden inputs (one per selected group id)
// named per the widget's data-field-name (default "GroupIds"). The server contract
// is unchanged: it still receives GroupIds[].
//
// Wiring contract (rendered by Views/Admin/Users/_GroupSelectorDrilldown.cshtml):
//   - root: [data-testid="group-drilldown-selector"] with
//       data-catalog='[{"id":1,"name":"Fondo","processes":[{"id":2,"name":"Proc","groups":[{"id":3,"name":"G"}]}]}]'
//       data-selected='[{"id":3,"name":"G"}]'   (initial selection, may include archived-fund groups)
//       data-field-name, data-placeholder-groups, data-empty-label, data-remove-label
//   - [data-role="fund"] / [data-role="process"]  cascading selects
//   - [data-role="options"]  group checkbox container
//   - [data-role="chips"]    selected-group chips container
//   - [data-role="hidden"]   hidden-input sink (posted GroupIds)

(function () {
    'use strict';

    // Spec 031 — accent/case fold for the group-options filter (mirrors the
    // searchable-select.js matcher). U+0300–U+036F built from char codes so the
    // source stays pure-ASCII.
    var COMBINING_MARKS = new RegExp('[' + String.fromCharCode(0x300) + '-' + String.fromCharCode(0x36f) + ']', 'g');
    function fold(text) {
        return (text || '')
            .normalize('NFD')
            .replace(COMBINING_MARKS, '')
            .toLocaleLowerCase('es');
    }
    function searchPlaceholder() {
        return (document.body && document.body.getAttribute('data-searchable-placeholder')) || '';
    }

    function init(root) {
        var catalog, selected;
        try { catalog = JSON.parse(root.getAttribute('data-catalog') || '[]'); } catch (e) { catalog = []; }
        try { selected = JSON.parse(root.getAttribute('data-selected') || '[]'); } catch (e) { selected = []; }

        var fieldName = root.getAttribute('data-field-name') || 'GroupIds';
        var placeholderGroups = root.getAttribute('data-placeholder-groups') || '';
        var emptyLabel = root.getAttribute('data-empty-label') || '';
        var removeLabel = root.getAttribute('data-remove-label') || '';

        var fundSel = root.querySelector('[data-role="fund"]');
        var procSel = root.querySelector('[data-role="process"]');
        var optionsBox = root.querySelector('[data-role="options"]');
        var chipsBox = root.querySelector('[data-role="chips"]');
        var hiddenBox = root.querySelector('[data-role="hidden"]');
        if (!fundSel || !procSel || !optionsBox || !chipsBox || !hiddenBox) return;

        // Spec 031 — in-place text filter over the group checkbox list (FR-007).
        // Hidden until a process with groups is shown; never touches checked state.
        var filterInput = document.createElement('input');
        filterInput.type = 'text';
        filterInput.className = 'form-control form-control-sm mb-2';
        filterInput.placeholder = searchPlaceholder();
        filterInput.setAttribute('data-role', 'group-filter');
        filterInput.setAttribute('data-testid', 'group-selector-filter');
        filterInput.setAttribute('aria-label', searchPlaceholder());
        filterInput.hidden = true;
        optionsBox.parentNode.insertBefore(filterInput, optionsBox);
        filterInput.addEventListener('input', function () { applyGroupFilter(filterInput.value); });

        function applyGroupFilter(query) {
            var folded = fold(query);
            var labels = optionsBox.querySelectorAll('label.form-check');
            for (var i = 0; i < labels.length; i++) {
                var hay = labels[i].getAttribute('data-filter-text') || '';
                labels[i].hidden = folded.length > 0 && hay.indexOf(folded) === -1;
            }
        }

        // Ordered map id -> name (string keys).
        var selectedMap = new Map();
        selected.forEach(function (g) { selectedMap.set(String(g.id), g.name); });

        function findFund(id) {
            return catalog.find(function (f) { return String(f.id) === String(id); });
        }
        function findProcess(fund, id) {
            return fund && (fund.processes || []).find(function (p) { return String(p.id) === String(id); });
        }

        function buildFundOptions() {
            catalog.forEach(function (f) {
                var o = document.createElement('option');
                o.value = String(f.id);
                o.textContent = f.name;
                fundSel.appendChild(o);
            });
        }

        function buildProcessOptions() {
            while (procSel.options.length > 1) { procSel.remove(1); }
            var fund = findFund(fundSel.value);
            if (!fund) { procSel.disabled = true; procSel.selectedIndex = 0; return; }
            (fund.processes || []).forEach(function (p) {
                var o = document.createElement('option');
                o.value = String(p.id);
                o.textContent = p.name;
                procSel.appendChild(o);
            });
            procSel.disabled = false;
            procSel.selectedIndex = 0;
        }

        function buildGroupOptions() {
            optionsBox.innerHTML = '';
            // Reset the filter each rebuild so a fresh process starts unfiltered.
            filterInput.value = '';
            filterInput.hidden = true;
            var fund = findFund(fundSel.value);
            var proc = findProcess(fund, procSel.value);
            if (!proc) {
                appendPlaceholder(placeholderGroups);
                return;
            }
            var groups = proc.groups || [];
            if (groups.length === 0) {
                appendPlaceholder(placeholderGroups);
                return;
            }
            filterInput.hidden = false;
            groups.forEach(function (g) {
                var id = String(g.id);
                var label = document.createElement('label');
                label.className = 'form-check';
                label.setAttribute('data-filter-text', fold(g.name));

                var cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.className = 'form-check-input';
                cb.value = id;
                cb.checked = selectedMap.has(id);
                cb.setAttribute('data-role', 'group-option');
                cb.setAttribute('data-testid', 'group-option-' + id);
                cb.addEventListener('change', function () {
                    if (cb.checked) { selectedMap.set(id, g.name); }
                    else { selectedMap.delete(id); }
                    renderChips();
                    renderHidden();
                });

                var span = document.createElement('span');
                span.className = 'form-check-label';
                span.textContent = g.name;

                label.appendChild(cb);
                label.appendChild(span);
                optionsBox.appendChild(label);
            });
        }

        function appendPlaceholder(text) {
            var ph = document.createElement('div');
            ph.className = 'text-muted small';
            ph.textContent = text;
            optionsBox.appendChild(ph);
        }

        function renderChips() {
            chipsBox.innerHTML = '';
            if (selectedMap.size === 0) {
                var empty = document.createElement('span');
                empty.className = 'text-muted small';
                empty.textContent = emptyLabel;
                chipsBox.appendChild(empty);
                return;
            }
            selectedMap.forEach(function (name, id) {
                var chip = document.createElement('span');
                chip.className = 'badge bg-blue-lt text-blue d-inline-flex align-items-center gap-1';
                chip.setAttribute('data-testid', 'group-chip-' + id);
                chip.setAttribute('data-group-name', name);

                var text = document.createElement('span');
                text.textContent = name;

                var btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'btn-close';
                btn.style.fontSize = '0.6rem';
                btn.setAttribute('aria-label', removeLabel);
                btn.setAttribute('data-remove', id);
                btn.addEventListener('click', function () {
                    selectedMap.delete(String(id));
                    var box = optionsBox.querySelector('[data-testid="group-option-' + id + '"]');
                    if (box) { box.checked = false; }
                    renderChips();
                    renderHidden();
                });

                chip.appendChild(text);
                chip.appendChild(btn);
                chipsBox.appendChild(chip);
            });
        }

        function renderHidden() {
            hiddenBox.innerHTML = '';
            selectedMap.forEach(function (name, id) {
                var input = document.createElement('input');
                input.type = 'hidden';
                input.name = fieldName;
                input.value = id;
                hiddenBox.appendChild(input);
            });
        }

        fundSel.addEventListener('change', function () {
            buildProcessOptions();
            buildGroupOptions();
        });
        procSel.addEventListener('change', buildGroupOptions);

        buildFundOptions();
        buildProcessOptions();
        buildGroupOptions();
        renderChips();
        renderHidden();

        // Signal init complete so tests (and any observer) can wait for the
        // chips/hidden inputs to be materialized from data-selected rather than
        // racing the initial static markup.
        root.setAttribute('data-ready', 'true');
    }

    function boot() {
        var roots = document.querySelectorAll('[data-testid="group-drilldown-selector"]');
        Array.prototype.forEach.call(roots, init);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
