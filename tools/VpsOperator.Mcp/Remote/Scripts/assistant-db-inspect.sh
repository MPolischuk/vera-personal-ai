# Read extension version, role flags, and whether an index exists. No secrets.
extension="${1:?extension}"
role="${2:?role}"
index="${3:?index}"
if [[ ! "$extension" =~ ^[a-z][a-z0-9_]*$ || ! "$role" =~ ^[a-z][a-z0-9_]*$ || ! "$index" =~ ^[a-z][a-z0-9_]*$ ]]; then
  printf '%s\n' "A database name was not usable." >&2
  exit 2
fi
db="$(assistant_db_require_running)"
image="$(docker inspect --format '{{.Config.Image}}' "$db")"
extversion="$(assistant_psql "$db" -c "SELECT extversion FROM pg_extension WHERE extname = '${extension}';" | tr -d '[:space:]')"
flags="$(assistant_psql "$db" -c "SELECT rolsuper::text || ',' || rolcanlogin::text FROM pg_roles WHERE rolname = '${role}';" | tr -d '[:space:]')"
members="$(assistant_psql "$db" -c "SELECT count(*) FROM pg_auth_members AS membership JOIN pg_roles AS granted ON granted.oid = membership.roleid JOIN pg_roles AS member ON member.oid = membership.member WHERE granted.rolname = current_user AND member.rolname = '${role}';" | tr -d '[:space:]')"
indexes="$(assistant_psql "$db" -c "SELECT count(*) FROM pg_indexes WHERE indexname = '${index}';" | tr -d '[:space:]')"
if [[ ! "$indexes" =~ ^[0-9]+$ ]]; then
  indexes=0
fi
if [[ ! "$members" =~ ^[0-9]+$ ]]; then
  members=0
fi
role_exists=false
rolsuper=false
rolcanlogin=false
if [[ "$flags" == "true,true" || "$flags" == "true,false" || "$flags" == "false,true" || "$flags" == "false,false" ]]; then
  role_exists=true
  rolsuper="${flags%%,*}"
  rolcanlogin="${flags##*,}"
fi
member_of_admin=false
if [[ "$members" != "0" ]]; then
  member_of_admin=true
fi
index_exists=false
if [[ "$indexes" != "0" ]]; then
  index_exists=true
fi
if [[ -z "$extversion" ]]; then
  ext_json=null
else
  ext_json="\"$(json_escape "$extversion")\""
fi
printf '{"image":"%s","extension":"%s","extversion":%s,"role":"%s","role_exists":%s,"rolsuper":%s,"rolcanlogin":%s,"member_of_admin":%s,"index":"%s","index_exists":%s}\n' \
  "$(json_escape "$image")" \
  "$(json_escape "$extension")" \
  "$ext_json" \
  "$(json_escape "$role")" \
  "$role_exists" \
  "$rolsuper" \
  "$rolcanlogin" \
  "$member_of_admin" \
  "$(json_escape "$index")" \
  "$index_exists"
