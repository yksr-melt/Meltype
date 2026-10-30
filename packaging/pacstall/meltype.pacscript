# meltype: リリースの .deb を展開するだけの pacscript (ビルドなし)。
# 版を上げるときは pkgver と sha256sums_* を更新する。
pkgname="meltype"
pkgver="1.3.0"
arch=("amd64" "arm64")
license=("GPL-3.0-or-later")
url="https://github.com/yksr-melt/Meltype"
pkgdesc="雪解けのように、半角/全角の壁を溶かす日本語入力 (IBus / fcitx5)"
maintainer=("(未定。pacstall に出すときに決める)")
depends=("python3" "zlib1g" "libstdc++6" "libgcc-s1")
optdepends=("ibus: IBus で使う"
            "python3-gi: IBus で使う"
            "gir1.2-ibus-1.0: IBus で使う"
            "fcitx5: fcitx5 で使う")
breaks=("meltype-bin")
source=("meltype_${pkgver}_${CARCH}.deb::https://github.com/yksr-melt/Meltype/releases/download/v${pkgver}/meltype_${pkgver}_${CARCH}.deb")
sha256sums_amd64=("6eec30d93299399ec19f89c73cda0796ad6f08606cc106dec07a4166a3c64862")
sha256sums_arm64=("e6b9d0dc7e8dc40225b9ec02d9371b613d4a0f4c6545a190548d8f762faaa661")

package() {
    dpkg-deb -x "${srcdir}/meltype_${pkgver}_${CARCH}.deb" "${pkgdir}"
}

post_install() {
    if command -v ibus > /dev/null 2>&1; then ibus write-cache --system > /dev/null 2>&1 || true; fi
    echo "Meltype を入れました。ログアウトしてログインし直してから、入力ソース (IBus) か fcitx5 の設定で Meltype を追加してください。"
}
