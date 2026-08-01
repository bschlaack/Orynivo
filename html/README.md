# Orynivo Product Website

Static, responsive product website for Orynivo. English is the default
language; German, French, and Spanish are directly selectable. All required
files and media assets live in this directory. Search engines receive static
localized pages at `/`, `/de/`, `/fr/`, and `/es/`, complete with canonical
URLs, hreflang references, and structured software data.

After changing shared content or translations, regenerate the localized pages:

```powershell
node html/generate-localized-pages.js
```

## Local Preview

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
