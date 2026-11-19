# ALTER EXTENSION <name> UPDATE for an extension that is already installed.
extension="${1:?extension}"
if [[ ! "$extension" =~ ^[a-z][a-z0-9_]*$ ]]; then
  printf '%s\n' "The extension name was not usable." >&2
  exit 2
fi
db="$(assistant_db_require_running)"
installed="$(assistant_psql "$db" -c "SELECT count(*) FROM pg_extension WHERE extname = '${extension}';" | tr -d '[:space:]')"
if [[ "$installed" != "1" ]]; then
  printf '%s\n' "The extension is not installed." >&2
  exit 3
fi
assistant_psql "$db" -c "ALTER EXTENSION ${extension} UPDATE;" >/dev/null
extversion="$(assistant_psql "$db" -c "SELECT extversion FROM pg_extension WHERE extname = '${extension}';" | tr -d '[:space:]')"
if [[ -z "$extversion" ]]; then
  printf '%s\n' "The extension version was empty after update." >&2
  exit 1
fi
printf '{"extension":"%s","extversion":"%s"}\n' \
  "$(json_escape "$extension")" \
  "$(json_escape "$extversion")"
