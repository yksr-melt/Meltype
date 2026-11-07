// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import android.app.Activity
import android.app.AlertDialog
import android.content.ComponentName
import android.content.Intent
import android.content.res.Configuration
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.provider.Settings as SystemSettings
import android.view.Gravity
import android.view.KeyEvent
import android.view.WindowInsets
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.InputMethodManager
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Switch
import android.widget.TextView
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import java.util.Locale

/**
 * Settings screens inspired by the Android keyboard settings information
 * architecture. Only pending setup steps are shown; no tutorial or demo editor.
 */
class MainActivity : Activity() {
    private val settings by lazy { Settings(this) }
    private val density by lazy { resources.displayMetrics.density }
    private fun dp(n: Int) = (n * density + .5f).toInt()
    private enum class Page { HOME, LANGUAGES, GENERAL, CORRECTION, EMOJI }
    private var page = Page.HOME

    // Android 13+ gesture Back dispatches through this callback. Earlier
    // Android versions still call the onBackPressed() compatibility override.
    private var systemBackCallback: OnBackInvokedCallback? = null

    private fun navigateBack() {
        if (page == Page.HOME) finish() else navigate(Page.HOME)
    }

    /**
     * Android 15+ enforces edge-to-edge. Keep all settings content below the
     * status bar / display cutout and above the system navigation bar.
     * Apply insets to the top-level view only, including the split fold UI.
     */
    private fun protectSystemBars(root: View): View {
        // Some OEM edge-to-edge windows dispatch consumed (zero) insets to
        // Activity content even though the status bar is visibly overlaying it.
        // Use the actual window insets where available and the platform bar
        // dimensions as a fallback, rather than assuming a zero top inset.
        fun platformBarHeight(name: String): Int {
            val id = resources.getIdentifier(name, "dimen", "android")
            return if (id != 0) resources.getDimensionPixelSize(id) else 0
        }
        fun applyBars(insets: WindowInsets?) {
            @Suppress("DEPRECATION")
            val reportedTop = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                insets?.getInsetsIgnoringVisibility(
                    WindowInsets.Type.statusBars() or WindowInsets.Type.displayCutout())?.top ?: 0
            } else insets?.systemWindowInsetTop ?: 0
            @Suppress("DEPRECATION")
            val reportedBottom = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                insets?.getInsets(WindowInsets.Type.navigationBars())?.bottom ?: 0
            } else insets?.systemWindowInsetBottom ?: 0
            // Legacy and synthetic window-inset dispatch can carry dimensions
            // not populated in the newer typed accessors (also true on OEMs).
            // Keep the larger nonzero reported value so no page enters the bars.
            @Suppress("DEPRECATION")
            val legacyTop = insets?.systemWindowInsetTop ?: 0
            @Suppress("DEPRECATION")
            val legacyBottom = insets?.systemWindowInsetBottom ?: 0
            val top = maxOf(reportedTop, legacyTop, platformBarHeight("status_bar_height"))
            val bottom = maxOf(reportedBottom, legacyBottom, platformBarHeight("navigation_bar_height"))
            if (root.paddingTop != top || root.paddingBottom != bottom) {
                root.setPadding(0, top, 0, bottom)
            }
        }
        root.setOnApplyWindowInsetsListener { _, insets ->
            applyBars(insets)
            insets
        }
        root.addOnAttachStateChangeListener(object : View.OnAttachStateChangeListener {
            override fun onViewAttachedToWindow(view: View) {
                // Initial content insets can be zero before the first traversal.
                applyBars(window.decorView.rootWindowInsets)
                view.requestApplyInsets()
            }
            override fun onViewDetachedFromWindow(view: View) = Unit
        })
        // Protect the very first rendered frame, before Android delivers insets.
        applyBars(window.decorView.rootWindowInsets)
        return root
    }

    private data class Theme(val background: Int, val card: Int, val text: Int,
                             val secondary: Int, val line: Int)
    private val theme by lazy {
        if (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK ==
            Configuration.UI_MODE_NIGHT_YES) Theme(
            0xFF1D1D1F.toInt(), 0xFF39393C.toInt(), Color.WHITE,
            0xFFC4C4CA.toInt(), 0xFF505055.toInt()
        ) else Theme(
            0xFFF5F5F8.toInt(), Color.WHITE, 0xFF1A1A20.toInt(),
            0xFF686872.toInt(), 0xFFE5E5EA.toInt()
        )
    }

    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        page = state?.getString("page")?.let { runCatching { Page.valueOf(it) }.getOrNull() } ?: Page.HOME
        // Let our root own *all* bar insets. Android 15+ is edge-to-edge
        // regardless of target window flags; explicitly match that behavior
        // on Android 11-14 so our layout receives unconsumed status insets.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false)
        }
        window.statusBarColor = theme.background
        window.navigationBarColor = theme.background
        if (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK !=
            Configuration.UI_MODE_NIGHT_YES) {
            @Suppress("DEPRECATION")
            // Keep LAYOUT_FULLSCREEN / LAYOUT_HIDE_NAVIGATION flags installed
            // by setDecorFitsSystemWindows(false); replacing all flags here
            // would silently undo the edge-to-edge layout policy.
            window.decorView.systemUiVisibility =
                window.decorView.systemUiVisibility or
                View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR or View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            val callback = OnBackInvokedCallback { navigateBack() }
            systemBackCallback = callback
            onBackInvokedDispatcher.registerOnBackInvokedCallback(
                OnBackInvokedDispatcher.PRIORITY_DEFAULT, callback)
        }
        render()
    }

    override fun onDestroy() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            systemBackCallback?.let(onBackInvokedDispatcher::unregisterOnBackInvokedCallback)
            systemBackCallback = null
        }
        super.onDestroy()
    }

    override fun onSaveInstanceState(state: Bundle) {
        state.putString("page", page.name)
        super.onSaveInstanceState(state)
    }
    override fun onResume() { super.onResume(); render() }
    @Deprecated("Android 13+ uses the registered platform callback")
    override fun onBackPressed() = navigateBack()

    // Handle a hardware/adb Back key as one complete DOWN+UP interaction.
    // Navigating on DOWN makes an unmatched UP escape to Android's predictive
    // back dispatcher, which can close Settings immediately after returning
    // to HOME. Back gestures still use the OnBackInvokedCallback above.
    private var backKeyCaptured = false

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (event.keyCode == KeyEvent.KEYCODE_BACK) {
            when (event.action) {
                KeyEvent.ACTION_DOWN -> if (page != Page.HOME || backKeyCaptured) {
                    backKeyCaptured = true
                    return true
                }
                KeyEvent.ACTION_UP -> if (backKeyCaptured) {
                    backKeyCaptured = false
                    if (!event.isCanceled) navigateBack()
                    return true
                }
            }
        }
        return super.dispatchKeyEvent(event)
    }
    private fun navigate(next: Page) { page = next; render() }
    private fun background(color: Int) = GradientDrawable().apply {
        setColor(color)
        cornerRadius = dp(22).toFloat()
    }
    private fun text(value: String, size: Float, muted: Boolean = false, strong: Boolean = false) =
        TextView(this).apply {
            text = value; textSize = size
            setTextColor(if (muted) theme.secondary else theme.text)
            if (strong) typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
        }

    private fun LinearLayout.divider() {
        addView(View(this@MainActivity).apply { setBackgroundColor(theme.line) },
            LinearLayout.LayoutParams(-1, dp(1)).apply {
                marginStart = dp(18); marginEnd = dp(18)
            })
    }

    private fun LinearLayout.entry(
        title: String, detail: String? = null, symbol: String? = null,
        checked: Boolean? = null, onToggle: ((Boolean) -> Unit)? = null,
        selected: Boolean = false, action: (() -> Unit)? = null
    ) {
        val row = LinearLayout(this@MainActivity).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(18), dp(10), dp(18), dp(10))
            minimumHeight = dp(if (detail == null) 68 else 84)
            if (selected) setBackgroundColor(
                if (theme.background == 0xFF1D1D1F.toInt()) 0xFF25252A.toInt()
                else 0xFFE8EDF6.toInt()
            )
        }
        if (symbol != null) row.addView(text(symbol, 21f, muted = true).apply {
            gravity = Gravity.CENTER
        }, LinearLayout.LayoutParams(dp(35), dp(40)).apply { marginEnd = dp(14) })
        val labels = LinearLayout(this@MainActivity).apply { orientation = LinearLayout.VERTICAL }
        labels.addView(text(title, 16f, strong = true))
        if (detail != null) labels.addView(text(detail, 13f, muted = true),
            LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(4) })
        row.addView(labels, LinearLayout.LayoutParams(0, -2, 1f))
        if (checked != null) {
            val toggle = Switch(this@MainActivity).apply {
                isChecked = checked
                contentDescription = title
                setOnCheckedChangeListener { _, value -> onToggle?.invoke(value) }
            }
            row.addView(toggle, LinearLayout.LayoutParams(-2, -2).apply { marginStart = dp(12) })
            row.setOnClickListener { toggle.isChecked = !toggle.isChecked }
            row.isFocusable = true
        } else if (action != null) {
            row.addView(text("›", 26f, muted = true).apply { gravity = Gravity.CENTER },
                LinearLayout.LayoutParams(dp(25), -2))
            row.setOnClickListener { action() }
            row.isFocusable = true
        }
        addView(row)
    }

    private fun card(container: LinearLayout, heading: String? = null, items: LinearLayout.() -> Unit) {
        if (heading != null) container.addView(text(heading, 13f, strong = true),
            LinearLayout.LayoutParams(-1, -2).apply {
                marginStart = dp(8); topMargin = dp(16); bottomMargin = dp(10)
            })
        val group = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = background(theme.card)
            clipToOutline = true
            items()
        }
        container.addView(group, LinearLayout.LayoutParams(-1, -2).apply { bottomMargin = dp(17) })
    }

    private fun title(container: LinearLayout, value: String) {
        container.addView(text(value, 32f, strong = true),
            LinearLayout.LayoutParams(-1, -2).apply {
                marginStart = dp(6); topMargin = dp(45); bottomMargin = dp(24)
            })
    }

    private fun hint(container: LinearLayout, value: String) {
        container.addView(text(value, 13f, muted = true),
            LinearLayout.LayoutParams(-1, -2).apply {
                marginStart = dp(8); marginEnd = dp(8)
            })
    }

    private fun choose(title: String, selected: Int, values: List<Pair<String, () -> Unit>>) {
        AlertDialog.Builder(this).setTitle(title)
            .setSingleChoiceItems(values.map { it.first }.toTypedArray(), selected) { dialog, index ->
                values[index].second()
                dialog.dismiss()
                render()
            }.setNegativeButton(android.R.string.cancel, null).show()
    }

    private fun setupStatus(): Int {
        val imm = getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager
        val id = ComponentName(this, KeyboardService::class.java).flattenToShortString()
        if (imm.enabledInputMethodList.none { it.id == id }) return 1
        val selected = SystemSettings.Secure.getString(
            contentResolver, SystemSettings.Secure.DEFAULT_INPUT_METHOD)
        return if (selected == id) 0 else 2
    }

    /**
     * Phones show one screen at a time. Expanded foldables keep the category
     * list and selected section visible side-by-side, like Gboard.
     */
    private fun render() {
        val expanded = resources.configuration.screenWidthDp >= 700
        if (!expanded) {
            setContentView(protectSystemBars(pane(page)))
            return
        }

        val active = if (page == Page.HOME) Page.GENERAL else page
        val split = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setBackgroundColor(theme.background)
            addView(pane(Page.HOME, active), LinearLayout.LayoutParams(0, -1, 1f))
            addView(View(this@MainActivity).apply { setBackgroundColor(theme.line) },
                LinearLayout.LayoutParams(dp(1), -1))
            addView(pane(active), LinearLayout.LayoutParams(0, -1, 1f))
        }
        setContentView(protectSystemBars(split))
    }

    private fun pane(target: Page, selected: Page? = null): ScrollView {
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(21), dp(10), dp(21), dp(40))
        }
        val top = LinearLayout(this).apply {
            gravity = Gravity.CENTER_VERTICAL
            minimumHeight = dp(56)
        }
        if (target != Page.HOME) {
            val back = text("‹", 31f).apply {
                gravity = Gravity.CENTER
                contentDescription = "戻る"
                background = GradientDrawable().apply {
                    setColor(theme.card)
                    shape = GradientDrawable.OVAL
                }
                setOnClickListener { navigateBack() }
                isFocusable = true
                isClickable = true
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_YES
            }
            top.addView(back, LinearLayout.LayoutParams(dp(44), dp(44)).apply {
                marginEnd = dp(16)
            })
        }
        top.addView(text(if (target == Page.HOME) "Meltype の設定" else "設定",
            19f, strong = true))
        content.addView(top)
        when (target) {
            Page.HOME -> home(content, selected)
            Page.LANGUAGES -> languages(content)
            Page.GENERAL -> general(content)
            Page.CORRECTION -> correction(content)
            Page.EMOJI -> emoji(content)
        }
        return ScrollView(this).apply {
            isFillViewport = true
            setBackgroundColor(theme.background)
            addView(content)
        }
    }

    private fun home(content: LinearLayout, selected: Page? = null) {
        content.addView(View(this), LinearLayout.LayoutParams(1, dp(14)))
        when (setupStatus()) {
            1 -> card(content) {
                entry("Meltypeを有効にする", "Androidのキーボード設定を開く", "⌨") {
                    startActivity(Intent(SystemSettings.ACTION_INPUT_METHOD_SETTINGS))
                }
            }
            2 -> card(content) {
                entry("Meltypeを選択する", "入力方法として有効にする", "⌨") {
                    (getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager).showInputMethodPicker()
                }
            }
        }
        card(content) {
            entry("言語", "日本語・English", "◎", selected = selected == Page.LANGUAGES) { navigate(Page.LANGUAGES) }
            divider()
            entry("設定", "スペース・振動・キー操作", "☷", selected = selected == Page.GENERAL) { navigate(Page.GENERAL) }
        }
        card(content) {
            entry("修正と候補", "ライブ変換・入力候補", "✓", selected = selected == Page.CORRECTION) { navigate(Page.CORRECTION) }
            divider()
            entry("絵文字", if (settings.emojiEnabled) "有効" else "無効", "☺", selected = selected == Page.EMOJI) {
                navigate(Page.EMOJI)
            }
        }
        card(content) {
            entry("ライセンス", "オープンソースに関する情報", "ⓘ") {
                startActivity(Intent(this@MainActivity, LicensesActivity::class.java))
            }
        }
    }

    private fun languages(content: LinearLayout) {
        title(content, "言語")
        card(content, "入力レイアウト") {
            entry("日本語", if (settings.japaneseLayout == LayoutId.ROMAJI)
                "QWERTY（ローマ字）" else "12キー（フリック）") {
                choose("日本語の入力", if (settings.japaneseLayout == LayoutId.ROMAJI) 0 else 1,
                    listOf("QWERTY（ローマ字）" to { settings.japaneseLayout = LayoutId.ROMAJI },
                        "12キー（フリック）" to { settings.japaneseLayout = LayoutId.KANA }))
            }
            divider()
            entry("English", if (settings.englishLayout == LayoutId.QWERTY)
                "QWERTY" else "12-key flick") {
                choose("英語の入力", if (settings.englishLayout == LayoutId.QWERTY) 0 else 1,
                    listOf("QWERTY" to { settings.englishLayout = LayoutId.QWERTY },
                        "12-key flick" to { settings.englishLayout = LayoutId.LATIN_FLICK }))
            }
        }
        hint(content, "日本語QWERTYでは、入力内容から日本語と英語を自動判別する。")
    }

    private fun general(content: LinearLayout) {
        title(content, "設定")
        card(content, "キー") {
            entry("日本語入力時のスペース", if (settings.japaneseSpaceFullWidth) "全角" else "半角") {
                choose("日本語入力時のスペース", if (settings.japaneseSpaceFullWidth) 0 else 1,
                    listOf("全角" to { settings.japaneseSpaceFullWidth = true },
                        "半角" to { settings.japaneseSpaceFullWidth = false }))
            }
            divider()
            entry("キー入力時に振動", "キーを押したときの触覚フィードバック",
                checked = settings.hapticEnabled,
                onToggle = { settings.hapticEnabled = it })
        }
        card(content, "12キー") {
            entry("トグル入力", "連続タップで同じキーの文字を切り替える",
                checked = settings.toggleEnabled,
                onToggle = { settings.toggleEnabled = it; render() })
            if (settings.toggleEnabled) {
                divider()
                entry("トグル判定時間", waitText(settings.toggleWaitMs)) { timing() }
            }
        }
    }

    private fun timing() {
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(8), dp(18), dp(4))
        }
        val value = text(waitText(settings.toggleWaitMs), 17f)
        content.addView(value)
        content.addView(SeekBar(this).apply {
            max = WAIT_STEPS - 1
            progress = progressOf(settings.toggleWaitMs)
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(seek: SeekBar, n: Int, fromUser: Boolean) {
                    value.text = waitText(waitOfProgress(n))
                    if (fromUser) settings.toggleWaitMs = waitOfProgress(n)
                }
                override fun onStartTrackingTouch(seek: SeekBar) = Unit
                override fun onStopTrackingTouch(seek: SeekBar) = Unit
            })
        })
        AlertDialog.Builder(this).setTitle("トグル判定時間")
            .setView(content).setPositiveButton(android.R.string.ok) { _, _ -> render() }.show()
    }

    private fun correction(content: LinearLayout) {
        title(content, "修正と候補")
        card(content, "変換") {
            entry("ライブ変換", "入力途中に漢字・かなの変換候補を更新する",
                checked = settings.liveConversionEnabled,
                onToggle = { settings.liveConversionEnabled = it })
        }
        hint(content, "日英自動判別と予測候補は、ライブ変換とは独立して動作する。")
    }

    private fun emoji(content: LinearLayout) {
        title(content, "絵文字")
        card(content) {
            entry("絵文字を使う", "絵文字キー・パネル・候補の表示",
                checked = settings.emojiEnabled,
                onToggle = { settings.emojiEnabled = it })
        }
    }

    companion object {
        private const val WAIT_STEPS = 18
        private const val WAIT_BASE_MS = 300
        private const val WAIT_STEP_MS = 100
        fun waitOfProgress(n: Int) =
            WAIT_BASE_MS + n.coerceIn(0, WAIT_STEPS - 1) * WAIT_STEP_MS
        fun progressOf(ms: Int) =
            ((ms - WAIT_BASE_MS) / WAIT_STEP_MS).coerceIn(0, WAIT_STEPS - 1)
        fun waitText(ms: Int) =
            String.format(Locale.US, "%.1f 秒", ms / 1000.0)
    }
}
