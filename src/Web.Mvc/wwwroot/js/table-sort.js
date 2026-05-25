// Lightweight client-side table sort. Auto-attaches to every <table> that has a
// <thead><tr><th>...</th></tr></thead> in the document. Click a header to sort
// ascending; click again to sort descending; a third click restores the
// original DOM order. Footer (<tfoot>) rows are never reordered.
//
// Per-cell value resolution:
//   1. <td data-sort-value="...">  -> use that string (parsed as number if it
//      looks numeric, else as a date if it parses, else as lowercase text).
//   2. Otherwise the cell's textContent is used, stripped of currency / commas
//      / percent signs / parentheses-as-negative so "$1,234.56" -> 1234.56 and
//      "(2.5%)" -> -2.5.
//
// Opt out per-table with <table data-no-sort> or per-column with
// <th data-no-sort>. The script is idempotent (initialises each table only
// once) and safe to call after dynamic DOM updates via window.StockySortTables.
(function () {
    'use strict';

    const NUM_CLEAN_RE = /[\s,$£€¥%]/g;
    const PARENS_NEG_RE = /^\((.+)\)$/;

    function parseCell(raw) {
        if (raw === null || raw === undefined) return { kind: 'empty', value: '' };
        let s = String(raw).trim();
        if (s === '' || s === '—' || s === '-' || s === 'N/A') {
            return { kind: 'empty', value: '' };
        }
        // ISO date or yyyy-mm-dd[ hh:mm]
        if (/^\d{4}-\d{2}-\d{2}(?:[ T]\d{2}:\d{2}(?::\d{2})?)?$/.test(s)) {
            const ts = Date.parse(s);
            if (!isNaN(ts)) return { kind: 'num', value: ts };
        }
        // Strip currency / thousands sep / percent. Handle (1.23) as -1.23.
        let neg = false;
        const m = s.match(PARENS_NEG_RE);
        if (m) { s = m[1]; neg = true; }
        const cleaned = s.replace(NUM_CLEAN_RE, '');
        if (cleaned !== '' && /^-?\d+(?:\.\d+)?$/.test(cleaned)) {
            const n = parseFloat(cleaned);
            return { kind: 'num', value: neg ? -n : n };
        }
        return { kind: 'text', value: s.toLowerCase() };
    }

    function cellValue(td) {
        if (td.hasAttribute('data-sort-value')) {
            return parseCell(td.getAttribute('data-sort-value'));
        }
        return parseCell(td.textContent);
    }

    function compare(a, b, dir) {
        // Empties always sort to the bottom regardless of direction.
        if (a.kind === 'empty' && b.kind === 'empty') return 0;
        if (a.kind === 'empty') return 1;
        if (b.kind === 'empty') return -1;
        let cmp;
        if (a.kind === 'num' && b.kind === 'num') {
            cmp = a.value - b.value;
        } else {
            cmp = String(a.value).localeCompare(String(b.value), undefined, { numeric: true, sensitivity: 'base' });
        }
        return dir === 'desc' ? -cmp : cmp;
    }

    function attach(table) {
        if (table.dataset.sortAttached === '1') return;
        if (table.hasAttribute('data-no-sort')) return;
        const thead = table.tHead;
        if (!thead || !thead.rows.length) return;
        const headerRow = thead.rows[thead.rows.length - 1];
        const headers = Array.from(headerRow.cells);
        if (headers.length === 0) return;
        // Skip tables with colspan'd headers in the sortable row (ambiguous).
        if (headers.some(th => th.colSpan > 1)) return;

        table.dataset.sortAttached = '1';
        table.classList.add('sortable');

        // Capture original tbody order so a third click restores it.
        const tbody = table.tBodies[0];
        if (!tbody) return;
        const originalOrder = Array.from(tbody.rows);

        let activeIdx = -1;
        let direction = null; // 'asc' | 'desc' | null

        headers.forEach((th, idx) => {
            if (th.hasAttribute('data-no-sort')) return;
            th.classList.add('sortable-col');
            th.setAttribute('role', 'button');
            th.setAttribute('tabindex', '0');
            const onActivate = () => {
                if (activeIdx === idx) {
                    direction = direction === 'asc' ? 'desc' : direction === 'desc' ? null : 'asc';
                } else {
                    activeIdx = idx;
                    direction = 'asc';
                }
                applySort();
            };
            th.addEventListener('click', onActivate);
            th.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    onActivate();
                }
            });
        });

        function applySort() {
            // Reset indicators.
            headers.forEach(th => {
                th.classList.remove('sort-asc', 'sort-desc');
                th.removeAttribute('aria-sort');
            });

            if (direction === null) {
                // Restore original order.
                const frag = document.createDocumentFragment();
                originalOrder.forEach(r => frag.appendChild(r));
                tbody.appendChild(frag);
                activeIdx = -1;
                return;
            }

            const th = headers[activeIdx];
            th.classList.add(direction === 'asc' ? 'sort-asc' : 'sort-desc');
            th.setAttribute('aria-sort', direction === 'asc' ? 'ascending' : 'descending');

            const rows = Array.from(tbody.rows).filter(r => !r.hasAttribute('data-no-sort'));
            const sticky = Array.from(tbody.rows).filter(r => r.hasAttribute('data-no-sort'));
            rows.sort((ra, rb) => {
                const ca = ra.cells[activeIdx];
                const cb = rb.cells[activeIdx];
                if (!ca || !cb) return 0;
                return compare(cellValue(ca), cellValue(cb), direction);
            });
            const frag = document.createDocumentFragment();
            rows.forEach(r => frag.appendChild(r));
            sticky.forEach(r => frag.appendChild(r));
            tbody.appendChild(frag);
        }
    }

    function attachAll(root) {
        (root || document).querySelectorAll('table').forEach(attach);
    }

    window.StockySortTables = attachAll;

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => attachAll());
    } else {
        attachAll();
    }
})();
