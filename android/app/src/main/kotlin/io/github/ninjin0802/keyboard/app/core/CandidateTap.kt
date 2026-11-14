// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 KoheiTakada
package io.github.ninjin0802.keyboard.app.core

/**
 * What a tap on the candidate bar sends to the Core, made only of existing events
 * (CandidateClicked and the Enter / → keys).
 *
 * - A prediction, or a conversion of one clause: the tap commits (CandidateClicked, then Enter
 *   if composing text is left).
 * - A conversion of two or more clauses: the tap chooses that clause's candidate and moves on to
 *   the next clause (→), whose candidates the bar then shows, so that every clause can be fixed
 *   from the first one on. A tap on the last clause commits the whole text (Enter). A clause
 *   without candidates to choose from is passed over.
 */
internal object CandidateTap {
    /**
     * Taps the Core's candidate [nativeIndex]. [send] sends one event to the Core and applies the
     * response to [adapter]; [field] gives the editor's facts for each event.
     */
    fun tap(adapter: CoreInputAdapter, nativeIndex: Int, field: () -> ImeFieldFacts, send: (String) -> Unit) {
        val clauseByClause = adapter.view.let { it != null && it.converting && it.clauses.size > 1 }
        send(ImeJson.candidateClicked(nativeIndex, field()))
        if (!adapter.composing) return
        if (!clauseByClause) {
            send(ImeJson.virtualKey(ImeJson.VK_RETURN, field()))
            return
        }
        var view = adapter.view
        while (view != null && view.converting && view.selectedClause < view.clauses.size - 1) {
            val from = view.selectedClause
            send(ImeJson.virtualKey(ImeJson.VK_RIGHT, field()))
            view = adapter.view
            // → did not move on (not expected): commit, as a tap did before.
            if (view == null || !view.converting || view.selectedClause <= from) break
            if (adapter.choices.isNotEmpty()) return
        }
        if (adapter.composing) send(ImeJson.virtualKey(ImeJson.VK_RETURN, field()))
    }
}
