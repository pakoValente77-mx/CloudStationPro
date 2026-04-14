package mx.cfe.grijalva.pih.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import mx.cfe.grijalva.pih.data.service.*
import mx.cfe.grijalva.pih.ui.theme.*

data class TabItem(
    val title: String,
    val icon: ImageVector,
    val badge: Int = 0
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(
    authService: AuthService,
    initialRoom: String? = null
) {
    val context = LocalContext.current
    var selectedTab by remember { mutableIntStateOf(if (initialRoom != null) 4 else 0) }

    val chatService = remember { ChatService(context, authService) }
    val funVasosService = remember { FunVasosService(context, authService) }
    val mapService = remember { MapService(context, authService) }
    val stationDataService = remember { StationDataService(context, authService) }
    val rainfallService = remember { RainfallService(context, authService) }

    val unreadCounts by chatService.unreadCounts.collectAsState()
    val totalUnread = unreadCounts.values.sum()

    // Connect chat on launch
    LaunchedEffect(Unit) {
        chatService.connect()
        if (initialRoom != null) {
            chatService.joinRoom(initialRoom)
        }
    }

    // Cleanup on disposal
    DisposableEffect(Unit) {
        onDispose {
            chatService.disconnect()
            funVasosService.stopAutoRefresh()
        }
    }

    val tabs = listOf(
        TabItem("Vasos", Icons.Default.Water),
        TabItem("Mapa", Icons.Default.Map),
        TabItem("Datos", Icons.Default.BarChart),
        TabItem("Lluvia", Icons.Default.WaterDrop),
        TabItem("Chat", Icons.Default.Chat, badge = totalUnread),
        TabItem("En Línea", Icons.Default.People),
        TabItem("Ajustes", Icons.Default.Settings)
    )

    Scaffold(
        containerColor = PihBackground,
        bottomBar = {
            NavigationBar(
                containerColor = PihSurface,
                contentColor = PihTextPrimary,
                tonalElevation = 0.dp
            ) {
                tabs.forEachIndexed { index, tab ->
                    NavigationBarItem(
                        icon = {
                            if (tab.badge > 0) {
                                BadgedBox(
                                    badge = {
                                        Badge(
                                            containerColor = PihRed,
                                            contentColor = PihTextPrimary
                                        ) {
                                            Text(
                                                if (tab.badge > 99) "99+" else tab.badge.toString(),
                                                fontSize = 10.sp
                                            )
                                        }
                                    }
                                ) {
                                    Icon(tab.icon, contentDescription = tab.title)
                                }
                            } else {
                                Icon(tab.icon, contentDescription = tab.title)
                            }
                        },
                        label = {
                            Text(
                                tab.title,
                                fontSize = 10.sp,
                                fontWeight = if (selectedTab == index) FontWeight.Bold else FontWeight.Normal
                            )
                        },
                        selected = selectedTab == index,
                        onClick = {
                            selectedTab = index
                            if (index == 4) {
                                // Clear unread for current room when entering chat
                                chatService.clearUnread(chatService.currentRoom.value)
                            }
                        },
                        colors = NavigationBarItemDefaults.colors(
                            selectedIconColor = PihCyan,
                            selectedTextColor = PihCyan,
                            unselectedIconColor = PihTextSecondary,
                            unselectedTextColor = PihTextSecondary,
                            indicatorColor = PihCyan.copy(alpha = 0.12f)
                        )
                    )
                }
            }
        }
    ) { paddingValues ->
        Box(modifier = Modifier.padding(paddingValues)) {
            when (selectedTab) {
                0 -> FunVasosScreen(funVasosService)
                1 -> MapScreen(mapService)
                2 -> StationDataScreen(stationDataService)
                3 -> RainfallReportScreen(rainfallService)
                4 -> ChatScreen(chatService, authService)
                5 -> OnlineUsersScreen(chatService, authService.userName, onNavigateToChat = { selectedTab = 4 })
                6 -> SettingsScreen(authService, chatService)
            }
        }
    }
}
