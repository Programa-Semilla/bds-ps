// Spec 035 (evolved 2026-06-16) — reusable client-side list filter for admin tables.
// Filters rows by a free-text search (over name + description) and a status select.
// ES5 house style, no build step. Accent/case-insensitive matching (es-CR).
//
// Markup contract (see Views/Admin/Categories.cshtml):
//   container:  [data-list-filter]
//   search:     [data-list-filter-search]            (text input)
//   status:     [data-list-filter-status]            (select; value '' = all)
//   rows:       tbody tr[data-filter-row] with
//                 data-filter-text   (lowercased name + description)
//                 data-filter-status ('active' | 'inactive')
//   empty note: [data-list-filter-empty]             (shown when 0 rows match)
(function () {
    'use strict';

    var COMBINING_MARKS = new RegExp('[\\u0300-\\u036f]', 'g');

    function normalize(s) {
        if (s === null || s === undefined) { return ''; }
        // Strip diacritics so "garantia" matches "garantía".
        return String(s).normalize('NFD').replace(COMBINING_MARKS, '').toLowerCase();
    }

    function wire(container) {
        var search = container.querySelector('[data-list-filter-search]');
        var status = container.querySelector('[data-list-filter-status]');
        var empty = container.querySelector('[data-list-filter-empty]');
        var rows = container.querySelectorAll('tbody tr[data-filter-row]');

        function apply() {
            var term = search ? normalize(search.value.trim()) : '';
            var wantStatus = status ? status.value : '';
            var visible = 0;

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var text = normalize(row.getAttribute('data-filter-text'));
                var rowStatus = row.getAttribute('data-filter-status') || '';
                var matchesText = term === '' || text.indexOf(term) !== -1;
                var matchesStatus = wantStatus === '' || rowStatus === wantStatus;
                var show = matchesText && matchesStatus;
                row.hidden = !show;
                if (show) { visible++; }
            }

            if (empty) { empty.hidden = visible !== 0; }
        }

        if (search) { search.addEventListener('input', apply); }
        if (status) { status.addEventListener('change', apply); }
        apply();
    }

    document.addEventListener('DOMContentLoaded', function () {
        var containers = document.querySelectorAll('[data-list-filter]');
        for (var i = 0; i < containers.length; i++) {
            wire(containers[i]);
        }
    });
})();
