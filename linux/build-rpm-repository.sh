#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 aster-void
#
# Build a signed, single-version dnf repository for GitHub Pages.
# Signs the packages and repodata/repomd.xml with the same key as the apt repository.
# Usage: APT_SIGNING_KEY_B64=... APT_SIGNING_KEY_PASSPHRASE=... \
#        bash linux/build-rpm-repository.sh <site-root> <.rpm>...
# Takes meltype and meltype-fcitx5 for both x86_64 and aarch64.
# Needs: rpm (rpmsign), createrepo_c, gpg
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <GitHub Pages site root> <.rpm>..." >&2
  exit 2
fi

site_root="$1"
shift
expected=(meltype.x86_64 meltype.aarch64 meltype-fcitx5.x86_64 meltype-fcitx5.aarch64)
declare -A packages_found=()
rpm_root="$site_root/rpm"
packages="$rpm_root/packages"
mkdir -p "$packages"
find "$packages" -maxdepth 1 -type f -name 'meltype-*.rpm' -delete

for rpm in "$@"; do
  if [[ ! -f "$rpm" ]]; then
    echo "Error: .rpm not found: $rpm" >&2
    exit 1
  fi
  package="$(rpm -qp --queryformat '%{NAME}.%{ARCH}' "$rpm")"
  if [[ " ${expected[*]} " != *" $package "* ]]; then
    echo "Error: unexpected .rpm: $package ($rpm)." >&2
    exit 1
  fi
  if [[ -n "${packages_found[$package]:-}" ]]; then
    echo "Error: duplicate .rpm for $package." >&2
    exit 1
  fi
  packages_found["$package"]=1
  cp "$rpm" "$packages/"
done

for package in "${expected[@]}"; do
  if [[ -z "${packages_found[$package]:-}" ]]; then
    echo "Error: missing .rpm for $package." >&2
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
gpg --batch --armor --export "$fingerprint" > "$rpm_root/meltype-archive-key.asc"

# rpmsign runs gpg itself; pass the passphrase through a file because it cannot prompt in CI.
# Ubuntu's rpm sets %__gpg to /usr/bin/gpg2, which does not exist there.
passphrase_file="$gpg_home/passphrase"
printf '%s' "${APT_SIGNING_KEY_PASSPHRASE:-}" > "$passphrase_file"
rpmsign --addsign \
  --define "__gpg $(command -v gpg)" \
  --define "_gpg_name $fingerprint" \
  --define "_gpg_sign_cmd_extra_args --batch --pinentry-mode loopback --passphrase-file $passphrase_file" \
  "$packages"/meltype-*.rpm

createrepo_c "$rpm_root"
gpg --batch --yes --pinentry-mode loopback \
  --passphrase "${APT_SIGNING_KEY_PASSPHRASE:-}" --local-user "$fingerprint" \
  --armor --detach-sign --output "$rpm_root/repodata/repomd.xml.asc" "$rpm_root/repodata/repomd.xml"

# Users add the repository with: sudo dnf config-manager addrepo --from-repofile=<site>/rpm/meltype.repo
cat > "$rpm_root/meltype.repo" << 'EOF'
[meltype]
name=Meltype
baseurl=https://yksr-melt.github.io/Meltype/rpm
enabled=1
gpgcheck=1
repo_gpgcheck=1
gpgkey=https://yksr-melt.github.io/Meltype/rpm/meltype-archive-key.asc
EOF

echo "Signed dnf repository created at $rpm_root"
