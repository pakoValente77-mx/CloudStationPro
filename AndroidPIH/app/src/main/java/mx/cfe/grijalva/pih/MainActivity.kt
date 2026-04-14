package mx.cfe.grijalva.pih

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.*
import mx.cfe.grijalva.pih.data.service.AuthService
import mx.cfe.grijalva.pih.ui.screens.LoginScreen
import mx.cfe.grijalva.pih.ui.screens.MainScreen
import mx.cfe.grijalva.pih.ui.theme.PIHTheme

class MainActivity : ComponentActivity() {

    private lateinit var authService: AuthService

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        authService = AuthService(this)

        setContent {
            PIHTheme {
                val isAuthenticated by authService.isAuthenticated.collectAsState()

                if (isAuthenticated) {
                    val navigateRoom = intent?.getStringExtra("navigate_room")
                    MainScreen(
                        authService = authService,
                        initialRoom = navigateRoom
                    )
                } else {
                    LoginScreen(authService = authService)
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
    }
}
