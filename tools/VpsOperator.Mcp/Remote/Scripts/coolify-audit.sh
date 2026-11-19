# Audit rows for one token and one action. Safe columns only.
token_id="${1:?token id}"
action="${2:?action}"
if [[ ! "$token_id" =~ ^[0-9]+$ ]]; then
  printf '%s\n' "token_id must be a number." >&2
  exit 2
fi
case "$action" in
  restarted|ability_denied) ;;
  *)
    printf '%s\n' "action must be restarted or ability_denied." >&2
    exit 2
    ;;
esac
found="$(coolify_psql -c "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'audit_events' LIMIT 1;" | tr -d '[:space:]')"
if [[ "$found" != "1" ]]; then
  printf '%s\n' "Table audit_events was not found." >&2
  exit 5
fi
user="$(coolify_db_user)"
name="$(coolify_db_name)"
sql=$(cat <<'SQL'
SELECT COALESCE(json_agg(row_to_json(t) ORDER BY t.created_at DESC, t.id DESC), '[]'::json)
FROM (
  SELECT id, created_at, event, source, action, level,
         actor_token_id, actor_token_name, resource_type, resource_name
  FROM audit_events
  WHERE actor_token_id = :token_id
    AND action = :'action'
  ORDER BY created_at DESC, id DESC
  LIMIT 50
) t
SQL
)
raw="$(docker exec coolify-db psql -U "$user" -d "$name" -v ON_ERROR_STOP=1 -v token_id="$token_id" -v action="$action" -At -c "$sql")"
if [[ -z "$raw" ]]; then
  raw='[]'
fi
printf '{"events":%s}\n' "$raw"
