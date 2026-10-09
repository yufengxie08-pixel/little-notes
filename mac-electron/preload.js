const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('littleNotes', {
  postMessage(message) { ipcRenderer.send('little-notes:message', message); },
  onMessage(callback) { ipcRenderer.on('little-notes:message', (_event, message) => callback(message)); }
});
