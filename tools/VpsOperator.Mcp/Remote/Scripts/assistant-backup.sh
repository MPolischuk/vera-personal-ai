# Run the assistant database backup. Stdout is only ok, exit, and the object key.
env_file="/home/operator/.config/assistant-backup.env"
script="/home/operator/personal_ai_assistant/scripts/backup-db.sh"
if [[ ! -f "$script" ]]; then
  printf '%s\n' "backup-db.sh was not found." >&2
  exit 1
fi
err="$(mktemp)"
chmod 600 "$err"
set +e
output="$(
  set -a
  # shellcheck disable=SC1090
  source "$env_file"
  set +a
  "$script"
)"
code=$?
set -e
rm -f "$err"
output="$(printf '%s' "$output" | tr -d '\r')"
if [[ "$code" -eq 0 && "$output" =~ ^daily/assistant-[0-9]{8}-[0-9]{6}\.sql\.age$ ]]; then
  printf '{"ok":true,"exit":0,"key":"%s"}\n' "$output"
else
  printf '{"ok":false,"exit":%s,"key":null}\n' "$code"
  exit "$code"
fi
