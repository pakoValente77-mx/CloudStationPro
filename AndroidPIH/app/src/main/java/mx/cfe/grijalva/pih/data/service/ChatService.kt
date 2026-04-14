package mx.cfe.grijalva.pih.data.service

import android.content.Context
import android.net.Uri
import android.util.Log
import com.google.gson.Gson
import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.HubConnectionState
import com.microsoft.signalr.TransportEnum
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import mx.cfe.grijalva.pih.data.model.*
import mx.cfe.grijalva.pih.data.network.NetworkModule
import okhttp3.MediaType.Companion.toMediaTypeOrNull

class ChatService(private val context: Context, private val authService: AuthService) {
    private val TAG = "ChatService"
    private val gson = Gson()
    private var hubConnection: HubConnection? = null
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())

    private val _messages = MutableStateFlow<List<ChatMessage>>(emptyList())
    val messages: StateFlow<List<ChatMessage>> = _messages

    private val _rooms = MutableStateFlow<List<ChatRoom>>(emptyList())
    val rooms: StateFlow<List<ChatRoom>> = _rooms

    private val _onlineUsers = MutableStateFlow<List<OnlineUser>>(emptyList())
    val onlineUsers: StateFlow<List<OnlineUser>> = _onlineUsers

    private val _currentRoom = MutableStateFlow("general")
    val currentRoom: StateFlow<String> = _currentRoom

    private val _isConnected = MutableStateFlow(false)
    val isConnected: StateFlow<Boolean> = _isConnected

    private val _connectionError = MutableStateFlow<String?>(null)
    val connectionError: StateFlow<String?> = _connectionError

    private val _unreadCounts = MutableStateFlow<Map<String, Int>>(emptyMap())
    val unreadCounts: StateFlow<Map<String, Int>> = _unreadCounts

    fun connect() {
        if (authService.token.isBlank()) return

        val baseUrl = NetworkModule.getBaseUrl(context)
        val encodedToken = java.net.URLEncoder.encode(authService.token, "UTF-8")
        val hubUrl = "$baseUrl/hubs/chat?access_token=$encodedToken&platform=android"

        try {
            hubConnection = HubConnectionBuilder.create(hubUrl)
                .withTransport(TransportEnum.WEBSOCKETS)
                .shouldSkipNegotiate(true)
                .build()

            // ReceiveMessage
            hubConnection?.on("ReceiveMessage", { msgJson ->
                try {
                    val msg = gson.fromJson(msgJson, ChatMessage::class.java)
                    val isSelf = msg.userName == authService.userName
                    val msgRoom = msg.room ?: ""

                    if (msgRoom == _currentRoom.value) {
                        _messages.value = _messages.value + msg
                    } else if (msgRoom.isNotBlank()) {
                        val counts = _unreadCounts.value.toMutableMap()
                        counts[msgRoom] = (counts[msgRoom] ?: 0) + 1
                        _unreadCounts.value = counts
                    }

                    // Local notification for messages from others
                    if (!isSelf) {
                        NotificationHelper.showMessageNotification(
                            context, msg.displayName, msg.message ?: "", msg.room ?: "",
                            _currentRoom.value
                        )
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error parsing message: ${e.message}")
                }
            }, String::class.java)

            // UserConnected
            hubConnection?.on("UserConnected", { _, _, _ ->
                loadOnlineUsers()
            }, String::class.java, String::class.java, String::class.java)

            // UserDisconnected
            hubConnection?.on("UserDisconnected", { _, _ ->
                loadOnlineUsers()
            }, String::class.java, String::class.java)

            hubConnection?.onClosed {
                _isConnected.value = false
                _connectionError.value = it?.message
                // Auto-reconnect after 5 seconds
                scope.launch {
                    delay(5000)
                    if (!_isConnected.value) connect()
                }
            }

            hubConnection?.start()?.blockingAwait()

            _isConnected.value = true
            _connectionError.value = null

            // Join current room + alertas
            hubConnection?.send("JoinRoom", _currentRoom.value)
            hubConnection?.send("JoinRoom", "alertas-precipitacion")

            // Register FCM token
            registerFcmToken()

            // Load initial data
            loadHistory(_currentRoom.value)
            loadRooms()
            loadOnlineUsers()

        } catch (e: Exception) {
            Log.e(TAG, "Connection failed: ${e.message}")
            _isConnected.value = false
            _connectionError.value = "No se pudo conectar: ${e.localizedMessage}"
        }
    }

    fun disconnect() {
        try {
            hubConnection?.stop()?.blockingAwait()
        } catch (_: Exception) {}
        hubConnection = null
        _isConnected.value = false
    }

    fun sendMessage(text: String) {
        if (text.isBlank()) return
        try {
            hubConnection?.send("SendMessage", _currentRoom.value, text)
        } catch (e: Exception) {
            Log.e(TAG, "Error sending: ${e.message}")
        }
    }

    fun joinRoom(room: String) {
        val oldRoom = _currentRoom.value
        _currentRoom.value = room

        // Clear unread
        val counts = _unreadCounts.value.toMutableMap()
        counts.remove(room)
        _unreadCounts.value = counts

        try {
            hubConnection?.send("LeaveRoom", oldRoom)
            hubConnection?.send("JoinRoom", room)
        } catch (_: Exception) {}

        loadHistory(room)
    }

    fun loadRooms() {
        scope.launch {
            try {
                val api = NetworkModule.getApi(context)
                val response = api.getRooms(authService.authHeader)
                if (response.isSuccessful) {
                    val roomInfos = response.body() ?: emptyList()
                    val chatRooms = roomInfos.map { info ->
                        ChatRoom(
                            id = info.room,
                            name = info.room,
                            isDm = info.room.startsWith("dm:")
                        )
                    }.toMutableList()
                    if (chatRooms.none { it.id == "general" }) {
                        chatRooms.add(0, ChatRoom("general", "general"))
                    }
                    if (chatRooms.none { it.id == "centinela" }) {
                        val idx = chatRooms.indexOfLast { !(it.isDm ?: false) }.let { if (it < 0) 0 else it + 1 }
                        chatRooms.add(idx, ChatRoom("centinela", "centinela"))
                    }
                    _rooms.value = chatRooms
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading rooms: ${e.message}")
            }
        }
    }

    fun loadHistory(room: String) {
        scope.launch {
            try {
                val api = NetworkModule.getApi(context)
                val response = api.getHistory(authService.authHeader, room)
                if (response.isSuccessful) {
                    _messages.value = response.body() ?: emptyList()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading history: ${e.message}")
            }
        }
    }

    fun loadOnlineUsers() {
        scope.launch {
            try {
                val api = NetworkModule.getApi(context)
                val response = api.getOnlineUsers(authService.authHeader)
                if (response.isSuccessful) {
                    _onlineUsers.value = response.body() ?: emptyList()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading online users: ${e.message}")
            }
        }
    }

    fun clearUnread(room: String) {
        val counts = _unreadCounts.value.toMutableMap()
        counts.remove(room)
        _unreadCounts.value = counts
    }

    fun uploadFile(ctx: Context, uri: Uri) {
        scope.launch {
            try {
                val contentResolver = ctx.contentResolver
                val mimeType = contentResolver.getType(uri) ?: "application/octet-stream"
                val cursor = contentResolver.query(uri, null, null, null, null)
                val fileName = cursor?.use {
                    if (it.moveToFirst()) {
                        val nameIndex = it.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
                        if (nameIndex >= 0) it.getString(nameIndex) else "file"
                    } else "file"
                } ?: "file"
                val bytes = contentResolver.openInputStream(uri)?.readBytes() ?: return@launch
                uploadFile(bytes, fileName, mimeType) { _ -> }
            } catch (e: Exception) {
                Log.e(TAG, "Error uploading file from Uri: ${e.message}")
            }
        }
    }

    fun uploadFile(data: ByteArray, fileName: String, mimeType: String, onResult: (Boolean) -> Unit) {
        scope.launch {
            try {
                val api = NetworkModule.getApi(context)
                val roomBody = okhttp3.RequestBody.create(
                    "text/plain".toMediaTypeOrNull(), _currentRoom.value
                )
                val fileBody = okhttp3.RequestBody.create(
                    mimeType.toMediaTypeOrNull(), data
                )
                val filePart = okhttp3.MultipartBody.Part.createFormData("file", fileName, fileBody)
                val response = api.uploadFile(authService.authHeader, roomBody, filePart)
                withContext(Dispatchers.Main) { onResult(response.isSuccessful) }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) { onResult(false) }
            }
        }
    }

    private fun registerFcmToken() {
        val token = NetworkModule.getPrefs(context).getString("fcm_token", null) ?: return
        scope.launch {
            try {
                val api = NetworkModule.getApi(context)
                api.registerDevice(authService.authHeader, DeviceRegistration(token, "android"))
                Log.d(TAG, "FCM token registered")
            } catch (e: Exception) {
                Log.e(TAG, "FCM token registration failed: ${e.message}")
            }
        }
    }

    val totalUnread: Int get() = _unreadCounts.value.values.sum()
}
