# Orynivo product website

Static, responsive product website for Orynivo. English is the default
language; German, French, and Spanish can be selected directly. All files and
media used by the site live in this directory. Static localized versions are
available under `/`, `/de/`, `/fr/`, and `/es/`, including canonical,
`hreflang`, and structured software metadata for search engines.

After changing shared content or translations, regenerate the localized pages:

```powershell
node html/generate-localized-pages.js
```

## Preview locally

```powershell
python -m http.server 8080 --directory html
```

Then open `http://localhost:8080`. A local web server is recommended because the
site queries the public GitHub API to populate the current version and download
links for the latest release. If that request fails, all download buttons still
lead to the current GitHub Releases page.

## Deployment

The complete directory can be uploaded unchanged to the existing nginx server.
Use `html/` as the document root. Submit `sitemap.xml` to Google Search Console
and Bing Webmaster Tools after deployment.
