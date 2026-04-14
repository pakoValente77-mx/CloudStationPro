package mx.cfe.grijalva.pih.data.network

import mx.cfe.grijalva.pih.data.model.*
import okhttp3.MultipartBody
import okhttp3.RequestBody
import retrofit2.Response
import retrofit2.http.*

interface ApiService {

    // Auth
    @POST("api/auth/login")
    suspend fun login(@Body request: LoginRequest): Response<LoginResponse>

    // Chat REST
    @GET("Chat/Rooms")
    suspend fun getRooms(@Header("Authorization") auth: String): Response<List<ChatRoomResponse>>

    @GET("Chat/History")
    suspend fun getHistory(
        @Header("Authorization") auth: String,
        @Query("room") room: String
    ): Response<List<ChatMessage>>

    @GET("Chat/OnlineUsers")
    suspend fun getOnlineUsers(@Header("Authorization") auth: String): Response<List<OnlineUser>>

    @Multipart
    @POST("Chat/UploadFile")
    suspend fun uploadFile(
        @Header("Authorization") auth: String,
        @Part("room") room: RequestBody,
        @Part file: MultipartBody.Part
    ): Response<Void>

    // Device Registration
    @POST("api/MobileApi/RegisterDevice")
    suspend fun registerDevice(
        @Header("Authorization") auth: String,
        @Body registration: DeviceRegistration
    ): Response<Void>

    @POST("api/MobileApi/UnregisterDevice")
    suspend fun unregisterDevice(
        @Header("Authorization") auth: String,
        @Body registration: DeviceRegistration
    ): Response<Void>

    // FunVasos
    @GET("FunVasos/GetCascadeData")
    suspend fun getCascadeData(@Header("Authorization") auth: String): Response<CascadeResponse>

    @GET("FunVasos/GetData")
    suspend fun getFunVasosData(
        @Header("Authorization") auth: String,
        @Query("fechaInicio") fechaInicio: String?,
        @Query("fechaFin") fechaFin: String?
    ): Response<FunVasosResponse>

    // Map
    @GET("Map/GetVariables")
    suspend fun getMapVariables(
        @Header("Authorization") auth: String
    ): Response<List<String>>

    @GET("Map/GetMapData")
    suspend fun getMapData(
        @Header("Authorization") auth: String,
        @Query("variable") variable: String,
        @Query("onlyCfe") onlyCfe: Boolean = true
    ): Response<List<StationMapData>>

    // Station Data
    @GET("DataAnalysis/GetStations")
    suspend fun getStations(
        @Header("Authorization") auth: String,
        @Query("onlyCfe") onlyCfe: Boolean = true
    ): Response<List<StationInfo>>

    @GET("DataAnalysis/GetStationVariables")
    suspend fun getStationVariables(
        @Header("Authorization") auth: String,
        @Query("stationId") stationId: String
    ): Response<List<StationVariable>>

    @POST("DataAnalysis/GetAnalysisData")
    suspend fun getAnalysisData(
        @Header("Authorization") auth: String,
        @Body request: AnalysisRequest
    ): Response<DataAnalysisResponse>

    // Rainfall Report
    @GET("api/lluvia/reporte")
    suspend fun getRainfallReport(
        @Header("Authorization") auth: String,
        @Query("tipo") tipo: String,
        @Query("fecha") fecha: String? = null
    ): Response<RainfallReportResponse>
}
