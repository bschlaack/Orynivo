#!/bin/bash
set -euo pipefail

ROOT=/var/lib/orynivo-server/updates
MANIFEST="$ROOT/update-manifest.json"
SIGNATURE="$ROOT/update-manifest.sig"
PACKAGE="$ROOT/package.bin"
PACKAGE_NAME_FILE="$ROOT/package-name"
READY="$ROOT/ready"
STATUS="$ROOT/status"
PUBLIC_KEY=/usr/lib/orynivo-server/update-public-key.der

fail() { printf 'failed\n' > "$STATUS"; rm -f "$READY"; }
trap fail ERR
rm -f "$READY"
printf 'installing\n' > "$STATUS"
test -s "$MANIFEST" -a -s "$SIGNATURE" -a -s "$PACKAGE" -a -s "$PACKAGE_NAME_FILE" -a -s "$PUBLIC_KEY"

PACKAGE_NAME="$(cat "$PACKAGE_NAME_FILE")"
case "$PACKAGE_NAME" in
    orynivo-server_*_amd64.deb|orynivo-server_*_arm64.deb) PACKAGE_TYPE=deb ;;
    orynivo-server-*-1.x86_64.rpm|orynivo-server-*-1.aarch64.rpm) PACKAGE_TYPE=rpm ;;
    *) exit 1 ;;
esac

openssl dgst -sha256 -verify "$PUBLIC_KEY" -keyform DER -signature "$SIGNATURE" "$MANIFEST"
HASH="$(sha256sum "$PACKAGE" | cut -d' ' -f1)"
grep -Fq "\"file\":\"$PACKAGE_NAME\"" "$MANIFEST"
grep -Fq "\"sha256\":\"$HASH\"" "$MANIFEST"

if [ "$PACKAGE_TYPE" = deb ]; then
    dpkg-deb --info "$PACKAGE" >/dev/null
    NEW_VERSION="$(dpkg-deb -f "$PACKAGE" Version)"
    CURRENT_VERSION="$(dpkg-query -W -f='${Version}' orynivo-server 2>/dev/null || printf '0')"
    dpkg --compare-versions "$NEW_VERSION" gt "$CURRENT_VERSION"
    dpkg -i "$PACKAGE"
else
    rpm -Uvh "$PACKAGE"
fi

printf 'installed\n' > "$STATUS"
trap - ERR
systemctl daemon-reload
systemctl restart orynivo-server
