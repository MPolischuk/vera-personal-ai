# dotnet Assistant.Api.dll audit inside the API container. Stdout is the exit code and the report.
api="$(assistant_api_require_running)"
err="$(mktemp)"
chmod 600 "$err"
set +e
output="$(docker exec "$api" dotnet Assistant.Api.dll audit 2>"$err")"
code=$?
set -e
rm -f "$err"
if [[ ! "$code" =~ ^[0-9]+$ ]]; then
  code=1
fi
if [[ "$code" -ne 0 && -z "$output" ]]; then
  output="audit failed"
fi

json_quote() {
  local s=$1
  s=${s//\\/\\\\}
  s=${s//\"/\\\"}
  s=${s//$'\r'/}
  s=${s//$'\t'/\\t}
  s=${s//$'\n'/\\n}
  printf '"%s"' "$s"
}

printf '{"exit_code":%s,"output":%s}\n' "$code" "$(json_quote "$output")"
