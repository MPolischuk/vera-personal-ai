# Exact running/healthy check for the assistant API, plus a log excerpt when it is not.
ids="$(docker ps -aq --filter label=coolify.projectName=assistant --filter label=com.docker.compose.service=api)"
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
status="$(docker inspect --format '{{.State.Status}}' "$newest")"
health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$newest")"
exit_code="$(docker inspect --format '{{.State.ExitCode}}' "$newest")"
image="$(docker inspect --format '{{.Config.Image}}' "$newest")"
runtime="unknown"
if [[ "$status" == "running" ]]; then
  listed="$(docker exec "$newest" dotnet --list-runtimes 2>/dev/null || true)"
  runtime="$(printf '%s\n' "$listed" | awk '/Microsoft.AspNetCore.App / { print $2; exit }')"
  runtime="$(printf '%s' "$runtime" | tr -d '[:space:]')"
  if [[ ! "$runtime" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    runtime="unknown"
  fi
fi
printf '{"status":"%s","health":"%s","exit_code":%s,"image":"%s","runtime":"%s"}\n' \
  "$(json_escape "$status")" \
  "$(json_escape "$health")" \
  "$exit_code" \
  "$(json_escape "$image")" \
  "$(json_escape "$runtime")"
if [[ "$status" != "running" || "$health" != "healthy" ]]; then
  printf '%s\n' "LOG"
  docker logs --tail 80 "$newest" 2>&1 | grep -Ev 'KEY|TOKEN|PASSWORD|SECRET' || true
fi
