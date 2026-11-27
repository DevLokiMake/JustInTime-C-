package com.justintime.app.core

import com.justintime.app.data.TaskDao
import java.time.Clock
import java.time.Instant
import java.time.ZoneId
import java.util.UUID

/** Итог операции для интерфейса: warning не отменяет успех, но о нём нужно сказать пользователю. */
data class ServiceResult(val ok: Boolean, val warning: String? = null, val error: String? = null)

/** Создание, отмена и удаление задач. */
class TaskService(
    private val tasks: TaskDao,
    private val alarms: AlarmPort,
    private val texts: Texts,
    private val clock: Clock,
    private val currentZone: () -> ZoneId = { ZoneId.systemDefault() },
) {
    suspend fun create(timeOfDayMinutes: Int, enable: Boolean, repeat: Repeat, title: String? = null): ServiceResult {
        if (timeOfDayMinutes !in 0 until NextRunCalculator.MINUTES_PER_DAY) {
            return ServiceResult(ok = false, error = "time")
        }

        val now = clock.millis()
        val zone = currentZone()
        val next = NextRunCalculator.nextOccurrence(Instant.ofEpochMilli(now), zone, timeOfDayMinutes)

        val task = ScheduledTask(
            id = UUID.randomUUID().toString(),
            title = title?.trim().takeUnless { it.isNullOrEmpty() } ?: texts.defaultTitle(enable, timeOfDayMinutes),
            action = ActionType.BLUETOOTH,
            enable = enable,
            timeOfDayMinutes = timeOfDayMinutes,
            zoneId = zone.id,
            repeat = repeat,
            scheduledAtMillis = next.toEpochMilli(),
            status = TaskStatus.SCHEDULED,
            createdAtMillis = now,
            updatedAtMillis = now,
        )

        tasks.insert(task)

        // Задача уже сохранена: даже если будильник не поставить, она восстановится при следующем запуске приложения.
        val kind = alarms.schedule(task.id, task.scheduledAtMillis)

        return if (kind == AlarmKind.EXACT) ServiceResult(ok = true) else ServiceResult(ok = true, warning = texts.inexactAlarm())
    }

    suspend fun cancel(id: String): Boolean {
        val cancelled = tasks.tryCancel(id, clock.millis()) > 0

        if (cancelled) {
            alarms.cancel(id)
        }

        return cancelled
    }

    suspend fun delete(id: String): Boolean = tasks.deleteFinished(id) > 0
}
