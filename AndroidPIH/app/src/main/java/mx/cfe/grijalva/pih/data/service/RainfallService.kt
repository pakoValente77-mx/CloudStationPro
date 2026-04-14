package mx.cfe.grijalva.pih.data.service

import android.content.Context
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import mx.cfe.grijalva.pih.data.model.RainfallReportResponse
import mx.cfe.grijalva.pih.data.network.NetworkModule

class RainfallService(private val context: Context, private val authService: AuthService) {
    private val TAG = "RainfallService"

    private val _report = MutableStateFlow<RainfallReportResponse?>(null)
    val report: StateFlow<RainfallReportResponse?> = _report

    private val _isLoading = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading

    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage

    suspend fun loadReport(tipo: String = "parcial", fecha: String? = null) {
        _isLoading.value = true
        _errorMessage.value = null
        _report.value = null
        try {
            val api = NetworkModule.getApi(context)
            val response = api.getRainfallReport(authService.authHeader, tipo, fecha)
            if (response.isSuccessful) {
                _report.value = response.body()
            } else {
                _errorMessage.value = "Error: ${response.code()}"
            }
        } catch (e: Exception) {
            Log.e(TAG, "loadReport error", e)
            _errorMessage.value = "Error: ${e.localizedMessage}"
        } finally {
            _isLoading.value = false
        }
    }
}
