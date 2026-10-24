// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import android.app.Activity
import android.content.ComponentName
import android.content.Intent
import android.graphics.Typeface
import android.os.Bundle
import android.provider.Settings as SystemSettings
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.Button
import android.widget.CompoundButton
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Switch
import android.widget.TextView
import io.github.ninjin0802.keyboard.app.layout.LayoutId
import io.github.ninjin0802.keyboard.app.view.Palette

/** 起動したときの画面。導入の手順、試し書き、設定、ライセンスへのリンクを縦に並べる。 */
class MainActivity : Activity() {
    private lateinit var palette: Palette
    private lateinit var settings: Settings
    private lateinit var enableButton: Button
    private lateinit var selectButton: Button
    private lateinit var enableDone: TextView
    private lateinit var selectDone: TextView
    private lateinit var waitSeek: SeekBar
    private lateinit var waitLabel: TextView

    private val dp by lazy { resources.displayMetrics.density }
    private fun px(v: Int) = (v * dp).toInt()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        palette = Palette.of(this)
        settings = Settings(this)

        val column = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(px(20), px(24), px(20), px(24))
        }
        column.addView(title(getString(R.string.app_name), 28))

        column.addView(heading(getString(R.string.step_enable_title)))
        enableDone = label(getString(R.string.step_done), palette.accent)
        enableButton = button(getString(R.string.step_enable_button)) {
            startActivity(Intent(SystemSettings.ACTION_INPUT_METHOD_SETTINGS))
        }
        column.addView(enableDone)
        column.addView(enableButton)

        column.addView(heading(getString(R.string.step_select_title)))
        selectDone = label(getString(R.string.step_done), palette.accent)
        selectButton = button(getString(R.string.step_select_button)) {
            (getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager).showInputMethodPicker()
        }
        column.addView(selectDone)
        column.addView(selectButton)

        column.addView(heading(getString(R.string.try_title)))
        column.addView(EditText(this).apply {
            id = R.id.try_field   // ID があると、画面が作り直されても打った文字が残る
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE
            minLines = 4
            gravity = Gravity.TOP or Gravity.START
            hint = getString(R.string.try_hint)
            setTextColor(palette.text)
            setHintTextColor(palette.subText)
            setBackgroundColor(palette.key)
            setPadding(px(12), px(12), px(12), px(12))
        })

        column.addView(heading(getString(R.string.settings_title)))
        val toggleSwitch = Switch(this).apply {
            text = getString(R.string.toggle_input)
            textSize = 16f
            setTextColor(palette.text)
            isChecked = settings.toggleEnabled
        }
        column.addView(toggleSwitch)
        column.addView(label(getString(R.string.toggle_wait), palette.text).apply { setPadding(0, px(12), 0, 0) })
        waitLabel = label("", palette.subText)
        waitSeek = SeekBar(this).apply {
            max = WAIT_STEPS - 1
            progress = progressOf(settings.toggleWaitMs)
            isEnabled = settings.toggleEnabled
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(bar: SeekBar, value: Int, fromUser: Boolean) {
                    waitLabel.text = waitText(waitOfProgress(value))
                    if (fromUser) settings.toggleWaitMs = waitOfProgress(value)
                }
                override fun onStartTrackingTouch(bar: SeekBar) {}
                override fun onStopTrackingTouch(bar: SeekBar) {}
            })
        }
        waitLabel.text = waitText(settings.toggleWaitMs)
        toggleSwitch.setOnCheckedChangeListener { _: CompoundButton, on: Boolean ->
            settings.toggleEnabled = on
            waitSeek.isEnabled = on
        }
        column.addView(waitSeek)
        column.addView(waitLabel)

        column.addView(label(getString(R.string.japanese_input), palette.text).apply { setPadding(0, px(16), 0, 0) })
        column.addView(
            choice(
                listOf(LayoutId.KANA to R.string.japanese_input_flick, LayoutId.ROMAJI to R.string.japanese_input_romaji),
                settings.japaneseLayout,
            ) { settings.japaneseLayout = it },
        )
        column.addView(label(getString(R.string.english_input), palette.text).apply { setPadding(0, px(12), 0, 0) })
        column.addView(
            choice(
                listOf(LayoutId.QWERTY to R.string.english_input_qwerty, LayoutId.LATIN_FLICK to R.string.english_input_flick),
                settings.englishLayout,
            ) { settings.englishLayout = it },
        )

        column.addView(TextView(this).apply {
            text = getString(R.string.licenses_link)
            textSize = 16f
            setTextColor(palette.accent)
            setPadding(0, px(28), 0, px(12))
            setOnClickListener { startActivity(Intent(this@MainActivity, LicensesActivity::class.java)) }
        })

        setContentView(ScrollView(this).apply {
            id = R.id.setup_scroll
            setBackgroundColor(palette.background)
            isFillViewport = true
            addView(column)
            padForSystemBars()
        })
        window.decorView.setBackgroundColor(palette.background)
    }

    override fun onResume() {
        super.onResume()
        refreshSteps()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) refreshSteps()
    }

    private fun refreshSteps() {
        val imm = getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager
        val ourId = ComponentName(this, KeyboardService::class.java).flattenToShortString()
        val enabled = imm.enabledInputMethodList.map { it.id }
        val selected = SystemSettings.Secure.getString(contentResolver, SystemSettings.Secure.DEFAULT_INPUT_METHOD)
        val step = setupStep(enabled, selected, ourId)
        enableDone.visibility = if (step == SetupStep.ENABLE) View.GONE else View.VISIBLE
        selectDone.visibility = if (step == SetupStep.DONE) View.VISIBLE else View.GONE
        paint(enableButton, step == SetupStep.ENABLE)
        paint(selectButton, step == SetupStep.SELECT)
    }

    private fun paint(button: Button, current: Boolean) {
        button.setBackgroundColor(if (current) palette.accent else palette.functionKey)
        button.setTextColor(if (current) palette.onAccent else palette.text)
    }

    private fun title(text: String, size: Int) = TextView(this).apply {
        this.text = text; textSize = size.toFloat(); setTextColor(palette.text); typeface = Typeface.DEFAULT_BOLD
    }

    private fun heading(text: String) = TextView(this).apply {
        this.text = text; textSize = 18f; setTextColor(palette.text); typeface = Typeface.DEFAULT_BOLD
        setPadding(0, px(24), 0, px(6))
    }

    private fun label(text: String, color: Int) = TextView(this).apply {
        this.text = text; textSize = 15f; setTextColor(color)
    }

    /** 配列を1つ選ぶ項目。[options] は配列と、その名前の文字列。選ぶと、すぐ [onChosen] に渡す。 */
    private fun choice(options: List<Pair<LayoutId, Int>>, selected: LayoutId, onChosen: (LayoutId) -> Unit) = RadioGroup(this).apply {
        orientation = RadioGroup.VERTICAL
        val ids = options.map { (layout, name) ->
            val option = RadioButton(this@MainActivity).apply {
                id = View.generateViewId()
                text = getString(name)
                textSize = 16f
                setTextColor(palette.text)
                isSaveEnabled = false     // 選んである値は設定から読む（ID は画面を作るたびに変わる）
            }
            addView(option)
            option.id to layout
        }
        check(ids.first { it.second == selected }.first)
        setOnCheckedChangeListener { _, checkedId -> ids.firstOrNull { it.first == checkedId }?.let { onChosen(it.second) } }
    }

    private fun button(text: String, onClick: () -> Unit) = Button(this).apply {
        this.text = text
        isAllCaps = false
        setOnClickListener { onClick() }
    }

    companion object {
        private const val WAIT_STEPS = 18
        private const val WAIT_BASE_MS = 300
        private const val WAIT_STEP_MS = 100

        fun waitOfProgress(progress: Int) = WAIT_BASE_MS + progress.coerceIn(0, WAIT_STEPS - 1) * WAIT_STEP_MS
        fun progressOf(waitMs: Int) = ((waitMs - WAIT_BASE_MS) / WAIT_STEP_MS).coerceIn(0, WAIT_STEPS - 1)
        fun waitText(waitMs: Int) = String.format(java.util.Locale.US, "%.1f 秒", waitMs / 1000.0)
    }
}
