package com.justintime.app.system

import android.content.Context
import com.justintime.app.R
import com.justintime.app.core.Texts

/** Тексты на языке устройства из ресурсов (values и values-ru). Уже записанные в журнал остаются на прежнем языке. */
class AndroidTexts(private val context: Context) : Texts {
    override fun defaultTitle(enable: Boolean, timeOfDayMinutes: Int): String =
        context.getString(
            if (enable) R.string.title_bt_on else R.string.title_bt_off,
            formatTime(timeOfDayMinutes),
        )

    override fun btDone(enable: Boolean): String =
        context.getString(if (enable) R.string.bt_done_on else R.string.bt_done_off)

    override fun btAlready(enable: Boolean): String =
        context.getString(if (enable) R.string.bt_already_on else R.string.bt_already_off)

    override fun btNoAdapter(): String = context.getString(R.string.bt_no_adapter)

    override fun btNoPermission(): String = context.getString(R.string.bt_no_permission)

    override fun btRefused(): String = context.getString(R.string.bt_refused)

    override fun btNotConfirmed(state: String): String = context.getString(R.string.bt_not_confirmed, state)

    override fun missed(lateMinutes: Long, graceMinutes: Long): String =
        context.getString(R.string.run_missed, lateMinutes, graceMinutes)

    override fun interrupted(): String = context.getString(R.string.run_interrupted)

    override fun unexpected(message: String?): String = context.getString(R.string.run_unexpected, message ?: "")

    override fun inexactAlarm(): String = context.getString(R.string.warn_inexact_alarm)

    companion object {
        fun formatTime(minutes: Int): String = String.format(java.util.Locale.ROOT, "%02d:%02d", minutes / 60, minutes % 60)
    }
}
