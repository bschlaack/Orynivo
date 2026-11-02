# Hermes issue review

Reviewed on 2026-10-19 against the current working repository. The five issues
were posted through the maintainer account; their author field does not
separately identify Hermes. Findings below use the issue text and actual source.
The linked issues track implementation progress and remaining acceptance.

| Issue                                                   | Finding and local work                                                                                                                                                                                                             | Remaining acceptance                                                                                                                               |
| ------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| [#178](https://github.com/bschlaack/Orynivo/issues/178) | No AUR recipe existed in this repository. Added a pinned `orynivo-bin` recipe under `packaging/aur/`. The published Arch archive is covered by the signed update manifest; it is not necessarily a pacman-signed archive.          | Arch clean-chroot build, native dependency/installation/upgrade validation, AUR maintainer publication, then real website/wiki links.              |
| [#179](https://github.com/bschlaack/Orynivo/issues/179) | Missing parser/DSP coverage confirmed. Added 23 offline cases. Thirteen failed before the parser correction; all passed afterward. Malformed directives and non-finite numbers are rejected, neutral profiles accepted.            | Debug and Release complete verification passed. No physical audio claim.                                                                           |
| [#180](https://github.com/bschlaack/Orynivo/issues/180) | Added Caddy/nginx HTTPS guidance in `SERVER-REVERSE-PROXY.md` and the local wiki, including path-prefix stripping, authentication, range/SSE handling and avoiding query logs.                                                     | Human end-to-end reports with Orynivo, both proxy versions and a real MCP client; deployment-specific DNS/certificate/firewall setup.              |
| [#181](https://github.com/bschlaack/Orynivo/issues/181) | This requests a native-speaker linguistic review, rather than missing keys or a demonstrated code defect. Existing parity cannot certify translation quality. No speculative translation replacement or native-speaker claim made. | A native reviewer chooses one language and a bounded UI area, reports each changed string and reason, then checks format parity and actual layout. |
| [#182](https://github.com/bschlaack/Orynivo/issues/182) | Added an architecture-specific cask under `packaging/homebrew/`, pinned to real release hashes, plus an update/acceptance checklist. Existing Gatekeeper limitations remain explicit.                                              | Publish the tap, run Homebrew audit/style and native install/upgrade/playback checks on Intel and Apple Silicon, then link the actual tap.         |

## Equalizer details

The importer supports preamp, `GraphicEQ` and the existing peak/shelf/pass
filters with `Fc`, optional `Gain` and `Q`. Issue #179's `Bw/Gb` wording is not
the syntax of the implemented parser. Equalizer APO's valid `BW Oct` variant was
previously swallowed as a suffix and replaced with default Q. It is now
explicitly rejected because the current profile contract has no bandwidth field.
Supporting its full response is separate work; importing a changed response
silently is unacceptable.

DSP checks use synthetic stereo frames. They cover exact unity/disabled bypass,
steady-state center-frequency gain, externally scaled +6 dB input, profile
crossfade and state reset. The boost case proves gain composition at the Core
processing boundary; it does not exercise a desktop backend or a physical DAC.

See [package drafts](../packaging/README.md) for publication and native
acceptance steps, and [proxy guidance](SERVER-REVERSE-PROXY.md) for
configuration details.

## Final local verification

Debug and Release each passed all eleven `scripts/verify-all.ps1` checks,
including all three test projects, 66 headless UI groups, compatibility builds
and localization parity. The 23 new equalizer cases are included in both runs.

Actual task-local Caddy 2.11.7 and nginx 1.30.6 passed synthetic TLS proxy
checks on Windows with substituted loopback ports and test certificates. Both
preserved authentication, paths/queries, unauthorized responses, exact range
bytes and early SSE delivery. This is not native package installation, real MCP
or production certificate/long-session evidence.

The AUR draft passes Bash syntax checking. No Ruby/Homebrew runtime or Arch
clean-chroot validation was available, so both distribution drafts remain open
for native acceptance. The proxy guide was mirrored into the local wiki and
linked from its server page/sidebar. Publication does not replace the native and
human acceptance steps listed above.
