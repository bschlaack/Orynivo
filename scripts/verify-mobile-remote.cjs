// Run with Node.js and Playwright available (NODE_PATH may point to a shared installation).
// All network traffic and credentials below are synthetic; the real player is never contacted.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const words = JSON.parse(fs.readFileSync(path.join(root,'Orynivo/Localization/MobileRemote.json'),'utf8'));
for (const lang of Object.keys(words)) assert.deepEqual(Object.keys(words[lang]).sort(),Object.keys(words.en).sort());
const script = fs.readFileSync(path.join(root,'Orynivo/Remote/MobileRemote.js'),'utf8');
const html = fs.readFileSync(path.join(root,'Orynivo/Remote/MobileRemote.html'),'utf8')
    .replace('/*REMOTE_WORDS*/','const words = '+JSON.stringify(words)+';').replace('/*REMOTE_SCRIPT*/',script);
const token = 'synthetic-test-key&plus+equals=';
const commands = [], errors = [], requests = [];
const track = {id:'server:synthetic:42',title:'A song with a longer title',artist:'Example Artist',album:'Example Album',year:1998,source:'Music server'};
const state = {status:'playing',title:track.title,artist:track.artist,album:track.album,positionSeconds:65,durationSeconds:240,volume:.35,isFavorite:true,artworkKey:'fixture',queue:[{index:0,isCurrent:true,title:track.title}]};
state.playback = {mediaKind:'music',audioFormat:'FLAC · 44,100 Hz · 2 ch',canSeek:true};
const eventClients = new Set();
const broadcast = () => {for(const client of eventClients) client.write('event: state\ndata: '+JSON.stringify(state)+'\n\n');};
let artworkMissing = false, failNextPlay = false;
let deny = false, releaseRadio;
let signalRadioHeld;
const radioHeld = new Promise(resolve=>{signalRadioHeld=resolve;});
const server = http.createServer(async(req,res)=>{
    requests.push(req.url);
    if(!req.url.startsWith('/remote/api/')) {
        res.setHeader('Content-Type','text/html');
        res.setHeader('Content-Security-Policy',"default-src 'self'; connect-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; frame-ancestors 'none'");
        res.end(html); return;
    }
    if(deny || req.headers.authorization !== 'Bearer '+token){res.writeHead(401);res.end();return;}
    if(req.url.includes('/media/radio?q=failure')) {res.writeHead(502);res.end();return;}
    if(req.url.includes('/media/radio?q=slow')) {
        releaseRadio = () => {res.setHeader('Content-Type','application/json');res.end(JSON.stringify([{id:'old',title:'Obsolete radio'}]));};
        signalRadioHeld();
        return;
    }
    if(req.method==='POST'){
        let body='';for await(const chunk of req) body+=chunk;
        const value=JSON.parse(body);commands.push({url:req.url,body:value});
        const starts = req.url.endsWith('/play') || value.action==='play' || value.command==='queue-index';
        if(starts && failNextPlay){failNextPlay=false;res.writeHead(400);res.end();return;}
        if(starts){
            artworkMissing=false;
            Object.assign(state,{title:track.title,artist:track.artist,album:track.album,durationSeconds:240,artworkKey:'music',
                playback:{mediaKind:'music',audioFormat:'FLAC · 44,100 Hz · 2 ch',canSeek:true}});
            if(req.url.includes('/media/radio/')) {
                artworkMissing=true;
                Object.assign(state,{title:'Live radio title',artist:'Live artist',album:null,isFavorite:null,durationSeconds:0,artworkKey:'radio-pending',
                    playback:{mediaKind:'radio',sourceName:'Synthetic radio',details:'Jazz · DE · MP3 · 128 kbps',canSeek:false}});
            } else if(req.url.includes('/media/podcasts/'))
                Object.assign(state,{title:'Synthetic episode',artist:'Synthetic podcast',album:null,isFavorite:null,durationSeconds:1800,artworkKey:'podcast',
                    playback:{mediaKind:'podcasts',sourceName:'Publisher',details:'Science',audioFormat:'AAC · 48,000 Hz',publishedAt:'2026-01-02T00:00:00Z',canSeek:true}});
        }
        res.writeHead(204);res.end();return;
    }
    if(req.url.endsWith('/events')){
        res.writeHead(200,{'Content-Type':'text/event-stream'});
        eventClients.add(res);res.on('close',()=>eventClients.delete(res));
        res.write('event: state\ndata: '+JSON.stringify(state)+'\n\n');return;
    }
    let data;
    if(req.url.endsWith('/state')) data=state;
    else if(req.url.startsWith('/remote/api/artwork')){
        if(artworkMissing){res.writeHead(404);res.end();return;}
        res.writeHead(200,{'Content-Type':'image/svg+xml'});
        res.end('<svg xmlns="http://www.w3.org/2000/svg" width="320" height="320"><rect width="320" height="320" fill="#235972"/><circle cx="160" cy="160" r="90" fill="#62437b"/><circle cx="160" cy="160" r="25" fill="#20364b"/></svg>');return;
    }
    else if(req.url.endsWith('/outputs')) data=[{name:'Living room',selected:true}];
    else if(req.url.includes('/media/radio')) data=[{id:'station-id',title:'Synthetic radio',subtitle:'MP3'}];
    else if(req.url.includes('/media/podcasts/podcast-id/episodes')) data=[{id:'episode-id',title:'Synthetic episode',subtitle:'Synthetic podcast',durationSeconds:1800,publishedAt:'2026-01-02T00:00:00Z'}];
    else if(req.url.includes('/media/podcasts')) data=[{id:'podcast-id',title:'Synthetic podcast',subtitle:'Publisher'}];
    else if(req.url.endsWith('/playlists')) data=[{id:1,name:'Evening favorites',trackCount:2,isSmart:false},{id:2,name:'Smart discovery',trackCount:null,isSmart:true}];
    else if(req.url.includes('/library/artists')) data=[{id:'local:1',name:'Example Artist',source:'Local'}];
    else if(req.url.includes('/library/albums')) data=[{id:'local:2',title:'Example Album',year:1998,source:'Local'}];
    else data=[track];
    res.setHeader('Content-Type','application/json');res.end(JSON.stringify(data));
});
(async()=>{
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
    const url='http://127.0.0.1:'+server.address().port+'/remote';
    const browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{})});
    try {
        const context=await browser.newContext({viewport:{width:390,height:844},locale:'de-DE',reducedMotion:'reduce'});
        const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
        await page.goto(url);
        assert(await page.locator('#login').isVisible(),'Manual URL must require token');
        await page.locator('#connect').click();
        await page.waitForFunction(()=>document.getElementById('authError').textContent.length>0);
        await page.locator('#token').fill('invalid');
        await page.locator('#connect').click();
        await page.waitForFunction(()=>document.getElementById('authError').textContent.includes('nicht gültig'));
        await page.goto(url+'#token='+encodeURIComponent(token));
        await page.waitForSelector('#app',{state:'visible'});
        await page.waitForSelector('#cover',{state:'visible'});
        assert.equal(new URL(page.url()).hash,'');
        assert.equal(await page.evaluate(()=>sessionStorage.length),0);
        assert.equal(await page.locator('#status').textContent(),words.de.playing);
        assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Phone width must not overflow');
        await page.screenshot({path:path.join(root,'out/mobile-remote-phone.png'),fullPage:true});
        await page.locator('[data-view=radio]').click();
        await page.waitForSelector('#radioList .result-actions button');
        failNextPlay=true;
        await page.locator('#radioList .result-actions button').click();
        await page.waitForFunction(text=>document.getElementById('message').textContent===text,words.de.failed);
        assert(await page.locator('#radio').isVisible(),'Rejected play must retain browsing');
        await page.locator('#radioList .result-actions button').click();
        await page.waitForFunction(()=>!document.querySelector('#radioList .result-actions button').disabled);
        await page.locator('#playing').waitFor({state:'visible'});
        await page.waitForFunction(()=>document.getElementById('trackTitle').textContent==='Live radio title');
        assert.equal(await page.locator('#sourceName').textContent(),'Synthetic radio');
        assert.equal(await page.locator('#album').textContent(),'');
        assert(await page.locator('#seek').isDisabled());
        artworkMissing=false;state.artworkKey='radio-ready';broadcast();
        await page.waitForFunction(()=>!document.getElementById('cover').hidden&&document.getElementById('cover').naturalWidth>0);
        await page.screenshot({path:path.join(root,'out/mobile-remote-radio-playing.png'),fullPage:true});
        assert(commands.some(x=>x.url==='/remote/api/media/radio/play'&&x.body.id==='station-id'));
        await page.locator('[data-view=radio]').click();
        await page.locator('#radioSearch').fill('jazz');
        await Promise.all([page.waitForRequest(r=>r.url().includes('/media/radio?q=jazz')),
            page.locator('#radioSearch').press('Enter')]);
        await page.waitForSelector('#radioList .result-actions button');
        await page.locator('#radioRefresh').click();
        await page.waitForSelector('#radioList .result-actions button');
        assert.equal(await page.locator('#radioSearch').inputValue(),'');
        await page.locator('#radioSearch').fill('failure');
        await page.locator('#radioSearch').press('Enter');
        await page.waitForFunction(text=>document.getElementById('radioList').textContent===text,words.de.failed);
        await page.locator('#radioSearch').fill('slow');
        await Promise.all([page.waitForRequest(r=>r.url().includes('/media/radio?q=slow')),
            page.locator('#radioSearch').press('Enter')]);
        await page.locator('[data-view=podcasts]').click();
        await radioHeld;
        releaseRadio();
        await page.getByRole('button',{name:'Synthetic podcast',exact:true}).click();
        await page.waitForSelector('#podcastsList .result-actions button');
        assert((await page.locator('#podcastsList').textContent()).includes('30:00'));
        await page.locator('#podcastsList .result-actions button').click();
        await page.waitForFunction(()=>!document.querySelector('#podcastsList .result-actions button').disabled);
        await page.locator('#playing').waitFor({state:'visible'});
        await page.waitForFunction(()=>document.getElementById('trackTitle').textContent==='Synthetic episode');
        assert.equal(await page.locator('#artist').textContent(),'Synthetic podcast');
        assert.equal(await page.locator('#sourceName').textContent(),'Publisher');
        assert((await page.locator('#published').textContent()).includes('2026'));
        assert.equal(await page.locator('#audioFormat').textContent(),'AAC · 48,000 Hz');
        assert(await page.locator('#seek').isEnabled());
        assert(commands.some(x=>x.url==='/remote/api/media/podcasts/play'&&x.body.id==='episode-id'));
        await page.screenshot({path:path.join(root,'out/mobile-remote-podcasts.png'),fullPage:true});
        await page.locator('[data-view=podcasts]').click();
        await page.getByRole('button',{name:'Synthetic podcast',exact:true}).click();
        await page.locator('#podcastsBack').click();
        await page.getByRole('button',{name:'Synthetic podcast',exact:true}).waitFor();
        await page.locator('#podcastsSearch').fill('science');
        await Promise.all([page.waitForRequest(r=>r.url().includes('/media/podcasts?q=science')),
            page.locator('#podcastsSearch').press('Enter')]);
        await page.getByRole('button',{name:'Synthetic podcast',exact:true}).waitFor();
        assert(!(await page.locator('#radioList').textContent()).includes('Obsolete radio'),'Abandoned media responses must not publish');
        assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Media tabs must not overflow phone');
        await page.locator('[data-view=playlists]').click();
        await page.waitForSelector('#playlistList .row-name');
        await page.screenshot({path:path.join(root,'out/mobile-remote-playlists.png'),fullPage:true});
        await page.getByRole('button',{name:'Evening favorites',exact:true}).click();
        await page.waitForSelector('#playlistPlay',{state:'visible'});
        await page.locator('#playlistPlay').click();
        await page.waitForFunction(()=>!document.getElementById('playlistPlay').disabled);
        await page.locator('#playing').waitFor({state:'visible'});
        assert(commands.some(x=>x.url==='/remote/api/playlists/1/queue'&&x.body.action==='play'));
        await page.locator('[data-view=playlists]').click();
        await page.locator('#playlistAppend').click();
        await page.waitForFunction(()=>!document.getElementById('playlistAppend').disabled);
        assert(commands.some(x=>x.body.action==='append'));
        assert(await page.locator('#playlists').isVisible(),'Append must retain browsing');
        await page.locator('#playlistList button').filter({hasText:words.de.playNext}).click();
        await page.waitForTimeout(100);
        assert(commands.some(x=>x.body.id===track.id&&x.body.action==='next'));
        await page.locator('[data-view=library]').click();
        await page.getByRole('button',{name:'Example Artist',exact:true}).click();
        await page.getByRole('button',{name:'Example Album',exact:true}).click();
        await page.waitForSelector('#libraryList .result-actions');
        await page.locator('#libraryList .result-actions button').filter({hasText:words.de.play}).first().click();
        await page.locator('#playing').waitFor({state:'visible'});
        await page.waitForFunction(()=>document.getElementById('album').textContent==='Example Album');
        await page.locator('[data-view=queue]').click();
        await page.locator('#queueList .row-name').click();
        await page.locator('#playing').waitFor({state:'visible'});
        await page.setViewportSize({width:1024,height:768});
        await page.screenshot({path:path.join(root,'out/mobile-remote-tablet.png'),fullPage:true});
        assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
        deny=true;
        await page.locator('[data-cmd=next]').click();
        await page.waitForSelector('#login',{state:'visible'});
        assert.equal(await page.locator('#token').inputValue(),'');
        deny=false;
        await page.reload();assert(await page.locator('#login').isVisible());
        await page.locator('#token').fill(token);
        await page.locator('#token').press('Enter');
        await page.waitForSelector('#app',{state:'visible'});
        await page.locator('#logout').click();
        assert(await page.locator('#login').isVisible(),'Sign out must clear authentication');
        for(const lang of Object.keys(words).filter(lang => lang !== 'de')){
            const c=await browser.newContext({locale:lang,viewport:{width:320,height:700}});
            const p=await c.newPage();p.on('pageerror',e=>errors.push(e.message));
            await p.goto(url+'#token='+encodeURIComponent(token));
            await p.waitForSelector('#app',{state:'visible'});
            assert.equal(await p.locator('html').getAttribute('lang'),lang);
            assert.equal(await p.locator('#logout').textContent(),words[lang].logout);
            await p.locator('[data-view=radio]').click();
            assert.equal(await p.locator('#radioTitle').textContent(),words[lang].radio);
            await p.locator('#radioList .result-actions button').waitFor();
            await p.locator('[data-view=podcasts]').click();
            await p.getByRole('button',{name:'Synthetic podcast',exact:true}).click();
            await p.locator('#podcastsList .result-actions button').waitFor();
            assert.equal(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,lang+' 320px overflow');
            await c.close();
        }
        assert.deepEqual(errors,[]);
        assert(requests.every(x=>!x.includes(token)&&!x.includes('token=')),'No token may occur in request URLs');
        console.log('PASS: QR/manual/invalid/expired login, memory-only credentials, playlists, radio search/play/failure/stale response, podcast search/episode/play, navigation, artwork CSP, all '+Object.keys(words).length+' mobile languages, 320/390/1024px layout.');
    } finally {await browser.close();server.closeAllConnections();server.close();}
})().catch(e=>{console.error(e);process.exitCode=1;server.closeAllConnections();server.close();});
