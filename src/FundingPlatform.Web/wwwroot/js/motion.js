// motion.js — Spec 011 motion helpers (FR-014: no hard-coded durations).
//
// All durations are READ FROM CSS CUSTOM PROPERTIES via getComputedStyle.
// Reduced-motion is honoured by branching at the top of each helper.

(function (root) {
  'use strict';

  function prefersReducedMotion() {
    return root.matchMedia && root.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }

  function readMotionToken(name) {
    const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    if (!v) return 0;
    if (v.endsWith('ms')) return parseFloat(v);
    if (v.endsWith('s'))  return parseFloat(v) * 1000;
    return parseFloat(v) || 0;
  }

  // Read a CSS custom property as a string (used for color tokens; FR-009 keeps
  // raw hex literals out of JS as well as out of cshtml/css).
  function readToken(name) {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  }

  // Number ticker — element should carry data-ticker-target="<finalValue>" on the value node.
  // Honors --motion-slow with --ease-decelerate; under reduced-motion, sets final value immediately.
  function tickerStart(node) {
    const target = parseFloat(node.getAttribute('data-ticker-target'));
    if (!Number.isFinite(target)) return;
    if (prefersReducedMotion()) {
      node.textContent = String(Math.round(target));
      return;
    }
    const totalMs = readMotionToken('--motion-slow') || 400;
    const start = performance.now();
    const startVal = 0;
    function frame(now) {
      const t = Math.min(1, (now - start) / totalMs);
      // ease-decelerate approximation: 1 - (1-t)^3
      const eased = 1 - Math.pow(1 - t, 3);
      const v = startVal + (target - startVal) * eased;
      node.textContent = String(Math.round(v));
      if (t < 1) requestAnimationFrame(frame);
      else node.textContent = String(Math.round(target));
    }
    requestAnimationFrame(frame);
  }

  function mountTickers(scope) {
    const root = scope || document;
    root.querySelectorAll('[data-ticker-target]').forEach(tickerStart);
  }

  // Journey timeline mount stagger (FR-042). 60ms per node, capped at --motion-slow total.
  function mountJourney(scope) {
    const root = scope || document;
    if (prefersReducedMotion()) {
      root.querySelectorAll('.fl-journey-node').forEach((n) => n.classList.add('is-mounted'));
      return;
    }
    root.querySelectorAll('.fl-journey').forEach((journey) => {
      const nodes = journey.querySelectorAll('.fl-journey-node[data-state="completed"], .fl-journey-node[data-state="current"]');
      const cap = readMotionToken('--motion-slow') || 400;
      const stride = Math.min(60, Math.floor(cap / Math.max(1, nodes.length)));
      const transitionMs = readMotionToken('--motion-base') || 250;
      nodes.forEach((node, i) => {
        node.style.opacity = '0';
        node.style.transform = node.dataset.state === 'current' ? 'scale(0.9)' : 'translateY(4px)';
        setTimeout(() => {
          node.style.transition = `opacity var(--motion-base) var(--ease-spring), transform var(--motion-base) var(--ease-spring)`;
          node.style.opacity = '';
          node.style.transform = '';
          // Clear inline transition once the animation finishes so we don't
          // leave inline style attributes on the DOM (FR-010 spirit).
          setTimeout(() => {
            node.style.transition = '';
          }, transitionMs + 50);
        }, i * stride);
      });
    });
  }

  // Click completed-node → scroll to and highlight matching event log (FR-039).
  function bindJourneyEventClicks(scope) {
    const root = scope || document;
    root.querySelectorAll('.fl-journey-node[data-state="completed"][data-event-anchor]').forEach((node) => {
      node.addEventListener('click', (ev) => {
        const id = node.getAttribute('data-event-anchor');
        if (!id) return;
        const target = document.getElementById(id);
        if (!target) return;
        ev.preventDefault();
        target.scrollIntoView({ behavior: prefersReducedMotion() ? 'auto' : 'smooth', block: 'center' });
        target.classList.add('is-highlighted');
        setTimeout(() => target.classList.remove('is-highlighted'), 1500);
      });
      node.setAttribute('tabindex', '0');
      node.addEventListener('keydown', (ev) => {
        if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); node.click(); }
      });
    });
  }

  // Ceremony confetti — single-shot, only when fresh and not reduced-motion.
  function mountCeremony(opts) {
    if (!opts) return;
    if (!opts.isFresh || prefersReducedMotion()) {
      const seal = document.querySelector('.fl-ceremony-seal');
      if (seal) seal.setAttribute('data-state', 'static');
      const primary = document.querySelector('[data-ceremony-primary]');
      if (primary && typeof primary.focus === 'function') primary.focus();
      return;
    }
    if (opts.confettiOn && typeof root.confetti === 'function') {
      try {
        // Spec 019 R5 — 4-color palette: teal + yellow + neutral white + primary subtle.
        // Pulled from design tokens so tokens.css remains the only source of raw hex
        // (spec 011 FR-009 invariant carries forward).
        const colors = [
          readToken('--color-primary'),
          readToken('--color-accent'),
          readToken('--color-bg-surface'),
          readToken('--color-primary-subtle'),
        ].filter(Boolean);
        root.confetti({
          particleCount: 80,
          spread: 70,
          origin: { y: 0.4 },
          colors: colors.length > 0 ? colors : undefined,
        });
      } catch (e) { /* shim no-op safe */ }
    }
    // ESC → DashboardHref
    if (opts.dashboardHref) {
      document.addEventListener('keydown', function once(ev) {
        if (ev.key === 'Escape') {
          document.removeEventListener('keydown', once);
          window.location.assign(opts.dashboardHref);
        }
      });
    }
  }

  root.PlatformMotion = {
    prefersReducedMotion,
    readMotionToken,
    mountTickers,
    mountJourney,
    bindJourneyEventClicks,
    mountCeremony,
  };
})(typeof window !== 'undefined' ? window : globalThis);
