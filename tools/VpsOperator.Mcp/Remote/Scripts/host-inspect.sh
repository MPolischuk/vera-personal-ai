# Read-only snapshot of the Coolify host. Prints one JSON object.
image="$(docker inspect coolify --format '{{.Config.Image}}')"
status="$(docker inspect coolify --format '{{.State.Status}}')"
health="$(docker inspect coolify --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}')"

autoupdate_defined=false
if sudo grep -q '^AUTOUPDATE=' /data/coolify/source/.env; then
  autoupdate_defined=true
fi

custom_compose=false
soketi=false
if [[ -f /data/coolify/source/docker-compose.custom.yml ]]; then
  custom_compose=true
  if sudo grep -q soketi /data/coolify/source/docker-compose.custom.yml; then
    soketi=true
  fi
fi

realtime_present=false
if docker ps -a --format '{{.Names}}' | grep -qx 'coolify-realtime'; then
  realtime_present=true
fi

ports="$(docker inspect coolify --format '{{range $p, $b := .HostConfig.PortBindings}}{{$p}} {{end}}')"
published_6001=false
published_6002=false
if printf '%s\n' "$ports" | tr ' ' '\n' | grep -qx '6001/tcp'; then
  published_6001=true
fi
if printf '%s\n' "$ports" | tr ' ' '\n' | grep -qx '6002/tcp'; then
  published_6002=true
fi

coolify_env_mode="$(sudo stat -c %a /data/coolify/source/.env 2>/dev/null || printf 'missing')"
application_env_mode="missing"
application_compose_mode="missing"
uuid_count="$(coolify_psql -c "SELECT count(*) FROM applications WHERE name = 'assistant';" | tr -d '[:space:]')"
uuid=""
if [[ "$uuid_count" == "1" ]]; then
  uuid="$(coolify_psql -c "SELECT uuid FROM applications WHERE name = 'assistant' ORDER BY id LIMIT 1;" | tr -d '[:space:]')"
fi
if [[ "$uuid" =~ ^[A-Za-z0-9-]+$ ]]; then
  app_dir="/data/coolify/applications/${uuid}"
  if [[ -e "${app_dir}/.env" ]]; then
    application_env_mode="$(sudo stat -c %a "${app_dir}/.env")"
  fi
  if [[ -e "${app_dir}/docker-compose.yaml" ]]; then
    application_compose_mode="$(sudo stat -c %a "${app_dir}/docker-compose.yaml")"
  elif [[ -e "${app_dir}/docker-compose.yml" ]]; then
    application_compose_mode="$(sudo stat -c %a "${app_dir}/docker-compose.yml")"
  fi
fi

# Newest container id from a newline-separated list. Empty input prints nothing.
newest_of() {
  local ids=$1 id created newest="" newest_created=""
  while read -r id; do
    [[ -z "$id" ]] && continue
    created="$(docker inspect --format '{{.Created}}' "$id" 2>/dev/null || true)"
    if [[ -z "$created" ]]; then
      continue
    fi
    if [[ -z "$newest_created" || "$created" > "$newest_created" ]]; then
      newest="$id"
      newest_created="$created"
    fi
  done < <(printf '%s\n' "$ids")
  printf '%s' "$newest"
}

# Prefer a running compose service, then any service container, then a known name.
resolve_container() {
  local service=$1 fallback=$2 ids
  ids="$(docker ps -aq --filter label=coolify.projectName=assistant --filter label=com.docker.compose.service="$service" --filter status=running)"
  if [[ -z "$ids" ]]; then
    ids="$(docker ps -aq --filter label=coolify.projectName=assistant --filter label=com.docker.compose.service="$service")"
  fi
  if [[ -z "$ids" ]] && docker container inspect "$fallback" >/dev/null 2>&1; then
    docker inspect --format '{{.Id}}' "$fallback"
    return
  fi
  if [[ -z "$ids" ]]; then
    ids="$(docker ps -aq --filter name="$service")"
  fi
  newest_of "$ids"
}

# The coolify-mcp network alias. A stopped stale name without that alias is ignored.
resolve_listener() {
  local ids="" id status
  while read -r id; do
    [[ -z "$id" ]] && continue
    status="$(docker inspect --format '{{.State.Status}}' "$id" 2>/dev/null || true)"
    if [[ "$status" == "running" ]]; then
      ids+="$id"$'\n'
    fi
  done < <(mcp_listener_ids)
  if [[ -z "${ids//[[:space:]]/}" ]]; then
    ids="$(mcp_listener_ids)"
  fi
  newest_of "$ids"
}

# Version plus whether the emSign ECC G3 root is in the store. grep -q only: no PEM, no file list.
trust_object() {
  local role=$1 id=$2
  local name image image_id state version emsign queried grep_status
  if [[ -z "$id" ]]; then
    printf '{"role":"%s","found":false}' "$(json_escape "$role")"
    return
  fi
  name="$(container_name "$id")"
  image="$(docker inspect --format '{{.Config.Image}}' "$id")"
  image_id="$(docker inspect --format '{{.Image}}' "$id")"
  state="$(docker inspect --format '{{.State.Status}}' "$id")"
  version="missing"
  emsign="null"
  if [[ ! "$image_id" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    image_id="unknown"
  fi
  if [[ "$state" == "running" ]]; then
    if queried="$(docker exec "$id" dpkg-query -W -f '${Version}' ca-certificates 2>/dev/null)"; then
      queried="$(printf '%s' "$queried" | tr -d '[:space:]')"
      if [[ "$queried" =~ ^[A-Za-z0-9.+~:-]+$ ]]; then
        version="$queried"
      else
        version="unreadable"
      fi
    fi
    set +e
    docker exec "$id" sh -c 'if grep -q -E "emSign ECC Root CA.{0,16}G3" /etc/ssl/certs/ca-certificates.crt 2>/dev/null; then exit 0; fi; if find /etc/ssl/certs /usr/share/ca-certificates -iname "*emsign*g3*" -print -quit 2>/dev/null | grep -q .; then exit 0; fi; if [ ! -f /etc/ssl/certs/ca-certificates.crt ]; then exit 2; fi; exit 1' >/dev/null 2>&1
    grep_status=$?
    set -e
    if [[ "$grep_status" -eq 0 ]]; then
      emsign=true
    elif [[ "$grep_status" -eq 1 ]]; then
      emsign=false
    fi
  fi
  printf '{"role":"%s","found":true,"name":"%s","image":"%s","image_id":"%s","status":"%s","ca_certificates":"%s","emsign_ecc_g3":%s}' \
    "$(json_escape "$role")" \
    "$(json_escape "$name")" \
    "$(json_escape "$image")" \
    "$(json_escape "$image_id")" \
    "$(json_escape "$state")" \
    "$(json_escape "$version")" \
    "$emsign"
}

# Publication date of the image that is actually running. The date is Image.Created, not the container.
image_record() {
  local role=$1 service=$2 fallback=$3
  local id name image image_id created published
  id="$(resolve_container "$service" "$fallback")"
  if [[ -z "$id" ]]; then
    printf '{"role":"%s","found":false}' "$(json_escape "$role")"
    return
  fi
  name="$(container_name "$id")"
  image="$(docker inspect --format '{{.Config.Image}}' "$id")"
  image_id="$(docker inspect --format '{{.Image}}' "$id")"
  created=""
  if [[ "$image_id" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    created="$(docker inspect --type image --format '{{.Created}}' "$image_id" 2>/dev/null || true)"
  fi
  if [[ -z "${created//[[:space:]]/}" ]]; then
    created="$(docker inspect --type image --format '{{.Created}}' "$image" 2>/dev/null || true)"
  fi
  created="$(printf '%s' "$created" | tr -d '[:space:]')"
  published="unknown"
  if [[ "$created" =~ ([0-9]{4}-[0-9]{2}-[0-9]{2}) ]]; then
    published="${BASH_REMATCH[1]}"
  fi
  printf '{"role":"%s","found":true,"name":"%s","image":"%s","published":"%s"}' \
    "$(json_escape "$role")" \
    "$(json_escape "$name")" \
    "$(json_escape "$image")" \
    "$(json_escape "$published")"
}

trust_stores="$(trust_object api "$(resolve_container api assistant-api)")"
trust_stores+=",$(trust_object coolify-mcp "$(resolve_listener)")"
third_party_images="$(image_record aspire-dashboard dashboard assistant-dashboard)"
third_party_images+=",$(image_record weather-mcp weather-mcp assistant-weather-mcp)"

printf '{"image":"%s","status":"%s","health":"%s","autoupdate_defined":%s,"custom_compose":%s,"soketi":%s,"realtime_present":%s,"published_6001":%s,"published_6002":%s,"coolify_env_mode":"%s","application_env_mode":"%s","application_compose_mode":"%s","trust_stores":[%s],"third_party_images":[%s]}\n' \
  "$(json_escape "$image")" \
  "$(json_escape "$status")" \
  "$(json_escape "$health")" \
  "$autoupdate_defined" \
  "$custom_compose" \
  "$soketi" \
  "$realtime_present" \
  "$published_6001" \
  "$published_6002" \
  "$(json_escape "$coolify_env_mode")" \
  "$(json_escape "$application_env_mode")" \
  "$(json_escape "$application_compose_mode")" \
  "$trust_stores" \
  "$third_party_images"
