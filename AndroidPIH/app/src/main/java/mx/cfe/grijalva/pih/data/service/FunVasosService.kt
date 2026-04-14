package mx.cfe.grijalva.pih.data.service

import android.content.Context
import android.util.Log
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import mx.cfe.grijalva.pih.data.model.*
import mx.cfe.grijalva.pih.data.network.NetworkModule

class FunVasosService(private val context: Context, private val authService: AuthService) {
    private val TAG = "FunVasosService"
    private var autoRefreshJob: Job? = null
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())

    private val _cascadeData = MutableStateFlow<List<CascadePresa>>(emptyList())
    val cascadeData: StateFlow<List<CascadePresa>> = _cascadeData

    private val _allPresas = MutableStateFlow<List<FunVasosResumenPresa>>(emptyList())
    val allPresas: StateFlow<List<FunVasosResumenPresa>> = _allPresas

    private val _fechasDisponibles = MutableStateFlow<List<String>>(emptyList())
    val fechasDisponibles: StateFlow<List<String>> = _fechasDisponibles

    private val _selectedFecha = MutableStateFlow("")
    val selectedFecha: StateFlow<String> = _selectedFecha

    private val _isLoading = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading

    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage

    suspend fun loadCascade() {
        _isLoading.value = true
        _errorMessage.value = null
        try {
            val api = NetworkModule.getApi(context)
            val response = api.getCascadeData(authService.authHeader)
            if (response.isSuccessful && response.body() != null) {
                _cascadeData.value = response.body()!!.presas
            } else {
                _errorMessage.value = "Error ${response.code()}"
            }
        } catch (e: Exception) {
            _errorMessage.value = "Error: ${e.localizedMessage}"
            Log.e(TAG, "loadCascade error", e)
        } finally {
            _isLoading.value = false
        }
    }

    suspend fun loadData(fechaInicio: String? = null, fechaFin: String? = null) {
        _isLoading.value = true
        _errorMessage.value = null
        try {
            val api = NetworkModule.getApi(context)
            val response = api.getFunVasosData(authService.authHeader, fechaInicio, fechaFin)
            if (response.isSuccessful && response.body() != null) {
                val data = response.body()!!
                _allPresas.value = data.presas
                _fechasDisponibles.value = data.fechasDisponibles ?: emptyList()
                if (_selectedFecha.value.isBlank() && data.fechasDisponibles?.isNotEmpty() == true) {
                    _selectedFecha.value = data.fechasDisponibles.last()
                }
            } else {
                _errorMessage.value = "Error ${response.code()}"
            }
        } catch (e: Exception) {
            _errorMessage.value = "Error: ${e.localizedMessage}"
            Log.e(TAG, "loadData error", e)
        } finally {
            _isLoading.value = false
        }
    }

    suspend fun loadDataForDate(fecha: String) {
        _selectedFecha.value = fecha
        loadData(fecha, fecha)
    }

    fun startAutoRefresh() {
        stopAutoRefresh()
        autoRefreshJob = scope.launch {
            while (isActive) {
                delay(5 * 60 * 1000L) // 5 minutes
                try {
                    loadCascade()
                    loadData()
                } catch (e: Exception) {
                    Log.e(TAG, "Auto-refresh error", e)
                }
            }
        }
    }

    fun stopAutoRefresh() {
        autoRefreshJob?.cancel()
        autoRefreshJob = null
    }
}
