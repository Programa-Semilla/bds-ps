/*
 * Spec 020 — AI quote comparison front-end.
 * No framework, vanilla JS only (consistent with the project's no-CDN posture).
 */
(function () {
    'use strict';

    var ctx = window.__fundingComparison || {};
    var POLL_INTERVAL_MS = ctx.pollIntervalMs || 3000;
    var pollTimer = null;

    function getToken() {
        return ctx.antiforgeryToken || '';
    }

    function postJson(url, body) {
        var headers = { 'Content-Type': 'application/json', 'Accept': 'application/json' };
        var tokenHeader = ctx.antiforgeryHeader || 'X-CSRF-TOKEN';
        var token = getToken();
        if (token) headers[tokenHeader] = token;
        return fetch(url, {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(body || {})
        });
    }

    // FINDING-9 — Accept: text/html signals the controller to return the
    // rendered _ComparisonRegion partial so the JS can do an inline outerHTML
    // swap instead of a full window.location.reload. JSON errors still come
    // back as JSON (the controller's failure envelopes are content-negotiated
    // independently of this preference).
    function postForHtml(url, body) {
        var headers = { 'Content-Type': 'application/json', 'Accept': 'text/html, application/json;q=0.9' };
        var tokenHeader = ctx.antiforgeryHeader || 'X-CSRF-TOKEN';
        var token = getToken();
        if (token) headers[tokenHeader] = token;
        return fetch(url, {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(body || {})
        });
    }

    function getJson(url) {
        return fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' } });
    }

    function setStatus(itemId, text) {
        var el = document.querySelector('[data-testid="comparison-status-pill"][data-item-id="' + itemId + '"]');
        if (el) el.textContent = text || '';
    }

    function renderError(itemId, code, payload) {
        var msg = 'Generación falló. Reintentar.';
        if (code === 'provider_transient') msg = 'Generación falló: el proveedor de IA no respondió. Reintentar.';
        else if (code === 'provider_hard') msg = 'Generación falló. Contacte un administrador.';
        else if (code === 'schema_invalid') msg = 'La respuesta de IA no fue válida. Reintentar.';
        else if (code === 'rate_limit_exceeded') msg = 'Límite de generaciones alcanzado para esta solicitud (10/24h). Inténtelo más tarde o contacte un administrador.';
        else if (code === 'token_cap_exceeded') {
            var offending = (payload && payload.offendingInput) || 'una entrada';
            msg = 'El proveedor ' + offending + ' adjuntó un PDF demasiado grande; pida una versión recortada o ejecute como administrador para anular el límite.';
        } else if (code === 'pii_redaction_failed') msg = 'No se pudo procesar de forma segura el archivo. Pida una versión legible.';
        else if (code === 'single_supplier') msg = 'Se necesitan al menos 2 cotizaciones para comparar.';
        else if (code === 'timeout') msg = 'Tiempo de espera agotado. Reintentar.';
        else if (code === 'application_closed') msg = 'La solicitud está cerrada.';
        else if (code === 'concurrent_generation') msg = 'Ya hay una generación en curso.';
        setStatus(itemId, msg);
    }

    function generateForItem(itemId, force) {
        var region = document.querySelector('[data-testid="comparison-region"][data-item-id="' + itemId + '"]');
        if (!region) return;

        var toggle = region.querySelector('[data-testid="comparison-bypass-toggle"] input[type="checkbox"]');
        var bypass = !!(toggle && toggle.checked);

        setStatus(itemId, 'Generando…');

        // FINDING-9 — request HTML so we can inline-swap the partial instead
        // of forcing a full page reload (which throws away scroll position,
        // open dropdowns, and rotates the anti-CSRF token unnecessarily).
        postForHtml('/Review/GenerateComparison/' + itemId, {
            bypassRateLimit: bypass,
            bypassTokenCap: bypass,
            forceRegenerate: !!force
        }).then(function (resp) {
            var contentType = resp.headers.get('Content-Type') || '';
            if (resp.ok && contentType.indexOf('text/html') !== -1) {
                return resp.text().then(function (html) {
                    region.outerHTML = html;
                    // Re-bind generate buttons inside the swapped region. The
                    // outerHTML swap drops the old listeners; bind() is
                    // idempotent on data-* selectors so calling it again is safe.
                    bindRegion(itemId);
                    setStatus(itemId, 'Listo');
                });
            }
            return resp.json().then(function (payload) {
                if (resp.ok) {
                    // Fallback path — server didn't return HTML for some reason.
                    setStatus(itemId, 'Listo');
                } else {
                    renderError(itemId, payload && payload.code, payload);
                }
            });
        }).catch(function () {
            setStatus(itemId, 'Generación falló: error de red. Reintentar.');
        });
    }

    function bindRegion(itemId) {
        var region = document.querySelector('[data-testid="comparison-region"][data-item-id="' + itemId + '"]');
        if (!region) return;
        region.querySelectorAll('[data-testid="comparison-generate-btn"]').forEach(function (btn) {
            if (btn.dataset.bound === '1') return;
            btn.dataset.bound = '1';
            btn.addEventListener('click', function () {
                var id = btn.getAttribute('data-item-id');
                var action = btn.getAttribute('data-action');
                generateForItem(id, action === 'regenerate');
            });
        });
    }

    function pollItemStatus(itemId) {
        return getJson('/Review/ItemStatus/' + itemId).then(function (resp) {
            return resp.ok ? resp.json() : null;
        });
    }

    function startPollingAll() {
        var statusPills = document.querySelectorAll('[data-testid="comparison-status-pill"]');
        if (!statusPills.length) return;
        if (pollTimer) clearInterval(pollTimer);

        function tick() {
            var anyInflight = false;
            statusPills.forEach(function (pill) {
                var itemId = pill.getAttribute('data-item-id');
                if (!itemId) return;
                pollItemStatus(itemId).then(function (status) {
                    if (!status) return;
                    var state = status.state;
                    if (state === 'Pending') {
                        setStatus(itemId, 'Pendiente');
                        anyInflight = true;
                    } else if (state === 'Running') {
                        setStatus(itemId, 'En progreso');
                        anyInflight = true;
                    } else if (state === 'Failed') {
                        setStatus(itemId, 'Falló: ' + (status.failureReason || 'desconocido'));
                    } else if (state === 'CachedFresh' || state === 'CachedStale') {
                        setStatus(itemId, state === 'CachedStale' ? 'Listo (desactualizado)' : 'Listo');
                    }
                });
            });
            if (!anyInflight && pollTimer) {
                clearInterval(pollTimer);
                pollTimer = null;
            }
        }
        pollTimer = setInterval(tick, POLL_INTERVAL_MS);
        tick();
    }

    function generateAll(force) {
        var appId = ctx.applicationId;
        if (!appId) return;
        var globalToggle = document.querySelector('[data-testid="comparison-bypass-app-toggle"] input[type="checkbox"]');
        var bypass = !!(globalToggle && globalToggle.checked);

        postJson('/Review/GenerateAll/' + appId, {
            forceAll: !!force,
            bypassRateLimit: bypass,
            bypassTokenCap: bypass
        }).then(function (resp) {
            if (resp.ok) {
                startPollingAll();
            } else {
                return resp.json().then(function (payload) {
                    // Spec 024 — surface AJAX failures as a toast instead of window.alert.
                    var msg = (payload && payload.code) || 'Error desconocido al encolar.';
                    if (window.Notify) { window.Notify.error(msg); } else { alert(msg); }
                });
            }
        });
    }

    function init() {
        document.querySelectorAll('[data-testid="comparison-generate-btn"]').forEach(function (btn) {
            if (btn.dataset.bound === '1') return;
            btn.dataset.bound = '1';
            btn.addEventListener('click', function () {
                var id = btn.getAttribute('data-item-id');
                var action = btn.getAttribute('data-action');
                generateForItem(id, action === 'regenerate');
            });
        });

        var allBtn = document.querySelector('[data-testid="comparison-generate-all-btn"]');
        if (allBtn) allBtn.addEventListener('click', function () { generateAll(false); });

        var forceBtn = document.querySelector('[data-testid="comparison-force-all-btn"]');
        var appBypass = document.querySelector('[data-testid="comparison-bypass-app-toggle"] input[type="checkbox"]');
        if (appBypass && forceBtn) {
            appBypass.addEventListener('change', function () {
                forceBtn.disabled = !appBypass.checked;
            });
        }
        if (forceBtn) forceBtn.addEventListener('click', function () { generateAll(true); });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
