/**
 * Cascade 3D - Sistema Hidroeléctrico Grijalva
 * Three.js visualization with panoramic intro, turbine units, and animation controls
 */
(function () {
    'use strict';

    /* ====== STATE ====== */
    var _scene, _camera, _renderer, _clock;
    var _container, _canvas;
    var _waterMeshes = [];
    var _waterfallSystems = [];
    var _riverSystems = [];
    var _turbineMeshes = [];
    var _ambientParticles;
    var _glowLights = [];
    var _sparkleSystems = [];
    var _animId = null;
    var _initialized = false;
    var _disposed = false;
    var _animState = 'playing';

    /* ====== CAMERA ====== */
    var _camAngle = Math.PI * 0.18;
    var _camRadius = 42;
    var _camHeight = 24;
    var _isMouseDown = false;
    var _prevMouseX = 0;
    var _prevMouseY = 0;
    var _autoRotate = false;
    var _autoRotateTimer = null;
    var _lookAtX = 3, _lookAtY = -1, _lookAtZ = 0;

    /* ====== PANORAMIC INTRO ====== */
    var _introActive = false;
    var _introDuration = 25.0;
    var _introElapsed = 0;
    var _introStartAngle = Math.PI * 0.03;
    var _introEndAngle = Math.PI * 0.22;
    var _introStartRadius = 36;
    var _introEndRadius = 34;
    var _introStartHeight = 18;
    var _introEndHeight = 22;
    var _introStartLookX = 0;
    var _introEndLookX = 0;
    var _introStartLookY = -1;
    var _introEndLookY = 0;

    /* ====== DAM CONFIG (fallback – overridden by data from server) ====== */
    var DAM_REFS = {
        'Angostura':           { namo: 539.00, namin: 510.40 },
        'Chicoasen':           { namo: 395.00, namin: 378.50 },
        'Malpaso':             { namo: 189.70, namin: 163.00 },
        'Tapon_Juan_Grijalva': { namo: 100.00, namin:  87.00 },
        'Penitas':             { namo:  95.10, namin:  84.50 }
    };

    var DAM_ORDER = ['Angostura', 'Chicoasen', 'Malpaso', 'Tapon_Juan_Grijalva', 'Penitas'];

    var DAM_UNITS = {
        'Angostura': 5,
        'Chicoasen': 8,
        'Malpaso': 6,
        'Tapon_Juan_Grijalva': 0,
        'Penitas': 4
    };

    var DAM_W = 5.5, DAM_D = 4.0, WALL_H = 3.0, WALL_T = 0.25;
    var SPACING = 10.5, STEP = 1.8;
    var COLORS = [0x00e676, 0x00bcd4, 0x2196f3, 0xff7043, 0xffc107];
    var RIVER_COLOR = 0x0288d1;
    var GULF_COLOR = 0x01579b;

    /* ====== UTILITIES ====== */
    function fillPct(key, elev, damData) {
        // Prefer dynamic data from server, fall back to hardcoded DAM_REFS
        var namo = (damData && damData.namo != null) ? damData.namo : (DAM_REFS[key] ? DAM_REFS[key].namo : null);
        var namin = (damData && damData.namino != null) ? damData.namino : (DAM_REFS[key] ? DAM_REFS[key].namin : null);
        if (namo == null || namin == null || elev == null) return 0.55;
        return Math.max(0.05, Math.min(1.0, (elev - namin) / (namo - namin)));
    }
    function _smoothstep(t) { t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t); }
    function _lerp(a, b, t) { return a + (b - a) * t; }
    function _rr(ctx,x,y,w,h,r){ctx.beginPath();ctx.moveTo(x+r,y);ctx.lineTo(x+w-r,y);ctx.quadraticCurveTo(x+w,y,x+w,y+r);ctx.lineTo(x+w,y+h-r);ctx.quadraticCurveTo(x+w,y+h,x+w-r,y+h);ctx.lineTo(x+r,y+h);ctx.quadraticCurveTo(x,y+h,x,y+h-r);ctx.lineTo(x,y+r);ctx.quadraticCurveTo(x,y,x+r,y);ctx.closePath();}

    /* ====== PUBLIC: INIT ====== */
    window.initCascade3D = function (containerId, damsData) {
        if (typeof THREE === 'undefined') { console.warn('Three.js not loaded'); return; }
        if (_initialized && !_disposed) { _onResize(); return; }

        // Use data order as-is (server sends sorted by SortOrder); fall back to DAM_ORDER for legacy data
        if (damsData[0] && damsData[0].namo == null) {
            // Legacy data without config — sort by DAM_ORDER
            damsData.sort(function (a, b) {
                var ia = DAM_ORDER.indexOf(a.key), ib = DAM_ORDER.indexOf(b.key);
                if (ia === -1) ia = 99; if (ib === -1) ib = 99;
                return ia - ib;
            });
        }

        _container = document.getElementById(containerId);
        _canvas = document.getElementById('cascade3d-canvas');
        if (!_container || !_canvas) return;

        var W = _container.clientWidth, H = _container.clientHeight || 600;
        _renderer = new THREE.WebGLRenderer({ canvas: _canvas, antialias: true, alpha: false });
        _renderer.setSize(W, H);
        _renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        _renderer.shadowMap.enabled = true;

        _scene = new THREE.Scene();
        _scene.background = new THREE.Color(0x030a10);
        _scene.fog = new THREE.FogExp2(0x030a10, 0.005);

        _camera = new THREE.PerspectiveCamera(42, W / H, 0.1, 400);
        _clock = new THREE.Clock();

        _introActive = false;
        _introElapsed = 0;
        _autoRotate = false;
        _animState = 'playing';
        _updateCamera();

        _scene.add(new THREE.AmbientLight(0x1a2a40, 0.9));
        var dir = new THREE.DirectionalLight(0x8899bb, 1.0);
        dir.position.set(15, 30, 20); dir.castShadow = true;
        _scene.add(dir);
        var dir2 = new THREE.DirectionalLight(0x2244aa, 0.3);
        dir2.position.set(-10, 5, -10);
        _scene.add(dir2);

        var n = damsData.length;
        var totalW = (n + 1) * SPACING;
        var sx = -totalW / 2;

        _buildFloor(sx, n + 1);
        _buildStars();
        _buildAmbient();
        _sparkleSystems = [];

        for (var i = 0; i < n; i++) {
            var d = damsData[i];
            var x = sx + i * SPACING;
            var by = -i * STEP;
            var fp = fillPct(d.key, d.currentElev, d);
            var col = (d.color ? parseInt(d.color.replace('#',''), 16) : COLORS[i % COLORS.length]);
            _buildDam(x, by, fp, col, d);
            _buildSparkles(x, by, fp, col);

            var totalU = (d.totalUnits != null) ? d.totalUnits : (DAM_UNITS[d.key] || 0);
            var activeU = d.activeUnits || 0;
            if (totalU > 0) {
                _buildTurbines(x, by, totalU, activeU, col);
            }
            if (d.isTapon || d.key === 'Tapon_Juan_Grijalva') {
                _buildTunnels(x, by, fp, col);
            }

            if (i < n - 1) {
                var nextX = sx + (i + 1) * SPACING;
                var nby = -(i + 1) * STEP;
                var fp2 = fillPct(damsData[i + 1].key, damsData[i + 1].currentElev, damsData[i + 1]);
                _buildRiver(x, by, fp, nextX, nby, fp2, col);
            }
        }

        var lastI = n - 1;
        var lastX = sx + lastI * SPACING;
        var lastBy = -lastI * STEP;
        var lastFp = fillPct(damsData[lastI].key, damsData[lastI].currentElev, damsData[lastI]);
        var gulfX = sx + n * SPACING;
        var gulfBy = -n * STEP;
        _buildRiver(lastX, lastBy, lastFp, gulfX, gulfBy, 0.5, COLORS[lastI % COLORS.length]);
        _buildGulf(gulfX, gulfBy);

        _canvas.addEventListener('mousedown', _mdHandler);
        _canvas.addEventListener('mousemove', _mmHandler);
        _canvas.addEventListener('mouseup', _muHandler);
        _canvas.addEventListener('mouseleave', _muHandler);
        _canvas.addEventListener('wheel', _wheelHandler, { passive: false });
        _canvas.addEventListener('touchstart', _tsHandler, { passive: false });
        _canvas.addEventListener('touchmove', _tmHandler, { passive: false });
        _canvas.addEventListener('touchend', _muHandler);
        window.addEventListener('resize', _onResize);
        document.addEventListener('fullscreenchange', function () { setTimeout(_onResize, 150); });

        _initialized = true; _disposed = false;
        _updateControlButtons('playing');
        _animate();
    };

    window.destroyCascade3D = function () {
        _disposed = true;
        if (_animId) cancelAnimationFrame(_animId);
        if (_renderer) _renderer.dispose();
        _initialized = false;
    };
    window.resizeCascade3D = function () { _onResize(); };

    window.toggle3DFullscreen = function () {
        var el = document.getElementById('cascade3d-wrapper');
        if (!el) return;
        if (!document.fullscreenElement) {
            (el.requestFullscreen || el.webkitRequestFullscreen || el.msRequestFullscreen).call(el);
        } else {
            (document.exitFullscreen || document.webkitExitFullscreen || document.msExitFullscreen).call(document);
        }
    };

    /* ====== PUBLIC: EXPORT IMAGE ====== */
    window.exportCascade3DImage = function () {
        if (!_renderer || !_scene || !_camera) return;

        // Render one frame to ensure canvas is fresh
        _renderer.render(_scene, _camera);

        var srcCanvas = _renderer.domElement;
        var w = 1920, h = 1080;

        var c = document.createElement('canvas');
        c.width = w; c.height = h;
        var ctx = c.getContext('2d');

        // Dark gradient background
        var grad = ctx.createLinearGradient(0, 0, 0, h);
        grad.addColorStop(0, '#0a1628');
        grad.addColorStop(1, '#040c14');
        ctx.fillStyle = grad;
        ctx.fillRect(0, 0, w, h);

        // Draw 3D scene scaled to fill
        ctx.drawImage(srcCanvas, 0, 0, w, h);

        // Semi-transparent header bar
        var headerGrad = ctx.createLinearGradient(0, 0, 0, 80);
        headerGrad.addColorStop(0, 'rgba(4,12,20,0.92)');
        headerGrad.addColorStop(1, 'rgba(4,12,20,0)');
        ctx.fillStyle = headerGrad;
        ctx.fillRect(0, 0, w, 80);

        // CFE Logo/Title
        ctx.font = 'bold 26px "Segoe UI", Arial, sans-serif';
        ctx.fillStyle = '#00e676';
        ctx.textAlign = 'left';
        ctx.shadowColor = 'rgba(0,230,118,0.5)';
        ctx.shadowBlur = 12;
        ctx.fillText('⚡ Sistema Hidroeléctrico Grijalva', 30, 42);
        ctx.shadowBlur = 0;

        // Subtitle
        ctx.font = '600 15px "Segoe UI", Arial, sans-serif';
        ctx.fillStyle = '#4fc3f7';
        ctx.fillText('Cascada en Tiempo Real — Comisión Federal de Electricidad', 30, 66);

        // Timestamp
        var ts = document.getElementById('c3dTimestamp');
        var tsText = ts ? ts.textContent.trim() : '';
        if (tsText) {
            ctx.font = '13px "Segoe UI", Arial, sans-serif';
            ctx.fillStyle = '#80cbc4';
            ctx.textAlign = 'right';
            ctx.fillText(tsText, w - 30, 42);
        }

        // Date
        var now = new Date();
        var dateStr = now.toLocaleDateString('es-MX', { year: 'numeric', month: 'long', day: 'numeric', hour: '2-digit', minute: '2-digit' });
        ctx.font = '12px "Segoe UI", Arial, sans-serif';
        ctx.fillStyle = '#90a4ae';
        ctx.textAlign = 'right';
        ctx.fillText(dateStr, w - 30, 62);

        // Bottom banner with dam data
        var bannerH = 110;
        var bannerY = h - bannerH;
        var bannerGrad = ctx.createLinearGradient(0, bannerY, 0, h);
        bannerGrad.addColorStop(0, 'rgba(4,12,20,0)');
        bannerGrad.addColorStop(0.3, 'rgba(4,12,20,0.88)');
        bannerGrad.addColorStop(1, 'rgba(4,12,20,0.95)');
        ctx.fillStyle = bannerGrad;
        ctx.fillRect(0, bannerY, w, bannerH);

        // Thin accent line
        ctx.strokeStyle = 'rgba(0,230,118,0.4)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(30, bannerY + 30);
        ctx.lineTo(w - 30, bannerY + 30);
        ctx.stroke();

        // Read banner items from DOM
        var items = document.querySelectorAll('.c3d-banner-item');
        var colW = (w - 60) / Math.max(items.length, 1);
        ctx.textAlign = 'center';

        items.forEach(function (item, i) {
            var cx = 30 + colW * i + colW / 2;

            // Dam name
            var nameEl = item.querySelector('.c3d-banner-name');
            var isGulf = item.classList.contains('c3d-banner-gulf');
            ctx.font = 'bold 14px "Segoe UI", Arial, sans-serif';
            ctx.fillStyle = isGulf ? '#4fc3f7' : '#00e676';
            ctx.shadowColor = isGulf ? 'rgba(79,195,247,0.4)' : 'rgba(0,230,118,0.4)';
            ctx.shadowBlur = 6;
            ctx.fillText(nameEl ? nameEl.textContent.trim() : '', cx, bannerY + 48);
            ctx.shadowBlur = 0;

            // Data rows
            var rows = item.querySelectorAll('.c3d-banner-row');
            var ry = bannerY + 66;
            ctx.font = '12px "Segoe UI", Arial, sans-serif';
            rows.forEach(function (row) {
                var label = row.querySelector('.c3d-banner-label');
                var val = row.querySelector('.c3d-banner-val');
                var text = '';
                if (label && val) {
                    text = label.textContent.trim() + ' ' + val.textContent.trim();
                } else if (label) {
                    text = label.textContent.trim();
                }
                ctx.fillStyle = '#b0bec5';
                ctx.fillText(text, cx, ry);
                ry += 16;
            });
        });

        // Watermark
        ctx.font = '11px "Segoe UI", Arial, sans-serif';
        ctx.fillStyle = 'rgba(255,255,255,0.2)';
        ctx.textAlign = 'right';
        ctx.fillText('CloudStation — Subgerencia Técnica Grijalva', w - 20, h - 8);

        // Download
        var link = document.createElement('a');
        link.download = 'Cascada_Grijalva_' + now.toISOString().slice(0, 10) + '.png';
        link.href = c.toDataURL('image/png');
        link.click();
    };

    /* ====== PUBLIC: ANIMATION CONTROLS ====== */
    window.playCascade3D = function () {
        if (_animState === 'playing') return;
        if (_animState === 'stopped') {
            _introActive = false;
            _camAngle = Math.PI * 0.18;
            _camRadius = 42;
            _camHeight = 24;
            _lookAtX = 3;
            _lookAtY = -1;
            _autoRotate = false;
        }
        _animState = 'playing';
        _clock.start();
        _clock.getDelta();
        _animate();
        _updateControlButtons('playing');
    };

    window.pauseCascade3D = function () {
        if (_animState !== 'playing') return;
        _animState = 'paused';
        if (_animId) { cancelAnimationFrame(_animId); _animId = null; }
        _updateControlButtons('paused');
    };

    window.stopCascade3D = function () {
        _animState = 'stopped';
        if (_animId) { cancelAnimationFrame(_animId); _animId = null; }
        _introActive = false;
        _camAngle = Math.PI * 0.18;
        _camRadius = 42;
        _camHeight = 24;
        _lookAtX = 3;
        _lookAtY = -1;
        _autoRotate = false;
        _updateCamera();
        if (_renderer && _scene && _camera) _renderer.render(_scene, _camera);
        _updateControlButtons('stopped');
    };

    function _updateControlButtons(state) {
        var playBtn = document.getElementById('c3d-btn-play');
        var pauseBtn = document.getElementById('c3d-btn-pause');
        var stopBtn = document.getElementById('c3d-btn-stop');
        if (!playBtn) return;
        playBtn.classList.toggle('disabled', state === 'playing');
        pauseBtn.classList.toggle('disabled', state !== 'playing');
        stopBtn.classList.toggle('disabled', state === 'stopped');
        if (state === 'playing') { playBtn.style.opacity = '0.5'; pauseBtn.style.opacity = '1'; stopBtn.style.opacity = '1'; }
        else if (state === 'paused') { playBtn.style.opacity = '1'; pauseBtn.style.opacity = '0.5'; stopBtn.style.opacity = '1'; }
        else { playBtn.style.opacity = '1'; pauseBtn.style.opacity = '0.5'; stopBtn.style.opacity = '0.5'; }
    }

    /* ====== BUILD: FLOOR ====== */
    function _buildFloor(sx, count) {
        var fl = new THREE.Mesh(new THREE.PlaneGeometry(160, 70), new THREE.MeshLambertMaterial({ color: 0x050e08 }));
        fl.rotation.x = -Math.PI / 2; fl.position.y = -count * STEP - 2; fl.receiveShadow = true;
        _scene.add(fl);

        var gr = new THREE.Mesh(
            new THREE.PlaneGeometry(140, 60, 70, 30),
            new THREE.MeshBasicMaterial({ color: 0x00e676, wireframe: true, transparent: true, opacity: 0.025 })
        );
        gr.rotation.x = -Math.PI / 2; gr.position.y = -count * STEP - 1.9;
        _scene.add(gr);

        for (var i = 0; i < count; i++) {
            // Organic terrain platform for each dam site
            var px = sx + i * SPACING, py = -i * STEP - 0.5;
            var platGeo = new THREE.CylinderGeometry(DAM_W * 0.75, DAM_W * 0.85, 0.6, 8);
            var platMat = new THREE.MeshPhongMaterial({ color: 0x1a3015, specular: 0x111111, shininess: 8, flatShading: true });
            var plat = new THREE.Mesh(platGeo, platMat);
            plat.scale.z = DAM_D / DAM_W * 1.1;
            plat.position.set(px, py, 0); plat.receiveShadow = true;
            _scene.add(plat);
        }
    }

    /* ====== BUILD: CRYSTAL WALL HELPERS ====== */
    function _buildTerrainBank(cx, by, zBase, halfLen, bankH, side) {
        var glassMat = new THREE.MeshPhongMaterial({
            color: 0x88ccee, specular: 0xffffff, shininess: 200,
            transparent: true, opacity: 0.18, side: THREE.DoubleSide,
            envMapIntensity: 1.0
        });
        var edgeMat = new THREE.LineBasicMaterial({ color: 0x4fc3f7, transparent: true, opacity: 0.35 });

        var wallLen = halfLen * 2 + 0.4;
        var wallThick = 0.12;
        // Crystal wall panel
        var wallGeo = new THREE.BoxGeometry(wallLen, bankH, wallThick);
        var wall = new THREE.Mesh(wallGeo, glassMat);
        wall.position.set(cx, by + bankH / 2, zBase);
        wall.castShadow = false; _scene.add(wall);
        // Edge highlight
        var wallEdge = new THREE.LineSegments(new THREE.EdgesGeometry(wallGeo), edgeMat);
        wallEdge.position.copy(wall.position); _scene.add(wallEdge);
        // Horizontal reinforcement strips
        var stripCount = 3;
        for (var i = 1; i <= stripCount; i++) {
            var sy = by + (bankH / (stripCount + 1)) * i;
            var strip = new THREE.Mesh(
                new THREE.BoxGeometry(wallLen + 0.1, 0.04, wallThick + 0.06),
                new THREE.MeshPhongMaterial({ color: 0xb0bec5, shininess: 80, transparent: true, opacity: 0.4 })
            );
            strip.position.set(cx, sy, zBase); _scene.add(strip);
        }
        // Top rail
        var rail = new THREE.Mesh(
            new THREE.BoxGeometry(wallLen + 0.2, 0.08, wallThick + 0.12),
            new THREE.MeshPhongMaterial({ color: 0xcfd8dc, shininess: 100, transparent: true, opacity: 0.5 })
        );
        rail.position.set(cx, by + bankH + 0.04, zBase); _scene.add(rail);
    }

    function _buildUpstreamTerrain(xPos, by, halfDepth, bankH) {
        var glassMat = new THREE.MeshPhongMaterial({
            color: 0x88ccee, specular: 0xffffff, shininess: 200,
            transparent: true, opacity: 0.18, side: THREE.DoubleSide
        });
        var edgeMat = new THREE.LineBasicMaterial({ color: 0x4fc3f7, transparent: true, opacity: 0.35 });

        var wallLen = halfDepth * 2 + 0.4;
        var wallThick = 0.12;
        // Crystal wall panel (upstream, perpendicular)
        var wallGeo = new THREE.BoxGeometry(wallThick, bankH, wallLen);
        var wall = new THREE.Mesh(wallGeo, glassMat);
        wall.position.set(xPos, by + bankH / 2, 0);
        _scene.add(wall);
        var wallEdge = new THREE.LineSegments(new THREE.EdgesGeometry(wallGeo), edgeMat);
        wallEdge.position.copy(wall.position); _scene.add(wallEdge);
        // Horizontal strips
        var stripCount = 3;
        for (var i = 1; i <= stripCount; i++) {
            var sy = by + (bankH / (stripCount + 1)) * i;
            var strip = new THREE.Mesh(
                new THREE.BoxGeometry(wallThick + 0.06, 0.04, wallLen + 0.1),
                new THREE.MeshPhongMaterial({ color: 0xb0bec5, shininess: 80, transparent: true, opacity: 0.4 })
            );
            strip.position.set(xPos, sy, 0); _scene.add(strip);
        }
        // Top rail
        var rail = new THREE.Mesh(
            new THREE.BoxGeometry(wallThick + 0.12, 0.08, wallLen + 0.2),
            new THREE.MeshPhongMaterial({ color: 0xcfd8dc, shininess: 100, transparent: true, opacity: 0.5 })
        );
        rail.position.set(xPos, by + bankH + 0.04, 0); _scene.add(rail);
    }

    function _buildCortina(cx, by, cortH, cortD, color) {
        // Trapezoidal dam wall (gravity dam cross-section)
        var baseW = 1.3, topW = 0.35;
        var trapShape = new THREE.Shape();
        trapShape.moveTo(-baseW / 2, 0);
        trapShape.lineTo(baseW / 2, 0);
        trapShape.lineTo(topW / 2, cortH);
        trapShape.lineTo(-topW / 2, cortH);
        trapShape.closePath();
        var extSettings = { depth: cortD + 0.6, bevelEnabled: false };
        var cortGeo = new THREE.ExtrudeGeometry(trapShape, extSettings);
        var concreteMat = new THREE.MeshPhongMaterial({ color: 0x8a8880, specular: 0x555555, shininess: 25 });
        var cortMesh = new THREE.Mesh(cortGeo, concreteMat);
        cortMesh.position.set(cx, by, -(cortD + 0.6) / 2);
        cortMesh.castShadow = true; _scene.add(cortMesh);
        // Concrete edge highlight
        var cortEdge = new THREE.LineSegments(
            new THREE.EdgesGeometry(cortGeo),
            new THREE.LineBasicMaterial({ color: new THREE.Color(color), transparent: true, opacity: 0.3 })
        );
        cortEdge.position.copy(cortMesh.position); _scene.add(cortEdge);
        // Dam crest road
        var crestGeo = new THREE.BoxGeometry(topW + 0.35, 0.1, cortD + 1.0);
        var crest = new THREE.Mesh(crestGeo, new THREE.MeshPhongMaterial({ color: 0xaaaaaa, shininess: 15 }));
        crest.position.set(cx, by + cortH + 0.05, 0); _scene.add(crest);
        // Spillway channel grooves on dam face
        for (var g = 0; g < 3; g++) {
            var gz = -cortD * 0.3 + g * cortD * 0.3;
            var groove = new THREE.Mesh(
                new THREE.BoxGeometry(baseW * 0.15, cortH * 0.6, 0.15),
                new THREE.MeshPhongMaterial({ color: 0x555550, shininess: 10 })
            );
            groove.position.set(cx + baseW * 0.35, by + cortH * 0.55, gz);
            _scene.add(groove);
        }
    }

    /* ====== BUILD: DAM (EMBALSE REALISTA) ====== */
    function _buildDam(x, by, fp, color, data) {
        var hw = DAM_W / 2, hd = DAM_D / 2;
        var wl = fp * WALL_H;
        var cortH = WALL_H + 0.5;

        // --- Cortina (dam wall) on downstream side ---
        _buildCortina(x + hw, by, cortH, DAM_D, color);

        // --- Crystal walls (valley sides) ---
        _buildTerrainBank(x, by, -hd - 1.0, hw + 0.8, cortH * 1.1, -1);  // left bank
        _buildTerrainBank(x, by, hd + 1.0, hw + 0.8, cortH * 1.1, 1);    // right bank

        // --- Basin floor (concave elliptical) ---
        var floorR = hw * 0.9;
        var floorGeo = new THREE.CircleGeometry(floorR, 24);
        floorGeo.rotateX(-Math.PI / 2);
        var floorMesh = new THREE.Mesh(floorGeo, new THREE.MeshPhongMaterial({ color: 0x152a10, shininess: 5, transparent: true, opacity: 0.5 }));
        floorMesh.scale.z = DAM_D / DAM_W;
        floorMesh.position.set(x, by + 0.02, 0); _scene.add(floorMesh);

        // --- Water volume (truncated cone = valley shape) ---
        if (wl > 0.2) {
            var topR = hw * 0.88, botR = hw * 0.45;
            var vol = new THREE.Mesh(
                new THREE.CylinderGeometry(topR, botR, wl - 0.15, 24),
                new THREE.MeshPhongMaterial({ color: color, transparent: true, opacity: 0.15, side: THREE.DoubleSide })
            );
            vol.scale.z = DAM_D / DAM_W;
            vol.position.set(x, by + (wl - 0.15) / 2, 0); _scene.add(vol);
        }

        // --- Water surface (elliptical with wave vertices) ---
        var surfR = hw * 0.88;
        var wg = new THREE.CircleGeometry(surfR, 32, 0, Math.PI * 2);
        // Subdivide for wave animation
        var wGeo = new THREE.PlaneGeometry(surfR * 2, surfR * 2 * (DAM_D / DAM_W), 22, 22);
        wGeo.rotateX(-Math.PI / 2);
        // Clip vertices to ellipse
        var wPos = wGeo.attributes.position;
        for (var vi = 0; vi < wPos.count; vi++) {
            var vx = wPos.getX(vi), vz = wPos.getZ(vi);
            var ex = vx / surfR, ez = vz / (surfR * DAM_D / DAM_W);
            var dist = Math.sqrt(ex * ex + ez * ez);
            if (dist > 1.0) { wPos.setX(vi, vx / dist * 0.99); wPos.setZ(vi, vz / dist * 0.99); }
        }
        var waterMesh = new THREE.Mesh(wGeo, new THREE.MeshPhongMaterial({
            color: color, specular: 0xffffff, shininess: 140,
            transparent: true, opacity: 0.72, side: THREE.DoubleSide
        }));
        waterMesh.position.set(x, by + wl, 0);
        waterMesh.userData = { baseY: by + wl, phase: Math.random() * 6.28 };
        _scene.add(waterMesh); _waterMeshes.push(waterMesh);

        // --- NAMO ring indicator ---
        var namoRing = new THREE.Mesh(
            new THREE.TorusGeometry(surfR, 0.04, 6, 32),
            new THREE.MeshBasicMaterial({ color: color, transparent: true, opacity: 0.45 })
        );
        namoRing.scale.z = DAM_D / DAM_W;
        namoRing.rotation.x = Math.PI / 2;
        namoRing.position.set(x, by + WALL_H, 0); _scene.add(namoRing);

        // --- Glow light ---
        var gl = new THREE.PointLight(color, 0.6, 14);
        gl.position.set(x, by + wl * 0.5, 0);
        _scene.add(gl); _glowLights.push(gl);

        // --- Shimmer particles on water surface ---
        var shimCount = 50, shimPos = new Float32Array(shimCount * 3);
        for (var s = 0; s < shimCount; s++) {
            var sAngle = Math.random() * Math.PI * 2;
            var sR = Math.random() * surfR * 0.85;
            shimPos[s * 3] = x + Math.cos(sAngle) * sR;
            shimPos[s * 3 + 1] = by + wl + 0.05;
            shimPos[s * 3 + 2] = Math.sin(sAngle) * sR * (DAM_D / DAM_W);
        }
        var shimGeo = new THREE.BufferGeometry();
        shimGeo.setAttribute('position', new THREE.BufferAttribute(shimPos, 3));
        _scene.add(new THREE.Points(shimGeo, new THREE.PointsMaterial({
            color: 0xffffff, size: 0.08, transparent: true, opacity: 0.5,
            blending: THREE.AdditiveBlending, depthWrite: false
        })));

        // --- Shoreline foam ring ---
        var foamCount = 60, foamPos = new Float32Array(foamCount * 3);
        for (var f = 0; f < foamCount; f++) {
            var fAng = Math.random() * Math.PI * 2;
            var fR = surfR * (0.88 + Math.random() * 0.12);
            foamPos[f * 3] = x + Math.cos(fAng) * fR;
            foamPos[f * 3 + 1] = by + wl + 0.03;
            foamPos[f * 3 + 2] = Math.sin(fAng) * fR * (DAM_D / DAM_W);
        }
        var foamGeo = new THREE.BufferGeometry();
        foamGeo.setAttribute('position', new THREE.BufferAttribute(foamPos, 3));
        _scene.add(new THREE.Points(foamGeo, new THREE.PointsMaterial({
            color: 0xb2dfdb, size: 0.06, transparent: true, opacity: 0.4,
            blending: THREE.AdditiveBlending, depthWrite: false
        })));

        // --- Label ---
        var totalU = DAM_UNITS[data.key] || 0;
        _buildLabel(x, by + WALL_H + 2.4, data.name, data.currentElev, fp, color, data.generation, data.activeUnits, totalU);
    }

    /* ====== BUILD: TURBINES ====== */
    function _buildTurbines(x, by, totalUnits, activeUnits, color) {
        var turbineR = 0.35;
        var gap = 0.75;
        var startOff = -(totalUnits - 1) * gap / 2;
        var turbX = x + DAM_W / 2 + 1.0;
        var turbY = by + 0.8;

        var housingW = 0.8;
        var housingD = totalUnits * gap + 0.4;
        var housingH = 1.2;
        var housing = new THREE.Mesh(
            new THREE.BoxGeometry(housingW, housingH, housingD),
            new THREE.MeshPhongMaterial({ color: 0x1a2a3a, specular: 0x223344, shininess: 30, transparent: true, opacity: 0.5 })
        );
        housing.position.set(turbX, turbY - housingH / 2 + 0.3, 0);
        _scene.add(housing);

        var housingEdge = new THREE.LineSegments(
            new THREE.EdgesGeometry(new THREE.BoxGeometry(housingW, housingH, housingD)),
            new THREE.LineBasicMaterial({ color: color, transparent: true, opacity: 0.3 })
        );
        housingEdge.position.copy(housing.position);
        _scene.add(housingEdge);

        for (var i = 0; i < totalUnits; i++) {
            var isActive = i < activeUnits;
            var zPos = startOff + i * gap;

            var geo = new THREE.TorusGeometry(turbineR, turbineR * 0.25, 8, 16);
            var mat = new THREE.MeshPhongMaterial({
                color: isActive ? color : 0x333333,
                specular: isActive ? 0xffffff : 0x222222,
                shininess: isActive ? 100 : 15,
                emissive: isActive ? new THREE.Color(color) : new THREE.Color(0x000000),
                emissiveIntensity: isActive ? 0.5 : 0,
                transparent: true,
                opacity: isActive ? 0.9 : 0.4
            });
            var mesh = new THREE.Mesh(geo, mat);
            mesh.position.set(turbX + housingW / 2 + 0.05, turbY, zPos);
            mesh.rotation.y = Math.PI / 2;
            mesh.userData = { active: isActive, speed: isActive ? 0.08 + Math.random() * 0.05 : 0 };
            _scene.add(mesh);
            _turbineMeshes.push(mesh);

            var hubGeo = new THREE.SphereGeometry(turbineR * 0.2, 8, 8);
            var hubMat = new THREE.MeshPhongMaterial({ color: isActive ? 0xffffff : 0x555555, emissive: isActive ? new THREE.Color(color) : new THREE.Color(0), emissiveIntensity: isActive ? 0.3 : 0 });
            var hub = new THREE.Mesh(hubGeo, hubMat);
            hub.position.copy(mesh.position);
            _scene.add(hub);

            if (isActive) {
                var tl = new THREE.PointLight(color, 0.12, 3);
                tl.position.set(turbX + housingW / 2 + 0.2, turbY + 0.3, zPos);
                _scene.add(tl); _glowLights.push(tl);
            }
        }

        _buildTurbineLabel(turbX, turbY + 1.6, totalUnits, activeUnits, color);
    }

    /* ====== BUILD: TUNNELS (Tapón Juan Grijalva) ====== */
    function _buildTunnels(x, by, fp, color) {
        var tunnelR = 0.45;
        var tunnelLen = DAM_W + 2.5;
        var tunnelY = by + 1.5;
        var gap = 1.2;
        var tunnelColor = 0x546e7a;

        for (var t = 0; t < 2; t++) {
            var zOff = (t === 0 ? -gap / 2 : gap / 2);

            // Tunnel cylinder — along X (river direction)
            var cylGeo = new THREE.CylinderGeometry(tunnelR, tunnelR, tunnelLen, 16, 1, true);
            cylGeo.rotateZ(Math.PI / 2);
            var cylMat = new THREE.MeshPhongMaterial({
                color: tunnelColor, specular: 0x90a4ae, shininess: 60,
                transparent: true, opacity: 0.35, side: THREE.DoubleSide
            });
            var cyl = new THREE.Mesh(cylGeo, cylMat);
            cyl.position.set(x, tunnelY, zOff);
            _scene.add(cyl);

            // Tunnel edge rings (entry + exit) along X
            var ringGeo = new THREE.TorusGeometry(tunnelR, 0.06, 8, 24);
            var ringMat = new THREE.MeshPhongMaterial({
                color: color, emissive: new THREE.Color(color), emissiveIntensity: 0.3,
                transparent: true, opacity: 0.8
            });
            var ringEntry = new THREE.Mesh(ringGeo, ringMat);
            ringEntry.position.set(x - tunnelLen / 2, tunnelY, zOff);
            ringEntry.rotation.y = Math.PI / 2;
            _scene.add(ringEntry);

            var ringExit = new THREE.Mesh(ringGeo.clone(), ringMat.clone());
            ringExit.position.set(x + tunnelLen / 2, tunnelY, zOff);
            ringExit.rotation.y = Math.PI / 2;
            _scene.add(ringExit);

            // Inner water volume (semi-transparent cylinder to show water)
            var waterCylGeo = new THREE.CylinderGeometry(tunnelR * 0.75, tunnelR * 0.75, tunnelLen * 0.95, 12);
            waterCylGeo.rotateZ(Math.PI / 2);
            var waterCylMat = new THREE.MeshPhongMaterial({
                color: 0x29b6f6, specular: 0xffffff, shininess: 120,
                transparent: true, opacity: 0.25, side: THREE.DoubleSide
            });
            var waterCyl = new THREE.Mesh(waterCylGeo, waterCylMat);
            waterCyl.position.set(x, tunnelY, zOff);
            _scene.add(waterCyl);

            // Water flow inside tunnel (animated particles along X)
            var flowCount = 60;
            var flowPos = new Float32Array(flowCount * 3);
            for (var f = 0; f < flowCount; f++) {
                var angle = Math.random() * Math.PI * 2;
                var rad = Math.random() * tunnelR * 0.6;
                flowPos[f * 3] = x + (Math.random() - 0.5) * tunnelLen;
                flowPos[f * 3 + 1] = tunnelY + Math.sin(angle) * rad;
                flowPos[f * 3 + 2] = zOff + Math.cos(angle) * rad;
            }
            var flowGeo = new THREE.BufferGeometry();
            flowGeo.setAttribute('position', new THREE.BufferAttribute(flowPos, 3));
            var flowPts = new THREE.Points(flowGeo, new THREE.PointsMaterial({
                color: 0x4fc3f7, size: 0.15, transparent: true, opacity: 0.9,
                blending: THREE.AdditiveBlending, depthWrite: false
            }));
            _scene.add(flowPts);

            _riverSystems.push({
                mesh: flowPts,
                baseX: x,
                baseY: tunnelY,
                baseZ: zOff,
                tunnelR: tunnelR,
                xMin: x - tunnelLen / 2,
                xMax: x + tunnelLen / 2,
                isTunnel: true
            });

            // Glow light inside tunnel
            var tLight = new THREE.PointLight(0x4fc3f7, 0.3, 4);
            tLight.position.set(x, tunnelY, zOff);
            _scene.add(tLight);
            _glowLights.push(tLight);
        }

        // Waterfall at tunnel exit (water falling towards Peñitas)
        var exitX = x + tunnelLen / 2 + 0.2;
        var wfTopY = tunnelY + tunnelR * 0.5;
        var wfBotY = by - 1.0;
        var wfRange = Math.max(0.5, wfTopY - wfBotY);

        // Falling water particles
        var wfCnt = 100, wfPos = new Float32Array(wfCnt * 3), wfVels = [];
        for (var w = 0; w < wfCnt; w++) {
            wfPos[w*3] = exitX + (Math.random()-0.5) * 1.8;
            wfPos[w*3+1] = wfTopY - Math.random() * wfRange;
            wfPos[w*3+2] = (Math.random()-0.5) * gap * 1.5;
            wfVels.push({ vy: -(0.015 + Math.random() * 0.05), vx: 0.005 + Math.random() * 0.008 });
        }
        var wfGeo = new THREE.BufferGeometry(); wfGeo.setAttribute('position', new THREE.BufferAttribute(wfPos, 3));
        var wfPts = new THREE.Points(wfGeo, new THREE.PointsMaterial({
            color: 0x4fc3f7, size: 0.14, transparent: true, opacity: 0.85,
            blending: THREE.AdditiveBlending, depthWrite: false
        }));
        _scene.add(wfPts);

        // Spray mist at the base
        var spCnt = 40, spPos = new Float32Array(spCnt * 3), spVels = [];
        for (var s = 0; s < spCnt; s++) {
            spPos[s*3] = exitX + (Math.random()-0.5) * 2.5;
            spPos[s*3+1] = wfBotY + Math.random() * 1.2;
            spPos[s*3+2] = (Math.random()-0.5) * 2.5;
            spVels.push({ vx: (Math.random()-0.5) * 0.02, vy: 0.003 + Math.random() * 0.015, vz: (Math.random()-0.5) * 0.02, life: Math.random() });
        }
        var spGeo = new THREE.BufferGeometry(); spGeo.setAttribute('position', new THREE.BufferAttribute(spPos, 3));
        var sprayPts = new THREE.Points(spGeo, new THREE.PointsMaterial({
            color: 0xffffff, size: 0.1, transparent: true, opacity: 0.35,
            blending: THREE.AdditiveBlending, depthWrite: false
        }));
        _scene.add(sprayPts);

        // Falling stream cone
        var stream = new THREE.Mesh(
            new THREE.CylinderGeometry(0.15, 0.5, wfRange, 8, 1, true),
            new THREE.MeshBasicMaterial({ color: 0x4fc3f7, transparent: true, opacity: 0.1, side: THREE.DoubleSide })
        );
        stream.position.set(exitX, wfTopY - wfRange / 2, 0);
        _scene.add(stream);

        // Waterfall light
        var wfl = new THREE.PointLight(0x4fc3f7, 0.3, 8);
        wfl.position.set(exitX, (wfTopY + wfBotY) / 2, 0);
        _scene.add(wfl);

        _waterfallSystems.push({
            pts: wfPts, vels: wfVels, topY: wfTopY, botY: wfBotY, cx: exitX,
            spray: sprayPts, spVels: spVels, spBaseY: wfBotY, light: wfl, stream: stream
        });

        // Tunnel label
        _buildTunnelLabel(x, tunnelY + 1.8, color);
    }

    function _buildTunnelLabel(x, y, color) {
        var c = document.createElement('canvas'); c.width = 256; c.height = 64;
        var ctx = c.getContext('2d');
        var hex = '#' + ('000000' + color.toString(16)).slice(-6);
        ctx.fillStyle = 'rgba(4,12,20,0.8)';
        _rr(ctx, 4, 4, 248, 56, 10); ctx.fill();
        ctx.strokeStyle = hex; ctx.lineWidth = 1;
        _rr(ctx, 4, 4, 248, 56, 10); ctx.stroke();
        ctx.textAlign = 'center'; ctx.font = 'bold 20px Segoe UI, Arial';
        ctx.fillStyle = '#4fc3f7'; ctx.shadowColor = '#4fc3f7'; ctx.shadowBlur = 8;
        ctx.fillText('\uD83C\uDF0A 2 T\u00faneles', 128, 28);
        ctx.shadowBlur = 0;
        ctx.font = '14px Segoe UI, Arial'; ctx.fillStyle = 'rgba(255,255,255,0.4)';
        ctx.fillText('Paso de agua', 128, 50);
        var tex = new THREE.CanvasTexture(c);
        var spMat = new THREE.SpriteMaterial({ map: tex, transparent: true, depthWrite: false });
        var sp = new THREE.Sprite(spMat);
        sp.position.set(x, y, 0);
        sp.scale.set(4.5, 1.1, 1);
        _scene.add(sp);
    }

    /* ====== BUILD: TURBINE LABEL ====== */
    function _buildTurbineLabel(x, y, total, active, color) {
        var c = document.createElement('canvas'); c.width = 256; c.height = 64;
        var ctx = c.getContext('2d');
        var hex = '#' + ('000000' + color.toString(16)).slice(-6);
        ctx.fillStyle = 'rgba(4,12,20,0.8)';
        _rr(ctx, 4, 4, 248, 56, 10); ctx.fill();
        ctx.strokeStyle = hex; ctx.lineWidth = 1;
        _rr(ctx, 4, 4, 248, 56, 10); ctx.stroke();
        ctx.textAlign = 'center'; ctx.font = 'bold 22px Segoe UI, Arial';
        ctx.fillStyle = hex; ctx.shadowColor = hex; ctx.shadowBlur = 8;
        ctx.fillText('\u26A1 ' + (active || 0) + '/' + total + ' Unidades', 128, 28);
        ctx.shadowBlur = 0;
        ctx.font = '14px Segoe UI, Arial'; ctx.fillStyle = 'rgba(255,255,255,0.4)';
        ctx.fillText('Generando', 128, 50);
        var tex = new THREE.CanvasTexture(c);
        var sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, transparent: true }));
        sp.scale.set(3.5, 0.9, 1); sp.position.set(x, y, 0);
        _scene.add(sp);
    }

    /* ====== BUILD: LABEL (enhanced) ====== */
    function _buildLabel(x, y, name, elev, fp, color, generation, activeUnits, totalUnits) {
        var c = document.createElement('canvas');
        var hasGen = totalUnits > 0;
        c.width = 512; c.height = hasGen ? 240 : 200;
        var ctx = c.getContext('2d');
        var hex = '#' + ('000000' + color.toString(16)).slice(-6);

        var cardH = hasGen ? 228 : 188;
        ctx.fillStyle = 'rgba(4,12,20,0.85)';
        _rr(ctx, 16, 6, 480, cardH, 18); ctx.fill();
        ctx.strokeStyle = hex; ctx.lineWidth = 2;
        _rr(ctx, 16, 6, 480, cardH, 18); ctx.stroke();

        ctx.strokeStyle = hex; ctx.lineWidth = 3; ctx.shadowColor = hex; ctx.shadowBlur = 15;
        ctx.beginPath(); ctx.moveTo(40, 10); ctx.lineTo(472, 10); ctx.stroke(); ctx.shadowBlur = 0;

        ctx.textAlign = 'center'; ctx.font = 'bold 36px Segoe UI, Arial'; ctx.fillStyle = '#ffffff';
        ctx.shadowColor = hex; ctx.shadowBlur = 12; ctx.fillText(name, 256, 56);

        ctx.font = '28px Segoe UI, Arial'; ctx.fillStyle = hex; ctx.shadowBlur = 8;
        ctx.fillText(elev != null ? elev.toFixed(2) + ' msnm' : 'Sin datos', 256, 96);
        ctx.shadowBlur = 0;

        ctx.fillStyle = 'rgba(255,255,255,0.15)';
        _rr(ctx, 58, 113, 396, 26, 10); ctx.fill();
        ctx.strokeStyle = 'rgba(255,255,255,0.3)'; ctx.lineWidth = 1.5;
        _rr(ctx, 58, 113, 396, 26, 10); ctx.stroke();
        var bw = Math.max(6, 390 * fp);
        ctx.fillStyle = hex; ctx.shadowColor = hex; ctx.shadowBlur = 6;
        _rr(ctx, 61, 116, bw, 20, 8); ctx.fill(); ctx.shadowBlur = 0;
        ctx.font = 'bold 16px Segoe UI, Arial'; ctx.fillStyle = '#ffffff'; ctx.textAlign = 'center';
        ctx.shadowColor = '#000'; ctx.shadowBlur = 4;
        ctx.fillText(Math.round(fp * 100) + '% capacidad', 256, 133);
        ctx.shadowBlur = 0;
        ctx.font = 'bold 13px Segoe UI, Arial'; ctx.fillStyle = 'rgba(255,255,255,0.6)'; ctx.textAlign = 'right';
        ctx.fillText('NAMO', 448, 112);

        if (hasGen) {
            ctx.strokeStyle = 'rgba(255,255,255,0.15)'; ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(60, 148); ctx.lineTo(452, 148); ctx.stroke();

            ctx.textAlign = 'left'; ctx.font = 'bold 22px Segoe UI, Arial'; ctx.fillStyle = '#ffffff';
            ctx.shadowColor = hex; ctx.shadowBlur = 6;
            ctx.fillText('\u26A1 ' + (activeUnits || 0) + '/' + totalUnits + ' Unidades', 60, 175);
            ctx.shadowBlur = 0;

            ctx.textAlign = 'right'; ctx.font = 'bold 22px Segoe UI, Arial'; ctx.fillStyle = '#ffd54f';
            ctx.shadowColor = '#ffc107'; ctx.shadowBlur = 8;
            var genText = generation != null && generation > 0 ? generation.toFixed(1) + ' MWh' : '\u2014';
            ctx.fillText(genText, 452, 175); ctx.shadowBlur = 0;

            var unitBarW = 392;
            var unitW = unitBarW / totalUnits;
            for (var u = 0; u < totalUnits; u++) {
                ctx.fillStyle = u < (activeUnits || 0) ? hex : 'rgba(255,255,255,0.12)';
                ctx.strokeStyle = u < (activeUnits || 0) ? 'rgba(255,255,255,0.4)' : 'rgba(255,255,255,0.08)';
                ctx.lineWidth = 1;
                _rr(ctx, 60 + u * unitW + 1, 188, unitW - 2, 14, 4); ctx.fill(); ctx.stroke();
            }
            ctx.font = 'bold 12px Segoe UI, Arial'; ctx.fillStyle = 'rgba(255,255,255,0.45)'; ctx.textAlign = 'center';
            ctx.fillText('Generando', 256, 218);
        } else {
            ctx.strokeStyle = 'rgba(255,255,255,0.1)'; ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(60, 148); ctx.lineTo(452, 148); ctx.stroke();
            ctx.textAlign = 'center'; ctx.font = 'italic 16px Segoe UI, Arial'; ctx.fillStyle = 'rgba(255,255,255,0.35)';
            ctx.fillText('Cortina de contenci\u00f3n \u2014 Sin generaci\u00f3n', 256, 172);
        }

        var tex = new THREE.CanvasTexture(c);
        var sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, transparent: true }));
        sp.scale.set(7.5, hasGen ? 3.5 : 2.9, 1); sp.position.set(x, y, 0);
        _scene.add(sp);
    }

    /* ====== BUILD: RIVER ====== */
    function _buildRiver(fromX, fromBy, fromFp, toX, toBy, toFp, color) {
        var exit = fromX + DAM_W / 2, enter = toX - DAM_W / 2;
        var midX = (exit + enter) / 2;
        var fromWL = fromBy + fromFp * WALL_H, toWL = toBy + toFp * WALL_H;

        var riverLen = enter - exit, riverW = 2.0;
        var rGeo = new THREE.PlaneGeometry(riverLen, riverW, 40, 6); rGeo.rotateX(-Math.PI / 2);
        var rMat = new THREE.MeshPhongMaterial({ color: RIVER_COLOR, specular: 0x88ccff, shininess: 160, transparent: true, opacity: 0.6, side: THREE.DoubleSide });
        var riverMesh = new THREE.Mesh(rGeo, rMat);
        var riverY = (fromWL + toWL) / 2 - 0.5;
        riverMesh.position.set(midX, riverY, 0);
        riverMesh.userData = { phase: Math.random() * 6.28, slope: (fromWL - toWL) / riverLen, halfLen: riverLen / 2 };
        _scene.add(riverMesh); _waterMeshes.push(riverMesh);

        var bankMat = new THREE.MeshBasicMaterial({ color: 0x1b5e20, transparent: true, opacity: 0.3 });
        [-riverW/2-0.15, riverW/2+0.15].forEach(function(zOff){
            var bk = new THREE.Mesh(new THREE.BoxGeometry(riverLen, 0.12, 0.3), bankMat);
            bk.position.set(midX, riverY + 0.06, zOff); _scene.add(bk);
        });

        var rl = new THREE.PointLight(RIVER_COLOR, 0.3, 12); rl.position.set(midX, riverY - 0.5, 0);
        _scene.add(rl); _glowLights.push(rl);

        var flowCount = 100, flowPos = new Float32Array(flowCount * 3), flowVels = [];
        for (var i = 0; i < flowCount; i++) {
            flowPos[i*3] = exit + Math.random() * riverLen; flowPos[i*3+1] = riverY + 0.08 + Math.random() * 0.15; flowPos[i*3+2] = (Math.random()-0.5) * riverW * 0.85;
            flowVels.push({ vx: 0.015 + Math.random() * 0.03 });
        }
        var flowGeo = new THREE.BufferGeometry(); flowGeo.setAttribute('position', new THREE.BufferAttribute(flowPos, 3));
        var flowPts = new THREE.Points(flowGeo, new THREE.PointsMaterial({ color: 0x4fc3f7, size: 0.12, transparent: true, opacity: 0.7, blending: THREE.AdditiveBlending, depthWrite: false }));
        _scene.add(flowPts);

        var wfTopY = toWL + 0.3, wfBotY = toBy + 0.5, wfX = enter + 0.3;
        var wfCnt = 120, wfPos = new Float32Array(wfCnt * 3), wfVels = [];
        var wfRange = Math.max(0.5, wfTopY - wfBotY);
        for (var j = 0; j < wfCnt; j++) {
            wfPos[j*3] = wfX + (Math.random()-0.5) * 1.2; wfPos[j*3+1] = wfTopY - Math.random() * wfRange; wfPos[j*3+2] = (Math.random()-0.5) * 1.8;
            wfVels.push({ vy: -(0.02 + Math.random() * 0.06), vx: (Math.random()-0.5) * 0.003 });
        }
        var wfGeo = new THREE.BufferGeometry(); wfGeo.setAttribute('position', new THREE.BufferAttribute(wfPos, 3));
        var wfPts = new THREE.Points(wfGeo, new THREE.PointsMaterial({ color: color, size: 0.15, transparent: true, opacity: 0.8, blending: THREE.AdditiveBlending, depthWrite: false }));
        _scene.add(wfPts);

        var spCnt = 50, spPos = new Float32Array(spCnt * 3), spVels = [];
        for (var s = 0; s < spCnt; s++) {
            spPos[s*3] = wfX + (Math.random()-0.5) * 2; spPos[s*3+1] = wfBotY + Math.random() * 1.5; spPos[s*3+2] = (Math.random()-0.5) * 2.5;
            spVels.push({ vx: (Math.random()-0.5) * 0.02, vy: 0.003 + Math.random() * 0.015, vz: (Math.random()-0.5) * 0.02, life: Math.random() });
        }
        var spGeo = new THREE.BufferGeometry(); spGeo.setAttribute('position', new THREE.BufferAttribute(spPos, 3));
        var sprayPts = new THREE.Points(spGeo, new THREE.PointsMaterial({ color: 0xffffff, size: 0.1, transparent: true, opacity: 0.3, blending: THREE.AdditiveBlending, depthWrite: false }));
        _scene.add(sprayPts);

        var streamH = wfRange;
        var stream = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.3, streamH, 8, 1, true), new THREE.MeshBasicMaterial({ color: color, transparent: true, opacity: 0.1, side: THREE.DoubleSide }));
        stream.position.set(wfX, wfTopY - streamH / 2, 0); _scene.add(stream);

        var wfl = new THREE.PointLight(color, 0.25, 8); wfl.position.set(wfX, (wfTopY + wfBotY) / 2, 0); _scene.add(wfl);

        _riverSystems.push({ flow: flowPts, flowVels: flowVels, exit: exit, enter: enter, riverY: riverY, riverW: riverW });
        _waterfallSystems.push({ pts: wfPts, vels: wfVels, topY: wfTopY, botY: wfBotY, cx: wfX, spray: sprayPts, spVels: spVels, spBaseY: wfBotY, light: wfl, stream: stream });
    }

    /* ====== BUILD: GULF OF MEXICO ====== */
    function _buildGulf(x, by) {
        var gulfW = 12, gulfD = 10, gulfY = by - 0.2;

        var ocean = new THREE.Mesh(new THREE.BoxGeometry(gulfW, 2, gulfD), new THREE.MeshPhongMaterial({ color: GULF_COLOR, specular: 0x4488aa, shininess: 120, transparent: true, opacity: 0.25, side: THREE.DoubleSide }));
        ocean.position.set(x + gulfW / 2 - DAM_W / 2, gulfY - 1, 0); _scene.add(ocean);

        var osGeo = new THREE.PlaneGeometry(gulfW, gulfD, 30, 30); osGeo.rotateX(-Math.PI / 2);
        var oceanSurf = new THREE.Mesh(osGeo, new THREE.MeshPhongMaterial({ color: 0x0277bd, specular: 0xffffff, shininess: 200, transparent: true, opacity: 0.65, side: THREE.DoubleSide }));
        oceanSurf.position.set(x + gulfW / 2 - DAM_W / 2, gulfY, 0); oceanSurf.userData = { baseY: gulfY, phase: 0, isOcean: true };
        _scene.add(oceanSurf); _waterMeshes.push(oceanSurf);

        var ogl = new THREE.PointLight(0x0288d1, 0.8, 20); ogl.position.set(x + gulfW / 2 - DAM_W / 2, gulfY - 0.5, 0);
        _scene.add(ogl); _glowLights.push(ogl);

        var foamCnt = 120, foamPos = new Float32Array(foamCnt * 3);
        for (var i = 0; i < foamCnt; i++) {
            foamPos[i*3] = x + gulfW / 2 - DAM_W / 2 + (Math.random()-0.5) * gulfW * 0.9; foamPos[i*3+1] = gulfY + 0.08; foamPos[i*3+2] = (Math.random()-0.5) * gulfD * 0.9;
        }
        var foamGeo = new THREE.BufferGeometry(); foamGeo.setAttribute('position', new THREE.BufferAttribute(foamPos, 3));
        _scene.add(new THREE.Points(foamGeo, new THREE.PointsMaterial({ color: 0xffffff, size: 0.06, transparent: true, opacity: 0.4, blending: THREE.AdditiveBlending, depthWrite: false })));

        _buildGulfLabel(x + gulfW / 2 - DAM_W / 2, gulfY + 5);
    }

    function _buildGulfLabel(x, y) {
        var c = document.createElement('canvas'); c.width = 512; c.height = 140;
        var ctx = c.getContext('2d');
        ctx.fillStyle = 'rgba(1,87,155,0.7)'; _rr(ctx, 16, 6, 480, 128, 18); ctx.fill();
        ctx.strokeStyle = '#0288d1'; ctx.lineWidth = 2; _rr(ctx, 16, 6, 480, 128, 18); ctx.stroke();
        ctx.strokeStyle = '#4fc3f7'; ctx.lineWidth = 3; ctx.shadowColor = '#4fc3f7'; ctx.shadowBlur = 15;
        ctx.beginPath(); ctx.moveTo(40, 10); ctx.lineTo(472, 10); ctx.stroke(); ctx.shadowBlur = 0;
        ctx.textAlign = 'center'; ctx.font = 'bold 34px Segoe UI, Arial'; ctx.fillStyle = '#ffffff';
        ctx.shadowColor = '#4fc3f7'; ctx.shadowBlur = 12; ctx.fillText('Golfo de M\u00e9xico', 256, 56);
        ctx.font = '22px Segoe UI, Arial'; ctx.fillStyle = '#4fc3f7'; ctx.shadowBlur = 6;
        ctx.fillText('Desembocadura del R\u00edo Grijalva', 256, 92); ctx.shadowBlur = 0;
        ctx.font = '20px Segoe UI, Arial'; ctx.fillStyle = '#80deea'; ctx.fillText('\u2248 Oc\u00e9ano Atl\u00e1ntico', 256, 120);
        var tex = new THREE.CanvasTexture(c); var sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, transparent: true }));
        sp.scale.set(8, 2.2, 1); sp.position.set(x, y, 0); _scene.add(sp);
    }

    /* ====== BUILD: SPARKLES ====== */
    function _buildSparkles(x, by, fp, color) {
        var wl = fp * WALL_H;
        // Water surface sparkles
        var nW = 60, pW = new Float32Array(nW * 3), sW = [];
        for (var i = 0; i < nW; i++) {
            pW[i*3] = x + (Math.random()-0.5) * DAM_W * 1.6;
            pW[i*3+1] = by + wl + 0.1 + Math.random() * 0.3;
            pW[i*3+2] = (Math.random()-0.5) * DAM_D * 1.6;
            sW.push({ phase: Math.random() * Math.PI * 2, speed: 1.5 + Math.random() * 2.5, baseY: pW[i*3+1] });
        }
        var gW = new THREE.BufferGeometry(); gW.setAttribute('position', new THREE.BufferAttribute(pW, 3));
        var mW = new THREE.PointsMaterial({ color: 0xffffff, size: 0.18, transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false });
        var ptsW = new THREE.Points(gW, mW); _scene.add(ptsW);
        _sparkleSystems.push({ pts: ptsW, data: sW, type: 'water', color: color });

        // Wall edge sparkles
        var nE = 30, pE = new Float32Array(nE * 3), sE = [];
        for (var j = 0; j < nE; j++) {
            var side = Math.random() > 0.5 ? 1 : -1;
            var along = (Math.random()-0.5) * DAM_W * 1.8;
            pE[j*3] = x + along;
            pE[j*3+1] = by + WALL_H * (0.4 + Math.random() * 0.6);
            pE[j*3+2] = side * DAM_D * 0.5 + (Math.random()-0.5) * 0.3;
            sE.push({ phase: Math.random() * Math.PI * 2, speed: 2 + Math.random() * 3 });
        }
        var gE = new THREE.BufferGeometry(); gE.setAttribute('position', new THREE.BufferAttribute(pE, 3));
        var mE = new THREE.PointsMaterial({ color: color, size: 0.12, transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false });
        var ptsE = new THREE.Points(gE, mE); _scene.add(ptsE);
        _sparkleSystems.push({ pts: ptsE, data: sE, type: 'edge' });
    }

    /* ====== BUILD: AMBIENT ====== */
    function _buildAmbient() {
        var n = 300, p = new Float32Array(n * 3);
        for (var i = 0; i < n; i++) { p[i*3] = (Math.random()-0.5) * 120; p[i*3+1] = Math.random() * 20 - 2; p[i*3+2] = (Math.random()-0.5) * 60; }
        var g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(p, 3));
        _ambientParticles = new THREE.Points(g, new THREE.PointsMaterial({ color: 0x00e676, size: 0.07, transparent: true, opacity: 0.35, blending: THREE.AdditiveBlending, depthWrite: false }));
        _scene.add(_ambientParticles);
    }

    function _buildStars() {
        var n = 600, p = new Float32Array(n * 3);
        for (var i = 0; i < n; i++) { p[i*3] = (Math.random()-0.5) * 220; p[i*3+1] = 25 + Math.random() * 70; p[i*3+2] = (Math.random()-0.5) * 150 - 30; }
        var g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(p, 3));
        _scene.add(new THREE.Points(g, new THREE.PointsMaterial({ color: 0xffffff, size: 0.12, transparent: true, opacity: 0.5 })));
    }

    /* ====== ANIMATION LOOP ====== */
    function _animate() {
        if (_disposed || _animState !== 'playing') return;
        _animId = requestAnimationFrame(_animate);
        var dt = _clock.getDelta();
        var t = _clock.getElapsedTime();

        /* -- Panoramic intro -- */
        if (_introActive) {
            _introElapsed += dt;
            var progress = Math.min(1.0, _introElapsed / _introDuration);
            var eased = _smoothstep(progress);
            _camAngle = _lerp(_introStartAngle, _introEndAngle, eased);
            _camRadius = _lerp(_introStartRadius, _introEndRadius, eased);
            _camHeight = _lerp(_introStartHeight, _introEndHeight, eased);
            _lookAtX = _lerp(_introStartLookX, _introEndLookX, eased);
            _lookAtY = _lerp(_introStartLookY, _introEndLookY, eased);
            if (progress >= 1.0) {
                _introActive = false;
            }
        }

        _updateCamera();

        /* -- Water surfaces -- */
        for (var w = 0; w < _waterMeshes.length; w++) {
            var wm = _waterMeshes[w], pa = wm.geometry.attributes.position, ph = wm.userData.phase;
            var isOcean = wm.userData.isOcean, amp = isOcean ? 0.12 : 0.06, freq = isOcean ? 1.2 : 1.5;
            for (var v = 0; v < pa.count; v++) {
                var px = pa.getX(v), pz = pa.getZ(v);
                pa.setY(v, Math.sin(px * freq + t * 2 + ph) * amp + Math.cos(pz * (freq + 0.5) + t * 1.4 + ph) * (amp * 0.66) + Math.sin((px + pz) * 0.8 + t * 1.1 + ph) * (amp * 0.4));
            }
            pa.needsUpdate = true;
        }

        /* -- Glow pulse (static) -- */
        /* lights remain at their initial intensity */

        /* -- River flow -- */
        for (var r = 0; r < _riverSystems.length; r++) {
            var rv = _riverSystems[r];
            if (rv.isTunnel) {
                var tp = rv.mesh.geometry.attributes.position;
                for (var ti = 0; ti < tp.count; ti++) {
                    var tx = tp.getX(ti) + 0.04;
                    if (tx > rv.xMax) {
                        tx = rv.xMin + Math.random() * 0.5;
                        var ang = Math.random() * Math.PI * 2;
                        var rad = Math.random() * rv.tunnelR * 0.7;
                        tp.setY(ti, rv.baseY + Math.sin(ang) * rad);
                        tp.setZ(ti, rv.baseZ + Math.cos(ang) * rad);
                    }
                    tp.setX(ti, tx);
                }
                tp.needsUpdate = true;
                continue;
            }
            var rp = rv.flow.geometry.attributes.position;
            for (var ri = 0; ri < rp.count; ri++) {
                var rx = rp.getX(ri) + rv.flowVels[ri].vx;
                if (rx > rv.enter) { rx = rv.exit + Math.random() * 0.5; rp.setZ(ri, (Math.random()-0.5) * rv.riverW * 0.85); }
                rp.setX(ri, rx); rp.setZ(ri, rp.getZ(ri) + Math.sin(t * 2.5 + ri * 0.3) * 0.001);
                rp.setY(ri, rv.riverY + 0.08 + Math.sin(rx * 0.8 + t * 2) * 0.03);
            }
            rp.needsUpdate = true;
        }

        /* -- Waterfalls & spray -- */
        for (var f = 0; f < _waterfallSystems.length; f++) {
            var wf = _waterfallSystems[f], pp = wf.pts.geometry.attributes.position;
            for (var pi = 0; pi < pp.count; pi++) {
                var py = pp.getY(pi) + wf.vels[pi].vy, ppx = pp.getX(pi) + wf.vels[pi].vx;
                wf.vels[pi].vy -= 0.0005;
                if (py < wf.botY) { py = wf.topY + Math.random() * 0.3; ppx = wf.cx + (Math.random()-0.5) * 1.2; pp.setZ(pi, (Math.random()-0.5) * 1.8); wf.vels[pi].vy = -(0.02 + Math.random() * 0.06); }
                pp.setX(pi, ppx); pp.setY(pi, py);
            }
            pp.needsUpdate = true;

            var sp = wf.spray.geometry.attributes.position;
            for (var si = 0; si < sp.count; si++) {
                var sv = wf.spVels[si], sx2 = sp.getX(si) + sv.vx, sy = sp.getY(si) + sv.vy, sz = sp.getZ(si) + sv.vz;
                sv.life -= 0.007;
                if (sv.life <= 0) { sx2 = wf.cx + (Math.random()-0.5) * 1.5; sy = wf.spBaseY; sz = (Math.random()-0.5) * 2; sv.vx = (Math.random()-0.5) * 0.025; sv.vy = 0.003 + Math.random() * 0.018; sv.vz = (Math.random()-0.5) * 0.025; sv.life = 0.4 + Math.random() * 0.6; }
                sp.setX(si, sx2); sp.setY(si, sy); sp.setZ(si, sz);
            }
            sp.needsUpdate = true;
            wf.stream.material.opacity = 0.08 + 0.05 * Math.sin(t * 3 + f);
            wf.light.intensity = 0.2 + 0.15 * Math.sin(t * 2.5 + f * 0.8);
        }

        /* -- Spin turbines -- */
        for (var ti = 0; ti < _turbineMeshes.length; ti++) {
            var tm = _turbineMeshes[ti];
            if (tm.userData.active) tm.rotation.z += tm.userData.speed;
        }

        /* -- Sparkle shimmer -- */
        for (var sk = 0; sk < _sparkleSystems.length; sk++) {
            var ss = _sparkleSystems[sk];
            if (ss.type === 'water') {
                var spos = ss.pts.geometry.attributes.position;
                for (var si2 = 0; si2 < ss.data.length; si2++) {
                    var sd = ss.data[si2];
                    spos.setY(si2, sd.baseY + Math.sin(t * 1.5 + sd.phase) * 0.08);
                }
                spos.needsUpdate = true;
                var twinkle = 0;
                for (var si3 = 0; si3 < ss.data.length; si3++) {
                    twinkle += Math.max(0, Math.sin(t * ss.data[si3].speed + ss.data[si3].phase));
                }
                ss.pts.material.opacity = 0.15 + 0.55 * (twinkle / ss.data.length);
            } else {
                var pulse = 0;
                for (var si4 = 0; si4 < ss.data.length; si4++) {
                    pulse += Math.max(0, Math.sin(t * ss.data[si4].speed + ss.data[si4].phase));
                }
                ss.pts.material.opacity = 0.1 + 0.4 * (pulse / ss.data.length);
            }
        }

        /* -- Ambient float -- */
        if (_ambientParticles) {
            var ap = _ambientParticles.geometry.attributes.position;
            for (var ai = 0; ai < ap.count; ai++) {
                ap.setY(ai, ap.getY(ai) + Math.sin(t * 0.8 + ai * 0.5) * 0.002);
            }
            ap.needsUpdate = true;
            _ambientParticles.material.opacity = 0.25 + 0.15 * Math.sin(t * 0.6);
        }

        _renderer.render(_scene, _camera);
    }

    /* ====== CAMERA ====== */
    function _updateCamera() {
        if (!_camera) return;
        _camera.position.x = _camRadius * Math.sin(_camAngle);
        _camera.position.z = _camRadius * Math.cos(_camAngle);
        _camera.position.y = _camHeight;
        _camera.lookAt(_lookAtX, _lookAtY, _lookAtZ);
    }

    /* ====== MOUSE / TOUCH ====== */
    function _mdHandler(e) { _isMouseDown = true; _prevMouseX = e.clientX; _prevMouseY = e.clientY; _autoRotate = false; _introActive = false; clearTimeout(_autoRotateTimer); }
    function _mmHandler(e) { if (!_isMouseDown) return; _camAngle -= (e.clientX - _prevMouseX) * 0.005; _camHeight = Math.max(4, Math.min(45, _camHeight + (e.clientY - _prevMouseY) * 0.06)); _prevMouseX = e.clientX; _prevMouseY = e.clientY; }
    function _muHandler() { _isMouseDown = false; }
    function _wheelHandler(e) { e.preventDefault(); _camRadius = Math.max(15, Math.min(100, _camRadius + e.deltaY * 0.04)); }
    function _tsHandler(e) { if (e.touches.length === 1) { e.preventDefault(); _isMouseDown = true; _prevMouseX = e.touches[0].clientX; _prevMouseY = e.touches[0].clientY; _autoRotate = false; _introActive = false; clearTimeout(_autoRotateTimer); } }
    function _tmHandler(e) { if (!_isMouseDown || e.touches.length !== 1) return; e.preventDefault(); _camAngle -= (e.touches[0].clientX - _prevMouseX) * 0.005; _camHeight = Math.max(4, Math.min(45, _camHeight + (e.touches[0].clientY - _prevMouseY) * 0.06)); _prevMouseX = e.touches[0].clientX; _prevMouseY = e.touches[0].clientY; }
    function _onResize() { if (!_container || !_renderer || !_camera) return; var w = _container.clientWidth, h = _container.clientHeight || 600; _camera.aspect = w / h; _camera.updateProjectionMatrix(); _renderer.setSize(w, h); }
})();
