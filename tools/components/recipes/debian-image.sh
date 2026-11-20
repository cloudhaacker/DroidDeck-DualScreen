#!/usr/bin/env bash
set -euo pipefail
. tools/linuxfs/debian-image.env
image=public.ecr.aws/docker/library/debian:bullseye
if gh release download "$DEBIAN_IMAGE_TAG" -R "$GITHUB_REPOSITORY" -p "$DEBIAN_IMAGE_ASSET" -O /tmp/debian-image.tar.zst \
   && { [ -z "$DEBIAN_IMAGE_SHA256" ] || echo "$DEBIAN_IMAGE_SHA256  /tmp/debian-image.tar.zst" | sha256sum -c -; }; then
  zstd -dc /tmp/debian-image.tar.zst | docker load
  rm -f /tmp/debian-image.tar.zst
else
  echo "::warning::the hosted Debian image could not be fetched; pulling from the registry instead"
  pulled=0
  for attempt in 1 2 3; do
    if docker pull "$image"; then pulled=1; break; fi
    [ "$attempt" -lt 3 ] || break
    echo "pull attempt $attempt failed (registry busy?); retrying in $((attempt * 20)) s"
    sleep $((attempt * 20))
  done
  if [ "$pulled" != 1 ]; then
    mirror=mirror.gcr.io/library/debian:bullseye
    echo "::warning::could not pull $image; trying $mirror"
    docker pull "$mirror" && docker tag "$mirror" "$image" || { echo "::error::could not pull $image or $mirror"; exit 1; }
  fi
fi
docker image ls "$image"
