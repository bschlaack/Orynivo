const fs = require('fs');
const path = require('path');
const {translations, pageMetadata, languageUrls} = require('./i18n.js');
const root = __dirname;
const sourcePath = fs.existsSync(path.join(root, 'de', 'index.html')) ? path.join(root, 'de', 'index.html') : path.join(root, 'index.html');
const source = fs.readFileSync(sourcePath, 'utf8')
  .replace(/\r\n?/g, '\n')
  .replace(/^\s*<meta property="og:(?:site_name|url|locale)"[^>]*>\r?\n/gm, '')
  .replace(/^\s*<meta name="twitter:(?:title|description|image)"[^>]*>\r?\n/gm, '')
  .replace(/^\s*<link rel="(?:canonical|alternate)"[^>]*>\r?\n/gm, '')
  .replace(/^\s*<script type="application\/ld\+json">.*?<\/script>\r?\n/gm, '')
  .replace(/<body data-page-language="[^"]+">/, '<body>')
  .replace(/ loading="lazy" decoding="async"/g, '')
  .replace(/ width="\d+" height="\d+"/g, '');
const metadata = Object.fromEntries(Object.entries(pageMetadata).map(([key, value]) => [key, [value.title, value.description]]));
const urls = Object.fromEntries(Object.entries(languageUrls).map(([key, value]) => [key, 'https://orynivo.app' + value]));
function translateText(html, language) {
  if (language === 'de') return html;
  return html.replace(/>([^<>]+)</g, (whole, value) => {
    const key = value.trim();
    const translated = translations[key]?.[language];
    return translated ? `>${value.slice(0, value.indexOf(key))}${translated}${value.slice(value.indexOf(key) + key.length)}<` : whole;
  });
}
function build(language) {
  let html = translateText(source, language)
    .replace(/(alt|aria-label|data-title)="([^"]*)"/g, (whole, attr, key) => translations[key] ? `${attr}="${translations[key][language].replace(/"/g, '&quot;')}"` : whole)
    .replace(/<html lang="[^"]+">/, `<html lang="${language}">`)
    .replace(/<title>[^<]*<\/title>/, `<title>${metadata[language][0]}</title>`)
    .replace(/<meta name="description" content="[^"]*">/, `<meta name="description" content="${metadata[language][1]}">`)
    .replace(/<meta property="og:title" content="[^"]*">/, `<meta property="og:title" content="${metadata[language][0]}">`)
    .replace(/<meta property="og:description" content="[^"]*">/, `<meta property="og:description" content="${metadata[language][1]}">`)
    .replace(/<meta property="og:image" content="[^"]*">/, '<meta property="og:image" content="https://orynivo.app/assets/og.png">')
    .replace(/(<meta property="og:type" content="website">)/, `$1\n  <meta property="og:site_name" content="Orynivo">\n  <meta property="og:url" content="${urls[language]}">\n  <meta property="og:locale" content="${{en:'en_US',de:'de_DE',fr:'fr_FR',es:'es_ES',ru:'ru_RU',zh:'zh_CN',hi:'hi_IN'}[language]}">`)
    .replace(/(<meta name="twitter:card" content="summary_large_image">)/, `$1\n  <meta name="twitter:title" content="${metadata[language][0]}">\n  <meta name="twitter:description" content="${metadata[language][1]}">\n  <meta name="twitter:image" content="https://orynivo.app/assets/og.png">`)
    .replace(/(<link rel="icon"[^>]+>)/, `<link rel="canonical" href="${urls[language]}">\n  <link rel="alternate" hreflang="x-default" href="https://orynivo.app/">\n  ${Object.entries(urls).map(([lang, url]) => `<link rel="alternate" hreflang="${lang}" href="${url}">`).join('\n  ')}\n  $1`)
    .replace('</head>', `  <script type="application/ld+json">${JSON.stringify({'@context':'https://schema.org','@type':'SoftwareApplication',name:'Orynivo',url:urls[language],applicationCategory:'MultimediaApplication',operatingSystem:'Windows 10, Windows 11, Linux, macOS',description:metadata[language][1],license:'https://www.apache.org/licenses/LICENSE-2.0',isAccessibleForFree:true,downloadUrl:'https://github.com/bschlaack/Orynivo/releases/latest',softwareHelp:'https://github.com/bschlaack/Orynivo/wiki',author:{'@type':'Person',name:'Björn Schlaack'}})}</script>\n</head>`)
    .replace(/assets\/screenshots\/([a-z-]+)\.png/g, 'assets/screenshots/$1.webp')
    .replace(/data-shot="([a-z-]+)\.png"/g, 'data-shot="$1.webp"')
    .replace(/(href|src)="(assets\/|styles\.css|wiki-links\.css|i18n\.js|script\.js)/g, '$1="/$2')
    .replace(/src="\/assets\/screenshots\/([^\"]+)"/g, 'src="/assets/screenshots/$1" width="1920" height="1044"')
    .replace(/src="\/assets\/brand\/orynivo\.png"/g, 'src="/assets/brand/orynivo.png" width="2172" height="724"')
    .replace(/src="\/assets\/brand\/icon\.png"/g, 'src="/assets/brand/icon.png" width="812" height="587"')
    .replace(/<img id="main-shot"/, '<img loading="lazy" decoding="async" id="main-shot"')
    .replace(/<body>/, `<body data-page-language="${language}">`);
  const destination = language === 'en' ? path.join(root, 'index.html') : path.join(root, language, 'index.html');
  fs.mkdirSync(path.dirname(destination), {recursive:true});
  fs.writeFileSync(destination, html);
}
Object.keys(languageUrls).forEach(build);

// Derive the sitemap from the same supported locales as the selector and resources.
const alternates = Object.entries(urls).map(([lang, url]) =>
  `<xhtml:link rel="alternate" hreflang="${lang}" href="${url}"/>`).join('')
  + '<xhtml:link rel="alternate" hreflang="x-default" href="https://orynivo.app/"/>';
fs.writeFileSync(path.join(root, 'sitemap.xml'),
  '<?xml version="1.0" encoding="UTF-8"?>\n'
  + '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n'
  + Object.values(urls).map(url => `  <url><loc>${url}</loc>${alternates}</url>`).join('\n')
  + '\n</urlset>\n');
