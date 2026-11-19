# Recreate only the assistant db container on the pinned pgvector image. Keeps the data volume.
tag="${1:?tag}"
if [[ "$tag" != "0.8.7-pg17" ]]; then
  printf '%s\n' "The database image tag is not pinned." >&2
  exit 2
fi
digest="sha256:ac08538c6f8b9904c33c8224c5e5706dbe760aca29db1d096972b4052c22a75d"
image="pgvector/pgvector:${tag}@${digest}"
db="$(assistant_db_require_running)"
mount_name="$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}' "$db")"
mount_source="$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Source}}{{end}}{{end}}' "$db")"
if [[ -z "$mount_name" && -z "$mount_source" ]]; then
  printf '%s\n' "The database data mount was not found." >&2
  exit 5
fi
label_of() {
  docker inspect --format "{{index .Config.Labels \"$2\"}}" "$1" 2>/dev/null || true
}
trim() {
  local value=$1
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  if [[ "$value" == "<no value>" ]]; then
    value=""
  fi
  printf '%s' "$value"
}
service="$(trim "$(label_of "$db" com.docker.compose.service)")"
project="$(trim "$(label_of "$db" com.docker.compose.project)")"
workdir="$(trim "$(label_of "$db" com.docker.compose.project.working_dir)")"
config="$(trim "$(label_of "$db" com.docker.compose.project.config_files)")"
config="${config%%,*}"
config="$(trim "$config")"
if [[ -z "$project" || -z "$workdir" ]]; then
  while read -r sibling; do
    [[ -z "$sibling" || "$sibling" == "$db" ]] && continue
    sibling_project="$(trim "$(label_of "$sibling" com.docker.compose.project)")"
    sibling_workdir="$(trim "$(label_of "$sibling" com.docker.compose.project.working_dir)")"
    if [[ -n "$sibling_project" && -n "$sibling_workdir" ]]; then
      project="$sibling_project"
      workdir="$sibling_workdir"
      if [[ -z "$config" ]]; then
        config="$(trim "$(label_of "$sibling" com.docker.compose.project.config_files)")"
        config="${config%%,*}"
        config="$(trim "$config")"
      fi
      break
    fi
  done < <(docker ps -aq --filter label=coolify.projectName=assistant)
fi
if [[ -n "$config" && "$config" != /* && "$workdir" == /* && "$config" != *..* ]]; then
  config="${workdir%/}/${config#./}"
fi
if [[ -z "$service" ]]; then
  service=db
fi
if ! sudo test -f "${config:-/missing}"; then
  uuid="$(coolify_psql -c "SELECT uuid FROM applications WHERE name = 'assistant' ORDER BY id LIMIT 1;" | tr -d '[:space:]')"
  if [[ "$uuid" =~ ^[A-Za-z0-9-]+$ ]]; then
    app_dir="/data/coolify/applications/${uuid}"
    if sudo test -f "${app_dir}/docker-compose.yaml"; then
      config="${app_dir}/docker-compose.yaml"
      workdir="$app_dir"
    elif sudo test -f "${app_dir}/docker-compose.yml"; then
      config="${app_dir}/docker-compose.yml"
      workdir="$app_dir"
    fi
  fi
fi
if [[ ! "$service" =~ ^[A-Za-z0-9_-]+$ || ! "$workdir" =~ ^/ || "$workdir" == *..* || ! "$project" =~ ^[A-Za-z0-9_-]+$ || ! "$config" =~ ^/ || "$config" == *..* ]] || ! sudo test -f "$config"; then
  printf 'The database compose project was not usable. service=%s project_set=%s workdir_set=%s config_set=%s\n' \
    "${service:-empty}" \
    "$([[ -n "$project" ]] && printf yes || printf no)" \
    "$([[ -n "$workdir" ]] && printf yes || printf no)" \
    "$([[ -n "$config" ]] && printf yes || printf no)" >&2
  exit 5
fi
matches="$(sudo grep -c 'pgvector/pgvector:' "$config" || true)"
matches="$(printf '%s' "$matches" | tr -d '[:space:]')"
if [[ "$matches" != "1" ]]; then
  printf 'Expected one pgvector image line. matches=%s\n' "${matches:-0}" >&2
  exit 5
fi
sudo docker pull "$image" >/dev/null
want="$(sudo docker image inspect "$image" --format '{{.Id}}')"
have="$(docker inspect --format '{{.Image}}' "$db")"
current_line="$(sudo grep -F 'pgvector/pgvector:' "$config")"
recreated=false
if [[ "$want" != "$have" || "$current_line" != *"@${digest}"* ]]; then
  sudo sed -i -E "s|pgvector/pgvector:[^[:space:]'\"]+|${image}|" "$config"
  sudo docker compose -p "$project" --project-directory "$workdir" -f "$config" up -d --no-deps --force-recreate --pull never "$service" >/dev/null
  recreated=true
  db="$(assistant_db_require_running)"
  after_name="$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}' "$db")"
  after_source="$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Source}}{{end}}{{end}}' "$db")"
  if [[ "$after_name" != "$mount_name" || "$after_source" != "$mount_source" ]]; then
    printf '%s\n' "The database volume changed." >&2
    exit 1
  fi
fi
running="$(docker inspect --format '{{.Config.Image}}' "$db")"
printf '{"image":"%s","recreated":%s,"volume_unchanged":true}\n' \
  "$(json_escape "$running")" \
  "$recreated"
