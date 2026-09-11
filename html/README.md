# Orynivo Product Website

Static, responsive product website for Orynivo. English is the default
language; German, French, Spanish, Russian, Simplified Chinese, and Hindi are directly selectable. All required
files and media assets live in this directory. Search engines receive static
localized pages at `/`, `/de/`, `/fr/`, `/es/`, `/ru/`, `/zh/`, and `/hi/`, complete with canonical
URLs, hreflang references, and structured software data.

All application screenshots use the fictional Neon Harbor demo library. Keep
future website captures free of real library names, covers, playback history,
server names, device identifiers, and filesystem paths.

After changing shared content or translations, regenerate the localized pages:

```powershell
node html/generate-localized-pages.js
```

## Local Preview

All seven languages use the same named fields in `i18n.js`; no supplemental
RU/ZH dictionaries or positional translation arrays are used. Metadata and
gallery titles are shared by the generator and browser. Missing resource
values fail validation. Accessible labels and image descriptions are generated
in the selected language as well. Run `node scripts/verify-localization.cjs`
from the repository root after regeneration.

With Node.js and Playwright available, run
`node scripts/verify-website-locales.cjs` to exercise all language pages,
desktop/mobile layouts, gallery titles and Hindi language navigation. The test
serves only local files and blocks external release requests. Hindi uses
Devanagari system-font fallbacks and script-appropriate heading spacing.

```powershell
python -m http.server 8080 --directory html
```

Then open `http://localhost:8080`. Using a local web server is recommended
because the site requests the public GitHub API to populate the current version
and download links for the latest release. If the request fails, all download
buttons continue to link to the latest GitHub Releases page.

## Publishing

The complete directory can be uploaded unchanged to the existing nginx web
server. Configure `html/` as the document root. After publishing, submit
`sitemap.xml` to Google Search Console and Bing Webmaster Tools.
