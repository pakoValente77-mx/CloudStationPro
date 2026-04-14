package mx.cfe.grijalva.pih.ui.screens

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch
import mx.cfe.grijalva.pih.data.model.DataPoint
import mx.cfe.grijalva.pih.data.model.DataSeries
import mx.cfe.grijalva.pih.data.service.StationDataService
import mx.cfe.grijalva.pih.ui.theme.*
import java.text.SimpleDateFormat
import java.util.*

private val utcZone = TimeZone.getTimeZone("UTC")

private fun gmtParsers(): List<SimpleDateFormat> = listOf(
    SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.getDefault()),
    SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS", Locale.getDefault()),
    SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault())
).onEach { it.timeZone = utcZone }

private fun localDisplayFormat(): SimpleDateFormat =
    SimpleDateFormat("dd/MM HH:mm", Locale.getDefault())

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StationDataScreen(service: StationDataService) {
    val stations by service.stations.collectAsState()
    val variables by service.variables.collectAsState()
    val analysisData by service.analysisData.collectAsState()
    val isLoadingStations by service.isLoadingStations.collectAsState()
    val isLoadingVariables by service.isLoadingVariables.collectAsState()
    val isLoadingData by service.isLoadingData.collectAsState()
    val errorMessage by service.errorMessage.collectAsState()
    val scope = rememberCoroutineScope()

    var selectedStationId by remember { mutableStateOf<String?>(null) }
    var selectedStationName by remember { mutableStateOf("Seleccionar estación") }
    var selectedVariable by remember { mutableStateOf<String?>(null) }
    var onlyCfe by remember { mutableStateOf(true) }
    var showStationPicker by remember { mutableStateOf(false) }
    var searchQuery by remember { mutableStateOf("") }

    // Quick period options
    var selectedPeriod by remember { mutableStateOf("24h") }
    val periods = listOf("6h", "12h", "24h", "3d", "7d")

    LaunchedEffect(Unit) {
        service.loadStations(onlyCfe)
    }

    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .background(PihBackground),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        item {
            Text(
                "Análisis de Datos",
                fontSize = 24.sp,
                fontWeight = FontWeight.Bold,
                color = PihTextPrimary
            )
        }

        // CFE toggle
        item {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Text("Solo CFE", fontSize = 14.sp, color = PihTextPrimary)
                Switch(
                    checked = onlyCfe,
                    onCheckedChange = {
                        onlyCfe = it
                        scope.launch { service.loadStations(it) }
                    },
                    colors = SwitchDefaults.colors(
                        checkedThumbColor = PihPurple,
                        checkedTrackColor = PihPurple.copy(alpha = 0.3f)
                    )
                )
            }
        }

        // Station picker
        item {
            Card(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { showStationPicker = true },
                colors = CardDefaults.cardColors(containerColor = PihCard),
                shape = RoundedCornerShape(8.dp)
            ) {
                Row(
                    modifier = Modifier.padding(16.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Icon(Icons.Default.LocationOn, contentDescription = null, tint = PihPurple)
                    Spacer(modifier = Modifier.width(12.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text("Estación", fontSize = 11.sp, color = PihTextSecondary)
                        Text(selectedStationName, fontSize = 14.sp, color = PihTextPrimary)
                    }
                    if (isLoadingStations) {
                        CircularProgressIndicator(modifier = Modifier.size(20.dp), color = PihCyan, strokeWidth = 2.dp)
                    } else {
                        Icon(Icons.Default.ArrowDropDown, contentDescription = null, tint = PihTextSecondary)
                    }
                }
            }
        }

        // Variable picker (only if station selected)
        if (variables.isNotEmpty()) {
            item {
                Text("Variable", fontSize = 13.sp, color = PihTextSecondary)
            }
            item {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    variables.forEach { variable ->
                        Card(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    selectedVariable = variable.variable
                                    val hours = when (selectedPeriod) {
                                        "6h" -> 6; "12h" -> 12; "24h" -> 24
                                        "3d" -> 72; "7d" -> 168; else -> 24
                                    }
                                    val end = Date()
                                    val start = Date(end.time - hours * 3600000L)
                                    scope.launch {
                                        service.loadAnalysisData(
                                            listOf(selectedStationId ?: ""),
                                            variable.variable ?: "",
                                            start, end
                                        )
                                    }
                                },
                            colors = CardDefaults.cardColors(
                                containerColor = if (variable.variable == selectedVariable) PihPurple.copy(alpha = 0.2f) else PihCard
                            ),
                            shape = RoundedCornerShape(8.dp)
                        ) {
                            Row(
                                modifier = Modifier.padding(12.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    variable.displayName ?: variable.variable ?: "—",
                                    fontSize = 13.sp,
                                    color = PihTextPrimary,
                                    modifier = Modifier.weight(1f)
                                )
                                if (variable.hasData == true) {
                                    Box(
                                        modifier = Modifier
                                            .size(8.dp)
                                            .background(PihGreen, RoundedCornerShape(4.dp))
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }

        // Period selector
        if (selectedVariable != null) {
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    periods.forEach { period ->
                        FilterChip(
                            selected = period == selectedPeriod,
                            onClick = {
                                selectedPeriod = period
                                val hours = when (period) {
                                    "6h" -> 6; "12h" -> 12; "24h" -> 24
                                    "3d" -> 72; "7d" -> 168; else -> 24
                                }
                                val end = Date()
                                val start = Date(end.time - hours * 3600000L)
                                scope.launch {
                                    service.loadAnalysisData(
                                        listOf(selectedStationId ?: ""),
                                        selectedVariable ?: "",
                                        start, end
                                    )
                                }
                            },
                            label = { Text(period, fontSize = 12.sp) },
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

        // Loading
        if (isLoadingData) {
            item {
                Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator(color = PihCyan, modifier = Modifier.size(32.dp))
                }
            }
        }

        // Error
        errorMessage?.let { msg ->
            item {
                Card(
                    colors = CardDefaults.cardColors(containerColor = PihRed.copy(alpha = 0.15f)),
                    shape = RoundedCornerShape(8.dp)
                ) {
                    Text(msg, color = PihRed, fontSize = 13.sp, modifier = Modifier.padding(12.dp))
                }
            }
        }

        // Chart
        analysisData?.let { data ->
            data.series?.forEach { series ->
                item {
                    AnalysisChart(
                        series = series,
                        variable = selectedVariable ?: "",
                        isPrecipitation = selectedVariable?.contains("precipitación", ignoreCase = true) == true
                    )
                }

                // Statistics
                item {
                    AnalysisStats(series)
                }

                // Data table
                item {
                    Text(
                        "Datos (últimos 100)",
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        color = PihTextPrimary
                    )
                }
                item { DataTableHeader() }

                val points = series.dataPoints?.takeLast(100) ?: emptyList()
                items(points) { point ->
                    DataTableRow(point)
                }
            }
        }
    }

    // Station picker bottom sheet
    if (showStationPicker) {
        ModalBottomSheet(
            onDismissRequest = { showStationPicker = false },
            containerColor = PihBackground,
            contentColor = PihTextPrimary
        ) {
            Column(modifier = Modifier.padding(16.dp)) {
                OutlinedTextField(
                    value = searchQuery,
                    onValueChange = { searchQuery = it },
                    label = { Text("Buscar estación") },
                    leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = PihPurple,
                        unfocusedBorderColor = PihDivider,
                        cursorColor = PihCyan,
                        focusedTextColor = PihTextPrimary,
                        unfocusedTextColor = PihTextPrimary
                    )
                )
                Spacer(modifier = Modifier.height(8.dp))

                val filtered = stations.filter {
                    searchQuery.isBlank() ||
                            (it.name ?: "").contains(searchQuery, ignoreCase = true) ||
                            (it.id ?: "").contains(searchQuery, ignoreCase = true)
                }

                LazyColumn(
                    modifier = Modifier.heightIn(max = 400.dp),
                    verticalArrangement = Arrangement.spacedBy(2.dp)
                ) {
                    items(filtered) { station ->
                        Card(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    selectedStationId = station.id
                                    selectedStationName = station.name ?: station.id ?: "—"
                                    selectedVariable = null
                                    showStationPicker = false
                                    scope.launch { service.loadVariables(station.id ?: "") }
                                },
                            colors = CardDefaults.cardColors(containerColor = PihCard),
                            shape = RoundedCornerShape(4.dp)
                        ) {
                            Text(
                                "${station.name ?: "—"} (${station.id ?: "—"})",
                                fontSize = 13.sp,
                                color = PihTextPrimary,
                                modifier = Modifier.padding(12.dp)
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun AnalysisChart(series: DataSeries, variable: String, isPrecipitation: Boolean) {
    val points = series.dataPoints?.filter { it.isValid == true } ?: return
    if (points.isEmpty()) return

    val values = points.mapNotNull { it.value }
    if (values.isEmpty()) return
    val minVal = values.min()
    val maxVal = values.max()
    val range = if (maxVal - minVal < 0.001) 1.0 else maxVal - minVal

    var tooltipIndex by remember { mutableStateOf<Int?>(null) }

    val displayFormat = remember { localDisplayFormat() }
    val parsers = remember { gmtParsers() }

    Card(
        modifier = Modifier
            .fillMaxWidth()
            .height(200.dp),
        colors = CardDefaults.cardColors(containerColor = PihCard),
        shape = RoundedCornerShape(12.dp)
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Text(
                series.stationName ?: "—",
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold,
                color = PihTextPrimary
            )
            Text(variable, fontSize = 11.sp, color = PihTextSecondary)
            Spacer(modifier = Modifier.height(8.dp))

            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f)
            ) {
                Canvas(
                    modifier = Modifier
                        .fillMaxSize()
                        .pointerInput(points) {
                            detectTapGestures { offset ->
                                val count = points.size
                                if (count == 0) return@detectTapGestures
                                val idx = if (isPrecipitation) {
                                    ((offset.x / size.width) * count).toInt().coerceIn(0, count - 1)
                                } else {
                                    ((offset.x / size.width) * (count - 1).coerceAtLeast(1) + 0.5f).toInt().coerceIn(0, count - 1)
                                }
                                tooltipIndex = if (tooltipIndex == idx) null else idx
                            }
                        }
                ) {
                val width = size.width
                val height = size.height
                val count = points.size

                if (isPrecipitation) {
                    // Bar chart for precipitation
                    val barWidth = (width / count).coerceAtMost(12f)
                    points.forEachIndexed { index, point ->
                        val value = point.value ?: 0.0
                        val barHeight = ((value - minVal) / range * height * 0.9).toFloat()
                        val x = (index.toFloat() / count) * width
                        drawRect(
                            color = PihCyan,
                            topLeft = Offset(x, height - barHeight),
                            size = Size(barWidth * 0.8f, barHeight)
                        )
                    }
                } else {
                    // Line chart with gradient fill
                    val path = Path()
                    points.forEachIndexed { index, point ->
                        val value = point.value ?: 0.0
                        val x = (index.toFloat() / (count - 1).coerceAtLeast(1)) * width
                        val y = height - ((value - minVal) / range * height * 0.9).toFloat()
                        if (index == 0) path.moveTo(x, y) else path.lineTo(x, y)
                    }
                    drawPath(path, color = PihCyan, style = Stroke(width = 2f))

                    // Fill
                    val fillPath = Path().apply {
                        addPath(path)
                        lineTo(width, height)
                        lineTo(0f, height)
                        close()
                    }
                    drawPath(
                        fillPath,
                        brush = Brush.verticalGradient(
                            colors = listOf(PihCyan.copy(alpha = 0.3f), PihCyan.copy(alpha = 0.01f))
                        )
                    )
                }

                    // Tooltip crosshair
                    tooltipIndex?.let { idx ->
                        if (idx in points.indices) {
                            val value = points[idx].value ?: 0.0
                            val tx = if (isPrecipitation) {
                                (idx.toFloat() / count) * width
                            } else {
                                (idx.toFloat() / (count - 1).coerceAtLeast(1)) * width
                            }
                            val ty = height - ((value - minVal) / range * height * 0.9).toFloat()
                            drawLine(Color.White.copy(alpha = 0.4f), Offset(tx, 0f), Offset(tx, height), 1f)
                            drawCircle(PihCyan, 5f, Offset(tx, ty))
                            drawCircle(Color.White, 3f, Offset(tx, ty))
                        }
                    }
                }

                // Tooltip popup
                tooltipIndex?.let { idx ->
                    if (idx in points.indices) {
                        val pt = points[idx]
                        val ts = pt.timestamp?.let { ts ->
                            parsers.firstNotNullOfOrNull { p ->
                                try { p.parse(ts) } catch (_: Exception) { null }
                            }?.let { displayFormat.format(it) } ?: ts.takeLast(11).take(5)
                        } ?: "—"
                        val v = pt.value?.let { String.format("%.2f", it) } ?: "—"

                        Card(
                            modifier = Modifier
                                .align(Alignment.TopCenter)
                                .padding(top = 2.dp),
                            colors = CardDefaults.cardColors(containerColor = PihSurface.copy(alpha = 0.95f)),
                            shape = RoundedCornerShape(6.dp),
                            elevation = CardDefaults.cardElevation(defaultElevation = 4.dp)
                        ) {
                            Row(
                                modifier = Modifier.padding(horizontal = 10.dp, vertical = 5.dp),
                                horizontalArrangement = Arrangement.spacedBy(8.dp)
                            ) {
                                Text(ts, fontSize = 10.sp, fontWeight = FontWeight.Bold, color = PihTextPrimary)
                                Text(v, fontSize = 10.sp, color = PihCyan, fontFamily = FontFamily.Monospace)
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun AnalysisStats(series: DataSeries) {
    val validPoints = series.dataPoints?.filter { it.isValid == true }?.mapNotNull { it.value } ?: return
    if (validPoints.isEmpty()) return

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        StatCard("Mínimo", String.format("%.2f", validPoints.min()), PihBlue, Modifier.weight(1f))
        StatCard("Máximo", String.format("%.2f", validPoints.max()), PihRed, Modifier.weight(1f))
        StatCard("Promedio", String.format("%.2f", validPoints.average()), PihGreen, Modifier.weight(1f))
        StatCard("Puntos", "${validPoints.size}", PihTextSecondary, Modifier.weight(1f))
    }
}

@Composable
fun StatCard(label: String, value: String, color: Color, modifier: Modifier = Modifier) {
    Card(
        modifier = modifier,
        colors = CardDefaults.cardColors(containerColor = PihCard),
        shape = RoundedCornerShape(8.dp)
    ) {
        Column(
            modifier = Modifier.padding(8.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(label, fontSize = 10.sp, color = PihTextSecondary)
            Text(
                value,
                fontSize = 14.sp,
                fontWeight = FontWeight.Bold,
                color = color,
                fontFamily = FontFamily.Monospace
            )
        }
    }
}

@Composable
fun DataTableHeader() {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(PihSurface, RoundedCornerShape(4.dp))
            .padding(horizontal = 12.dp, vertical = 6.dp)
    ) {
        Text("Fecha/Hora", fontSize = 11.sp, fontWeight = FontWeight.Bold, color = PihTextSecondary, modifier = Modifier.weight(1.5f))
        Text("Valor", fontSize = 11.sp, fontWeight = FontWeight.Bold, color = PihTextSecondary, modifier = Modifier.weight(1f), textAlign = TextAlign.End)
        Text("✓", fontSize = 11.sp, fontWeight = FontWeight.Bold, color = PihTextSecondary, modifier = Modifier.weight(0.4f), textAlign = TextAlign.Center)
    }
}

@Composable
fun DataTableRow(point: DataPoint) {
    val displayFormat = remember { localDisplayFormat() }
    val parsers = remember { gmtParsers() }

    val formattedTime = point.timestamp?.let { ts ->
        parsers.firstNotNullOfOrNull { parser ->
            try { parser.parse(ts) } catch (_: Exception) { null }
        }?.let { displayFormat.format(it) } ?: ts.takeLast(11).take(5)
    } ?: "—"

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 3.dp)
    ) {
        Text(
            formattedTime,
            fontSize = 11.sp,
            color = PihTextSecondary,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.weight(1.5f)
        )
        Text(
            point.value?.let { String.format("%.2f", it) } ?: "—",
            fontSize = 11.sp,
            color = PihCyan,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.End
        )
        Text(
            if (point.isValid == true) "✓" else "✗",
            fontSize = 11.sp,
            color = if (point.isValid == true) PihGreen else PihRed,
            modifier = Modifier.weight(0.4f),
            textAlign = TextAlign.Center
        )
    }
    Divider(thickness = 0.5.dp, color = PihDivider.copy(alpha = 0.2f))
}
