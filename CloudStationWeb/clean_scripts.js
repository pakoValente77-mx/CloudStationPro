<script src="https://code.highcharts.com/highcharts.js"></script>
<script src="https://cdn.sheetjs.com/xlsx-latest/package/dist/xlsx.full.min.js"></script>
<script>
    let analysisData = null;
    let chart = null;
    let currentAggregationLevel = 'daily'; // Default

    $(document).ready(function() {
        $('.ui.dropdown').dropdown();
        $('.menu .item').tab(); // Initialize tabs
        
        // Configure Highcharts timezone
        Highcharts.setOptions({
            time: {
                timezoneOffset: 6 * 60 // CDMX UTC-6
            }
        });

        loadStations();
        setDefaultDates();

        $('#station-dropdown').change(function() {
            const stationId = $(this).val();
            if (stationId) {
                loadStationVariables(stationId);
            } else {
                // Reset to default or clear?
                // loadStationVariables(''); // potentially clear
            }
        });

        $('#add-series-btn').click(function() {
            addSeries();
        });

        $('#clear-btn').click(function() {
            clearChart();
        });

        $('#variable-dropdown').change(function() {
             // Optional: clear chart on variable change or warn user?
             // Implementation plan said: "Changing the variable will clear the chart"
             // But we handle it in addSeries with a confirm.
             // Let's also auto-clear if they specifically change the dropdown to avoid confusion?
             // Or better, let them keep the chart until they try to add a conflicting series.
             // I'll stick to the check in addSeries.
        });

        $('#refresh-btn').click(function() {
            refreshAllSeries();
        });

        $('.ui.mini.buttons button').click(function() {
            const hours = $(this).data('hours');
            const days = $(this).data('days');
            const now = new Date();
            const end = now;
            let start;

            if (hours) {
                start = new Date(now.getTime() - hours * 60 * 60 * 1000);
            } else if (days) {
                start = new Date(now.getTime() - days * 24 * 60 * 60 * 1000);
            }

            $('#start-date').val(formatDateTimeLocal(start));
            $('#end-date').val(formatDateTimeLocal(end));
        });

        $('#export-excel-btn').click(function() {
            exportToExcel();
        });
    });

    function refreshAllSeries() {
        if (activeSeries.length === 0) {
            alert('No hay series para consultar. Agrega primero una estación.');
            return;
        }

        $('#loading-overlay').show();
        
        const startDate = new Date($('#start-date').val());
        const endDate = new Date($('#end-date').val());

        const promises = activeSeries.map(series => {
            const request = {
                stationIds: [series.stationId],
                variable: series.variable, // Use specific variable for each series
                startDate: startDate.toISOString(),
                endDate: endDate.toISOString()
            };
            
            return $.ajax({
                url: '/DataAnalysis/GetAnalysisData',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(request)
            });
        });

        Promise.all(promises)
            .then(responses => {
                const newActiveSeries = [];
                // Check if any response has aggregation level (all should be same if same dates)
                let aggregationLevel = 'daily'; 
                if (responses.length > 0 && responses[0].aggregationLevel) {
                    aggregationLevel = responses[0].aggregationLevel;
                }

                responses.forEach((resp, index) => {
                    if (resp.series && resp.series.length > 0) {
                        const s = resp.series[0];
                        const original = activeSeries[index];
                        
                        // Preserve metadata from original series
                        s.variable = original.variable;
                        s.stationId = original.stationId;
                        s.stationName = original.stationName;
                        s.variableDisplayName = original.variableDisplayName;
                        s.newAxis = original.newAxis;
                        
                        newActiveSeries.push(s);
                    }
                });

                activeSeries = newActiveSeries;
                if (activeSeries.length > 0) {
                     currentAggregationLevel = aggregationLevel;
                     updateChart(activeSeries, aggregationLevel); // Fixed signature
                     renderTable(activeSeries);
                     updateInfo(activeSeries, aggregationLevel);
                } else {
                    clearChart(); 
                    alert('No se encontraron datos en el nuevo rango de fechas para las series seleccionadas.');
                }
                
                $('#loading-overlay').hide();
            })
            .catch(error => {
                $('#loading-overlay').hide();
                console.error(error);
                alert('Error al consultar datos.');
            });
    }

    function loadStations() {
        $.ajax({
            url: '/DataAnalysis/GetStations',
            method: 'GET',
            success: function(stations) {
                const dropdown = $('#station-dropdown');
                dropdown.empty();
                dropdown.append('<option value="">Selecciona una estación...</option>');
                
                stations.forEach(s => {
                    dropdown.append(`<option value="${s.id}">${s.name}</option>`);
                });

                dropdown.dropdown('refresh');
            },
            error: function(xhr) {
                alert('Error al cargar estaciones: ' + (xhr.responseJSON?.error || 'Error desconocido'));
            }
        });
    }

    function loadStationVariables(stationId) {
        const variableDropdown = $('#variable-dropdown');
        variableDropdown.parent().addClass('loading');

        $.ajax({
            url: `/DataAnalysis/GetStationVariables?stationId=${stationId}`,
            method: 'GET',
            success: function(variables) {
                variableDropdown.empty();
                
                // Helper to format name if DisplayName missing
                const formatName = (name) => {
                    return name.charAt(0).toUpperCase() + name.slice(1).replace(/_/g, ' ');
                };

                variables.forEach(v => {
                    const name = v.displayName || formatName(v.variable);
                    // Use Unicode for status since <option> only supports text
                    const statusPrefix = v.hasData ? '' : '⚠️ ';
                    const statusSuffix = v.hasData ? '' : ' (Sin datos)';
                    
                    const optionText = `${statusPrefix}${name}${statusSuffix}`;
                    const option = $(`<option value="${v.variable}">${optionText}</option>`);
                    
                    // Optional: disable if no data? Or allow selection but warn?
                    // User said "indicar si esa variable y estacion cuenta con datos".
                    // Disabling might persist the idea that it's broken.
                    // Let's disable for now to prevent empty charts, or maybe allow it?
                    // Previous logic disabled it. I will keep it disabled but maybe allow "Consulta" to re-check later?
                    // Actually, if I disable it, the user can't select it to "Consultar". 
                    // But if it has no data NOW, it likely won't have data in 5 seconds.
                    // Better to disable to avoid confusion.
                    if (!v.hasData) {
                        option.prop('disabled', true);
                    }
                    
                    variableDropdown.append(option);
                });

                if (variables.length === 0) {
                     variableDropdown.append('<option value="">Sin variables configuradas</option>');
                }

                // Select first available?
                const firstAvailable = variables.find(v => v.hasData);
                if (firstAvailable) {
                     variableDropdown.val(firstAvailable.variable);
                } else if (variables.length > 0) {
                    // If none available, select the first one anyway (even if disabled, programmatically setting val works?)
                    // Selecting a disabled option programmatically usually works in Select, but maybe not in Semantic UI dropdown.
                    // Semantic UI might ignore it.
                }

                variableDropdown.parent().removeClass('loading');
                variableDropdown.dropdown('refresh'); 
            },
            error: function(xhr) {
                variableDropdown.parent().removeClass('loading');
                console.error('Error loading variables', xhr);
                variableDropdown.empty();
                variableDropdown.append('<option value="">Error al cargar</option>');
            }
        });
    }

    function setDefaultDates() {
        const now = new Date();
        const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);
        
        $('#start-date').val(formatDateTimeLocal(yesterday));
        $('#end-date').val(formatDateTimeLocal(now));
    }

    function formatDateTimeLocal(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        const hours = String(date.getHours()).padStart(2, '0');
        const minutes = String(date.getMinutes()).padStart(2, '0');
        return `${year}-${month}-${day}T${hours}:${minutes}`;
    }

    let activeSeries = [];

    function addSeries() {
        const stationId = $('#station-dropdown').dropdown('get value');
        const variable = $('#variable-dropdown').dropdown('get value');
        // Get Station Name and Variable Name for legend
        const stationName = $('#station-dropdown option:selected').text();
        const variableName = $('#variable-dropdown option:selected').text();
        
        const startDate = new Date($('#start-date').val());
        const endDate = new Date($('#end-date').val());
        const newAxis = $('#new-axis-checkbox').prop('checked');
        
        // Read manual Y-Axis configs
        const manualYMin = $('#y-min').val() !== "" ? parseFloat($('#y-min').val()) : null;
        const manualYMax = $('#y-max').val() !== "" ? parseFloat($('#y-max').val()) : null;
        const manualYStep = $('#y-step').val() !== "" ? parseFloat($('#y-step').val()) : null;

        if (!stationId) {
            alert('Selecciona una estación');
            return;
        }

        // Check for duplicates
        if (activeSeries.some(s => s.stationId === stationId && s.variable === variable)) {
            alert('Esta estación ya está en el gráfico');
            return;
        }

        // Remove variable consistency check - user wants flexibility
        
        const request = {
            stationIds: [stationId],
            variable: variable,
            startDate: startDate.toISOString(),
            endDate: endDate.toISOString()
        };

        $('#loading-overlay').show();

        $.ajax({
            url: '/DataAnalysis/GetAnalysisData',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(request),
            success: function(response) {
                if (response.series && response.series.length > 0) {
                    const newSeries = response.series[0];
                    newSeries.variable = variable; 
                    newSeries.stationId = stationId;
                    newSeries.stationName = stationName; // Use clean name from dropdown
                    newSeries.variableDisplayName = variableName;
                    newSeries.newAxis = newAxis; // Store axis preference
                    newSeries.manualYMin = manualYMin;
                    newSeries.manualYMax = manualYMax;
                    newSeries.manualYStep = manualYStep;
                    
                    activeSeries.push(newSeries);
                    currentAggregationLevel = response.aggregationLevel; // Store for removal logic
                    updateChart(activeSeries, response.aggregationLevel);
                    renderTable(activeSeries);
                    updateInfo(activeSeries, response.aggregationLevel);
                }
                $('#loading-overlay').hide();
            },
            error: function(xhr) {
                $('#loading-overlay').hide();
                alert('Error al agregar serie: ' + (xhr.responseJSON?.error || 'Error desconocido'));
            }
        });
    }

    function clearChart() {
        activeSeries = [];
        if (chart) {
            while (chart.series.length > 0) {
                chart.series[0].remove(true);
            }
            while (chart.yAxis.length > 0) {
                 chart.yAxis[0].remove(true); // Remove extra axes? Highcharts might need complete rebuild
            }
            chart.setTitle({ text: 'Análisis de Datos Hidrometeorológicos' });
            
            // Re-initialize to clear axes cleanly
            chart.destroy();
            chart = null;
        }
        $('#data-table tbody').empty();
        $('.data-table-container').hide();
        $('#analysis-info').hide();
        
        // Clear WMO quality panel
        $('#data-quality-panel').removeClass('active');
        $('#last-update-time').text('--');
        $('#aggregation-level-text').text('--');
        $('#data-points-count').text('--');
        $('#active-series-count').text('0');
        
        // Clear series chips
        $('#active-series-list').empty();
        $('#active-series-container').hide();
    }

    // Helper function to generate informative subtitle for charts
    function getSubtitleText(aggregationLevel, seriesList) {
        let aggText = '';
        if (aggregationLevel === 'raw') {
            aggText = 'Datos puntuales (10 min)';
        } else if (aggregationLevel === 'hourly') {
            aggText = 'Agregación horaria';
        } else {
            aggText = 'Agregación diaria';
        }
        
        const seriesCount = seriesList ? seriesList.length : 0;
        const totalPoints = seriesList ? seriesList.reduce((sum, s) => sum + s.dataPoints.length, 0) : 0;
        
        return `${aggText} | ${seriesCount} serie(s) | ${totalPoints.toLocaleString('es-MX')} puntos`;
    }

    function updateChart(seriesList, aggregationLevel) {
        // Destroy and Rebuild is safer for changing axes structure dynamically
        if (chart) {
            chart.destroy();
            chart = null;
        }
        
        // Build Y-Axes configuration
        const yAxes = [];
        let axisIndexMap = new Map(); // Map 'variable+index' to axis index
        let nextAxisIndex = 0;

        // Group series by axis requirement
        // Strategy: 
        // 1. Primary axis (index 0) always exists.
        // 2. If series requests 'newAxis', it gets a new one.
        // 3. If series does NOT request newAxis, it tries to share based on variable name? 
        //    User said "indicate if adding another Y axis or maintaining the same".
        //    "Maintaining the same" implies sharing with existing series of same type or just the primary one?
        //    Let's assume "Same" means Axis 0 (Primary) unless it's a completely different unit/scale where it makes no sense?
        //    But user explicitly asks for control. So:
        //    - Checkbox OFF: use Axis 0 (or find existing axis for this variable?). 
        //      Let's simpliy: Checkbox OFF = Axis 0 (Left). Checkbox ON = New Axis (Right).
        
        // Better Strategy obeying "maintain same":
        // - Axis 0 is default.
        // - If Checkbox ON, create new axis.
        
        // We need to store which series maps to which axis.
        // But activeSeries persistence makes this tricky if we rebuild.
        // We stored `newAxis` bool.
        // Iterate series:
        // - Series 1 (Precip): newAxis=false -> Axis 0
        // - Series 2 (Temp): newAxis=true -> Axis 1
        // - Series 3 (Temp): newAxis=false -> Axis 0 ?? Or share with Series 2?
        // "Maintain same" usually means "Same as previous" or "Same as specific one". 
        // With simple boolean "New Axis", it implies:
        // - False: Use Default/Primary Axis.
        // - True: Create A NEW Axis for THIS series.
        
        yAxes.push({
            title: { text: 'Valor' }, // Generic title for primary
            labels: { format: '{value}' },
            opposite: false,
            min: 0 // Force zero baseline
        });

        seriesList.forEach((s, i) => {
            if (s.newAxis) {
                // ADD new axis
                yAxes.push({
                    title: { text: s.variableDisplayName },
                    labels: { format: '{value}' },
                    opposite: true, // Secondary axes on right
                    min: 0 // Force zero baseline
                });
                s.yAxisIndex = yAxes.length - 1;
            } else {
                // Use primary
                s.yAxisIndex = 0;
                // Update primary title if it's the first series using it
                 if (yAxes[0].title.text === 'Valor') {
                    yAxes[0].title.text = s.variableDisplayName;
                } else if (!yAxes[0].title.text.includes(s.variableDisplayName)) {
                    // Append if mixed? e.g. "Precip, Temp"
                    // yAxes[0].title.text += `, ${s.variableDisplayName}`;
                }
            }
        });

        // Generate Accumulation Series for Precipitation
        const derivedSeries = [];
        seriesList.forEach(s => {
            if (s.variable.toLowerCase().includes('precipitación')) {
                // Calculate accumulated values
                let runningTotal = 0;
                const accumulatedData = s.dataPoints.map(dp => {
                    if (dp.value !== null) {
                        runningTotal += dp.value;
                    }
                    return [new Date(dp.timestamp).getTime(), runningTotal];
                });

                derivedSeries.push({
                    name: `${s.stationName} - Acumulado`,
                    data: accumulatedData,
                    type: 'spline',
                    yAxis: s.yAxisIndex, 
                    dashStyle: 'ShortDot',
                    marker: { enabled: false },
                    tooltip: {
                        valueDecimals: 2,
                        valueSuffix: ' mm'
                    }
                });
            }
        });

        const finalSeriesConfig = [];

        seriesList.forEach(s => {
            const isRain = s.variable.toLowerCase().includes('precipitación');
            
            // Separate valid and invalid data
            const validData = [];
            const invalidData = [];
            
            let minValidY = Number.MAX_VALUE;
            let maxValidY = Number.MIN_VALUE;

            s.dataPoints.forEach(dp => {
                const time = new Date(dp.timestamp).getTime();
                
                if (dp.isValid !== false) { // Default to true if undefined
                    validData.push([time, dp.value]);
                    if (dp.value !== null) {
                        minValidY = Math.min(minValidY, dp.value);
                        maxValidY = Math.max(maxValidY, dp.value);
                    }
                } else {
                    // Invalid data point
                    // Push null to validData to break the line (if not column)
                    if (!isRain) {
                        validData.push([time, null]);
                    }
                    invalidData.push({ x: time, y: dp.value, realValue: dp.value });
                }
            });

            // Adjust Y-axis scale based ONLY on valid data
            if (validData.length > 0 && minValidY !== Number.MAX_VALUE) {
                let yAxis = yAxes[s.yAxisIndex];
                
                // Apply User Manual Overrides or Smart Defaults
                if (s.manualYMin !== null) yAxis.min = s.manualYMin;
                if (s.manualYMax !== null) yAxis.max = s.manualYMax;
                
                if (s.manualYStep !== null) {
                    yAxis.tickInterval = s.manualYStep;
                } else {
                    // Default smart tick intervals if not manually set
                    if (s.variable.toLowerCase().includes('nivel')) {
                        yAxis.tickInterval = 0.1; // 0.1 meters/units for precise water level
                    } else if (s.variable.toLowerCase().includes('precipitación')) {
                        yAxis.tickInterval = 1; // 1 mm for rain
                    } else {
                         // Let highcharts decide for temperature, wind, etc., or force 1
                         // User requested "1 para las demas", applying rule:
                         yAxis.tickInterval = 1;
                    }
                }
                
                // Add padding only if the user didn't specify min/max manually
                let diff = maxValidY - minValidY;
                if (s.manualYMin === null && s.manualYMax === null && diff < 1.0) {
                    const padding = (1.0 - diff) / 2.0;
                    yAxis.min = minValidY - padding;
                    yAxis.max = maxValidY + padding;
                    
                    // Don't let min go below 0 if it's precipitation or naturally positive
                    if (s.variable.toLowerCase().includes('precipitación') || s.variable.toLowerCase().includes('nivel')) {
                        yAxis.min = Math.max(0, yAxis.min);
                    }
                } else if (s.manualYMin === null && s.manualYMax === null) {
                    // Let Highcharts auto-scale, but we could enforce soft limits
                    yAxis.softMin = minValidY;
                    yAxis.softMax = maxValidY;
                }
                
                // Clamp invalid data points to the visual boundaries so they appear at the top/bottom edge
                const visualMin = yAxis.min !== undefined ? yAxis.min : minValidY;
                const visualMax = yAxis.max !== undefined ? yAxis.max : maxValidY;
                
                invalidData.forEach(dp => {
                    if (dp.y > visualMax) dp.y = visualMax;
                    else if (dp.y < visualMin) dp.y = visualMin;
                });

                // --- NEW: Populate UI controls with calculated defaults if they were empty ---
                // We only do this for the primary axis (index 0) to avoid confusing multi-axis overriding,
                // or we update it if they are empty
                if (s.yAxisIndex === 0) {
                    if (s.manualYMin === null) $('#y-min').val(visualMin.toFixed(2));
                    if (s.manualYMax === null) $('#y-max').val(visualMax.toFixed(2));
                    if (s.manualYStep === null && yAxis.tickInterval !== undefined) {
                        $('#y-step').val(yAxis.tickInterval);
                    }
                }
            }

            // Main valid series
            finalSeriesConfig.push({
                name: `${s.stationName} - ${s.variableDisplayName}`,
                data: validData,
                type: isRain ? 'column' : 'areaspline',
                fillOpacity: 0.15, // Añade un sombreado suave debajo de la línea
                yAxis: s.yAxisIndex,
                marker: { 
                    enabled: true,
                    radius: aggregationLevel === 'daily' ? 4 : 2
                },
                gapSize: 5,
                gapUnit: 'value'
            });

            // Invalid series (Scatter dots in red)
            if (invalidData.length > 0) {
                finalSeriesConfig.push({
                    name: `${s.stationName} - Inválidos`,
                    data: invalidData,
                    type: 'scatter',
                    yAxis: s.yAxisIndex,
                    color: 'red',
                    marker: {
                        symbol: 'triangle-down',
                        radius: 6
                    },
                    tooltip: {
                        pointFormatter: function() {
                            return `<span style="color:${this.color}">\u25CF</span> ${this.series.name}: <b>${this.realValue}</b><br/>`;
                        }
                    }
                });
            }
        });
        
        finalSeriesConfig.push(...derivedSeries);

        chart = Highcharts.chart('analysis-chart', {
            chart: {
                backgroundColor: 'transparent',
                zoomType: 'x'
            },
            title: {
                text: 'Análisis de Datos Hidrometeorológicos',
                style: { color: '#333', fontSize: '16px', fontWeight: '600' }
            },
            subtitle: {
                text: getSubtitleText(aggregationLevel, seriesList),
                style: { color: '#666', fontSize: '12px' }
            },
            xAxis: {
                type: 'datetime',
                title: { text: 'Fecha/Hora' },
                ordinal: false 
            },
            yAxis: yAxes,
            tooltip: {
                shared: true,
                crosshairs: true,
                xDateFormat: '%Y-%m-%d %H:%M'
            },
            legend: {
                enabled: true,
                layout: 'horizontal',
                align: 'center',
                verticalAlign: 'bottom',
                itemStyle: { fontSize: '11px' }
            },
            plotOptions: {
                column: { borderRadius: 3, grouping: false, shadow: false, borderWidth: 0 },
                series: {
                    connectNulls: false, 
                }
            },
            credits: { enabled: false },
            series: finalSeriesConfig
        });
    }

    function initializeChart(variable, aggregationLevel) {
        chart = Highcharts.chart('analysis-chart', {
            chart: {
                backgroundColor: 'transparent',
                zoomType: 'x'
            },
            title: {
                text: 'Análisis de Datos Hidrometeorológicos',
                style: { color: '#333', fontSize: '16px', fontWeight: '600' }
            },
            subtitle: {
                text: `Variable: ${variable} | Agregación: ${aggregationLevel}`,
                style: { color: '#666', fontSize: '12px' }
            },
            xAxis: {
                type: 'datetime',
                title: { text: 'Fecha/Hora' }
            },
            yAxis: {
                title: { text: variable }
            },
            tooltip: {
                shared: true,
                crosshairs: true,
                xDateFormat: '%Y-%m-%d %H:%M'
            },
            legend: {
                enabled: true
            },
            plotOptions: {
                column: {
                    borderRadius: 3
                }
            },
            credits: { enabled: false }
        });
    }

    function renderChart(data) {
        const isPrecipitation = data.variable.toLowerCase().includes('precipitación');
        const chartType = isPrecipitation ? 'column' : 'spline';

        const series = data.series.map(s => ({
            name: s.stationName,
            data: s.dataPoints.map(dp => [new Date(dp.timestamp).getTime(), dp.value]),
            type: chartType
        }));

        if (chart) {
            chart.destroy();
        }

        chart = Highcharts.chart('analysis-chart', {
            chart: {
                backgroundColor: 'transparent',
                zoomType: 'x'
            },
            title: {
                text: 'Análisis de Datos Hidrometeorológicos',
                style: { color: '#333', fontSize: '16px', fontWeight: '600' }
            },
            subtitle: {
                text: `Variable: ${data.variable} | Agregación: ${data.aggregationLevel}`,
                style: { color: '#666', fontSize: '12px' }
            },
            xAxis: {
                type: 'datetime',
                title: { text: 'Fecha/Hora' }
            },
            yAxis: {
                title: { text: data.variable }
            },
            tooltip: {
                shared: true,
                crosshairs: true,
                xDateFormat: '%Y-%m-%d %H:%M'
            },
            legend: {
                enabled: true
            },
            plotOptions: {
                series: {
                    marker: {
                        enabled: data.aggregationLevel === 'daily',
                        radius: 3
                    }
                },
                column: {
                    borderRadius: 3
                }
            },
            series: series,
            credits: { enabled: false }
        });
    }

    function renderTable(seriesList) {
        const tbody = $('#data-table tbody');
        tbody.empty();

        let rowCount = 0;
        // Flatten data for table
        seriesList.forEach(s => {
            s.dataPoints.forEach(dp => {
                if (rowCount < 2000) { // Limit rows for rendering performance
                    const row = `
                        <tr>
                            <td>${new Date(dp.timestamp).toLocaleString('es-MX')}</td>
                            <td>${s.stationName}</td>
                            <td>${s.variableDisplayName}</td>
                            <td>${dp.value !== null ? dp.value.toFixed(2) : 'N/A'}</td>
                        </tr>
                    `;
                    tbody.append(row);
                    rowCount++;
                }
            });
        });
        
        // Update global for export logic
        analysisData = { series: seriesList };
    }

    function updateInfo(seriesList, aggregationLevel) {
        const totalPoints = seriesList.reduce((sum, s) => sum + s.dataPoints.length, 0);
        const stationCount = seriesList.length;
        
        // Update aggregation level text
        let aggregationText = '';
        if (aggregationLevel === 'raw') {
            aggregationText = 'Datos puntuales (10 min)';
        } else if (aggregationLevel === 'hourly') {
            aggregationText = 'Agregación horaria';
        } else {
            aggregationText = 'Agregación diaria';
        }

        // Update old info message (keep for compatibility)
        const infoText = `${stationCount} estación(es) | ${totalPoints} puntos de datos | ${aggregationText}`;
        $('#analysis-info-text').text(infoText);
        $('#analysis-info').show();
        
        // Update new WMO-style quality indicators
        updateDataQualityPanel(seriesList, aggregationLevel, totalPoints);
        
        // Update series chips
        renderActiveSeriesTags();
    }
    
    // New function for WMO-style data quality panel
    function updateDataQualityPanel(seriesList, aggregationLevel, totalPoints) {
        // Update last update time
        const now = new Date();
        const timeStr = now.toLocaleString('es-MX', { 
            year: 'numeric', 
            month: '2-digit', 
            day: '2-digit',
            hour: '2-digit', 
            minute: '2-digit' 
        });
        $('#last-update-time').text(timeStr);
        
        // Update aggregation level
        let aggregationText = '';
        if (aggregationLevel === 'raw') {
            aggregationText = 'Datos puntuales (10 min)';
        } else if (aggregationLevel === 'hourly') {
            aggregationText = 'Agregación horaria';
        } else {
            aggregationText = 'Agregación diaria';
        }
        $('#aggregation-level-text').text(aggregationText);
        
        // Update data points count with formatting
        $('#data-points-count').text(totalPoints.toLocaleString('es-MX'));
        
        // Update active series count
        $('#active-series-count').text(seriesList.length);
        
        // Show the panel
        $('#data-quality-panel').addClass('active');
    }

    function renderActiveSeriesTags() {
        const container = $('#active-series-list');
        container.empty();
        
        if (activeSeries.length === 0) {
            $('#active-series-container').hide();
            $('#data-quality-panel').removeClass('active');
            return;
        }

        // Render NOAA-style chips
        activeSeries.forEach((s, index) => {
            const axisLabel = s.newAxis ? ' (Eje Sec.)' : '';
            const chip = $(`
                <div class="series-chip" data-index="${index}">
                    <span>${s.stationName} - ${s.variableDisplayName}${axisLabel}</span>
                    <i class="times icon remove-btn"></i>
                </div>
            `);
            
            chip.find('.remove-btn').click(function() {
                removeSeries(index);
            });
            
            container.append(chip);
        });
        
        $('#active-series-container').show();
    }

    // Expose to global scope for onclick
    window.removeSeries = function(index) {
        if (index >= 0 && index < activeSeries.length) {
            activeSeries.splice(index, 1);
            
            if (activeSeries.length === 0) {
                clearChart();
            } else {
                // Re-render everything needed
                // We need aggregationLevel... stored in first series? Or globally?
                // It was passed in response.
                // Let's assume daily/hourly from date diff? 
                // Or just grab it from chart title? A bit hacky.
                // Or better: store aggregationLevel in a global variable.
                
                // For now, let's infer or default. 
                // Actually, the updateChart needs it to enable/disable markers.
                // Let's store it globally when received.
                // (I will add 'currentAggregationLevel' global var in next step if needed, 
                // but simpler: check activeSeries[0].dataPoints frequency? 
                // Let's modify updateInfo to NOT need it for tags, but updateChart DOES.
                
                // Let's try to pass the last known aggregation level.
                // I will add a global var `currentAggregationLevel`.
                updateChart(activeSeries, currentAggregationLevel);
                renderTable(activeSeries);
                updateInfo(activeSeries, currentAggregationLevel);
            }
        }
    };

    function exportToExcel() {
        if (!analysisData || !analysisData.series || analysisData.series.length === 0) {
            alert('No hay datos para exportar');
            return;
        }

        // Prepare data for SheetJS
        const exportData = [];
        
        // Header
        exportData.push(["Fecha/Hora", "Estación", "Variable", "Valor"]);

        // Rows
        analysisData.series.forEach(s => {
            s.dataPoints.forEach(dp => {
                exportData.push([
                    new Date(dp.timestamp).toLocaleString('es-MX'),
                    s.stationName,
                    s.variableDisplayName,
                    dp.value
                ]);
            });
        });

        // Create Worksheet
        const ws = XLSX.utils.aoa_to_sheet(exportData);
        // Create Workbook
        const wb = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(wb, ws, "Datos");

        // Write File
        XLSX.writeFile(wb, "Analisis_Datos_CFE.xlsx");
    }
    function exportToCSV() {
        if (!analysisData) {
            alert('No hay datos para exportar');
            return;
        }

        let csv = 'Fecha/Hora,Estación,Valor\n';

        analysisData.series.forEach(s => {
            s.dataPoints.forEach(dp => {
                const timestamp = new Date(dp.timestamp).toLocaleString('es-MX');
                const value = dp.value !== null ? dp.value.toFixed(2) : '';
                csv += `"${timestamp}","${s.stationName}",${value}\n`;
            });
        });

        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
        const link = document.createElement('a');
        const url = URL.createObjectURL(blob);
        link.setAttribute('href', url);
        link.setAttribute('download', `analisis_${analysisData.variable}_${new Date().toISOString().split('T')[0]}.csv`);
        link.style.visibility = 'hidden';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    }
</script>
