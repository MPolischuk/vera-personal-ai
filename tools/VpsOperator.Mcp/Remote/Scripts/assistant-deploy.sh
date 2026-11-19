# POST deploy through the Coolify API using the token already inside the MCP container.
# force is omitted: the string "false" is truthy in older Coolify, and a normal deploy already pulls.
count=0
listener=""
while read -r id; do
  [[ -z "$id" ]] && continue
  count=$((count + 1))
  listener="$id"
done < <(mcp_listener_ids)
if [[ "$count" -ne 1 || -z "$listener" ]]; then
  printf '{"status_http":0,"alias_count":%s,"refused":"alias"}\n' "$count"
  exit 6
fi
name="$(container_name "$listener")"
if [[ "$name" == "assistant-coolify-mcp" ]]; then
  printf '%s\n' "The alias is served by assistant-coolify-mcp. Refusing to call the API through that container." >&2
  exit 6
fi
uuid="$(coolify_psql -c "SELECT uuid FROM applications WHERE name = 'assistant' ORDER BY id LIMIT 1;" | tr -d '[:space:]')"
if [[ ! "$uuid" =~ ^[A-Za-z0-9-]+$ ]]; then
  printf '%s\n' "Application assistant was not found." >&2
  exit 5
fi
status_http="$(docker exec -e "APP_UUID=${uuid}" "$listener" sh -c 'base="${COOLIFY_API_URL%/}"; curl -sS -o /dev/null -w "%{http_code}" --max-time 90 -X POST -H "Authorization: Bearer ${COOLIFY_API_TOKEN}" -H "Accept: application/json" "${base}/api/v1/deploy?uuid=${APP_UUID}"')"
status_http="$(printf '%s' "$status_http" | tr -d '[:space:]')"
if [[ ! "$status_http" =~ ^[0-9]{3}$ ]]; then
  status_http=0
fi
printf '{"status_http":%s,"alias_count":1,"via":"coolify-api"}\n' "$status_http"
