package mx.cfe.grijalva.pih.data.model

import com.google.gson.annotations.SerializedName
import java.text.SimpleDateFormat
import java.util.*

// ============ AUTH ============

data class LoginRequest(
    val userName: String,
    val password: String
)

data class LoginResponse(
    val token: String,
    val usuario: String,
    val nombre: String,
    val roles: List<String>?
)

// ============ CHAT ============

data class ChatMessage(
    val id: String,
    val chatId: String,
    val room: String,
    val userId: String,
    val userName: String,
    val fullName: String?,
    val message: String,
    val timestamp: String,
    val fileName: String?,
    val fileUrl: String?,
    val fileSize: Long?,
    val fileType: String?
) {
    val displayName: String get() = fullName?.takeIf { it.isNotBlank() } ?: userName

    val timeString: String
        get() {
            return try {
                val utcParser = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.getDefault()).apply {
                    timeZone = TimeZone.getTimeZone("UTC")
                }
                val localFormat = SimpleDateFormat("HH:mm", Locale.getDefault())
                val date = utcParser.parse(timestamp.take(19))
                if (date != null) localFormat.format(date) else ""
            } catch (_: Exception) {
                try {
                    val idx = timestamp.indexOf('T')
                    if (idx >= 0 && timestamp.length >= idx + 6) {
                        timestamp.substring(idx + 1, idx + 6)
                    } else ""
                } catch (_: Exception) { "" }
            }
        }

    val isBot: Boolean get() = userName == "Centinela"

    val hasFile: Boolean get() = !fileName.isNullOrBlank()

    val isImage: Boolean
        get() = fileType?.lowercase()?.startsWith("image/") == true

    val fileSizeFormatted: String
        get() {
            val size = fileSize ?: return ""
            return when {
                size < 1024 -> "$size B"
                size < 1024 * 1024 -> String.format("%.1f KB", size / 1024.0)
                else -> String.format("%.1f MB", size / (1024.0 * 1024.0))
            }
        }
}

data class ChatRoom(
    val id: String,
    val name: String,
    val isDm: Boolean = false
)

data class ChatRoomResponse(
    val room: String,
    val messageCount: Int?,
    val lastActivity: String?
)

data class OnlineUser(
    val userId: String?,
    val userName: String,
    val fullName: String?,
    val platforms: List<String>?
)

data class DeviceRegistration(
    val token: String,
    val platform: String = "android"
)

// ============ FUNVASOS ============

data class CascadeResponse(
    val presas: List<CascadePresa>,
    val fecha: String?
)

data class CascadePresa(
    val key: String?,
    val name: String,
    val currentElev: Double?,
    val generation: Double?,
    val activeUnits: Int?,
    val almacenamiento: Double?,
    val ultimaHora: Int?,
    val fecha: String?,
    val aportacionesV: Double?,
    val extraccionesV: Double?
)

data class FunVasosResponse(
    val fechaInicio: String?,
    val fechaFin: String?,
    val presas: List<FunVasosResumenPresa>,
    val fechasDisponibles: List<String>?
)

data class FunVasosResumenPresa(
    val presa: String,
    val ultimaElevacion: Double?,
    val ultimoAlmacenamiento: Double?,
    val totalAportacionesV: Double?,
    val totalExtraccionesV: Double?,
    val totalGeneracion: Double?,
    val ultimaHora: Int?,
    val datos: List<FunVasosDatoHorario>?
)

data class FunVasosDatoHorario(
    val ts: String?,
    val presa: String?,
    val hora: Int?,
    val elevacion: Double?,
    val almacenamiento: Double?,
    val diferencia: Double?,
    val aportacionesQ: Double?,
    val aportacionesV: Double?,
    @SerializedName("extraccionesTurbQ") val extraccionesTurbQ: Double?,
    @SerializedName("extraccionesTurbV") val extraccionesTurbV: Double?,
    @SerializedName("extraccionesVertQ") val extraccionesVertQ: Double?,
    @SerializedName("extraccionesVertV") val extraccionesVertV: Double?,
    @SerializedName("extraccionesTotalQ") val extraccionesTotalQ: Double?,
    @SerializedName("extraccionesTotalV") val extraccionesTotalV: Double?,
    val generacion: Double?,
    val numUnidades: Int?,
    val aportacionCuencaPropia: Double?,
    val aportacionPromedio: Double?
)

// ============ MAP ============

data class StationMapData(
    val id: String?,
    val dcpId: String?,
    val nombre: String?,
    val lat: Double,
    val lon: Double,
    val estatusColor: String?,
    val valorActual: Double?,
    val valorAuxiliar: Double?,
    val variableActual: String?,
    val ultimaTx: String?,
    val isCfe: Boolean?,
    val isGolfoCentro: Boolean?,
    val hasCota: Boolean?,
    val enMantenimiento: Boolean?
)

// ============ STATION DATA ============

data class StationInfo(
    val id: String,
    val name: String,
    val lat: Double?,
    val lon: Double?
)

data class StationVariable(
    val variable: String,
    val displayName: String?,
    val hasData: Boolean?,
    val lastUpdate: String?,
    val sensorId: String?
)

data class AnalysisRequest(
    val stationIds: List<String>,
    val variable: String,
    val startDate: String,
    val endDate: String
)

data class DataAnalysisResponse(
    val aggregationLevel: String?,
    val variable: String?,
    val startDate: String?,
    val endDate: String?,
    val series: List<DataSeries>?
)

data class DataSeries(
    val stationId: String?,
    val stationName: String?,
    val minLimit: Double?,
    val maxLimit: Double?,
    val enMantenimiento: Boolean?,
    val dataPoints: List<DataPoint>?
)

data class DataPoint(
    val timestamp: String?,
    val value: Double?,
    val isValid: Boolean?
)

// ============ RAINFALL REPORT ============

data class RainfallReportResponse(
    val titulo: String,
    val tipo: String,
    val periodoInicio: String?,
    val periodoFin: String?,
    val periodoInicioLocal: String,
    val periodoFinLocal: String,
    val generado: String?,
    val totalEstaciones: Int,
    val estacionesConLluvia: Int,
    val subcuencas: List<SubcuencaReporte>
)

data class SubcuencaReporte(
    val subcuenca: String,
    val estaciones: List<EstacionLluvia>,
    val promedioMm: Double
)

data class EstacionLluvia(
    val idAsignado: String,
    val dcpId: String?,
    val nombre: String,
    val cuenca: String?,
    val subcuenca: String?,
    val acumuladoMm: Double,
    val horasConDato: Int?
)
