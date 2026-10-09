const { app, BrowserWindow, Tray, Menu, Notification, nativeImage, ipcMain, screen } = require('electron');
const fs = require('node:fs');
const path = require('node:path');

app.setName('LittleNotes');
const testing = process.argv.includes('--selftest');
const userData = testing ? path.join(__dirname, 'selftest-data') : path.join(app.getPath('appData'), 'LittleNotes');
app.setPath('userData', userData);

let win;
let tray;
let quitting = false;
let tasks = [];
const dataFile = path.join(userData, 'tasks.json');

function loadTasks() {
  try {
    const parsed = JSON.parse(fs.readFileSync(dataFile, 'utf8'));
    tasks = Array.isArray(parsed) ? parsed : [];
  } catch (error) {
    if (error.code !== 'ENOENT') console.error('Unable to read tasks:', error);
    tasks = [];
  }
}

function saveTasks(next) {
  if (!Array.isArray(next)) return;
  const valid = next.filter(t => t && typeof t.title === 'string' && t.title.trim()).map(t => ({
    id: String(t.id || ''), title: t.title.slice(0, 140),
    category: String(t.category || '生活'), priority: String(t.priority || '普通'),
    list: t.list === 'planned' ? 'planned' : 'today', due: String(t.due || ''),
    completed: Boolean(t.completed), reminded: Boolean(t.reminded),
    created: String(t.created || '')
  }));
  fs.mkdirSync(userData, { recursive: true });
  const temporary = dataFile + '.tmp';
  fs.writeFileSync(temporary, JSON.stringify(valid), 'utf8');
  fs.renameSync(temporary, dataFile);
  tasks = valid;
}

function showMain() {
  if (!win || win.isDestroyed()) return;
  if (win.isMinimized()) win.restore();
  win.show();
  win.focus();
}

function checkReminders() {
  const now = Date.now();
  let changed = false;
  for (const task of tasks) {
    if (task.completed || task.reminded || !task.due) continue;
    const due = new Date(task.due).getTime();
    if (!Number.isFinite(due) || due > now) continue;
    if (Notification.isSupported()) {
      const notice = new Notification({ title: '小贴事 · 该做这件小事啦', body: task.title, icon: path.join(__dirname, 'icon.png') });
      notice.on('click', showMain);
      notice.show();
    }
    task.reminded = true;
    changed = true;
    if (win && !win.isDestroyed()) win.webContents.send('little-notes:message', { type: 'reminded', id: task.id });
  }
  if (changed) saveTasks(tasks);
}

function createWindow() {
  const bounds = screen.getPrimaryDisplay().workArea;
  win = new BrowserWindow({
    width: 1000, height: 765, minWidth: 1000, minHeight: 765,
    x: Math.round(bounds.x + (bounds.width - 1000) / 2),
    y: Math.round(bounds.y + (bounds.height - 765) / 2),
    frame: false, show: false, backgroundColor: '#faf5e9',
    icon: path.join(__dirname, 'icon.png'),
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false, sandbox: false }
  });
  win.loadFile(path.join(__dirname, 'scene.html'));
  win.once('ready-to-show', () => win.show());
  win.on('close', event => { if (!quitting) { event.preventDefault(); win.hide(); } });
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', event => event.preventDefault());
  if (testing) win.webContents.once('did-finish-load', async () => {
    try {
      await new Promise(resolve => setTimeout(resolve, 350));
      const result = await win.webContents.executeJavaScript(`(() => {
        document.querySelector('#newTitle').value = '跨平台验证';
        document.querySelector('#addBtn').click();
        const created = document.querySelectorAll('#tasks .task').length === 1;
        document.querySelector('.task .priority-inline').click();
        const priorityOpened = !document.querySelector('#quickPriority').classList.contains('hidden');
        document.querySelector('#quickPriority [data-value="紧急"]').click();
        const priorityChanged = document.querySelector('.task .priority-inline').textContent.includes('紧急');
        document.querySelector('.task .category-inline').click();
        const categoryOpened = !document.querySelector('#quickCategory').classList.contains('hidden');
        document.querySelector('#quickCategoryChoices [data-value="工作"]').click();
        document.querySelector('.task .due').click();
        const dueOpened = !document.querySelector('#quickDue').classList.contains('hidden');
        return {created, priorityOpened, priorityChanged, categoryOpened, dueOpened};
      })()`);
      await new Promise(resolve => setTimeout(resolve, 350));
      result.persisted = tasks.length === 1 && tasks[0].title === '跨平台验证' && tasks[0].priority === '紧急' && tasks[0].category === '工作';
      fs.writeFileSync(path.join(__dirname, 'electron-selftest-result.json'), JSON.stringify(result));
      fs.writeFileSync(path.join(__dirname, 'electron-selftest.png'), await win.webContents.capturePage().then(image => image.toPNG()));
    } catch (error) {
      fs.writeFileSync(path.join(__dirname, 'electron-selftest-result.json'), JSON.stringify({ error: String(error) }));
    }
    quitting = true;
    app.quit();
  });
}

function createTray() {
  const icon = nativeImage.createFromPath(path.join(__dirname, 'icon.png')).resize({ width: 18, height: 18 });
  tray = new Tray(icon);
  tray.setToolTip('小贴事');
  tray.on('click', showMain);
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: '打开小贴事', click: showMain },
    { label: '今日速览', click: () => {
      const due = tasks.filter(t => !t.completed && (!t.due || new Date(t.due).toDateString() === new Date().toDateString())).slice(0, 5);
      if (Notification.isSupported()) new Notification({ title: '今日的小事', body: due.length ? due.map(t => '• ' + t.title).join('\n') : '今天还没有小事 ♡' }).show();
    } },
    { type: 'separator' },
    { label: '退出', click: () => { quitting = true; app.quit(); } }
  ]));
}

ipcMain.on('little-notes:message', (event, message) => {
  if (!win || event.sender !== win.webContents || !message || typeof message !== 'object') return;
  if (message.type === 'ready') {
    event.sender.send('little-notes:message', { type: 'state', tasks });
  } else if (message.type === 'save') {
    try { saveTasks(message.tasks); } catch (error) { console.error('Unable to save tasks:', error); }
  } else if (message.type === 'window') {
    if (message.action === 'minimize') win.minimize();
    if (message.action === 'maximize') win.isMaximized() ? win.unmaximize() : win.maximize();
    if (message.action === 'close') win.close();
  }
});

if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', showMain);
  app.whenReady().then(() => {
    loadTasks();
    createWindow();
    if (!testing) createTray();
    if (process.platform === 'darwin') {
      try { app.dock.setIcon(path.join(__dirname, 'icon.png')); } catch {}
      if (!testing && app.isPackaged) {
        try { app.setLoginItemSettings({ openAtLogin: true }); } catch (error) { console.error('Unable to enable login startup:', error); }
      }
    }
    if (!testing) { setInterval(checkReminders, 15000); checkReminders(); }
    app.on('activate', showMain);
  });
}
