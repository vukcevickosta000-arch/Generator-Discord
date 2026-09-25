#!/usr/bin/env bash
# Stops processes started by run-local.sh
for p in $(ps aux | grep -E "bin/(Debug|Release)/net8.0/Bloodfall\.(GameServer|Backend)" | grep -v grep | awk '{print $2}'); do kill "$p" && echo "stopped $p"; done
