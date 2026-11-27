package com.justintime.app

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import com.justintime.app.ui.JustInTimeTheme
import com.justintime.app.ui.MainScreen
import com.justintime.app.ui.MainViewModel

class MainActivity : ComponentActivity() {
    private val viewModel: MainViewModel by viewModels {
        MainViewModel.Factory((application as JustInTimeApp).container)
    }

    private val bluetoothPermission =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { viewModel.refreshPermissions() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        setContent {
            JustInTimeTheme {
                MainScreen(
                    viewModel = viewModel,
                    onRequestBluetooth = {
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                            bluetoothPermission.launch(Manifest.permission.BLUETOOTH_CONNECT)
                        }
                    },
                    onOpenExactAlarmSettings = {
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                            open(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM, Uri.parse("package:$packageName")))
                        }
                    },
                    onOpenBatterySettings = {
                        open(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))
                    },
                )
            }
        }
    }

    override fun onResume() {
        super.onResume()
        // Пользователь мог выдать или отозвать разрешение в системных настройках.
        viewModel.refreshPermissions()
    }

    private fun open(intent: Intent) {
        try {
            startActivity(intent)
        } catch (_: ActivityNotFoundException) {
            // На некоторых прошивках такого экрана нет: пользователь найдёт настройку вручную.
        }
    }
}
