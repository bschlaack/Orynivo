#!/bin/bash
set -e

# Create system group and user for the service
if ! getent group orynivo-server > /dev/null 2>&1; then
    groupadd --system orynivo-server
fi
if ! id -u orynivo-server > /dev/null 2>&1; then
    useradd --system --no-create-home --shell /sbin/nologin \
        --gid orynivo-server orynivo-server
fi

# Ensure correct permissions
chmod 755 /usr/lib/orynivo-server/Orynivo.Server
chown -R root:root /usr/lib/orynivo-server
chown -R orynivo-server:orynivo-server /etc/orynivo-server

# Data directory for the service (SQLite database, caches, downloaded artwork).
# The service user has no writable home, so the server is pointed here via
# ORYNIVO_DATA_DIR in the systemd unit; create it owned by the service user.
install -d -o orynivo-server -g orynivo-server /var/lib/orynivo-server

systemctl daemon-reload
systemctl enable --now orynivo-server-updater.path
