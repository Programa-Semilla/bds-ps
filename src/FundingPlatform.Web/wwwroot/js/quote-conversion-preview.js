// Spec 015 / T112 — supplier-quote conversion-preview client.
//
// Lifecycle:
//   - Activates on the quotation Add form (`<form data-quote-form>` + price/currency inputs).
//   - On change of the currency <select> or blur of the price input, debounces 300ms
//     and POSTs to the Convert action returned by the form's `data-convert-url`.
//   - The server returns the contract shape from `contracts/conversion-preview-api.md`.
//     The client renders the converted amount and rate metadata into
//     `<div data-quote-preview>`. CRC selection hides the preview entirely.
//
// FR-019: the client never multiplies. Every conversion goes through the server.

(function () {
    'use strict';

    function init() {
        var form = document.querySelector('[data-quote-form]');
        if (!form) return;

        var priceInput = form.querySelector('[name="Price"]');
        var currencySelect = form.querySelector('[name="Currency"]');
        var preview = document.querySelector('[data-quote-preview]');
        var convertUrl = form.getAttribute('data-convert-url');
        var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');

        if (!priceInput || !currencySelect || !preview || !convertUrl) return;

        var amountEl = preview.querySelector('[data-preview-amount]');
        var rateEl = preview.querySelector('[data-preview-rate]');
        var statusEl = preview.querySelector('[data-preview-status]');

        var debounceTimer = null;

        function hidePreview() {
            preview.hidden = true;
            preview.classList.add('d-none');
            if (statusEl) statusEl.textContent = '';
        }

        function showPreview() {
            preview.hidden = false;
            preview.classList.remove('d-none');
        }

        function setStatus(msg, isError) {
            if (!statusEl) return;
            statusEl.textContent = msg || '';
            statusEl.classList.toggle('text-danger', !!isError);
        }

        function fetchConversion() {
            var price = parseFloat(priceInput.value);
            var currency = (currencySelect.value || '').toUpperCase();
            if (!currency) { hidePreview(); return; }

            // CRC short-circuit: no preview region.
            if (currency === 'CRC') { hidePreview(); return; }
            if (!price || price <= 0 || isNaN(price)) {
                showPreview();
                if (amountEl) amountEl.textContent = '';
                if (rateEl) rateEl.textContent = '';
                setStatus('Ingrese un precio para ver la conversión.', false);
                return;
            }

            showPreview();
            setStatus('Calculando…', false);
            if (amountEl) amountEl.textContent = '';
            if (rateEl) rateEl.textContent = '';

            var headers = { 'Content-Type': 'application/json', 'Accept': 'application/json' };
            if (tokenInput && tokenInput.value) {
                headers['RequestVerificationToken'] = tokenInput.value;
            }

            fetch(convertUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: headers,
                body: JSON.stringify({ currencyCode: currency, amount: price })
            }).then(function (response) {
                return response.json().then(function (body) { return { status: response.status, body: body }; });
            }).then(function (result) {
                if (result.status !== 200) {
                    setStatus((result.body && result.body.error) || 'No se pudo calcular la conversión.', true);
                    return;
                }
                var body = result.body;
                if (body.isCrc) { hidePreview(); return; }

                var crc = formatCrc(body.convertedCrcAmount);
                if (amountEl) amountEl.textContent = crc;
                if (rateEl && body.rate) {
                    rateEl.textContent =
                        '1 ' + (body.originalCurrencyCode || currency) + ' = ' +
                        formatRate(body.rate.rateValue) + ' CRC ' +
                        '(Tipo ' + translateRateType(body.rate.rateType) + ', vigente desde ' +
                        formatDate(body.rate.effectiveAtUtc) + ')';
                }
                setStatus('', false);
            }).catch(function () {
                setStatus('No se pudo calcular la conversión.', true);
            });
        }

        function schedule() {
            if (debounceTimer) clearTimeout(debounceTimer);
            debounceTimer = setTimeout(fetchConversion, 300);
        }

        currencySelect.addEventListener('change', function () {
            // Currency changes are interactive and must update the preview immediately;
            // skip the debounce so the user sees the CRC short-circuit instantly.
            if (debounceTimer) clearTimeout(debounceTimer);
            fetchConversion();
        });
        priceInput.addEventListener('blur', schedule);
        priceInput.addEventListener('change', schedule);

        // Initial render: if the form starts with a non-CRC currency, kick off a preview.
        fetchConversion();
    }

    function formatCrc(value) {
        if (value === null || value === undefined) return '';
        try {
            return new Intl.NumberFormat('es-CR', { style: 'currency', currency: 'CRC', minimumFractionDigits: 2 }).format(value);
        } catch (e) {
            return '₡' + Number(value).toFixed(2);
        }
    }
    function formatRate(value) {
        if (value === null || value === undefined) return '';
        return Number(value).toFixed(6).replace(/0+$/, '').replace(/\.$/, '');
    }
    function formatDate(iso) {
        if (!iso) return '';
        var d = new Date(iso);
        if (isNaN(d.getTime())) return iso;
        return d.toLocaleString('es-CR');
    }
    function translateRateType(rt) {
        if (!rt) return '';
        if (rt === 'Buy')  return 'Compra';
        if (rt === 'Sell') return 'Venta';
        return rt;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
