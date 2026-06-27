#!/bin/bash
# $1 = 0 means uninstall; $1 = 1 means upgrade
if [ "$1" -eq 0 ]; then
    systemctl stop orynivo-server 2>/dev/null || true
    systemctl disable orynivo-server 2>/dev/null || true
fi
