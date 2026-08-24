#!/bin/zsh
emulate -LR zsh
setopt errexit nounset pipefail

readonly ROOT="${0:A:h:h}"
readonly URL="http://127.0.0.1:5183"
mkdir -p "$ROOT/.artifacts"

dotnet run \
  --project "$ROOT/src/Release.Api/Release.Api.csproj" \
  --configuration Release \
  --no-build \
  --urls "$URL" >"$ROOT/.artifacts/smoke-api.log" 2>&1 &
readonly api_pid=$!
trap 'kill "$api_pid" 2>/dev/null || true; wait "$api_pid" 2>/dev/null || true' EXIT INT TERM

for attempt in {1..30}; do
  if curl --fail --silent "$URL/health" >/dev/null; then
    break
  fi
  sleep 0.2
done

curl --fail --silent "$URL/health" |
  ruby -rjson -e 'body = JSON.parse(STDIN.read); abort "bad health response" unless body == {"status"=>"healthy"}'
curl --fail --silent "$URL/version" |
  ruby -rjson -e 'body = JSON.parse(STDIN.read); required=%w[service version revision]; abort "bad version fields" unless body.keys.sort == required.sort; abort "unsafe revision" unless body["revision"].match?(/\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z/)'

print "PASS: /health and /version returned safe expected JSON\n"
