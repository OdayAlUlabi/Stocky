// Lazy ticker autocomplete. Any <input list="tickers"> triggers a debounced
// search against /Securities/Search (MVC in-process proxy to the API) and
// repopulates the shared <datalist id="tickers"> with the top matches.
(function () {
    var dl = document.getElementById('tickers');
    if (!dl) return;
    var cache = Object.create(null);
    var timer = null;
    var lastQuery = '';

    function setOptions(rows) {
        var html = '';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            // value = symbol so form posts the ticker; label shows name.
            html += '<option value="' + escapeAttr(r.symbol) + '">' + escapeAttr(r.name || '') + '</option>';
        }
        dl.innerHTML = html;
    }
    function escapeAttr(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function search(q) {
        q = (q || '').trim();
        if (q === lastQuery) return;
        lastQuery = q;
        if (cache[q]) { setOptions(cache[q]); return; }
        fetch('/Securities/Search?q=' + encodeURIComponent(q) + '&limit=15', { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (rows) {
                if (!Array.isArray(rows)) rows = [];
                cache[q] = rows;
                if (q === lastQuery) setOptions(rows);
            })
            .catch(function () { /* ignore */ });
    }

    function onInput(e) {
        var t = e.target;
        if (!t || t.getAttribute('list') !== 'tickers') return;
        var q = t.value;
        clearTimeout(timer);
        timer = setTimeout(function () { search(q); }, 150);
    }
    document.addEventListener('input', onInput, true);
    document.addEventListener('focusin', function (e) {
        if (e.target && e.target.getAttribute && e.target.getAttribute('list') === 'tickers') {
            search(e.target.value || '');
        }
    }, true);
})();
