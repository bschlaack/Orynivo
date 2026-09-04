(() => {
    'use strict';
    const $ = id => document.getElementById(id);
    const lang = (navigator.language || 'en').slice(0, 2).toLowerCase();
    const t = words[lang] || words.en;
    document.documentElement.lang = words[lang] ? lang : 'en';
    document.querySelectorAll('[data-i18n]').forEach(el => el.textContent = t[el.dataset.i18n]);
    $('search').placeholder = t.placeholder;
    $('libraryFilter').placeholder = t.filter;
    const fragment = new URLSearchParams(location.hash.slice(1));
    let key = fragment.get('token') || '';
    if (fragment.has('token')) history.replaceState(null, '', location.pathname + location.search);
    // Credentials live only in this page's memory, never in browser storage.
    try { sessionStorage.removeItem('orynivoRemoteToken'); } catch { /* Storage may be disabled. */ }
    let session = 0, controller, reconnectTimer, messageTimer, connected = false;
    let state, artKey = '', artUrl = '', queueKey = '', currentView = 'playing';
    let libraryLoaded = false, playlistsLoaded = false, libraryLevel = 'artists', libraryArtist;
    let libraryVersion = 0, playlistVersion = 0, searchVersion = 0, playlistId, filterTimer;
    const paths = {
        previous:'M5 4h3v16H5zM19 4v16L8 12z', next:'M16 4h3v16h-3zM5 4v16l11-8z',
        play:'M7 3v18l15-9z', pause:'M5 3h5v18H5zM14 3h5v18h-5z', stop:'M5 5h14v14H5z'
    };
    function icon(button, kind, label) {
        button.replaceChildren();
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('class', 'icon'); svg.setAttribute('aria-hidden', 'true');
        const path = document.createElementNS(svg.namespaceURI, 'path'); path.setAttribute('d', paths[kind]);
        svg.append(path); button.append(svg); button.setAttribute('aria-label', label); button.title = label;
    }
    const transport = [...document.querySelectorAll('[data-cmd]')];
    transport.forEach(button => {
        const cmd = button.dataset.cmd;
        icon(button, cmd === 'pause-resume' ? 'play' : cmd, t[cmd === 'pause-resume' ? 'play' : cmd]);
        button.onclick = () => perform(() => command(cmd), button);
    });
    function notice(text) {
        clearTimeout(messageTimer); $('message').textContent = text; $('message').hidden = !text;
        if (text) messageTimer = setTimeout(() => { if (connected) $('message').hidden = true; }, 5000);
    }
    function disconnect(error = '') {
        session++; controller?.abort(); clearTimeout(reconnectTimer); connected = false; key = '';
        $('app').hidden = true; $('logout').hidden = true; $('login').hidden = false;
        $('token').value = ''; $('authError').textContent = error; $('connect').disabled = false;
        libraryLoaded = playlistsLoaded = false; libraryVersion++; playlistVersion++; searchVersion++;
        queueKey = artKey = ''; state = null;
        $('cover').hidden = true; $('cover').removeAttribute('src');
        if (artUrl) URL.revokeObjectURL(artUrl); artUrl = '';
        notice(''); $('token').focus();
    }
    async function api(path, body) {
        const generation = session;
        const r = await fetch('/remote/api/' + path, {
            method: body === undefined ? 'GET' : 'POST',
            headers: { Authorization: 'Bearer ' + key, 'Content-Type': 'application/json' },
            cache: 'no-store', signal: controller?.signal,
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        if (generation !== session) throw new DOMException('', 'AbortError');
        if (r.status === 401) { disconnect(t.denied); throw new DOMException('', 'AbortError'); }
        if (!r.ok) throw new Error('remote_request_failed');
        return r;
    }
    async function perform(action, button) {
        if (!connected) return;
        if (button) button.disabled = true;
        try { await action(); notice(t.done); }
        catch (e) { if (e.name !== 'AbortError') notice(t.failed); }
        finally { if (button) button.disabled = false; }
    }
    const command = (command, value) => api('command', { command, value });
    function actionButton(label, action) {
        const button = document.createElement('button'); button.type = 'button'; button.textContent = label;
        button.onclick = () => perform(action, button); return button;
    }
    function empty(list, text) {
        const li = document.createElement('li'); li.className = 'empty'; li.textContent = text; list.replaceChildren(li);
    }
    function row(primary, secondary, open) {
        const li = document.createElement('li'), name = document.createElement(open ? 'button' : 'div');
        name.className = open ? 'row-name' : 'row-title'; name.textContent = primary;
        if (open) { name.type = 'button'; name.onclick = open; }
        const meta = document.createElement('div'); meta.className = 'meta'; meta.textContent = secondary || '';
        li.append(name, meta); return li;
    }
    function trackRow(track) {
        const li = row(track.title, [track.artist, track.album, track.year, track.source].filter(Boolean).join(' · '));
        const actions = document.createElement('div'); actions.className = 'result-actions';
        for (const action of ['play','next','append'])
            actions.append(actionButton(t[action === 'next' ? 'playNext' : action], () => api('tracks/queue', { id:track.id, action })));
        li.append(actions); return li;
    }
    function showRows(list, rows, build, emptyText = t.empty) {
        if (!rows.length) empty(list, emptyText); else list.replaceChildren(...rows.map(build));
    }
    function clock(seconds) {
        seconds = Math.max(0, Math.floor(Number(seconds) || 0));
        return Math.floor(seconds / 60) + ':' + String(seconds % 60).padStart(2, '0');
    }
    async function artwork(k) {
        if (artKey === k) return;
        artKey = k;
        const generation = session;
        $('cover').hidden = true;
        try {
            const r = await api('artwork'); const blob = await r.blob();
            if (generation !== session || artKey !== k) return;
            if (artUrl) URL.revokeObjectURL(artUrl);
            artUrl = URL.createObjectURL(blob); $('cover').src = artUrl; $('cover').hidden = false;
        } catch { /* Missing artwork leaves the illustration visible. */ }
    }
    function render(s) {
        state = s;
        $('status').textContent = t[s.status] || t.stopped;
        $('trackTitle').textContent = s.title || t.nothingPlaying;
        $('artist').textContent = s.artist || ''; $('album').textContent = s.album || '';
        $('seek').max = Math.max(1, s.durationSeconds); $('seek').disabled = !(s.durationSeconds > 0);
        if (document.activeElement !== $('seek')) $('seek').value = s.positionSeconds;
        if (document.activeElement !== $('volume')) $('volume').value = s.volume;
        $('elapsed').textContent = clock(s.positionSeconds); $('duration').textContent = clock(s.durationSeconds);
        $('volumeValue').textContent = Math.round(Number($('volume').value) * 100) + '%';
        $('favorite').hidden = s.isFavorite == null;
        $('favorite').setAttribute('aria-pressed', String(!!s.isFavorite));
        $('favorite').textContent = (s.isFavorite ? '♥ ' : '♡ ') + t.favorite;
        icon(transport[1], s.status === 'playing' ? 'pause' : 'play', s.status === 'playing' ? t.pause : t.play);
        artwork(s.artworkKey);
        const queue = s.queue || [], signature = JSON.stringify(queue);
        $('queueCount').textContent = '(' + queue.length + ')'; $('clear').disabled = !queue.length;
        if (signature === queueKey) return;
        queueKey = signature;
        showRows($('queueList'), queue, x => {
            const li = row(x.title, '', () => perform(() => command('queue-index',x.index)));
            li.className = x.isCurrent ? 'active' : '';
            const actions = document.createElement('div'); actions.className = 'result-actions';
            for (const action of ['up','down','remove']) {
                const b = actionButton(t[action], () => api('queue',{action,index:x.index}));
                b.disabled = (action === 'up' && x.index === 0) || (action === 'down' && x.index === queue.length - 1);
                actions.append(b);
            }
            li.append(actions); return li;
        }, t.emptyQueue);
    }
    async function live(generation) {
        try {
            const r = await api('events');
            const reader = r.body.getReader(), decoder = new TextDecoder(); let buffer = '';
            while (generation === session) {
                const part = await reader.read(); if (part.done) throw new Error('stream_ended');
                buffer += decoder.decode(part.value, {stream:true});
                let end;
                while ((end = buffer.indexOf('\n\n')) >= 0) {
                    const event = buffer.slice(0,end); buffer = buffer.slice(end + 2);
                    const data = event.split('\n').find(line => line.startsWith('data: '));
                    if (data && generation === session) { render(JSON.parse(data.slice(6))); connected = true; }
                }
            }
        } catch(e) {
            if (e.name === 'AbortError' || generation !== session) return;
            notice(t.offline);
            reconnectTimer = setTimeout(() => { if (key && generation === session) live(generation); }, 2000);
        }
    }
    async function connect() {
        controller?.abort(); clearTimeout(reconnectTimer); controller = new AbortController();
        const generation = ++session; $('connect').disabled = true; $('authError').textContent = '';
        try {
            const s = await (await api('state')).json();
            if (generation !== session) return;
            connected = true; $('login').hidden = true; $('app').hidden = false; $('logout').hidden = false;
            $('token').value = ''; artKey = queueKey = ''; render(s); live(generation);
            loadOutputs(); showView(currentView);
        } catch(e) {
            if (generation === session && e.name !== 'AbortError') $('authError').textContent = t.failed;
        } finally { if (generation === session) $('connect').disabled = false; }
    }
    async function loadOutputs() {
        try {
            const rows = await (await api('outputs')).json();
            $('output').replaceChildren(...rows.map(x => { const o = document.createElement('option'); o.value = o.textContent = x.name; o.selected = x.selected; return o; }));
        } catch(e) { if(e.name !== 'AbortError') notice(t.failed); }
    }
    function showView(view) {
        currentView = view;
        document.querySelectorAll('[data-panel]').forEach(p => p.hidden = p.id !== view);
        document.querySelectorAll('[data-view]').forEach(b => b.setAttribute('aria-pressed',String(b.dataset.view === view)));
        if (view === 'library' && !libraryLoaded) browseArtists();
        if (view === 'playlists' && !playlistsLoaded) browsePlaylists();
    }
    async function libraryFetch(path, build) {
        const version = ++libraryVersion; empty($('libraryList'),t.loading);
        try {
            const rows = await (await api(path)).json();
            if (version !== libraryVersion) return;
            showRows($('libraryList'),rows,build); libraryLoaded = true;
        } catch(e) { if(version === libraryVersion && e.name !== 'AbortError') empty($('libraryList'),t.failed); }
    }
    function browseArtists() {
        libraryLevel = 'artists'; libraryArtist = null;
        $('libraryTitle').textContent = t.artists; $('libraryBack').hidden = true; $('libraryFilter').hidden = false;
        return libraryFetch('library/artists?q='+encodeURIComponent($('libraryFilter').value.trim())+'&limit=250',
            x => row(x.name,x.source,()=>browseAlbums(x)));
    }
    function browseAlbums(artist) {
        libraryLevel = 'albums'; libraryArtist = artist;
        $('libraryTitle').textContent = artist.name; $('libraryBack').hidden = false; $('libraryFilter').hidden = true;
        return libraryFetch('library/albums?artistId='+encodeURIComponent(artist.id),
            x => row(x.title,[x.year,x.source].filter(Boolean).join(' · '),()=>browseTracks(x)));
    }
    function browseTracks(album) {
        libraryLevel = 'tracks'; $('libraryTitle').textContent = album.title;
        return libraryFetch('library/tracks?albumId='+encodeURIComponent(album.id),trackRow);
    }
    async function runSearch() {
        const q = $('search').value.trim(), version = ++searchVersion;
        if(q.length < 2) { $('results').replaceChildren(); return; }
        empty($('results'),t.loading);
        try {
            const rows = await (await api('search?q='+encodeURIComponent(q)+'&limit=50')).json();
            if(version === searchVersion) showRows($('results'),rows,trackRow);
        } catch(e) { if(version === searchVersion && e.name !== 'AbortError') empty($('results'),t.failed); }
    }
    async function browsePlaylists() {
        const version = ++playlistVersion; playlistId = null;
        $('playlistTitle').textContent = t.playlists; $('playlistBack').hidden = true; $('playlistActions').hidden = true;
        empty($('playlistList'),t.loading);
        try {
            const rows = await (await api('playlists')).json();
            if(version !== playlistVersion) return;
            showRows($('playlistList'),rows,x=>row(x.name,x.isSmart ? t.smart : x.trackCount+' '+t.tracks,()=>openPlaylist(x)),t.emptyPlaylists);
            playlistsLoaded = true;
        } catch(e) { if(version === playlistVersion && e.name !== 'AbortError') empty($('playlistList'),t.failed); }
    }
    async function openPlaylist(playlist) {
        const version = ++playlistVersion; playlistId = playlist.id;
        $('playlistTitle').textContent = playlist.name; $('playlistBack').hidden = false; $('playlistActions').hidden = true;
        empty($('playlistList'),t.loading);
        try {
            const rows = await (await api('playlists/'+playlist.id+'/tracks')).json();
            if(version !== playlistVersion) return;
            showRows($('playlistList'),rows,trackRow); $('playlistActions').hidden = !rows.length;
        } catch(e) { if(version === playlistVersion && e.name !== 'AbortError') empty($('playlistList'),t.failed); }
    }
    $('login').onsubmit = e => { e.preventDefault(); key = $('token').value.trim(); if(!key){$('authError').textContent=t.missing;return;} connect(); };
    $('logout').onclick = () => disconnect();
    $('favorite').onclick = () => perform(()=>command('favorite',state?.isFavorite ? 0 : 1),$('favorite'));
    $('seek').oninput = () => $('elapsed').textContent = clock($('seek').value);
    $('seek').onchange = () => perform(()=>command('seek',+$('seek').value));
    $('volume').oninput = () => $('volumeValue').textContent = Math.round(+$('volume').value*100)+'%';
    $('volume').onchange = () => perform(()=>command('volume',+$('volume').value));
    $('output').onchange = () => perform(async()=>{await api('outputs/select',{name:$('output').value});await loadOutputs();},$('output'));
    $('clear').onclick = () => {if(confirm(t.confirmClear)) perform(()=>api('queue',{action:'clear'}),$('clear'));};
    $('searchForm').onsubmit = e => {e.preventDefault();runSearch();};
    $('search').oninput = () => {if(!$('search').value){searchVersion++;$('results').replaceChildren();}};
    $('libraryFilter').oninput = () => {clearTimeout(filterTimer);libraryVersion++;filterTimer=setTimeout(browseArtists,250);};
    $('libraryBack').onclick = () => libraryLevel === 'tracks' && libraryArtist ? browseAlbums(libraryArtist) : browseArtists();
    $('playlistBack').onclick = $('playlistRefresh').onclick = browsePlaylists;
    $('playlistPlay').onclick = () => perform(()=>api('playlists/'+playlistId+'/queue',{action:'play'}),$('playlistPlay'));
    $('playlistAppend').onclick = () => perform(()=>api('playlists/'+playlistId+'/queue',{action:'append'}),$('playlistAppend'));
    document.querySelectorAll('[data-view]').forEach(b=>b.onclick=()=>showView(b.dataset.view));
    window.addEventListener('hashchange', () => {
        const incoming = new URLSearchParams(location.hash.slice(1));
        if (!incoming.has('token')) return;
        const supplied = incoming.get('token') || '';
        history.replaceState(null, '', location.pathname + location.search);
        disconnect();
        key = supplied;
        if (key) connect();
    });
    if(key) connect();
})();
