#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Build a signed, single-version apt repository for GitHub Pages.
# Usage: APT_SIGNING_KEY_B64=... APT_SIGNING_KEY_PASSPHRASE=... \
#        bash linux/build-apt-repository.sh <amd64.deb> <arm64.deb> <site-root>
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <amd64 .deb> <arm64 .deb> <GitHub Pages site root>" >&2
  exit 2
fi

debs=("$1" "$2")
site_root="$3"
declare -A architectures=()
apt_root="$site_root/apt"
pool="$apt_root/pool/main/m/meltype"
mkdir -p "$pool"
find "$pool" -maxdepth 2 -type f -name 'meltype_*.deb' -delete

for deb in "${debs[@]}"; do
  if [[ ! -f "$deb" ]]; then
    echo "Error: .deb not found: $deb" >&2
    exit 1
  fi
  package="$(dpkg-deb --field "$deb" Package)"
  architecture="$(dpkg-deb --field "$deb" Architecture)"
  if [[ "$package" != meltype || ( "$architecture" != amd64 && "$architecture" != arm64 ) ]]; then
    echo "Error: expected a Meltype amd64 or arm64 package; got $package ($architecture)." >&2
    exit 1
  fi
  if [[ -n "${architectures[$architecture]:-}" ]]; then
    echo "Error: duplicate .deb for architecture $architecture." >&2
    exit 1
  fi
  architectures["$architecture"]=1
  mkdir -p "$pool/$architecture"
  cp "$deb" "$pool/$architecture/"
done

for architecture in amd64 arm64; do
  if [[ -z "${architectures[$architecture]:-}" ]]; then
    echo "Error: missing Meltype .deb for architecture $architecture." >&2
    exit 1
  fi
done

if [[ -z "${APT_SIGNING_KEY_B64:-}" ]]; then
  echo "Error: set the APT_SIGNING_KEY_B64 environment variable." >&2
  exit 1
fi

gpg_home="$(mktemp -d)"
chmod 700 "$gpg_home"
trap 'rm -rf "$gpg_home"' EXIT
export GNUPGHOME="$gpg_home"
printf '%s' "$APT_SIGNING_KEY_B64" | base64 --decode | gpg --batch --import

fingerprint="$(gpg --batch --with-colons --list-secret-keys | awk -F: '$1 == "sec" { want = 1; next } want && $1 == "fpr" { print $10; exit }')"
if [[ -z "$fingerprint" ]]; then
  echo "Error: no private signing key was found in APT_SIGNING_KEY_B64." >&2
  exit 1
fi
gpg --batch --armor --export "$fingerprint" > "$apt_root/meltype-archive-key.asc"

(
  cd "$apt_root"
  for architecture in amd64 arm64; do
    index="dists/stable/main/binary-$architecture"
    mkdir -p "$index"
    apt-ftparchive packages "pool/main/m/meltype/$architecture" > "$index/Packages"
    gzip -n -9 -c "$index/Packages" > "$index/Packages.gz"
  done
  apt-ftparchive \
    -o APT::FTPArchive::Release::Origin="Meltype" \
    -o APT::FTPArchive::Release::Label="Meltype" \
    -o APT::FTPArchive::Release::Suite="stable" \
    -o APT::FTPArchive::Release::Codename="stable" \
    -o APT::FTPArchive::Release::Components="main" \
    -o APT::FTPArchive::Release::Architectures="amd64 arm64" \
    release dists/stable > dists/stable/Release
  gpg --batch --yes --pinentry-mode loopback \
    --passphrase "${APT_SIGNING_KEY_PASSPHRASE:-}" --local-user "$fingerprint" \
    --clearsign --output dists/stable/InRelease dists/stable/Release
  gpg --batch --yes --pinentry-mode loopback \
    --passphrase "${APT_SIGNING_KEY_PASSPHRASE:-}" --local-user "$fingerprint" \
    --detach-sign --output dists/stable/Release.gpg dists/stable/Release
)

echo "Signed apt repository created at $apt_root"
