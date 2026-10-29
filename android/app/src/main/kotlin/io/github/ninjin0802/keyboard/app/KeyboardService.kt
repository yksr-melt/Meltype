// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import android.content.SharedPreferences
import android.graphics.Color
import android.inputmethodservice.InputMethodService
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.KeyEvent
import android.view.View
import android.view.ViewTreeObserver
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.inputmethod.EditorInfo
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.view.Gravity
import java.io.File
import io.github.ninjin0802.keyboard.app.core.CoreInputAdapter
import io.github.ninjin0802.keyboard.app.core.CoreMozcCallbacks
import io.github.ninjin0802.keyboard.app.core.MeltypeCoreSession
import io.github.ninjin0802.keyboard.app.emoji.EmojiCatalog
import io.github.ninjin0802.keyboard.app.emoji.EmojiPanelView
import io.github.ninjin0802.keyboard.app.emoji.EmojiRecentsStore
import io.github.ninjin0802.keyboard.app.emoji.PrefsStore
import io.github.ninjin0802.keyboard.app.input.ArrowAction
import io.github.ninjin0802.keyboard.app.input.ComposingContext
import io.github.ninjin0802.keyboard.app.input.ConversionRunner
import io.github.ninjin0802.keyboard.app.input.Direction
import io.github.ninjin0802.keyboard.app.input.EditorBridge
import io.github.ninjin0802.keyboard.app.input.FieldPolicy
import io.github.ninjin0802.keyboard.app.input.RetryingOpener
import io.github.ninjin0802.keyboard.app.input.arrowAction
import io.github.ninjin0802.keyboard.app.input.ShiftState
import io.github.ninjin0802.keyboard.app.input.charAt
import io.github.ninjin0802.keyboard.app.input.enterAction
import io.github.ninjin0802.keyboard.app.input.enterKind
import io.github.ninjin0802.keyboard.app.input.fieldPolicy
import io.github.ninjin0802.keyboard.app.layout.KeyAction
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.layout.LayoutNavigator
import io.github.ninjin0802.keyboard.app.layout.Layouts
import io.github.ninjin0802.keyboard.app.layout.withoutEmoji
import io.github.ninjin0802.keyboard.app.layout.ScreenCoordinator
import io.github.ninjin0802.keyboard.app.view.BottomInsets
import io.github.ninjin0802.keyboard.app.view.CandidateBarView
import io.github.ninjin0802.keyboard.app.view.FlickPopupView
import io.github.ninjin0802.keyboard.app.view.KeyListener
import io.github.ninjin0802.keyboard.app.view.KeyboardView
import io.github.ninjin0802.keyboard.app.view.LanguagePickerPopup
import io.github.ninjin0802.keyboard.app.view.ImeGlassSurface
import io.github.ninjin0802.keyboard.app.view.KeyboardTopEdgeHighlight
import io.github.ninjin0802.keyboard.app.view.Palette
import io.github.ninjin0802.keyboard.app.view.bottomPaddingPx
import io.github.ninjin0802.keyboard.engine.Clause
import io.github.ninjin0802.keyboard.engine.Converter
import io.github.ninjin0802.keyboard.engine.InputSession
import io.github.ninjin0802.keyboard.engine.Kana
import io.github.ninjin0802.keyboard.engine.Latin
import io.github.ninjin0802.keyboard.engine.Operation
import io.github.ninjin0802.keyboard.engine.Segment
import io.github.ninjin0802.keyboard.engine.SessionConfig
import io.github.ninjin0802.keyboard.engine.Update
import io.github.ninjin0802.keyboard.mozc.MozcConverter
import io.github.ninjin0802.keyboard.mozc.MozcEngine
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit

/**
 * キーボード本体。キーの入力を入力セッションに渡し、返った指示を入力欄と候補バーに反映する。
 * 判断は InputSession・EditorBridge・ConversionRunner・fieldPolicy にあり、ここはつなぐだけ。
 *
 * 変換以外はすべて画面のスレッドで動く。
 */
class KeyboardService : InputMethodService(), KeyListener {
    /** 変換と学習を順に行う1本のスレッド。止めたあとに来た依頼は黙って捨てる。 */
    private val worker = ThreadPoolExecutor(
        1, 1, 0L, TimeUnit.MILLISECONDS, LinkedBlockingQueue(), ThreadPoolExecutor.DiscardPolicy(),
    )
    private val mainHandler = Handler(Looper.getMainLooper())
    private val converter = LateConverter(RetryingOpener(::openMozc, SystemClock::elapsedRealtime))
    private val runner = ConversionRunner(worker, { mainHandler.post(it) }, converter, ::logWorkerFailure)
    private val session = InputSession()
    private val bridge = EditorBridge { currentInputConnection }
    private val coreInput = CoreInputAdapter { currentInputConnection }
    private var core: MeltypeCoreSession? = null
    private var nativeCoreUnavailable = false

    /** Keep the same C# detection engine as Windows/Mac/Linux for QWERTY romaji. */
    private fun coreForRomaji(): MeltypeCoreSession? {
        val japaneseComposition =
            nav.current == LayoutId.ROMAJI ||
                ((nav.current == LayoutId.SYMBOLS || nav.current == LayoutId.SYMBOLS2) &&
                    nav.backTarget == LayoutId.ROMAJI)
        if (!policy.convert || !japaneseComposition || nativeCoreUnavailable) return null
        core?.let { return it }
        return try {
            MeltypeCoreSession.open(File(noBackupFilesDir, "meltype"), CoreMozcCallbacks(converter))
                .also {
                    core = it
                    it.setCanDelete(true)
                    it.setLiveConversion(Settings(this).liveConversionEnabled)
                }
        } catch (e: Exception) {
            nativeCoreUnavailable = true
            Log.w(TAG, "Native Core unavailable: ${e.javaClass.simpleName}")
            null
        } catch (e: LinkageError) {
            nativeCoreUnavailable = true
            Log.w(TAG, "Native Core library unavailable: ${e.javaClass.simpleName}")
            null
        }
    }

    private fun coreResult(payload: String?): Boolean {
        return try {
            coreInput.emojiEnabled = Settings(this).emojiEnabled
            val consumed = coreInput.apply(payload)
            candidateBar?.show(coreInput.candidates, coreInput.selected)
            consumed
        } catch (e: RuntimeException) {
            // InputMethodService must keep typing even when a native response fails.
            Log.w(TAG, "Native Core response rejected: ${e.javaClass.simpleName}")
            nativeCoreUnavailable = true
            discardCore()
            false
        }
    }

    private fun coreKey(vk: Int, scalar: Int = 0, modifiers: Int = 0): Boolean {
        val instance = coreForRomaji() ?: return false
        val before = currentInputConnection?.getTextBeforeCursor(20, 0)?.toString()
        val after = currentInputConnection?.getTextAfterCursor(20, 0)?.toString()
        return coreResult(instance.handleKey(vk, scalar, modifiers, before, after))
    }

    private fun commitCore() {
        val instance = core ?: return
        if (coreInput.composing) coreResult(instance.commit())
    }

    private fun discardCore() {
        core?.close()
        core = null
        coreInput.clear()
    }

    private val languagePopup by lazy { LanguagePickerPopup(this) }
    private var keyboardView: KeyboardView? = null
    private var candidateBar: CandidateBarView? = null
    private var flickPopup: FlickPopupView? = null
    private var emojiPanel: EmojiPanelView? = null
    private val emojiRecents by lazy { EmojiRecentsStore(PrefsStore(this, "emoji")) }

    private var policy = FieldPolicy(LayoutId.KANA, convert = true, learn = true)
    private var imeOptions = 0
    /** いまの配列と、あa・?123・絵文字・戻るキーの行き先。 */
    private val nav = LayoutNavigator()
    private val noEmojiLayouts = mutableMapOf<LayoutId, io.github.ninjin0802.keyboard.app.layout.KeyboardLayout>()

    /** 配列の切り替えと、絵文字の画面の開け閉め。どの順で確定して何を出すかは [ScreenCoordinator] が決める。 */
    private val screens = ScreenCoordinator(nav, object : ScreenCoordinator.Screen {
        override fun commitComposing() = this@KeyboardService.commitComposing()
        override fun showKeyboard() = showLayout()
        override fun releaseShift() = setShift(ShiftState.OFF)
        override fun showEmojiPanel(pressedAtMs: Long) = openEmojiPanel(pressedAtMs)
    })
    private var shift = ShiftState.OFF

    /** 入力欄が替わるたびに増やす。前の入力欄で頼んだ変換の結果を見分ける。 */
    private var fieldGeneration = 0
    private var selStart = 0
    private var selEnd = 0

    /** 空白キーの長押しで動かしているあいだの、カーソルの位置。入力欄からの知らせは遅れて届くので、自分で数える。 */
    private var cursorPosition: Int? = null

    /** 未確定文字の直前にある文章（変換と学習の手がかり）。いつ読むかは [ComposingContext] が決める。 */
    private val context = ComposingContext(CONTEXT_LENGTH)

    /** キーボードの画面の根と、その中の候補バーとキーの列。下の余白は列に付ける。 */
    private var inputRoot: View? = null
    private var keyColumn: View? = null

    private var createdAtNanos = 0L

    override fun onCreate() {
        super.onCreate()
        createdAtNanos = System.nanoTime()
        Settings(this).let {
            nav.choose(it.japaneseLayout, it.englishLayout)
            it.registerChangeListener(settingsListener)
        }
        // 辞書を写すので時間がかかる。変換と同じスレッドの先頭で行うので、変換はこれが終わってから動く。
        worker.execute { converter.open() }
    }

    /**
     * Mozc を開く。開けなければ null（変換できないまま、かなの入力だけで動き続ける）。
     * 変換のスレッドで呼ばれる。失敗したときのやり直しは [RetryingOpener] が決める。
     */
    private fun openMozc(): Converter? = try {
        val start = System.nanoTime()
        MozcEngine.open(applicationContext)?.let { engine ->
            MozcConverter(engine).also { PerfLog.event("mozc-open", (System.nanoTime() - start) / 1000) }
        }
    } catch (t: Throwable) {
        logWorkerFailure(null, t)
        null
    }

    // 設定が変わったら、開いているキーボードにもすぐ効かせる。弱い参照で持たれるので、フィールドで握る。
    private val settingsListener = SharedPreferences.OnSharedPreferenceChangeListener { _, changedKey ->
        val settings = Settings(this)
        // 配列を選び直して、いま出している配列が替わるときは、未確定文字を確定してから出し直す。
        screens.settingsChanged(settings.japaneseLayout, settings.englishLayout)
        session.config = session.config.copy(
            toggleEnabled = settings.toggleEnabled,
            toggleWaitMs = settings.toggleWaitMs.toLong(),
        )
        if (!settings.emojiEnabled) closeEmojiPanel()
        if (changedKey == Settings.KEY_LIVE_CONVERSION_ENABLED) {
            core?.setLiveConversion(settings.liveConversionEnabled)
        }
        if (core != null) {
            coreInput.emojiEnabled = settings.emojiEnabled
            candidateBar?.show(coreInput.candidates, coreInput.selected)
        }
        showLayout()
    }

    override fun onDestroy() {
        Settings(this).unregisterChangeListener(settingsListener)
        fieldGeneration++
        runner.cancelPending()
        languagePopup.dismiss()
        discardCore()
        worker.shutdown()   // 積んである学習は終わらせる
        super.onDestroy()
    }

    /** 変換のスレッドで起きた思いがけない失敗。出すのは、どの作業かと大まかな種類だけ。 */
    private fun logWorkerFailure(step: ConversionRunner.Step?, t: Throwable) {
        Log.w(TAG, "worker: ${workerFailureText(step, t)}")
    }

    /** 横向きでも入力欄を全画面にしない（アプリの画面を見ながら打てるようにする）。 */
    override fun onEvaluateFullscreenMode(): Boolean = false

    override fun onCreateInputView(): View {
        val start = System.nanoTime()
        val bar = CandidateBarView(this).also { it.onPick = { index ->
            if (core != null && nav.current == LayoutId.ROMAJI) {
                when (val choice = coreInput.choices.getOrNull(index)) {
                    null -> Unit
                    else -> when (choice.kind) {
                        io.github.ninjin0802.keyboard.app.core.CoreChoiceKind.PREDICTION ->
                            coreResult(core?.selectPrediction(choice.nativeIndex))
                        io.github.ninjin0802.keyboard.app.core.CoreChoiceKind.CONVERSION -> {
                            coreResult(core?.selectCandidate(choice.nativeIndex))
                            // Tapping a candidate commits it; the desktop UI
                            // normally waits for a separate Enter press.
                            coreKey(0x0D, 0x0D)
                        }
                        io.github.ninjin0802.keyboard.app.core.CoreChoiceKind.KANA_CORRECTION -> {
                            coreKey(0x75) // F6: convert original keystrokes to hiragana
                            coreKey(0x0D) // Enter: commit the corrected text
                        }
                        io.github.ninjin0802.keyboard.app.core.CoreChoiceKind.KATAKANA_CORRECTION -> {
                            coreKey(0x76) // F7: convert to katakana
                            coreKey(0x0D)
                        }
                        io.github.ninjin0802.keyboard.app.core.CoreChoiceKind.LATIN_CORRECTION -> {
                            coreKey(0x79) // F10: use the original roman letters
                            coreKey(0x0D)
                        }
                    }
                }
            } else render(session.handle(Operation.Pick(index)))
        } }
        // フリックの案内は、候補バーとキーの両方（と下の余白）に重ねた View に描く。キーの View の外にも出られる。
        val popup = FlickPopupView(this)
        val keys = KeyboardView(this).also { it.listener = this; it.popup = popup }
        // 絵文字の画面は、開いているあいだ、候補バーとキーの代わりに同じ場所に出る（高さは同じ）。
        val emoji = EmojiPanelView(this).also {
            it.keys.listener = this; it.recents = { emojiRecents.list }; it.onPick = ::onEmojiPicked; it.visibility = View.GONE
        }
        EmojiCatalog.load(this)   // 端末で描ける絵文字を、別のスレッドで調べておく
        candidateBar = bar
        keyboardView = keys
        flickPopup = popup
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
            addView(popup, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
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
        super.onStartInputView(info, restarting)
        // Also refresh this on every editor focus, not only layout changes.
        keyboardView?.hapticEnabled = Settings(this).hapticEnabled
        // 同じ入力欄のまま始め直されることがある（アプリが作り直されない画面の回転など）。そのとき入力欄には
        // 未確定文字が残っていて、何もしないと次の1文字がそれを置き換えて消してしまう。先に確定させる。
        // 新しい入力欄では、何も書き込まない。
        currentInputConnection?.finishComposingText()
        dropComposition()
        policy = fieldPolicy(info.inputType, info.imeOptions)
        imeOptions = info.imeOptions
        selStart = info.initialSelStart
        selEnd = info.initialSelEnd
        val settings = Settings(this)
        session.config = SessionConfig(
            toggleEnabled = settings.toggleEnabled,
            toggleWaitMs = settings.toggleWaitMs.toLong(),
            direct = !policy.convert,
            holdLast = policy.holdLast,
        )
        screens.fieldStarted(policy.layout)
        applyCurrentInsets()
    }

    override fun onFinishInputView(finishingInput: Boolean) {
        keyboardView?.cancelTouch()
        languagePopup.dismiss()
        emojiPanel?.cancelTouch()
        dropComposition()
        super.onFinishInputView(finishingInput)
    }

    override fun onFinishInput() {
        keyboardView?.cancelTouch()
        emojiPanel?.cancelTouch()
        dropComposition()
        super.onFinishInput()
    }

    override fun onUpdateSelection(
        oldSelStart: Int, oldSelEnd: Int, newSelStart: Int, newSelEnd: Int, candidatesStart: Int, candidatesEnd: Int,
    ) {
        super.onUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd)
        selStart = newSelStart
        selEnd = newSelEnd
        if (bridge.onSelectionChanged(newSelStart, newSelEnd, candidatesStart, candidatesEnd)) {
            // 利用者がカーソルを動かした、またはアプリが文章を書き換えた。出ている文字はそのまま確定させる。
            dropComposition()
            currentInputConnection?.finishComposingText()
        }
    }

    /** 未確定文字を、入力欄に何も送らずに捨てる。頼んである変換の結果も受け取らない。 */
    private fun dropComposition() {
        fieldGeneration++
        session.reset()
        runner.cancelPending()
        bridge.forget()
        context.reset()
        discardCore()
        candidateBar?.show(emptyList(), null)
    }

    override fun onKey(action: KeyAction, direction: Direction, timeMs: Long) {
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
        when (action) {
            is KeyAction.Flick -> render(
                session.handle(
                    if (direction == Direction.TAP) Operation.Tap(action.key, timeMs)
                    else Operation.Insert(action.key.charAt(direction) ?: return),   // 文字のない向き
                ),
            )
            is KeyAction.Text -> {
                val punctuation = action.text in setOf("。", "、", "！", "？", "!", "?")
                val upper = nav.current == LayoutId.QWERTY
                val typed = if (upper) shift.apply(action.text) else action.text
                if (punctuation) {
                    // Only punctuation or the explicit Return key can confirm.
                    commitComposing()
                    insert(typed)
                } else {
                    // A center numpad digit or symbol page input is a composing
                    // key, not a commitText() operation.
                    val native = coreForRomaji()
                    if (native != null) {
                        typed.forEach { ch ->
                            val vk = if (ch.code <= 0x7F) ch.uppercaseChar().code else 0
                            val consumed = coreKey(vk, ch.code)
                            if (!consumed) {
                                // A deliberately direct key still reaches the
                                // editor, without a preceding commit of Japanese.
                                insert(ch.toString())
                            }
                        }
                    } else {
                        insert(typed)
                    }
                }
                if (upper) setShift(shift.onCharTyped())
            }
            is KeyAction.Romaji -> {
                // Core runs exactly the desktop detection rules; fallback to the existing
                // Kotlin romaji path only if the native shared library is unavailable.
                val scalar = if (shift != ShiftState.OFF) action.char.uppercaseChar() else action.char
                val consumed = coreKey(action.char.uppercaseChar().code, scalar.code,
                    if (shift != ShiftState.OFF) 1 else 0)
                if (core != null) {
                    if (!consumed) insert(scalar.toString())
                } else if (shift != ShiftState.OFF && action.char.isLetter()) {
                    // ⇧ が効いているときは、ローマ字にしない。確定してから、大文字を1文字そのまま入れる。
                    commitComposing()
                    insert(shift.apply(action.char.toString()))
                } else {
                    render(session.handle(Operation.Romaji(action.char)))
                }
                setShift(shift.onCharTyped())
            }
            KeyAction.Modify -> replaceLast(Operation.Modify, Kana::modify)
            KeyAction.ToggleCase -> replaceLast(Operation.ToggleCase, Latin::toggleCase)
            KeyAction.Delete -> when {
                coreInput.composing && coreForRomaji() != null -> {
                    if (!coreKey(0x08)) bridge.deleteBeforeCursor()
                }
                session.isComposing -> render(session.handle(Operation.Delete))
                selStart != selEnd -> sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL)   // 選んである範囲を消す
                else -> bridge.deleteBeforeCursor()
            }
            KeyAction.Space -> {
                if (coreInput.composing && coreForRomaji() != null) {
                    if (!coreKey(0x20, ' '.code)) insert(" ")
                    return
                }
                val selecting = session.isComposing && !session.config.direct
                // 変換する欄では次の候補を選ぶ。変換しない欄では、持っている1文字を確定するだけ。
                render(session.handle(Operation.Space))
                if (!selecting) {
                    val full = nav.current.isJapanese && Settings(this).japaneseSpaceFullWidth
                    insert(if (full) "　" else " ")
                }
            }
            KeyAction.Enter -> if (coreInput.composing && coreForRomaji() != null) {
                if (!coreKey(0x0D)) {
                    val editorAction = enterAction(imeOptions)
                    if (editorAction != null) currentInputConnection?.performEditorAction(editorAction)
                    else sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER)
                }
            } else if (session.isComposing && !session.config.direct) {
                render(session.handle(Operation.Commit))
            } else {
                commitComposing()
                val editorAction = enterAction(imeOptions)
                if (editorAction != null) {
                    currentInputConnection?.performEditorAction(editorAction)
                } else {
                    sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER)
                }
            }
            KeyAction.Left, KeyAction.Right -> when (arrowAction(session.isComposing, session.config.direct)) {
                ArrowAction.MOVE_CURSOR ->
                    sendDownUpKeyEvents(if (action == KeyAction.Left) KeyEvent.KEYCODE_DPAD_LEFT else KeyEvent.KEYCODE_DPAD_RIGHT)
                ArrowAction.COMMIT_ONLY -> commitComposing()
                ArrowAction.SESSION -> if (action == KeyAction.Right) render(session.handle(Operation.Right))
            }
            KeyAction.Shift -> setShift(shift.onShiftPressed())
            KeyAction.Language -> screens.language()
            is KeyAction.Switch -> screens.switchTo(action.to)
            KeyAction.Undo -> render(session.handle(Operation.Undo(timeMs)))
            KeyAction.Emoji -> if (Settings(this).emojiEnabled) screens.openEmoji(timeMs)
            KeyAction.Back -> screens.back()
        }
    }

    /** ゛゜小 と a⇔A。持っている文字に効かせる。何も持たない欄（パスワード）では、入力欄にある直前の1文字を置き換える。 */
    private fun replaceLast(op: Operation, change: (Char) -> Char?) {
        if (session.isComposing || session.config.holdLast || !session.config.direct) {
            render(session.handle(op))
        } else if (selStart == selEnd) {
            bridge.modifyBeforeCursor(change)
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

    /** 未確定文字があるあいだは、空白キーは「次の候補」のまま。変換しない欄では、持っている1文字を確定してから動かす。 */
    override fun onSpaceLanguageMenu(x: Float, y: Float): Boolean {
        val anchor = keyboardView ?: return false
        if (nav.current != LayoutId.ROMAJI && nav.current != LayoutId.QWERTY) return false
        return languagePopup.show(
            anchor = anchor,
            touchX = x,
            touchY = y,
            selected = nav.current,
            allowEmoji = Settings(this).emojiEnabled,
            onSelectLanguage = { target ->
                if (nav.current != target) screens.language()
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

    override fun onCursorModeStart(): Boolean {
        if (coreInput.composing || (session.isComposing && !session.config.direct)) return false
        commitComposing()
        cursorPosition = null
        return true
    }

    override fun onCursorMove(steps: Int) {
        // 最初の1回は、入力欄が知らせてきた位置から。範囲を選んであれば、動かす向きの端から。
        val from = cursorPosition ?: if (selStart < 0 || selEnd < 0) null else if (steps < 0) minOf(selStart, selEnd) else maxOf(selStart, selEnd)
        cursorPosition = bridge.moveCursor(steps, from)
    }

    private fun commitComposing() {
        commitCore()
        if (session.isComposing) render(session.handle(Operation.Commit))
    }

    /** 未確定文字を作らずに、そのまま入力する。 */
    private fun insert(text: String) {
        bridge.apply(Update(commit = text))
    }

    private fun setShift(value: ShiftState) {
        if (shift == value) return
        shift = value
        keyboardView?.shift = value
    }

    /** いまの配列を出す。絵文字の画面が開いていれば閉じる。 */
    private fun showLayout() {
        closeEmojiPanel()
        // 英字フリック配列は、どの欄でも変換せず、最後の1文字だけを持つ（spec 04・09）。配列を替える前に、未確定文字は確定してある。
        val direct = !policy.convert || nav.current == LayoutId.LATIN_FLICK
        if (session.config.direct != direct) session.config = session.config.copy(direct = direct)
        val view = keyboardView ?: return
        view.hapticEnabled = Settings(this).hapticEnabled
        val baseLayout = Layouts.of(nav.current)
        val layout = if (Settings(this).emojiEnabled) baseLayout else
            noEmojiLayouts.getOrPut(nav.current) { baseLayout.withoutEmoji() }
        if (view.layout !== layout) view.layout = layout
        if (view.shift != shift) view.shift = shift
        val kind = enterKind(imeOptions)
        if (view.enterKind != kind) view.enterKind = kind
        if (view.backTarget != nav.backTarget) view.backTarget = nav.backTarget
        flickPopup?.showPreview = policy.flickPreview
    }

    /** engine の指示を、入力欄 → 候補バーの順に反映し、頼まれた変換と学習を出す。 */
    private fun render(update: Update) {
        // 未確定文字が始まるところなら、入力欄に出す前に、その直前の文章を読む。
        context.beforeApply(update, converts = !session.config.direct) { length ->
            currentInputConnection?.getTextBeforeCursor(length, 0)?.toString()
        }
        bridge.apply(update)
        val shown = if (Settings(this).emojiEnabled) update.candidates else
            update.candidates.filterNot {
                io.github.ninjin0802.keyboard.app.core.CoreCandidateChoices.hasEmoji(it.text)
            }
        val selected = update.selected?.let { chosen ->
            val choice = update.candidates.getOrNull(chosen)
            choice?.let(shown::indexOf)?.takeIf { it >= 0 }
        }
        candidateBar?.show(shown, selected)

        val learn = update.learn
        if (learn != null && policy.learn) runner.learn(learn, context.text)
        context.afterApply(update)

        val request = update.request ?: return
        val field = fieldGeneration
        runner.submit(request, context.text) { result ->
            // 読みが変わっていれば onConversion が null を返す。入力欄が替わっていればここで捨てる。
            if (field != fieldGeneration) return@submit
            val start = System.nanoTime()
            val update = session.onConversion(result) ?: return@submit
            render(update)
            PerfLog.event("result-handling", (System.nanoTime() - start) / 1000, count = update.candidates.size)
        }
    }

    /**
     * Mozc の用意ができるまでは、変換に失敗したものとして答える。用意に失敗していたら、
     * 変換や学習が頼まれたときに [mozc] がやり直す。変換のスレッドでだけ使う。
     */
    private class LateConverter(private val mozc: RetryingOpener<Converter>) : Converter {
        fun open() { mozc.get() }

        override fun convert(reading: String, context: String?): List<Segment>? =
            timed("convert", reading.length) { mozc.get()?.convert(reading, context) }

        override fun predict(reading: String): List<String> =
            timed("predict", reading.length) { mozc.get()?.predict(reading) } ?: emptyList()

        private inline fun <T> timed(kind: String, length: Int, block: () -> T): T {
            val start = System.nanoTime()
            return block().also { PerfLog.event(kind, (System.nanoTime() - start) / 1000, length) }
        }
        override fun learn(clauses: List<Clause>, context: String?) { mozc.get()?.learn(clauses, context) }
    }

    private companion object {
        const val TAG = "KeyboardService"
        const val CONTEXT_LENGTH = 20
    }
}
