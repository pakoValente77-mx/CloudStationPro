package mx.cfe.grijalva.pih.ui.theme

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat

private val PIHDarkColorScheme = darkColorScheme(
    primary = PihPurple,
    onPrimary = Color.White,
    secondary = PihCyan,
    onSecondary = Color.Black,
    tertiary = PihBlue,
    background = PihBackground,
    surface = PihSurface,
    surfaceVariant = PihCard,
    onBackground = PihTextPrimary,
    onSurface = PihTextPrimary,
    onSurfaceVariant = PihTextSecondary,
    error = PihRed,
    outline = PihDivider
)

@Composable
fun PIHTheme(content: @Composable () -> Unit) {
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as Activity).window
            window.statusBarColor = PihBackground.toArgb()
            window.navigationBarColor = PihSurface.toArgb()
            WindowCompat.getInsetsController(window, view).isAppearanceLightStatusBars = false
        }
    }

    MaterialTheme(
        colorScheme = PIHDarkColorScheme,
        content = content
    )
}
