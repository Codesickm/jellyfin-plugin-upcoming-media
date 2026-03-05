/**
 * Upcoming Media — Admin configuration page logic.
 * Handles settings, search, add (with fallback), edit/update, delete.
 */
(function () {
    'use strict';

    console.log('[UpcomingMedia] configPage.js loaded');

    var pluginId = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
    var lastSearchResults = []; // cache for fallback add

    function pg() {
        return document.querySelector('#UpcomingMediaConfigPage');
    }

    function showStatus(msg, isError) {
        var el = pg().querySelector('#statusMsg');
        if (!el) return;
        el.textContent = msg;
        el.style.display = 'block';
        el.style.background = isError ? 'rgba(229,57,53,.15)' : 'rgba(76,175,80,.15)';
        el.style.color = isError ? '#ef5350' : '#66bb6a';
        clearTimeout(el._timer);
        el._timer = setTimeout(function () { el.style.display = 'none'; }, 4000);
    }

    // ── Config ────────────────────────────────────────────────
    function loadConfig() {
        var p = pg(); if (!p) return;
        ApiClient.getPluginConfiguration(pluginId).then(function (c) {
            p.querySelector('#chkShowOnHomePage').checked = c.ShowOnHomePage !== false;
            p.querySelector('#txtMaxItems').value = c.MaxItemsOnHomePage || 20;
            p.querySelector('#chkEnableReminders').checked = c.EnableReminders !== false;
            p.querySelector('#txtTmdbApiKey').value = c.TmdbApiKey || '';
            p.querySelector('#txtLibraryPath').value = c.UpcomingMediaLibraryPath || '';
        }).catch(function () { });
    }

    function saveConfig() {
        var p = pg(); if (!p) return;
        ApiClient.getPluginConfiguration(pluginId).then(function (c) {
            c.ShowOnHomePage = p.querySelector('#chkShowOnHomePage').checked;
            c.MaxItemsOnHomePage = parseInt(p.querySelector('#txtMaxItems').value, 10) || 20;
            c.EnableReminders = p.querySelector('#chkEnableReminders').checked;
            c.TmdbApiKey = p.querySelector('#txtTmdbApiKey').value.trim() || null;
            c.UpcomingMediaLibraryPath = p.querySelector('#txtLibraryPath').value.trim() || null;
            ApiClient.updatePluginConfiguration(pluginId, c).then(function () {
                showStatus('Settings saved!');
            }).catch(function () { showStatus('Failed to save settings.', true); });
        });
    }

    // ── Search ────────────────────────────────────────────────
    function doSearch() {
        var p = pg(); if (!p) return;
        var query = p.querySelector('#txtSearch').value.trim();
        var type = p.querySelector('#selMediaType').value;
        if (!query) return;

        var rd = p.querySelector('#searchResults');
        rd.innerHTML = '<p>Searching&hellip;</p>';

        var url = ApiClient.getUrl('UpcomingMedia/Search', { query: query, type: type });
        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (results) {
            if (!results || results.length === 0) {
                rd.innerHTML = '<p>No results found.</p>';
                lastSearchResults = [];
                return;
            }
            lastSearchResults = results;

            var html = '<div style="display:flex;flex-wrap:wrap;gap:12px;">';
            results.forEach(function (r, i) {
                html += '<div style="width:160px;text-align:center;" class="search-result-card" data-idx="' + i + '">'
                    + '<img src="' + (r.PosterUrl || '') + '" style="width:100%;border-radius:4px;"'
                    + ' onerror="this.style.display=\'none\'" />'
                    + '<div style="font-size:.85em;margin-top:4px;">'
                    + '<strong>' + esc(r.Title) + '</strong><br/>'
                    + (r.ReleaseDate ? new Date(r.ReleaseDate).toLocaleDateString() : 'TBA')
                    + '</div>'
                    + '<button is="emby-button" class="raised btnAddResult" data-idx="' + i + '" '
                    + 'style="margin-top:6px;font-size:.8em;padding:4px 10px;"><span>+ Add</span></button>'
                    + '</div>';
            });
            html += '</div>';
            rd.innerHTML = html;

            rd.querySelectorAll('.btnAddResult').forEach(function (btn) {
                btn.addEventListener('click', function (e) {
                    e.stopPropagation();
                    var idx = parseInt(btn.dataset.idx, 10);
                    addFromSearch(idx);
                });
            });
        }).catch(function () {
            rd.innerHTML = '<p style="color:red;">Search failed. Check server logs.</p>';
        });
    }

    function esc(s) {
        if (!s) return '';
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(s));
        return d.innerHTML;
    }

    function addFromSearch(idx) {
        var r = lastSearchResults[idx];
        if (!r) return;

        // Attempt AddFromTmdb first (gets full metadata)
        if (r.TmdbId) {
            var type = (pg().querySelector('#selMediaType') || {}).value || 'movie';
            var url = ApiClient.getUrl('UpcomingMedia/AddFromTmdb', { tmdbId: r.TmdbId, type: type });
            ApiClient.ajax({ url: url, type: 'POST' }).then(function () {
                showStatus('Added: ' + r.Title);
                loadItems();
            }).catch(function () {
                // Fallback: direct POST with search data
                addDirect(r);
            });
        } else {
            addDirect(r);
        }
    }

    function addDirect(r) {
        var item = {
            Title: r.Title || 'Unknown',
            Overview: r.Overview || '',
            TmdbId: r.TmdbId || 0,
            ImdbId: r.ImdbId || '',
            MediaType: r.MediaType || 'Movie',
            PosterUrl: r.PosterUrl || '',
            BackdropUrl: r.BackdropUrl || '',
            ReleaseDate: r.ReleaseDate || null,
            AvailableDate: r.ReleaseDate || null,
            Genres: r.Genres || [],
            Status: 'ComingSoon'
        };

        var url = ApiClient.getUrl('UpcomingMedia/Items');
        ApiClient.ajax({
            url: url,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(item)
        }).then(function () {
            showStatus('Added: ' + r.Title);
            loadItems();
        }).catch(function () {
            showStatus('Failed to add item.', true);
        });
    }

    // ── Items List ────────────────────────────────────────────
    function loadItems() {
        var p = pg(); if (!p) return;
        var listDiv = p.querySelector('#upcomingList');
        var url = ApiClient.getUrl('UpcomingMedia/Items');

        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (items) {
            if (!items || items.length === 0) {
                listDiv.innerHTML = '<p>No upcoming items yet. Use the search above.</p>';
                return;
            }

            var html = '<table style="width:100%;border-collapse:collapse;">';
            html += '<thead><tr>'
                + '<th></th><th>Title</th><th>Type</th><th>Available</th>'
                + '<th>Status</th><th>Dummy</th><th>Message</th><th>Actions</th>'
                + '</tr></thead><tbody>';

            items.forEach(function (item) {
                var rawDate = item.AvailableDate || item.ReleaseDate;
                var dateStr = 'TBA';
                if (rawDate) {
                    var d = new Date(rawDate);
                    dateStr = d.toLocaleDateString() + ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
                }

                html += '<tr>'
                    + '<td><img src="' + (item.PosterUrl || '') + '" style="height:48px;border-radius:3px;"'
                    + ' onerror="this.style.display=\'none\'" /></td>'
                    + '<td>' + esc(item.Title) + '</td>'
                    + '<td>' + esc(item.MediaType) + '</td>'
                    + '<td>' + dateStr + '</td>'
                    + '<td>' + esc(item.Status) + '</td>'
                    + '<td>' + (item.DummyCreated ? '<span style=\"color:#66bb6a;\">✅ Active</span>' : '<span style=\"opacity:.3;\">—</span>') + '</td>'
                    + '<td>' + esc(item.CustomMessage || '') + '</td>'
                    + '<td style="white-space:nowrap;">'
                    + '<button is="emby-button" class="raised btnEdit" data-id="' + item.Id + '" '
                    + 'style="font-size:.8em;padding:4px 8px;margin-right:4px;">Edit</button>'
                    + '<button is="emby-button" class="raised btnDelete" data-id="' + item.Id + '" '
                    + 'style="font-size:.8em;padding:4px 8px;color:#ef5350;">Delete</button>'
                    + '</td></tr>';
            });

            html += '</tbody></table>';
            listDiv.innerHTML = html;

            listDiv.querySelectorAll('.btnEdit').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var id = btn.dataset.id;
                    var match = items.find(function (i) { return i.Id === id; });
                    if (match) openEditForm(match);
                });
            });

            listDiv.querySelectorAll('.btnDelete').forEach(function (btn) {
                btn.addEventListener('click', function () { deleteItem(btn.dataset.id); });
            });
        }).catch(function () {
            listDiv.innerHTML = '<p style="color:red;">Failed to load items.</p>';
        });
    }

    function deleteItem(id) {
        if (!confirm('Delete this upcoming item?')) return;
        var url = ApiClient.getUrl('UpcomingMedia/Items/' + id);
        ApiClient.ajax({ url: url, type: 'DELETE' }).then(function () {
            showStatus('Deleted.');
            loadItems();
        });
    }

    // ── Edit Form ─────────────────────────────────────────────
    function openEditForm(item) {
        var p = pg(); if (!p) return;
        var sec = p.querySelector('#editSection');
        sec.style.display = 'block';
        p.querySelector('#editTitle').textContent = 'Edit: ' + item.Title;
        p.querySelector('#editId').value = item.Id;
        p.querySelector('#editName').value = item.Title || '';
        p.querySelector('#editOverview').value = item.Overview || '';
        // Convert UTC dates from server to local date/time for the form
        if (item.AvailableDate) {
            var aLocal = new Date(item.AvailableDate);
            p.querySelector('#editAvailDate').value = formatLocalDate(aLocal);
            p.querySelector('#editAvailTime').value = formatLocalTime(aLocal);
        } else {
            p.querySelector('#editAvailDate').value = '';
            p.querySelector('#editAvailTime').value = '00:00';
        }
        if (item.ReleaseDate) {
            var rLocal = new Date(item.ReleaseDate);
            p.querySelector('#editReleaseDate').value = formatLocalDate(rLocal);
            p.querySelector('#editReleaseTime').value = formatLocalTime(rLocal);
        } else {
            p.querySelector('#editReleaseDate').value = '';
            p.querySelector('#editReleaseTime').value = '00:00';
        }
        p.querySelector('#editStatus').value = item.Status || 'ComingSoon';
        p.querySelector('#editMessage').value = item.CustomMessage || '';
        p.querySelector('#editTrailerUrl').value = item.TrailerUrl || '';
        p.querySelector('#editRealFilePath').value = item.RealFilePath || '';
        var tfs = p.querySelector('#trailerFetchStatus');
        if (tfs) tfs.textContent = '';
        var das = p.querySelector('#dummyActionStatus');
        if (das) das.textContent = '';

        // Update library folder & dummy file section UI
        var libraryFolderInfo = p.querySelector('#libraryFolderInfo');
        var dummyStatus = p.querySelector('#dummyStatus');
        var btnCreate = p.querySelector('#btnCreateDummy');
        var btnDelete = p.querySelector('#btnDeleteDummy');
        var btnSwap = p.querySelector('#btnSwapFile');
        if (item.LibraryFolderPath) {
            libraryFolderInfo.style.display = 'block';
            libraryFolderInfo.innerHTML = '\ud83d\udcc2 <strong>Library folder:</strong> <span style="opacity:.7;font-size:.9em;">' + esc(item.LibraryFolderPath) + '</span>' +
                '<br/><span style="opacity:.5;font-size:.85em;">Drop your file here with a .real extension \u2192 it activates at the scheduled time</span>';
            libraryFolderInfo.style.borderLeft = '3px solid #00a4dc';
        } else {
            libraryFolderInfo.style.display = 'none';
        }
        if (item.DummyCreated) {
            dummyStatus.style.display = 'block';
            dummyStatus.innerHTML = '\u2705 <strong>Dummy file active:</strong> <span style="opacity:.7;font-size:.9em;">' + esc(item.DummyFilePath || '') + '</span>';
            dummyStatus.style.borderLeft = '3px solid #66bb6a';
            btnCreate.style.display = 'none';
            btnDelete.style.display = '';
            btnSwap.style.display = '';
        } else {
            dummyStatus.style.display = 'none';
            btnCreate.style.display = '';
            btnDelete.style.display = 'none';
            // Show swap button if there's a .real file detected or library folder exists
            btnSwap.style.display = (item.RealFilePath && item.RealFilePath.endsWith('.real')) ? '' : 'none';
        }
        sec.scrollIntoView({ behavior: 'smooth' });
    }

    // Format a Date object to YYYY-MM-DD (local)
    function formatLocalDate(d) {
        var y = d.getFullYear();
        var m = ('0' + (d.getMonth() + 1)).slice(-2);
        var day = ('0' + d.getDate()).slice(-2);
        return y + '-' + m + '-' + day;
    }
    // Format a Date object to HH:MM (local)
    function formatLocalTime(d) {
        var h = ('0' + d.getHours()).slice(-2);
        var m = ('0' + d.getMinutes()).slice(-2);
        return h + ':' + m;
    }

    function closeEditForm() {
        var p = pg(); if (!p) return;
        p.querySelector('#editSection').style.display = 'none';
    }

    function saveEdit() {
        var p = pg(); if (!p) return;
        var id = p.querySelector('#editId').value;
        if (!id) return;

        // First GET the full item to preserve all fields
        var getUrl = ApiClient.getUrl('UpcomingMedia/Items/' + id);
        ApiClient.ajax({ url: getUrl, type: 'GET', dataType: 'json' }).then(function (existing) {
            existing.Title = p.querySelector('#editName').value || existing.Title;
            existing.Overview = p.querySelector('#editOverview').value || '';
            var ad = p.querySelector('#editAvailDate').value;
            var at = p.querySelector('#editAvailTime').value || '00:00';
            var rd = p.querySelector('#editReleaseDate').value;
            var rt = p.querySelector('#editReleaseTime').value || '00:00';
            // Build a local Date, then convert to UTC ISO string for the server
            if (ad) {
                var localAvail = new Date(ad + 'T' + at + ':00');
                existing.AvailableDate = localAvail.toISOString();
            }
            if (rd) {
                var localRel = new Date(rd + 'T' + rt + ':00');
                existing.ReleaseDate = localRel.toISOString();
            }
            existing.Status = p.querySelector('#editStatus').value || existing.Status;
            existing.CustomMessage = p.querySelector('#editMessage').value || '';
            existing.TrailerUrl = p.querySelector('#editTrailerUrl').value.trim() || null;

            var putUrl = ApiClient.getUrl('UpcomingMedia/Items/' + id);
            ApiClient.ajax({
                url: putUrl,
                type: 'PUT',
                contentType: 'application/json',
                data: JSON.stringify(existing)
            }).then(function () {
                showStatus('Updated: ' + existing.Title);
                closeEditForm();
                loadItems();
            }).catch(function () { showStatus('Failed to update.', true); });
        }).catch(function () { showStatus('Failed to load item for editing.', true); });
    }

    // ── Init ──────────────────────────────────────────────────
    function init() {
        var p = pg(); if (!p) return;

        p.querySelector('#UpcomingMediaConfigForm')
            .addEventListener('submit', function (e) {
                e.preventDefault();
                saveConfig();
                return false;
            });

        p.querySelector('#btnSearch').addEventListener('click', doSearch);
        p.querySelector('#txtSearch').addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); doSearch(); }
        });

        var btnSave = p.querySelector('#btnSaveEdit');
        var btnCancel = p.querySelector('#btnCancelEdit');
        var btnFetchTrailer = p.querySelector('#btnFetchTrailer');
        var btnCreateDummy = p.querySelector('#btnCreateDummy');
        var btnDeleteDummy = p.querySelector('#btnDeleteDummy');
        var btnScanRealFile = p.querySelector('#btnScanRealFile');
        var btnSwapFile = p.querySelector('#btnSwapFile');
        if (btnSave) btnSave.addEventListener('click', saveEdit);
        if (btnCancel) btnCancel.addEventListener('click', closeEditForm);
        if (btnFetchTrailer) btnFetchTrailer.addEventListener('click', fetchTrailer);
        if (btnCreateDummy) btnCreateDummy.addEventListener('click', createDummyFile);
        if (btnDeleteDummy) btnDeleteDummy.addEventListener('click', deleteDummyFile);
        if (btnScanRealFile) btnScanRealFile.addEventListener('click', scanRealFile);
        if (btnSwapFile) btnSwapFile.addEventListener('click', swapFileNow);

        loadConfig();
        loadItems();
    }

    function fetchTrailer() {
        var p = pg(); if (!p) return;
        var id = p.querySelector('#editId').value;
        if (!id) return;
        var statusEl = p.querySelector('#trailerFetchStatus');
        var btn = p.querySelector('#btnFetchTrailer');
        statusEl.textContent = 'Fetching trailer from TMDb...';
        statusEl.style.color = '#00a4dc';
        btn.disabled = true;

        var url = ApiClient.getUrl('UpcomingMedia/Items/' + id + '/FetchTrailer');
        ApiClient.ajax({ url: url, type: 'POST', dataType: 'json' }).then(function (result) {
            btn.disabled = false;
            if (result.trailerUrl) {
                p.querySelector('#editTrailerUrl').value = result.trailerUrl;
                statusEl.textContent = '\u2713 Trailer found and saved!';
                statusEl.style.color = '#66bb6a';
            } else {
                statusEl.textContent = 'No trailer found on TMDb for this title.';
                statusEl.style.color = '#ff9800';
            }
        }).catch(function (err) {
            btn.disabled = false;
            statusEl.style.color = '#ef5350';
            try {
                var body = JSON.parse(err.responseText || '{}');
                statusEl.textContent = body.error || 'Failed to fetch trailer.';
            } catch (e) {
                statusEl.textContent = 'Failed to fetch trailer. Check TMDb API key in settings.';
            }
        });
    }

    // ── Dummy File Actions ───────────────────────────────────
    function dummyStatus(msg, color) {
        var p = pg(); if (!p) return;
        var el = p.querySelector('#dummyActionStatus');
        if (!el) return;
        el.textContent = msg;
        el.style.color = color || '#00a4dc';
    }

    function createDummyFile() {
        var p = pg(); if (!p) return;
        var id = p.querySelector('#editId').value;
        if (!id) return;
        dummyStatus('Creating dummy file...', '#00a4dc');

        var url = ApiClient.getUrl('UpcomingMedia/Items/' + id + '/CreateDummy');
        ApiClient.ajax({ url: url, type: 'POST', dataType: 'json' }).then(function (result) {
            dummyStatus('\u2713 ' + result.message, '#66bb6a');
            // Refresh the edit form to show updated dummy state
            refreshEditItem(id);
        }).catch(function (err) {
            try {
                var body = JSON.parse(err.responseText || '{}');
                dummyStatus(body.error || 'Failed to create dummy file.', '#ef5350');
            } catch (e) {
                dummyStatus('Failed to create dummy file. Check library path in settings.', '#ef5350');
            }
        });
    }

    function deleteDummyFile() {
        var p = pg(); if (!p) return;
        var id = p.querySelector('#editId').value;
        if (!id) return;
        if (!confirm('Delete the dummy file from the library?')) return;
        dummyStatus('Deleting dummy...', '#00a4dc');

        var url = ApiClient.getUrl('UpcomingMedia/Items/' + id + '/Dummy');
        ApiClient.ajax({ url: url, type: 'DELETE', dataType: 'json' }).then(function (result) {
            dummyStatus('\u2713 ' + result.message, '#66bb6a');
            refreshEditItem(id);
        }).catch(function () {
            dummyStatus('Failed to delete dummy file.', '#ef5350');
        });
    }

    function scanRealFile() {
        var p = pg(); if (!p) return;
        var id = p.querySelector('#editId').value;
        if (!id) return;
        dummyStatus('Scanning for .real file...', '#00a4dc');

        var url = ApiClient.getUrl('UpcomingMedia/Items/' + id + '/ScanRealFile');
        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (result) {
            if (result.found) {
                dummyStatus('\u2713 Found: ' + result.realFilePath, '#66bb6a');
                p.querySelector('#editRealFilePath').value = result.realFilePath;
                var btnSwap = p.querySelector('#btnSwapFile');
                if (btnSwap) btnSwap.style.display = '';
            } else {
                var msg = result.message || 'No .real file found.';
                if (result.libraryFolder) {
                    msg += '\nFolder: ' + result.libraryFolder;
                }
                dummyStatus(msg, '#ff9800');
            }
        }).catch(function () {
            dummyStatus('Failed to scan for .real file.', '#ef5350');
        });
    }

    function swapFileNow() {
        var p = pg(); if (!p) return;
        var id = p.querySelector('#editId').value;
        if (!id) return;
        if (!confirm('Activate the .real file now? This removes the .real extension so Jellyfin picks it up.')) return;
        dummyStatus('Swapping files...', '#00a4dc');

        var url = ApiClient.getUrl('UpcomingMedia/Items/' + id + '/SwapFile');
        ApiClient.ajax({ url: url, type: 'POST', dataType: 'json' }).then(function (result) {
            dummyStatus('\u2713 ' + result.message, '#66bb6a');
            refreshEditItem(id);
            loadItems();
        }).catch(function (err) {
            try {
                var body = JSON.parse(err.responseText || '{}');
                dummyStatus(body.error || 'File swap failed.', '#ef5350');
            } catch (e) {
                dummyStatus('File swap failed. Check server logs.', '#ef5350');
            }
        });
    }

    function refreshEditItem(id) {
        var url = ApiClient.getUrl('UpcomingMedia/Items/' + id);
        ApiClient.ajax({ url: url, type: 'GET', dataType: 'json' }).then(function (item) {
            if (item) openEditForm(item);
        }).catch(function () {});
    }

    var configPage = pg();
    if (configPage) {
        init();
        configPage.addEventListener('pageshow', function () { loadConfig(); loadItems(); });
        configPage.addEventListener('viewshow', function () { loadConfig(); loadItems(); });
    }
})();
