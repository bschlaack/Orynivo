# Orynivo Programmwebsite

Statische, responsive Produktwebsite für Orynivo. Englisch ist die
Standardsprache; Deutsch, Französisch und Spanisch sind direkt umschaltbar.
Alle Dateien und verwendeten Medien liegen in diesem Verzeichnis. Für
Suchmaschinen stehen statische Sprachfassungen unter `/`, `/de/`, `/fr/` und
`/es/` mit Canonical-, hreflang- und strukturierten Softwaredaten bereit.

Nach Änderungen an gemeinsamen Inhalten oder Übersetzungen werden die
Sprachfassungen neu erzeugt:

```powershell
node html/generate-localized-pages.js
```

## Lokal ansehen

```powershell
python -m http.server 8080 --directory html
```

Danach `http://localhost:8080` öffnen. Ein lokaler Webserver ist sinnvoll, weil
die Seite die öffentliche GitHub-API abfragt, um Version und Downloadlinks des
jeweils neuesten Releases einzusetzen. Fällt die Abfrage aus, führen alle
Download-Schaltflächen weiterhin zur aktuellen GitHub-Release-Seite.

## Veröffentlichung

Der komplette Ordner kann unverändert auf den vorhandenen nginx-Webserver
hochgeladen werden. Als Dokumentenwurzel muss dabei `html/` verwendet werden.
Die `sitemap.xml` sollte anschließend in der Google Search Console und bei
Bing Webmaster Tools eingereicht werden.
