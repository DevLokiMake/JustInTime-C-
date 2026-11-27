package com.justintime.app.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import com.justintime.app.core.HistoryEntry
import com.justintime.app.core.ScheduledTask
import kotlinx.coroutines.flow.Flow

/**
 * Операции с условием в самом запросе атомарны на уровне SQLite: два одновременных вызова не могут оба «выиграть».
 * Статусы в SQL записаны литералами, это имена значений [com.justintime.app.core.TaskStatus].
 */
@Dao
interface TaskDao {
    @Insert
    suspend fun insert(task: ScheduledTask)

    @Query("SELECT * FROM tasks ORDER BY scheduledAtMillis ASC")
    fun observeAll(): Flow<List<ScheduledTask>>

    @Query("SELECT * FROM tasks WHERE id = :id")
    suspend fun get(id: String): ScheduledTask?

    @Query("SELECT * FROM tasks WHERE status IN ('PENDING', 'SCHEDULED')")
    suspend fun getStartable(): List<ScheduledTask>

    @Query("SELECT * FROM tasks WHERE status = 'RUNNING'")
    suspend fun getRunning(): List<ScheduledTask>

    /** Берёт задачу в работу: ждущая и срок наступил (с допуском). Возвращает 1, если задачу взяли именно мы. */
    @Query(
        "UPDATE tasks SET status = 'RUNNING', attemptCount = attemptCount + 1, updatedAtMillis = :now " +
            "WHERE id = :id AND status IN ('PENDING', 'SCHEDULED') AND scheduledAtMillis <= :deadline"
    )
    suspend fun tryClaim(id: String, now: Long, deadline: Long): Int

    /** Завершает попытку: только из RUNNING. */
    @Query(
        "UPDATE tasks SET status = :status, lastError = :error, lastRunAtMillis = :lastRun, " +
            "scheduledAtMillis = :scheduledAt, updatedAtMillis = :now WHERE id = :id AND status = 'RUNNING'"
    )
    suspend fun finishRun(id: String, status: String, error: String?, lastRun: Long, scheduledAt: Long, now: Long): Int

    @Query(
        "UPDATE tasks SET status = 'CANCELLED', updatedAtMillis = :now WHERE id = :id AND status IN " +
            "('PENDING', 'SCHEDULED', 'WAITING_FOR_UNLOCK', 'WAITING_FOR_NETWORK', 'NEEDS_USER_ACTION')"
    )
    suspend fun tryCancel(id: String, now: Long): Int

    @Query(
        "UPDATE tasks SET scheduledAtMillis = :scheduledAt, zoneId = :zoneId, updatedAtMillis = :now " +
            "WHERE id = :id AND status IN ('PENDING', 'SCHEDULED')"
    )
    suspend fun reschedule(id: String, scheduledAt: Long, zoneId: String, now: Long): Int

    @Query("DELETE FROM tasks WHERE id = :id AND status IN ('COMPLETED', 'FAILED', 'CANCELLED')")
    suspend fun deleteFinished(id: String): Int
}

@Dao
interface HistoryDao {
    @Insert
    suspend fun insert(entry: HistoryEntry): Long

    @Query("UPDATE history SET finishedAtMillis = :finishedAt, result = :result, message = :message WHERE id = :id")
    suspend fun finish(id: Long, finishedAt: Long, result: String, message: String?): Int

    /** Закрывает незавершённые попытки задачи (после сбоя процесса). */
    @Query(
        "UPDATE history SET finishedAtMillis = :finishedAt, result = :result, message = :message " +
            "WHERE taskId = :taskId AND finishedAtMillis IS NULL"
    )
    suspend fun closeOpen(taskId: String, finishedAt: Long, result: String, message: String?): Int

    @Query("SELECT * FROM history ORDER BY startedAtMillis DESC LIMIT :limit")
    fun observeRecent(limit: Int): Flow<List<HistoryEntry>>
}
