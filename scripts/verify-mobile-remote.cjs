// Run with Node.js and Playwright available (NODE_PATH may point to a shared installation).
// All network traffic and credentials below are synthetic; the real player is never contacted.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '..');
const words = JSON.parse(fs.readFileSync(path.join(root,'Orynivo/Localization/MobileRemote.json'),'utf8'));
for (const lang of ['de','en','fr','es']) assert.deepEqual(Object.keys(words[lang]).sort(),Object.keys(words.en).sort());
const script = fs.readFileSync(path.join(root,'Orynivo/Remote/MobileRemote.js'),'utf8');
const html = fs.readFileSync(path.join(root,'Orynivo/Remote/MobileRemote.html'),'utf8')
    .replace('/*REMOTE_WORDS*/','const words = '+JSON.stringify(words)+';').replace('/*REMOTE_SCRIPT*/',script);
const token = 'synthetic-test-key&plus+equals=';
const commands = [], errors = [], requests = [];
const track = {id:'server:synthetic:42',title:'A song with a longer title',artist:'Example Artist',album:'Example Album',year:1998,source:'Music server'};
const state = {status:'playing',title:track.title,artist:track.artist,album:track.album,positionSeconds:65,durationSeconds:240,volume:.35,isFavorite:true,artworkKey:'fixture',queue:[{index:0,isCurrent:true,title:track.title}]};
let deny = false;
const server = http.createServer(async(req,res)=>{
    requests.push(req.url);
    if(!req.url.startsWith('/remote/api/')) {
        res.setHeader('Content-Type','text/html');
        res.setHeader('Content-Security-Policy',"default-src 'self'; connect-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; frame-ancestors 'none'");
        res.end(html); return;
    }
    if(deny || req.headers.authorization !== 'Bearer '+token){res.writeHead(401);res.end();return;}
    if(req.method==='POST'){
        let body='';for await(const chunk of req) body+=chunk;
        commands.push({url:req.url,body:JSON.parse(body)});res.writeHead(204);res.end();return;
    }
    if(req.url.endsWith('/events')){
        res.writeHead(200,{'Content-Type':'text/event-stream'});
        res.write('event: state\ndata: '+JSON.stringify(state)+'\n\n');return;
    }
    let data;
    if(req.url.endsWith('/state')) data=state;
    else if(req.url.endsWith('/artwork')){
        res.writeHead(200,{'Content-Type':'image/svg+xml'});
        res.end('<svg xmlns="http://www.w3.org/2000/svg" width="320" height="320"><rect width="320" height="320" fill="#235972"/><circle cx="160" cy="160" r="90" fill="#62437b"/><circle cx="160" cy="160" r="25" fill="#20364b"/></svg>');return;
    }
    else if(req.url.endsWith('/outputs')) data=[{name:'Living room',selected:true}];
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
        await page.locator('[data-view=playlists]').click();
        await page.waitForSelector('#playlistList .row-name');
        await page.screenshot({path:path.join(root,'out/mobile-remote-playlists.png'),fullPage:true});
        await page.getByRole('button',{name:'Evening favorites',exact:true}).click();
        await page.waitForSelector('#playlistPlay',{state:'visible'});
        await page.locator('#playlistPlay').click();
        await page.waitForFunction(()=>!document.getElementById('playlistPlay').disabled);
        assert(commands.some(x=>x.url==='/remote/api/playlists/1/queue'&&x.body.action==='play'));
        await page.locator('#playlistAppend').click();
        await page.waitForFunction(()=>!document.getElementById('playlistAppend').disabled);
        assert(commands.some(x=>x.body.action==='append'));
        await page.locator('#playlistList button').filter({hasText:words.de.playNext}).click();
        await page.waitForTimeout(100);
        assert(commands.some(x=>x.body.id===track.id&&x.body.action==='next'));
        await page.locator('[data-view=library]').click();
        await page.getByRole('button',{name:'Example Artist',exact:true}).click();
        await page.getByRole('button',{name:'Example Album',exact:true}).click();
        await page.waitForSelector('#libraryList .result-actions');
        await page.locator('[data-view=playing]').click();
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
        for(const lang of ['en','fr','es']){
            const c=await browser.newContext({locale:lang,viewport:{width:320,height:700}});
            const p=await c.newPage();p.on('pageerror',e=>errors.push(e.message));
            await p.goto(url+'#token='+encodeURIComponent(token));
            await p.waitForSelector('#app',{state:'visible'});
            assert.equal(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,lang+' 320px overflow');
            await c.close();
        }
        assert.deepEqual(errors,[]);
        assert(requests.every(x=>!x.includes(token)&&!x.includes('token=')),'No token may occur in request URLs');
        console.log('PASS: QR login, manual/invalid/expired login, in-memory credentials, playlist play/append/track actions, library navigation, artwork CSP, 4 languages, 320/390/1024px layout.');
    } finally {await browser.close();server.closeAllConnections();server.close();}
})().catch(e=>{console.error(e);process.exitCode=1;server.closeAllConnections();server.close();});
