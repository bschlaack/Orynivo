#!/usr/bin/env bash
set -euo pipefail

TAG="$1"
RELEASE_NAME="$2"
CREATE_DRAFT="$3"
shift 3

find_release_id() {
  gh api "repos/${GITHUB_REPOSITORY}/releases?per_page=100" \
    --jq "[.[] | select(.draft and .tag_name == \"${TAG}\" and .name == \"${RELEASE_NAME}\")][0].id // empty"
}

RELEASE_ID="$(find_release_id)"
if [ -z "$RELEASE_ID" ] && [ "$CREATE_DRAFT" = "true" ]; then
  RELEASE_ID="$(gh api --method POST "repos/${GITHUB_REPOSITORY}/releases" \
    -f tag_name="$TAG" \
    -f name="$RELEASE_NAME" \
    -F draft=true \
    -F generate_release_notes=false \
    -f body="$(cat)" \
    --jq .id)"
fi

if [ -z "$RELEASE_ID" ]; then
  for ATTEMPT in $(seq 1 60); do
    RELEASE_ID="$(find_release_id)"
    [ -n "$RELEASE_ID" ] && break
    echo "Canonical draft '${RELEASE_NAME}' is not available yet (attempt ${ATTEMPT}/60)."
    sleep 10
  done
fi

if [ -z "$RELEASE_ID" ]; then
  echo "Canonical draft '${RELEASE_NAME}' for tag '${TAG}' was not found." >&2
  exit 1
fi

for FILE in "$@"; do
  NAME="$(basename "$FILE")"
  EXISTING_ID="$(gh api "repos/${GITHUB_REPOSITORY}/releases/${RELEASE_ID}/assets" \
    --jq "[.[] | select(.name == \"${NAME}\")][0].id // empty")"
  if [ -n "$EXISTING_ID" ]; then
    gh api --method DELETE "repos/${GITHUB_REPOSITORY}/releases/assets/${EXISTING_ID}"
  fi
  ENCODED_NAME="$(jq -rn --arg value "$NAME" '$value|@uri')"
  curl --fail --silent --show-error \
    --request POST \
    --header "Authorization: Bearer ${GH_TOKEN}" \
    --header 'Accept: application/vnd.github+json' \
    --header 'X-GitHub-Api-Version: 2022-11-28' \
    --header 'Content-Type: application/octet-stream' \
    --data-binary "@${FILE}" \
    "https://uploads.github.com/repos/${GITHUB_REPOSITORY}/releases/${RELEASE_ID}/assets?name=${ENCODED_NAME}" \
    > /dev/null
  echo "Uploaded ${NAME} to release ${RELEASE_ID}."
done
