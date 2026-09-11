package com.totall.messenger

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.totall.messenger.data.remote.NetworkProvider
import com.totall.messenger.data.remote.ServerConfig
import com.totall.messenger.ui.nav.MessengerNavGraph
import com.totall.messenger.ui.theme.TotallMessengerTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // ⚙️ اتصال به سرور توتال — این دو خط را با مقادیر واقعی لاگین پر کنید:
        // ServerConfig.setBaseUrl("http://192.168.1.10:5000/")
        // NetworkProvider.tokenProvider = { sessionManager.jwtToken }

        enableEdgeToEdge()
        setContent {
            TotallMessengerTheme {
                MessengerNavGraph()
            }
        }
    }
}
