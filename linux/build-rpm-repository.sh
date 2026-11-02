#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 aster-void
#
# Build a signed, single-version dnf repository for GitHub Pages.
# Signs the packages and repodata/repomd.xml with the same key as the apt repository.
# Usage: APT_SIGNING_KEY_B64=... APT_SIGNING_KEY_PASSPHRASE=... \
#        bash linux/build-rpm-repository.sh <x86_64.rpm> <aarch64.rpm> <site-root>
# Needs: rpm (rpmsign), createrepo_c, gpg
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <x86_64 .rpm> <aarch64 .rpm> <GitHub Pages site root>" >&2
  exit 2
fi

rpms=("$1" "$2")
site_root="$3"
declare -A architectures=()
rpm_root="$site_root/rpm"
packages="$rpm_root/packages"
mkdir -p "$packages"
find "$packages" -maxdepth 1 -type f -name 'meltype-*.rpm' -delete

for rpm in "${rpms[@]}"; do
  if [[ ! -f "$rpm" ]]; then
    echo "Error: .rpm not found: $rpm" >&2
    exit 1
  fi
  name="$(rpm -qp --queryformat '%{NAME}' "$rpm")"
  architecture="$(rpm -qp --queryformat '%{ARCH}' "$rpm")"
  if [[ "$name" != meltype || ( "$architecture" != x86_64 && "$architecture" != aarch64 ) ]]; then
    echo "Error: expected a Meltype x86_64 or aarch64 package; got $name ($architecture)." >&2
    exit 1
  fi
  if [[ -n "${architectures[$architecture]:-}" ]]; then
    echo "Error: duplicate .rpm for architecture $architecture." >&2
    exit 1
  fi
  architectures["$architecture"]=1
  cp "$rpm" "$packages/"
done

for architecture in x86_64 aarch64; do
  if [[ -z "${architectures[$architecture]:-}" ]]; then
    echo "Error: missing Meltype .rpm for architecture $architecture." >&2
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
