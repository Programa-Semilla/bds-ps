// Shared Fondo → Proceso → Grupo cascading FILTER (single-select per level).
// Used by the admin filter toolbars (Users, Suppliers, Processes, Reports). Each
// level is an independent GET field whose empty value means "all". Picking a Fund
// narrows the Process options; picking a Process narrows the Group options.
// Server-selected values are restored on first paint.
//
// Wiring contract (rendered by Views/Shared/Components/_CascadingFundFilter.cshtml):
//   - root: .cascading-fund-filter with
//       data-catalog='[{"id":1,"name":"Fondo","processes":[{"id":2,"name":"Proc","groups":[{"id":3,"name":"G"}]}]}]'
//       data-depth = 1 (Fund) | 2 (Fund+Process) | 3 (Fund+Process+Group)
//   - one <select data-role="fund|process|group" data-selected="<id or empty>"> per level.

(function () {
    'use strict';

    function init(root) {
        var catalog;
        try { catalog = JSON.parse(root.getAttribute('data-catalog') || '[]'); } catch (e) { catalog = []; }
        var depth = parseInt(root.getAttribute('data-depth') || '3', 10);

        var fundSel = root.querySelector('[data-role="fund"]');
        var procSel = depth >= 2 ? root.querySelector('[data-role="process"]') : null;
        var groupSel = depth >= 3 ? root.querySelector('[data-role="group"]') : null;
        if (!fundSel) return;

        function addOption(sel, value, text) {
            var o = document.createElement('option');
            o.value = String(value);
            o.textContent = text;
            sel.appendChild(o);
        }
        function clearOptions(sel) { while (sel.options.length > 1) { sel.remove(1); } }
        function setValue(sel, value) {
            if (value === null || value === undefined || value === '') { sel.value = ''; return; }
            for (var i = 0; i < sel.options.length; i++) {
                if (sel.options[i].value === String(value)) { sel.selectedIndex = i; return; }
            }
            sel.value = '';
        }

        function processesForFund(fundId) {
            if (fundId) {
                var f = catalog.find(function (x) { return String(x.id) === String(fundId); });
                return f ? (f.processes || []) : [];
            }
            var all = [];
            catalog.forEach(function (f) { (f.processes || []).forEach(function (p) { all.push(p); }); });
            return all;
        }
        function findProcess(processId) {
            for (var i = 0; i < catalog.length; i++) {
                var p = (catalog[i].processes || []).find(function (x) { return String(x.id) === String(processId); });
                if (p) { return p; }
            }
            return null;
        }
        function groupsForSelection(fundId, processId) {
            if (processId) {
                var p = findProcess(processId);
                return p ? (p.groups || []) : [];
            }
            var all = [];
            processesForFund(fundId).forEach(function (p) { (p.groups || []).forEach(function (g) { all.push(g); }); });
            return all;
        }

        function rebuildProcess(keepValue) {
            if (!procSel) return;
            clearOptions(procSel);
            processesForFund(fundSel.value).forEach(function (p) { addOption(procSel, p.id, p.name); });
            setValue(procSel, keepValue);
        }
        function rebuildGroup(keepValue) {
            if (!groupSel) return;
            clearOptions(groupSel);
            groupsForSelection(fundSel.value, procSel ? procSel.value : '').forEach(function (g) {
                addOption(groupSel, g.id, g.name);
            });
            setValue(groupSel, keepValue);
        }

        // Build fund options + restore.
        catalog.forEach(function (f) { addOption(fundSel, f.id, f.name); });
        setValue(fundSel, fundSel.getAttribute('data-selected'));

        // Changing a level resets the levels below it to "all".
        fundSel.addEventListener('change', function () {
            rebuildProcess('');
            rebuildGroup('');
        });
        if (procSel) {
            procSel.addEventListener('change', function () { rebuildGroup(''); });
        }

        // Initial paint: build narrowed option sets and restore server selections.
        rebuildProcess(procSel ? procSel.getAttribute('data-selected') : '');
        rebuildGroup(groupSel ? groupSel.getAttribute('data-selected') : '');

        root.setAttribute('data-ready', 'true');
    }

    function boot() {
        var roots = document.querySelectorAll('.cascading-fund-filter');
        Array.prototype.forEach.call(roots, init);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
