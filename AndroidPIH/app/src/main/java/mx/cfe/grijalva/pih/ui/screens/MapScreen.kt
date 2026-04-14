package mx.cfe.grijalva.pih.ui.screens

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.Drawable
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import kotlinx.coroutines.launch
import mx.cfe.grijalva.pih.data.model.StationMapData
import mx.cfe.grijalva.pih.data.service.MapService
import mx.cfe.grijalva.pih.ui.theme.*
import org.osmdroid.config.Configuration
import org.osmdroid.tileprovider.tilesource.TileSourceFactory
import org.osmdroid.util.GeoPoint
import org.osmdroid.views.MapView
import org.osmdroid.views.overlay.Marker

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MapScreen(service: MapService) {
    val stations by service.stations.collectAsState()
    val selectedVariable by service.selectedVariable.collectAsState()
    val availableVariables by service.availableVariables.collectAsState()
    val isLoading by service.isLoading.collectAsState()
    val errorMessage by service.errorMessage.collectAsState()
    val scope = rememberCoroutineScope()
    val context = LocalContext.current

    var selectedStation by remember { mutableStateOf<StationMapData?>(null) }
    var mapView by remember { mutableStateOf<MapView?>(null) }

    LaunchedEffect(Unit) {
        Configuration.getInstance().userAgentValue = "mx.cfe.grijalva.pih"
        service.loadMapData()
    }

    Box(modifier = Modifier.fillMaxSize()) {
        // Map
        AndroidView(
            modifier = Modifier.fillMaxSize(),
            factory = { ctx ->
                MapView(ctx).apply {
                    setTileSource(TileSourceFactory.MAPNIK)
                    setMultiTouchControls(true)
                    controller.setZoom(8.0)
                    controller.setCenter(GeoPoint(17.0, -93.0))
                    mapView = this
                }
            },
            update = { map ->
                map.overlays.clear()
                stations.forEach { station ->
                    if (station.lat != null && station.lon != null) {
                        val markerIcon = createStationMarker(
                            context = map.context,
                            station = station,
                            variable = selectedVariable
                        )
                        val marker = Marker(map).apply {
                            position = GeoPoint(station.lat!!, station.lon!!)
                            title = station.nombre ?: station.id
                            snippet = "${station.variableActual}: ${station.valorActual ?: "—"}"
                            icon = markerIcon
                            setAnchor(Marker.ANCHOR_CENTER, 1.0f)
                            setOnMarkerClickListener { _, _ ->
                                selectedStation = station
                                true
                            }
                        }
                        map.overlays.add(marker)
                    }
                }
                map.invalidate()
            }
        )

        // Top bar with variable selector
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(12.dp)
        ) {
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = PihSurface.copy(alpha = 0.95f)),
                shape = RoundedCornerShape(12.dp)
            ) {
                Column(modifier = Modifier.padding(8.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            "Mapa de Estaciones",
                            fontSize = 16.sp,
                            fontWeight = FontWeight.Bold,
                            color = PihTextPrimary
                        )
                        if (isLoading) {
                            CircularProgressIndicator(
                                modifier = Modifier.size(20.dp),
                                color = PihCyan,
                                strokeWidth = 2.dp
                            )
                        } else {
                            Text(
                                "${stations.size} estaciones",
                                fontSize = 12.sp,
                                color = PihTextSecondary
                            )
                        }
                    }

                    Spacer(modifier = Modifier.height(8.dp))

                    LazyRow(
                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        items(availableVariables) { variable ->
                            FilterChip(
                                selected = variable == selectedVariable,
                                onClick = {
                                    scope.launch { service.changeVariable(variable) }
                                },
                                label = {
                                    Text(
                                        variable.replace("_", " ").replaceFirstChar { it.uppercase() },
                                        fontSize = 11.sp
                                    )
                                },
                                colors = FilterChipDefaults.filterChipColors(
                                    selectedContainerColor = PihPurple,
                                    selectedLabelColor = PihTextPrimary,
                                    containerColor = PihCard,
                                    labelColor = PihTextSecondary
                                )
                            )
                        }
                    }
                }
            }
        }

        // Error message
        errorMessage?.let { msg ->
            Card(
                modifier = Modifier
                    .align(Alignment.TopCenter)
                    .padding(top = 120.dp, start = 16.dp, end = 16.dp),
                colors = CardDefaults.cardColors(containerColor = PihRed.copy(alpha = 0.9f)),
                shape = RoundedCornerShape(8.dp)
            ) {
                Text(msg, color = PihTextPrimary, fontSize = 13.sp, modifier = Modifier.padding(12.dp))
            }
        }

        // Station detail card at bottom
        selectedStation?.let { station ->
            StationDetailCard(
                station = station,
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .padding(12.dp),
                onDismiss = { selectedStation = null }
            )
        }
    }
}

@Composable
fun StationDetailCard(
    station: StationMapData,
    modifier: Modifier = Modifier,
    onDismiss: () -> Unit
) {
    val statusColor = when (station.estatusColor?.lowercase()) {
        "green" -> PihGreen
        "yellow" -> PihYellow
        "red" -> PihRed
        "blue" -> PihBlue
        else -> PihTextSecondary
    }

    Card(
        modifier = modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = PihSurface.copy(alpha = 0.97f)),
        shape = RoundedCornerShape(16.dp),
        elevation = CardDefaults.cardElevation(defaultElevation = 8.dp)
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.Top
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(
                            modifier = Modifier
                                .size(10.dp)
                                .clip(CircleShape)
                                .background(statusColor)
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                        Text(
                            station.nombre ?: "—",
                            fontSize = 16.sp,
                            fontWeight = FontWeight.Bold,
                            color = PihTextPrimary
                        )
                    }
                    Text(
                        "ID: ${station.id ?: "—"}  •  DCP: ${station.dcpId ?: "—"}",
                        fontSize = 11.sp,
                        color = PihTextSecondary,
                        fontFamily = FontFamily.Monospace
                    )
                }
                IconButton(onClick = onDismiss, modifier = Modifier.size(24.dp)) {
                    Icon(Icons.Default.Close, contentDescription = "Cerrar", tint = PihTextSecondary, modifier = Modifier.size(18.dp))
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceEvenly
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(station.variableActual ?: "—", fontSize = 10.sp, color = PihTextSecondary)
                    Text(
                        station.valorActual?.let { String.format("%.2f", it) } ?: "—",
                        fontSize = 20.sp,
                        fontWeight = FontWeight.Bold,
                        color = statusColor,
                        fontFamily = FontFamily.Monospace
                    )
                }
                if (station.valorAuxiliar != null) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text("Auxiliar", fontSize = 10.sp, color = PihTextSecondary)
                        Text(
                            String.format("%.2f", station.valorAuxiliar),
                            fontSize = 20.sp,
                            fontWeight = FontWeight.Bold,
                            color = PihCyan,
                            fontFamily = FontFamily.Monospace
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(8.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Text(
                    "Lat: ${station.lat?.let { String.format("%.4f", it) } ?: "—"}  Lon: ${station.lon?.let { String.format("%.4f", it) } ?: "—"}",
                    fontSize = 10.sp,
                    color = PihTextSecondary,
                    fontFamily = FontFamily.Monospace
                )
                if (station.enMantenimiento == true) {
                    Text("🔧 Mantenimiento", fontSize = 10.sp, color = PihOrange)
                }
            }

            station.ultimaTx?.let { tx ->
                Text(
                    "Última Tx: $tx",
                    fontSize = 10.sp,
                    color = PihTextSecondary,
                    modifier = Modifier.padding(top = 4.dp)
                )
            }
        }
    }
}

/**
 * Creates a custom station marker bitmap with vector-drawn icons.
 * - Rounded-rect card with shadow
 * - Variable icon drawn with Canvas paths
 * - Value text with status color accent
 */
fun createStationMarker(
    context: android.content.Context,
    station: StationMapData,
    variable: String
): Drawable {
    val d = context.resources.displayMetrics.density
    val w = (56 * d).toInt()
    val h = (64 * d).toInt()
    val bitmap = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)

    val statusColor = when (station.estatusColor?.lowercase()) {
        "green" -> 0xFF2E7D32.toInt()
        "yellow" -> 0xFFF9A825.toInt()
        "red" -> 0xFFC62828.toInt()
        "blue" -> 0xFF1565C0.toInt()
        else -> 0xFF546E7A.toInt()
    }
    val maintenanceMode = station.enMantenimiento == true
    val accentColor = if (maintenanceMode) 0xFFEF6C00.toInt() else statusColor

    val cx = w / 2f
    val cardW = 48f * d
    val cardH = 40f * d
    val cardLeft = (w - cardW) / 2f
    val cardTop = 2f * d
    val cardRadius = 10f * d

    // --- Shadow ---
    val shadowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0x00000000
        setShadowLayer(4f * d, 0f, 2f * d, 0x55000000)
        style = Paint.Style.FILL
    }
    canvas.drawRoundRect(
        RectF(cardLeft + 1f * d, cardTop + 1f * d, cardLeft + cardW, cardTop + cardH),
        cardRadius, cardRadius, shadowPaint
    )

    // --- Card background ---
    val cardPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0xFF1E293B.toInt() // dark slate
        style = Paint.Style.FILL
    }
    val cardRect = RectF(cardLeft, cardTop, cardLeft + cardW, cardTop + cardH)
    canvas.drawRoundRect(cardRect, cardRadius, cardRadius, cardPaint)

    // --- Left accent stripe ---
    canvas.save()
    val stripePath = android.graphics.Path().apply {
        addRoundRect(
            RectF(cardLeft, cardTop, cardLeft + 5f * d, cardTop + cardH),
            floatArrayOf(cardRadius, cardRadius, 0f, 0f, 0f, 0f, cardRadius, cardRadius),
            android.graphics.Path.Direction.CW
        )
    }
    val stripePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = accentColor
        style = Paint.Style.FILL
    }
    canvas.drawPath(stripePath, stripePaint)
    canvas.restore()

    // --- Pointer triangle ---
    val pointerPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0xFF1E293B.toInt()
        style = Paint.Style.FILL
    }
    val pointerPath = android.graphics.Path().apply {
        moveTo(cx - 5f * d, cardTop + cardH - 1f)
        lineTo(cx, cardTop + cardH + 8f * d)
        lineTo(cx + 5f * d, cardTop + cardH - 1f)
        close()
    }
    canvas.drawPath(pointerPath, pointerPaint)

    // --- Icon area (left side of card) ---
    val iconCx = cardLeft + 16f * d
    val iconCy = cardTop + cardH / 2f

    // Icon circle background
    val iconBgPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = accentColor
        style = Paint.Style.FILL
        alpha = 40
    }
    canvas.drawCircle(iconCx, iconCy, 10f * d, iconBgPaint)

    // Draw variable icon with Canvas paths
    val iconPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0xFFFFFFFF.toInt()
        style = Paint.Style.FILL
    }
    val iconStroke = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0xFFFFFFFF.toInt()
        style = Paint.Style.STROKE
        strokeWidth = 1.5f * d
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }

    drawVariableIcon(canvas, variable, iconCx, iconCy, d, iconPaint, iconStroke)

    // --- Value text (right side) ---
    val valText = station.valorActual?.let { valor ->
        if (valor >= 1000) String.format("%.0f", valor)
        else if (valor >= 100) String.format("%.0f", valor)
        else if (valor >= 10) String.format("%.1f", valor)
        else String.format("%.1f", valor)
    } ?: "—"

    val valPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = 0xFFFFFFFF.toInt()
        textSize = 11f * d
        textAlign = Paint.Align.CENTER
        typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
    }
    val textX = cardLeft + 16f * d + (cardW - 16f * d) / 2f + 2f * d
    canvas.drawText(valText, textX, iconCy + 4f * d, valPaint)

    // --- Maintenance badge ---
    if (maintenanceMode) {
        val badgePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFFEF6C00.toInt()
            style = Paint.Style.FILL
        }
        canvas.drawCircle(cardLeft + cardW - 6f * d, cardTop + 6f * d, 4f * d, badgePaint)
        val mPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFFFFFFFF.toInt()
            textSize = 5f * d
            textAlign = Paint.Align.CENTER
            typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
        }
        canvas.drawText("M", cardLeft + cardW - 6f * d, cardTop + 8f * d, mPaint)
    }

    // --- Border glow for active status ---
    val glowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = accentColor
        style = Paint.Style.STROKE
        strokeWidth = 1.2f * d
        alpha = 140
    }
    canvas.drawRoundRect(cardRect, cardRadius, cardRadius, glowPaint)

    return BitmapDrawable(context.resources, bitmap)
}

/** Draws a vector icon for the given variable using Canvas primitives. */
private fun drawVariableIcon(
    canvas: Canvas,
    variable: String,
    cx: Float,
    cy: Float,
    d: Float,
    fill: Paint,
    stroke: Paint
) {
    when {
        // Precipitación — water drop
        variable.contains("precipit", ignoreCase = true) -> {
            val drop = android.graphics.Path().apply {
                moveTo(cx, cy - 8f * d)
                cubicTo(cx - 7f * d, cy, cx - 6f * d, cy + 7f * d, cx, cy + 8f * d)
                cubicTo(cx + 6f * d, cy + 7f * d, cx + 7f * d, cy, cx, cy - 8f * d)
                close()
            }
            fill.alpha = 230
            canvas.drawPath(drop, fill)
        }
        // Nivel — three wave lines
        variable.contains("nivel", ignoreCase = true) || variable.contains("cota", ignoreCase = true) -> {
            for (i in -1..1) {
                val wy = cy + i * 5f * d
                val wave = android.graphics.Path().apply {
                    moveTo(cx - 7f * d, wy)
                    cubicTo(cx - 4f * d, wy - 3f * d, cx - 1f * d, wy + 3f * d, cx + 2f * d, wy)
                    cubicTo(cx + 4f * d, wy - 2f * d, cx + 5f * d, wy + 2f * d, cx + 7f * d, wy)
                }
                canvas.drawPath(wave, stroke)
            }
        }
        // Temperatura — thermometer
        variable.contains("temp", ignoreCase = true) -> {
            // Stem
            val stemRect = RectF(cx - 2f * d, cy - 8f * d, cx + 2f * d, cy + 2f * d)
            canvas.drawRoundRect(stemRect, 2f * d, 2f * d, stroke)
            // Bulb
            canvas.drawCircle(cx, cy + 5f * d, 4f * d, stroke)
            // Mercury fill
            val mercuryFill = Paint(fill).apply { alpha = 200 }
            canvas.drawCircle(cx, cy + 5f * d, 2.5f * d, mercuryFill)
            canvas.drawRect(cx - 1f * d, cy - 3f * d, cx + 1f * d, cy + 3f * d, mercuryFill)
            // Tick marks
            for (j in 0..2) {
                val ty = cy - 6f * d + j * 3f * d
                canvas.drawLine(cx + 2f * d, ty, cx + 4f * d, ty, stroke)
            }
        }
        // Humedad — cloud with drop
        variable.contains("humedad", ignoreCase = true) -> {
            // Cloud body
            canvas.drawCircle(cx - 3f * d, cy - 2f * d, 4f * d, stroke)
            canvas.drawCircle(cx + 2f * d, cy - 3f * d, 5f * d, stroke)
            canvas.drawCircle(cx + 6f * d, cy - 1f * d, 3f * d, stroke)
            // Small drop below
            val miniDrop = android.graphics.Path().apply {
                moveTo(cx, cy + 4f * d)
                cubicTo(cx - 3f * d, cy + 7f * d, cx - 2f * d, cy + 9f * d, cx, cy + 9f * d)
                cubicTo(cx + 2f * d, cy + 9f * d, cx + 3f * d, cy + 7f * d, cx, cy + 4f * d)
                close()
            }
            fill.alpha = 200
            canvas.drawPath(miniDrop, fill)
        }
        // Viento — wind curves
        variable.contains("viento", ignoreCase = true) -> {
            for (i in -1..1) {
                val wy = cy + i * 5f * d
                val wind = android.graphics.Path().apply {
                    moveTo(cx - 7f * d, wy)
                    quadTo(cx, wy - 3f * d, cx + 5f * d + i * d, wy - 1f * d)
                }
                canvas.drawPath(wind, stroke)
            }
        }
        // Batería
        variable.contains("bater", ignoreCase = true) -> {
            val bRect = RectF(cx - 5f * d, cy - 6f * d, cx + 5f * d, cy + 6f * d)
            canvas.drawRoundRect(bRect, 1.5f * d, 1.5f * d, stroke)
            // Terminal
            canvas.drawRect(cx - 2f * d, cy - 8f * d, cx + 2f * d, cy - 6f * d, fill)
            // Charge level bars
            for (k in 0..2) {
                val by = cy + 3f * d - k * 3.5f * d
                val barFill = Paint(fill).apply { alpha = 180 }
                canvas.drawRect(cx - 3f * d, by, cx + 3f * d, by + 2f * d, barFill)
            }
        }
        // Default — signal/antenna
        else -> {
            // Antenna pole
            canvas.drawLine(cx, cy + 8f * d, cx, cy - 2f * d, stroke)
            // Signal arcs
            for (r in 1..3) {
                val arcRect = RectF(
                    cx - r * 3f * d, cy - 2f * d - r * 3f * d,
                    cx + r * 3f * d, cy - 2f * d + r * 3f * d
                )
                val arcStroke = Paint(stroke).apply { alpha = 255 - r * 50 }
                canvas.drawArc(arcRect, -150f, 120f, false, arcStroke)
            }
        }
    }
}
