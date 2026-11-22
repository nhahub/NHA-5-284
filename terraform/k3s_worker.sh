#!/bin/bash
set -euo pipefail

echo "i am worker" > /file.txt   # debug marker, remove later

export K3S_TOKEN='${k3s_token}'
export K3S_URL='${controller_url}'

# Wait for the K3s controller API to become ready (max ~5 min)
n=0
until curl -ksf --connect-timeout 2 "$K3S_URL/ping" > /dev/null; do
    n=$((n + 1))
    if [ "$n" -ge 60 ]; then
        echo "Controller not reachable at $K3S_URL after 60 tries" >&2
        exit 1
    fi
    echo "Waiting for K3s controller at $K3S_URL... ($n/60)"
    sleep 5
done

curl -sfL https://get.k3s.io | sh -