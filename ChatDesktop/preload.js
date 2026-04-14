const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
    getConfig: () => ipcRenderer.invoke('get-config'),
    saveConfig: (data) => ipcRenderer.invoke('save-config', data),
    showNotification: (title, body) => ipcRenderer.invoke('show-notification', { title, body }),
    openExternal: (url) => ipcRenderer.invoke('open-external', url),
    flashFrame: () => ipcRenderer.invoke('flash-frame')
});
