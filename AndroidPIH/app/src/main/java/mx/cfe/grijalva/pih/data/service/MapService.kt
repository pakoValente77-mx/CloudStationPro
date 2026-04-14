package mx.cfe.grijalva.pih.data.service

import android.content.Context
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import mx.cfe.grijalva.pih.data.model.*
import mx.cfe.grijalva.pih.data.network.NetworkModule

class MapService(private val context: Context, private val authService: AuthService) {
    private val TAG = "MapService"

    private val _availableVariables = MutableStateFlow<List<String>>(emptyList())
    val availableVariables: StateFlow<List<String>> = _availableVariables

    private val _stations = MutableStateFlow<List<StationMapData>>(emptyList())
    val stations: StateFlow<List<StationMapData>> = _stations

    private val _selectedVariable = MutableStateFlow("")
    val selectedVariable: StateFlow<String> = _selectedVariable

    private val _isLoading = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading

    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage

    suspend fun loadMapData() {
        _isLoading.value = true
        _errorMessage.value = null
        try {
            val api = NetworkModule.getApi(context)

            // Load variables from server if not yet loaded
            if (_availableVariables.value.isEmpty()) {
                try {
                    val varsResponse = api.getMapVariables(authService.authHeader)
                    if (varsResponse.isSuccessful) {
                        val vars = varsResponse.body()?.takeIf { it.isNotEmpty() }
                        if (vars != null) {
                            _availableVariables.value = vars
                            if (_selectedVariable.value.isEmpty()) {
                                _selectedVariable.value = vars.firstOrNull { it.contains("precipit", ignoreCase = true) } ?: vars.first()
                            }
                        } else {
                            applyFallbackVariables()
                        }
                    } else {
                        applyFallbackVariables()
                    }
                } catch (e: Exception) {
                    Log.w(TAG, "Could not load variables, using fallback", e)
                    applyFallbackVariables()
                }
            }

            if (_selectedVariable.value.isEmpty()) return

            val response = api.getMapData(authService.authHeader, _selectedVariable.value)
            if (response.isSuccessful) {
                _stations.value = response.body() ?: emptyList()
            } else {
                _errorMessage.value = "Error ${response.code()}"
            }
        } catch (e: Exception) {
            _errorMessage.value = "Error: ${e.localizedMessage}"
            Log.e(TAG, "loadMapData error", e)
        } finally {
            _isLoading.value = false
        }
    }

    suspend fun changeVariable(variable: String) {
        _selectedVariable.value = variable
        loadMapData()
    }

    private fun applyFallbackVariables() {
        _availableVariables.value = listOf("precipitación", "nivel_de_agua", "temperatura", "humedad_relativa")
        if (_selectedVariable.value.isEmpty()) {
            _selectedVariable.value = "precipitación"
        }
    }
}
