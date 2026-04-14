package mx.cfe.grijalva.pih.data.service

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import mx.cfe.grijalva.pih.data.model.LoginRequest
import mx.cfe.grijalva.pih.data.network.NetworkModule

class AuthService(private val context: Context) {
    private val prefs = NetworkModule.getPrefs(context)

    private val _isAuthenticated = MutableStateFlow(false)
    val isAuthenticated: StateFlow<Boolean> = _isAuthenticated

    private val _isLoading = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading

    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage

    var token: String = ""
        private set
    var userName: String = ""
        private set
    var fullName: String = ""
        private set
    var roles: List<String> = emptyList()
        private set

    val serverUrl: String get() = NetworkModule.getServerUrl(context)
    val authHeader: String get() = "Bearer $token"

    init {
        // Restore session
        token = prefs.getString("auth_token", "") ?: ""
        userName = prefs.getString("user_name", "") ?: ""
        fullName = prefs.getString("full_name", "") ?: ""
        _isAuthenticated.value = token.isNotBlank()
    }

    suspend fun login(server: String, user: String, password: String) {
        _isLoading.value = true
        _errorMessage.value = null

        try {
            NetworkModule.setServerUrl(context, server)
            val api = NetworkModule.getApi(context)
            val response = api.login(LoginRequest(user, password))

            if (response.isSuccessful && response.body() != null) {
                val body = response.body()!!
                token = body.token
                userName = body.usuario
                fullName = body.nombre
                roles = body.roles ?: emptyList()

                prefs.edit()
                    .putString("auth_token", token)
                    .putString("user_name", userName)
                    .putString("full_name", fullName)
                    .putString("server_url", server)
                    .apply()

                _isAuthenticated.value = true
            } else {
                _errorMessage.value = "Credenciales incorrectas (${response.code()})"
            }
        } catch (e: Exception) {
            _errorMessage.value = "Error de conexión: ${e.localizedMessage}"
        } finally {
            _isLoading.value = false
        }
    }

    fun logout() {
        token = ""
        userName = ""
        fullName = ""
        roles = emptyList()
        prefs.edit()
            .remove("auth_token")
            .remove("user_name")
            .remove("full_name")
            .apply()
        _isAuthenticated.value = false
    }
}
