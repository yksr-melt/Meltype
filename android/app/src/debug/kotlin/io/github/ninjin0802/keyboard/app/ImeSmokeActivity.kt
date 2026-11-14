// SPDX-License-Identifier: GPL-3.0-or-later
package io.github.ninjin0802.keyboard.app

import android.app.Activity
import android.os.Bundle
import android.text.InputType
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.LinearLayout

/** Debug-only editor used to verify a selected IME can actually type. */
class ImeSmokeActivity : Activity() {
    lateinit var editor: EditText
        private set
    lateinit var japaneseEditor: EditText
        private set

    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        editor = EditText(this).apply {
            id = android.R.id.edit
            hint = "IME smoke input"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS
            setSingleLine(true)
            isFocusableInTouchMode = true
            textSize = 20f
        }
        japaneseEditor = EditText(this).apply {
            hint = "Japanese input smoke"
            inputType = InputType.TYPE_CLASS_TEXT
            setSingleLine(true)
            isFocusableInTouchMode = true
            textSize = 20f
        }
        setContentView(LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(24, 80, 24, 24)
            addView(editor, LinearLayout.LayoutParams(-1, -2))
            addView(japaneseEditor, LinearLayout.LayoutParams(-1, -2))
        })
        window.setSoftInputMode(android.view.WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_VISIBLE)
        editor.requestFocus()
        editor.post {
            (getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager)
                .showSoftInput(editor, InputMethodManager.SHOW_IMPLICIT)
        }
    }
}
