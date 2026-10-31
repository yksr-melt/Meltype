// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
package io.github.ninjin0802.keyboard.app

import kotlin.test.Test
import kotlin.test.assertEquals

class SetupStateTest {
    private val us = "io.github.ninjin0802.keyboard/.app.KeyboardService"
    @Test fun not_enabled() = assertEquals(SetupStep.ENABLE, setupStep(listOf("other/.Ime"), "other/.Ime", us))
    @Test fun enabled_not_selected() = assertEquals(SetupStep.SELECT, setupStep(listOf("other/.Ime", us), "other/.Ime", us))
    @Test fun selected() = assertEquals(SetupStep.DONE, setupStep(listOf(us), us, us))
    @Test fun nothing_selected() = assertEquals(SetupStep.SELECT, setupStep(listOf(us), null, us))
}
