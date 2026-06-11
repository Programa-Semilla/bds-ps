// searchable-select.js — Spec 031.
//
// Progressive-enhancement combobox over any native <select data-searchable>.
// The native <select> stays in the DOM and remains the authoritative, posted
// value; the enhancer renders an accessible WAI-ARIA combobox (text input +
// role=listbox) on top of it. Matching is accent/case-insensitive (es-CR).
//
// Markup contract (authored in views — see contracts/searchable-select.md):
//   <select ... class="form-select" data-testid="x" data-searchable
//           [data-searchable-threshold="N"] [data-searchable-placeholder="…"]>
//
// Behaviour:
//   - Enhances only when the count of *selectable* options (non-empty value)
//     exceeds the threshold (per-control data-searchable-threshold, else the
//     global default 7). Below-threshold controls stay plain native selects.
//   - Commit (Enter / click) sets select.value + dispatches a bubbling `change`,
//     so cascade scripts and form binding keep working unchanged.
//   - Typed text only filters; it never becomes a value (must-pick-from-list).
//     Blur reverts the input display to the committed value's label.
//   - A document-level MutationObserver enhances AJAX-injected controls; a
//     per-select childList observer refreshes the combobox (and re-evaluates the
//     threshold) when cascade logic rebuilds the <option>s.
//   - Spanish copy comes from markup (data-searchable-placeholder / a body-level
//     default + data-searchable-empty); this file contains no Spanish literals.

(function () {
    'use strict';

    var GLOBAL_DEFAULT_THRESHOLD = 7;
    var uid = 0;

    function nextId(prefix) {
        uid += 1;
        return prefix + '-' + uid;
    }

    // Accent/case fold for es-CR substring matching: "josé"/"JOSÉ" -> "jose".
    // The combining-diacritic range U+0300–U+036F is built from char codes so
    // the source stays pure-ASCII (no literal combining marks in the file).
    var COMBINING_MARKS = new RegExp('[' + String.fromCharCode(0x300) + '-' + String.fromCharCode(0x36f) + ']', 'g');
    function fold(text) {
        return (text || '')
            .normalize('NFD')
            .replace(COMBINING_MARKS, '')
            .toLocaleLowerCase('es');
    }

    function bodyData(name) {
        var v = document.body ? document.body.getAttribute(name) : null;
        return v != null ? v : '';
    }

    function parseThreshold(select) {
        var raw = select.getAttribute('data-searchable-threshold');
        var t = parseInt(raw, 10);
        return isNaN(t) ? GLOBAL_DEFAULT_THRESHOLD : t;
    }

    // ------------------------------------------------------------------ //
    // Controller — one per managed <select>.
    // ------------------------------------------------------------------ //
    function Controller(select) {
        this.select = select;
        this.enhanced = false;
        this.open = false;
        this.root = null;
        this.input = null;
        this.list = null;
        this.emptyEl = null;
        this.statusEl = null;
        this.options = [];      // { value, label, normalizedLabel }
        this.visible = [];      // option objects currently rendered
        this.activeIndex = -1;
        this.placeholder = select.getAttribute('data-searchable-placeholder') || bodyData('data-searchable-placeholder');
        this.emptyText = bodyData('data-searchable-empty');
        this.threshold = parseThreshold(select);
        this.listboxId = nextId('fl-searchable-list');
    }

    Controller.prototype.snapshot = function () {
        var opts = [];
        for (var i = 0; i < this.select.options.length; i++) {
            var o = this.select.options[i];
            var label = (o.textContent || '').trim();
            opts.push({ value: o.value, label: label, normalizedLabel: fold(label) });
        }
        this.options = opts;
    };

    Controller.prototype.selectableCount = function () {
        var n = 0;
        for (var i = 0; i < this.options.length; i++) {
            if (this.options[i].value !== '') { n += 1; }
        }
        return n;
    };

    Controller.prototype.committedLabel = function () {
        var v = this.select.value;
        for (var i = 0; i < this.options.length; i++) {
            if (this.options[i].value === v) { return this.options[i].label; }
        }
        return '';
    };

    // Label to show in the input: empty for the empty/"all" option so the
    // placeholder ("Escriba para filtrar…") shows instead of the prompt text.
    Controller.prototype.displayLabel = function () {
        return this.select.value === '' ? '' : this.committedLabel();
    };

    // Decide enhance / refresh / revert based on the current option count.
    Controller.prototype.evaluate = function () {
        this.snapshot();
        var aboveThreshold = this.selectableCount() > this.threshold;
        if (aboveThreshold && !this.enhanced) {
            this.enhance();
        } else if (!aboveThreshold && this.enhanced) {
            this.unenhance();
        } else if (this.enhanced) {
            this.refresh();
        }
    };

    // Build the combobox DOM/ARIA structure around the native select.
    Controller.prototype.enhance = function () {
        var self = this;
        var select = this.select;

        var root = document.createElement('div');
        root.className = 'fl-searchable';
        root.setAttribute('data-searchable-root', '');

        // Insert the combobox wrapper immediately AFTER the native select WITHOUT
        // moving the select. Moving it into the wrapper would detach it from the DOM
        // mid-action, making native-driven automation (SelectOptionAsync) racy; the
        // select stays put, hidden in place (1px), and remains the posted value.
        select.parentNode.insertBefore(root, select.nextSibling);
        select.setAttribute('data-searchable-enhanced', '');
        select.setAttribute('aria-hidden', 'true');
        select.setAttribute('tabindex', '-1');

        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'form-select fl-searchable-input';
        input.setAttribute('role', 'combobox');
        input.setAttribute('autocomplete', 'off');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-controls', this.listboxId);
        input.setAttribute('aria-activedescendant', '');
        if (this.placeholder) { input.setAttribute('placeholder', this.placeholder); }
        this.wireAccessibleName(input);
        var srcTestId = select.getAttribute('data-testid');
        if (srcTestId) { input.setAttribute('data-testid', srcTestId + '-search'); }
        if (select.disabled) { input.disabled = true; }

        var list = document.createElement('ul');
        list.id = this.listboxId;
        list.className = 'fl-searchable-list';
        list.setAttribute('role', 'listbox');
        list.hidden = true;

        var emptyEl = document.createElement('li');
        emptyEl.className = 'fl-searchable-empty';
        emptyEl.hidden = true;
        emptyEl.setAttribute('aria-live', 'polite');
        emptyEl.textContent = this.emptyText;

        // Visually-hidden polite live region for the filtered result count.
        var statusEl = document.createElement('span');
        statusEl.className = 'visually-hidden';
        statusEl.setAttribute('aria-live', 'polite');

        root.appendChild(input);
        root.appendChild(list);
        root.appendChild(statusEl);

        this.root = root;
        this.input = input;
        this.list = list;
        this.emptyEl = emptyEl;
        this.statusEl = statusEl;
        this.enhanced = true;

        input.value = this.displayLabel();

        input.addEventListener('focus', function () { self.onFocus(); });
        input.addEventListener('input', function () { self.onInput(); });
        input.addEventListener('keydown', function (e) { self.onKeydown(e); });
        input.addEventListener('blur', function () { self.onBlur(); });

        // Keep the combobox label in sync when the authoritative value is changed
        // by something other than our own commit (e.g. a cascade restoring a value
        // without a childList rebuild). Re-snapshot first so the lookup is fresh.
        select.addEventListener('change', function () {
            if (!self.enhanced || self._committing) { return; }
            self.snapshot();
            self.input.value = self.displayLabel();
        });

        // Commit via mousedown so it beats the input's blur teardown.
        list.addEventListener('mousedown', function (e) {
            var li = e.target;
            while (li && li !== list && li.getAttribute('role') !== 'option') { li = li.parentNode; }
            if (li && li.getAttribute('role') === 'option') {
                e.preventDefault();
                self.commitByOptionId(li.id);
            }
        });
    };

    Controller.prototype.wireAccessibleName = function (input) {
        var select = this.select;
        var label = select.id ? document.querySelector('label[for="' + select.id + '"]') : null;
        if (label) {
            if (!label.id) { label.id = nextId('fl-searchable-label'); }
            input.setAttribute('aria-labelledby', label.id);
        } else if (select.getAttribute('aria-label')) {
            input.setAttribute('aria-label', select.getAttribute('aria-label'));
        }
    };

    // Revert to the plain native select (count dropped to/under the threshold).
    Controller.prototype.unenhance = function () {
        if (!this.enhanced) { return; }
        var select = this.select;
        var root = this.root;
        select.removeAttribute('data-searchable-enhanced');
        select.removeAttribute('aria-hidden');
        select.removeAttribute('tabindex');
        // The select was never moved — just drop the sibling combobox wrapper.
        root.parentNode.removeChild(root);
        this.root = this.input = this.list = this.emptyEl = this.statusEl = null;
        this.enhanced = false;
        this.open = false;
        this.activeIndex = -1;
    };

    // Cascade rebuilt the options while enhanced: re-sync display, clear query.
    Controller.prototype.refresh = function () {
        if (!this.input) { return; }
        this.closeList();
        this.input.value = this.displayLabel();
    };

    // Disconnect observers + clear registry flags when the host subtree is
    // discarded (AJAX partial swap), so detached controllers/observers don't leak.
    Controller.prototype.dispose = function () {
        if (this.observer) { this.observer.disconnect(); }
        this.select.__searchableManaged = false;
        this.select.__searchableController = null;
        this.enhanced = false;
    };

    Controller.prototype.openList = function () {
        if (!this.enhanced) { return; }
        this.list.hidden = false;
        this.open = true;
        this.input.setAttribute('aria-expanded', 'true');
    };

    Controller.prototype.closeList = function () {
        if (!this.enhanced) { return; }
        this.list.hidden = true;
        this.open = false;
        this.activeIndex = -1;
        this.input.setAttribute('aria-expanded', 'false');
        this.input.setAttribute('aria-activedescendant', '');
    };

    // Render the options whose folded label contains the folded query.
    Controller.prototype.render = function (query) {
        var folded = fold(query);
        var list = this.list;
        // Clear existing option nodes (keep none — empty li is re-appended).
        list.innerHTML = '';
        this.visible = [];
        for (var i = 0; i < this.options.length; i++) {
            var opt = this.options[i];
            if (folded && opt.normalizedLabel.indexOf(folded) === -1) { continue; }
            var li = document.createElement('li');
            li.id = this.listboxId + '-opt-' + i;
            li.className = 'fl-searchable-option';
            li.setAttribute('role', 'option');
            li.setAttribute('data-value', opt.value);
            li.setAttribute('aria-selected', 'false');
            li.textContent = opt.label;
            list.appendChild(li);
            this.visible.push({ opt: opt, el: li });
        }
        list.appendChild(this.emptyEl);
        if (this.visible.length === 0) {
            this.emptyEl.hidden = false;
            this.activeIndex = -1;
        } else {
            this.emptyEl.hidden = true;
            this.activeIndex = 0;
        }
        this.highlight();
        // Announce the result count (language-neutral number).
        this.statusEl.textContent = String(this.visible.length);
    };

    Controller.prototype.highlight = function () {
        for (var i = 0; i < this.visible.length; i++) {
            var isActive = i === this.activeIndex;
            this.visible[i].el.setAttribute('aria-selected', isActive ? 'true' : 'false');
            if (isActive) {
                this.input.setAttribute('aria-activedescendant', this.visible[i].el.id);
                this.scrollIntoView(this.visible[i].el);
            }
        }
        if (this.activeIndex === -1) {
            this.input.setAttribute('aria-activedescendant', '');
        }
    };

    Controller.prototype.scrollIntoView = function (el) {
        var list = this.list;
        if (el.offsetTop < list.scrollTop) {
            list.scrollTop = el.offsetTop;
        } else if (el.offsetTop + el.offsetHeight > list.scrollTop + list.clientHeight) {
            list.scrollTop = el.offsetTop + el.offsetHeight - list.clientHeight;
        }
    };

    Controller.prototype.move = function (delta) {
        if (!this.open) { this.openList(); this.render(''); return; }
        if (this.visible.length === 0) { return; }
        var next = this.activeIndex + delta;
        if (next < 0) { next = 0; }
        if (next > this.visible.length - 1) { next = this.visible.length - 1; }
        this.activeIndex = next;
        this.highlight();
    };

    Controller.prototype.commitByOptionId = function (id) {
        for (var i = 0; i < this.visible.length; i++) {
            if (this.visible[i].el.id === id) { this.commit(this.visible[i].opt); return; }
        }
    };

    Controller.prototype.commitActive = function () {
        if (this.activeIndex >= 0 && this.activeIndex < this.visible.length) {
            this.commit(this.visible[this.activeIndex].opt);
            return true;
        }
        return false;
    };

    // The only writer of select.value: an explicit option commit.
    Controller.prototype.commit = function (opt) {
        this._committing = true;
        this.select.value = opt.value;
        this.select.dispatchEvent(new Event('change', { bubbles: true }));
        this._committing = false;
        this.input.value = opt.label;
        this.closeList();
    };

    Controller.prototype.onFocus = function () {
        this.openList();
        this.render('');
        // Select the text so the first keystroke replaces the committed label.
        try { this.input.select(); } catch (e) { /* ignore */ }
    };

    Controller.prototype.onInput = function () {
        this.openList();
        this.render(this.input.value);
    };

    Controller.prototype.onKeydown = function (e) {
        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                this.move(1);
                break;
            case 'ArrowUp':
                e.preventDefault();
                this.move(-1);
                break;
            case 'Enter':
                if (this.open) {
                    e.preventDefault();
                    this.commitActive();
                }
                break;
            case 'Escape':
                if (this.open) {
                    e.preventDefault();
                    e.stopPropagation();
                    this.closeList();
                    this.input.value = this.displayLabel();
                }
                break;
            case 'Tab':
                // Commit the highlight if open with a match; never block the Tab.
                if (this.open) { this.commitActive(); this.closeList(); }
                break;
            default:
                break;
        }
    };

    Controller.prototype.onBlur = function () {
        // Restore the committed label; typed-but-uncommitted text is discarded.
        this.closeList();
        this.input.value = this.displayLabel();
    };

    Controller.prototype.observe = function () {
        var self = this;
        if (typeof MutationObserver !== 'function') { return; }
        this.observer = new MutationObserver(function () { self.evaluate(); });
        this.observer.observe(this.select, { childList: true });
    };

    // ------------------------------------------------------------------ //
    // Boot / registry.
    // ------------------------------------------------------------------ //
    function manage(select) {
        if (select.__searchableManaged) { return; }
        select.__searchableManaged = true;
        try {
            var ctrl = new Controller(select);
            select.__searchableController = ctrl;
            ctrl.observe();
            ctrl.evaluate();
        } catch (e) {
            // Enhancement failure must leave the native select fully usable.
            select.__searchableManaged = false;
            if (window.console && console.warn) {
                console.warn('searchable-select: enhancement failed', e);
            }
        }
    }

    function scan(root) {
        var selects = root.querySelectorAll('select[data-searchable]');
        for (var i = 0; i < selects.length; i++) { manage(selects[i]); }
    }

    // Dispose any managed select inside a removed subtree (AJAX partial swap) so
    // its per-select observer + controller don't leak with the detached DOM.
    function disposeIn(node) {
        if (node.nodeType !== 1) { return; }
        if (node.matches && node.matches('select[data-searchable]') && node.__searchableController) {
            node.__searchableController.dispose();
        }
        if (node.querySelectorAll) {
            var found = node.querySelectorAll('select[data-searchable]');
            for (var i = 0; i < found.length; i++) {
                if (found[i].__searchableController) { found[i].__searchableController.dispose(); }
            }
        }
    }

    function watchDocument() {
        if (typeof MutationObserver !== 'function' || !document.body) { return; }
        var observer = new MutationObserver(function (records) {
            for (var i = 0; i < records.length; i++) {
                var added = records[i].addedNodes;
                for (var j = 0; j < added.length; j++) {
                    var node = added[j];
                    if (node.nodeType !== 1) { continue; }
                    if (node.matches && node.matches('select[data-searchable]')) {
                        manage(node);
                    }
                    if (node.querySelectorAll) { scan(node); }
                }
                var removed = records[i].removedNodes;
                for (var k = 0; k < removed.length; k++) {
                    disposeIn(removed[k]);
                }
            }
        });
        observer.observe(document.body, { childList: true, subtree: true });
    }

    function boot() {
        scan(document);
        watchDocument();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
