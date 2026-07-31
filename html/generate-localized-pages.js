const fs = require('fs');
const path = require('path');
const vm = require('vm');
const root = __dirname;
const sourcePath = fs.existsSync(path.join(root, 'de', 'index.html')) ? path.join(root, 'de', 'index.html') : path.join(root, 'index.html');
const source = fs.readFileSync(sourcePath, 'utf8')
  .replace(/^\s*<meta property="og:(?:site_name|url|locale)"[^>]*>\r?\n/gm, '')
  .replace(/^\s*<meta name="twitter:(?:title|description|image)"[^>]*>\r?\n/gm, '')
  .replace(/^\s*<link rel="(?:canonical|alternate)"[^>]*>\r?\n/gm, '')
  .replace(/^\s*<script type="application\/ld\+json">.*?<\/script>\r?\n/gm, '')
  .replace(/<body data-page-language="[^"]+">/, '<body>')
  .replace(/ loading="lazy" decoding="async"/g, '')
  .replace(/ width="\d+" height="\d+"/g, '');
const i18nSource = fs.readFileSync(path.join(root, 'i18n.js'), 'utf8');
const context = {};
vm.createContext(context);
vm.runInContext(`${i18nSource.slice(0, i18nSource.indexOf('const languageIndex'))};globalThis.data=translations;`, context);
const indexes = { en: 0, fr: 1, es: 2 };
const metadata = {
  en: ['Orynivo – Hi-Res music player for Windows, Linux & macOS', 'Orynivo is the modern open-source music player for Windows, Linux and macOS—with Hi-Res, DSD, a library server, radio, podcasts and AI chat.'],
  de: ['Orynivo – Hi-Res-Musikplayer für Windows, Linux & macOS', 'Orynivo ist der moderne, quelloffene Musikplayer für Windows, Linux und macOS – mit Hi-Res, DSD, Bibliotheksserver, Radio, Podcasts und KI-Chat.'],
  fr: ['Orynivo – Lecteur Hi-Res pour Windows, Linux et macOS', 'Orynivo est un lecteur de musique open source moderne pour Windows, Linux et macOS, avec Hi-Res, DSD, serveur, radio, podcasts et chat IA.'],
  es: ['Orynivo – Reproductor Hi-Res para Windows, Linux y macOS', 'Orynivo es un reproductor de música moderno y de código abierto para Windows, Linux y macOS, con Hi-Res, DSD, servidor, radio, pódcasts y chat de IA.']
};
const urls = { en: 'https://orynivo.app/', de: 'https://orynivo.app/de/', fr: 'https://orynivo.app/fr/', es: 'https://orynivo.app/es/' };
function translateText(html, language) {
  if (language === 'de') return html;
  return html.replace(/>([^<>]+)</g, (whole, value) => {
    const key = value.trim();
    const translated = context.data[key]?.[indexes[language]];
    return translated ? `>${value.slice(0, value.indexOf(key))}${translated}${value.slice(value.indexOf(key) + key.length)}<` : whole;
  });
}
function build(language) {
  let html = translateText(source, language)
    .replace(/<html lang="[^"]+">/, `<html lang="${language}">`)
    .replace(/<title>[^<]*<\/title>/, `<title>${metadata[language][0]}</title>`)
    .replace(/<meta name="description" content="[^"]*">/, `<meta name="description" content="${metadata[language][1]}">`)
    .replace(/<meta property="og:title" content="[^"]*">/, `<meta property="og:title" content="${metadata[language][0]}">`)
    .replace(/<meta property="og:description" content="[^"]*">/, `<meta property="og:description" content="${metadata[language][1]}">`)
    .replace(/<meta property="og:image" content="[^"]*">/, '<meta property="og:image" content="https://orynivo.app/assets/og.png">')
    .replace(/(<meta property="og:type" content="website">)/, `$1\n  <meta property="og:site_name" content="Orynivo">\n  <meta property="og:url" content="${urls[language]}">\n  <meta property="og:locale" content="${{en:'en_US',de:'de_DE',fr:'fr_FR',es:'es_ES'}[language]}">`)
    .replace(/(<meta name="twitter:card" content="summary_large_image">)/, `$1\n  <meta name="twitter:title" content="${metadata[language][0]}">\n  <meta name="twitter:description" content="${metadata[language][1]}">\n  <meta name="twitter:image" content="https://orynivo.app/assets/og.png">`)
    .replace(/(<link rel="icon"[^>]+>)/, `<link rel="canonical" href="${urls[language]}">\n  <link rel="alternate" hreflang="x-default" href="https://orynivo.app/">\n  <link rel="alternate" hreflang="en" href="https://orynivo.app/">\n  <link rel="alternate" hreflang="de" href="https://orynivo.app/de/">\n  <link rel="alternate" hreflang="fr" href="https://orynivo.app/fr/">\n  <link rel="alternate" hreflang="es" href="https://orynivo.app/es/">\n  $1`)
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
['de', 'fr', 'es', 'en'].forEach(build);
