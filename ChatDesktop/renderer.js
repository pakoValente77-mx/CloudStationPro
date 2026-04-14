// ===== CloudStation Chat Desktop — Renderer =====
let serverUrl = '';
let authToken = '';
let currentUser = '';
let currentFullName = '';
let currentRoom = 'general';
let connection = null;
let dmUnread = {};

// ===== Init =====
window.addEventListener('DOMContentLoaded', async () => {
    const config = await window.electronAPI.getConfig();
    if (config.serverUrl) document.getElementById('serverUrl').value = config.serverUrl;
    if (config.username) document.getElementById('loginUser').value = config.username;

    // Enter key on login fields
    ['serverUrl', 'loginUser', 'loginPass'].forEach(id => {
        document.getElementById(id).addEventListener('keypress', (e) => {
            if (e.key === 'Enter') doLogin();
        });
    });

    // Enter key to send message
    document.getElementById('msgInput').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') sendMessage();
    });

    // File input
    document.getElementById('fileInput').addEventListener('change', (e) => {
        const files = e.target.files;
        for (let i = 0; i < files.length; i++) uploadFile(files[i]);
        e.target.value = '';
    });

    // Drag & drop
    const chatMain = document.getElementById('chatMain');
    let dragCounter = 0;
    chatMain.addEventListener('dragenter', (e) => { e.preventDefault(); dragCounter++; showDropOverlay(true); });
    chatMain.addEventListener('dragleave', (e) => { e.preventDefault(); dragCounter--; if (dragCounter <= 0) { showDropOverlay(false); dragCounter = 0; } });
    chatMain.addEventListener('dragover', (e) => e.preventDefault());
    chatMain.addEventListener('drop', (e) => {
        e.preventDefault(); dragCounter = 0; showDropOverlay(false);
        const files = e.dataTransfer.files;
        for (let i = 0; i < files.length; i++) uploadFile(files[i]);
    });

    // Paste
    document.addEventListener('paste', (e) => {
        const items = e.clipboardData && e.clipboardData.items;
        if (!items) return;
        for (let i = 0; i < items.length; i++) {
            if (items[i].kind === 'file') {
                const file = items[i].getAsFile();
                if (file) uploadFile(file);
            }
        }
    });

    // Auto-login if we have a saved token (same session)
    if (config.token && config.serverUrl) {
        serverUrl = config.serverUrl;
        authToken = config.token;
        currentUser = config.username || '';
        currentFullName = config.fullName || currentUser;
        try {
            await initSignalR();
            showChat();
        } catch (e) {
            // Token expired, show login
        }
    }
});

function showDropOverlay(show) {
    const el = document.getElementById('dropOverlay');
    if (!el) {
        // Create it dynamically
        const overlay = document.createElement('div');
        overlay.id = 'dropOverlay';
        overlay.innerHTML = '📁 Suelta el archivo aquí';
        document.getElementById('chatMain').appendChild(overlay);
    }
    const overlay = document.getElementById('dropOverlay');
    overlay.style.display = show ? 'flex' : 'none';
}

// ===== Login =====
async function doLogin() {
    const server = document.getElementById('serverUrl').value.trim().replace(/\/+$/, '');
    const user = document.getElementById('loginUser').value.trim();
    const pass = document.getElementById('loginPass').value;

    if (!server || !user || !pass) {
        showLoginError('Completa todos los campos.');
        return;
    }

    showLoginLoading(true);
    hideLoginError();

    try {
        const resp = await fetch(`${server}/api/auth/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: user, password: pass })
        });

        const data = await resp.json();

        if (!resp.ok) {
            showLoginError(data.error || 'Error de autenticación.');
            showLoginLoading(false);
            return;
        }

        serverUrl = server;
        authToken = data.token;
        currentUser = data.usuario;
        currentFullName = data.nombre || data.usuario;

        await window.electronAPI.saveConfig({
            serverUrl: server,
            username: user,
            token: authToken,
            fullName: currentFullName
        });

        await initSignalR();
        showChat();
    } catch (err) {
        showLoginError('No se pudo conectar al servidor: ' + err.message);
    } finally {
        showLoginLoading(false);
    }
}

function doLogout() {
    if (connection) {
        connection.stop();
        connection = null;
    }
    authToken = '';
    window.electronAPI.saveConfig({ token: '' });
    document.getElementById('loginPass').value = '';
    document.getElementById('chatScreen').classList.remove('active');
    document.getElementById('loginScreen').classList.add('active');
}

function showLoginError(msg) {
    const el = document.getElementById('loginError');
    el.textContent = msg;
    el.style.display = 'block';
}
function hideLoginError() { document.getElementById('loginError').style.display = 'none'; }
function showLoginLoading(on) {
    document.getElementById('loginBtnText').style.display = on ? 'none' : '';
    document.getElementById('loginSpinner').style.display = on ? 'inline-block' : 'none';
    document.getElementById('btnLogin').disabled = on;
}

function showChat() {
    document.getElementById('loginScreen').classList.remove('active');
    document.getElementById('chatScreen').classList.add('active');
    document.getElementById('currentUserDisplay').textContent = currentFullName || currentUser;
    document.getElementById('msgInput').focus();
    loadHistory(currentRoom);
    loadDmHistory();
}

// ===== SignalR =====
async function initSignalR() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl(`${serverUrl}/hubs/chat?platform=desktop`, { accessTokenFactory: () => authToken })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .build();

    connection.on('ReceiveMessage', (msg) => {
        if (msg.room === currentRoom) {
            appendMessage(msg);
        } else if (msg.room && msg.room.startsWith('dm:')) {
            dmUnread[msg.room] = (dmUnread[msg.room] || 0) + 1;
            const parts = msg.room.split(':');
            const otherUser = (parts[1] === currentUser) ? parts[2] : parts[1];
            ensureDmItem(msg.room, otherUser);
            updateDmBadge(msg.room);

            // Desktop notification
            const sender = msg.fullName || msg.userName;
            const body = msg.fileName ? `📎 ${msg.fileName}` : msg.message;
            window.electronAPI.showNotification(`DM de ${sender}`, body);
            window.electronAPI.flashFrame();
        } else {
            // Notification for other rooms
            const sender = msg.fullName || msg.userName;
            window.electronAPI.showNotification(`[${msg.room}] ${sender}`, msg.message);
        }
    });

    connection.on('SystemMessage', (data) => appendSystemMessage(data.message));

    connection.on('UserConnected', (data) => {
        document.getElementById('onlineCount').textContent = data.onlineCount;
        refreshOnlineUsers();
    });

    connection.on('UserDisconnected', (data) => {
        document.getElementById('onlineCount').textContent = data.onlineCount;
        refreshOnlineUsers();
    });

    connection.onreconnecting(() => setConnStatus(false, 'Reconectando...'));
    connection.onreconnected(() => {
        setConnStatus(true);
        connection.invoke('JoinRoom', currentRoom);
    });
    connection.onclose(() => setConnStatus(false));

    await connection.start();
    setConnStatus(true);
}

function setConnStatus(connected, text) {
    const badge = document.getElementById('connBadge');
    badge.textContent = text || (connected ? 'Conectado' : 'Desconectado');
    badge.className = 'conn-badge' + (connected ? ' connected' : '');
}

// ===== Rooms =====
function switchRoom(room) {
    if (room === currentRoom) return;

    // Leave old group room
    if (!currentRoom.startsWith('dm:') && connection && connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke('LeaveRoom', currentRoom);
    }

    currentRoom = room;

    // Update header
    if (room === 'centinela') {
        document.getElementById('roomTitle').textContent = '🤖 Centinela';
    } else if (room.startsWith('dm:')) {
        const parts = room.split(':');
        const other = (parts[1] === currentUser) ? parts[2] : parts[1];
        document.getElementById('roomTitle').textContent = '✉ ' + other;
        dmUnread[room] = 0;
        updateDmBadge(room);
    } else {
        document.getElementById('roomTitle').textContent = room;
    }

    // Active states
    document.querySelectorAll('.room-item, .dm-item').forEach(el => el.classList.remove('active'));
    const target = document.querySelector(`[data-room="${room}"]`);
    if (target) target.classList.add('active');

    // Update input placeholder
    document.getElementById('msgInput').placeholder = room === 'centinela'
        ? 'Escribe un comando (/ayuda) o pregunta...'
        : 'Escribe un mensaje...';

    document.getElementById('messageArea').innerHTML = '';

    if (!room.startsWith('dm:') && connection && connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke('JoinRoom', room);
    }
    loadHistory(room);
}

function getDmRoom(otherUser) {
    const users = [currentUser, otherUser].sort((a, b) => a.toLowerCase().localeCompare(b.toLowerCase()));
    return `dm:${users[0]}:${users[1]}`;
}

function openDm(userName) {
    if (userName === currentUser) return;
    const room = getDmRoom(userName);
    ensureDmItem(room, userName);
    dmUnread[room] = 0;
    updateDmBadge(room);
    switchRoom(room);
}

function ensureDmItem(room, otherUser) {
    if (document.querySelector(`#dmList [data-room="${room}"]`)) return;
    const div = document.createElement('div');
    div.className = 'dm-item';
    div.dataset.room = room;
    div.onclick = () => switchRoom(room);
    div.innerHTML = `<span style="color:#ce93d8;">●</span> ${esc(otherUser)}`;
    document.getElementById('dmList').appendChild(div);
}

function updateDmBadge(room) {
    const item = document.querySelector(`#dmList [data-room="${room}"]`);
    if (!item) return;
    let badge = item.querySelector('.dm-badge');
    const count = dmUnread[room] || 0;
    if (count > 0) {
        if (!badge) {
            badge = document.createElement('span');
            badge.className = 'dm-badge';
            item.appendChild(badge);
        }
        badge.textContent = count;
    } else if (badge) {
        badge.remove();
    }
}

async function loadDmHistory() {
    try {
        const resp = await fetch(`${serverUrl}/Chat/Rooms`, {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        // The Rooms endpoint uses cookie auth, but we might get a redirect. 
        // Use the API approach instead - fetch history for known DM patterns
        if (!resp.ok) return;
        const rooms = await resp.json();
        rooms.forEach(r => {
            if (r.room && r.room.startsWith('dm:')) {
                const parts = r.room.split(':');
                const other = (parts[1] === currentUser) ? parts[2] : parts[1];
                ensureDmItem(r.room, other);
            }
        });
    } catch (e) { /* DM history load failed, not critical */ }
}

// ===== Messages =====
async function loadHistory(room) {
    try {
        const resp = await fetch(`${serverUrl}/Chat/History?room=${encodeURIComponent(room)}&limit=100`, {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        if (!resp.ok) return;
        const msgs = await resp.json();
        document.getElementById('messageArea').innerHTML = '';
        msgs.forEach(m => appendMessage(m));
        scrollToBottom();
    } catch (e) { /* History load failed */ }
}

function sendMessage() {
    const input = document.getElementById('msgInput');
    const text = input.value.trim();
    if (!text || !connection || connection.state !== signalR.HubConnectionState.Connected) return;
    connection.invoke('SendMessage', currentRoom, text).catch(err => console.error('Send error:', err));
    input.value = '';
    input.focus();
}

function appendMessage(msg) {
    const isMine = msg.userName === currentUser;
    const isBot = msg.userName === 'Centinela';
    const time = new Date(msg.timestamp).toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });
    const area = document.getElementById('messageArea');

    const authorStyle = isBot ? ' style="color:#00bcd4;"' : '';
    const authorPrefix = isBot ? '🤖 ' : '';
    let html = `<div class="msg ${isMine ? 'mine' : ''} ${isBot ? 'bot' : ''}">`;
    html += `<div class="meta"><span class="author"${authorStyle}>${authorPrefix}${esc(msg.fullName || msg.userName)}</span> · ${time}</div>`;

    if (msg.fileUrl && msg.fileName) {
        const fileUrl = msg.fileUrl.startsWith('http') ? msg.fileUrl : `${serverUrl}${msg.fileUrl}`;
        const isImage = /\.(jpg|jpeg|png|gif|webp|bmp|svg)$/i.test(msg.fileName) || (msg.fileType && msg.fileType.startsWith('image/'));
        if (isImage) {
            // WhatsApp-style: image inside bubble with caption below
            const imgBubbleStyle = isBot ? ' style="background:rgba(128,90,213,0.15); border-left:3px solid #ce93d8;"' : '';
            html += `<div class="bubble image-bubble"${imgBubbleStyle}>`;
            html += `<img src="${esc(fileUrl)}" alt="${esc(msg.fileName)}" class="file-preview-img" onclick="openImageLightbox(this.src)">`;
            if (msg.message) {
                html += `<div class="img-caption">${isBot ? formatBotMessage(msg.message) : esc(msg.message)}</div>`;
            }
            html += `</div>`;
        } else {
            html += `<div class="file-attachment">`;
            html += `<span class="file-icon">${getFileIcon(msg.fileName)}</span>`;
            html += `<div><a href="#" onclick="window.electronAPI.openExternal('${esc(fileUrl)}'); return false;">${esc(msg.fileName)}</a>`;
            html += `<div class="file-size">${formatFileSize(msg.fileSize)}</div></div></div>`;
            if (msg.message) {
                const bubbleStyle = isBot ? ' style="background:rgba(128,90,213,0.15); border-left:3px solid #ce93d8;"' : '';
                html += `<div class="bubble"${bubbleStyle}>${isBot ? formatBotMessage(msg.message) : esc(msg.message)}</div>`;
            }
        }
    } else {
        const bubbleStyle = isBot ? ' style="background:rgba(128,90,213,0.15); border-left:3px solid #ce93d8;"' : '';
        html += `<div class="bubble"${bubbleStyle}>${isBot ? formatBotMessage(msg.message) : esc(msg.message)}</div>`;
    }

    html += '</div>';
    area.insertAdjacentHTML('beforeend', html);
    scrollToBottom();
}

function appendSystemMessage(text) {
    const area = document.getElementById('messageArea');
    area.insertAdjacentHTML('beforeend', `<div class="msg system"><div class="bubble">${esc(text)}</div></div>`);
    scrollToBottom();
}

function scrollToBottom() {
    const area = document.getElementById('messageArea');
    area.scrollTop = area.scrollHeight;
}

// ===== File Upload =====
async function uploadFile(file) {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('room', currentRoom);

    const progressEl = document.getElementById('uploadProgress');
    const fillEl = document.getElementById('progressFill');
    const textEl = document.getElementById('progressText');
    progressEl.style.display = 'block';
    fillEl.style.width = '0%';
    textEl.textContent = `Subiendo ${file.name}...`;

    try {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', `${serverUrl}/Chat/UploadFile`);
        xhr.setRequestHeader('Authorization', `Bearer ${authToken}`);

        xhr.upload.onprogress = (e) => {
            if (e.lengthComputable) {
                const pct = Math.round((e.loaded / e.total) * 100);
                fillEl.style.width = pct + '%';
                textEl.textContent = `Subiendo ${file.name}... ${pct}%`;
            }
        };

        xhr.onload = () => {
            progressEl.style.display = 'none';
            if (xhr.status !== 200) {
                try {
                    const err = JSON.parse(xhr.responseText);
                    alert('Error: ' + (err.message || 'Error al subir archivo'));
                } catch (e) {
                    alert('Error al subir archivo');
                }
            }
        };

        xhr.onerror = () => {
            progressEl.style.display = 'none';
            alert('Error de conexión al subir archivo');
        };

        xhr.send(formData);
    } catch (err) {
        progressEl.style.display = 'none';
        alert('Error: ' + err.message);
    }
}

// ===== Online Users =====
async function refreshOnlineUsers() {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) return;
    try {
        const users = await connection.invoke('GetOnlineUsers');
        const list = document.getElementById('onlineList');
        list.innerHTML = '';
        document.getElementById('onlineCount').textContent = users.length;

        users.forEach(u => {
            const name = u.fullName || u.userName;
            const div = document.createElement('div');
            div.className = 'online-item';
            let platIcons = '';
            if (u.platforms && u.platforms.length > 0) {
                u.platforms.forEach(p => {
                    if (p === 'desktop') platIcons += '🖥️';
                    else if (p === 'android' || p === 'ios') platIcons += '📱';
                    else platIcons += '🌐';
                });
            } else {
                platIcons = '🌐';
            }
            if (u.userName === currentUser) {
                div.innerHTML = `${platIcons} <span class="online-dot">●</span> <strong>${esc(name)}</strong> (tú)`;
            } else {
                div.innerHTML = `${platIcons} <span class="online-dot">●</span> ${esc(name)}`;
                div.onclick = () => openDm(u.userName);
                div.title = `Chat privado con ${name}`;
            }
            list.appendChild(div);
        });
    } catch (e) { /* Online users fetch failed */ }
}

// ===== Helpers =====
function openImageLightbox(src) {
    let lb = document.getElementById('imageLightbox');
    if (!lb) {
        lb = document.createElement('div');
        lb.id = 'imageLightbox';
        lb.innerHTML = '<span class="close-btn">&times;</span><img>';
        lb.addEventListener('click', () => { lb.style.display = 'none'; });
        document.body.appendChild(lb);
    }
    lb.querySelector('img').src = src;
    lb.style.display = 'flex';
}

function esc(text) {
    const div = document.createElement('div');
    div.appendChild(document.createTextNode(text || ''));
    return div.innerHTML;
}

function getFileIcon(fileName) {
    const ext = (fileName || '').split('.').pop().toLowerCase();
    const icons = {
        'pdf': '📄', 'doc': '📝', 'docx': '📝', 'xls': '📊', 'xlsx': '📊',
        'ppt': '📊', 'pptx': '📊', 'zip': '📦', 'rar': '📦', '7z': '📦',
        'mp4': '🎬', 'avi': '🎬', 'mov': '🎬', 'mp3': '🎵', 'wav': '🎵',
        'jpg': '🖼️', 'jpeg': '🖼️', 'png': '🖼️', 'gif': '🖼️', 'webp': '🖼️',
        'txt': '📃', 'csv': '📃', 'json': '📃', 'xml': '📃'
    };
    return icons[ext] || '📎';
}

function formatFileSize(bytes) {
    if (!bytes) return '';
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
    if (bytes < 1073741824) return (bytes / 1048576).toFixed(1) + ' MB';
    return (bytes / 1073741824).toFixed(1) + ' GB';
}

function formatBotMessage(text) {
    if (!text) return '';
    // Escape HTML first, then apply markdown-like formatting
    let html = esc(text);
    // Bold **text**
    html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
    // Inline code `text`
    html = html.replace(/`(.+?)`/g, '<code style="background:rgba(255,255,255,0.08); padding:1px 4px; border-radius:3px;">$1</code>');
    // Line breaks
    html = html.replace(/\n/g, '<br>');
    return html;
}

// ===== Rainfall Report =====
async function showRainfallReport(tipo) {
    if (!serverUrl || !authToken) return;

    const msgArea = document.getElementById('messageArea');
    document.getElementById('roomTitle').textContent = tipo === '24h' ? '🌧 Lluvia 24 horas' : '🌧 Lluvia Parcial';
    msgArea.innerHTML = '<div style="text-align:center;padding:40px;color:#aaa;"><div class="spinner"></div> Cargando reporte...</div>';

    try {
        const resp = await fetch(`${serverUrl}/api/lluvia/reporte?tipo=${tipo}`, {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        if (!resp.ok) throw new Error('Error ' + resp.status);
        const data = await resp.json();
        renderRainfallReport(data, msgArea);
    } catch (err) {
        msgArea.innerHTML = `<div style="text-align:center;padding:40px;color:#e74c3c;">Error: ${esc(err.message)}</div>`;
    }
}

function renderRainfallReport(data, container) {
    let globalMax = 1;
    if (data.subcuencas) {
        data.subcuencas.forEach(sc => {
            sc.estaciones.forEach(e => { if (e.acumuladoMm > globalMax) globalMax = e.acumuladoMm; });
        });
    }

    let html = `
        <div style="padding:16px;">
            <div style="background:linear-gradient(135deg,#1b5e20,#388e3c);border-radius:10px;padding:16px 20px;margin-bottom:16px;">
                <div style="font-size:1.3em;font-weight:700;color:#fff;">${esc(data.titulo || 'Reporte de precipitación')}</div>
                <div style="color:rgba(255,255,255,0.8);font-size:0.9em;margin-top:4px;">De ${esc(data.periodoInicioLocal)} a ${esc(data.periodoFinLocal)}</div>
            </div>
            <div style="display:flex;gap:10px;margin-bottom:16px;flex-wrap:wrap;">
                <div style="background:#1a1a2e;border-radius:8px;padding:10px 16px;flex:1;min-width:100px;">
                    <div style="color:#aaa;font-size:0.75em;">Estaciones</div>
                    <div style="color:#fff;font-size:1.4em;font-weight:700;">${data.totalEstaciones}</div>
                </div>
                <div style="background:#1a1a2e;border-radius:8px;padding:10px 16px;flex:1;min-width:100px;">
                    <div style="color:#aaa;font-size:0.75em;">Con lluvia</div>
                    <div style="color:#81c784;font-size:1.4em;font-weight:700;">${data.estacionesConLluvia}</div>
                </div>
                <div style="background:#1a1a2e;border-radius:8px;padding:10px 16px;flex:1;min-width:100px;">
                    <div style="color:#aaa;font-size:0.75em;">Máximo</div>
                    <div style="color:#fbbf24;font-size:1.4em;font-weight:700;">${globalMax.toFixed(1)} mm</div>
                </div>
            </div>`;

    if (!data.subcuencas || data.subcuencas.length === 0) {
        html += '<div style="text-align:center;padding:30px;color:#999;">Sin datos de precipitación.</div>';
    } else {
        data.subcuencas.forEach(sc => {
            html += `<div style="margin-bottom:10px;">
                <div style="background:linear-gradient(90deg,#2e7d32,#43a047);color:#fff;padding:6px 14px;border-radius:6px 6px 0 0;font-weight:600;font-size:0.95em;">
                    🌊 ${esc(sc.subcuenca)}
                </div>`;

            sc.estaciones.forEach(e => {
                const pct = (e.acumuladoMm / globalMax * 100).toFixed(1);
                html += `<div style="display:flex;align-items:center;background:#2a2a3e;padding:4px 14px;border-bottom:1px solid rgba(255,255,255,0.05);">
                    <div style="width:38%;font-size:0.9em;color:#ddd;">${esc(e.nombre)}</div>
                    <div style="width:42%;padding:0 8px;">
                        <div style="background:rgba(255,255,255,0.1);border-radius:3px;height:14px;overflow:hidden;">
                            <div style="background:linear-gradient(90deg,#43a047,#66bb6a);height:100%;width:${pct}%;border-radius:3px;min-width:2px;"></div>
                        </div>
                    </div>
                    <div style="width:20%;text-align:right;font-weight:600;color:#fff;font-size:0.9em;">${e.acumuladoMm.toFixed(1)}</div>
                </div>`;
            });

            html += `<div style="background:rgba(46,125,50,0.15);color:#81c784;padding:5px 14px;border-radius:0 0 6px 6px;font-weight:600;font-size:0.85em;">
                Promedio: ${sc.promedioMm.toFixed(1)} mm
            </div></div>`;
        });
    }

    html += `<div style="margin-top:12px;color:#666;font-size:0.8em;padding:8px;background:rgba(255,255,255,0.03);border-radius:6px;">
        <strong>Observación:</strong> Datos PRELIMINARES, validación oficial a las 07:00 horas.
    </div></div>`;

    container.innerHTML = html;
}
