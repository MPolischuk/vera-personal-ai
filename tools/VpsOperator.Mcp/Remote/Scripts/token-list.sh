# List Coolify personal access tokens without the hash or the secret.
sql=$(cat <<'SQL'
SELECT COALESCE(json_agg(row_to_json(t) ORDER BY t.id), '[]'::json)
FROM (
  SELECT id,
         name,
         abilities,
         expires_at,
         created_at,
         (expires_at IS NOT NULL AND expires_at <= now()) AS expired
  FROM personal_access_tokens
  ORDER BY id
) t
SQL
)
raw="$(coolify_psql -c "$sql")"
if [[ -z "$raw" ]]; then
  raw='[]'
fi
printf '{"tokens":%s}\n' "$raw"
