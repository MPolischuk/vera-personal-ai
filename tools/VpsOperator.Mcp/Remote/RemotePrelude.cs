namespace VpsOperator.Mcp;

internal static class RemotePrelude
{
    public const string Text =
        """
        set -euo pipefail
        export PATH="/usr/local/bin:/usr/bin:/bin:${PATH:-}"
        umask 077

        json_escape() {
          local s=$1
          s=${s//\\/\\\\}
          s=${s//\"/\\\"}
          s=${s//$'\n'/ }
          s=${s//$'\t'/ }
          s=${s//$'\r'/}
          printf '%s' "$s"
        }

        require_simple_name() {
          local label=$1 value=$2
          if [[ ! "$value" =~ ^[A-Za-z0-9_-]+$ ]]; then
            printf '%s\n' "${label} was not usable." >&2
            exit 1
          fi
        }

        coolify_db_user() {
          local user
          user="$(docker exec coolify printenv DB_USERNAME 2>/dev/null || true)"
          if [[ -z "$user" ]]; then
            user=coolify
          fi
          require_simple_name "Database user" "$user"
          printf '%s\n' "$user"
        }

        coolify_db_name() {
          local name
          name="$(docker exec coolify printenv DB_DATABASE 2>/dev/null || true)"
          if [[ -z "$name" ]]; then
            name=coolify
          fi
          require_simple_name "Database name" "$name"
          printf '%s\n' "$name"
        }

        coolify_psql() {
          local user name
          user="$(coolify_db_user)"
          name="$(coolify_db_name)"
          docker exec coolify-db psql -U "$user" -d "$name" -v ON_ERROR_STOP=1 -At "$@"
        }

        coolify_storage_uid() {
          local uid
          uid="$(docker exec -u root coolify stat -c %u /var/www/html/storage/app/applications 2>/dev/null || true)"
          uid="$(printf '%s' "$uid" | tr -d '[:space:]')"
          if [[ ! "$uid" =~ ^[0-9]+$ || "$uid" == "0" ]]; then
            uid=9999
          fi
          printf '%s\n' "$uid"
        }

        sanitize_line() {
          tr -d '\r' | tr -cd 'A-Za-z0-9 .:_/-' | sed -E 's/[0-9a-fA-F]{16,}/redacted/g' | cut -c 1-200
        }

        # Runs a PHP body inside Coolify as the storage owner. The body must not open <?php,
        # declare or import; it finishes with vps_done([...]) or vps_fail('...', code).
        # The single JSON line lands in $3; failures exit with one sanitized stderr line.
        coolify_php() {
          local body=$1 name=$2 result=$3
          shift 3
          require_simple_name "PHP script name" "$name"
          local remote="/tmp/${name}.php" source err uid code detail json
          source="$(mktemp)"
          err="$(mktemp)"
          chmod 600 "$source" "$err"
          {
            cat <<'PHP'
        <?php
        declare(strict_types=1);
        ob_start();
        function vps_fail(string $message, int $code = 5): never
        {
            while (ob_get_level() > 0) {
                ob_end_clean();
            }
            fwrite(STDERR, 'error: '.$message."\n");
            exit($code);
        }
        function vps_done(array $payload): never
        {
            $encoded = json_encode($payload, JSON_UNESCAPED_SLASHES);
            while (ob_get_level() > 0) {
                ob_end_clean();
            }
            if (!is_string($encoded) || $encoded === '') {
                fwrite(STDERR, "error: json encode failed\n");
                exit(1);
            }
            fwrite(STDOUT, $encoded."\n");
            exit(0);
        }
        try {
            require '/var/www/html/vendor/autoload.php';
            $app = require '/var/www/html/bootstrap/app.php';
            $app->make(\Illuminate\Contracts\Console\Kernel::class)->bootstrap();
        PHP
            cat "$body"
            cat <<'PHP'

            vps_fail('script ended without a result', 1);
        } catch (\Throwable $exception) {
            $detail = '';
            // Query exceptions carry the bound values, which may include secrets.
            if (!($exception instanceof \PDOException)) {
                $detail = ': '.substr((string) strtok($exception->getMessage(), "\n"), 0, 160);
            }
            vps_fail(basename(str_replace('\\', '/', $exception::class)).$detail, 6);
        }
        PHP
          } >"$source"

          if [[ "${COOLIFY_PHP_UID:-}" =~ ^[0-9]+$ ]]; then
            uid="$COOLIFY_PHP_UID"
          else
            uid="$(coolify_storage_uid)"
          fi
          if ! docker cp "$source" "coolify:${remote}" >/dev/null; then
            rm -f "$source" "$err"
            printf '%s\n' "The PHP script could not be copied into Coolify." >&2
            exit 1
          fi
          rm -f "$source"
          docker exec -u root coolify chown "${uid}:root" "$remote"
          docker exec -u root coolify chmod 640 "$remote"

          set +e
          docker exec -u "${uid}:root" -e HOME=/tmp -w /var/www/html coolify \
            php -d display_errors=stderr -d log_errors=0 -d html_errors=0 "$remote" "$@" >"$result" 2>"$err"
          code=$?
          set -e
          docker exec -u root coolify rm -f "$remote" >/dev/null 2>&1 || true

          detail="$({ grep -m 1 '^error:' "$err" || head -n 1 "$err" || true; } | sanitize_line)"
          rm -f "$err"
          if [[ "$code" -ne 0 ]]; then
            : >"$result"
            printf 'Coolify PHP failed with exit %s. %s\n' "$code" "${detail:-no detail}" >&2
            exit "$code"
          fi
          json="$(tr -d '\r' <"$result" | sed '/^[[:space:]]*$/d' | tail -n 1)"
          if [[ ! "$json" =~ ^\{.*\}$ ]]; then
            : >"$result"
            printf 'Coolify PHP did not return JSON. %s\n' "${detail:-no detail}" >&2
            exit 1
          fi
          printf '%s\n' "$json" >"$result"
        }

        mcp_listener_ids() {
          local id aliases
          while read -r id; do
            [[ -z "$id" ]] && continue
            aliases="$(docker inspect --format '{{range $n, $c := .NetworkSettings.Networks}}{{range $c.Aliases}}{{.}} {{end}}{{end}}' "$id" 2>/dev/null || true)"
            if printf '%s\n' "$aliases" | tr ' ' '\n' | grep -qx 'coolify-mcp'; then
              printf '%s\n' "$id"
            fi
          done < <(docker ps -q)
        }

        container_name() {
          docker inspect --format '{{.Name}}' "$1" | sed 's#^/##'
        }

        token_length_of() {
          local length
          if ! length="$(docker exec "$1" sh -c 'printf %s "$COOLIFY_API_TOKEN" | wc -c' 2>/dev/null)"; then
            printf 'null'
            return
          fi
          length="$(printf '%s' "$length" | tr -d '[:space:]')"
          if [[ "$length" =~ ^[0-9]+$ ]]; then
            printf '%s' "$length"
          else
            printf 'null'
          fi
        }

        assistant_db_id() {
          if docker container inspect assistant-db >/dev/null 2>&1; then
            docker inspect --format '{{.Id}}' assistant-db
            return
          fi
          local ids count
          ids="$(docker ps -aq --filter label=coolify.projectName=assistant --filter label=com.docker.compose.service=db --filter status=running)"
          if [[ -z "$ids" ]]; then
            printf '%s\n' "The assistant database container was not found." >&2
            exit 5
          fi
          count="$(printf '%s\n' "$ids" | grep -c .)"
          if [[ "$count" -ne 1 ]]; then
            printf '%s\n' "Expected one assistant database container." >&2
            exit 5
          fi
          printf '%s\n' "$ids"
        }

        assistant_db_require_running() {
          local id status
          id="$(assistant_db_id)"
          status="$(docker inspect --format '{{.State.Status}}' "$id")"
          if [[ "$status" != "running" ]]; then
            printf '%s\n' "The assistant database container is not running." >&2
            exit 5
          fi
          printf '%s\n' "$id"
        }

        assistant_psql() {
          local id=$1
          shift
          local user name
          user="$(docker exec "$id" printenv POSTGRES_USER)"
          name="$(docker exec "$id" printenv POSTGRES_DB)"
          require_simple_name "Database user" "$user"
          require_simple_name "Database name" "$name"
          docker exec "$id" psql -U "$user" -d "$name" -v ON_ERROR_STOP=1 -At "$@"
        }

        assistant_api_ids() {
          docker ps -aq --filter label=coolify.projectName=assistant --filter label=com.docker.compose.service=api
        }

        assistant_api_newest() {
          local ids id created newest newest_created
          ids="$(assistant_api_ids)"
          if [[ -z "$ids" ]]; then
            printf '%s\n' "The assistant API container was not found." >&2
            exit 5
          fi
          newest=""
          newest_created=""
          while read -r id; do
            [[ -z "$id" ]] && continue
            created="$(docker inspect --format '{{.Created}}' "$id")"
            if [[ -z "$newest_created" || "$created" > "$newest_created" ]]; then
              newest="$id"
              newest_created="$created"
            fi
          done < <(printf '%s\n' "$ids")
          if [[ -z "$newest" ]]; then
            printf '%s\n' "The assistant API container was not found." >&2
            exit 5
          fi
          printf '%s\n' "$newest"
        }

        assistant_api_require_running() {
          local id status
          id="$(assistant_api_newest)"
          status="$(docker inspect --format '{{.State.Status}}' "$id")"
          if [[ "$status" != "running" ]]; then
            printf '%s\n' "The assistant API container is not running." >&2
            exit 5
          fi
          printf '%s\n' "$id"
        }
        """;
}
