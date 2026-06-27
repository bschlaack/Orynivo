#!/bin/bash
set -e

case "$1" in
    remove|purge)
        systemctl stop orynivo-server 2>/dev/null || true
        systemctl disable orynivo-server 2>/dev/null || true
        ;;
esac
