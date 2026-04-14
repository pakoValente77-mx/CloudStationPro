package mx.cfe.grijalva.pih.data.service

import android.content.Context
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import mx.cfe.grijalva.pih.data.model.*
import mx.cfe.grijalva.pih.data.network.NetworkModule
import java.text.SimpleDateFormat
import java.util.*

class StationDataService(private val context: Context, private val authService: AuthService) {
    private val TAG = "StationDataService"

    private val _stations = MutableStateFlow<List<StationInfo>>(emptyList())
    val stations: StateFlow<List<StationInfo>> = _stations

    private val _variables = MutableStateFlow<List<StationVariable>>(emptyList())
    val variables: StateFlow<List<StationVariable>> = _variables

    private val _analysisData = MutableStateFlow<DataAnalysisResponse?>(null)
    val analysisData: StateFlow<DataAnalysisResponse?> = _analysisData

    private val _isLoadingStations = MutableStateFlow(false)
    val isLoadingStations: StateFlow<Boolean> = _isLoadingStations

    private val _isLoadingVariables = MutableStateFlow(false)
    val isLoadingVariables: StateFlow<Boolean> = _isLoadingVariables

    private val _isLoadingData = MutableStateFlow(false)
    val isLoadingData: StateFlow<Boolean> = _isLoadingData

    private val _isLoading = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading

    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage

    suspend fun loadStations(onlyCfe: Boolean = true) {
        _isLoadingStations.value = true
        _isLoading.value = true
        try {
            val api = NetworkModule.getApi(context)
            val response = api.getStations(authService.authHeader, onlyCfe)
            if (response.isSuccessful) {
                _stations.value = response.body() ?: emptyList()
            }
        } catch (e: Exception) {
            Log.e(TAG, "loadStations error", e)
        } finally {
            _isLoadingStations.value = false
            _isLoading.value = false
        }
    }

    suspend fun loadVariables(stationId: String) {
        _isLoadingVariables.value = true
        try {
            val api = NetworkModule.getApi(context)
            val response = api.getStationVariables(authService.authHeader, stationId)
            if (response.isSuccessful) {
                _variables.value = response.body() ?: emptyList()
            }
        } catch (e: Exception) {
            Log.e(TAG, "loadVariables error", e)
        } finally {
            _isLoadingVariables.value = false
        }
    }

    suspend fun loadAnalysisData(
        stationIds: List<String>, variable: String,
        startDate: Date, endDate: Date
    ) {
        val fmt = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply {
            timeZone = TimeZone.getTimeZone("UTC")
        }
        loadAnalysisData(stationIds, variable, fmt.format(startDate), fmt.format(endDate))
    }

    suspend fun loadAnalysisData(
        stationIds: List<String>, variable: String,
        startDate: String, endDate: String
    ) {
        _isLoadingData.value = true
        _isLoading.value = true
        _errorMessage.value = null
        try {
            val api = NetworkModule.getApi(context)
            val response = api.getAnalysisData(
                authService.authHeader,
                AnalysisRequest(stationIds, variable, startDate, endDate)
            )
            if (response.isSuccessful) {
                _analysisData.value = response.body()
            } else {
                _errorMessage.value = "Error ${response.code()}"
            }
        } catch (e: Exception) {
            _errorMessage.value = "Error: ${e.localizedMessage}"
            Log.e(TAG, "loadAnalysisData error", e)
        } finally {
            _isLoadingData.value = false
            _isLoading.value = false
        }
    }
}
