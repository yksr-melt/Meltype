#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Build a signed, single-version apt repository for GitHub Pages.
# Usage: APT_SIGNING_KEY_B64=... APT_SIGNING_KEY_PASSPHRASE=... \
#        bash linux/build-apt-repository.sh <site-root> <.deb>...
# Takes meltype-common, meltype and meltype-tty for both amd64 and arm64.
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <GitHub Pages site root> <.deb>..." >&2
  exit 2
fi

site_root="$1"
shift
expected=(meltype-common.amd64 meltype-common.arm64 meltype.amd64 meltype.arm64 meltype-tty.amd64 meltype-tty.arm64)
declare -A packages_found=()
apt_root="$site_root/apt"
pool="$apt_root/pool/main/m/meltype"
mkdir -p "$pool"
find "$pool" -maxdepth 2 -type f -name 'meltype*.deb' -delete

for deb in "$@"; do
  if [[ ! -f "$deb" ]]; then
    echo "Error: .deb not found: $deb" >&2
    exit 1
  fi
  architecture="$(dpkg-deb --field "$deb" Architecture)"
  package="$(dpkg-deb --field "$deb" Package).$architecture"
  if [[ " ${expected[*]} " != *" $package "* ]]; then
    echo "Error: unexpected .deb: $package ($deb)." >&2
    exit 1
  fi
  if [[ -n "${packages_found[$package]:-}" ]]; then
    echo "Error: duplicate .deb for $package." >&2
    exit 1
  fi
  packages_found["$package"]=1
  mkdir -p "$pool/$architecture"
  cp "$deb" "$pool/$architecture/"
done

for package in "${expected[@]}"; do
  if [[ -z "${packages_found[$package]:-}" ]]; then
    echo "Error: missing .deb for $package." >&2
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
