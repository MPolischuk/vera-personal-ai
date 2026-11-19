# Apply pending EF migrations as POSTGRES_USER. Does not deploy, grant, or print secrets.
db=""
conn=""
key=""
known=""
src=""
eflog=""
pullog=""
clonelog=""
fetchlog=""
checkoutlog=""
pkg=""
body=""
php_out=""

cleanup() {
  docker rm -f vps-operator-migrate >/dev/null 2>&1 || true
  rm -f \
    "${conn:-}" "${key:-}" "${known:-}" "${eflog:-}" "${pullog:-}" \
    "${clonelog:-}" "${fetchlog:-}" "${checkoutlog:-}" "${body:-}" "${php_out:-}"
  if [[ -n "${src:-}" ]]; then
    sudo rm -rf "$src" || true
  fi
  if [[ -n "${pkg:-}" ]]; then
    sudo rm -rf "$pkg" || true
  fi
  sudo rm -rf /tmp/tmp.6EnOnIXoV7 || true
  sudo rm -f /tmp/migrate-ef.sh /tmp/ef-update.log /tmp/ef-status.txt /tmp/assistant-conn.env || true
  docker exec -u root coolify rm -f /tmp/vps-operator-git.key >/dev/null 2>&1 || true
  if [[ -n "${db:-}" ]]; then
    docker exec -u root "$db" rm -f /tmp/conn.env /tmp/assistant-conn.env >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT HUP INT TERM

sudo rm -f /tmp/migrate-ef.sh /tmp/ef-update.log /tmp/ef-status.txt /tmp/assistant-conn.env || true
docker rm -f vps-operator-migrate >/dev/null 2>&1 || true

trim_label() {
  local value=$1
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  if [[ "$value" == "<no value>" ]]; then
    value=""
  fi
  printf '%s' "$value"
}

is_git_checkout() {
  local path=$1
  [[ "$path" == /* && "$path" != *..* ]] || return 1
  sudo test -e "${path}/.git"
}

find_checkout() {
  local api workdir cursor uuid
  api="$(assistant_api_newest)"
  workdir="$(trim_label "$(docker inspect --format '{{index .Config.Labels "com.docker.compose.project.working_dir"}}' "$api")")"
  cursor="$workdir"
  local _step
  for _step in 1 2 3 4 5; do
    if is_git_checkout "$cursor"; then
      printf '%s\n' "$cursor"
      return 0
    fi
    [[ -n "$cursor" && "$cursor" != "/" ]] || break
    cursor="$(dirname "$cursor")"
  done
  uuid="$(coolify_psql -c "SELECT uuid FROM applications WHERE name = 'assistant' ORDER BY id LIMIT 1;" | tr -d '[:space:]')"
  if [[ "$uuid" =~ ^[A-Za-z0-9-]+$ ]]; then
    local app_dir="/data/coolify/applications/${uuid}" child
    if is_git_checkout "$app_dir"; then
      printf '%s\n' "$app_dir"
      return 0
    fi
    if sudo test -d "$app_dir"; then
      while IFS= read -r child; do
        [[ -z "$child" || "$child" == *..* ]] && continue
        if is_git_checkout "$child"; then
          printf '%s\n' "$child"
          return 0
        fi
      done < <(sudo find "$app_dir" -mindepth 1 -maxdepth 2 -name .git -printf '%h\n' 2>/dev/null || true)
    fi
  fi
  return 1
}

github_slug() {
  local raw=$1
  raw="${raw%.git}"
  raw="${raw%/}"
  if [[ "$raw" =~ github\.com[:/]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)$ ]]; then
    printf '%s/%s\n' "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}"
    return 0
  fi
  if [[ "$raw" =~ ^([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)$ ]]; then
    printf '%s/%s\n' "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}"
    return 0
  fi
  return 1
}

migration_ids() {
  local col out id
  col="$(assistant_psql "$db" -c "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '__ef_migrations_history' AND column_name IN ('migration_id', 'MigrationId') ORDER BY column_name LIMIT 1;" 2>/dev/null || true)"
  col="$(printf '%s' "$col" | tr -d '[:space:]')"
  if [[ "$col" == "migration_id" ]]; then
    out="$(assistant_psql "$db" -c 'SELECT migration_id FROM __ef_migrations_history ORDER BY migration_id;' 2>/dev/null || true)"
  elif [[ "$col" == "MigrationId" ]]; then
    out="$(assistant_psql "$db" -c 'SELECT "MigrationId" FROM __ef_migrations_history ORDER BY "MigrationId";' 2>/dev/null || true)"
  else
    return 0
  fi
  while IFS= read -r id; do
    id="$(printf '%s' "$id" | tr -d '[:space:]')"
    if [[ "$id" =~ ^[0-9]{14}_[A-Za-z0-9]+$ ]]; then
      printf '%s\n' "$id"
    fi
  done < <(printf '%s\n' "$out")
}

ef_reason() {
  local line safe
  while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    if [[ "$line" =~ [Pp]assword || "$line" =~ ASSISTANT_DB_CONNECTION || "$line" =~ PRIVATE[[:space:]]KEY || "$line" =~ api\.telegram\.org/bot[0-9] ]]; then
      continue
    fi
    safe="$(printf '%s' "$line" | tr -cd 'A-Za-z0-9 .:_/+=[]()-' | cut -c 1-180)"
    [[ -z "$safe" ]] && continue
    printf '%s\n' "$safe"
  done < "$1" | tail -n 8
}

json_string_list() {
  local first=1 id
  printf '['
  for id in "$@"; do
    if [[ ! "$id" =~ ^[0-9]{14}_[A-Za-z0-9]+$ ]]; then
      continue
    fi
    if [[ "$first" -eq 0 ]]; then
      printf ','
    fi
    first=0
    printf '"%s"' "$(json_escape "$id")"
  done
  printf ']'
}

db="$(assistant_db_require_running)"
docker exec -u root "$db" rm -f /tmp/conn.env /tmp/assistant-conn.env >/dev/null 2>&1 || true

git_dir=""
if git_dir="$(find_checkout)"; then
  git_dir="$(printf '%s' "$git_dir" | tr -d '[:space:]')"
else
  git_dir=""
fi

repo_raw="$(coolify_psql -c "SELECT git_repository FROM applications WHERE name = 'assistant' ORDER BY id LIMIT 1;")"
repo_raw="${repo_raw%%$'\n'*}"
repo_raw="$(printf '%s' "$repo_raw" | tr -d '[:space:]')"
if ! owner_repo="$(github_slug "$repo_raw")"; then
  unset repo_raw
  printf '%s\n' "The assistant source was not usable." >&2
  exit 5
fi
unset repo_raw
owner_repo="$(printf '%s' "$owner_repo" | tr -d '[:space:]')"
if [[ ! "$owner_repo" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
  printf '%s\n' "The assistant source was not usable." >&2
  exit 5
fi

branch="$(coolify_psql -c "SELECT git_branch FROM applications WHERE name = 'assistant' ORDER BY id LIMIT 1;" | tr -d '[:space:]')"
if [[ -z "$branch" ]]; then
  branch=main
fi
if [[ ! "$branch" =~ ^[A-Za-z0-9._/-]+$ || "$branch" == *..* || "$branch" == -* ]]; then
  printf '%s\n' "The assistant branch was not usable." >&2
  exit 5
fi

body="$(mktemp)"
php_out="$(mktemp)"
key="$(mktemp)"
chmod 600 "$body" "$php_out" "$key"
docker exec -u root coolify rm -f /tmp/vps-operator-git.key >/dev/null 2>&1 || true
cat > "$body" <<'PHP'
$apps = \App\Models\Application::query()->where('name', 'assistant')->get();
if ($apps->count() !== 1) {
    vps_fail('expected one application named assistant');
}
$deployKey = $apps->first()->private_key;
if ($deployKey === null) {
    vps_fail('deploy key was not available');
}
$plain = (string) $deployKey->private_key;
if ($plain === '' || !str_contains($plain, 'PRIVATE KEY')) {
    vps_fail('deploy key was not available');
}
if (!str_ends_with($plain, "\n")) {
    $plain .= "\n";
}
if (file_put_contents('/tmp/vps-operator-git.key', $plain) === false) {
    vps_fail('deploy key was not available');
}
chmod('/tmp/vps-operator-git.key', 0600);
unset($plain, $deployKey);
vps_done(['action' => 'ready']);
PHP
coolify_php "$body" vps-operator-gitkey "$php_out"
action="$(sed -n 's/.*"action":"\([a-z]*\)".*/\1/p' "$php_out")"
rm -f "$body" "$php_out"
body=""
php_out=""
if [[ "$action" != "ready" ]]; then
  printf '%s\n' "The deploy key was not available." >&2
  exit 5
fi
if ! docker cp coolify:/tmp/vps-operator-git.key "$key" >/dev/null; then
  printf '%s\n' "The deploy key was not available." >&2
  exit 5
fi
docker exec -u root coolify rm -f /tmp/vps-operator-git.key >/dev/null 2>&1 || true
chmod 600 "$key"
if ! grep -q 'PRIVATE KEY' "$key"; then
  printf '%s\n' "The deploy key was not available." >&2
  exit 5
fi

src="$(mktemp -d)"
chmod 700 "$src"
clonelog="$(mktemp)"
chmod 600 "$clonelog"
copied=0
if [[ -n "$git_dir" ]] && is_git_checkout "$git_dir"; then
  if sudo git -c "safe.directory=${git_dir}" clone --local --no-hardlinks "$git_dir" "$src/repo" >"$clonelog" 2>&1; then
    copied=1
  fi
fi
rm -f "$clonelog"
clonelog=""

known="$(mktemp)"
chmod 600 "$known"
fetchlog="$(mktemp)"
chmod 600 "$fetchlog"
export GIT_SSH_COMMAND="ssh -i ${key} -o IdentitiesOnly=yes -o BatchMode=yes -o ConnectTimeout=15 -o StrictHostKeyChecking=accept-new -o UserKnownHostsFile=${known} -o LogLevel=ERROR"
if [[ "$copied" -eq 1 ]]; then
  sudo chown -R "$(id -u):$(id -g)" "$src/repo"
  chmod -R u+rwX "$src/repo"
  rm -f "$src/repo/.env"
  if ! git -C "$src/repo" remote set-url origin "git@github.com:${owner_repo}.git" >/dev/null 2>&1 \
    && ! git -C "$src/repo" remote add origin "git@github.com:${owner_repo}.git" >/dev/null 2>&1; then
    printf '%s\n' "The assistant source was not usable." >&2
    exit 5
  fi
  git -C "$src/repo" config --unset-all http.extraheader >/dev/null 2>&1 || true
  if ! git -C "$src/repo" fetch --depth=1 "git@github.com:${owner_repo}.git" "$branch" >"$fetchlog" 2>&1; then
    printf '%s\n' "The source checkout could not be updated." >&2
    exit 5
  fi
  checkoutlog="$(mktemp)"
  chmod 600 "$checkoutlog"
  if ! git -C "$src/repo" checkout --detach --force FETCH_HEAD >"$checkoutlog" 2>&1; then
    rm -f "$checkoutlog"
    checkoutlog=""
    printf '%s\n' "The source checkout could not be updated." >&2
    exit 5
  fi
  rm -f "$checkoutlog"
  checkoutlog=""
else
  if ! git clone --depth=1 --branch "$branch" "git@github.com:${owner_repo}.git" "$src/repo" >"$fetchlog" 2>&1; then
    printf '%s\n' "The source checkout could not be updated." >&2
    exit 5
  fi
fi
rm -f "$fetchlog" "$key" "$known"
fetchlog=""
key=""
known=""
unset GIT_SSH_COMMAND
rm -f "$src/repo/.env"

if [[ ! -f "$src/repo/src/Assistant.Persistence/Assistant.Persistence.csproj" || ! -f "$src/repo/.config/dotnet-tools.json" ]]; then
  printf '%s\n' "The persistence project was not in the checkout." >&2
  exit 5
fi

conn="$(mktemp)"
chmod 600 "$conn"
if ! docker exec "$db" sh -c 'printf %s "Host=127.0.0.1;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"' >"$conn"; then
  printf '%s\n' "The database connection could not be prepared." >&2
  exit 5
fi
if ! grep -q '^Host=127.0.0.1;Port=5432;Database=' "$conn"; then
  printf '%s\n' "The database connection could not be prepared." >&2
  exit 5
fi

before_ids=()
mapfile -t before_ids < <(migration_ids)

sdk_image="mcr.microsoft.com/dotnet/sdk:10.0.400"
if ! docker image inspect "$sdk_image" >/dev/null 2>&1; then
  pullog="$(mktemp)"
  chmod 600 "$pullog"
  if ! docker pull "$sdk_image" >"$pullog" 2>&1; then
    rm -f "$pullog"
    pullog=""
    printf '%s\n' "The SDK image could not be pulled." >&2
    exit 5
  fi
  rm -f "$pullog"
  pullog=""
fi

pkg="$(mktemp -d)"
chmod 700 "$pkg"
mkdir -p "$pkg/nuget" "$pkg/dotnet"

eflog="$(mktemp)"
chmod 600 "$eflog"
set +e
docker run --rm \
  --name vps-operator-migrate \
  -v "${src}/repo:/src" \
  -v "${pkg}/nuget:/root/.nuget/packages" \
  -v "${pkg}/dotnet:/root/.dotnet" \
  -w /src \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -e NUGET_XMLDOC_MODE=skip \
  -e NUGET_PACKAGES=/root/.nuget/packages \
  "$sdk_image" \
  bash -c 'dotnet tool restore && dotnet build src/Assistant.Persistence/Assistant.Persistence.csproj' \
  >"$eflog" 2>&1
restore_code=$?
set -e
if [[ "$restore_code" -ne 0 ]]; then
  reason="$(ef_reason "$eflog" || true)"
  rm -f "$eflog"
  eflog=""
  printf '%s\n' "The database update failed." >&2
  if [[ -n "$reason" ]]; then
    printf '%s\n' "$reason" >&2
  fi
  exit "$restore_code"
fi

docker rm -f vps-operator-migrate >/dev/null 2>&1 || true
set +e
docker run --rm \
  --name vps-operator-migrate \
  --network "container:${db}" \
  -v "${src}/repo:/src" \
  -v "${pkg}/nuget:/root/.nuget/packages" \
  -v "${pkg}/dotnet:/root/.dotnet" \
  -v "${conn}:/run/conn:ro" \
  -w /src \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -e NUGET_XMLDOC_MODE=skip \
  -e NUGET_PACKAGES=/root/.nuget/packages \
  "$sdk_image" \
  bash -c 'export PATH="$PATH:/root/.dotnet/tools"; export ASSISTANT_DB_CONNECTION="$(cat /run/conn)"; dotnet ef database update --project src/Assistant.Persistence --startup-project src/Assistant.Persistence --no-build' \
  >"$eflog" 2>&1
code=$?
set -e
if [[ "$code" -ne 0 ]]; then
  reason="$(ef_reason "$eflog" || true)"
else
  reason=""
fi
rm -f "$eflog"
eflog=""

after_ids=()
mapfile -t after_ids < <(migration_ids)
applied=()
for id in "${after_ids[@]+"${after_ids[@]}"}"; do
  skip=0
  for prev in "${before_ids[@]+"${before_ids[@]}"}"; do
    if [[ "$id" == "$prev" ]]; then
      skip=1
      break
    fi
  done
  if [[ "$skip" -eq 0 ]]; then
    applied+=("$id")
  fi
done

if [[ ${#applied[@]} -eq 0 ]]; then
  applied_json="[]"
else
  applied_json="$(json_string_list "${applied[@]}")"
fi
head_json=null
if [[ ${#after_ids[@]} -gt 0 ]]; then
  head_id="${after_ids[$((${#after_ids[@]} - 1))]}"
  if [[ "$head_id" =~ ^[0-9]{14}_[A-Za-z0-9]+$ ]]; then
    head_json="\"$(json_escape "$head_id")\""
  fi
fi
printf '{"applied":%s,"head":%s}\n' "$applied_json" "$head_json"
if [[ "$code" -ne 0 ]]; then
  printf '%s\n' "The database update failed." >&2
  if [[ -n "${reason:-}" ]]; then
    printf '%s\n' "$reason" >&2
  fi
  exit "$code"
fi
