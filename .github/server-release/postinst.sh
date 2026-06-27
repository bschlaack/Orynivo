#!/bin/bash
set -e

# Create system group and user for the service
if ! getent group orynivo-server > /dev/null 2>&1; then
    groupadd --system orynivo-server
fi
if ! id -u orynivo-server > /dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin \
        --gid orynivo-server orynivo-server
fi

# Ensure correct permissions
chmod 755 /usr/lib/orynivo-server/Orynivo.Server
chown -R root:root /usr/lib/orynivo-server
chown -R orynivo-server:orynivo-server /etc/orynivo-server

# Register the systemd unit (do not auto-start — user must configure first)
systemctl daemon-reload
