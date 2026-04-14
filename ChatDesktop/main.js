const { app, BrowserWindow, Tray, Menu, nativeImage, Notification, shell, ipcMain } = require('electron');
const path = require('path');
const fs = require('fs');

let mainWindow = null;
let tray = null;
let isQuitting = false;

// Config file for server URL persistence
const configPath = path.join(app.getPath('userData'), 'config.json');

function loadConfig() {
    try {
        if (fs.existsSync(configPath)) {
            return JSON.parse(fs.readFileSync(configPath, 'utf8'));
        }
    } catch (e) { /* ignore */ }
    return {};
}

function saveConfig(data) {
    const current = loadConfig();
    const merged = { ...current, ...data };
    fs.writeFileSync(configPath, JSON.stringify(merged, null, 2));
}

function createWindow() {
    mainWindow = new BrowserWindow({
        width: 1100,
        height: 750,
        minWidth: 800,
        minHeight: 500,
        title: 'CloudStation Chat',
        icon: path.join(__dirname, 'assets', 'icon.png'),
        backgroundColor: '#0e121a',
        autoHideMenuBar: true,
        webPreferences: {
            preload: path.join(__dirname, 'preload.js'),
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: false
        }
    });

    mainWindow.loadFile('index.html');

    // Minimize to tray instead of closing
    mainWindow.on('close', (event) => {
        if (!isQuitting) {
            event.preventDefault();
            mainWindow.hide();
        }
    });

    mainWindow.on('closed', () => {
        mainWindow = null;
    });
}

function createTray() {
    // Create a simple tray icon (16x16 colored square)
    const iconSize = 16;
    const canvas = nativeImage.createEmpty();
    
    // Use a simple icon - on production replace with real .png
    const iconPath = path.join(__dirname, 'assets', 'icon.png');
    let trayIcon;
    if (fs.existsSync(iconPath)) {
        trayIcon = nativeImage.createFromPath(iconPath).resize({ width: 16, height: 16 });
    } else {
        // Fallback: create a minimal icon
        trayIcon = nativeImage.createEmpty();
    }

    tray = new Tray(trayIcon);
    tray.setToolTip('CloudStation Chat');

    const contextMenu = Menu.buildFromTemplate([
        {
            label: 'Abrir Chat',
            click: () => {
                if (mainWindow) {
                    mainWindow.show();
                    mainWindow.focus();
                }
            }
        },
        { type: 'separator' },
        {
            label: 'Salir',
            click: () => {
                isQuitting = true;
                app.quit();
            }
        }
    ]);

    tray.setContextMenu(contextMenu);
    tray.on('double-click', () => {
        if (mainWindow) {
            mainWindow.show();
            mainWindow.focus();
        }
    });
}

// IPC handlers
ipcMain.handle('get-config', () => loadConfig());
ipcMain.handle('save-config', (event, data) => {
    saveConfig(data);
    return true;
});

ipcMain.handle('show-notification', (event, { title, body }) => {
    if (Notification.isSupported()) {
        const notif = new Notification({ title, body });
        notif.on('click', () => {
            if (mainWindow) {
                mainWindow.show();
                mainWindow.focus();
            }
        });
        notif.show();
    }
});

ipcMain.handle('open-external', (event, url) => {
    shell.openExternal(url);
});

ipcMain.handle('flash-frame', () => {
    if (mainWindow && !mainWindow.isFocused()) {
        mainWindow.flashFrame(true);
    }
});

app.whenReady().then(() => {
    createWindow();
    createTray();

    app.on('activate', () => {
        if (mainWindow === null) {
            createWindow();
        } else {
            mainWindow.show();
        }
    });
});

app.on('before-quit', () => {
    isQuitting = true;
});

app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') {
        app.quit();
    }
});
