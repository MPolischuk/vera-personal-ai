# Upgrade Coolify to one 4.4.N release at or above 4.4.7 and leave AUTOUPDATE unset.
version="${1:?version}"
if [[ ! "$version" =~ ^4\.4\.([7-9]|[1-9][0-9]+)$ ]]; then
  printf '%s\n' "The version must look like 4.4.N and be at least 4.4.7." >&2
  exit 2
fi
running="$(docker inspect coolify --format '{{.Config.Image}}' 2>/dev/null || true)"
running_tag="${running##*:}"
if [[ "$running_tag" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  running_major="${BASH_REMATCH[1]}"
  running_minor="${BASH_REMATCH[2]}"
  running_patch="${BASH_REMATCH[3]}"
  target_patch="${version##*.}"
  if (( running_major > 4 || (running_major == 4 && running_minor > 4) || (running_major == 4 && running_minor == 4 && running_patch > target_patch) )); then
    printf '%s\n' "Coolify is already on ${running_tag}. Refusing to install ${version}." >&2
    exit 5
  fi
fi
if [[ -f /data/coolify/source/docker-compose.custom.yml ]] && sudo grep -q soketi /data/coolify/source/docker-compose.custom.yml; then
  printf '%s\n' "docker-compose.custom.yml references soketi. Migrate that file before the upgrade." >&2
  exit 4
fi

log="/home/operator/coolify-upgrade.log"
: > "$log"
chmod 600 "$log"
set +e
curl -fsSL https://cdn.coollabs.io/coolify/install.sh | sudo bash -s "$version" >"$log" 2>&1
code=$?
set -e
sudo sed -i '/^AUTOUPDATE=/d' /data/coolify/source/.env
sudo chmod 600 /data/coolify/source/.env
autoupdate_defined=false
if sudo grep -q '^AUTOUPDATE=' /data/coolify/source/.env; then
  autoupdate_defined=true
fi
if [[ "$code" -ne 0 ]]; then
  printf '%s\n' "Coolify install exited ${code}. The log stayed on the server at /home/operator/coolify-upgrade.log with mode 600." >&2
  exit "$code"
fi
rm -f "$log"
image="$(docker inspect coolify --format '{{.Config.Image}}')"
status="$(docker inspect coolify --format '{{.State.Status}}')"
health="$(docker inspect coolify --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}')"
printf '{"image":"%s","status":"%s","health":"%s","autoupdate_defined":%s}\n' \
  "$(json_escape "$image")" \
  "$(json_escape "$status")" \
  "$(json_escape "$health")" \
  "$autoupdate_defined"
