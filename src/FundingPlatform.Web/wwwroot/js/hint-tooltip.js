// Spec 027 / US7 — HTML-capable field hint tooltips.
//
// Self-contained, like confirm-dialog.js: Tabler bundles Bootstrap's CSS but
// does NOT expose the `window.bootstrap` JS namespace, so we drive the bubble
// ourselves rather than calling `new bootstrap.Popover(...)`. Triggers are any
// element carrying a `data-hint` attribute (rendered by _HintTooltip.cshtml as
// an info icon). The attribute value is curated HTML (es-CR copy authored in
// the HintCopy provider, never user input) and is injected as innerHTML so the
// bubble renders formatting (bold, lists) rather than escaped tags.
(function () {
    'use strict';

    var BUBBLE_ID = 'fl-hint-bubble';
    var bubble = null;
    var activeTrigger = null;

    function ensureBubble() {
        if (bubble) {
            return bubble;
        }
        bubble = document.createElement('div');
        bubble.id = BUBBLE_ID;
        bubble.className = 'fl-hint-bubble';
        bubble.setAttribute('role', 'tooltip');
        bubble.style.position = 'absolute';
        bubble.style.zIndex = '1080';
        bubble.style.maxWidth = '20rem';
        bubble.style.display = 'none';
        // Tabler-ish card styling, set inline to keep the module self-contained.
        bubble.style.background = '#1d273b';
        bubble.style.color = '#fff';
        bubble.style.padding = '0.5rem 0.75rem';
        bubble.style.borderRadius = '6px';
        bubble.style.fontSize = '0.8125rem';
        bubble.style.lineHeight = '1.35';
        bubble.style.boxShadow = '0 0.5rem 1rem rgba(0,0,0,.18)';
        document.body.appendChild(bubble);
        return bubble;
    }

    function show(trigger) {
        var html = trigger.getAttribute('data-hint');
        if (!html) {
            return;
        }
        var b = ensureBubble();
        b.innerHTML = html; // curated HTML — see header note.
        b.style.display = 'block';
        activeTrigger = trigger;
        position(trigger, b);
    }

    function position(trigger, b) {
        var rect = trigger.getBoundingClientRect();
        var scrollX = window.pageXOffset || document.documentElement.scrollLeft;
        var scrollY = window.pageYOffset || document.documentElement.scrollTop;
        // Default: just below the icon, left-aligned to it.
        var top = rect.bottom + scrollY + 6;
        var left = rect.left + scrollX;
        // Keep within the viewport horizontally.
        var maxLeft = scrollX + document.documentElement.clientWidth - b.offsetWidth - 8;
        if (left > maxLeft) {
            left = Math.max(scrollX + 8, maxLeft);
        }
        b.style.top = top + 'px';
        b.style.left = left + 'px';
    }

    function hide(trigger) {
        if (activeTrigger && trigger && activeTrigger !== trigger) {
            return;
        }
        if (bubble) {
            bubble.style.display = 'none';
            bubble.innerHTML = '';
        }
        activeTrigger = null;
    }

    function init() {
        // Event delegation: works for fields rendered after load too.
        document.addEventListener('mouseover', function (e) {
            var t = e.target.closest('[data-hint]');
            if (t) {
                show(t);
            }
        });
        document.addEventListener('mouseout', function (e) {
            var t = e.target.closest('[data-hint]');
            // Ignore moves that stay within the same trigger (span <-> its icon).
            if (t && !t.contains(e.relatedTarget)) {
                hide(t);
            }
        });
        document.addEventListener('focusin', function (e) {
            var t = e.target.closest('[data-hint]');
            if (t) {
                show(t);
            }
        });
        document.addEventListener('focusout', function (e) {
            var t = e.target.closest('[data-hint]');
            if (t) {
                hide(t);
            }
        });
        document.addEventListener('keydown', function (e) {
            if ((e.key === 'Escape' || e.key === 'Esc') && activeTrigger) {
                hide(activeTrigger);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
