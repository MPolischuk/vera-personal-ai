# Revoke one token unless it is the only live read+deploy token.
token_id="${1:?token id}"
if [[ ! "$token_id" =~ ^[0-9]+$ ]]; then
  printf '%s\n' "token_id must be a number." >&2
  exit 2
fi
user="$(coolify_db_user)"
name="$(coolify_db_name)"
decision="$(docker exec coolify-db psql -U "$user" -d "$name" -v ON_ERROR_STOP=1 -v token_id="$token_id" -At -c "
WITH target AS (
  SELECT id, abilities::jsonb AS abilities, expires_at
  FROM personal_access_tokens
  WHERE id = :token_id
),
active AS (
  SELECT id
  FROM personal_access_tokens
  WHERE (expires_at IS NULL OR expires_at > now())
    AND abilities::jsonb @> '[\"read\",\"deploy\"]'::jsonb
    AND abilities::jsonb <@ '[\"read\",\"deploy\"]'::jsonb
    AND NOT abilities::jsonb ? 'root'
    AND NOT abilities::jsonb ? 'read:sensitive'
    AND NOT abilities::jsonb ? '*'
)
SELECT CASE
  WHEN NOT EXISTS (SELECT 1 FROM target) THEN 'missing'
  WHEN EXISTS (SELECT 1 FROM target t JOIN active a ON a.id = t.id)
       AND (SELECT count(*) FROM active) = 1 THEN 'last'
  ELSE 'ok'
END;
")"
decision="$(printf '%s' "$decision" | tr -d '[:space:]')"
if [[ "$decision" == "missing" ]]; then
  printf '%s\n' "Token id was not found." >&2
  exit 4
fi
if [[ "$decision" == "last" ]]; then
  printf '%s\n' "Refusing to revoke the only live read+deploy token." >&2
  exit 3
fi
if [[ "$decision" != "ok" ]]; then
  printf '%s\n' "Token revoke did not get a decision." >&2
  exit 1
fi
docker exec coolify-db psql -U "$user" -d "$name" -v ON_ERROR_STOP=1 -v token_id="$token_id" -c "DELETE FROM personal_access_tokens WHERE id = :token_id;" >/dev/null
printf '{"revoked_id":%s}\n' "$token_id"
