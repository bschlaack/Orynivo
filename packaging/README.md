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
OpenAL and FFmpeg dependencies. It conflicts with the official `orynivo` package
so the same installation paths cannot be owned twice. It preserves managed
binaries without stripping or debug splitting. It does not use a moving `latest`
URL or a `pkgver()` implementation: this is a versioned binary release, and
version/checksum updates should be reviewed together.

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
