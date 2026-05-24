// SignalR price-tick client. Scans for [data-price-symbol] elements, subscribes
// to the matching symbol groups on /hubs/prices, and patches in live ticks.
// Loaded post-cutover when App Gateway routes /hubs/prices to the API.
(function () {
    if (!window.signalR) return;
    var els = Array.prototype.slice.call(document.querySelectorAll('[data-price-symbol]'));
    if (!els.length) return;
    var symbols = Array.from(new Set(els.map(function (e) {
        return (e.getAttribute('data-price-symbol') || '').toUpperCase();
    }).filter(Boolean)));
    if (!symbols.length) return;

    var conn = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/prices')
        .withAutomaticReconnect()
        .build();

    function fmtMoney(n, currency) {
        try {
            return new Intl.NumberFormat(undefined, { style: 'currency', currency: currency || 'USD' }).format(n);
        } catch { return n.toFixed(2); }
    }
    function fmtPct(n) { return (n >= 0 ? '+' : '') + n.toFixed(2) + '%'; }

    conn.on('price', function (tick) {
        if (!tick || !tick.symbol) return;
        var sym = String(tick.symbol).toUpperCase();
        document.querySelectorAll('[data-price-symbol="' + sym + '"]').forEach(function (el) {
            var currency = el.getAttribute('data-price-currency') || 'USD';
            var kind = el.getAttribute('data-price-kind') || 'price';
            if (kind === 'pct') {
                el.textContent = fmtPct(Number(tick.changePercent || 0));
            } else {
                el.textContent = fmtMoney(Number(tick.price || 0), currency);
            }
            el.classList.remove('price-up', 'price-down');
            el.classList.add(Number(tick.changePercent || 0) >= 0 ? 'price-up' : 'price-down');
        });
    });

    conn.start()
        .then(function () { return conn.invoke('Subscribe', symbols); })
        .catch(function (err) { /* hub unreachable in dev; ignore */ console.debug('prices hub:', err); });
})();
