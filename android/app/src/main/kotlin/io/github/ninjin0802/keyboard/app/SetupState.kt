// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

enum class SetupStep { ENABLE, SELECT, DONE }

/** 導入の手順のうち、今やるもの。 */
fun setupStep(enabledImeIds: List<String>, selectedImeId: String?, ourId: String): SetupStep = when {
    ourId !in enabledImeIds -> SetupStep.ENABLE
    selectedImeId != ourId -> SetupStep.SELECT
    else -> SetupStep.DONE
}
