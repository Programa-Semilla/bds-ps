// public-code-banner.js — Spec 021 / T064 / FR-008.
//
// On click of `[data-public-code-copy]`, copies the adjacent PublicCode text
// to clipboard via `navigator.clipboard.writeText`. Shows a transient
// "Copiado" tooltip for 2s.
//
// Source text is resolved in this order:
//   1. `data-public-code` attribute on the trigger (preferred — explicit).
//   2. Text content of the sibling element matched by `data-public-code-text`.
//   3. Text content of the immediately previous sibling.

(function () {
    'use strict';

    var TOOLTIP_MS = 2000;

    function resolveText(trigger) {
        var explicit = trigger.getAttribute('data-public-code');
        if (explicit) return explicit;
        var targetSel = trigger.getAttribute('data-public-code-text');
        if (targetSel) {
            var t = document.querySelector(targetSel);
            if (t) return (t.textContent || '').trim();
        }
        var prev = trigger.previousElementSibling;
        if (prev) return (prev.textContent || '').trim();
        return '';
    }

    function showCopiedTooltip(trigger) {
        var prior = trigger.getAttribute('data-original-title') || trigger.getAttribute('title') || '';
        trigger.setAttribute('data-original-title', prior);
        trigger.setAttribute('title', 'Copiado');

        // Tabler bundles Bootstrap; use the tooltip API when available.
        var bootstrapTooltip = window.bootstrap && window.bootstrap.Tooltip;
        if (bootstrapTooltip) {
            var tip = bootstrapTooltip.getOrCreateInstance(trigger);
            tip.setContent({ '.tooltip-inner': 'Copiado' });
            tip.show();
            setTimeout(function () {
                tip.hide();
                if (prior) {
                    trigger.setAttribute('title', prior);
                } else {
                    trigger.removeAttribute('title');
                }
            }, TOOLTIP_MS);
        } else {
            // Fallback: append an inline "Copiado" badge for 2s.
            var badge = document.createElement('span');
            badge.className = 'ms-2 text-success fl-public-code-copied';
            badge.textContent = 'Copiado';
            trigger.parentElement && trigger.parentElement.appendChild(badge);
            setTimeout(function () { badge.remove(); }, TOOLTIP_MS);
        }
    }

    function onCopy(trigger) {
        var text = resolveText(trigger);
        if (!text) return;
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(function () {
                showCopiedTooltip(trigger);
            });
        } else {
            // Legacy fallback: stage a hidden <textarea> + execCommand.
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', '');
            ta.style.position = 'absolute';
            ta.style.left = '-9999px';
            document.body.appendChild(ta);
            ta.select();
            try { document.execCommand('copy'); } catch (_e) { /* swallow */ }
            ta.remove();
            showCopiedTooltip(trigger);
        }
    }

    function init() {
        var triggers = document.querySelectorAll('[data-public-code-copy]');
        for (var i = 0; i < triggers.length; i++) {
            (function (trigger) {
                trigger.addEventListener('click', function (e) {
                    e.preventDefault();
                    onCopy(trigger);
                });
            })(triggers[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
