package com.justintime.app.data

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import com.justintime.app.core.HistoryEntry
import com.justintime.app.core.ScheduledTask

/**
 * Локальная база задач и журнала. Деструктивных миграций нет намеренно: при смене структуры нужна настоящая
 * миграция, иначе приложение не стартует, но пользовательские задачи не будут молча удалены.
 */
@Database(entities = [ScheduledTask::class, HistoryEntry::class], version = 1, exportSchema = false)
abstract class AppDatabase : RoomDatabase() {
    abstract fun tasks(): TaskDao

    abstract fun history(): HistoryDao

    companion object {
        fun create(context: Context): AppDatabase =
            Room.databaseBuilder(context.applicationContext, AppDatabase::class.java, "justintime.db").build()
    }
}
