# REINDEX INDEX CONCURRENTLY outside a transaction. The index must already exist.
index="${1:?index}"
if [[ ! "$index" =~ ^[a-z][a-z0-9_]*$ ]]; then
  printf '%s\n' "The index name was not usable." >&2
  exit 2
fi
db="$(assistant_db_require_running)"
found="$(assistant_psql "$db" -c "SELECT count(*) FROM pg_indexes WHERE indexname = '${index}';" | tr -d '[:space:]')"
if [[ "$found" != "1" ]]; then
  printf '%s\n' "The index was not found." >&2
  exit 3
fi
assistant_psql "$db" -c "REINDEX INDEX CONCURRENTLY ${index};" >/dev/null
printf '{"index":"%s","reindexed":true}\n' "$(json_escape "$index")"
