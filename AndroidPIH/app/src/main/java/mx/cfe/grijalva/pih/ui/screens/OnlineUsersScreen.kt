package mx.cfe.grijalva.pih.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch
import mx.cfe.grijalva.pih.data.service.ChatService
import mx.cfe.grijalva.pih.ui.theme.*

@Composable
fun OnlineUsersScreen(chatService: ChatService, currentUserName: String, onNavigateToChat: () -> Unit) {
    val onlineUsers by chatService.onlineUsers.collectAsState()
    val scope = rememberCoroutineScope()

    LaunchedEffect(Unit) {
        chatService.loadOnlineUsers()
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(PihBackground)
    ) {
        // Header
        Surface(color = PihSurface) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(16.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column {
                    Text(
                        "Usuarios En Línea",
                        fontSize = 20.sp,
                        fontWeight = FontWeight.Bold,
                        color = PihTextPrimary
                    )
                    Text(
                        "${onlineUsers.size} conectados",
                        fontSize = 13.sp,
                        color = PihTextSecondary
                    )
                }
                IconButton(onClick = { scope.launch { chatService.loadOnlineUsers() } }) {
                    Icon(Icons.Default.Refresh, contentDescription = "Actualizar", tint = PihCyan)
                }
            }
        }

        if (onlineUsers.isEmpty()) {
            // Empty state
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(32.dp),
                contentAlignment = Alignment.Center
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("👥", fontSize = 48.sp)
                    Spacer(modifier = Modifier.height(16.dp))
                    Text(
                        "No hay usuarios en línea",
                        fontSize = 16.sp,
                        color = PihTextSecondary,
                        textAlign = TextAlign.Center
                    )
                }
            }
        } else {
            LazyColumn(
                contentPadding = PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                items(onlineUsers) { user ->
                    Card(
                        modifier = Modifier.fillMaxWidth(),
                        colors = CardDefaults.cardColors(containerColor = PihCard),
                        shape = RoundedCornerShape(12.dp)
                    ) {
                        Row(
                            modifier = Modifier.padding(12.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            // Avatar
                            Box(
                                modifier = Modifier
                                    .size(44.dp)
                                    .clip(CircleShape)
                                    .background(PihPurple),
                                contentAlignment = Alignment.Center
                            ) {
                                val displayName = user.fullName?.takeIf { it.isNotBlank() } ?: user.userName ?: "?"
                                Text(
                                    displayName.take(1).uppercase(),
                                    fontSize = 18.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = PihTextPrimary
                                )
                            }

                            Spacer(modifier = Modifier.width(12.dp))

                            // Name & status
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    user.fullName?.takeIf { it.isNotBlank() } ?: user.userName ?: "—",
                                    fontSize = 15.sp,
                                    fontWeight = FontWeight.Medium,
                                    color = PihTextPrimary
                                )
                                Row(
                                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Box(
                                        modifier = Modifier
                                            .size(6.dp)
                                            .clip(CircleShape)
                                            .background(PihGreen)
                                    )
                                    Text(
                                        "En línea",
                                        fontSize = 11.sp,
                                        color = PihGreen
                                    )

                                    // Platform icons
                                    user.platforms?.forEach { platform ->
                                        val icon = when (platform.lowercase()) {
                                            "desktop" -> "🖥️"
                                            "ios" -> "📱"
                                            "android" -> "📱"
                                            else -> "🌐"
                                        }
                                        Text(icon, fontSize = 12.sp)
                                    }
                                }
                            }

                            // DM button
                            IconButton(
                                onClick = {
                                    val targetUser = user.userName ?: return@IconButton
                                    val sorted = listOf(currentUserName, targetUser).sorted()
                                    val dmRoom = "dm:${sorted[0]}:${sorted[1]}"
                                    scope.launch {
                                        chatService.joinRoom(dmRoom)
                                        onNavigateToChat()
                                    }
                                }
                            ) {
                                Icon(
                                    Icons.Default.Send,
                                    contentDescription = "Mensaje directo",
                                    tint = PihPurple,
                                    modifier = Modifier.size(20.dp)
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
