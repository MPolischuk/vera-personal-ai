# Dump the Coolify instance database. Prints path, size, and TOC size. Never the dump.
stamp="$(date -u +%Y%m%d-%H%M%S)"
dest_dir="/home/operator/coolify-instance-backups"
dest="${dest_dir}/coolify-${stamp}.dump"
mkdir -p "$dest_dir"
user="$(coolify_db_user)"
name="$(coolify_db_name)"
docker exec coolify-db rm -f /tmp/vps-operator-instance.dump
docker exec coolify-db pg_dump -U "$user" -d "$name" -Fc -f /tmp/vps-operator-instance.dump
toc="$(docker exec coolify-db pg_restore -l /tmp/vps-operator-instance.dump | wc -l | tr -d '[:space:]')"
docker cp coolify-db:/tmp/vps-operator-instance.dump "$dest"
docker exec coolify-db rm -f /tmp/vps-operator-instance.dump
chmod 600 "$dest"
bytes="$(stat -c %s "$dest")"
if [[ ! "$bytes" =~ ^[0-9]+$ || "$bytes" -le 0 ]]; then
  printf '%s\n' "The instance dump is empty." >&2
  exit 1
fi
if [[ ! "$toc" =~ ^[0-9]+$ || "$toc" -le 0 ]]; then
  printf '%s\n' "The instance dump has no table of contents." >&2
  exit 1
fi
printf '{"path":"%s","bytes":%s,"toc_lines":%s}\n' "$(json_escape "$dest")" "$bytes" "$toc"
