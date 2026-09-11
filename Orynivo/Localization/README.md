# Localization

German, English, French, Spanish, Russian, Simplified Chinese and Hindi all use
complete LocalizedStrings instances in LocalizationManager.cs. Strings.cs
defines the shared schema. There are no JSON overlays or language-specific
English inheritance paths.

Hindi uses the appended `Language.Hindi` enum value, the `hi-IN` culture,
and the `hi` website, artist-profile and mobile-remote language code. Do not
renumber persisted enum values. The mobile remote independently selects its
language from the browser locale.

For every new visible string, add the key to Strings.cs and supply its value
in every language initializer. Preserve all composite-format placeholders,
including indexes and format specifiers. Use localized resources in views,
dialogs and dynamically generated messages.

Run from the repository root:

```powershell
node html/generate-localized-pages.js
./scripts/verify-localization-parity.ps1
dotnet run --project scripts/LocalizationSmoke/LocalizationSmoke.csproj
dotnet build Orynivo/Orynivo.csproj
```

The checker verifies full key coverage and placeholder parity for all seven
languages, website resource completeness and gallery initialization. It does
not replace linguistic review or visual layout testing. The compiled smoke
test also exercises all format strings and switches resources through all
languages in both directions without launching the desktop UI.
