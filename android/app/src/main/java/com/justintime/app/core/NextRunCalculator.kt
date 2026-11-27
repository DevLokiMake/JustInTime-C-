package com.justintime.app.core

import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZonedDateTime

/**
 * Расчёт ближайшего запуска по времени суток. Чистая логика без Android, проверяется обычными тестами.
 *
 * Переход на летнее время: если локального времени не существует (пропущенный час), берётся время сразу после
 * пропуска; если оно встречается дважды, берётся первое. Это правила [ZonedDateTime.of].
 */
object NextRunCalculator {
    /** Ближайший момент строго позже [now], когда на часах [zone] будет [timeOfDayMinutes]. */
    fun nextOccurrence(now: Instant, zone: ZoneId, timeOfDayMinutes: Int): Instant {
        require(timeOfDayMinutes in 0 until MINUTES_PER_DAY) { "timeOfDayMinutes вне диапазона: $timeOfDayMinutes" }

        val today = ZonedDateTime.ofInstant(now, zone).toLocalDate()
        val todayAt = at(today, zone, timeOfDayMinutes)

        return if (todayAt.isAfter(now)) todayAt else at(today.plusDays(1), zone, timeOfDayMinutes)
    }

    private fun at(date: LocalDate, zone: ZoneId, timeOfDayMinutes: Int): Instant =
        ZonedDateTime.of(date, LocalTime.of(timeOfDayMinutes / 60, timeOfDayMinutes % 60), zone).toInstant()

    const val MINUTES_PER_DAY = 24 * 60
}
