/**
 * Upcoming Media — Full page view.
 * Shows all upcoming items with filters.
 * "Available" items are clickable and link to the real Jellyfin library item.
 * "Coming Soon" items show a countdown and are NOT clickable.
 */
(function () {
    'use strict';

    console.log('[UpcomingMedia] Upcoming page JS loaded');

    var allItems = [];

    function getPage() {
        return document.querySelector('#UpcomingMediaPage');
    }

    function esc(s) {
        if (!s) return '';
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(s));
        return d.innerHTML;
    }

    function formatDate(iso) {
        if (!iso) return 'TBA';
        try {
            return new Date(iso).toLocaleDateString(undefined, {
                year: 'numeric', month: 'short', day: 'numeric'
            });
        } catch (e) { return 'TBA'; }
    }

    function daysUntil(iso) {
        if (!iso) return '';
        var diff = Math.ceil((new Date(iso) - new Date()) / 86400000);
        if (diff < 0) return 'Now available!';
        if (diff === 0) return 'Today!';
        if (diff === 1) return 'Tomorrow';
        return diff + ' days';
    }

    /* Navigate to the actual Jellyfin library item */
    function navigateToLibraryItem(item) {
        if (typeof ApiClient === 'undefined') return;

        var searchUrl = ApiClient.getUrl('Items', {
            searchTerm: item.Title,
            Recursive: true,
            IncludeItemTypes: item.MediaType === 'Series' ? 'Series' : 'Movie',
            Limit: 5,
            Fields: 'ProviderIds'
        });

        ApiClient.ajax({ url: searchUrl, type: 'GET', dataType: 'json' }).then(function (result) {
            var items = result.Items || result;
            if (!Array.isArray(items)) items = [];

            var match = null;
            for (var i = 0; i < items.length; i++) {
                var providerIds = items[i].ProviderIds || {};
                if (item.TmdbId && providerIds.Tmdb === String(item.TmdbId)) {
                    match = items[i];
                    break;
                }
            }
            if (!match) {
                for (var j = 0; j < items.length; j++) {
                    if (items[j].Name && items[j].Name.toLowerCase() === item.Title.toLowerCase()) {
                        match = items[j];
                        break;
                    }
                }
            }
            if (!match && items.length > 0) match = items[0];

            if (match && match.Id) {
                var detailUrl = 'details?id=' + match.Id;
                if (typeof Emby !== 'undefined' && Emby.Page && Emby.Page.show) {
                    Emby.Page.show('/' + detailUrl);
                } else if (typeof Dashboard !== 'undefined' && Dashboard.navigate) {
                    Dashboard.navigate(detailUrl);
                } else {
                    window.location.hash = '#!/' + detailUrl;
                }
            } else {
                Dashboard.alert('Item not found in your library yet.');
            }
        }).catch(function () {
            Dashboard.alert('Could not search library.');
        });
    }

    function loadItems() {
        var pg = getPage();
        if (!pg) return;

        var url = ApiClient.getUrl('UpcomingMedia/Items');
        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (data) {
            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) { data = []; }
            }
            allItems = Array.isArray(data) ? data : [];
            renderGrid();
        }).catch(function () {
            var grid = pg.querySelector('#upcomingGrid');
            if (grid) grid.innerHTML = '<p style="color:red;">Failed to load items.</p>';
        });
    }

    function renderGrid() {
        var pg = getPage();
        if (!pg) return;
        var grid = pg.querySelector('#upcomingGrid');
        if (!grid) return;

        var statusFilter = (pg.querySelector('#filterStatus') || {}).value || '';
        var typeFilter = (pg.querySelector('#filterType') || {}).value || '';

        var filtered = allItems.filter(function (item) {
            if (item.Status === 'Expired') return false;
            if (statusFilter && item.Status !== statusFilter) return false;
            if (typeFilter && item.MediaType !== typeFilter) return false;
            return true;
        });

        if (filtered.length === 0) {
            grid.innerHTML = '<p>No items match your filters.</p>';
            return;
        }

        var html = '';
        filtered.forEach(function (item, idx) {
            var date = item.AvailableDate || item.ReleaseDate;
            var dateStr = formatDate(date);
            var countdown = daysUntil(date);
            var isAvailable = item.Status === 'Available';
            var isSeries = item.MediaType === 'Series';

            var cardStyle = 'width:160px;border-radius:8px;overflow:hidden;background:rgba(255,255,255,.05);'
                + (isAvailable ? 'cursor:pointer;' : '');

            html += '<div class="up-card" data-idx="' + idx + '" data-available="' + (isAvailable ? '1' : '0') + '" '
                + 'style="' + cardStyle + '">'
                + '<div style="position:relative;aspect-ratio:2/3;background:#222;">';

            if (item.PosterUrl) {
                html += '<img src="' + esc(item.PosterUrl) + '" style="width:100%;height:100%;object-fit:cover;display:block;" '
                    + 'onerror="this.outerHTML=\'<div style=padding:20px;color:#555;text-align:center>No Image</div>\'" />';
            } else {
                html += '<div style="padding:20px;color:#555;text-align:center;">No Image</div>';
            }

            // Type badge
            html += '<span style="position:absolute;top:6px;left:6px;padding:2px 6px;border-radius:4px;font-size:.65em;font-weight:700;'
                + (isSeries ? 'background:rgba(156,39,176,.85);' : 'background:rgba(229,57,53,.85);')
                + 'color:#fff;">' + (isSeries ? 'TV' : 'MOVIE') + '</span>';

            // Status badge
            html += '<span style="position:absolute;top:6px;right:6px;padding:2px 6px;border-radius:4px;font-size:.6em;font-weight:700;'
                + (isAvailable ? 'background:rgba(76,175,80,.85);' : 'background:rgba(0,164,220,.85);')
                + 'color:#fff;">' + (isAvailable ? 'AVAILABLE' : 'COMING SOON') + '</span>';

            // Countdown
            if (countdown) {
                html += '<div style="position:absolute;bottom:0;left:0;right:0;padding:4px;text-align:center;font-size:.72em;font-weight:600;'
                    + 'background:rgba(0,0,0,.7);'
                    + (isAvailable ? 'color:#4caf50;' : 'color:#00a4dc;')
                    + '">' + esc(countdown) + '</div>';
            }

            html += '</div><div style="padding:8px;">'
                + '<div style="font-size:.82em;font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;" '
                + 'title="' + esc(item.Title) + '">' + esc(item.Title) + '</div>'
                + '<div style="font-size:.72em;opacity:.6;margin-top:2px;">' + dateStr + '</div>';

            if (item.CustomMessage) {
                html += '<div style="font-size:.7em;opacity:.8;margin-top:4px;padding:3px 6px;background:rgba(0,164,220,.1);'
                    + 'border-radius:4px;border-left:2px solid #00a4dc;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;" '
                    + 'title="' + esc(item.CustomMessage) + '">' + esc(item.CustomMessage) + '</div>';
            }

            if (item.Overview) {
                html += '<div style="font-size:.7em;opacity:.5;margin-top:4px;overflow:hidden;display:-webkit-box;'
                    + '-webkit-line-clamp:3;-webkit-box-orient:vertical;">'
                    + esc(item.Overview) + '</div>';
            }

            html += '</div></div>';
        });

        grid.innerHTML = html;

        // Bind click for Available items
        grid.querySelectorAll('.up-card').forEach(function (card) {
            if (card.dataset.available === '1') {
                card.addEventListener('click', function () {
                    var idx = parseInt(card.dataset.idx, 10);
                    if (!isNaN(idx) && filtered[idx]) {
                        navigateToLibraryItem(filtered[idx]);
                    }
                });
            }
        });
    }

    // Init
    function init() {
        var pg = getPage();
        if (!pg) return;

        var filterStatus = pg.querySelector('#filterStatus');
        var filterType = pg.querySelector('#filterType');
        if (filterStatus) filterStatus.addEventListener('change', renderGrid);
        if (filterType) filterType.addEventListener('change', renderGrid);

        loadItems();
    }

    var pgEl = getPage();
    if (pgEl) {
        init();
        pgEl.addEventListener('pageshow', function () { loadItems(); });
        pgEl.addEventListener('viewshow', function () { loadItems(); });
    }
})();
