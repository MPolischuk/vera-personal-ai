# Ensure assistant_app. The password stays on the server. The API is not redeployed.
db="$(assistant_db_require_running)"
secret="$(mktemp)"
body="$(mktemp)"
php_out="$(mktemp)"
chmod 600 "$secret" "$body" "$php_out"
cleanup() {
  rm -f "${secret:-}" "${body:-}" "${php_out:-}"
  docker exec -u root "$db" rm -f /tmp/vps-operator-app.env >/dev/null 2>&1 || true
  docker exec -u root coolify rm -f /tmp/vps-operator-app.php /tmp/vps-operator-app.env /tmp/vps-operator-env-written >/dev/null 2>&1 || true
}
trap cleanup EXIT

cat > "$body" <<'PHP'
$schema = \Illuminate\Support\Facades\Schema::class;
$apps = \App\Models\Application::query()->where('name', 'assistant')->get();
if ($apps->count() !== 1) {
    vps_fail('expected one application named assistant');
}
$application = $apps->first();
$uuid = (string) $application->uuid;
if (preg_match('/^[A-Za-z0-9-]+$/', $uuid) !== 1) {
    vps_fail('application uuid was not usable');
}
$envPath = '/var/www/html/storage/app/applications/'.$uuid.'/.env';
$stat = stat($envPath);
if ($stat === false) {
    vps_fail('application env was not found');
}
$fileUid = (int) $stat['uid'];
$fileGid = (int) $stat['gid'];
if (!is_readable($envPath)) {
    vps_done(['action' => 'reread', 'uuid' => $uuid, 'file_uid' => $fileUid, 'file_gid' => $fileGid]);
}

$type = $application->getMorphClass();
$resourceId = $application->getKey();
$lookup = function (string $key) use ($type, $resourceId, $schema) {
    $query = \App\Models\EnvironmentVariable::query()
        ->where('resourceable_type', $type)
        ->where('resourceable_id', $resourceId)
        ->where('key', $key);
    if ($schema::hasColumn('environment_variables', 'is_preview')) {
        $query->where(function ($inner) {
            $inner->where('is_preview', false)->orWhereNull('is_preview');
        });
    }
    return $query->get();
};
$save = function ($variable, string $key, string $value) use ($type, $resourceId, $schema): void {
    $variable->key = $key;
    $variable->value = $value;
    $variable->resourceable_type = $type;
    $variable->resourceable_id = $resourceId;
    if ($schema::hasColumn('environment_variables', 'is_preview')) {
        $variable->is_preview = false;
    }
    if ($schema::hasColumn('environment_variables', 'is_runtime')) {
        $variable->is_runtime = true;
    }
    if ($schema::hasColumn('environment_variables', 'is_shown_once')) {
        $variable->is_shown_once = false;
    }
    if ($schema::hasColumn('environment_variables', 'is_literal')) {
        $variable->is_literal = true;
    }
    if ($schema::hasColumn('environment_variables', 'is_multiline')) {
        $variable->is_multiline = false;
    }
    if ($schema::hasColumn('environment_variables', 'version') && empty($variable->version)) {
        $variable->version = '0';
    }
    $variable->save();
};

$users = $lookup('POSTGRES_APP_USER');
if ($users->isEmpty()) {
    $save(new \App\Models\EnvironmentVariable(), 'POSTGRES_APP_USER', 'assistant_app');
} else {
    foreach ($users as $variable) {
        if ((string) $variable->value !== 'assistant_app') {
            vps_fail('POSTGRES_APP_USER is not assistant_app');
        }
    }
}

$passwords = $lookup('POSTGRES_APP_PASSWORD');
$envState = 'present';
if ($passwords->isEmpty()) {
    $plain = bin2hex(random_bytes(32));
    $save(new \App\Models\EnvironmentVariable(), 'POSTGRES_APP_PASSWORD', $plain);
    $envState = 'written';
} else {
    $plain = (string) $passwords->first()->value;
    if ($plain === '') {
        vps_fail('POSTGRES_APP_PASSWORD was empty');
    }
    foreach ($passwords as $variable) {
        if ((string) $variable->value !== $plain) {
            $save($variable, 'POSTGRES_APP_PASSWORD', $plain);
            $envState = 'written';
        }
    }
}

$quotedUser = '"assistant_app"';
$quotedPassword = '"'.str_replace(['\\', '"'], ['\\\\', '\\"'], $plain).'"';
$lines = file($envPath, FILE_IGNORE_NEW_LINES);
if ($lines === false) {
    vps_fail('env read failed');
}
$written = [];
$foundUser = false;
$foundPassword = false;
$fileChanged = false;
foreach ($lines as $line) {
    if (preg_match('/^POSTGRES_APP_USER=/', $line) === 1) {
        $next = 'POSTGRES_APP_USER='.$quotedUser;
        $fileChanged = $fileChanged || $next !== $line;
        $written[] = $next;
        $foundUser = true;
        continue;
    }
    if (preg_match('/^POSTGRES_APP_PASSWORD=/', $line) === 1) {
        $next = 'POSTGRES_APP_PASSWORD='.$quotedPassword;
        $fileChanged = $fileChanged || $next !== $line;
        $written[] = $next;
        $foundPassword = true;
        continue;
    }
    $written[] = $line;
}
if (!$foundUser) {
    $written[] = 'POSTGRES_APP_USER='.$quotedUser;
    $fileChanged = true;
}
if (!$foundPassword) {
    $written[] = 'POSTGRES_APP_PASSWORD='.$quotedPassword;
    $fileChanged = true;
}
if ($fileChanged) {
    if (file_put_contents('/tmp/vps-operator-env-written', implode("\n", $written)."\n") === false) {
        vps_fail('env write failed');
    }
    chmod('/tmp/vps-operator-env-written', 0600);
    $envState = 'written';
}

$secretPath = '/tmp/vps-operator-app.env';
$secretBody = "POSTGRES_APP_USER=assistant_app\nPOSTGRES_APP_PASSWORD=".$plain."\n";
if (file_put_contents($secretPath, $secretBody) === false) {
    vps_fail('secret file write failed');
}
chmod($secretPath, 0600);
unset($plain, $quotedPassword, $written, $secretBody);
vps_done([
    'action' => 'ready',
    'env' => $envState,
    'uuid' => $uuid,
    'file_uid' => $fileUid,
    'file_gid' => $fileGid,
    'env_file' => $fileChanged ? 'update' : 'same',
]);
PHP

read_env_json() {
  env_json="$(cat "$php_out")"
  action="$(printf '%s' "$env_json" | sed -n 's/.*"action":"\([a-z]*\)".*/\1/p')"
  env_state="$(printf '%s' "$env_json" | sed -n 's/.*"env":"\([a-z]*\)".*/\1/p')"
  uuid="$(printf '%s' "$env_json" | sed -n 's/.*"uuid":"\([A-Za-z0-9-]*\)".*/\1/p')"
  file_uid="$(printf '%s' "$env_json" | sed -n 's/.*"file_uid":\([0-9][0-9]*\).*/\1/p')"
  file_gid="$(printf '%s' "$env_json" | sed -n 's/.*"file_gid":\([0-9][0-9]*\).*/\1/p')"
  env_file="$(printf '%s' "$env_json" | sed -n 's/.*"env_file":"\([a-z]*\)".*/\1/p')"
}

COOLIFY_PHP_UID=""
attempt=0
while true; do
  coolify_php "$body" vps-operator-app "$php_out"
  read_env_json
  if [[ "$action" == "ready" ]]; then
    break
  fi
  if [[ "$action" != "reread" || ! "$file_uid" =~ ^[0-9]+$ || ! "$file_gid" =~ ^[0-9]+$ ]]; then
    printf '%s\n' "The runtime role env update did not report a state." >&2
    exit 1
  fi
  attempt=$((attempt + 1))
  if [[ "$attempt" -eq 1 ]]; then
    COOLIFY_PHP_UID="$file_uid"
  elif [[ "$attempt" -eq 2 && "$COOLIFY_PHP_UID" != "0" ]]; then
    COOLIFY_PHP_UID=0
  else
    printf '%s\n' "The application env was not readable." >&2
    exit 1
  fi
done
ran_as_root=0
if [[ "$COOLIFY_PHP_UID" == "0" ]]; then
  ran_as_root=1
fi
unset COOLIFY_PHP_UID
rm -f "$body" "$php_out"
body=""
php_out=""

if [[ "$env_state" != "written" && "$env_state" != "present" ]]; then
  printf '%s\n' "The runtime role env update did not report a state." >&2
  exit 1
fi
if [[ ! "$uuid" =~ ^[A-Za-z0-9-]+$ || ! "$file_uid" =~ ^[0-9]+$ || ! "$file_gid" =~ ^[0-9]+$ ]]; then
  printf '%s\n' "The runtime role env update did not report a state." >&2
  exit 1
fi
if [[ "$env_file" != "update" && "$env_file" != "same" ]]; then
  printf '%s\n' "The runtime role env update did not report a state." >&2
  exit 1
fi

target="/var/www/html/storage/app/applications/${uuid}/.env"
if [[ "$env_file" == "update" ]]; then
  docker exec -u root coolify cp /tmp/vps-operator-env-written "$target"
fi
docker exec -u root coolify chown "${file_uid}:${file_gid}" "$target"
docker exec -u root coolify chmod 600 "$target"
docker exec -u root coolify rm -f /tmp/vps-operator-env-written >/dev/null 2>&1 || true
if [[ "$ran_as_root" == "1" ]]; then
  owner="$(coolify_storage_uid)"
  docker exec -u root coolify find /var/www/html/storage/framework /var/www/html/storage/logs /var/www/html/bootstrap/cache -user root -mmin -3 -exec chown "${owner}:root" {} + >/dev/null 2>&1 || true
fi
if sudo test -f "/data/coolify/applications/${uuid}/.env"; then
  sudo chmod 600 "/data/coolify/applications/${uuid}/.env"
fi

docker cp coolify:/tmp/vps-operator-app.env "$secret"
docker exec -u root coolify rm -f /tmp/vps-operator-app.env >/dev/null 2>&1 || true
docker cp "$secret" "$db":/tmp/vps-operator-app.env
rm -f "$secret"
secret=""
docker exec -u root "$db" chmod 600 /tmp/vps-operator-app.env

if ! docker exec -u root -i "$db" bash -s <<'BASH'
set -euo pipefail
set -a
# shellcheck disable=SC1091
. /tmp/vps-operator-app.env
set +a
rm -f /tmp/vps-operator-app.env
psql \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  -v ON_ERROR_STOP=1 \
  -q \
  --no-psqlrc <<'SQL'
\set ECHO none
\getenv app_user POSTGRES_APP_USER
\getenv app_password POSTGRES_APP_PASSWORD
\getenv admin_user POSTGRES_USER
\getenv db_name POSTGRES_DB

SELECT format(
  'CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD %L',
  :'app_user',
  :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user')
\gexec

SELECT format(
  'ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD %L',
  :'app_user',
  :'app_password')
\gexec

SELECT format('REVOKE %I FROM %I', :'admin_user', :'app_user')
WHERE EXISTS (
  SELECT 1
  FROM pg_auth_members AS membership
  JOIN pg_roles AS granted ON granted.oid = membership.roleid
  JOIN pg_roles AS member ON member.oid = membership.member
  WHERE granted.rolname = :'admin_user'
    AND member.rolname = :'app_user')
\gexec

REVOKE CREATE ON SCHEMA public FROM :"app_user";
GRANT CONNECT ON DATABASE :"db_name" TO :"app_user";
GRANT USAGE ON SCHEMA public TO :"app_user";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"app_user";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"app_user";

SELECT format(
  'REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON TABLE public.__ef_migrations_history FROM %I',
  :'app_user')
WHERE to_regclass('public.__ef_migrations_history') IS NOT NULL
\gexec

SELECT format(
  'GRANT SELECT ON TABLE public.__ef_migrations_history TO %I',
  :'app_user')
WHERE to_regclass('public.__ef_migrations_history') IS NOT NULL
\gexec

ALTER DEFAULT PRIVILEGES FOR ROLE :"admin_user" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_user";
ALTER DEFAULT PRIVILEGES FOR ROLE :"admin_user" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO :"app_user";
SQL
BASH
then
  printf '%s\n' "The runtime role script failed." >&2
  exit 1
fi

flags="$(assistant_psql "$db" -c "SELECT rolsuper::text FROM pg_roles WHERE rolname = 'assistant_app';" | tr -d '[:space:]')"
members="$(assistant_psql "$db" -c "SELECT count(*) FROM pg_auth_members AS membership JOIN pg_roles AS granted ON granted.oid = membership.roleid JOIN pg_roles AS member ON member.oid = membership.member WHERE granted.rolname = current_user AND member.rolname = 'assistant_app';" | tr -d '[:space:]')"
if [[ "$flags" != "false" ]]; then
  printf '%s\n' "The runtime role is still a superuser." >&2
  exit 1
fi
if [[ "$members" != "0" ]]; then
  printf '%s\n' "The runtime role is a member of the admin role." >&2
  exit 1
fi
printf '{"role":"assistant_app","rolsuper":false,"member_of_admin":false,"env":"%s","api_redeployed":false}\n' \
  "$(json_escape "$env_state")"
