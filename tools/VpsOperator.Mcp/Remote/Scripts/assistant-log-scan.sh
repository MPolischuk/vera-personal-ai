# Count log lines that embed a Telegram bot token. The line stays on the server.
scan=""
cleanup() {
  rm -f "${scan:-}"
}
trap cleanup EXIT HUP INT TERM

ids="$(assistant_api_ids)"
if [[ -z "$ids" ]]; then
  printf '%s\n' "The assistant API container was not found." >&2
  exit 5
fi
total=0
while read -r id; do
  [[ -z "$id" ]] && continue
  scan="$(mktemp)"
  chmod 600 "$scan"
  if ! docker logs "$id" >"$scan" 2>&1; then
    rm -f "$scan"
    scan=""
    printf '%s\n' "The assistant API logs could not be read." >&2
    exit 5
  fi
  n="$(grep -cE 'api\.telegram\.org/bot[0-9]+' "$scan" || true)"
  rm -f "$scan"
  scan=""
  n="$(printf '%s' "$n" | tr -d '[:space:]')"
  if [[ ! "$n" =~ ^[0-9]+$ ]]; then
    printf '%s\n' "The assistant API logs could not be read." >&2
    exit 5
  fi
  total=$((total + n))
done < <(printf '%s\n' "$ids")
printf '{"matches":%s}\n' "$total"
