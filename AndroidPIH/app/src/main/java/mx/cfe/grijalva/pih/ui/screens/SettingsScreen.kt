package mx.cfe.grijalva.pih.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import mx.cfe.grijalva.pih.data.service.AuthService
import mx.cfe.grijalva.pih.data.service.ChatService
import mx.cfe.grijalva.pih.ui.theme.*

@Composable
fun SettingsScreen(authService: AuthService, chatService: ChatService) {
    val userName = authService.userName
    val fullName = authService.fullName
    val serverUrl = authService.serverUrl

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(PihBackground)
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Spacer(modifier = Modifier.height(24.dp))

        // Avatar
        Box(
            modifier = Modifier
                .size(80.dp)
                .clip(CircleShape)
                .background(PihPurple),
            contentAlignment = Alignment.Center
        ) {
            Text(
                fullName.take(1).uppercase().ifEmpty { "?" },
                fontSize = 32.sp,
                fontWeight = FontWeight.Bold,
                color = PihTextPrimary
            )
        }

        Spacer(modifier = Modifier.height(16.dp))

        Text(
            fullName.ifEmpty { "Usuario" },
            fontSize = 22.sp,
            fontWeight = FontWeight.Bold,
            color = PihTextPrimary
        )
        Text(
            "@$userName",
            fontSize = 14.sp,
            color = PihTextSecondary
        )

        Spacer(modifier = Modifier.height(32.dp))

        // Info cards
        Card(
            modifier = Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = PihCard),
            shape = RoundedCornerShape(12.dp)
        ) {
            Column(modifier = Modifier.padding(16.dp)) {
                SettingsRow(Icons.Default.Cloud, "Servidor", serverUrl)
                Divider(thickness = 0.5.dp, color = PihDivider)
                SettingsRow(Icons.Default.Info, "Versión", "1.0.0")
                Divider(thickness = 0.5.dp, color = PihDivider)
                SettingsRow(Icons.Default.PhoneAndroid, "Plataforma", "Android")
            }
        }

        Spacer(modifier = Modifier.weight(1f))

        // Logout button
        Button(
            onClick = {
                chatService.disconnect()
                authService.logout()
            },
            modifier = Modifier
                .fillMaxWidth()
                .height(50.dp),
            shape = RoundedCornerShape(12.dp),
            colors = ButtonDefaults.buttonColors(
                containerColor = PihRed.copy(alpha = 0.8f)
            )
        ) {
            Icon(Icons.Default.Logout, contentDescription = null, modifier = Modifier.size(20.dp))
            Spacer(modifier = Modifier.width(8.dp))
            Text(
                "Cerrar Sesión",
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold
            )
        }

        Spacer(modifier = Modifier.height(16.dp))

        Text(
            "PIH - Plataforma Integral Hidrometeorológica\nCFE Subgerencia Técnica Grijalva",
            fontSize = 11.sp,
            color = PihTextSecondary.copy(alpha = 0.5f),
            textAlign = TextAlign.Center
        )
    }
}

@Composable
fun SettingsRow(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, value: String) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(icon, contentDescription = null, tint = PihPurple, modifier = Modifier.size(20.dp))
        Spacer(modifier = Modifier.width(12.dp))
        Text(label, fontSize = 14.sp, color = PihTextSecondary, modifier = Modifier.weight(1f))
        Text(
            value,
            fontSize = 13.sp,
            color = PihTextPrimary,
            fontFamily = FontFamily.Monospace
        )
    }
}
