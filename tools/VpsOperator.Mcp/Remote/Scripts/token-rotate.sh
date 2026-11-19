# Create a read+deploy token, install it, prove the API, and only then revoke the previous id.
valid_days="${1:?days}"
revoke_id="${2:?revoke id}"
stop_stale="${3:?stop stale}"
if [[ ! "$valid_days" =~ ^[0-9]+$ || ! "$revoke_id" =~ ^[0-9]+$ || ! "$stop_stale" =~ ^[01]$ ]]; then
  printf '%s\n' "Invalid rotation arguments." >&2
  exit 2
fi
if (( valid_days < 1 || valid_days > 90 || revoke_id < 1 )); then
  printf '%s\n' "Invalid rotation arguments." >&2
  exit 2
fi

reported=0
new_id=""
script_file="$(mktemp)"
php_out="$(mktemp)"
chmod 600 "$script_file" "$php_out"
finish() {
  local code=$?
  docker exec -u root coolify rm -f /tmp/vps-operator-token.php >/dev/null 2>&1 || true
  rm -f "$script_file" "$php_out"
  if [[ "$reported" != "1" && "$new_id" =~ ^[0-9]+$ ]]; then
    printf '{"new_id":%s,"expires_at":"","abilities":["read","deploy"],"status_http":0,"alias_count":0,"token_length":0,"db_env":"unknown","env_mode":"","compose_mode":"","revoked_id":null,"revoke_skipped":true,"stale_stopped":false}\n' "$new_id"
  fi
  exit "$code"
}
trap finish EXIT

cat > "$script_file" <<'PHP'
$db = \Illuminate\Support\Facades\DB::class;
$schema = \Illuminate\Support\Facades\Schema::class;
$days = isset($argv[1]) ? (int) $argv[1] : 0;
$revokeId = isset($argv[2]) ? (int) $argv[2] : 0;
if ($days < 1 || $days > 90 || $revokeId < 1) {
    vps_fail('invalid token arguments', 2);
}

$apps = \App\Models\Application::query()->where('name', 'assistant')->get();
if ($apps->count() !== 1) {
    vps_fail('expected one application named assistant');
}
$application = $apps->first();
$uuid = (string) $application->uuid;
if (preg_match('/^[A-Za-z0-9-]+$/', $uuid) !== 1) {
    vps_fail('application uuid was not usable');
}

$old = $db::table('personal_access_tokens')->where('id', $revokeId)->first();
if ($old === null) {
    vps_fail('token id was not found', 3);
}

$plain = bin2hex(random_bytes(32));
$expires = now()->utc()->addDays($days);
$row = [
    'tokenable_type' => $old->tokenable_type,
    'tokenable_id' => $old->tokenable_id,
    'name' => 'assistant-coolify-mcp',
    'token' => hash('sha256', $plain),
    'abilities' => json_encode(['read', 'deploy']),
    'expires_at' => $expires->format('Y-m-d H:i:s'),
    'created_at' => now()->utc()->format('Y-m-d H:i:s'),
    'updated_at' => now()->utc()->format('Y-m-d H:i:s'),
];
if ($schema::hasColumn('personal_access_tokens', 'team_id')) {
    $row['team_id'] = 0;
}

$base = '/var/www/html/storage/app/applications/'.$uuid;
$envPath = $base.'/.env';
if (!is_file($envPath) || !is_readable($envPath) || !is_writable($base)) {
    vps_fail('application env is not accessible');
}
$newId = $db::table('personal_access_tokens')->insertGetId($row);

$composePath = $base.'/docker-compose.yaml';
$composeName = 'docker-compose.yaml';
if (!is_file($composePath) && is_file($base.'/docker-compose.yml')) {
    $composePath = $base.'/docker-compose.yml';
    $composeName = 'docker-compose.yml';
}
if (!is_dir($base) || !is_file($composePath) || !is_file($envPath)) {
    vps_fail('application files were not found');
}

$quoted = '"'.str_replace(['\\', '"'], ['\\\\', '\\"'], $plain).'"';
$lines = file($envPath, FILE_IGNORE_NEW_LINES);
if ($lines === false) {
    vps_fail('env read failed');
}
$written = [];
$found = false;
foreach ($lines as $line) {
    if (preg_match('/^COOLIFY_API_TOKEN=/', $line) === 1) {
        $written[] = 'COOLIFY_API_TOKEN='.$quoted;
        $found = true;
        continue;
    }
    $written[] = $line;
}
if (!$found) {
    $written[] = 'COOLIFY_API_TOKEN='.$quoted;
}
$tmp = $envPath.'.vps-operator-tmp';
if (file_put_contents($tmp, implode("\n", $written)."\n") === false) {
    vps_fail('env write failed');
}
chmod($tmp, 0600);
if (!rename($tmp, $envPath)) {
    vps_fail('env rename failed');
}
chmod($envPath, 0600);

$compose = file_get_contents($composePath);
$composeCount = 0;
if ($compose !== false) {
    $updated = preg_replace(
        '/^([ \t]*-?[ \t]*COOLIFY_API_TOKEN[:=][ \t]*).*$/m',
        '${1}'.$quoted,
        $compose,
        -1,
        $composeCount
    );
    if ($composeCount > 0 && is_string($updated)) {
        $composeTmp = $composePath.'.vps-operator-tmp';
        if (file_put_contents($composeTmp, $updated) === false || !rename($composeTmp, $composePath)) {
            vps_fail('compose write failed');
        }
    }
}
chmod($composePath, 0600);

$dbEnv = 'failed';
try {
    $type = $application->getMorphClass();
    $resourceId = $application->getKey();
    $query = \App\Models\EnvironmentVariable::query()
        ->where('resourceable_type', $type)
        ->where('resourceable_id', $resourceId)
        ->where('key', 'COOLIFY_API_TOKEN');
    if ($schema::hasColumn('environment_variables', 'is_preview')) {
        $query->where(function ($inner) {
            $inner->where('is_preview', false)->orWhereNull('is_preview');
        });
    }
    $matches = $query->get();
    if ($matches->isEmpty()) {
        $created = new \App\Models\EnvironmentVariable();
        $created->key = 'COOLIFY_API_TOKEN';
        $created->value = $plain;
        $created->resourceable_type = $type;
        $created->resourceable_id = $resourceId;
        if ($schema::hasColumn('environment_variables', 'is_preview')) {
            $created->is_preview = false;
        }
        if ($schema::hasColumn('environment_variables', 'is_runtime')) {
            $created->is_runtime = true;
        }
        if ($schema::hasColumn('environment_variables', 'is_shown_once')) {
            $created->is_shown_once = true;
        }
        $created->save();
    } else {
        foreach ($matches as $variable) {
            $variable->value = $plain;
            $variable->save();
        }
    }
    $dbEnv = 'updated';
} catch (Throwable $exception) {
    fwrite(STDERR, 'error: environment variable update failed: '.$exception::class."\n");
    $dbEnv = 'failed';
}

unset($plain);
vps_done([
    'new_id' => $newId,
    'expires_at' => $expires->format('Y-m-d\TH:i:s\Z'),
    'uuid' => $uuid,
    'db_env' => $dbEnv,
    'compose_name' => $composeName,
]);
PHP

coolify_php "$script_file" vps-operator-token "$php_out" "$valid_days" "$revoke_id"
create_json="$(cat "$php_out")"

new_id="$(printf '%s' "$create_json" | sed -n 's/.*"new_id":\([0-9][0-9]*\).*/\1/p')"
expires_at="$(printf '%s' "$create_json" | sed -n 's/.*"expires_at":"\([^"]*\)".*/\1/p')"
uuid="$(printf '%s' "$create_json" | sed -n 's/.*"uuid":"\([A-Za-z0-9-]*\)".*/\1/p')"
db_env="$(printf '%s' "$create_json" | sed -n 's/.*"db_env":"\([a-z]*\)".*/\1/p')"
compose_name="$(printf '%s' "$create_json" | sed -n 's/.*"compose_name":"\([^"]*\)".*/\1/p')"
if [[ ! "$new_id" =~ ^[0-9]+$ || ! "$uuid" =~ ^[A-Za-z0-9-]+$ ]]; then
  printf '%s\n' "Token creation did not return an id." >&2
  exit 1
fi
if [[ ! "$compose_name" =~ ^docker-compose\.ya?ml$ ]]; then
  printf '%s\n' "Compose file name was not usable." >&2
  exit 5
fi

app_dir="/data/coolify/applications/${uuid}"
env_file="${app_dir}/.env"
compose_file="${app_dir}/${compose_name}"
container_env="/var/www/html/storage/app/applications/${uuid}/.env"
container_compose="/var/www/html/storage/app/applications/${uuid}/${compose_name}"
docker exec -u root coolify chown "$(coolify_storage_uid):root" "$container_env" "$container_compose"
sudo chmod 600 "$env_file" "$compose_file"
env_mode="$(sudo stat -c %a "$env_file")"
compose_mode="$(sudo stat -c %a "$compose_file")"

listener=""
while read -r id; do
  [[ -z "$id" ]] && continue
  listener_name="$(container_name "$id")"
  if [[ "$listener_name" == "assistant-coolify-mcp" ]]; then
    continue
  fi
  listener="$id"
  break
done < <(mcp_listener_ids)

project=""
workdir=""
config=""
service=""
if [[ -n "$listener" ]]; then
  project="$(docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}' "$listener")"
  workdir="$(docker inspect --format '{{index .Config.Labels "com.docker.compose.project.working_dir"}}' "$listener")"
  config="$(docker inspect --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}' "$listener")"
  service="$(docker inspect --format '{{index .Config.Labels "com.docker.compose.service"}}' "$listener")"
fi
config="${config%%,*}"
if [[ "$service" == "coolify-mcp" && "$workdir" == "$app_dir" && "$project" =~ ^[A-Za-z0-9_-]+$ && -f "$config" ]]; then
  sudo docker compose -p "$project" --project-directory "$workdir" -f "$config" up -d --no-deps --force-recreate coolify-mcp
else
  sudo docker compose --project-directory "$app_dir" -f "$compose_file" up -d --no-deps --force-recreate coolify-mcp
fi

stale_stopped=false
if [[ "$stop_stale" == "1" ]] && docker container inspect assistant-coolify-mcp >/dev/null 2>&1; then
  docker update --restart=no assistant-coolify-mcp >/dev/null
  docker stop assistant-coolify-mcp >/dev/null
  stale_stopped=true
fi

alias_count=0
listener=""
while read -r id; do
  [[ -z "$id" ]] && continue
  listener_name="$(container_name "$id")"
  if [[ "$listener_name" == "assistant-coolify-mcp" ]]; then
    continue
  fi
  alias_count=$((alias_count + 1))
  listener="$id"
done < <(mcp_listener_ids)

status_http=0
token_length=0
if [[ "$alias_count" -eq 1 && -n "$listener" ]]; then
  token_length="$(token_length_of "$listener")"
  if [[ "$token_length" == "null" ]]; then
    token_length=0
  fi
  status_http="$(docker exec -e "APP_UUID=${uuid}" "$listener" sh -c 'base="${COOLIFY_API_URL%/}"; curl -sS -o /dev/null -w "%{http_code}" --max-time 30 -H "Authorization: Bearer ${COOLIFY_API_TOKEN}" -H "Accept: application/json" "${base}/api/v1/applications/${APP_UUID}"')"
  status_http="$(printf '%s' "$status_http" | tr -d '[:space:]')"
  if [[ ! "$status_http" =~ ^[0-9]{3}$ ]]; then
    status_http=0
  fi
fi

should_revoke() {
  [[ "$status_http" == "200" && "$alias_count" == "1" && "$db_env" == "updated" && "$token_length" =~ ^[0-9]+$ && "$token_length" -gt 0 && "$env_mode" == "600" && "$compose_mode" == "600" && "$new_id" != "$revoke_id" ]]
}

revoked_id=null
revoke_skipped=true
if should_revoke; then
  user="$(coolify_db_user)"
  dbname="$(coolify_db_name)"
  docker exec coolify-db psql -U "$user" -d "$dbname" -v ON_ERROR_STOP=1 -c "DELETE FROM personal_access_tokens WHERE id = ${revoke_id};" >/dev/null
  revoked_id="$revoke_id"
  revoke_skipped=false
fi

reported=1
printf '{"new_id":%s,"expires_at":"%s","abilities":["read","deploy"],"status_http":%s,"alias_count":%s,"token_length":%s,"db_env":"%s","env_mode":"%s","compose_mode":"%s","revoked_id":%s,"revoke_skipped":%s,"stale_stopped":%s}\n' \
  "$new_id" \
  "$(json_escape "$expires_at")" \
  "$status_http" \
  "$alias_count" \
  "$token_length" \
  "$(json_escape "$db_env")" \
  "$(json_escape "$env_mode")" \
  "$(json_escape "$compose_mode")" \
  "$revoked_id" \
  "$revoke_skipped" \
  "$stale_stopped"
exit 0
