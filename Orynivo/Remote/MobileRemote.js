(() => {
    'use strict';
    const $ = id => document.getElementById(id);
    const lang = (navigator.language || 'en').slice(0, 2).toLowerCase();
    const t = words[lang] || words.en;
    document.documentElement.lang = words[lang] ? lang : 'en';
    document.querySelectorAll('[data-i18n]').forEach(el => el.textContent = t[el.dataset.i18n]);
    $('search').placeholder = t.placeholder;
    $('libraryFilter').placeholder = t.search;
    const fragment = new URLSearchParams(location.hash.slice(1));
    let key = fragment.get('token') || '';
    if (fragment.has('token')) history.replaceState(null, '', location.pathname + location.search);
    // Credentials live only in this page's memory, never in browser storage.
    try { sessionStorage.removeItem('orynivoRemoteToken'); } catch { /* Storage may be disabled. */ }
    let session = 0, controller, reconnectTimer, messageTimer, connected = false;
    let state, artKey = '', artUrl = '', queueKey = '', currentView = 'playing';
    let renderVersion = 0;
    const newBrowserClientId = () => [...crypto.getRandomValues(new Uint8Array(16))].map(x=>x.toString(16).padStart(2,'0')).join('');
    let browserClientId = newBrowserClientId();
    const browserAudio = $('browserAudio');
    for (const [id,label] of [['original',t.originalFormat],['dsd-pcm',t.dsdPcm],
        ...[[16,44100],[16,48000],[24,88200],[24,96000],[24,176400],[24,192000]].map(([bits,rate])=>['flac-'+bits+'-'+rate,'FLAC · '+bits+' bit · '+(rate/1000).toLocaleString()+' kHz']),
        ...[128,192,256,320].map(rate=>['mp3-'+rate,'MP3 · '+rate+' kbps'])])
        $('musicFormat').add(new Option(label,id));
    $('musicFormat').value = 'mp3-192';
    let browserInfo = null, audioContext, audioGain, audioBlocked = false, mediaFailed = false, playPending = false, reportPending = false;
    let outputVersion = 0, outputOwner, outputSwitching = false;
    let directSeekPending = false;
    const mediaVersions = { radio:0, podcasts:0 };
    let libraryLoaded = false, playlistsLoaded = false, libraryLevel = 'artists', libraryArtist;
    let libraryVersion = 0, playlistVersion = 0, searchVersion = 0, playlistId, filterTimer;
    let libraryImageController, libraryImageObserver, libraryImageQueue = [], libraryImageRunning = 0;
    const libraryImageUrls = new Set();
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
        resetLibraryImages();
        releaseBrowserAudio(); stopBrowserAudio();
        session++; controller?.abort(); clearTimeout(reconnectTimer); connected = false; key = '';
        $('app').hidden = true; $('logout').hidden = true; $('login').hidden = false;
        $('token').value = ''; $('authError').textContent = error; $('connect').disabled = false;
        mediaVersions.radio++; mediaVersions.podcasts++;
        libraryLoaded = playlistsLoaded = false; libraryVersion++; playlistVersion++; searchVersion++;
        queueKey = artKey = ''; state = null;
        $('cover').hidden = true; $('cover').removeAttribute('src');
        if (artUrl) URL.revokeObjectURL(artUrl); artUrl = '';
        notice(''); $('token').focus();
    }
    async function api(path, body, signal = controller?.signal) {
        const generation = session;
        const r = await fetch('/remote/api/' + path, {
            method: body === undefined ? 'GET' : 'POST',
            headers: { Authorization: 'Bearer ' + key, 'Content-Type': 'application/json' },
            cache: 'no-store', signal,
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
    async function startPlayback(path, body) {
        unlockBrowserAudio();
        const generation = session;
        await api(path, body);
        if (generation !== session) return;
        showView('playing'); window.scrollTo({top:0,behavior:'auto'});
        const version = renderVersion;
        try {
            const next = await (await api('state')).json();
            if (generation === session && version === renderVersion) render(next);
        } catch { /* Live state still refreshes after a successful start. */ }
    }
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
    function resetLibraryImages() {
        libraryImageController?.abort(); libraryImageObserver?.disconnect(); libraryImageQueue = [];
        for (const url of libraryImageUrls) URL.revokeObjectURL(url);
        libraryImageUrls.clear(); libraryImageController = new AbortController();
        libraryImageObserver = new IntersectionObserver(entries=>{
            for (const entry of entries) if (entry.isIntersecting) {
                libraryImageObserver.unobserve(entry.target);
                libraryImageQueue.push({image:entry.target.querySelector('img'),signal:libraryImageController.signal});
            }
            loadLibraryImages();
        },{rootMargin:'120px'});
    }
    function loadLibraryImages() {
        while (libraryImageRunning < 4 && libraryImageQueue.length) {
            const {image,signal} = libraryImageQueue.shift();
            if (signal.aborted || !image.isConnected) continue;
            libraryImageRunning++;
            (async()=>{
                try {
                    const response = await api('library/artwork?key='+encodeURIComponent(image.dataset.artworkKey),undefined,signal);
                    const blob = await response.blob();
                    if (signal.aborted || !image.isConnected) return;
                    const url = URL.createObjectURL(blob); libraryImageUrls.add(url);
                    image.onload = () => { if (!signal.aborted && image.isConnected) { image.hidden = false; image.previousElementSibling.setAttribute('hidden',''); } };
                    image.src = url;
                } catch { /* Missing pictures retain the fixed-size illustration. */ }
                finally { libraryImageRunning--; loadLibraryImages(); }
            })();
        }
    }
    function libraryRow(item, kind, open) {
        const li = row(item.name || item.title,[item.artist,item.year,item.source].filter(Boolean).join(' · '),open);
        li.className = 'library-row';
        const copy = document.createElement('div'); copy.className = 'library-row-copy'; copy.append(...li.childNodes);
        const art = document.createElement('div'); art.className = 'library-row-art '+kind;
        const svg = document.createElementNS('http://www.w3.org/2000/svg','svg');
        svg.setAttribute('viewBox','0 0 24 24'); svg.setAttribute('aria-hidden','true');
        const path = document.createElementNS(svg.namespaceURI,'path');
        path.setAttribute('d',kind==='artist' ? 'M12 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8M4 21v-3a8 6 0 0 1 16 0v3z' : 'M3 3h18v18H3zM12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10M12 11a1 1 0 1 1 0 2 1 1 0 0 1 0-2');
        path.setAttribute('fill-rule','evenodd'); svg.append(path); art.append(svg);
        if (item.artworkKey) {
            const image = document.createElement('img'); image.alt = ''; image.hidden = true;
            image.width = image.height = 56; image.decoding = 'async'; image.dataset.artworkKey = item.artworkKey;
            art.append(image); libraryImageObserver.observe(art);
        }
        li.append(art,copy); return li;
    }
    function trackRow(track, context) {
        const li = row(track.title, [track.artist, track.album, track.year, track.source].filter(Boolean).join(' · '));
        const actions = document.createElement('div'); actions.className = 'result-actions';
        for (const action of ['play','next','append'])
            actions.append(actionButton(t[action === 'next' ? 'playNext' : action], () =>
                action === 'play' ? Array.isArray(context)
                    ? startPlayback('tracks/queue',{action,ids:context}) : context && track.playlistIndex != null
                    ? startPlayback('playlists/'+context.id+'/queue',{action,startIndex:track.playlistIndex,trackId:track.id})
                    : startPlayback('tracks/queue', {id:track.id,action}) : api('tracks/queue', {id:track.id,action})));
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
        $('cover').removeAttribute('src');
        if (artUrl) URL.revokeObjectURL(artUrl); artUrl = '';
        try {
            const r = await api('artwork?key='+encodeURIComponent(k)); const blob = await r.blob();
            if (generation !== session || artKey !== k) return;
            if (artUrl) URL.revokeObjectURL(artUrl);
            artUrl = URL.createObjectURL(blob); $('cover').src = artUrl; $('cover').hidden = false;
        } catch { /* Missing artwork leaves the illustration visible. */ }
    }
    function render(s) {
        renderVersion++;
        state = s;
        if (s.browserAudio?.formatId) $('musicFormat').value = s.browserAudio.formatId;
        $('musicFormat').disabled = s.browserAudio?.clientId !== browserClientId || s.playback?.mediaKind === 'radio' || s.playback?.mediaKind === 'podcasts';
        syncBrowserAudio(s.browserAudio);
        if (outputOwner !== s.browserAudio?.clientId) { outputOwner = s.browserAudio?.clientId; loadOutputs(); }
        $('status').textContent = t[s.status] || t.stopped;
        $('trackTitle').textContent = s.title || t.nothingPlaying;
        $('artist').textContent = s.artist || ''; $('album').textContent = s.album || '';
        $('artist').hidden = !s.artist; $('album').hidden = !s.album;
        const info = s.playback || {};
        for (const [id,value] of Object.entries({mediaKind:t[info.mediaKind],sourceName:info.sourceName,
            playbackDetails:info.details,audioFormat:s.browserAudio?.format || info.audioFormat,
            published:info.publishedAt ? t.published+' '+new Date(info.publishedAt).toLocaleDateString() : ''})) {
            $(id).textContent = value || ''; $(id).hidden = !value;
        }
        $('seek').max = Math.max(1, s.durationSeconds); $('seek').disabled = info.canSeek === false || !(s.durationSeconds > 0);
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
            const li = row(x.title, '', () => perform(() => startPlayback('command',{command:'queue-index',value:x.index})));
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
        const version = ++outputVersion;
        try {
            const rows = await (await api('outputs?clientId='+browserClientId)).json();
            if (version !== outputVersion) return;
            const options = rows.map(x => {
                const o = document.createElement('option'); o.value = x.isBrowser ? 'browser' : 'profile:'+x.name;
                o.textContent = x.isBrowser ? t.thisDevice : x.name; o.selected = x.selected; return o;
            });
            if (!rows.some(x=>x.selected) && state?.browserAudio) {
                const other = new Option(t.otherBrowser,'other',true,true); other.disabled = true; options.unshift(other);
            }
            $('output').replaceChildren(...options);
        } catch(e) { if(e.name !== 'AbortError') notice(t.failed); }
    }
    function unlockBrowserAudio() {
        if (!audioContext) {
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (AudioContext) {
                try {
                    audioContext = new AudioContext(); audioGain = audioContext.createGain();
                    audioContext.createMediaElementSource(browserAudio).connect(audioGain); audioGain.connect(audioContext.destination);
                } catch { audioContext = audioGain = null; }
            }
        }
        audioContext?.resume().catch(()=>{});
    }
    function stopBrowserAudio(releaseContext = true) {
        browserInfo = null; audioBlocked = mediaFailed = playPending = directSeekPending = false;
        browserAudio.pause(); browserAudio.removeAttribute('src'); browserAudio.load();
        if (releaseContext) audioContext?.suspend().catch(()=>{});
        $('browserAudioHint').hidden = $('browserAudioPlay').hidden = true;
    }
    function releaseBrowserAudio() {
        if (!key || state?.browserAudio?.clientId !== browserClientId) return;
        fetch('/remote/api/browser-audio/release',{method:'POST',keepalive:true,
            headers:{Authorization:'Bearer '+key,'Content-Type':'application/json'},body:JSON.stringify({clientId:browserClientId})}).catch(()=>{});
    }
    async function playBrowserAudio() {
        if (!browserInfo || playPending) return;
        if (!applyDirectSeek()) return;
        const id = browserInfo.streamId; playPending = true;
        try {
            await browserAudio.play();
            if (browserInfo?.streamId === id) {
                audioBlocked = !!audioContext && audioContext.state !== 'running';
                $('browserAudioPlay').hidden = !audioBlocked;
                if (audioBlocked) $('browserAudioHint').textContent = t.browserAudioBlocked;
            }
        } catch(e) {
            if (browserInfo?.streamId === id && e.name !== 'AbortError') {
                audioBlocked = true; $('browserAudioHint').textContent = t.browserAudioBlocked;
                $('browserAudioPlay').hidden = false;
            }
        } finally { if (browserInfo?.streamId === id) playPending = false; }
    }
    function syncBrowserAudio(info) {
        if (outputSwitching) return;
        if (info?.clientId !== browserClientId || !info.streamId) {
            if (browserInfo) stopBrowserAudio(info?.clientId !== browserClientId);
            return;
        }
        const changed = browserInfo?.streamId !== info.streamId;
        if (changed) {
            stopBrowserAudio(false); browserInfo = info;
            directSeekPending = !!info.direct && info.seekSeconds > 0;
            browserAudio.preload = directSeekPending ? 'metadata' : 'none';
            browserAudio.src = '/remote/api/browser-audio/stream/'+encodeURIComponent(info.streamId);
            browserAudio.load();
        } else browserInfo = info;
        $('browserAudioHint').hidden = false;
        if (mediaFailed) $('browserAudioHint').textContent = info.direct ? t.loading : t.browserAudioFailed;
        else if (!audioBlocked) $('browserAudioHint').textContent = t.browserAudioActive;
        if (audioGain) audioGain.gain.value = info.volume;
        else browserAudio.volume = info.volume;
        if (info.paused) browserAudio.pause();
        else if (browserAudio.paused && !audioBlocked && !mediaFailed) playBrowserAudio();
    }
    async function reportBrowserAudio(ended = false, failed = false) {
        if (!browserInfo || !connected || (reportPending && !ended && !failed)) return;
        reportPending = true;
        const info = browserInfo, time = Number.isFinite(browserAudio.currentTime) ? browserAudio.currentTime : 0;
        try { await api('browser-audio/report',{clientId:browserClientId,streamId:info.streamId,
            controlRevision:info.controlRevision,positionSeconds:info.startSeconds+time,
            playing:!browserAudio.paused && browserAudio.readyState >= 3,ended,failed,
            durationSeconds:Number.isFinite(browserAudio.duration) && browserAudio.duration>0 ? browserAudio.duration : null}); }
        catch { /* Old stream/command feedback is intentionally rejected. */ }
        finally { reportPending = false; }
    }
    browserAudio.onended = () => reportBrowserAudio(true);
    browserAudio.onplaying = () => reportBrowserAudio();
    function applyDirectSeek() {
        if (!directSeekPending) return true;
        if (!browserInfo || browserAudio.readyState < 1) return false;
        try { browserAudio.currentTime = browserInfo.seekSeconds; directSeekPending = false; return true; }
        catch { return false; }
    }
    browserAudio.onloadedmetadata = () => {
        if (applyDirectSeek() && browserInfo && !browserInfo.paused && !audioBlocked) playBrowserAudio();
    };
    browserAudio.onerror = () => {
        if (!browserInfo || !browserAudio.error || browserAudio.error.code === 1) return;
        mediaFailed = true;
        $('browserAudioHint').textContent = browserInfo.direct ? t.loading : t.browserAudioFailed;
        if (!browserInfo.direct) notice(t.browserAudioFailed);
        reportBrowserAudio(false,true);
    };
    $('browserAudioPlay').onclick = () => { unlockBrowserAudio(); audioBlocked = false; playBrowserAudio(); };
    setInterval(()=>reportBrowserAudio(false,mediaFailed),1500);
    window.addEventListener('pagehide',()=>{releaseBrowserAudio();stopBrowserAudio();});
    function showView(view) {
        mediaVersions.radio++; mediaVersions.podcasts++;
        $('moreMenu').hidden = true; $('moreButton').setAttribute('aria-expanded','false');
        currentView = view;
        const panel = view === 'artists' || view === 'albums' ? 'library' : view;
        if (panel !== 'library') resetLibraryImages();
        document.querySelectorAll('[data-panel]').forEach(p => p.hidden = p.id !== panel);
        $('searchForm').hidden = view !== 'library'; $('results').hidden = view !== 'library';
        $('libraryBrowser').hidden = view === 'library';
        document.querySelectorAll('[data-view]').forEach(b => b.setAttribute('aria-pressed',String(b.dataset.view === view)));
        if (view === 'radio' || view === 'podcasts') browseMedia(view);
        if (view === 'artists') browseArtists();
        if (view === 'albums') browseAllAlbums();
        if (view === 'playlists' && !playlistsLoaded) browsePlaylists();
    }
    async function libraryFetch(path, build) {
        resetLibraryImages();
        const version = ++libraryVersion; empty($('libraryList'),t.loading);
        try {
            const rows = await (await api(path)).json();
            if (version !== libraryVersion) return;
            showRows($('libraryList'),rows,build === trackRow ? (x,i)=>trackRow(x,rows.slice(i).map(t=>t.id)) : build); libraryLoaded = true;
        } catch(e) { if(version === libraryVersion && e.name !== 'AbortError') empty($('libraryList'),t.failed); }
    }
    function browseArtists() {
        libraryLevel = 'artists'; libraryArtist = null;
        $('libraryTitle').textContent = t.artists; $('libraryBack').hidden = true; $('libraryFilter').hidden = false;
        return libraryFetch('library/artists?q='+encodeURIComponent($('libraryFilter').value.trim())+'&limit=250',
            x => libraryRow(x,'artist',()=>browseAlbums(x)));
    }
    function browseAllAlbums() {
        libraryLevel = 'allAlbums'; libraryArtist = null;
        $('libraryTitle').textContent = t.albums; $('libraryBack').hidden = true; $('libraryFilter').hidden = false;
        return libraryFetch('library/albums?q='+encodeURIComponent($('libraryFilter').value.trim()),
            x => libraryRow(x,'album',()=>browseTracks(x)));
    }
    function browseAlbums(artist) {
        libraryLevel = 'albums'; libraryArtist = artist;
        $('libraryTitle').textContent = artist.name; $('libraryBack').hidden = false; $('libraryFilter').hidden = true;
        return libraryFetch('library/albums?artistId='+encodeURIComponent(artist.id),
            x => libraryRow(x,'album',()=>browseTracks(x)));
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
            if(version === searchVersion) showRows($('results'),rows,(x,i)=>trackRow(x,rows.slice(i).map(t=>t.id)));
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
            showRows($('playlistList'),rows,x=>trackRow(x,playlist)); $('playlistActions').hidden = !rows.length;
        } catch(e) { if(version === playlistVersion && e.name !== 'AbortError') empty($('playlistList'),t.failed); }
    }
    async function browseMedia(kind, podcast) {
        const version = ++mediaVersions[kind], list = $(kind+'List');
        $(kind+'Title').textContent = podcast ? podcast.title : t[kind];
        $(kind+'Back').hidden = !podcast;
        $(kind+'Form').hidden = !!podcast;
        empty(list,t.loading);
        const path = podcast ? 'media/podcasts/'+encodeURIComponent(podcast.id)+'/episodes'
            : 'media/'+kind+'?q='+encodeURIComponent($(kind+'Search').value.trim());
        try {
            const rows = await (await api(path)).json();
            if (version !== mediaVersions[kind] || currentView !== kind) return;
            showRows(list,rows,item => {
                const meta = [item.subtitle];
                if (item.publishedAt) meta.push(new Date(item.publishedAt).toLocaleDateString());
                if (item.durationSeconds) meta.push(clock(item.durationSeconds));
                const li = row(item.title,meta.filter(Boolean).join(' · '),
                    kind === 'podcasts' && !podcast ? ()=>browseMedia(kind,item) : null);
                if (kind === 'radio' || podcast) {
                    const actions = document.createElement('div'); actions.className = 'result-actions';
                    actions.append(actionButton(t.play,()=>startPlayback('media/'+kind+'/play',{id:item.id})));
                    li.append(actions);
                }
                return li;
            });
        } catch(e) { if(version === mediaVersions[kind] && e.name !== 'AbortError') empty(list,t.failed); }
    }
    for (const kind of ['radio','podcasts']) {
        $(kind+'Form').onsubmit = e => { e.preventDefault(); browseMedia(kind); };
        $(kind+'Search').oninput = () => { mediaVersions[kind]++; };
        $(kind+'Back').onclick = () => browseMedia(kind);
        $(kind+'Refresh').onclick = () => { $(kind+'Search').value = ''; browseMedia(kind); };
    }
    $('login').onsubmit = e => { e.preventDefault(); key = $('token').value.trim(); if(!key){$('authError').textContent=t.missing;return;} connect(); };
    $('logout').onclick = () => disconnect();
    $('favorite').onclick = () => perform(()=>command('favorite',state?.isFavorite ? 0 : 1),$('favorite'));
    $('seek').oninput = () => $('elapsed').textContent = clock($('seek').value);
    $('seek').onchange = () => perform(()=>command('seek',+$('seek').value));
    $('volume').oninput = () => $('volumeValue').textContent = Math.round(+$('volume').value*100)+'%';
    $('volume').onchange = () => perform(()=>command('volume',+$('volume').value));
    $('output').onchange = () => {
        unlockBrowserAudio();
        const selected = $('output').value;
        if (selected === 'browser') browserClientId = newBrowserClientId();
        outputSwitching = true;
        if (selected !== 'browser') stopBrowserAudio();
        perform(async()=>{
            try {
                if (selected === 'browser') await api('browser-audio/select',{clientId:browserClientId,formatId:$('musicFormat').value});
                else await api('outputs/select',{name:selected.slice(8)});
            } finally { outputSwitching = false; }
            const next = await (await api('state')).json(); render(next); await loadOutputs();
        },$('output'));
    };
    $('musicFormat').onchange = () => {
        outputSwitching = true; stopBrowserAudio(false);
        perform(async()=>{
            try { await api('browser-audio/select',{clientId:browserClientId,formatId:$('musicFormat').value}); }
            finally { outputSwitching = false; }
            render(await (await api('state')).json());
        },$('musicFormat'));
    };
    $('moreButton').onclick = () => {
        $('moreMenu').hidden = !$('moreMenu').hidden;
        $('moreButton').setAttribute('aria-expanded',String(!$('moreMenu').hidden));
    };
    document.addEventListener('keydown',e=>{if(e.key==='Escape'){$('moreMenu').hidden=true;$('moreButton').setAttribute('aria-expanded','false');$('moreButton').focus();}});
    $('clear').onclick = () => {if(confirm(t.confirmClear)) perform(()=>api('queue',{action:'clear'}),$('clear'));};
    $('searchForm').onsubmit = e => {e.preventDefault();runSearch();};
    $('search').oninput = () => {searchVersion++;$('results').replaceChildren();};
    $('libraryFilter').oninput = () => {clearTimeout(filterTimer);libraryVersion++;filterTimer=setTimeout(currentView==='albums'?browseAllAlbums:browseArtists,250);};
    $('libraryBack').onclick = () => libraryLevel === 'tracks' && libraryArtist ? browseAlbums(libraryArtist) : currentView==='albums'?browseAllAlbums():browseArtists();
    $('playlistBack').onclick = $('playlistRefresh').onclick = browsePlaylists;
    $('playlistPlay').onclick = () => perform(()=>startPlayback('playlists/'+playlistId+'/queue',{action:'play'}),$('playlistPlay'));
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
