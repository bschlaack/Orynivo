# AGENTS.md

## Mandatory Completion Checklist

Before completing any code change:

1. Build and test every affected project in proportion to the change.
2. Update `CHANGELOG.md` under **Unreleased** for every notable user-visible,
   architectural, build, compatibility, or bug-fix change.
3. Update `README.md` whenever features, requirements, build steps, supported
   formats, or known limitations change.
4. Update the applicable architecture reference and `AGENTS.md` routing or
   invariants whenever architecture, behavior, or constraints change.
5. Add or update English XML documentation for every affected public or internal
   C# type and member, including parameter and return documentation.
6. Add every new or changed visible string to German, English, French, Spanish,
   Russian, Simplified Chinese, and Hindi localization resources; never
   hard-code visible UI text. For every new feature, also update all currently
   supported interface languages (including Russian, Simplified Chinese and
   Hindi) in the complete built-in resources in
   `Orynivo/Localization/LocalizationManager.cs`. Run
   `scripts/verify-localization-parity.ps1` before completing the change so
   language files cannot silently drift apart.
7. Verify that credentials, authenticated URLs, and secrets are not persisted,
   logged, documented, or exposed to models.

Documentation and verification are part of the implementation. They must not be
deferred until the user asks for them separately.

## Instruction Routing

The closest `AGENTS.md` applies in addition to this repository-wide file:

- `Orynivo/AGENTS.md` — Avalonia UI, navigation, playback, Dashboard, remote
  library integration, localization, and Windows-specific behavior.
- `Orynivo.Core/AGENTS.md` — cross-platform library, database, scanning, search,
  web, audio-processing primitives, and streaming contracts/clients.
- `Orynivo.Server/AGENTS.md` — ASP.NET endpoints, authentication, configuration,
  packaging, service operation, and server-side caches.
- `Native/AGENTS.md` — Steinberg ASIO/cwASIO bridge code and native builds.

Read the applicable nested file before editing files in that subtree. The
detailed [project reference](docs/PROJECT-REFERENCE.md) is cumulative: read the
sections relevant to the planned change. It must not override the completion
checklist.

## XML Documentation Comments

All public and internal C# types, members, and parameters must carry XML
documentation comments (`///`). Write comments in **English**.

- Every `class`, `interface`, `enum`, `record`, `struct`, and `delegate` needs a
  `<summary>`.
- Every public or internal method and property needs a `<summary>`.
- Every method parameter needs a `<param name="…">` tag.
- Non-void methods need a `<returns>` tag.
- Use `<see cref="…"/>` for cross-references and `<see langword="…"/>` for
  keywords like `true`, `false`, and `null`.
- Generated files (under `obj/`) and AXAML code-behind auto-properties are
  exempt.

When adding or changing C# code, add or update the XML comments for all affected
members in the same edit. Never leave a newly introduced public or internal
declaration without a `<summary>`.

## Maintenance Requirement

Keep this file updated whenever architecture, build behavior, or user-visible
behavior changes.

Update `README.md` whenever features, requirements, build steps, supported
formats, or known limitations change so the public GitHub documentation matches
the actual project.

Keep `CHANGELOG.md` updated for every notable user-visible, architectural,
build, compatibility, or bug-fix change. Add ongoing work under **Unreleased**
and move those entries into a dated version section when preparing a release.

### Documentation language

- `README.md`, `CHANGELOG.md`, and the separate `Orynivo.wiki` repository must
  use Orynivo's official English UI and feature names (for example **AI Chat**,
  **Up Next**, **Play more like this**, **Create output**, **Configure output**,
  and **Delete output**) whenever they refer to visible product functions.
- Apply this consistently to historical entries as well as new documentation;
  explanatory prose may remain in its surrounding language, but UI labels must
  not be translated ad hoc.

### Markdown formatting

- Every `.md` file must follow markdownlint rule MD032: a list is preceded and
  followed by a blank line.
- Keep every markdown line at or below 80 characters and wrap prose and list
  continuations instead of exceeding it. Code blocks, tables, and long URLs are
  the only exceptions, because wrapping them would change their content.
- Format `.md` files with Prettier
  (`prettier --prose-wrap always --print-width 80 --write`) and check them with
  `markdownlint-cli2` before completing a documentation change.

## Localization Rule

- Do not hard-code new visible UI text or status/error messages in XAML or
  code-behind
- Store all such text under `Orynivo/Localization/`
- Every new or changed string must be provided in German, English, French,
  Spanish, Russian, Simplified Chinese, and Hindi
- A text change is complete only after all supported language resources contain
  meaningful translations

## Documentation map

- `README.md`: public features, setup, supported platforms, and limitations.
- `CHANGELOG.md`: shipped changes and notable pending changes.
- `docs/PROJECT-REFERENCE.md`: library, database, audio, UI, server, and build
  contracts, with repository-relative source paths.
- `docs/VISUALIZER-CONTRACTS.md`: detailed Core and desktop rendering contracts.
- `docs/VISUALIZER-STATUS.md`: current rendering paths, evidence, and limits.
- `DEPENDENCY-MIGRATION.md`: dependency policy and deliberate migration steps.
- `ROADMAP.md`: historical implementation milestones and visualizer follow-up.
- `.codex/project-context.local.md`: local continuity; never overrides rules.

Keep instructions focused on invariants and verification. Put lengthy
implementation explanations in the referenced documents and dated experiments in
historical reports. Do not copy package versions or feature status into multiple
instructions when a maintained reference already owns them.
