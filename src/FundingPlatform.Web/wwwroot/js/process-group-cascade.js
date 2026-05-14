// Spec 021 / FR-034 / T082 / T083 — admin users list cascading filter.
// When the user picks a Process in #admin-users-process-filter, narrow the
// adjacent Group dropdown (#admin-users-group-filter) to that Process's
// groups. The page emits the full Process → Groups catalog as a JSON blob
// in the data-process-catalog attribute so we can do this entirely client-side
// (no /api call needed — the catalog is tiny).
//
// Wiring contract:
//   - The container element carries `data-testid="admin-users-process-group-cascade"`
//     and `data-process-catalog='[{"id":1,"name":"Crocus 2025","groups":[{"id":7,"name":"Norte"}]}]'`.
//   - Process <select> id = `admin-users-process-filter`.
//   - Group   <select> id = `admin-users-group-filter`.

(function () {
    'use strict';

    function init() {
        var container = document.querySelector('[data-testid="admin-users-process-group-cascade"]');
        if (!container) return;

        var processSelect = document.getElementById('admin-users-process-filter');
        var groupSelect = document.getElementById('admin-users-group-filter');
        if (!processSelect || !groupSelect) return;

        var catalog;
        try {
            catalog = JSON.parse(container.getAttribute('data-process-catalog') || '[]');
        } catch (e) {
            catalog = [];
        }

        var initialGroupValue = groupSelect.getAttribute('data-current-value') || '';

        function rebuildGroups() {
            var processId = processSelect.value;
            // Clear all options except the "all" placeholder.
            while (groupSelect.options.length > 1) {
                groupSelect.remove(1);
            }

            if (!processId) {
                // No process picked — show every group across every process.
                catalog.forEach(function (proc) {
                    proc.groups.forEach(function (g) {
                        addOption(g.id, g.name);
                    });
                });
            } else {
                var proc = catalog.find(function (p) { return String(p.id) === String(processId); });
                if (proc) {
                    proc.groups.forEach(function (g) {
                        addOption(g.id, g.name);
                    });
                }
            }

            // Restore prior selection if it's still in the new option set.
            if (initialGroupValue) {
                for (var i = 0; i < groupSelect.options.length; i++) {
                    if (groupSelect.options[i].value === initialGroupValue) {
                        groupSelect.selectedIndex = i;
                        break;
                    }
                }
            }
        }

        function addOption(value, label) {
            var opt = document.createElement('option');
            opt.value = String(value);
            opt.textContent = label;
            groupSelect.appendChild(opt);
        }

        processSelect.addEventListener('change', function () {
            // After a manual process change, clear any pre-selected group hint
            // so the next rebuild does not snap back.
            initialGroupValue = '';
            rebuildGroups();
        });

        // Initial paint: align the Group dropdown with the pre-selected Process.
        rebuildGroups();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
