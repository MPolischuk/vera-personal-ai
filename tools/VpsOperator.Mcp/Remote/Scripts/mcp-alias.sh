# Which container owns the coolify-mcp alias, and which names collide.
listeners=""
first_listener=1
while read -r id; do
  [[ -z "$id" ]] && continue
  name="$(container_name "$id")"
  image="$(docker inspect --format '{{.Config.Image}}' "$id")"
  created="$(docker inspect --format '{{.Created}}' "$id")"
  restart="$(docker inspect --format '{{.HostConfig.RestartPolicy.Name}}' "$id")"
  status="$(docker inspect --format '{{.State.Status}}' "$id")"
  length="$(token_length_of "$id")"
  if [[ "$first_listener" -eq 0 ]]; then
    listeners+=","
  fi
  first_listener=0
  if [[ "$length" == "null" ]]; then
    length_json=null
  else
    length_json="$length"
  fi
  listeners+=$(printf '{"name":"%s","image":"%s","created":"%s","restart":"%s","status":"%s","token_length":%s}' \
    "$(json_escape "$name")" \
    "$(json_escape "$image")" \
    "$(json_escape "$created")" \
    "$(json_escape "$restart")" \
    "$(json_escape "$status")" \
    "$length_json")
done < <(mcp_listener_ids)

same_name=""
first_name=1
while read -r name; do
  [[ -z "$name" ]] && continue
  if ! docker container inspect "$name" >/dev/null 2>&1; then
    continue
  fi
  id="$(docker inspect --format '{{.Id}}' "$name")"
  image="$(docker inspect --format '{{.Config.Image}}' "$id")"
  restart="$(docker inspect --format '{{.HostConfig.RestartPolicy.Name}}' "$id")"
  status="$(docker inspect --format '{{.State.Status}}' "$id")"
  if [[ "$first_name" -eq 0 ]]; then
    same_name+=","
  fi
  first_name=0
  same_name+=$(printf '{"name":"%s","image":"%s","restart":"%s","status":"%s"}' \
    "$(json_escape "$name")" \
    "$(json_escape "$image")" \
    "$(json_escape "$restart")" \
    "$(json_escape "$status")")
done < <(docker ps -a --format '{{.Names}}' | grep 'coolify-mcp' || true)

printf '{"listeners":[%s],"same_name":[%s]}\n' "$listeners" "$same_name"
