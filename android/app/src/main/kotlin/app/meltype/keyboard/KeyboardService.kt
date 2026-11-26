// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package app.meltype.keyboard

import android.content.Context
import android.content.SharedPreferences
import android.graphics.Color
import android.content.Intent
import android.widget.TextView
import java.lang.ref.WeakReference
import kotlin.math.abs
import android.inputmethodservice.InputMethodService
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.KeyEvent
import android.view.View
import android.view.ViewConfiguration
import android.view.ViewTreeObserver
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.text.InputType
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.view.Gravity
import app.meltype.keyboard.core.CaretWatch
import app.meltype.keyboard.core.CoreInputAdapter
import app.meltype.keyboard.core.CoreMozcCallbacks
import app.meltype.keyboard.core.ImeFieldFacts
import app.meltype.keyboard.core.ImeJson
import app.meltype.keyboard.core.ImeView
import app.meltype.keyboard.core.MeltypeCoreHost
import app.meltype.keyboard.core.coreDataDirectory
import app.meltype.keyboard.emoji.EmojiCatalog
import app.meltype.keyboard.emoji.EmojiPanelView
import app.meltype.keyboard.emoji.EmojiRecentsStore
import app.meltype.keyboard.emoji.PrefsStore
import app.meltype.keyboard.input.Direction
import app.meltype.keyboard.input.EditorBridge
import app.meltype.keyboard.input.FieldPolicy
import app.meltype.keyboard.input.ShiftState
import app.meltype.keyboard.input.enterAction
import app.meltype.keyboard.input.enterKind
import app.meltype.keyboard.input.fieldPolicy
import app.meltype.keyboard.layout.KeyAction
import app.meltype.keyboard.layout.LayoutId
import app.meltype.keyboard.layout.LayoutNavigator
import app.meltype.keyboard.layout.Layouts
import app.meltype.keyboard.layout.withoutEmoji
import app.meltype.keyboard.layout.ScreenCoordinator
import app.meltype.keyboard.view.BottomInsets
import app.meltype.keyboard.view.CandidateBarView
import app.meltype.keyboard.view.KeyListener
import app.meltype.keyboard.view.KeyboardView
import app.meltype.keyboard.view.LanguagePickerPopup
import app.meltype.keyboard.view.ImeGlassSurface
import app.meltype.keyboard.view.KeyboardTopEdgeHighlight
import app.meltype.keyboard.view.Palette
import app.meltype.keyboard.view.bottomPaddingPx
import app.meltype.keyboard.mozc.MozcBackend
import app.meltype.keyboard.mozc.MozcConverter
import app.meltype.keyboard.mozc.MozcEngine
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit

/**
 * Android の入力フロント。日本語 QWERTY の入力判断は Meltype.Core/Ime が行う。
 * Android は入力欄の情報を送信し、Core の操作指示を反映する。
 *
 * 変換以外はすべて画面のスレッドで動く。
 */
class KeyboardService : InputMethodService(), KeyListener {
    /** Mozc を開く1本のスレッド。止めたあとに来た依頼は黙って捨てる。 */
    private val worker = ThreadPoolExecutor(
        1, 1, 0L, TimeUnit.MILLISECONDS, LinkedBlockingQueue(), ThreadPoolExecutor.DiscardPolicy(),
    )
    private val mainHandler = Handler(Looper.getMainLooper())
    private val bridge = EditorBridge { currentInputConnection }
    /** Where the Core's edits leave the caret; any other move is reported to the Core (CaretMoved). */
    private val caretWatch = CaretWatch()
    private val coreInput = CoreInputAdapter(caretWatch) { currentInputConnection }
    private var core: MeltypeCoreHost? = null
    private var nativeCoreUnavailable = false

    /** The shared Core (ImeHost) takes every key, as on Windows/Mac/Linux. Null once it has failed. */
    private fun coreHost(): MeltypeCoreHost? {
        if (nativeCoreUnavailable) return null
        core?.let { return it }
        return try {
            MeltypeCoreHost.open(coreDataDirectory(this), coreCallbacks(this)).also { core = it }
        } catch (error: RuntimeException) {
            enterEmergencyMode(error)
            null
        } catch (error: LinkageError) {
            enterEmergencyMode(error)
            null
        }
    }

    /** What the editor tells about itself (ImeTrait names). Facts only: no keyboard policy. */
    private var fieldTraits: List<String> = emptyList()

    /** The editor's app (EditorInfo.packageName), the name app rules use. */
    private var fieldApp: String? = null

    /** False for an editor without text of its own (TYPE_NULL): what it returns is not the app's text. */
    private var fieldReadable = true

    private fun fieldFacts(): ImeFieldFacts {
        val start = System.nanoTime()
        // パスワード欄の文字は読まない (Core はパスワード欄を受け持たず、文字も使わない)。
        return coreInput.field(fieldTraits, fieldApp, fieldReadable && !policy.isPassword, selected = selStart != selEnd).also {
            if (PerfLog.enabled) PerfLog.event("field", (System.nanoTime() - start) / 1000, it.before?.length ?: -1)
        }
    }

    /**
     * The Core already knows that the caret may have moved (the last event said so and no key came
     * since). Further moves tell it nothing new, so they are not sent again (the Kotlin input path
     * moves the caret on every key).
     */
    private var coreKnowsMoved = false

    /** Caret moves by the user or the app seen so far (smoke test only). */
    private var outsideCaretMoves = 0

    /** Sends one event to the Core and applies the response. Returns whether the Core consumed it. */
    private fun coreSend(instance: MeltypeCoreHost, event: String, moved: Boolean = false): Boolean {
        coreKnowsMoved = moved
        return try {
            val response = instance.handle(event)
                ?: throw IllegalStateException("Native Core returned no output")
            val consumed = coreInput.apply(response)
            candidateBar?.show(coreInput.candidates, coreInput.selected)
            // The letters page shows the Core's language mode.
            if (nav.japanese != coreInput.japanese) {
                nav.japanese = coreInput.japanese
                renderKeys()
            }
            consumed
        } catch (error: RuntimeException) {
            enterEmergencyMode(error)
            false
        } catch (error: LinkageError) {
            enterEmergencyMode(error)
            false
        }
    }

    /** A key that types [ch]. The Core decides its virtual key from the character. A key the Core passes on goes into the editor. */
    private fun typeCharacter(ch: Int, shift: Boolean = false) {
        val instance = coreHost() ?: return
        val consumed = coreSend(instance, ImeJson.characterKey(ch, shift, fieldFacts()))
        if (core != null && !consumed) insertPassed(String(Character.toChars(ch)))
    }

    /** A key without a character (Back 0x08, Enter 0x0D, ← 0x25, → 0x27). Returns whether the Core consumed it. */
    private fun coreVirtualKey(vk: Int): Boolean {
        val instance = coreHost() ?: return false
        return coreSend(instance, ImeJson.virtualKey(vk, fieldFacts()))
    }

    /** An event that changes the language mode (あa, the language menu). */
    private fun coreMode(event: (ImeFieldFacts) -> String) {
        val instance = coreHost() ?: return
        coreSend(instance, event(fieldFacts()))
    }

    /** The Core's converter finished in the background: the Core redraws the composing text with the result. */
    private fun coreConversionFinished() {
        val instance = core ?: return
        if (currentInputConnection == null || !coreInput.composing) return
        coreSend(instance, ImeJson.conversionFinished(fieldFacts()))
    }

    private fun commitCore() {
        val instance = core ?: return
        if (coreInput.composing) coreSend(instance, ImeJson.commitRequested(fieldFacts()), moved = true)
    }

    /**
     * The editor keeps the composing text as shown (finishComposingText, or the editor itself
     * when it loses focus): the Core takes it as committed by the app (CommittedByApp), as IBus
     * does. Without composing text, the Core still commits what it may hold (CommitRequested).
     * Either way the caret may be elsewhere next time. [field] is false when the editor is going
     * away: the Core then keeps the facts it last had.
     */
    private fun leaveCore(focusLeft: Boolean, field: Boolean = false) {
        val instance = core
        val composing = coreInput.composing
        coreInput.clear()
        if (instance == null) return
        val facts = if (field) fieldFacts() else null
        val event = if (composing) ImeJson.committedByApp(facts, focusLeft) else ImeJson.commitRequested(facts, focusLeft)
        coreSend(instance, event, moved = true)
    }

    /**
     * The user or the app moved the caret (a tap, the arrow keys, an edit by the app or by the
     * Kotlin input path). Composing text stays where it is, committed as shown; otherwise the
     * Core learns that the caret moved, so it does not re-commit the previous word there.
     */
    private fun caretMovedOutside() {
        outsideCaretMoves++
        val instance = core ?: return
        if (coreInput.composing) {
            currentInputConnection?.finishComposingText()
            leaveCore(focusLeft = false, field = true)
        } else if (!coreKnowsMoved) {
            coreSend(instance, ImeJson.caretMoved(fieldFacts()), moved = true)
        }
    }

    /**
     * A send button outside the IME can empty the editor without a focus change.
     * The editor's old composing text is gone; do not finish or reinsert it.
     * Notify the Core that its composition was handled by the app, without
     * generating an editor commit. Only a readable, completely empty editor
     * qualifies; a normal caret move must keep its existing behavior.
     */
    private fun forgetCompositionIfEditorWasCleared(): Boolean {
        if (!coreInput.composing || !fieldReadable) return false
        val input = currentInputConnection ?: return false
        if (input.getTextBeforeCursor(1, 0)?.isEmpty() != true ||
            input.getTextAfterCursor(1, 0)?.isEmpty() != true) return false
        leaveCore(focusLeft = false, field = true)
        dropComposition()
        caretWatch.reset(selStart, selEnd)
        return true
    }

    /** Inserts a character the Core passed on (did not consume), as the app would for a hardware key. */
    private fun insertPassed(text: String) {
        insert(text)
        caretWatch.replaced(0, text.length)
    }

    private fun discardCore() {
        core?.close()
        core = null
        coreInput.clear()
    }

    /** One Japanese engine only. Preserve existing text, then offer a system-level IME escape. */
    private fun enterEmergencyMode(error: Throwable) {
        Log.e(TAG, "Shared Core unavailable; opening emergency IME selector", error)
        try {
            currentInputConnection?.finishComposingText()
        } catch (cleanupError: RuntimeException) {
            Log.e(TAG, "Could not finish composing before emergency switch", cleanupError)
        }
        nativeCoreUnavailable = true
        try {
            discardCore()
        } catch (cleanupError: Throwable) {
            Log.e(TAG, "Could not dispose failed Core", cleanupError)
            core = null
            coreInput.clear()
        }
        setInputView(buildEmergencyKeyboardView())
    }

    /** True once the Japanese QWERTY path has really gone through the native Core (smoke test only). */
    internal fun nativeCoreActiveForSmokeTest(): Boolean = core != null && !nativeCoreUnavailable

    /** How many caret moves were taken as made by the user or the app (smoke test only). */
    internal fun outsideCaretMovesForSmokeTest(): Int = outsideCaretMoves

    /** The Core's last view (smoke test only). */
    internal fun coreViewForSmokeTest(): ImeView? = coreInput.view

    /** The candidate bar, to tap a candidate as a finger would (smoke test only). */
    internal fun candidateBarForSmokeTest(): CandidateBarView? = candidateBar

    private val languagePopup by lazy { LanguagePickerPopup(this) }
    private var keyboardView: KeyboardView? = null
    private var candidateBar: CandidateBarView? = null
    private var emojiPanel: EmojiPanelView? = null
    private val emojiRecents by lazy { EmojiRecentsStore(PrefsStore(this, "emoji")) }

    private var policy = FieldPolicy(emptyList(), symbols = false)
    private var imeOptions = 0
    /** いまのページと、?123・絵文字・戻るキーの行き先。文字のページの言語は Core の言語モード。 */
    private val nav = LayoutNavigator()
    private val noEmojiLayouts = mutableMapOf<LayoutId, app.meltype.keyboard.layout.KeyboardLayout>()

    /** 配列の切り替えと、絵文字の画面の開け閉め。どの順で確定して何を出すかは [ScreenCoordinator] が決める。 */
    private val screens = ScreenCoordinator(nav, object : ScreenCoordinator.Screen {
        override fun showKeyboard() = showLayout()
        override fun releaseShift() = setShift(ShiftState.Off)
        override fun showEmojiPanel(pressedAtMs: Long) = openEmojiPanel(pressedAtMs)
    })
    private var shift: ShiftState = ShiftState.Off

    private var selStart = 0
    private var selEnd = 0

    /** 空白キーの長押しで動かしているあいだの、カーソルの位置。入力欄からの知らせは遅れて届くので、自分で数える。 */
    private var cursorPosition: Int? = null

    /** キーボードの画面の根と、その中の候補バーとキーの列。下の余白は列に付ける。 */
    private var inputRoot: View? = null
    private var keyColumn: View? = null

    private var createdAtNanos = 0L

    /** Exposes the displayed IME keys to real touch-injection tests. */
    internal fun activeKeysForSmokeTest(): KeyboardView? =
        keyboardView?.takeIf { it.isAttachedToWindow && it.isShown && it.width > 0 && it.height > 0 }

    override fun onCreate() {
        super.onCreate()
        activeForSmokeTest = WeakReference(this)
        createdAtNanos = System.nanoTime()
        active = WeakReference(this)
        Settings(this).registerChangeListener(settingsListener)
        // 辞書を写すので時間がかかる。開き終わるまで、Core の変換は待つ。
        val mozc = mozc(this)
        worker.execute { mozc.value }
    }

    // 設定が変わったら、開いているキーボードにもすぐ効かせる。弱い参照で持たれるので、フィールドで握る。
    private val settingsListener = SharedPreferences.OnSharedPreferenceChangeListener { _, _ ->
        showLayout()
    }

    override fun onDestroy() {
        if (activeForSmokeTest?.get() === this) activeForSmokeTest = null
        if (active?.get() === this) active = null
        Settings(this).unregisterChangeListener(settingsListener)
        languagePopup.dismiss()
        discardCore()
        worker.shutdown()
        super.onDestroy()
    }

    /** 横向きでも入力欄を全画面にしない（アプリの画面を見ながら打てるようにする）。 */
    override fun onEvaluateFullscreenMode(): Boolean = false

    override fun onCreateInputView(): View = try {
        if (nativeCoreUnavailable) buildEmergencyKeyboardView() else buildNormalKeyboardView()
    } catch (error: Exception) {
        Log.e(TAG, "Keyboard UI failed; opening emergency selector", error)
        buildEmergencyKeyboardView()
    } catch (error: LinkageError) {
        Log.e(TAG, "Keyboard native dependency failed; opening emergency selector", error)
        buildEmergencyKeyboardView()
    }

    /** Use Android's system picker; never require launching our settings Activity to escape. */
    private fun showSystemImePicker() {
        try {
            (getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager).showInputMethodPicker()
        } catch (error: RuntimeException) {
            // An IME cannot force the system picker when Android refuses this request.
            Log.e(TAG, "Android input method picker unavailable", error)
        }
    }

    /** No native converter or keyboard assets are used in this emergency UI. */
    internal fun emergencyKeyboardForSmokeTest(): View = buildEmergencyKeyboardView()

    private fun buildEmergencyKeyboardView(): View {
        val pad = (16 * resources.displayMetrics.density).toInt()
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(pad, pad, pad, pad)
            setBackgroundColor(Color.WHITE)
            addView(TextView(this@KeyboardService).apply {
                text = "Meltype を起動できませんでした"
                textSize = 16f
                setTextColor(Color.BLACK)
            })
            addView(TextView(this@KeyboardService).apply {
                text = "別のキーボードに切り替える"
                contentDescription = text
                gravity = Gravity.CENTER
                textSize = 16f
                setTextColor(Color.BLUE)
                minimumHeight = (48 * resources.displayMetrics.density).toInt()
                isClickable = true
                isFocusable = true
                setOnClickListener { showSystemImePicker() }
            })
        }
    }

    private fun buildNormalKeyboardView(): View {
        val start = System.nanoTime()
        val bar = CandidateBarView(this).also { it.onPick = { index ->
            val instance = core
            val choice = coreInput.choices.getOrNull(index)
            // The Core decides what the tap does (a prediction commits; a conversion candidate is chosen for its clause).
            if (instance != null && choice != null) coreSend(instance, ImeJson.candidateClicked(choice.nativeIndex, fieldFacts()))
        } }
        val keys = KeyboardView(this).also { it.listener = this }
        // 絵文字の画面は、開いているあいだ、候補バーとキーの代わりに同じ場所に出る（高さは同じ）。
        val emoji = EmojiPanelView(this).also {
            it.keys.listener = this; it.recents = { emojiRecents.list }; it.onPick = ::onEmojiPicked; it.visibility = View.GONE
        }
        EmojiCatalog.load(this)   // 端末で描ける絵文字を、別のスレッドで調べておく
        candidateBar = bar
        keyboardView = keys
        emojiPanel = emoji
        showLayout()
        val background = Palette.of(this).background
        styleNavigationBar(background)
        ImeGlassSurface.configureWindow(window?.window, this)
        val column = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            addView(bar, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT))
            addView(keys, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT))
            addView(emoji, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT))
        }
        keyColumn = column
        return FrameLayout(this).apply {
            inputRoot = this
            this.background = ImeGlassSurface.keyboardBackground(this@KeyboardService)
            addView(column, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.WRAP_CONTENT))
            // Top-only, anti-aliased 1dp hairline follows the keyboard's 28dp
            // rounded shoulders, like the user's iOS reference screenshot.
            // Decorative: does not consume touches or focus.
            addView(
                KeyboardTopEdgeHighlight(this@KeyboardService),
                FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT,
                    (30f * resources.displayMetrics.density).toInt(),
                    Gravity.TOP,
                ),
            )
            // 端末が下に描くもの（ナビゲーションバー、「閉じる」と切り替えのボタン）と重ならないよう、下を空ける。
            // 空けるのは候補バーとキーの下。案内の View は、この余白にも重なる。
            setOnApplyWindowInsetsListener { _, insets ->
                applyBottomPadding(insets)
                insets
            }
            // 作り直した画面には、端末からの知らせがもう来ないことがある（ライトとダークの切り替えのあとなど）。
            // 窓に付いたところで、そのときの値で合わせ、知らせも頼んでおく。
            addOnAttachStateChangeListener(object : View.OnAttachStateChangeListener {
                override fun onViewAttachedToWindow(view: View) = applyCurrentInsets()
                override fun onViewDetachedFromWindow(view: View) = Unit
            })
            if (PerfLog.enabled) logFirstDraw(this, start)
        }
    }

    /** [view] が最初に描かれたときに、[start] からと、サービスができてからの時間を出す。 */
    private fun logFirstDraw(view: View, start: Long) {
        view.viewTreeObserver.addOnPreDrawListener(object : ViewTreeObserver.OnPreDrawListener {
            override fun onPreDraw(): Boolean {
                view.viewTreeObserver.removeOnPreDrawListener(this)
                // この描画が終わったところで測る。
                view.post {
                    val now = System.nanoTime()
                    PerfLog.event("first-draw", (now - start) / 1000)
                    PerfLog.event("first-draw-since-create", (now - createdAtNanos) / 1000)
                }
                return true
            }
        })
    }

    /** ナビゲーション領域に端末が描くボタン（閉じる、キーボードの切り替え）が、背景の上で見えるようにする。 */
    private fun styleNavigationBar(background: Int) {
        val imeWindow = window?.window ?: return
        val light = Color.luminance(background) > 0.5f
        // 3ボタンのバーに端末が重ねる薄い幕をやめて、バーの後ろもキーボードの背景色のままにする（ボタンの色は下で合わせる）。
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) imeWindow.isNavigationBarContrastEnforced = false
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val flag = WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS
            imeWindow.insetsController?.setSystemBarsAppearance(if (light) flag else 0, flag)
        } else {
            val decor = imeWindow.decorView
            @Suppress("DEPRECATION")
            decor.systemUiVisibility = if (light) {
                decor.systemUiVisibility or View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR
            } else {
                decor.systemUiVisibility and View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR.inv()
            }
        }
    }

    private fun applyBottomPadding(insets: WindowInsets) {
        val bottom = bottomPaddingPx(Build.VERSION.SDK_INT, bottomInsets(insets))
        keyColumn?.let { if (it.paddingBottom != bottom) it.setPadding(0, 0, 0, bottom) }
    }

    /** いま窓が持っている値で下の余白を合わせ、端末にも知らせを頼む。 */
    private fun applyCurrentInsets() {
        val root = inputRoot ?: return
        root.rootWindowInsets?.let(::applyBottomPadding)
        root.requestApplyInsets()
    }

    /** 端末が知らせてきた値から、下の端の分だけを取り出す。どれだけ空けるかは [bottomPaddingPx] が決める。 */
    private fun bottomInsets(insets: WindowInsets): BottomInsets {
        @Suppress("DEPRECATION")
        val legacy = insets.systemWindowInsetBottom
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            BottomInsets(
                navigationBars = insets.getInsets(WindowInsets.Type.navigationBars()).bottom,
                tappableElement = insets.getInsets(WindowInsets.Type.tappableElement()).bottom,
                systemBars = insets.getInsets(WindowInsets.Type.systemBars()).bottom,
                legacy = legacy,
            )
        } else {
            BottomInsets(navigationBars = 0, tappableElement = 0, systemBars = 0, legacy = legacy)
        }
    }

    override fun onStartInputView(info: EditorInfo, restarting: Boolean) {
        if (nativeCoreUnavailable) {
            super.onStartInputView(info, restarting)
            setInputView(buildEmergencyKeyboardView())
            return
        }
        try {
            startNormalInputView(info, restarting)
        } catch (error: Exception) {
            Log.e(TAG, "Keyboard start failed; opening emergency selector", error)
            setInputView(buildEmergencyKeyboardView())
        } catch (error: LinkageError) {
            Log.e(TAG, "Keyboard native startup failed; opening emergency selector", error)
            setInputView(buildEmergencyKeyboardView())
        }
    }

    private fun startNormalInputView(info: EditorInfo, restarting: Boolean) {
        super.onStartInputView(info, restarting)
        // A restart after an external send must not revive a composition the app deleted.
        forgetCompositionIfEditorWasCleared()
        // Also refresh this on every editor focus, not only layout changes.
        keyboardView?.hapticEnabled = Settings(this).hapticEnabled
        // 同じ入力欄のまま始め直されることがある（アプリが作り直されない画面の回転など）。そのとき入力欄には
        // 未確定文字が残っていて、何もしないと次の1文字がそれを置き換えて消してしまう。先に確定させる。
        // 新しい入力欄では、何も書き込まない。
        currentInputConnection?.finishComposingText()
        dropComposition()
        // Restarted in the same editor without finishing it: the editor kept the composing text.
        if (coreInput.composing) leaveCore(focusLeft = false)
        policy = fieldPolicy(info.inputType, info.imeOptions)
        imeOptions = info.imeOptions
        fieldTraits = policy.traits
        fieldApp = info.packageName
        fieldReadable = info.inputType != InputType.TYPE_NULL
        selStart = info.initialSelStart
        selEnd = info.initialSelEnd
        caretWatch.reset(selStart, selEnd)
        coreHost()?.let { instance -> coreSend(instance, ImeJson.focusEntered(fieldFacts()), moved = true) }
        coreInField = true
        screens.fieldStarted(policy.symbols)
        applyCurrentInsets()
    }

    override fun onFinishInputView(finishingInput: Boolean) {
        keyboardView?.cancelTouch()
        languagePopup.dismiss()
        emojiPanel?.cancelTouch()
        dropComposition()
        leaveField()
        super.onFinishInputView(finishingInput)
    }

    override fun onFinishInput() {
        keyboardView?.cancelTouch()
        emojiPanel?.cancelTouch()
        dropComposition()
        leaveField()
        super.onFinishInput()
    }

    /** Between onStartInputView and leaving it (both onFinishInputView and onFinishInput come). */
    private var coreInField = false

    /** The keyboard leaves the editor: the editor keeps the composing text as shown (focusLeft). */
    private fun leaveField() {
        if (!coreInField) return
        coreInField = false
        if (coreInput.composing) currentInputConnection?.finishComposingText()
        leaveCore(focusLeft = true)
    }

    override fun onUpdateSelection(
        oldSelStart: Int, oldSelEnd: Int, newSelStart: Int, newSelEnd: Int, candidatesStart: Int, candidatesEnd: Int,
    ) {
        super.onUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd)
        selStart = newSelStart
        selEnd = newSelEnd
        // An app-level send can clear the entire editor while leaving IME focus here.
        if (forgetCompositionIfEditorWasCleared()) return
        // Core の書き換えでないキャレットの移動は、Core に知らせる（変換中なら、出ている文字のまま確定させる）。
        if (!caretWatch.isOwn(newSelStart, newSelEnd, candidatesEnd, coreInput.composing)) caretMovedOutside()
    }

    /** Core owns composition; the view merely forgets stale candidate presentation. */
    private fun dropComposition() {
        candidateBar?.show(emptyList(), null)
    }

    override fun onKey(action: KeyAction, direction: Direction, timeMs: Long) {
        // Some editors do not deliver a selection callback when their Send button clears text.
        forgetCompositionIfEditorWasCleared()
        // 1つのキーで確定と入力の両方が起きることがある。入力欄には、まとめて1回の操作として届ける。
        val connection = currentInputConnection
        val start = System.nanoTime()
        connection?.beginBatchEdit()
        try {
            handleKey(action, direction, timeMs)
        } finally {
            connection?.endBatchEdit()
        }
        if (PerfLog.enabled) {
            // key: 指を離した（または繰り返しが起きた）ときから、入力欄への書き込みを呼び終えるまで。
            PerfLog.event("key", (SystemClock.uptimeMillis() - timeMs) * 1000)
            // キーの種類（kana、delete など）まで。どの文字かは出さない。
            PerfLog.event("key-handling:${perfKind(action)}", (System.nanoTime() - start) / 1000)
        }
    }

    private fun handleKey(action: KeyAction, direction: Direction, timeMs: Long) {
        // An error has already put the editor into emergency mode. Never keep typing into it
        // from queued touch events, and never silently fall back to raw/Kotlin input.
        if (nativeCoreUnavailable) return
        when (action) {
            is KeyAction.Text -> {
                val typed = if (nav.current == LayoutId.QWERTY) shift.apply(action.text) else action.text
                for (ch in typed) {
                    typeCharacter(ch.code)
                    if (core == null) return
                }
                if (nav.current == LayoutId.QWERTY) setShift(shift.onCharTyped())
            }
            is KeyAction.Romaji -> {
                val shifted = shift != ShiftState.Off
                typeCharacter((if (shifted) action.char.uppercaseChar() else action.char).code, shift = shifted)
                setShift(shift.onCharTyped())
            }
            KeyAction.Delete -> if (!coreVirtualKey(0x08) && core != null) {
                if (selStart != selEnd) sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL) else bridge.deleteBeforeCursor()
                caretWatch.lose()
            }
            KeyAction.Space -> typeCharacter(' '.code)
            KeyAction.Enter -> if (!coreVirtualKey(ImeJson.VK_RETURN) && core != null) {
                caretWatch.lose()
                val editorAction = enterAction(imeOptions)
                if (editorAction != null) currentInputConnection?.performEditorAction(editorAction)
                else sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER)
            }
            // 連打の間隔は Android の既定のダブルタップの間隔 (ふつう 300ms) に合わせる。
            KeyAction.Shift -> setShift(shift.onShiftPressed(timeMs, ViewConfiguration.getDoubleTapTimeout()))
            KeyAction.Language -> coreMode(ImeJson::modeToggleRequested)
            is KeyAction.Switch -> screens.switchTo(action.to)
            KeyAction.Emoji -> if (Settings(this).emojiEnabled) screens.openEmoji(timeMs)
            KeyAction.Back -> screens.back()
        }
    }

    /** 候補バーとキーを、絵文字の画面に入れ替える。戻るキー、空白、削除、確定は、いまの配列のものとして働く。 */
    private fun openEmojiPanel(timeMs: Long): Boolean {
        if (!Settings(this).emojiEnabled) return false
        val panel = emojiPanel ?: return false
        keyboardView?.cancelTouch()   // 絵文字キーから別のキーへ指を移していても、隠れるキーボードには入力させない
        // パスワードの欄では、覚えてある絵文字を見せない（spec 04・09）。
        panel.open(nav.current, enterKind(imeOptions), policy.showsRecentEmoji, timeMs)
        swapEmojiPanel(panel, open = true)
        return true
    }

    private fun closeEmojiPanel() {
        val panel = emojiPanel ?: return
        if (panel.visibility != View.VISIBLE) return
        panel.cancelTouch()
        swapEmojiPanel(panel, open = false)
    }

    private fun swapEmojiPanel(panel: EmojiPanelView, open: Boolean) {
        panel.visibility = if (open) View.VISIBLE else View.GONE
        candidateBar?.visibility = if (open) View.GONE else View.VISIBLE
        keyboardView?.visibility = if (open) View.GONE else View.VISIBLE
    }

    /** 絵文字の画面で選ばれた絵文字を入れる。「最近使ったもの」に足すかどうかは、入力欄の種類で決まる（spec 04・09）。 */
    private fun onEmojiPicked(emoji: String) {
        val connection = currentInputConnection
        connection?.beginBatchEdit()
        try {
            commitComposing()
            insert(emoji)
        } finally {
            connection?.endBatchEdit()
        }
        emojiRecents.used(emoji, policy.recordsEmoji)
    }

    /** 言語のメニューを出す。未確定文字があるあいだは出さない (空白キーの長押しで文節・カーソルを動かす)。 */
    override fun onSpaceLanguageMenu(x: Float, y: Float): Boolean {
        if (coreInput.composing) return false
        val anchor = keyboardView ?: return false
        if (nav.current != LayoutId.ROMAJI && nav.current != LayoutId.QWERTY) return false
        return languagePopup.show(
            anchor = anchor,
            touchX = x,
            touchY = y,
            selected = nav.current,
            allowEmoji = Settings(this).emojiEnabled,
            onSelectLanguage = { target ->
                coreMode(if (target == LayoutId.ROMAJI) ImeJson::japaneseModeSelected else ImeJson::alphanumericModeSelected)
            },
            onEmoji = { screens.openEmoji(SystemClock.uptimeMillis()) },
        )
    }

    override fun onSpaceLanguageMenuMove(x: Float, y: Float) {
        keyboardView?.let { languagePopup.move(it, x, y) }
    }

    override fun onSpaceLanguageMenuRelease(x: Float, y: Float) {
        keyboardView?.let { languagePopup.release(it, x, y) }
    }

    override fun onSpaceLanguageMenuCancel() {
        languagePopup.dismiss()
    }

    /** 変換中は、空白キーの長押しで動かすと、選ぶ文節を動かす（Core に ← → を送る）。 */
    override fun onCursorModeStart(): Boolean {
        cursorPosition = null
        return true
    }

    override fun onCursorMove(steps: Int) {
        if (coreInput.composing) {
            repeat(abs(steps)) { coreVirtualKey(if (steps < 0) ImeJson.VK_LEFT else ImeJson.VK_RIGHT) }
            return
        }
        // 最初の1回は、入力欄が知らせてきた位置から。範囲を選んであれば、動かす向きの端から。
        val from = cursorPosition ?: if (selStart < 0 || selEnd < 0) null else if (steps < 0) minOf(selStart, selEnd) else maxOf(selStart, selEnd)
        cursorPosition = bridge.moveCursor(steps, from)
    }

    private fun commitComposing() {
        commitCore()
    }

    /** 未確定文字を作らずに、そのまま入力する。 */
    private fun insert(text: String) {
        currentInputConnection?.commitText(text, 1)
    }

    private fun setShift(value: ShiftState) {
        if (shift == value) return
        shift = value
        keyboardView?.shift = value
    }

    /** いまの配列を出す。絵文字の画面が開いていれば閉じる。 */
    private fun showLayout() {
        nav.closeEmoji()
        closeEmojiPanel()
        renderKeys()
    }

    /** キーボードの配列を、いまのページと Core の言語モードに合わせる（絵文字の画面はそのまま）。 */
    private fun renderKeys() {
        val view = keyboardView ?: return
        view.hapticEnabled = Settings(this).hapticEnabled
        val baseLayout = Layouts.of(nav.current)
        val layout = if (Settings(this).emojiEnabled) baseLayout else
            noEmojiLayouts.getOrPut(nav.current) { baseLayout.withoutEmoji() }
        if (view.layout !== layout) view.layout = layout
        if (view.shift != shift) view.shift = shift
        val kind = enterKind(imeOptions)
        if (view.enterKind != kind) view.enterKind = kind
        if (view.backTarget != nav.letters) view.backTarget = nav.letters
    }

    companion object {
        @Volatile internal var activeForSmokeTest: WeakReference<KeyboardService>? = null
        private const val TAG = "KeyboardService"

        // The native Core keeps the first callbacks for the whole process (meltype_android_init), so they must not
        // hold a KeyboardService: Mozc is opened once per process, and results go to the service shown now.
        private var mozc: Lazy<MozcBackend?>? = null
        private var callbacks: CoreMozcCallbacks? = null

        /** The service whose keyboard shows the Core's conversion results. */
        @Volatile private var active: WeakReference<KeyboardService>? = null

        private fun mozc(context: Context): Lazy<MozcBackend?> =
            mozc ?: lazy { openMozc(context.applicationContext) }.also { mozc = it }

        private fun coreCallbacks(context: Context): CoreMozcCallbacks =
            callbacks ?: CoreMozcCallbacks(mozc(context)) {
                active?.get()?.let { service -> service.mainHandler.post(service::coreConversionFinished) }
            }.also { callbacks = it }

        /**
         * Mozc を開く。開けなければ null（変換できないまま、かなの入力だけで動き続ける）。
         * [worker] か Core の変換のスレッドで 1 回だけ呼ばれる。
         */
        private fun openMozc(context: Context): MozcBackend? = try {
            val start = System.nanoTime()
            MozcEngine.open(context)?.let { engine ->
                MozcConverter(engine).also { PerfLog.event("mozc-open", (System.nanoTime() - start) / 1000) }
            }
        } catch (t: Throwable) {
            // Avoid logging text from the user's input.
            Log.w(TAG, "mozc: ${t.javaClass.simpleName}")
            null
        }
    }
}
