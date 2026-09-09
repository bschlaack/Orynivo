# Desktop translation overrides

The desktop UI has a reviewed built-in fallback in
`LocalizationManager.cs`. Language-specific corrections and new translations
can be added here without changing the C# code:

- `ru-RU.json` — Russian overrides
- `zh-CN.json` — Simplified Chinese overrides

Each JSON property name must match a property in `LocalizedStrings`. Missing
properties intentionally use the built-in language value, so a file can be
translated incrementally. Values may contain `{0}`, `{1}`, and other format
placeholders; keep those placeholders unchanged.

The files are embedded into the application at build time. After editing a
file, rebuild Orynivo and switch the language (or restart the application) to
load the changes. Do not put credentials, personal paths, or library data in
translation files.

Before committing translation changes, verify that both language files contain
the same keys:

```powershell
.\scripts\verify-localization-parity.ps1
```
