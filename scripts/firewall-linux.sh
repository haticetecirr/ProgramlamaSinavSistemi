#!/bin/sh
# Allows students to reach the server on this computer: TCP 5000 (HTTPS) and UDP 47777 (discovery).
# Run on the INSTRUCTOR computer with: sudo sh firewall-linux.sh
# macOS: System Settings > Network > Firewall > Options: allow incoming connections for ProgLab.Server.
set -e

ufw allow 5000/tcp comment 'ProgLab Sunucu HTTPS'
ufw allow 47777/udp comment 'ProgLab Sunucu Kesfi'
echo "Done. Remove later with: sudo ufw delete allow 5000/tcp && sudo ufw delete allow 47777/udp"
