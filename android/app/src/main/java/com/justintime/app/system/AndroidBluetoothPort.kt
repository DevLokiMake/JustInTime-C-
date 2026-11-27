package com.justintime.app.system

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import com.justintime.app.core.BluetoothPort
import com.justintime.app.core.BtOutcome
import kotlinx.coroutines.delay
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Включает и выключает Bluetooth через BluetoothAdapter.enable()/disable().
 *
 * Важно: с Android 13 (API 33) приложения, нацеленные на API 33 и выше, не могут этого делать, вызов всегда
 * возвращает false. Поэтому у приложения targetSdk = 32 (см. app/build.gradle.kts и docs/android.md).
 * Успехом считается только подтверждённое состояние адаптера, а не то, что команда принята.
 */
@SuppressLint("MissingPermission") // BLUETOOTH_CONNECT проверяется вручную в hasPermission()
class AndroidBluetoothPort(
    private val context: Context,
    private val confirmTimeoutMillis: Long = 6_000,
    private val pollMillis: Long = 200,
) : BluetoothPort {

    fun hasPermission(): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
            context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED

    override suspend fun set(enable: Boolean): BtOutcome {
        val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter ?: return BtOutcome.NoAdapter

        if (!hasPermission()) {
            return BtOutcome.NoPermission
        }

        val target = if (enable) BluetoothAdapter.STATE_ON else BluetoothAdapter.STATE_OFF

        if (adapter.state == target) {
            return BtOutcome.Done(alreadyInState = true)
        }

        @Suppress("DEPRECATION")
        val accepted = if (enable) adapter.enable() else adapter.disable()

        if (!accepted) {
            return BtOutcome.Refused
        }

        // Команда асинхронная: ждём, пока адаптер действительно перейдёт в нужное состояние.
        val reached = withTimeoutOrNull(confirmTimeoutMillis) {
            while (adapter.state != target) {
                delay(pollMillis)
            }
            true
        }

        return if (reached == true) BtOutcome.Done(alreadyInState = false) else BtOutcome.NotConfirmed(stateName(adapter.state))
    }

    private fun stateName(state: Int): String = when (state) {
        BluetoothAdapter.STATE_OFF -> "OFF"
        BluetoothAdapter.STATE_TURNING_ON -> "TURNING_ON"
        BluetoothAdapter.STATE_ON -> "ON"
        BluetoothAdapter.STATE_TURNING_OFF -> "TURNING_OFF"
        else -> state.toString()
    }
}
