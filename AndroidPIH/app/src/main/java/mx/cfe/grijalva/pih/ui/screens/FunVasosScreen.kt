package mx.cfe.grijalva.pih.ui.screens

import androidx.compose.animation.core.*
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
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
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch
import mx.cfe.grijalva.pih.data.model.CascadePresa
import mx.cfe.grijalva.pih.data.model.FunVasosDatoHorario
import mx.cfe.grijalva.pih.data.model.FunVasosResumenPresa
import mx.cfe.grijalva.pih.data.service.FunVasosService
import mx.cfe.grijalva.pih.ui.theme.*
import java.text.SimpleDateFormat
import java.util.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FunVasosScreen(service: FunVasosService) {
    val cascadeData by service.cascadeData.collectAsState()
    val allPresas by service.allPresas.collectAsState()
    val fechasDisponibles by service.fechasDisponibles.collectAsState()
    val selectedFecha by service.selectedFecha.collectAsState()
    val isLoading by service.isLoading.collectAsState()
    val errorMessage by service.errorMessage.collectAsState()
    val scope = rememberCoroutineScope()

    var selectedPresaDetail by remember { mutableStateOf<FunVasosResumenPresa?>(null) }
    var showDetailSheet by remember { mutableStateOf(false) }
    var selectedPeriod by remember { mutableStateOf("Hoy") }
    val periods = listOf("Hoy", "Ayer", "3 días", "7 días")

    LaunchedEffect(Unit) {
        service.loadCascade()
        service.loadData()
        service.startAutoRefresh()
    }

    if (showDetailSheet && selectedPresaDetail != null) {
        PresaDetailSheet(
            presa = selectedPresaDetail!!,
            onDismiss = { showDetailSheet = false }
        )
    }

    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .background(PihBackground),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        // Title
        item {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column {
                    Text(
                        "FunVasos",
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold,
                        color = PihTextPrimary
                    )
                    Text(
                        "Sistema de Presas Grijalva",
                        fontSize = 13.sp,
                        color = PihTextSecondary
                    )
                }
                if (isLoading) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(24.dp),
                        color = PihCyan,
                        strokeWidth = 2.dp
                    )
                } else {
                    IconButton(onClick = {
                        scope.launch {
                            service.loadCascade()
                            service.loadData()
                        }
                    }) {
                        Icon(Icons.Default.Refresh, contentDescription = "Refresh", tint = PihCyan)
                    }
                }
            }
        }

        // Period selector
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                periods.forEach { period ->
                    FilterChip(
                        selected = period == selectedPeriod,
                        onClick = {
                            selectedPeriod = period
                            val fmt = SimpleDateFormat("yyyy-MM-dd", Locale.getDefault())
                            val cal = Calendar.getInstance()
                            val end = fmt.format(cal.time)
                            val start = when (period) {
                                "Ayer" -> { cal.add(Calendar.DAY_OF_YEAR, -1); fmt.format(cal.time) }
                                "3 días" -> { cal.add(Calendar.DAY_OF_YEAR, -2); fmt.format(cal.time) }
                                "7 días" -> { cal.add(Calendar.DAY_OF_YEAR, -6); fmt.format(cal.time) }
                                else -> end // Hoy
                            }
                            scope.launch { service.loadData(start, end) }
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

        // Date selector chips
        if (fechasDisponibles.isNotEmpty()) {
            item {
                LazyRow(
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    items(fechasDisponibles) { fecha ->
                        FilterChip(
                            selected = fecha == selectedFecha,
                            onClick = {
                                scope.launch { service.loadDataForDate(fecha) }
                            },
                            label = {
                                Text(
                                    fecha.takeLast(5), // MM-DD
                                    fontSize = 12.sp
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

        // Cascade Flow Diagram
        if (cascadeData.isNotEmpty()) {
            item {
                CascadeFlowDiagram(cascadeData)
            }
        }

        // Error
        errorMessage?.let { msg ->
            item {
                Card(
                    colors = CardDefaults.cardColors(containerColor = PihRed.copy(alpha = 0.15f)),
                    shape = RoundedCornerShape(8.dp)
                ) {
                    Text(
                        msg,
                        color = PihRed,
                        fontSize = 13.sp,
                        modifier = Modifier.padding(12.dp)
                    )
                }
            }
        }

        // Presa Cards
        if (allPresas.isNotEmpty()) {
            item {
                Text(
                    "Detalle por Presa",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = PihTextPrimary
                )
            }
            items(allPresas) { presa ->
                PresaCard(presa) {
                    selectedPresaDetail = presa
                    showDetailSheet = true
                }
            }
        }
    }
}

@Composable
fun CascadeFlowDiagram(presas: List<CascadePresa>) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = PihCard),
        shape = RoundedCornerShape(12.dp)
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                "Cascada del Grijalva",
                fontSize = 15.sp,
                fontWeight = FontWeight.Bold,
                color = PihCyan
            )
            Spacer(modifier = Modifier.height(12.dp))

            // Animated water flow
            val infiniteTransition = rememberInfiniteTransition(label = "cascade")
            val animOffset by infiniteTransition.animateFloat(
                initialValue = 0f,
                targetValue = 1f,
                animationSpec = infiniteRepeatable(
                    animation = tween(2000, easing = LinearEasing),
                    repeatMode = RepeatMode.Restart
                ),
                label = "waterFlow"
            )

            presas.forEachIndexed { index, presa ->
                // Dam box
                CascadeDamItem(presa)

                // Animated water flow arrow between dams
                if (index < presas.size - 1) {
                    Box(
                        modifier = Modifier
                            .width(4.dp)
                            .height(24.dp)
                            .background(
                                Brush.verticalGradient(
                                    colors = listOf(
                                        PihCyan.copy(alpha = 0.3f + 0.5f * animOffset),
                                        PihBlue.copy(alpha = 0.8f - 0.3f * animOffset),
                                        PihCyan.copy(alpha = 0.3f + 0.3f * animOffset)
                                    )
                                ),
                                shape = RoundedCornerShape(2.dp)
                            )
                    )
                    // Water droplet indicator
                    Text(
                        "▼",
                        color = PihCyan.copy(alpha = 0.5f + 0.5f * animOffset),
                        fontSize = 10.sp
                    )
                }
            }
        }
    }
}

@Composable
fun CascadeDamItem(presa: CascadePresa) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = PihSurface),
        shape = RoundedCornerShape(8.dp)
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Name
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    presa.name,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = PihTextPrimary
                )
                Text(
                    presa.ultimaHora?.let { "Hora $it" } ?: "—",
                    fontSize = 10.sp,
                    color = PihTextSecondary
                )
            }

            // Elevation
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("Elev", fontSize = 9.sp, color = PihTextSecondary)
                Text(
                    String.format("%.2f", presa.currentElev ?: 0.0),
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Bold,
                    color = PihBlue,
                    fontFamily = FontFamily.Monospace
                )
            }

            Spacer(modifier = Modifier.width(12.dp))

            // Generation
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("Gen", fontSize = 9.sp, color = PihTextSecondary)
                Text(
                    String.format("%.1f", presa.generation ?: 0.0),
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Bold,
                    color = PihYellow,
                    fontFamily = FontFamily.Monospace
                )
            }

            Spacer(modifier = Modifier.width(12.dp))

            // Flow (Aportaciones - Extracciones)
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("Apor/Ext", fontSize = 9.sp, color = PihTextSecondary)
                Text(
                    "${String.format("%.0f", presa.aportacionesV ?: 0.0)}/${String.format("%.0f", presa.extraccionesV ?: 0.0)}",
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = PihGreen,
                    fontFamily = FontFamily.Monospace
                )
            }
        }
    }
}

@Composable
fun PresaCard(presa: FunVasosResumenPresa, onClick: () -> Unit) {
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick),
        colors = CardDefaults.cardColors(containerColor = PihCard),
        shape = RoundedCornerShape(12.dp)
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    presa.presa ?: "—",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = PihTextPrimary
                )
                Text(
                    presa.ultimaHora?.let { "Hora $it" } ?: "—",
                    fontSize = 12.sp,
                    color = PihTextSecondary,
                    fontFamily = FontFamily.Monospace
                )
            }

            Spacer(modifier = Modifier.height(8.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceEvenly
            ) {
                PresaStatItem("Elevación", String.format("%.2f", presa.ultimaElevacion ?: 0.0), "msnm", PihBlue)
                PresaStatItem("Almac.", String.format("%.1f", presa.ultimoAlmacenamiento ?: 0.0), "hm³", PihCyan)
                PresaStatItem("Generación", String.format("%.1f", presa.totalGeneracion ?: 0.0), "GWh", PihYellow)
            }

            Spacer(modifier = Modifier.height(4.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceEvenly
            ) {
                PresaStatItem("Aportaciones", String.format("%.1f", presa.totalAportacionesV ?: 0.0), "hm³", PihGreen)
                PresaStatItem("Extracciones", String.format("%.1f", presa.totalExtraccionesV ?: 0.0), "hm³", PihOrange)
            }

            Spacer(modifier = Modifier.height(8.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.End
            ) {
                Text(
                    "Ver detalle →",
                    fontSize = 12.sp,
                    color = PihPurple,
                    fontWeight = FontWeight.Medium
                )
            }
        }
    }
}

@Composable
fun PresaStatItem(label: String, value: String, unit: String, color: Color) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(label, fontSize = 10.sp, color = PihTextSecondary)
        Text(
            value,
            fontSize = 15.sp,
            fontWeight = FontWeight.Bold,
            color = color,
            fontFamily = FontFamily.Monospace
        )
        Text(unit, fontSize = 9.sp, color = PihTextSecondary)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PresaDetailSheet(presa: FunVasosResumenPresa, onDismiss: () -> Unit) {
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = sheetState,
        containerColor = PihBackground,
        contentColor = PihTextPrimary
    ) {
        LazyColumn(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp),
            contentPadding = PaddingValues(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            // Header
            item {
                Text(
                    presa.presa ?: "Presa",
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = PihCyan
                )
            }

            // Summary cards row
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    DetailSummaryCard("Elevación", String.format("%.2f", presa.ultimaElevacion ?: 0.0), "msnm", PihBlue, Modifier.weight(1f))
                    DetailSummaryCard("Almac.", String.format("%.1f", presa.ultimoAlmacenamiento ?: 0.0), "hm³", PihCyan, Modifier.weight(1f))
                }
            }
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    DetailSummaryCard("Aportaciones", String.format("%.1f", presa.totalAportacionesV ?: 0.0), "hm³", PihGreen, Modifier.weight(1f))
                    DetailSummaryCard("Extracciones", String.format("%.1f", presa.totalExtraccionesV ?: 0.0), "hm³", PihOrange, Modifier.weight(1f))
                    DetailSummaryCard("Generación", String.format("%.1f", presa.totalGeneracion ?: 0.0), "GWh", PihYellow, Modifier.weight(1f))
                }
            }

            // Combined chart: Elevation (left Y), Aportaciones+Extracciones (right Y)
            if (!presa.datos.isNullOrEmpty()) {
                item {
                    FunVasosCombinedChart(presa.datos!!)
                }

                // Hourly data table
                item {
                    Text(
                        "Datos Horarios",
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = PihTextPrimary
                    )
                }

                // Table header
                item {
                    HourlyDataHeader()
                }

                // Table rows
                items(presa.datos!!) { dato ->
                    HourlyDataRow(dato)
                }
            }
        }
    }
}

@Composable
fun DetailSummaryCard(label: String, value: String, unit: String, color: Color, modifier: Modifier = Modifier) {
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
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = color,
                fontFamily = FontFamily.Monospace
            )
            Text(unit, fontSize = 9.sp, color = PihTextSecondary)
        }
    }
}

@Composable
fun FunVasosCombinedChart(datos: List<FunVasosDatoHorario>) {
    val elevValues = datos.map { it.elevacion ?: 0.0 }
    val aportValues = datos.map { it.aportacionesV ?: 0.0 }
    val extrValues = datos.map { it.extraccionesTotalV ?: 0.0 }

    if (elevValues.all { it == 0.0 } && aportValues.all { it == 0.0 } && extrValues.all { it == 0.0 }) return

    // Left axis: Elevation
    val elevMin = elevValues.min()
    val elevMax = elevValues.max()
    val elevRange = if (elevMax - elevMin < 0.001) 1.0 else elevMax - elevMin

    // Right axis: Aportaciones + Extracciones combined range (start from 0)
    val rightMax = (aportValues + extrValues).max().coerceAtLeast(0.1)
    val rightMin = 0.0
    val rightRange = rightMax

    // Tooltip state
    var tooltipIndex by remember { mutableStateOf<Int?>(null) }

    Card(
        modifier = Modifier
            .fillMaxWidth()
            .height(240.dp),
        colors = CardDefaults.cardColors(containerColor = PihCard),
        shape = RoundedCornerShape(8.dp)
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            // Legend row
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceEvenly
            ) {
                LegendItem("Elevación", PihBlue, isLine = true)
                LegendItem("Aportaciones", PihGreen, isLine = false)
                LegendItem("Extracciones", PihOrange, isLine = false)
            }
            Spacer(modifier = Modifier.height(4.dp))

            // Chart area with axis labels
            Row(modifier = Modifier.fillMaxWidth().weight(1f)) {
                // Left axis labels
                Column(
                    modifier = Modifier
                        .width(42.dp)
                        .fillMaxHeight(),
                    verticalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(
                        String.format("%.1f", elevMax),
                        fontSize = 8.sp,
                        color = PihBlue,
                        fontFamily = FontFamily.Monospace,
                        maxLines = 1
                    )
                    Text(
                        "msnm",
                        fontSize = 7.sp,
                        color = PihBlue.copy(alpha = 0.7f),
                        textAlign = TextAlign.Center,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Text(
                        String.format("%.1f", elevMin),
                        fontSize = 8.sp,
                        color = PihBlue,
                        fontFamily = FontFamily.Monospace,
                        maxLines = 1
                    )
                }

                // Canvas with touch
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight()
                ) {
                    Canvas(
                        modifier = Modifier
                            .fillMaxSize()
                            .pointerInput(datos) {
                                detectTapGestures { offset ->
                                    val n = datos.size
                                    if (n == 0) return@detectTapGestures
                                    val stepX = size.width.toFloat() / (n - 1).coerceAtLeast(1)
                                    val idx = ((offset.x / stepX) + 0.5f).toInt().coerceIn(0, n - 1)
                                    tooltipIndex = if (tooltipIndex == idx) null else idx
                                }
                            }
                    ) {
                    val w = size.width
                    val h = size.height
                    val n = datos.size
                    if (n == 0) return@Canvas
                    val stepX = if (n > 1) w / (n - 1) else w
                    val barWidth = (stepX * 0.35f).coerceAtLeast(3f)

                    // --- Bars: Aportaciones & Extracciones (right axis) ---
                    aportValues.forEachIndexed { i, v ->
                        val x = i * stepX
                        val barH = ((v - rightMin) / rightRange * h).toFloat()
                        drawRect(
                            color = PihGreen.copy(alpha = 0.7f),
                            topLeft = Offset(x - barWidth, h - barH),
                            size = androidx.compose.ui.geometry.Size(barWidth, barH)
                        )
                    }
                    extrValues.forEachIndexed { i, v ->
                        val x = i * stepX
                        val barH = ((v - rightMin) / rightRange * h).toFloat()
                        drawRect(
                            color = PihOrange.copy(alpha = 0.7f),
                            topLeft = Offset(x, h - barH),
                            size = androidx.compose.ui.geometry.Size(barWidth, barH)
                        )
                    }

                    // --- Spline: Elevation (left axis) ---
                    if (elevValues.size >= 2) {
                        val points = elevValues.mapIndexed { i, v ->
                            Offset(
                                i * stepX,
                                (h - ((v - elevMin) / elevRange * h).toFloat())
                            )
                        }
                        val splinePath = Path().apply {
                            moveTo(points[0].x, points[0].y)
                            for (i in 0 until points.size - 1) {
                                val p0 = if (i > 0) points[i - 1] else points[i]
                                val p1 = points[i]
                                val p2 = points[i + 1]
                                val p3 = if (i + 2 < points.size) points[i + 2] else points[i + 1]
                                val cp1x = p1.x + (p2.x - p0.x) / 6f
                                val cp1y = p1.y + (p2.y - p0.y) / 6f
                                val cp2x = p2.x - (p3.x - p1.x) / 6f
                                val cp2y = p2.y - (p3.y - p1.y) / 6f
                                cubicTo(cp1x, cp1y, cp2x, cp2y, p2.x, p2.y)
                            }
                        }
                        // Fill under spline
                        val fillPath = Path().apply {
                            addPath(splinePath)
                            lineTo(points.last().x, h)
                            lineTo(points.first().x, h)
                            close()
                        }
                        drawPath(fillPath, Brush.verticalGradient(
                            listOf(PihBlue.copy(alpha = 0.18f), PihBlue.copy(alpha = 0.01f))
                        ))
                        drawPath(splinePath, PihBlue, style = Stroke(2.5f))
                    }

                    // --- Tooltip vertical line & dot ---
                    tooltipIndex?.let { idx ->
                        if (idx in datos.indices) {
                            val tx = idx * stepX
                            // Vertical line
                            drawLine(
                                color = Color.White.copy(alpha = 0.4f),
                                start = Offset(tx, 0f),
                                end = Offset(tx, h),
                                strokeWidth = 1f
                            )
                            // Elevation dot
                            val ey = (h - ((elevValues[idx] - elevMin) / elevRange * h).toFloat())
                            drawCircle(PihBlue, 5f, Offset(tx, ey))
                            drawCircle(Color.White, 3f, Offset(tx, ey))
                        }
                    }
                }

                    // Tooltip popup overlay
                    tooltipIndex?.let { idx ->
                        if (idx in datos.indices) {
                            val d = datos[idx]
                            val hora = d.hora?.let { "Hora $it" } ?: "—"
                            val elev = d.elevacion?.let { String.format("%.2f", it) } ?: "—"
                            val aport = d.aportacionesV?.let { String.format("%.1f", it) } ?: "—"
                            val extr = d.extraccionesTotalV?.let { String.format("%.1f", it) } ?: "—"

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
                                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                                ) {
                                    Text(hora, fontSize = 10.sp, fontWeight = FontWeight.Bold, color = PihTextPrimary)
                                    Text("$elev msnm", fontSize = 10.sp, color = PihBlue, fontFamily = FontFamily.Monospace)
                                    Text("A:$aport", fontSize = 10.sp, color = PihGreen, fontFamily = FontFamily.Monospace)
                                    Text("E:$extr", fontSize = 10.sp, color = PihOrange, fontFamily = FontFamily.Monospace)
                                }
                            }
                        }
                    }
                }

                // Right axis labels
                Column(
                    modifier = Modifier
                        .width(42.dp)
                        .fillMaxHeight(),
                    verticalArrangement = Arrangement.SpaceBetween,
                    horizontalAlignment = Alignment.End
                ) {
                    Text(
                        String.format("%.1f", rightMax),
                        fontSize = 8.sp,
                        color = PihGreen,
                        fontFamily = FontFamily.Monospace,
                        maxLines = 1
                    )
                    Text(
                        "vol",
                        fontSize = 7.sp,
                        color = PihGreen.copy(alpha = 0.7f),
                        textAlign = TextAlign.Center,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Text(
                        String.format("%.1f", rightMin),
                        fontSize = 8.sp,
                        color = PihGreen,
                        fontFamily = FontFamily.Monospace,
                        maxLines = 1
                    )
                }
            }

            // Hours axis
            Row(
                modifier = Modifier.fillMaxWidth().padding(start = 42.dp, end = 42.dp),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                val hours = datos.mapNotNull { it.hora }
                if (hours.isNotEmpty()) {
                    val step = (hours.size / 5).coerceAtLeast(1)
                    hours.filterIndexed { i, _ -> i % step == 0 || i == hours.lastIndex }.forEach { h ->
                        Text("Hora $h", fontSize = 8.sp, color = PihTextSecondary)
                    }
                }
            }
        }
    }
}

@Composable
fun LegendItem(label: String, color: Color, isLine: Boolean = true) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        if (isLine) {
            Box(
                modifier = Modifier
                    .width(14.dp)
                    .height(3.dp)
                    .background(color, RoundedCornerShape(1.dp))
            )
        } else {
            Box(
                modifier = Modifier
                    .size(8.dp)
                    .background(color, RoundedCornerShape(2.dp))
            )
        }
        Spacer(modifier = Modifier.width(4.dp))
        Text(label, fontSize = 9.sp, color = PihTextSecondary)
    }
}

@Composable
fun HourlyDataHeader() {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(PihSurface, RoundedCornerShape(4.dp))
            .padding(horizontal = 8.dp, vertical = 6.dp),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        listOf("Hr", "Elev", "Almac", "AporV", "ExtrV", "Gen").forEach { header ->
            Text(
                header,
                fontSize = 10.sp,
                fontWeight = FontWeight.Bold,
                color = PihTextSecondary,
                modifier = Modifier.weight(1f),
                textAlign = TextAlign.Center
            )
        }
    }
}

@Composable
fun HourlyDataRow(dato: FunVasosDatoHorario) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 8.dp, vertical = 3.dp),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(
            dato.hora?.toString() ?: "—",
            fontSize = 11.sp,
            color = PihTextPrimary,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.Center
        )
        Text(
            String.format("%.2f", dato.elevacion ?: 0.0),
            fontSize = 11.sp,
            color = PihBlue,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.Center
        )
        Text(
            String.format("%.1f", dato.almacenamiento ?: 0.0),
            fontSize = 11.sp,
            color = PihCyan,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.Center
        )
        Text(
            String.format("%.1f", dato.aportacionesV ?: 0.0),
            fontSize = 11.sp,
            color = PihGreen,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.Center
        )
        Text(
            String.format("%.1f", dato.extraccionesTotalV ?: 0.0),
            fontSize = 11.sp,
            color = PihOrange,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.Center
        )
        Text(
            String.format("%.1f", dato.generacion ?: 0.0),
            fontSize = 11.sp,
            color = PihYellow,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.Center
        )
    }
    Divider(
        thickness = 0.5.dp, 
        color = PihDivider.copy(alpha = 0.3f)
    )
}
