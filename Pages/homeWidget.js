/**
 * Upcoming Media — Home section injection for Jellyfin.
 * Injects an "Upcoming" section as a regular row (like My Media / Continue Watching) on the home page.
 * Shows horizontal spotlight cards with poster, title, overview, genres, status, etc.
 *
 * - "Coming Soon" items show countdown + Notify button, NOT clickable.
 * - "Available" items are clickable → navigate to actual Jellyfin library item.
 */
(function () {
    'use strict';

    var SECTION_ID  = 'umSection';
    var STYLE_ID    = 'umStyle';
    var INJECTED    = false;
    var RETRIES     = 0;
    var MAX_RETRIES = 25;

    // ── helpers ──────────────────────────────────────────────
    function esc(s) {
        if (!s) return '';
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(s));
        return d.innerHTML;
    }
    function formatDate(iso) {
        if (!iso) return 'TBA';
        try {
            var d = new Date(iso);
            var opts = { year: 'numeric', month: 'short', day: 'numeric' };
            var str = d.toLocaleDateString(undefined, opts);
            if (d.getHours() !== 0 || d.getMinutes() !== 0) {
                str += ', ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
            }
            return str;
        }
        catch (e) { return 'TBA'; }
    }
    function daysUntil(iso) {
        if (!iso) return '';
        var ms = new Date(iso) - new Date();
        if (ms < 0) return 'Now available!';
        var totalSec = Math.floor(ms / 1000);
        var days = Math.floor(totalSec / 86400);
        if (days >= 2) return days + ' days';
        // Within 48 hours — show live countdown
        var hrs = Math.floor((totalSec % 86400) / 3600);
        var mins = Math.floor((totalSec % 3600) / 60);
        var secs = totalSec % 60;
        var pad = function (n) { return n < 10 ? '0' + n : '' + n; };
        if (days === 1) return '1d ' + pad(hrs) + 'h ' + pad(mins) + 'm ' + pad(secs) + 's';
        return pad(hrs) + 'h ' + pad(mins) + 'm ' + pad(secs) + 's';
    }

    // Live countdown ticker — updates every second
    var countdownInterval = null;
    function startCountdownTicker() {
        if (countdownInterval) return;
        countdownInterval = setInterval(function () {
            var els = document.querySelectorAll('.um-countdown-inline[data-release]');
            if (els.length === 0) return;
            els.forEach(function (el) {
                var iso = el.getAttribute('data-release');
                if (!iso) return;
                var text = daysUntil(iso);
                if (el.textContent !== text) el.textContent = text;
            });
        }, 1000);
    }

    // ── styles ───────────────────────────────────────────────
    function ensureStyles() {
        if (document.getElementById(STYLE_ID)) return;
        var s = document.createElement('style');
        s.id = STYLE_ID;
        s.textContent = [
            /* ───── section wrapper ───── */
            '#' + SECTION_ID + '{padding:0 3.3% 0.5em;margin-top:.2em;overflow:visible}',
            '#' + SECTION_ID + ' .um-header{display:flex;align-items:center;justify-content:space-between;margin-bottom:.8em}',
            '#' + SECTION_ID + ' .um-heading{font-size:1.4em;font-weight:700;color:#fff}',
            /* nav arrows */
            '.um-nav{display:flex;gap:6px}',
            '.um-nav button{background:rgba(255,255,255,.08);border:none;color:#fff;width:32px;height:32px;border-radius:50%;cursor:pointer;font-size:1.1em;display:flex;align-items:center;justify-content:center;transition:background .2s}',
            '.um-nav button:hover{background:rgba(255,255,255,.18)}',
            /* horizontal scroller */
            '.um-scroller{display:flex;gap:20px;overflow-x:auto;overflow-y:visible;scroll-behavior:smooth;padding:6px 0 10px;-ms-overflow-style:none;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.15) transparent}',
            '.um-scroller::-webkit-scrollbar{height:6px}',
            '.um-scroller::-webkit-scrollbar-thumb{background:rgba(255,255,255,.15);border-radius:3px}',

            /* ───── horizontal spotlight card ───── */
            '.um-card{flex-shrink:0;display:flex;width:580px;max-width:85vw;height:260px;border-radius:14px;overflow:hidden;background:rgba(255,255,255,.05);transition:transform .25s,box-shadow .25s;position:relative}',
            '.um-card:hover{transform:translateY(-3px);box-shadow:0 10px 30px rgba(0,0,0,.55)}',
            '.um-card-clickable{cursor:pointer}',

            /* poster (left) */
            '.um-poster{flex-shrink:0;width:175px;background:#1a1a1a;position:relative;overflow:hidden}',
            '.um-poster img{width:100%;height:100%;object-fit:cover;display:block}',
            '.um-noimg{width:100%;height:100%;display:flex;align-items:center;justify-content:center;color:#555;font-size:.8em}',
            '.um-badge{position:absolute;top:8px;left:8px;padding:2px 7px;border-radius:4px;font-size:.65em;font-weight:700;text-transform:uppercase;z-index:2}',
            '.um-badge-movie{background:rgba(229,57,53,.9);color:#fff}',
            '.um-badge-series{background:rgba(156,39,176,.9);color:#fff}',
            '.um-countdown-inline{padding:3px 10px;border-radius:5px;font-size:.75em;font-weight:600}',
            '.um-countdown-soon{color:#00a4dc;background:rgba(0,164,220,.1)}',
            '.um-countdown-avail{color:#4caf50;background:rgba(76,175,80,.1)}',

            /* details panel (right) */
            '.um-details{flex:1;padding:18px 20px;display:flex;flex-direction:column;overflow:hidden;position:relative}',
            '.um-backdrop{position:absolute;top:0;left:0;right:0;bottom:0;background-size:cover;background-position:center;opacity:.1;z-index:0}',
            '.um-details > *{position:relative;z-index:1}',

            /* title */
            '.um-title{font-size:1.25em;font-weight:800;line-height:1.2;margin-bottom:2px;color:#fff;text-shadow:0 2px 6px rgba(0,0,0,.4);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
            /* genres */
            '.um-genres{font-size:.72em;opacity:.5;margin-bottom:10px;font-style:italic;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
            /* status row */
            '.um-status-row{display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:10px}',
            '.um-status-label{padding:3px 10px;border-radius:5px;font-size:.75em;font-weight:700;text-transform:uppercase;letter-spacing:.4px}',
            '.um-status-soon{background:rgba(0,164,220,.18);color:#00a4dc;border:1px solid rgba(0,164,220,.35)}',
            '.um-status-avail{background:rgba(76,175,80,.18);color:#4caf50;border:1px solid rgba(76,175,80,.35)}',
            '.um-date{font-size:.8em;opacity:.55;font-weight:500}',
            '.um-notify-btn{position:absolute;bottom:14px;right:16px;padding:5px 16px;border-radius:16px;border:1.5px solid rgba(255,255,255,.5);background:transparent;color:#fff;font-size:.72em;font-weight:600;cursor:pointer;transition:all .2s;z-index:2}',
            '.um-notify-btn:hover{background:rgba(255,255,255,.12);border-color:#fff}',
            '.um-notify-btn.um-subscribed{border-color:#00a4dc;color:#00a4dc}',

            /* overview */
            '.um-overview{font-size:.82em;line-height:1.55;opacity:.75;margin-bottom:8px;display:-webkit-box;-webkit-line-clamp:4;-webkit-box-orient:vertical;overflow:hidden}',
            /* custom message */
            '.um-msg{font-size:.74em;display:flex;align-items:center;gap:8px;padding:8px 14px;background:linear-gradient(135deg,rgba(0,164,220,.12) 0%,rgba(156,39,176,.10) 100%);border-radius:8px;border:1px solid rgba(0,164,220,.2);color:rgba(255,255,255,.9);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;backdrop-filter:blur(4px);box-shadow:0 2px 12px rgba(0,164,220,.08);transition:all .3s ease}',
            '.um-msg:hover{background:linear-gradient(135deg,rgba(0,164,220,.18) 0%,rgba(156,39,176,.15) 100%);border-color:rgba(0,164,220,.35);box-shadow:0 4px 20px rgba(0,164,220,.15)}',
            '.um-msg-icon{flex-shrink:0;font-size:1.1em;filter:drop-shadow(0 0 3px rgba(0,164,220,.4))}',
            '.um-msg-text{overflow:hidden;text-overflow:ellipsis;font-weight:500;letter-spacing:.2px}',

            /* trailer play button on poster */
            '.um-trailer-btn{position:absolute;bottom:10px;right:10px;width:36px;height:36px;border-radius:50%;background:rgba(229,57,53,.9);border:2px solid rgba(255,255,255,.7);color:#fff;cursor:pointer;display:flex;align-items:center;justify-content:center;font-size:.85em;transition:all .3s ease;z-index:3;box-shadow:0 3px 12px rgba(0,0,0,.5);padding:0}',
            '.um-trailer-btn:hover{transform:scale(1.15);background:#e53935;box-shadow:0 5px 20px rgba(229,57,53,.6);border-color:#fff}',
            '.um-trailer-btn svg{width:16px;height:16px;fill:#fff;margin-left:2px}',

            /* trailer modal overlay */
            '.um-trailer-overlay{position:fixed;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,.88);z-index:100000;display:flex;align-items:center;justify-content:center;animation:umFadeIn .25s ease-out;backdrop-filter:blur(8px)}',
            '.um-trailer-modal{position:relative;width:90vw;max-width:960px;aspect-ratio:16/9;border-radius:16px;overflow:hidden;box-shadow:0 20px 60px rgba(0,0,0,.7);border:1px solid rgba(255,255,255,.1);animation:umScaleIn .3s ease-out}',
            '.um-trailer-modal iframe{width:100%;height:100%;border:none}',
            '.um-trailer-modal-close{position:absolute;top:-40px;right:0;background:rgba(255,255,255,.1);border:1px solid rgba(255,255,255,.2);color:#fff;width:34px;height:34px;border-radius:50%;cursor:pointer;font-size:1.2em;display:flex;align-items:center;justify-content:center;transition:all .2s;z-index:2}',
            '.um-trailer-modal-close:hover{background:rgba(229,57,53,.8);border-color:rgba(229,57,53,.8);transform:scale(1.1)}',
            '.um-trailer-modal-title{position:absolute;top:-38px;left:0;color:rgba(255,255,255,.8);font-size:.9em;font-weight:600}',
            '@keyframes umFadeIn{from{opacity:0}to{opacity:1}}',
            '@keyframes umScaleIn{from{opacity:0;transform:scale(.92)}to{opacity:1;transform:scale(1)}}',

            /* responsive - tablet */
            '@media (max-width:750px){.um-card{width:400px;height:230px}.um-poster{width:130px}.um-details{padding:14px 16px}.um-title{font-size:1.05em}.um-overview{-webkit-line-clamp:3}}',
            /* responsive - mobile */
            '@media (max-width:500px){.um-card{width:320px;max-width:92vw;height:180px;border-radius:10px}.um-poster{width:100px}.um-badge{top:4px;left:4px;padding:1px 5px;font-size:.55em}.um-countdown-inline{font-size:.6em;padding:2px 6px}.um-trailer-btn{width:28px;height:28px;bottom:6px;right:6px}.um-trailer-btn svg{width:12px;height:12px}.um-details{padding:10px 12px}.um-title{font-size:.9em;margin-bottom:1px}.um-genres{font-size:.62em;margin-bottom:6px}.um-status-row{gap:6px;margin-bottom:6px}.um-status-label{padding:2px 6px;font-size:.6em}.um-date{font-size:.65em}.um-notify-btn{bottom:8px;right:10px;padding:3px 10px;font-size:.6em}.um-overview{font-size:.72em;line-height:1.4;-webkit-line-clamp:2;margin-bottom:4px}.um-msg{font-size:.62em;padding:5px 8px;gap:5px}#umSection{padding:0 3% .3em}#umSection .um-heading{font-size:1.1em}.um-nav button{width:26px;height:26px;font-size:.9em}.um-scroller{gap:12px}.um-trailer-modal{width:95vw;border-radius:10px}}',

            /* empty state */
            '.um-empty{padding:2em;text-align:center;opacity:.4;font-size:.9em}',

            /* toast notification */
            '.um-toast-container{position:fixed;top:20px;right:20px;z-index:99999;display:flex;flex-direction:column;gap:10px;pointer-events:none}',
            '.um-toast{pointer-events:auto;background:linear-gradient(135deg,#1a1a2e,#16213e);border:1px solid rgba(0,164,220,.4);border-left:4px solid #00a4dc;border-radius:10px;padding:14px 18px;min-width:300px;max-width:420px;box-shadow:0 8px 32px rgba(0,0,0,.5);animation:umSlideIn .4s ease-out;display:flex;gap:12px;align-items:flex-start}',
            '.um-toast-icon{font-size:1.5em;flex-shrink:0;margin-top:2px}',
            '.um-toast-body{flex:1}',
            '.um-toast-title{font-weight:700;font-size:.95em;color:#fff;margin-bottom:4px}',
            '.um-toast-msg{font-size:.82em;opacity:.7;line-height:1.4}',
            '.um-toast-actions{display:flex;gap:8px;margin-top:8px}',
            '.um-toast-btn{padding:4px 12px;border-radius:6px;border:1px solid rgba(255,255,255,.2);background:transparent;color:#fff;font-size:.75em;cursor:pointer;transition:all .2s}',
            '.um-toast-btn:hover{background:rgba(255,255,255,.1)}',
            '.um-toast-btn-primary{background:rgba(0,164,220,.2);border-color:rgba(0,164,220,.5);color:#00a4dc}',
            '.um-toast-btn-primary:hover{background:rgba(0,164,220,.35)}',
            '.um-toast-close{position:absolute;top:6px;right:8px;background:none;border:none;color:rgba(255,255,255,.4);cursor:pointer;font-size:1.1em;padding:2px}',
            '.um-toast-close:hover{color:#fff}',
            '@keyframes umSlideIn{from{transform:translateX(100%);opacity:0}to{transform:translateX(0);opacity:1}}',
            '@keyframes umSlideOut{from{transform:translateX(0);opacity:1}to{transform:translateX(100%);opacity:0}}',

            /* playback interception overlay for dummy files */
            '.um-playback-overlay{position:relative;width:100%;min-height:340px;border-radius:16px;overflow:hidden;margin-bottom:2em;box-shadow:0 12px 48px rgba(0,0,0,.6)}',
            '.um-playback-overlay-bg{position:absolute;inset:0;background-size:cover;background-position:center;filter:blur(20px) brightness(.35);transform:scale(1.1);z-index:0}',
            '.um-playback-overlay-content{position:relative;z-index:1;display:flex;gap:28px;padding:36px;align-items:center}',
            '.um-playback-overlay-poster{flex-shrink:0;width:180px}',
            '.um-playback-overlay-poster img{width:100%;border-radius:12px;box-shadow:0 8px 32px rgba(0,0,0,.5)}',
            '.um-playback-overlay-info{flex:1;color:#fff}',
            '.um-playback-overlay-badge{display:inline-block;padding:4px 16px;border-radius:20px;background:linear-gradient(135deg,#ff6b35,#f7c948);color:#000;font-weight:800;font-size:.8em;letter-spacing:.5px;margin-bottom:12px;text-transform:uppercase}',
            '.um-playback-overlay-title{font-size:2em;font-weight:800;margin-bottom:8px;text-shadow:0 2px 8px rgba(0,0,0,.5)}',
            '.um-playback-overlay-genres{font-size:.85em;opacity:.6;margin-bottom:12px}',
            '.um-playback-overlay-timer{font-size:1.4em;margin-bottom:16px;padding:12px 20px;background:rgba(0,0,0,.4);border-radius:12px;display:inline-block;backdrop-filter:blur(8px);border:1px solid rgba(255,255,255,.1)}',
            '.um-playback-overlay-msg{font-size:.9em;opacity:.8;margin-bottom:14px;padding:8px 14px;background:rgba(255,255,255,.06);border-radius:8px;border-left:3px solid #00a4dc}',
            '.um-playback-overlay-trailer{padding:10px 24px;border-radius:30px;border:2px solid #e50914;background:rgba(229,9,20,.15);color:#fff;font-size:.95em;font-weight:600;cursor:pointer;transition:all .25s}',
            '.um-playback-overlay-trailer:hover{background:#e50914;transform:scale(1.05);box-shadow:0 4px 20px rgba(229,9,20,.4)}',
            '@keyframes umPulseGlow{0%{box-shadow:0 0 0 0 rgba(0,164,220,.4)}50%{box-shadow:0 0 30px 10px rgba(0,164,220,.3)}100%{box-shadow:0 0 0 0 rgba(0,164,220,0)}}',
            '@media(max-width:600px){.um-playback-overlay-content{flex-direction:column;padding:20px;gap:16px;text-align:center}.um-playback-overlay-poster{width:120px}.um-playback-overlay-title{font-size:1.4em}.um-playback-overlay-timer{font-size:1em}}'
        ].join('\n');
        document.head.appendChild(s);
    }

    // ── find the sections container (where My Media / Continue Watching live) ──
    // CRITICAL: We must NEVER inject into or near the hero/spotlight.
    // We wait until a real native section (My Media, Continue Watching, etc.)
    // exists, then use its parent as our container.

    function isHeroOrSpotlight(el) {
        if (!el) return false;
        var cn = (el.className || '').toLowerCase();
        var id = (el.id || '').toLowerCase();
        return cn.indexOf('hero') !== -1 || cn.indexOf('spotlight') !== -1 ||
               cn.indexOf('slideshow') !== -1 || cn.indexOf('banner') !== -1 ||
               id.indexOf('hero') !== -1 || id.indexOf('spotlight') !== -1;
    }

    function findSectionsContainer() {
        // ONLY strategy: Find a real native section heading (My Media, Continue Watching,
        // Latest, Recently Added, etc.) and use the parent of its section wrapper.
        // This guarantees we land in the same container as native sections.

        var homePage = document.querySelector('.homePage, [data-type="homeview"]');
        if (!homePage) return null;

        // Look for section title elements that belong to native Jellyfin sections
        var candidates = homePage.querySelectorAll('.sectionTitle, h2, h3');
        for (var i = 0; i < candidates.length; i++) {
            var txt = (candidates[i].textContent || '').trim().toLowerCase();
            // Match known native section names
            if (txt.indexOf('my media') !== -1 || txt.indexOf('continue') !== -1 ||
                txt.indexOf('latest') !== -1 || txt.indexOf('recently') !== -1 ||
                txt.indexOf('next up') !== -1 || txt.indexOf('libraries') !== -1) {

                // Walk up to the section wrapper
                var sectionEl = candidates[i].closest('.verticalSection, .homePageSection, section');
                if (!sectionEl) sectionEl = candidates[i].parentElement;
                if (!sectionEl || !sectionEl.parentElement) continue;

                var container = sectionEl.parentElement;

                // SAFETY: Make sure this container is NOT the hero/spotlight
                if (isHeroOrSpotlight(container)) continue;
                // Also make sure the container itself isn't the homePage root
                // (we want the intermediate sections wrapper)
                if (container === homePage && sectionEl.parentElement) {
                    // This is fine — some Jellyfin versions put sections directly in homePage
                }

                console.log('[UpcomingMedia] Found sections container via "' + txt + '" section, container:', container.tagName, container.className);
                return { container: container, firstSection: sectionEl };
            }
        }

        return null;
    }

    function findInsertionPoint() {
        var result = findSectionsContainer();
        if (!result) {
            console.log('[UpcomingMedia] No native sections found yet');
            return null;
        }

        // Insert BEFORE the first native section in that container.
        // This puts "Upcoming" at the top of the sections area,
        // completely separate from the hero above.
        return { parent: result.container, before: result.firstSection };
    }

    // ── inject section ───────────────────────────────────────
    function injectSection() {
        // Check if already in DOM
        var existing = document.getElementById(SECTION_ID);
        if (existing && document.body.contains(existing)) {
            INJECTED = true;
            return;
        }
        // Reset if destroyed by SPA
        if (INJECTED && !existing) {
            console.log('[UpcomingMedia] Section destroyed by SPA re-render, re-injecting...');
            INJECTED = false;
        }

        var insertion = findInsertionPoint();
        if (!insertion) {
            RETRIES++;
            if (RETRIES < MAX_RETRIES) {
                console.log('[UpcomingMedia] Insertion point not found, retry ' + RETRIES + '/' + MAX_RETRIES);
                setTimeout(injectSection, 500);
            } else {
                console.warn('[UpcomingMedia] Gave up finding insertion point after ' + MAX_RETRIES + ' retries');
            }
            return;
        }

        ensureStyles();

        // Create the section
        var section = document.createElement('div');
        section.id = SECTION_ID;
        section.className = 'verticalSection';
        section.innerHTML =
            '<div class="um-header">' +
                '<h2 class="um-heading">Upcoming</h2>' +
                '<div class="um-nav">' +
                    '<button class="um-nav-left" title="Scroll left">&#10094;</button>' +
                    '<button class="um-nav-right" title="Scroll right">&#10095;</button>' +
                '</div>' +
            '</div>' +
            '<div class="um-scroller"><p style="opacity:.4">Loading&hellip;</p></div>';

        // Insert into page
        insertion.parent.insertBefore(section, insertion.before);
        console.log('[UpcomingMedia] Section injected as first section row');

        // Wire up nav arrows
        var scroller = section.querySelector('.um-scroller');
        section.querySelector('.um-nav-left').addEventListener('click', function () {
            scroller.scrollBy({ left: -400, behavior: 'smooth' });
        });
        section.querySelector('.um-nav-right').addEventListener('click', function () {
            scroller.scrollBy({ left: 400, behavior: 'smooth' });
        });

        INJECTED = true;
        RETRIES = 0;

        // Fetch and render items
        fetchAndRender();
    }

    // ── navigate to Jellyfin library item (for Available items) ──
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
                var pids = items[i].ProviderIds || {};
                if (item.TmdbId && pids.Tmdb === String(item.TmdbId)) { match = items[i]; break; }
            }
            if (!match) {
                for (var j = 0; j < items.length; j++) {
                    if (items[j].Name && items[j].Name.toLowerCase() === item.Title.toLowerCase()) { match = items[j]; break; }
                }
            }
            if (!match && items.length > 0) match = items[0];
            if (match && match.Id) {
                var url = 'details?id=' + match.Id;
                if (typeof Emby !== 'undefined' && Emby.Page && Emby.Page.show) {
                    Emby.Page.show('/' + url);
                } else {
                    window.location.hash = '#!/' + url;
                }
            }
        }).catch(function () {});
    }

    // ── fetch & render ───────────────────────────────────────
    function fetchAndRender() {
        if (typeof ApiClient === 'undefined' || !ApiClient.getUrl) {
            // ApiClient not ready yet, retry
            setTimeout(fetchAndRender, 1000);
            return;
        }
        var url = ApiClient.getUrl('UpcomingMedia/Items');
        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (data) {
            if (typeof data === 'string') { try { data = JSON.parse(data); } catch (e) { data = []; } }
            if (!Array.isArray(data)) data = [];
            var items = data.filter(function (i) { return i.Status === 'ComingSoon' || i.Status === 'Available'; });
            renderCards(items);
        }).catch(function () {
            var scroller = document.querySelector('#' + SECTION_ID + ' .um-scroller');
            if (scroller) scroller.innerHTML = '<p class="um-empty">Failed to load upcoming items.</p>';
        });
    }

    function renderCards(items) {
        var scroller = document.querySelector('#' + SECTION_ID + ' .um-scroller');
        if (!scroller) return;

        if (items.length === 0) {
            scroller.innerHTML = '<p class="um-empty">No upcoming items yet.</p>';
            return;
        }

        var html = '';
        items.forEach(function (item, idx) {
            var date = item.AvailableDate || item.ReleaseDate;
            var dateStr = formatDate(date);
            var countdown = daysUntil(date);
            var isAvail = item.Status === 'Available';
            var isSeries = item.MediaType === 'Series';
            var overview = item.Overview || '';
            var genres = item.Genres || '';

            html += '<div class="um-card' + (isAvail ? ' um-card-clickable' : '') + '" '
                + 'data-idx="' + idx + '">';

            // ── Poster (left) ──
            html += '<div class="um-poster">';
            if (item.PosterUrl) {
                html += '<img src="' + esc(item.PosterUrl) + '" alt="' + esc(item.Title) + '" loading="lazy" '
                    + 'onerror="this.outerHTML=\'<div class=um-noimg>No Image</div>\'" />';
            } else {
                html += '<div class="um-noimg">No Image</div>';
            }
            html += '<span class="um-badge ' + (isSeries ? 'um-badge-series' : 'um-badge-movie') + '">'
                + (isSeries ? 'TV' : 'MOVIE') + '</span>';
            if (item.TrailerUrl) {
                html += '<button class="um-trailer-btn" data-trailer="' + esc(item.TrailerUrl) + '" data-title="' + esc(item.Title) + '" title="Watch Trailer">'
                    + '<svg viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>'
                    + '</button>';
            }
            html += '</div>';

            // ── Details (right) ──
            html += '<div class="um-details">';
            if (item.BackdropUrl) {
                html += '<div class="um-backdrop" style="background-image:url(\'' + esc(item.BackdropUrl) + '\')"></div>';
            }
            html += '<div class="um-title" title="' + esc(item.Title) + '">' + esc(item.Title) + '</div>';
            if (genres) {
                html += '<div class="um-genres">' + esc(genres) + '</div>';
            }
            html += '<div class="um-status-row">';
            html += '<span class="um-status-label ' + (isAvail ? 'um-status-avail' : 'um-status-soon') + '">'
                + (isAvail ? 'Available' : 'Coming Soon') + '</span>';
            html += '<span class="um-date">' + dateStr + '</span>';
            if (countdown) {
                html += '<span class="um-countdown-inline ' + (isAvail ? 'um-countdown-avail' : 'um-countdown-soon') + '"'
                    + (date && !isAvail ? ' data-release="' + esc(date) + '"' : '')
                    + '>' + esc(countdown) + '</span>';
            }
            html += '</div>';
            if (overview) {
                html += '<div class="um-overview">' + esc(overview) + '</div>';
            }
            if (item.CustomMessage) {
                html += '<div class="um-msg" title="' + esc(item.CustomMessage) + '">'
                    + '<span class="um-msg-icon">&#x1F4E2;</span>'
                    + '<span class="um-msg-text">' + esc(item.CustomMessage) + '</span>'
                    + '</div>';
            }
            if (!isAvail) {
                html += '<button class="um-notify-btn" data-itemid="' + esc(String(item.Id)) + '" data-title="' + esc(item.Title) + '">Notify</button>';
            }
            html += '</div>'; // .um-details
            html += '</div>'; // .um-card
        });

        scroller.innerHTML = html;

        // Bind Available card clicks
        scroller.querySelectorAll('.um-card-clickable').forEach(function (card) {
            card.addEventListener('click', function (e) {
                if (e.target.classList.contains('um-notify-btn')) return;
                if (e.target.closest('.um-trailer-btn')) return;
                var idx = parseInt(card.dataset.idx, 10);
                if (!isNaN(idx) && items[idx]) navigateToLibraryItem(items[idx]);
            });
        });

        // Bind Trailer buttons
        scroller.querySelectorAll('.um-trailer-btn').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                var trailerUrl = btn.dataset.trailer;
                var title = btn.dataset.title || '';
                if (trailerUrl) openTrailerModal(trailerUrl, title);
            });
        });

        // Bind Notify buttons — calls real API
        scroller.querySelectorAll('.um-notify-btn').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                var itemId = btn.dataset.itemid;
                if (!itemId) return;
                subscribeToItem(itemId, btn);
            });
        });

        // Mark already-subscribed items
        loadUserSubscriptions(scroller, items);

        // Start live countdown ticker
        startCountdownTicker();
    }

    // ── Trailer modal ────────────────────────────────────────
    function extractYouTubeId(url) {
        if (!url) return null;
        // Match youtube.com/watch?v=ID, youtu.be/ID, youtube.com/embed/ID
        var m = url.match(/(?:youtube\.com\/(?:watch\?v=|embed\/)|youtu\.be\/)([A-Za-z0-9_-]{11})/);
        return m ? m[1] : null;
    }

    function openTrailerModal(trailerUrl, title) {
        // Close any existing modal
        closeTrailerModal();

        var videoId = extractYouTubeId(trailerUrl);
        if (!videoId) {
            // Not a YouTube URL — open in new tab
            window.open(trailerUrl, '_blank');
            return;
        }

        ensureStyles();

        var overlay = document.createElement('div');
        overlay.id = 'umTrailerOverlay';
        overlay.className = 'um-trailer-overlay';
        overlay.innerHTML =
            '<div class="um-trailer-modal">' +
                '<span class="um-trailer-modal-title">' + esc(title) + '</span>' +
                '<button class="um-trailer-modal-close" title="Close">&times;</button>' +
                '<iframe src="https://www.youtube.com/embed/' + videoId + '?autoplay=1&rel=0&modestbranding=1" ' +
                    'allow="autoplay; encrypted-media; picture-in-picture" allowfullscreen></iframe>' +
            '</div>';

        // Close on overlay background click
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) closeTrailerModal();
        });

        // Close button
        overlay.querySelector('.um-trailer-modal-close').addEventListener('click', function () {
            closeTrailerModal();
        });

        document.body.appendChild(overlay);

        // Close on Escape key
        document.addEventListener('keydown', trailerEscHandler);
    }

    function closeTrailerModal() {
        var overlay = document.getElementById('umTrailerOverlay');
        if (overlay) {
            overlay.style.animation = 'none';
            overlay.style.opacity = '0';
            overlay.style.transition = 'opacity .2s ease-out';
            setTimeout(function () { if (overlay.parentElement) overlay.remove(); }, 200);
        }
        document.removeEventListener('keydown', trailerEscHandler);
    }

    function trailerEscHandler(e) {
        if (e.key === 'Escape') closeTrailerModal();
    }

    // ── Notification API helpers ──────────────────────────────
    function subscribeToItem(itemId, btn) {
        if (typeof ApiClient === 'undefined') return;
        var url = ApiClient.getUrl('UpcomingMedia/Notifications/Subscribe/' + itemId);
        ApiClient.ajax({ url: url, type: 'POST', dataType: 'json' }).then(function () {
            btn.textContent = '\u2713 Subscribed';
            btn.style.borderColor = '#00a4dc';
            btn.style.color = '#00a4dc';
            btn.disabled = true;
            btn.classList.add('um-subscribed');
        }).catch(function () {
            btn.textContent = 'Error';
            setTimeout(function () { btn.textContent = 'Notify'; btn.disabled = false; }, 2000);
        });
    }

    function loadUserSubscriptions(scroller, items) {
        if (typeof ApiClient === 'undefined') return;
        var url = ApiClient.getUrl('UpcomingMedia/Notifications/Subscriptions');
        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (subs) {
            if (!Array.isArray(subs)) return;
            var subSet = {};
            subs.forEach(function (id) { subSet[id] = true; });
            scroller.querySelectorAll('.um-notify-btn').forEach(function (btn) {
                if (subSet[btn.dataset.itemid]) {
                    btn.textContent = '\u2713 Subscribed';
                    btn.style.borderColor = '#00a4dc';
                    btn.style.color = '#00a4dc';
                    btn.disabled = true;
                    btn.classList.add('um-subscribed');
                }
            });
        }).catch(function () {});
    }

    // ── Toast notifications ──────────────────────────────────
    function getToastContainer() {
        var c = document.getElementById('umToastContainer');
        if (!c) {
            c = document.createElement('div');
            c.id = 'umToastContainer';
            c.className = 'um-toast-container';
            document.body.appendChild(c);
        }
        return c;
    }

    function showToast(title, message, itemId) {
        ensureStyles();
        var container = getToastContainer();
        var toast = document.createElement('div');
        toast.className = 'um-toast';
        toast.style.position = 'relative';
        toast.innerHTML =
            '<div class="um-toast-icon">\uD83C\uDF89</div>' +
            '<div class="um-toast-body">' +
                '<div class="um-toast-title">' + esc(title) + '</div>' +
                '<div class="um-toast-msg">' + esc(message) + '</div>' +
                '<div class="um-toast-actions">' +
                    '<button class="um-toast-btn um-toast-btn-primary" data-action="view" data-itemid="' + esc(itemId) + '">View</button>' +
                    '<button class="um-toast-btn" data-action="dismiss" data-itemid="' + esc(itemId) + '">Dismiss</button>' +
                '</div>' +
            '</div>' +
            '<button class="um-toast-close">&times;</button>';

        // Close button
        toast.querySelector('.um-toast-close').addEventListener('click', function () {
            dismissToast(toast, itemId);
        });

        // Dismiss button
        toast.querySelector('[data-action="dismiss"]').addEventListener('click', function () {
            dismissToast(toast, itemId);
        });

        // View button — navigate to the item
        toast.querySelector('[data-action="view"]').addEventListener('click', function () {
            dismissToast(toast, itemId);
            // Find the item data and navigate
            if (typeof ApiClient !== 'undefined') {
                var itemUrl = ApiClient.getUrl('UpcomingMedia/Items/' + itemId);
                ApiClient.ajax({ url: itemUrl, type: 'GET', dataType: 'json' }).then(function (item) {
                    if (item) navigateToLibraryItem(item);
                }).catch(function () {});
            }
        });

        container.appendChild(toast);

        // Auto-dismiss after 15 seconds
        setTimeout(function () {
            if (toast.parentElement) dismissToast(toast, itemId);
        }, 15000);
    }

    function dismissToast(toast, itemId) {
        toast.style.animation = 'umSlideOut .3s ease-in forwards';
        setTimeout(function () { if (toast.parentElement) toast.remove(); }, 300);
        // Call dismiss API
        if (typeof ApiClient !== 'undefined' && itemId) {
            var url = ApiClient.getUrl('UpcomingMedia/Notifications/Dismiss/' + itemId);
            ApiClient.ajax({ url: url, type: 'POST' }).catch(function () {});
        }
    }

    function checkPendingNotifications() {
        if (typeof ApiClient === 'undefined' || !ApiClient.getUrl) {
            setTimeout(checkPendingNotifications, 2000);
            return;
        }
        var url = ApiClient.getUrl('UpcomingMedia/Notifications/Pending');
        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (pending) {
            if (!Array.isArray(pending) || pending.length === 0) return;
            console.log('[UpcomingMedia] ' + pending.length + ' pending notification(s)');
            pending.forEach(function (sub, i) {
                // Stagger toasts
                setTimeout(function () {
                    showToast(
                        sub.ItemTitle + ' is now available!',
                        'The title you subscribed to is now available on the server.',
                        sub.ItemId
                    );
                }, i * 800);
            });
        }).catch(function () {});
    }

    // ── Playback interception for dummy files ────────────────
    var _upcomingItemsCache = null;
    var _lastCacheFetch = 0;
    var _playbackOverlayActive = false;

    function fetchUpcomingItemsCache(callback) {
        var now = Date.now();
        if (_upcomingItemsCache && (now - _lastCacheFetch) < 30000) {
            callback(_upcomingItemsCache);
            return;
        }
        if (typeof ApiClient === 'undefined' || !ApiClient.getUrl) return;
        var url = ApiClient.getUrl('UpcomingMedia/Items');
        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (items) {
            _upcomingItemsCache = items || [];
            _lastCacheFetch = Date.now();
            callback(_upcomingItemsCache);
        }).catch(function () {});
    }

    function findMatchingUpcomingItem(jellyfinItem, upcomingItems) {
        if (!jellyfinItem || !upcomingItems) return null;
        var providerIds = jellyfinItem.ProviderIds || {};
        for (var i = 0; i < upcomingItems.length; i++) {
            var ui = upcomingItems[i];
            if (ui.Status !== 'ComingSoon') continue;
            if (!ui.DummyCreated) continue;
            // Match by TMDb ID
            if (ui.TmdbId && providerIds.Tmdb && String(ui.TmdbId) === String(providerIds.Tmdb)) return ui;
            // Match by IMDb ID
            if (ui.ImdbId && providerIds.Imdb && ui.ImdbId === providerIds.Imdb) return ui;
        }
        return null;
    }

    function buildCountdownHtml(upcomingItem) {
        var targetDate = upcomingItem.AvailableDate || upcomingItem.ReleaseDate;
        var html = '<div class="um-playback-overlay">';
        html += '<div class="um-playback-overlay-bg" style="background-image:url(' + esc(upcomingItem.BackdropUrl || upcomingItem.PosterUrl || '') + ')"></div>';
        html += '<div class="um-playback-overlay-content">';
        html += '<div class="um-playback-overlay-poster">';
        if (upcomingItem.PosterUrl) {
            html += '<img src="' + esc(upcomingItem.PosterUrl) + '" alt="' + esc(upcomingItem.Title) + '" />';
        }
        html += '</div>';
        html += '<div class="um-playback-overlay-info">';
        html += '<div class="um-playback-overlay-badge">COMING SOON</div>';
        html += '<div class="um-playback-overlay-title">' + esc(upcomingItem.Title) + '</div>';
        if (upcomingItem.Genres) {
            html += '<div class="um-playback-overlay-genres">' + esc(upcomingItem.Genres) + '</div>';
        }
        if (targetDate) {
            html += '<div class="um-playback-overlay-timer" data-target="' + esc(targetDate) + '"></div>';
        }
        if (upcomingItem.CustomMessage) {
            html += '<div class="um-playback-overlay-msg">\uD83D\uDCE2 ' + esc(upcomingItem.CustomMessage) + '</div>';
        }
        if (upcomingItem.TrailerUrl) {
            html += '<button class="um-playback-overlay-trailer" data-trailer="' + esc(upcomingItem.TrailerUrl) + '">\u25B6 Watch Trailer</button>';
        }
        html += '</div></div></div>';
        return html;
    }

    function injectPlaybackOverlay(upcomingItem) {
        if (_playbackOverlayActive) return;
        ensureStyles();

        var existing = document.querySelector('.um-playback-overlay');
        if (existing) existing.remove();

        var div = document.createElement('div');
        div.innerHTML = buildCountdownHtml(upcomingItem);
        var overlay = div.firstElementChild;

        // Find the item detail page's main content area
        var detailPage = document.querySelector('.itemDetailPage, .detailPage, [data-type="item-detail"]');
        var target = detailPage || document.querySelector('.page:not(.hide), [data-role="page"]:not(.hide)');
        if (!target) target = document.body;

        // Insert at the top of the content area
        var contentArea = target.querySelector('.detailPageContent, .detailPagePrimaryContent, .content-primary');
        if (contentArea) {
            contentArea.insertBefore(overlay, contentArea.firstChild);
        } else {
            target.appendChild(overlay);
        }

        _playbackOverlayActive = true;

        // Start countdown timer
        var timerEl = overlay.querySelector('.um-playback-overlay-timer');
        if (timerEl) {
            var targetStr = timerEl.dataset.target;
            updateOverlayTimer(timerEl, targetStr);
            timerEl._interval = setInterval(function () {
                updateOverlayTimer(timerEl, targetStr);
            }, 1000);
        }

        // Trailer button
        var trailerBtn = overlay.querySelector('.um-playback-overlay-trailer');
        if (trailerBtn) {
            trailerBtn.addEventListener('click', function () {
                openTrailerModal(trailerBtn.dataset.trailer, upcomingItem.Title);
            });
        }

        // Intercept play buttons on this page
        interceptPlayButtons(upcomingItem);
    }

    function updateOverlayTimer(el, targetDateStr) {
        var now = new Date();
        var target = new Date(targetDateStr);
        var diff = target - now;
        if (diff <= 0) {
            el.innerHTML = '<span style="color:#66bb6a;font-weight:700;">🎉 Available Now!</span>';
            if (el._interval) clearInterval(el._interval);
            // Refresh page after a short delay
            setTimeout(function () { location.reload(); }, 3000);
            return;
        }
        var days = Math.floor(diff / 86400000);
        var hours = Math.floor((diff % 86400000) / 3600000);
        var mins = Math.floor((diff % 3600000) / 60000);
        var secs = Math.floor((diff % 60000) / 1000);
        var parts = [];
        if (days > 0) parts.push(days + 'd');
        parts.push(String(hours).padStart(2, '0') + 'h');
        parts.push(String(mins).padStart(2, '0') + 'm');
        parts.push(String(secs).padStart(2, '0') + 's');
        el.innerHTML = '\u23F3 Available in <strong>' + parts.join(' ') + '</strong>';
    }

    function interceptPlayButtons(upcomingItem) {
        // Observe the page for play buttons and disable them
        var interceptor = new MutationObserver(function () {
            var playBtns = document.querySelectorAll('.btnPlay, .detailButton-play, [data-action="play"], [data-action="resume"]');
            playBtns.forEach(function (btn) {
                if (btn._umIntercepted) return;
                btn._umIntercepted = true;
                btn.addEventListener('click', function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    e.stopImmediatePropagation();
                    // Show a message instead of playing
                    var overlay = document.querySelector('.um-playback-overlay');
                    if (overlay) {
                        overlay.style.animation = 'none';
                        overlay.offsetHeight; // trigger reflow
                        overlay.style.animation = 'umPulseGlow .6s ease';
                    }
                }, true); // capture phase to beat Jellyfin's handler
            });
        });
        interceptor.observe(document.body, { childList: true, subtree: true });
        // Run once immediately
        interceptor.takeRecords();
        var playBtns = document.querySelectorAll('.btnPlay, .detailButton-play, [data-action="play"], [data-action="resume"]');
        playBtns.forEach(function (btn) {
            if (btn._umIntercepted) return;
            btn._umIntercepted = true;
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
            }, true);
        });
        // Store interceptor so we can disconnect later
        window._umPlaybackInterceptor = interceptor;
    }

    function removePlaybackOverlay() {
        _playbackOverlayActive = false;
        var overlay = document.querySelector('.um-playback-overlay');
        if (overlay) {
            var timerEl = overlay.querySelector('.um-playback-overlay-timer');
            if (timerEl && timerEl._interval) clearInterval(timerEl._interval);
            overlay.remove();
        }
        if (window._umPlaybackInterceptor) {
            window._umPlaybackInterceptor.disconnect();
            window._umPlaybackInterceptor = null;
        }
    }

    function checkItemDetailPage() {
        var href = window.location.href;
        // Jellyfin item detail pages have patterns like #!/item?id=xxx or #/details?id=xxx
        var itemIdMatch = href.match(/[?&]id=([a-f0-9-]+)/i);
        if (!itemIdMatch) {
            removePlaybackOverlay();
            return;
        }
        var jellyfinItemId = itemIdMatch[1];

        // Get the item from Jellyfin to read its provider IDs
        if (typeof ApiClient === 'undefined' || !ApiClient.getUrl) return;
        var itemUrl = ApiClient.getUrl('Users/' + ApiClient.getCurrentUserId() + '/Items/' + jellyfinItemId);
        ApiClient.ajax({ url: itemUrl, type: 'GET', dataType: 'json' }).then(function (jellyfinItem) {
            fetchUpcomingItemsCache(function (upcomingItems) {
                var match = findMatchingUpcomingItem(jellyfinItem, upcomingItems);
                if (match) {
                    console.log('[UpcomingMedia] Dummy item detected: ' + match.Title + ' — injecting playback overlay');
                    // Small delay to let Jellyfin render the detail page
                    setTimeout(function () { injectPlaybackOverlay(match); }, 500);
                } else {
                    removePlaybackOverlay();
                }
            });
        }).catch(function () {
            removePlaybackOverlay();
        });
    }

    // ── lifecycle ────────────────────────────────────────────
    function isHomePage() {
        var hash = window.location.hash || '';
        var path = window.location.pathname || '';

        // Hash-based SPA routing (most common for Jellyfin)
        // Positive match: hash must be exactly /home (not /homevideos, /dashboard, etc.)
        if (hash === '#/home' || hash === '#!/home') return true;
        if (hash.match(/^#!?\/home$/)) return true;

        // No hash = default SPA route = home page
        if ((hash === '' || hash === '#' || hash === '#!/' || hash === '#/')
            && (path.endsWith('/web/index.html') || path.endsWith('/web/') || path.endsWith('/web') || path === '/')) return true;

        // DOM-based detection (fallback — most reliable)
        var homePage = document.querySelector('.homePage, [data-type="homeview"]');
        if (homePage) return true;

        return false;
    }

    function onPageChange() {
        console.log('[UpcomingMedia] Page change detected, URL:', window.location.href);
        if (isHomePage()) {
            console.log('[UpcomingMedia] On home page, will inject section');
            RETRIES = 0;
            INJECTED = false;
            setTimeout(injectSection, 500);
            setTimeout(injectSection, 1200);
            setTimeout(injectSection, 2500);
            setTimeout(injectSection, 4000);
            setTimeout(injectSection, 6000);
            // Check for pending notifications on every home page visit
            setTimeout(checkPendingNotifications, 2000);
            // Clean up any playback overlay from item detail pages
            removePlaybackOverlay();
        } else {
            var old = document.getElementById(SECTION_ID);
            if (old) old.remove();
            INJECTED = false;
            // Check if this is an item detail page with a dummy file
            setTimeout(checkItemDetailPage, 800);
        }
    }

    // Jellyfin SPA navigation events
    document.addEventListener('viewshow', onPageChange);
    document.addEventListener('pageshow', onPageChange);
    window.addEventListener('hashchange', onPageChange);
    window.addEventListener('popstate', onPageChange);

    // MutationObserver for SPA re-renders
    var debounce = null;
    var observer = new MutationObserver(function () {
        if (!isHomePage()) return;
        var exists = document.getElementById(SECTION_ID) && document.body.contains(document.getElementById(SECTION_ID));
        if (exists) return;
        if (debounce) return;
        debounce = setTimeout(function () {
            debounce = null;
            if (!document.getElementById(SECTION_ID) && isHomePage()) {
                console.log('[UpcomingMedia] Observer: section missing, re-injecting...');
                INJECTED = false;
                injectSection();
            }
        }, 600);
    });

    function startObserver() {
        var target = document.querySelector('.mainAnimatedPages, .skinBody, #main, main, body');
        if (target) {
            observer.observe(target, { childList: true, subtree: true });
            console.log('[UpcomingMedia] Observer started on:', target.tagName, target.className);
        } else {
            observer.observe(document.body, { childList: true, subtree: true });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { startObserver(); setTimeout(onPageChange, 1000); setTimeout(checkPendingNotifications, 3000); });
    } else {
        startObserver();
        setTimeout(onPageChange, 800);
        setTimeout(onPageChange, 2000);
        setTimeout(checkPendingNotifications, 3000);
    }

    console.log('[UpcomingMedia] Home section widget loaded v4');
})();
