// Local static-site smoke test. External release requests are mocked.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
const {languageUrls, galleryTitles} = require('../html/i18n.js');
const root = path.resolve(__dirname, '../html');
const server = http.createServer((req, res) => {
  let relative = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  if (relative.endsWith('/')) relative += 'index.html';
  const file = path.resolve(root, '.' + relative);
  if (!file.startsWith(root + path.sep) || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
  const types = {'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css','.webp':'image/webp','.png':'image/png'};
  res.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
  fs.createReadStream(file).pipe(res);
});
(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = 'http://127.0.0.1:' + server.address().port;
  const browser = await chromium.launch({headless:true, ...(process.platform === 'win32' ? {channel:'msedge'} : {})});
  try {
    for (const [language, url] of Object.entries(languageUrls)) {
      for (const width of [1440, 390]) {
        const page = await browser.newPage({viewport:{width,height:1000}});
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.route('https://**/*', route => route.abort());
        await page.goto(base + url);
        assert.equal(await page.locator('html').getAttribute('lang'), language);
        assert.equal(await page.locator('[data-language]').count(), Object.keys(languageUrls).length);
        await page.locator('[data-shot]').last().click();
        assert.equal(await page.locator('#shot-title').textContent(), galleryTitles[language].at(-1));
        const overflow = await page.evaluate(() => ({
          width:document.documentElement.scrollWidth,
          offenders:[...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > innerWidth + 1 && getComputedStyle(e).position !== 'absolute').slice(0,12).map(e => e.tagName+'.'+e.className)
        }));
        assert.ok(overflow.width <= width, language + ': page overflow at ' + width + ': ' + JSON.stringify(overflow));
        if (language === 'hi') {
          assert.ok((await page.locator('h1').textContent()).includes('आपका संगीत'));
          if (width === 1440 && process.env.ORYNIVO_WEBSITE_SCREENSHOT) {
            await page.evaluate(() => window.scrollTo({top:0,behavior:'instant'}));
            await page.waitForFunction(() => [...document.querySelectorAll('.hero .reveal')].every(e => getComputedStyle(e).opacity === '1'));
            await page.screenshot({path:process.env.ORYNIVO_WEBSITE_SCREENSHOT});
          }
          if (width < 720) await page.locator('.nav-toggle').click();
          await page.locator('[data-language="en"]').click();
          await page.waitForURL(base + '/');
          assert.equal(await page.locator('html').getAttribute('lang'), 'en');
        }
        assert.deepEqual(errors, []);
        await page.close();
      }
    }
    console.log('PASS: all website locales, desktop/mobile layout, gallery and Hindi language navigation.');
  } finally { await browser.close(); server.closeAllConnections(); server.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; server.closeAllConnections(); server.close(); });
