#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 aster-void
#
# linux/build.sh で組み立てたもの (または Meltype-<版>-linux.zip を展開したフォルダー) から、
# Fedora などの .rpm パッケージを作る。dnf で入れる・更新する・外すためのもの。中身は build-deb.sh と同じ。
#   linux/build-rpm.sh [版] [組み立てたフォルダー] [出力先]
#   例: linux/build-rpm.sh 1.2.2-pre.2 linux/build/Meltype-linux linux/build
#   使う人: sudo dnf install ./meltype-1.2.2~pre.2-1.x86_64.rpm / sudo dnf remove meltype
# fcitx5 のアドオン (fcitx5/meltype.so) は、入れる先のディストリビューションの fcitx5 に対してビルドしたものを置いておく
# (Ubuntu でビルドしたものは fcitx5 の版が違う)。CI は Fedora のコンテナでビルドし直してから、これを実行する。
# 必要なもの: rpmbuild (Fedora なら rpm-build)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

src="$(cd "${2:-$here/build/Meltype-linux}" && pwd)"
out="${3:-$here/build}"
# 版: 指定が無ければ meltype.xml の <version> を使う
version="${1:-}"
if [[ -z "$version" ]]; then
    version="$(sed -n 's|.*<version>\(.*\)</version>.*|\1|p' "$src/meltype.xml" | head -n 1)"
fi
version="${version#v}"
# rpm の版には「-」を使えない。build-deb.sh と同じく「~」にする (rpm でも 1.2.2~pre.2 < 1.2.2)
rpmversion="${version//-/\~}"
if [[ ! "$rpmversion" =~ ^[0-9][A-Za-z0-9.+~]*$ ]]; then
    echo "版の形が正しくありません: $version" >&2
    exit 1
fi

for f in libMeltypeNative.so ibus-engine-meltype meltype.xml icon.png LICENSE THIRD-PARTY-NOTICES.md mozc/meltype_mozc_helper; do
    [[ -e "$src/$f" ]] || { echo "必要なファイルがありません: $src/$f (先に linux/build.sh を実行してください)" >&2; exit 1; }
done
command -v rpmbuild > /dev/null || { echo "rpmbuild がありません (Fedora なら sudo dnf install rpm-build)" >&2; exit 1; }

# libMeltypeNative.so (ELF) の e_machine (18 バイト目から 2 バイト) で CPU を見る
machine="$(od -An -tx1 -j18 -N2 "$src/libMeltypeNative.so" | tr -d ' \n')"
case "$machine" in
    3e00) arch=x86_64 ;;
    b700) arch=aarch64 ;;
    *) echo "libMeltypeNative.so の CPU が分かりません (e_machine: $machine)" >&2; exit 1 ;;
esac
if [[ -f "$src/fcitx5/meltype.so" ]]; then
    with_fcitx5=1
else
    echo "fcitx5 のアドオンが無いので、IBus だけのパッケージにします。"
    with_fcitx5=0
fi

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

# 依存: C / C++ のライブラリは rpmbuild が ELF から自動で足す。
# 自動では足されないもの: ICU (.NET が実行時に dlopen する。InvariantGlobalization=false) と、
# python3 (IBus のエンジンの #! は /usr/bin/env python3 なので、自動では /usr/bin/env しか足されない)。
# fcitx5 のアドオンが使う libFcitx5Core などは、自動で足すと fcitx5 が必須になるので外す
# (IBus だけで使う人がいる。build-deb.sh と同じく fcitx5 は Suggests)。
# IBus の一覧 (ibus write-cache) は Fedora の ibus パッケージのファイルトリガーが作り直すので、ここでは実行しない
cat > "$stage/meltype.spec" << 'EOF'
%global debug_package %{nil}
%global _build_id_links none
# 本体とアドオンはビルド済み。rpmbuild の strip や #! の書き換えはしない
%global __os_install_post %{nil}
%global __requires_exclude ^libFcitx5
%global target /opt/meltype

Name: meltype
Version: %{meltype_version}
Release: 1
Summary: Japanese input method without the Hankaku/Zenkaku key
License: GPL-3.0-or-later
URL: https://github.com/yksr-melt/Meltype
Requires: libicu, python3
Recommends: ibus, python3-gobject
%if %{with_fcitx5}
Suggests: fcitx5
%endif

%description
Meltype is a Japanese input method (IBus engine and fcitx5 addon).
English words typed while writing Japanese stay as English,
and romaji becomes Japanese, without switching the input mode.

%install
mkdir -p %{buildroot}%{target} %{buildroot}%{_datadir}/ibus/component
cp -R %{meltype_src}/libMeltypeNative.so %{meltype_src}/ibus-engine-meltype %{meltype_src}/icon.png %{meltype_src}/mozc %{meltype_src}/LICENSE %{meltype_src}/THIRD-PARTY-NOTICES.md %{buildroot}%{target}/
chmod 644 %{buildroot}%{target}/libMeltypeNative.so %{buildroot}%{target}/icon.png %{buildroot}%{target}/LICENSE %{buildroot}%{target}/THIRD-PARTY-NOTICES.md
chmod 755 %{buildroot}%{target}/ibus-engine-meltype %{buildroot}%{target}/mozc/meltype_mozc_helper
sed "s|@DIR@|%{target}|g" %{meltype_src}/meltype.xml > %{buildroot}%{_datadir}/ibus/component/meltype.xml
%if %{with_fcitx5}
install -D -m 644 %{meltype_src}/fcitx5/meltype.so %{buildroot}%{_libdir}/fcitx5/meltype.so
install -D -m 644 %{meltype_src}/fcitx5/meltype-addon.conf %{buildroot}%{_datadir}/fcitx5/addon/meltype.conf
mkdir -p %{buildroot}%{_datadir}/fcitx5/inputmethod
sed "s|@DIR@|%{target}|g" %{meltype_src}/fcitx5/meltype.conf > %{buildroot}%{_datadir}/fcitx5/inputmethod/meltype.conf
%endif

%post
echo "Meltype を入れました。ログアウトしてログインし直してから、入力ソース (IBus) か fcitx5 の設定で Meltype を追加してください。"

# 外す前・更新の前: 動いている IBus のエンジンを止める (IBus は次に使うときに新しいものを起動する)
%preun
pkill -f %{target}/ibus-engine-meltype > /dev/null 2>&1 || :

# 設定と学習データ (~/.local/share/Meltype) は人ごとのものなので消さない
%files
%dir %{target}
%{target}/libMeltypeNative.so
%{target}/ibus-engine-meltype
%{target}/icon.png
%{target}/mozc
%{target}/THIRD-PARTY-NOTICES.md
%license %{target}/LICENSE
%{_datadir}/ibus/component/meltype.xml
%if %{with_fcitx5}
%{_libdir}/fcitx5/meltype.so
%{_datadir}/fcitx5/addon/meltype.conf
%{_datadir}/fcitx5/inputmethod/meltype.conf
%endif
EOF

mkdir -p "$out"
rpmbuild -bb --target "$arch" \
    --define "_topdir $stage/rpmbuild" \
    --define "_rpmdir $stage/out" \
    --define "meltype_version $rpmversion" \
    --define "meltype_src $src" \
    --define "with_fcitx5 $with_fcitx5" \
    "$stage/meltype.spec"
rpm="$(find "$stage/out" -name '*.rpm')"
cp "$rpm" "$out/"
echo "作成しました: $out/$(basename "$rpm")"
