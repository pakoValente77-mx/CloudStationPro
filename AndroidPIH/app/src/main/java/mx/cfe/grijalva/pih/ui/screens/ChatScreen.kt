package mx.cfe.grijalva.pih.ui.screens

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import coil.request.ImageRequest
import kotlinx.coroutines.launch
import mx.cfe.grijalva.pih.data.model.ChatMessage
import mx.cfe.grijalva.pih.data.service.AuthService
import mx.cfe.grijalva.pih.data.service.ChatService
import mx.cfe.grijalva.pih.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ChatScreen(chatService: ChatService, authService: AuthService) {
    val messages by chatService.messages.collectAsState()
    val rooms by chatService.rooms.collectAsState()
    val currentRoom by chatService.currentRoom.collectAsState()
    val isConnected by chatService.isConnected.collectAsState()
    val unreadCounts by chatService.unreadCounts.collectAsState()
    val scope = rememberCoroutineScope()
    val context = LocalContext.current

    var messageText by remember { mutableStateOf("") }
    var showRoomPicker by remember { mutableStateOf(false) }
    val listState = rememberLazyListState()

    val currentUserName = authService.userName

    // Auto scroll to bottom on new messages
    LaunchedEffect(messages.size) {
        if (messages.isNotEmpty()) {
            listState.animateScrollToItem(messages.size - 1)
        }
    }

    // Load rooms and history on appear
    LaunchedEffect(Unit) {
        chatService.loadRooms()
    }

    // File picker
    val filePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        uri?.let {
            scope.launch { chatService.uploadFile(context, it) }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(PihBackground)
    ) {
        // Top bar
        Surface(
            color = PihSurface,
            tonalElevation = 2.dp
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Connection indicator
                Box(
                    modifier = Modifier
                        .size(8.dp)
                        .clip(CircleShape)
                        .background(if (isConnected) PihGreen else PihRed)
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    if (isConnected) "Conectado" else "Desconectado",
                    fontSize = 11.sp,
                    color = if (isConnected) PihGreen else PihRed
                )

                Spacer(modifier = Modifier.weight(1f))

                // Room picker button
                TextButton(onClick = {
                    scope.launch { chatService.loadRooms() }
                    showRoomPicker = true
                }) {
                    val headerTitle = when {
                        currentRoom == "centinela" -> "🤖 Centinela"
                        currentRoom.startsWith("dm:") -> currentRoom.removePrefix("dm:").replace(":", " ↔ ")
                        else -> currentRoom
                    }
                    Text(
                        headerTitle,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Bold,
                        color = PihCyan,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Icon(
                        Icons.Default.ArrowDropDown,
                        contentDescription = null,
                        tint = PihCyan,
                        modifier = Modifier.size(20.dp)
                    )
                }
            }
        }

        // Messages
        LazyColumn(
            state = listState,
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .padding(horizontal = 12.dp),
            contentPadding = PaddingValues(vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp)
        ) {
            items(messages, key = { it.id ?: it.hashCode().toString() }) { message ->
                MessageBubble(
                    message = message,
                    isOwn = message.userName == currentUserName,
                    serverUrl = authService.serverUrl
                )
            }
        }

        // Input bar
        Surface(
            color = PihSurface,
            tonalElevation = 2.dp
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 8.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Attach button
                IconButton(
                    onClick = { filePickerLauncher.launch("*/*") },
                    modifier = Modifier.size(40.dp)
                ) {
                    Icon(Icons.Default.AttachFile, contentDescription = "Adjuntar", tint = PihTextSecondary)
                }

                // Text field
                OutlinedTextField(
                    value = messageText,
                    onValueChange = { messageText = it },
                    placeholder = { Text("Mensaje...", color = PihTextSecondary) },
                    modifier = Modifier.weight(1f),
                    maxLines = 3,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = PihPurple,
                        unfocusedBorderColor = PihDivider,
                        cursorColor = PihCyan,
                        focusedTextColor = PihTextPrimary,
                        unfocusedTextColor = PihTextPrimary
                    ),
                    shape = RoundedCornerShape(20.dp)
                )

                Spacer(modifier = Modifier.width(4.dp))

                // Send button
                IconButton(
                    onClick = {
                        if (messageText.isNotBlank()) {
                            chatService.sendMessage(messageText.trim())
                            messageText = ""
                        }
                    },
                    modifier = Modifier
                        .size(40.dp)
                        .background(PihPurple, CircleShape),
                    enabled = messageText.isNotBlank()
                ) {
                    Icon(
                        Icons.Default.Send,
                        contentDescription = "Enviar",
                        tint = PihTextPrimary,
                        modifier = Modifier.size(20.dp)
                    )
                }
            }
        }
    }

    // Room picker sheet
    if (showRoomPicker) {
        ModalBottomSheet(
            onDismissRequest = { showRoomPicker = false },
            containerColor = PihBackground,
            contentColor = PihTextPrimary
        ) {
            Column(modifier = Modifier.padding(16.dp)) {
                Text(
                    "Salas de Chat",
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    color = PihTextPrimary
                )
                Spacer(modifier = Modifier.height(12.dp))

                val publicRooms = rooms.filter { !(it.isDm ?: false) }
                val dmRooms = rooms.filter { it.isDm ?: false }

                if (publicRooms.isNotEmpty()) {
                    Text("Salas Públicas", fontSize = 12.sp, color = PihTextSecondary)
                    Spacer(modifier = Modifier.height(4.dp))
                    publicRooms.forEach { room ->
                        val icon = if (room.id == "centinela") "🤖" else "#"
                        val label = if (room.id == "centinela") "Centinela IA" else (room.name ?: room.id ?: "—")
                        RoomItem(
                            name = label,
                            icon = icon,
                            isSelected = room.id == currentRoom,
                            unread = unreadCounts[room.id] ?: 0,
                            onClick = {
                                scope.launch {
                                    chatService.joinRoom(room.id ?: "")
                                    showRoomPicker = false
                                }
                            }
                        )
                    }
                }

                if (dmRooms.isNotEmpty()) {
                    Spacer(modifier = Modifier.height(12.dp))
                    Text("Mensajes Directos", fontSize = 12.sp, color = PihTextSecondary)
                    Spacer(modifier = Modifier.height(4.dp))
                    dmRooms.forEach { room ->
                        RoomItem(
                            name = room.id?.removePrefix("dm:")?.replace(":", " ↔ ") ?: "—",
                            icon = "👤",
                            isSelected = room.id == currentRoom,
                            unread = unreadCounts[room.id] ?: 0,
                            onClick = {
                                scope.launch {
                                    chatService.joinRoom(room.id ?: "")
                                    showRoomPicker = false
                                }
                            }
                        )
                    }
                }

                Spacer(modifier = Modifier.height(32.dp))
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RoomItem(
    name: String,
    icon: String,
    isSelected: Boolean,
    unread: Int,
    onClick: () -> Unit
) {
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 2.dp)
            .clickable(onClick = onClick),
        colors = CardDefaults.cardColors(
            containerColor = if (isSelected) PihPurple.copy(alpha = 0.15f) else PihCard
        ),
        shape = RoundedCornerShape(8.dp)
    ) {
        Row(
            modifier = Modifier.padding(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(icon, fontSize = 16.sp)
            Spacer(modifier = Modifier.width(8.dp))
            Text(
                name,
                fontSize = 14.sp,
                color = PihTextPrimary,
                modifier = Modifier.weight(1f)
            )
            if (isSelected) {
                Icon(Icons.Default.Check, contentDescription = null, tint = PihPurple, modifier = Modifier.size(18.dp))
            }
            if (unread > 0) {
                Spacer(modifier = Modifier.width(8.dp))
                Badge(
                    containerColor = PihRed,
                    contentColor = PihTextPrimary
                ) {
                    Text(if (unread > 99) "99+" else "$unread", fontSize = 10.sp)
                }
            }
        }
    }
}

@Composable
fun MessageBubble(message: ChatMessage, isOwn: Boolean, serverUrl: String) {
    val isBot = message.isBot
    val alignment = if (isOwn) Arrangement.End else Arrangement.Start
    val bgColor = when {
        isBot -> PihPurple.copy(alpha = 0.15f)
        isOwn -> PihPurple.copy(alpha = 0.7f)
        else -> PihCard
    }
    val shape = if (isOwn) {
        RoundedCornerShape(16.dp, 16.dp, 4.dp, 16.dp)
    } else {
        RoundedCornerShape(16.dp, 16.dp, 16.dp, 4.dp)
    }

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = alignment
    ) {
        Card(
            modifier = Modifier.widthIn(max = 300.dp),
            colors = CardDefaults.cardColors(containerColor = bgColor),
            shape = shape,
            border = if (isBot) BorderStroke(1.dp, PihPurple.copy(alpha = 0.3f)) else null
        ) {
            Column(modifier = Modifier.padding(10.dp)) {
                // Sender name (only for others)
                if (!isOwn) {
                    Text(
                        (if (isBot) "🤖 " else "") + message.displayName,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold,
                        color = if (isBot) PihCyan else PihPurple
                    )
                    Spacer(modifier = Modifier.height(2.dp))
                }

                // File attachment
                if (message.hasFile) {
                    FileAttachment(message, serverUrl)
                    if (!message.message.isNullOrBlank()) {
                        Spacer(modifier = Modifier.height(4.dp))
                    }
                }

                // Message text
                if (!message.message.isNullOrBlank()) {
                    Text(
                        message.message!!,
                        fontSize = 14.sp,
                        color = PihTextPrimary
                    )
                }

                // Time
                Text(
                    message.timeString,
                    fontSize = 10.sp,
                    color = PihTextPrimary.copy(alpha = 0.6f),
                    modifier = Modifier.align(Alignment.End)
                )
            }
        }
    }
}

@Composable
fun FileAttachment(message: ChatMessage, serverUrl: String) {
    if (message.isImage) {
        // Image preview — support both absolute URLs (Azure SAS) and relative paths
        val imageUrl = if (message.fileUrl?.startsWith("http") == true) message.fileUrl else "$serverUrl${message.fileUrl}"
        AsyncImage(
            model = ImageRequest.Builder(LocalContext.current)
                .data(imageUrl)
                .crossfade(true)
                .build(),
            contentDescription = message.fileName,
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(max = 200.dp)
                .clip(RoundedCornerShape(8.dp)),
            contentScale = ContentScale.Fit
        )
    } else {
        // File card
        Card(
            colors = CardDefaults.cardColors(containerColor = PihSurface),
            shape = RoundedCornerShape(8.dp)
        ) {
            Row(
                modifier = Modifier.padding(8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                val fileIcon = when {
                    message.fileName?.endsWith(".pdf", true) == true -> "📄"
                    message.fileName?.matches(Regex(".*\\.(doc|docx)$", RegexOption.IGNORE_CASE)) == true -> "📝"
                    message.fileName?.matches(Regex(".*\\.(xls|xlsx)$", RegexOption.IGNORE_CASE)) == true -> "📊"
                    message.fileName?.matches(Regex(".*\\.(zip|rar|7z)$", RegexOption.IGNORE_CASE)) == true -> "🗜️"
                    else -> "📎"
                }
                Text(fileIcon, fontSize = 24.sp)
                Spacer(modifier = Modifier.width(8.dp))
                Column {
                    Text(
                        message.fileName ?: "archivo",
                        fontSize = 12.sp,
                        color = PihCyan,
                        fontWeight = FontWeight.Medium,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Text(
                        message.fileSizeFormatted,
                        fontSize = 10.sp,
                        color = PihTextSecondary
                    )
                }
            }
        }
    }
}
