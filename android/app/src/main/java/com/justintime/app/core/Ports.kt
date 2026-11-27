package com.justintime.app.core

/** Итог попытки изменить состояние Bluetooth. */
sealed interface BtOutcome {
    /** Нужное состояние подтверждено повторным чтением. alreadyInState: оно уже было таким. */
    data class Done(val alreadyInState: Boolean) : BtOutcome

    data object NoAdapter : BtOutcome

    data object NoPermission : BtOutcome

    /** Система отказала (disable()/enable() вернули false). */
    data object Refused : BtOutcome

    /** Команда принята, но за отведённое время состояние не стало нужным. */
    data class NotConfirmed(val state: String) : BtOutcome
}

interface BluetoothPort {
    suspend fun set(enable: Boolean): BtOutcome
}

enum class AlarmKind { EXACT, INEXACT }

interface AlarmPort {
    fun canScheduleExact(): Boolean

    /** Ставит будильник. Без разрешения на точные будильники ставит неточный и возвращает INEXACT. */
    fun schedule(taskId: String, atMillis: Long): AlarmKind

    fun cancel(taskId: String)
}

interface NotifyPort {
    fun problem(title: String, text: String)
}

/** Тексты для журнала и ошибок. Вынесены в интерфейс, чтобы логика не зависела от Android-ресурсов. */
interface Texts {
    fun defaultTitle(enable: Boolean, timeOfDayMinutes: Int): String

    fun btDone(enable: Boolean): String

    fun btAlready(enable: Boolean): String

    fun btNoAdapter(): String

    fun btNoPermission(): String

    fun btRefused(): String

    fun btNotConfirmed(state: String): String

    fun missed(lateMinutes: Long, graceMinutes: Long): String

    fun interrupted(): String

    fun unexpected(message: String?): String

    fun inexactAlarm(): String
}
