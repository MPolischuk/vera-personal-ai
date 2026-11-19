# Vera's coolify_control rows: event, tool, and outcome. Not the user text.
db=""
if docker container inspect assistant-db >/dev/null 2>&1; then
  db="assistant-db"
else
  ids="$(docker ps -q --filter label=coolify.projectName=assistant --filter label=com.docker.compose.service=db --filter status=running)"
  count="$(printf '%s\n' "$ids" | grep -c . || true)"
  if [[ "$count" -ne 1 ]]; then
    printf '%s\n' "Expected one assistant database container." >&2
    exit 5
  fi
  db="$(docker inspect --format '{{.Name}}' "$ids" | sed 's#^/##')"
fi
sql=$(cat <<'SQL'
SELECT COALESCE(json_agg(row_to_json(t) ORDER BY t.created_at DESC), '[]'::json)
FROM (
  SELECT created_at,
         event_type,
         COALESCE(payload->>'toolName', payload->>'ToolName') AS tool_name,
         COALESCE(payload->>'outcome', payload->>'Outcome') AS outcome,
         COALESCE(payload->>'level', payload->>'Level') AS level,
         COALESCE(payload->>'succeeded', payload->>'Succeeded') AS succeeded
  FROM audit_log
  WHERE event_type IN ('tool.invoked', 'confirmation.resolved', 'confirmation.requested')
    AND COALESCE(payload->>'toolName', payload->>'ToolName') = 'coolify_control'
  ORDER BY created_at DESC
  LIMIT 50
) t
SQL
)
raw="$(docker exec "$db" psql -U assistant -d assistant -v ON_ERROR_STOP=1 -At -c "$sql")"
if [[ -z "$raw" ]]; then
  raw='[]'
fi
printf '{"events":%s}\n' "$raw"
