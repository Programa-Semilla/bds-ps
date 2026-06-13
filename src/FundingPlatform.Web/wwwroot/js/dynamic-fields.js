// Spec 035 / US2 / T042 — shared client-side renderer for dynamic field forms.
// Drives BOTH the per-item category fields and the per-item impact parameters
// from a small JSON descriptor, replacing the duplicated DataType->control switch
// that previously lived in Impact.cshtml. ES5 house style, no build step.
//
// Descriptor shape (from the JSON endpoints):
//   { id, name, displayLabel, dataType, isRequired, value? }
// DataType maps to ParameterDataType: Text=0, Decimal=1, Integer=2, Date=3.
(function () {
    'use strict';

    function escapeHtml(s) {
        if (s === null || s === undefined) { return ''; }
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function controlAttributes(dataType) {
        switch (dataType) {
            case 1: return { type: 'number', step: '0.01' }; // Decimal
            case 2: return { type: 'number', step: '1' };    // Integer
            case 3: return { type: 'date', step: null };     // Date
            default: return { type: 'text', step: null };    // Text
        }
    }

    function fieldBlock(field, namePrefix) {
        var name = namePrefix + '[' + field.id + ']';
        var attrs = controlAttributes(field.dataType);
        var required = field.isRequired ? ' required' : '';
        var requiredMark = field.isRequired ? ' <span class="text-danger">*</span>' : '';
        var stepAttr = attrs.step ? ' step="' + attrs.step + '"' : '';
        var value = field.value != null ? ' value="' + escapeHtml(field.value) + '"' : '';
        return '' +
            '<div class="mb-3">' +
                '<label class="form-label">' + escapeHtml(field.displayLabel) + requiredMark + '</label>' +
                '<input type="' + attrs.type + '"' + stepAttr +
                    ' name="' + escapeHtml(name) + '"' + value +
                    ' class="form-control" data-dynamic-field="1"' + required + ' />' +
            '</div>';
    }

    // Render a list of descriptors into a container with the given posted-name prefix.
    function render(container, fields, namePrefix) {
        if (!container) { return; }
        if (!fields || !fields.length) {
            container.innerHTML = '';
            return;
        }
        container.innerHTML = fields.map(function (f) { return fieldBlock(f, namePrefix); }).join('');
    }

    // Fetch descriptors from a URL and render them. Returns the fetch promise.
    function load(container, url, namePrefix) {
        if (!container) { return Promise.resolve(); }
        return fetch(url, { headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (fields) { render(container, fields, namePrefix); })
            .catch(function () { render(container, [], namePrefix); });
    }

    window.DynamicFields = { render: render, load: load };
})();
