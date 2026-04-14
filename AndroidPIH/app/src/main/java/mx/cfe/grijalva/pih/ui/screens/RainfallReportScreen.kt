package mx.cfe.grijalva.pih.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch
import mx.cfe.grijalva.pih.data.model.EstacionLluvia
import mx.cfe.grijalva.pih.data.model.RainfallReportResponse
import mx.cfe.grijalva.pih.data.model.SubcuencaReporte
import mx.cfe.grijalva.pih.data.service.RainfallService
import mx.cfe.grijalva.pih.ui.theme.*

@Composable
fun RainfallReportScreen(rainfallService: RainfallService) {
    val report by rainfallService.report.collectAsState()
    val isLoading by rainfallService.isLoading.collectAsState()
    val errorMessage by rainfallService.errorMessage.collectAsState()
    val scope = rememberCoroutineScope()

    var selectedTipo by remember { mutableStateOf("parcial") }

    LaunchedEffect(Unit) {
        rainfallService.loadReport("parcial")
    }

    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .background(PihBackground)
            .padding(horizontal = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
        contentPadding = PaddingValues(top = 12.dp, bottom = 20.dp)
    ) {
        // Tipo selector
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("parcial" to "Parcial", "24h" to "24 Horas").forEach { (key, label) ->
                    FilterChip(
                        selected = selectedTipo == key,
                        onClick = {
                            selectedTipo = key
                            scope.launch { rainfallService.loadReport(key) }
                        },
                        label = { Text(label, fontSize = 13.sp, fontWeight = FontWeight.SemiBold) },
                        colors = FilterChipDefaults.filterChipColors(
                            selectedContainerColor = PihGreen,
                            selectedLabelColor = Color.Black,
                            containerColor = PihCard,
                            labelColor = PihTextPrimary
                        )
                    )
                }
            }
        }

        // Loading
        if (isLoading) {
            item {
                Box(
                    modifier = Modifier.fillMaxWidth().padding(top = 40.dp),
                    contentAlignment = Alignment.Center
                ) {
                    CircularProgressIndicator(color = PihCyan)
                }
            }
        }

        // Error
        errorMessage?.let { msg ->
            item {
                Text(msg, color = PihRed, fontSize = 14.sp, modifier = Modifier.padding(top = 20.dp))
            }
        }

        // Report content
        report?.let { rpt ->
            // Header
            item {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(10.dp))
                        .background(PihCard)
                        .padding(12.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Text(rpt.titulo, color = PihTextPrimary, fontSize = 15.sp, fontWeight = FontWeight.Bold)
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        "Período: ${rpt.periodoInicioLocal} — ${rpt.periodoFinLocal}",
                        color = PihTextSecondary, fontSize = 12.sp
                    )
                }
            }

            // Summary cards
            item {
                val allEstaciones = rpt.subcuencas.flatMap { it.estaciones }
                val maxLluvia = allEstaciones.maxOfOrNull { it.acumuladoMm } ?: 0.0
                val avgLluvia = if (allEstaciones.isNotEmpty())
                    allEstaciones.sumOf { it.acumuladoMm } / allEstaciones.size else 0.0

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    SummaryCard("Estaciones", "${rpt.totalEstaciones}", PihCyan, Modifier.weight(1f))
                    SummaryCard("Con lluvia", "${rpt.estacionesConLluvia}", PihGreen, Modifier.weight(1f))
                    SummaryCard("Máxima", "%.1f mm".format(maxLluvia), PihOrange, Modifier.weight(1f))
                    SummaryCard("Promedio", "%.1f mm".format(avgLluvia), PihPurple, Modifier.weight(1f))
                }
            }

            // Subcuencas
            val globalMax = rpt.subcuencas.flatMap { it.estaciones }.maxOfOrNull { it.acumuladoMm } ?: 1.0

            items(rpt.subcuencas, key = { it.subcuenca }) { sub ->
                SubcuencaCard(sub, globalMax)
            }
        }
    }
}

@Composable
private fun SummaryCard(title: String, value: String, color: Color, modifier: Modifier = Modifier) {
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(8.dp))
            .background(PihCard)
            .padding(vertical = 10.dp, horizontal = 4.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(value, color = PihTextPrimary, fontSize = 13.sp, fontWeight = FontWeight.Bold,
            maxLines = 1, textAlign = TextAlign.Center)
        Spacer(modifier = Modifier.height(2.dp))
        Text(title, color = color, fontSize = 9.sp)
    }
}

@Composable
private fun SubcuencaCard(sub: SubcuencaReporte, globalMax: Double) {
    Column(
        modifier = Modifier.fillMaxWidth()
    ) {
        // Header
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(topStart = 8.dp, topEnd = 8.dp))
                .background(
                    Brush.horizontalGradient(
                        listOf(Color(0xFF218C21), Color(0xFF2EA62E))
                    )
                )
                .padding(horizontal = 12.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text("💧", fontSize = 12.sp)
            Spacer(modifier = Modifier.width(6.dp))
            Text(
                sub.subcuenca,
                color = Color.White,
                fontSize = 14.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.weight(1f)
            )
            Text(
                "Prom: ${"%.1f".format(sub.promedioMm)} mm",
                color = Color.White.copy(alpha = 0.9f),
                fontSize = 12.sp,
                fontWeight = FontWeight.SemiBold
            )
        }

        // Station rows
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(bottomStart = 8.dp, bottomEnd = 8.dp))
                .background(PihCard)
        ) {
            sub.estaciones.forEachIndexed { index, est ->
                StationRow(est, globalMax, index % 2 == 0)
            }
        }
    }
}

@Composable
private fun StationRow(est: EstacionLluvia, globalMax: Double, isEven: Boolean) {
    val bgColor = if (isEven) Color.White.copy(alpha = 0.03f) else Color.Transparent
    val barFraction = if (globalMax > 0) (est.acumuladoMm / globalMax).toFloat().coerceIn(0f, 1f) else 0f

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(bgColor)
            .padding(horizontal = 12.dp, vertical = 5.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            est.nombre,
            color = PihTextPrimary,
            fontSize = 11.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.width(110.dp)
        )

        Spacer(modifier = Modifier.width(8.dp))

        // Bar
        Box(
            modifier = Modifier
                .weight(1f)
                .height(14.dp)
        ) {
            Box(
                modifier = Modifier
                    .fillMaxHeight()
                    .fillMaxWidth(fraction = barFraction.coerceAtLeast(0.01f))
                    .clip(RoundedCornerShape(3.dp))
                    .background(
                        Brush.horizontalGradient(
                            listOf(PihGreen.copy(alpha = 0.7f), PihGreen)
                        )
                    )
            )
        }

        Spacer(modifier = Modifier.width(8.dp))

        Text(
            "%.1f".format(est.acumuladoMm),
            color = PihCyan,
            fontSize = 11.sp,
            fontWeight = FontWeight.SemiBold,
            fontFamily = FontFamily.Monospace,
            modifier = Modifier.width(50.dp),
            textAlign = TextAlign.End
        )
    }
}
