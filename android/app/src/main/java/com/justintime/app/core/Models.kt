package com.justintime.app.core

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/** Какое действие выполняет задача. В версии 0.1 только Bluetooth. */
enum class ActionType { BLUETOOTH }

enum class Repeat { NONE, DAILY }

/** Статусы те же, что в Windows-версии (см. раздел 5 инструкции). */
enum class TaskStatus {
    PENDING,
    SCHEDULED,
    RUNNING,
    WAITING_FOR_UNLOCK,
    WAITING_FOR_NETWORK,
    NEEDS_USER_ACTION,
    COMPLETED,
    FAILED,
    CANCELLED,
}

enum class RunResult { SUCCESS, FAILED, SKIPPED }

/**
 * Задача. Время задаётся как время суток по часам устройства: «в 03:00». Следующий запуск (scheduledAtMillis)
 * вычисляется из него и часового пояса, поэтому смена пояса и переход на летнее время не сдвигают задачу.
 */
@Entity(tableName = "tasks")
data class ScheduledTask(
    @PrimaryKey val id: String,
    val title: String,
    val action: ActionType,
    /** Целевое состояние: true включить, false выключить. */
    val enable: Boolean,
    val timeOfDayMinutes: Int,
    val zoneId: String,
    val repeat: Repeat,
    val scheduledAtMillis: Long,
    val status: TaskStatus,
    val attemptCount: Int = 0,
    val lastError: String? = null,
    val lastRunAtMillis: Long? = null,
    val createdAtMillis: Long,
    val updatedAtMillis: Long,
)

/** Запись журнала. Не связана с задачей внешним ключом: журнал остаётся, даже если задачу удалили. */
@Entity(tableName = "history", indices = [Index("taskId")])
data class HistoryEntry(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val taskId: String,
    val taskTitle: String,
    val startedAtMillis: Long,
    val finishedAtMillis: Long? = null,
    val result: RunResult? = null,
    val message: String? = null,
    val attempt: Int = 1,
)

object TaskLimits {
    /** Насколько позже срока ещё можно выполнить задачу. Позже задача считается пропущенной. */
    const val GRACE_MILLIS = 30L * 60 * 1000

    /** Будильник может сработать чуть раньше расчётного времени. */
    const val EARLY_SLACK_MILLIS = 60L * 1000

    /** Задача в RUNNING дольше этого времени считается прерванной (процесс убили посреди действия). */
    const val STUCK_MILLIS = 3L * 60 * 1000
}
