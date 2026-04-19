/**
 * CloudStation Pro – Chart Configuration System
 * Adds configurable themes, series toggle, color picker, maximize to all Highcharts charts.
 * Usage: include this script after highcharts.js and before any chart creation.
 */
(function (window, Highcharts) {
    'use strict';

    // ───── Theme Definitions ─────
    var CS_THEMES = {
        dark: {
            name: 'Oscuro',
            icon: 'moon',
            chart: { backgroundColor: 'transparent', style: { fontFamily: "'Segoe UI',sans-serif" } },
            title: { style: { color: '#e8f5e9', fontSize: '14px', fontWeight: '700' } },
            subtitle: { style: { color: '#78909c' } },
            xAxis: { labels: { style: { color: '#78909c' } }, gridLineColor: 'rgba(255,255,255,0.05)', lineColor: 'rgba(255,255,255,0.1)', tickColor: 'rgba(255,255,255,0.1)', title: { style: { color: '#78909c' } } },
            yAxis: { labels: { style: { color: '#78909c' } }, gridLineColor: 'rgba(255,255,255,0.05)', title: { style: { color: '#78909c' } } },
            legend: { itemStyle: { color: '#b0bec5' }, itemHoverStyle: { color: '#fff' } },
            tooltip: { backgroundColor: 'rgba(15,25,30,0.95)', borderColor: '#00e676', style: { color: '#e8f5e9' } },
            plotOptions: { series: { dataLabels: { color: '#b0bec5' } } },
            credits: { enabled: false }
        },
        light: {
            name: 'Claro',
            icon: 'sun',
            chart: { backgroundColor: '#ffffff', style: { fontFamily: "'Segoe UI',sans-serif" } },
            title: { style: { color: '#333333', fontSize: '14px', fontWeight: '700' } },
            subtitle: { style: { color: '#666' } },
            xAxis: { labels: { style: { color: '#555' } }, gridLineColor: '#e0e0e0', lineColor: '#ccc', tickColor: '#ccc', title: { style: { color: '#555' } } },
            yAxis: { labels: { style: { color: '#555' } }, gridLineColor: '#e0e0e0', title: { style: { color: '#555' } } },
            legend: { itemStyle: { color: '#333' }, itemHoverStyle: { color: '#000' } },
            tooltip: { backgroundColor: 'rgba(255,255,255,0.96)', borderColor: '#2185d0', style: { color: '#333' } },
            plotOptions: { series: { dataLabels: { color: '#333' } } },
            credits: { enabled: false }
        },
        blue: {
            name: 'Azul',
            icon: 'tint',
            chart: { backgroundColor: '#0a1929', style: { fontFamily: "'Segoe UI',sans-serif" } },
            title: { style: { color: '#90caf9', fontSize: '14px', fontWeight: '700' } },
            subtitle: { style: { color: '#5c6bc0' } },
            xAxis: { labels: { style: { color: '#5c6bc0' } }, gridLineColor: 'rgba(92,107,192,0.15)', lineColor: 'rgba(92,107,192,0.3)', tickColor: 'rgba(92,107,192,0.3)', title: { style: { color: '#5c6bc0' } } },
            yAxis: { labels: { style: { color: '#5c6bc0' } }, gridLineColor: 'rgba(92,107,192,0.15)', title: { style: { color: '#5c6bc0' } } },
            legend: { itemStyle: { color: '#7986cb' }, itemHoverStyle: { color: '#90caf9' } },
            tooltip: { backgroundColor: 'rgba(10,25,41,0.96)', borderColor: '#42a5f5', style: { color: '#90caf9' } },
            plotOptions: { series: { dataLabels: { color: '#7986cb' } } },
            credits: { enabled: false }
        },
        highContrast: {
            name: 'Alto Contraste',
            icon: 'adjust',
            chart: { backgroundColor: '#000000', style: { fontFamily: "'Segoe UI',sans-serif" } },
            title: { style: { color: '#ffffff', fontSize: '14px', fontWeight: '700' } },
            subtitle: { style: { color: '#cccccc' } },
            xAxis: { labels: { style: { color: '#ffffff' } }, gridLineColor: 'rgba(255,255,255,0.2)', lineColor: '#fff', tickColor: '#fff', title: { style: { color: '#fff' } } },
            yAxis: { labels: { style: { color: '#ffffff' } }, gridLineColor: 'rgba(255,255,255,0.2)', title: { style: { color: '#fff' } } },
            legend: { itemStyle: { color: '#ffffff' }, itemHoverStyle: { color: '#ffff00' } },
            tooltip: { backgroundColor: 'rgba(0,0,0,0.96)', borderColor: '#ffff00', style: { color: '#fff' } },
            plotOptions: { series: { dataLabels: { color: '#fff' } } },
            credits: { enabled: false }
        }
    };

    // Color palettes for series
    var CS_PALETTES = {
        default:  ['#00e676','#00b0ff','#ffab40','#ef5350','#e040fb','#26c6da','#fdd835','#8d6e63','#78909c','#66bb6a'],
        vivid:    ['#f44336','#e91e63','#9c27b0','#2196f3','#00bcd4','#4caf50','#ff9800','#795548','#607d8b','#ffeb3b'],
        pastel:   ['#ef9a9a','#ce93d8','#90caf9','#80cbc4','#a5d6a7','#fff59d','#ffab91','#bcaaa4','#b0bec5','#ffe082'],
        ocean:    ['#0d47a1','#1565c0','#1976d2','#1e88e5','#2196f3','#42a5f5','#64b5f6','#90caf9','#bbdefb','#e3f2fd'],
        sunset:   ['#b71c1c','#d32f2f','#e53935','#f44336','#ef5350','#ff5722','#ff7043','#ff8a65','#ffab91','#ffccbc'],
        neon:     ['#00e676','#00e5ff','#d500f9','#ffea00','#ff3d00','#76ff03','#1de9b6','#f50057','#651fff','#ff9100']
    };

    // ───── State ─────
    var _currentTheme = localStorage.getItem('cs_chart_theme') || 'dark';
    var _currentPalette = localStorage.getItem('cs_chart_palette') || 'default';
    var _allCharts = []; // track all enhanced charts

    // ───── Fullscreen Overlay (singleton) ─────
    var _fsOverlay = null;
    function ensureFsOverlay() {
        if (_fsOverlay) return;
        var div = document.createElement('div');
        div.id = 'csChartFullscreenOverlay';
        div.className = 'cs-chart-fs-overlay';
        div.innerHTML =
            '<div class="cs-fs-header">' +
                '<span class="cs-fs-title" id="csFsTitle"></span>' +
                '<div class="cs-fs-actions">' +
                    '<button class="cs-fs-btn" id="csFsThemeBtn" title="Cambiar tema"><i class="icon adjust"></i></button>' +
                    '<button class="cs-fs-btn cs-fs-close" id="csFsCloseBtn" title="Cerrar"><i class="icon compress"></i> Cerrar</button>' +
                '</div>' +
            '</div>' +
            '<div class="cs-fs-chart" id="csFsChartContainer"></div>';
        document.body.appendChild(div);
        _fsOverlay = div;
        document.getElementById('csFsCloseBtn').addEventListener('click', closeFsOverlay);
        div.addEventListener('click', function(e) { if (e.target === div) closeFsOverlay(); });
        document.addEventListener('keydown', function(e) { if (e.key === 'Escape' && _fsOverlay.classList.contains('active')) closeFsOverlay(); });
    }

    function openFsOverlay(chartObj, title) {
        ensureFsOverlay();
        document.getElementById('csFsTitle').textContent = title || 'Gráfica';
        _fsOverlay.classList.add('active');
        document.body.style.overflow = 'hidden';
        setTimeout(function () {
            var container = document.getElementById('csFsChartContainer');
            var opts = buildChartOptions(chartObj.userOptions || chartObj.options, true);
            opts.chart = opts.chart || {};
            opts.chart.height = container.offsetHeight;
            opts.chart.width = null;
            Highcharts.chart('csFsChartContainer', opts);
        }, 80);
    }

    function closeFsOverlay() {
        if (!_fsOverlay) return;
        _fsOverlay.classList.remove('active');
        document.body.style.overflow = '';
        var c = document.getElementById('csFsChartContainer');
        if (c) c.innerHTML = '';
    }

    // ───── Configuration Panel ─────
    function showConfigPanel(chartObj, anchorEl) {
        closeConfigPanel();
        var panel = document.createElement('div');
        panel.id = 'csChartConfigPanel';
        panel.className = 'cs-chart-config-panel';

        var html = '<div class="cs-cfg-section"><label>Tema</label><div class="cs-cfg-themes">';
        Object.keys(CS_THEMES).forEach(function(k) {
            html += '<button class="cs-cfg-theme-btn' + (k === _currentTheme ? ' active' : '') + '" data-theme="' + k + '">' +
                '<i class="icon ' + CS_THEMES[k].icon + '"></i> ' + CS_THEMES[k].name + '</button>';
        });
        html += '</div></div>';

        html += '<div class="cs-cfg-section"><label>Paleta de Colores</label><div class="cs-cfg-palettes">';
        Object.keys(CS_PALETTES).forEach(function(k) {
            html += '<button class="cs-cfg-palette-btn' + (k === _currentPalette ? ' active' : '') + '" data-palette="' + k + '" title="' + k + '">';
            CS_PALETTES[k].slice(0, 5).forEach(function(c) {
                html += '<span class="cs-cfg-swatch" style="background:' + c + '"></span>';
            });
            html += '</button>';
        });
        html += '</div></div>';

        // Series visibility
        if (chartObj && chartObj.series) {
            html += '<div class="cs-cfg-section"><label>Series</label><div class="cs-cfg-series">';
            chartObj.series.forEach(function(s, i) {
                if (s.name === 'Navigator') return;
                var vis = s.visible !== false;
                var col = s.color || '#ccc';
                html += '<div class="cs-cfg-series-row">' +
                    '<input type="checkbox" data-idx="' + i + '" ' + (vis ? 'checked' : '') + ' class="cs-cfg-series-chk">' +
                    '<input type="color" value="' + toHex(col) + '" data-idx="' + i + '" class="cs-cfg-series-color" title="Cambiar color">' +
                    '<span class="cs-cfg-series-name">' + (s.name || 'Serie ' + (i + 1)) + '</span>' +
                '</div>';
            });
            html += '</div></div>';
        }

        html += '<div class="cs-cfg-section cs-cfg-footer">' +
            '<button class="cs-cfg-apply" id="csCfgApply">Aplicar</button>' +
            '<button class="cs-cfg-close" id="csCfgClose">Cerrar</button>' +
            '</div>';

        panel.innerHTML = html;
        document.body.appendChild(panel);

        // Position near anchor
        if (anchorEl) {
            var rect = anchorEl.getBoundingClientRect();
            panel.style.top = Math.min(rect.bottom + 5, window.innerHeight - panel.offsetHeight - 10) + 'px';
            panel.style.left = Math.min(rect.left, window.innerWidth - 320) + 'px';
        } else {
            panel.style.top = '50%';
            panel.style.left = '50%';
            panel.style.transform = 'translate(-50%, -50%)';
        }

        // Events
        panel.querySelectorAll('.cs-cfg-theme-btn').forEach(function(btn) {
            btn.addEventListener('click', function() {
                panel.querySelectorAll('.cs-cfg-theme-btn').forEach(function(b) { b.classList.remove('active'); });
                btn.classList.add('active');
            });
        });
        panel.querySelectorAll('.cs-cfg-palette-btn').forEach(function(btn) {
            btn.addEventListener('click', function() {
                panel.querySelectorAll('.cs-cfg-palette-btn').forEach(function(b) { b.classList.remove('active'); });
                btn.classList.add('active');
            });
        });

        document.getElementById('csCfgApply').addEventListener('click', function() {
            var selTheme = panel.querySelector('.cs-cfg-theme-btn.active');
            var selPalette = panel.querySelector('.cs-cfg-palette-btn.active');
            if (selTheme) {
                _currentTheme = selTheme.dataset.theme;
                localStorage.setItem('cs_chart_theme', _currentTheme);
            }
            if (selPalette) {
                _currentPalette = selPalette.dataset.palette;
                localStorage.setItem('cs_chart_palette', _currentPalette);
            }
            // Apply series visibility/color
            panel.querySelectorAll('.cs-cfg-series-chk').forEach(function(chk) {
                var idx = parseInt(chk.dataset.idx);
                if (chartObj.series[idx]) {
                    if (chk.checked) chartObj.series[idx].show(); else chartObj.series[idx].hide();
                }
            });
            panel.querySelectorAll('.cs-cfg-series-color').forEach(function(cp) {
                var idx = parseInt(cp.dataset.idx);
                if (chartObj.series[idx]) {
                    chartObj.series[idx].update({ color: cp.value }, false);
                }
            });
            // Re-apply theme to all charts
            applyGlobalTheme();
            chartObj.redraw();
            closeConfigPanel();
        });
        document.getElementById('csCfgClose').addEventListener('click', closeConfigPanel);

        // Close on outside click
        setTimeout(function() {
            document.addEventListener('mousedown', _cfgOutsideClick);
        }, 50);
    }

    var _cfgOutsideClick = function(e) {
        var panel = document.getElementById('csChartConfigPanel');
        if (panel && !panel.contains(e.target) && !e.target.closest('.cs-chart-toolbar-btn')) {
            closeConfigPanel();
        }
    };

    function closeConfigPanel() {
        var p = document.getElementById('csChartConfigPanel');
        if (p) p.remove();
        document.removeEventListener('mousedown', _cfgOutsideClick);
    }

    // ───── Toolbar Injection ─────
    function injectToolbar(chartObj) {
        var container = chartObj.container;
        if (!container) return;
        var parent = container.parentNode;
        if (!parent || parent.querySelector('.cs-chart-toolbar')) return;

        var toolbar = document.createElement('div');
        toolbar.className = 'cs-chart-toolbar';

        var btnConfig = document.createElement('button');
        btnConfig.className = 'cs-chart-toolbar-btn';
        btnConfig.innerHTML = '<i class="icon sliders horizontal"></i>';
        btnConfig.title = 'Configurar gráfica';
        btnConfig.addEventListener('click', function(e) {
            e.stopPropagation();
            showConfigPanel(chartObj, btnConfig);
        });

        var btnMaximize = document.createElement('button');
        btnMaximize.className = 'cs-chart-toolbar-btn';
        btnMaximize.innerHTML = '<i class="icon expand"></i>';
        btnMaximize.title = 'Maximizar';
        btnMaximize.addEventListener('click', function(e) {
            e.stopPropagation();
            var t = chartObj.title ? chartObj.title.textStr : 'Gráfica';
            openFsOverlay(chartObj, t);
        });

        var btnExport = document.createElement('button');
        btnExport.className = 'cs-chart-toolbar-btn';
        btnExport.innerHTML = '<i class="icon download"></i>';
        btnExport.title = 'Exportar';
        btnExport.addEventListener('click', function(e) {
            e.stopPropagation();
            if (chartObj.exportChart) {
                chartObj.exportChart({ type: 'image/png' });
            }
        });

        toolbar.appendChild(btnConfig);
        toolbar.appendChild(btnMaximize);
        toolbar.appendChild(btnExport);

        parent.style.position = 'relative';
        parent.appendChild(toolbar);
    }

    // ───── Theme Application ─────
    function buildChartOptions(userOpts, forFullscreen) {
        var theme = CS_THEMES[_currentTheme] || CS_THEMES.dark;
        var merged = Highcharts.merge(true, {}, theme, userOpts || {});
        if (forFullscreen) {
            merged.chart = merged.chart || {};
            merged.chart.backgroundColor = theme.chart.backgroundColor === 'transparent'
                ? 'rgba(5,10,15,0.98)' : theme.chart.backgroundColor;
        }
        // Apply palette to series that don't have explicit colors
        var palette = CS_PALETTES[_currentPalette] || CS_PALETTES.default;
        if (merged.series) {
            merged.series.forEach(function(s, i) {
                if (!userOpts.series || !userOpts.series[i] || !userOpts.series[i].color) {
                    s.color = palette[i % palette.length];
                }
            });
        }
        return merged;
    }

    function applyGlobalTheme() {
        var theme = CS_THEMES[_currentTheme] || CS_THEMES.dark;
        _allCharts.forEach(function(ref) {
            if (!ref.chart || ref.chart.renderer === undefined) return;
            try {
                ref.chart.update({
                    chart: { backgroundColor: theme.chart.backgroundColor },
                    title: { style: theme.title.style },
                    subtitle: { style: theme.subtitle ? theme.subtitle.style : {} },
                    xAxis: { labels: { style: theme.xAxis.labels.style }, gridLineColor: theme.xAxis.gridLineColor, lineColor: theme.xAxis.lineColor },
                    yAxis: Array.isArray(ref.chart.yAxis) ? ref.chart.yAxis.map(function() { return { labels: { style: theme.yAxis.labels.style }, gridLineColor: theme.yAxis.gridLineColor }; }) : { labels: { style: theme.yAxis.labels.style }, gridLineColor: theme.yAxis.gridLineColor },
                    legend: theme.legend,
                    tooltip: theme.tooltip
                }, true, false);
            } catch (e) { /* chart may have been destroyed */ }
        });
    }

    // ───── Wrap Highcharts.chart ─────
    var _origChart = Highcharts.chart;
    var _origStock = Highcharts.stockChart;

    function enhancedChart() {
        var args = Array.prototype.slice.call(arguments);
        // Highcharts.chart(container, options) or (options)
        var optsIdx = typeof args[0] === 'string' || (args[0] && args[0].nodeName) ? 1 : 0;
        var userOpts = args[optsIdx] || {};
        var mergedOpts = buildChartOptions(userOpts, false);
        // Ensure exporting & toolbar
        mergedOpts.exporting = mergedOpts.exporting || {};
        mergedOpts.exporting.enabled = mergedOpts.exporting.enabled !== false;
        mergedOpts.credits = { enabled: false };
        args[optsIdx] = mergedOpts;

        var chart = _origChart.apply(Highcharts, args);
        _allCharts.push({ chart: chart, userOpts: userOpts });
        try { injectToolbar(chart); } catch (e) {}
        return chart;
    }

    function enhancedStockChart() {
        var args = Array.prototype.slice.call(arguments);
        var optsIdx = typeof args[0] === 'string' || (args[0] && args[0].nodeName) ? 1 : 0;
        var userOpts = args[optsIdx] || {};
        var mergedOpts = buildChartOptions(userOpts, false);
        mergedOpts.exporting = mergedOpts.exporting || {};
        mergedOpts.exporting.enabled = mergedOpts.exporting.enabled !== false;
        mergedOpts.credits = { enabled: false };
        args[optsIdx] = mergedOpts;

        var chart = _origStock.apply(Highcharts, args);
        _allCharts.push({ chart: chart, userOpts: userOpts });
        try { injectToolbar(chart); } catch (e) {}
        return chart;
    }

    Highcharts.chart = enhancedChart;
    if (_origStock) Highcharts.stockChart = enhancedStockChart;

    // ───── Helper: color to hex ─────
    function toHex(color) {
        if (!color) return '#cccccc';
        if (/^#[0-9a-f]{6}$/i.test(color)) return color;
        if (/^#[0-9a-f]{3}$/i.test(color)) {
            return '#' + color[1]+color[1]+color[2]+color[2]+color[3]+color[3];
        }
        var m = (color + '').match(/rgba?\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
        if (m) {
            return '#' + ('0' + parseInt(m[1]).toString(16)).slice(-2) +
                ('0' + parseInt(m[2]).toString(16)).slice(-2) +
                ('0' + parseInt(m[3]).toString(16)).slice(-2);
        }
        return '#cccccc';
    }

    // ───── Public API ─────
    window.CSChartConfig = {
        themes: CS_THEMES,
        palettes: CS_PALETTES,
        setTheme: function(t) { _currentTheme = t; localStorage.setItem('cs_chart_theme', t); applyGlobalTheme(); },
        setPalette: function(p) { _currentPalette = p; localStorage.setItem('cs_chart_palette', p); },
        getTheme: function() { return _currentTheme; },
        getPalette: function() { return _currentPalette; },
        maximize: openFsOverlay,
        getAllCharts: function() { return _allCharts; },
        applyTheme: applyGlobalTheme
    };

})(window, Highcharts);
