# Community package drafts

These are local starting points for issues
[#178](https://github.com/bschlaack/Orynivo/issues/178) and
[#182](https://github.com/bschlaack/Orynivo/issues/182). They are not published
to AUR or a Homebrew tap. Do not advertise installation commands until the
target repository exists and native acceptance passes.

Version 0.48.1 hashes were checked against the GitHub Release asset metadata on
2026-10-19. This is not a verification of the signed update manifest. Before
publication, download the packages and check their hashes against the manifest
after verifying its ECDSA signature with Orynivo's trusted release public key.
Never use `SKIP` or `:no_check` as a substitute. The Arch archive itself is not
a detached pacman-signed package merely because its digest appears in Orynivo's
signed update manifest.

## Arch

`aur/PKGBUILD` repackages the existing x86-64 release tree with explicit ALSA,
OpenAL and FFmpeg dependencies, plus native .NET and Avalonia libraries. The
managed runtime is bundled; installing `dotnet-runtime` is unnecessary.
`lttng-ust2.12` is optional for CoreCLR tracing. The recipe conflicts with the
official `orynivo` package so the same installation paths cannot be owned twice.
It preserves managed binaries without stripping or debug splitting. It does not
use a moving `latest` URL or a `pkgver()` implementation: this is a versioned
binary release, and version/checksum updates should be reviewed together.

Validation on CachyOS on 2026-10-23:

- The pinned source SHA-256 passed. Arch `extra-x86_64-build` built and
  installed `0.48.1-2` successfully in a clean chroot. Recipe `namcap` reported
  no warnings; package `namcap` retained the upstream/dynamic-library warnings
  below.
- The local repackaged payload preserved all 405 upstream paths, including file
  hashes, modes and symlinks. The chroot-installed package passed `pacman -Qkk`
  with 406 total files and no altered files.
- In the disposable chroot, `pacman` upgraded draft revision 1 to revision 2 and
  replaced the official `orynivo` package with `orynivo-bin`. A synthetic
  profile marker survived both operations. This covers package transactions, not
  a previous application-version upgrade or migration of real user settings.
- With Xvfb, Xauth and DejaVu fonts installed only for acceptance, the command
  opened the Orynivo main window and initial profile dialog as an unprivileged
  test user. `ORYNIVO_DATA_DIR` selected isolated data and its migration marker
  was created. Desktop metadata validation and the installed icon check passed.

- The maintainer reported the manual CachyOS checklist successful after starting
  the extracted revision 2 package with an isolated `ORYNIVO_DATA_DIR`: display
  and artwork, music import and audible playback, pause, seeking, volume and
  settings persistence after restart. This was a user-observed test without
  installing the package on the host.
- The maintainer confirmed that a temporary KDE application-menu entry appeared
  and launched normally. It used the packaged desktop metadata with absolute
  executable/icon paths adjusted to the extracted tree and an isolated data
  root. The temporary entry was removed after the test; the host package was
  never installed.

- A disposable chroot upgraded the official `0.48.0-1` package to AUR draft
  `0.48.1-2`. Both versions opened the same synthetic profile under Xvfb/Openbox
  and exited through the window manager without forced termination. Active
  profile identity/name, one favorite marker, volume and language survived; each
  application saved 98 settings fields. SQLite integrity checks passed before
  and after the upgrade (23 tables). This is an empty-catalog/profile smoke
  test, not exhaustive migration coverage for populated libraries.

No personal profile or host player installation was used. AUR publication also
requires an available maintainer account. The manual playback report supplements
the virtual-display smoke check; it does not cover all audio devices, formats or
output modes.

Interpret `namcap` output in the context of this binary package:

- Unstripped SkiaSharp, HarfBuzzSharp and SQLite binaries and missing FULL RELRO
  are properties of the upstream release. Repackaging preserves those binaries;
  changing compiler flags in this recipe cannot harden prebuilt libraries.
- ALSA, OpenAL, FFmpeg and some framework libraries are loaded dynamically or
  invoked as processes, so static dependency analysis can report them as unused.
- The tracing provider references `liblttng-ust.so.0`; its optional dependency
  follows the Arch .NET runtime package.
- `checkpkg` cannot compare against a repository version of `orynivo-bin` before
  one exists. Its “target not found” message does not invalidate the build.

The native dependency baseline follows the
[Arch .NET runtime](https://archlinux.org/packages/extra/x86_64/dotnet-runtime/)
and
[Avalonia Linux requirements](https://docs.avaloniaui.net/docs/deployment/docker).
The manual CachyOS result establishes playback on the tested setup; broader
hardware coverage remains unverified. The menu check used an extracted package,
while installed file ownership was checked separately in the chroot.

Before submitting from an AUR maintainer account:

1. Run `makepkg --verifysource`, `makepkg --printsrcinfo > .SRCINFO` and
   `namcap PKGBUILD` on Arch.
2. Build with Arch `devtools` in a clean x86-64 chroot and run `namcap` on the
   resulting package. Validate the actual runtime shared-library dependencies.
3. Install on a disposable Arch machine, test the command, desktop launcher,
   artwork, PCM playback and an upgrade from the previous package. Confirm that
   replacing the official package does not remove personal settings.
4. Submit the recipe and generated `.SRCINFO` to `orynivo-bin` on AUR. Only then
   link the real AUR page from Installation and the website.

For each release, update `pkgver`, reset `pkgrel`, replace the hash after
release verification, regenerate `.SRCINFO`, and repeat clean-chroot validation.
An upstream replacement of an existing release artifact requires its own
reviewed checksum/package revision.

## Homebrew

`homebrew/Casks/orynivo.rb` selects a portable app ZIP and checksum for each
architecture. The app artifact installs `Orynivo.app` without running a PKG
installer. It leaves user data alone and documents Gatekeeper without bypassing
it. The application locates or downloads FFmpeg as in the existing portable
distribution; this cask introduces no machine-wide FFmpeg prerequisite.

Before publishing a tap such as `bschlaack/homebrew-orynivo`:

1. Create the tap repository with this `Casks/orynivo.rb` and a README.
2. Run `brew style`, `brew audit --cask --online` and install/uninstall tests in
   that tap on both Apple Silicon and Intel macOS. Confirm `Orynivo.app` exists
   at the archive root and the correct ZIP is selected.
3. Test launch, FFmpeg discovery, PCM playback and upgrades on each
   architecture; record macOS versions and the actual Gatekeeper approval path.
   No native Mac installation or audible playback has been performed here.
4. Link the verified tap from Installation and the website; document
   `brew tap bschlaack/orynivo` followed by `brew install --cask orynivo` only
   once that tap is live.

For each release, update `version` and both hashes after verifying the release
manifest, repeat audit/install tests, and commit the tap update. No automated
publisher or credentials are added by these drafts.

References:

- [Homebrew Cask Cookbook](https://docs.brew.sh/Cask-Cookbook)
- [Arch PKGBUILD reference](https://man.archlinux.org/man/PKGBUILD.5.en)
- [Arch .SRCINFO guidance](https://wiki.archlinux.org/title/.SRCINFO)
