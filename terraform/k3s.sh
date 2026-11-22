#!/bin/bash

set -euo pipefail

export K3S_TOKEN='${k3s_token}'

echo " i am controller " > /file.txt

curl -sfL https://get.k3s.io | sh -