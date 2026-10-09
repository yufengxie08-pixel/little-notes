using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LittleNotesV2 {
  public class TaskData {
    public string id { get; set; }
    public string title { get; set; }
    public string category { get; set; }
    public string priority { get; set; }
    public string list { get; set; }
    public string due { get; set; }
    public bool completed { get; set; }
    public bool reminded { get; set; }
    public string created { get; set; }
  }
  public class HostMessage {
    public string type { get; set; }
    public string action { get; set; }
    public List<TaskData> tasks { get; set; }
  }
  static class Program {
    [STAThread]
    static void Main() {
      if(Environment.GetCommandLineArgs().Any(a=>a=="--selftest")) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(null,true));
        return;
      }
      bool fresh;
      using(var mutex=new Mutex(true,"Local\\LittleNotesSingleInstance",out fresh)) {
        if(!fresh) {
          try { using(var signal=EventWaitHandle.OpenExisting("Local\\LittleNotesShow")) signal.Set(); } catch {}
          return;
        }
        using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\LittleNotesShow")) {
          Application.EnableVisualStyles();
          Application.SetCompatibleTextRenderingDefault(false);
          Application.Run(new MainForm(signal));
        }
      }
    }
  }
  public class MainForm : Form {
    readonly string dataDir;
    readonly string webDataDir;
    readonly JavaScriptSerializer serializer=new JavaScriptSerializer { MaxJsonLength=10000000 };
    readonly List<TaskData> tasks=new List<TaskData>();
    readonly EventWaitHandle showSignal;
    readonly bool selfTest;
    readonly WebView2 web=new WebView2();
    readonly PrivateFontCollection handFonts=new PrivateFontCollection();
    FontFamily handFamily;
    NotifyIcon tray;
    System.Windows.Forms.Timer timer;
    bool exiting;
    bool ready;
    public MainForm(EventWaitHandle signal,bool testing=false) {
      showSignal=signal;
      selfTest=testing;
      dataDir=testing?Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-data"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"LittleNotes");
      webDataDir=testing?Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-webview"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LittleNotes","WebView2");
      Text="小贴事 · 本地任务备忘录";
      StartPosition=FormStartPosition.CenterScreen;
      MinimumSize=new Size(1000,765);
      Size=new Size(1000,765);
      FormBorderStyle=FormBorderStyle.None;
      MaximizedBounds=Screen.PrimaryScreen.WorkingArea;
      BackColor=Color.FromArgb(255,250,240);
      Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
      try { handFonts.AddFontFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Xiaolai-Regular.ttf"));handFamily=handFonts.Families[0]; } catch { handFamily=new FontFamily("KaiTi"); }
      Resize += delegate { UpdateCorners(); };
      UpdateCorners();
      LoadTasks();
      web.Dock=DockStyle.Fill;
      Controls.Add(web);
      BuildTray();
      Shown += async delegate { await InitializeWeb(); };
      FormClosing += OnClosing;
      timer=new System.Windows.Forms.Timer { Interval=1000 };
      int ticks=0;
      timer.Tick += delegate {
        if(showSignal!=null && showSignal.WaitOne(0)) ShowMain();
        if(++ticks>=15) { ticks=0; CheckReminders(); }
      };
      timer.Start();
    }
    async System.Threading.Tasks.Task InitializeWeb() {
      try {
        Directory.CreateDirectory(webDataDir);
        var env=await CoreWebView2Environment.CreateAsync(null,webDataDir);
        await web.EnsureCoreWebView2Async(env);
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
        web.CoreWebView2.Settings.AreDevToolsEnabled=false;
        web.CoreWebView2.WebMessageReceived += OnWebMessage;
        string page=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"scene.html");
        web.Source=new Uri(page);
      } catch(Exception ex) {
        MessageBox.Show("界面启动失败：" + ex.Message,"小贴事",MessageBoxButtons.OK,MessageBoxIcon.Error);
      }
    }
    void OnWebMessage(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
      try {
        var msg=serializer.Deserialize<HostMessage>(e.WebMessageAsJson);
        if(msg==null)return;
        if(msg.type=="window") { WindowAction(msg.action);return; }
        if(msg.type=="ready") { ready=true; SendState(); if(selfTest) BeginInvoke(new Action(RunSelfTest)); }
        if(msg.type=="save" && msg.tasks!=null) {
          tasks.Clear(); tasks.AddRange(msg.tasks.Where(t=>t!=null && !string.IsNullOrWhiteSpace(t.title)));
          SaveTasks();
        }
      } catch(Exception ex) {
        MessageBox.Show("任务更新失败："+ex.Message,"小贴事",MessageBoxButtons.OK,MessageBoxIcon.Error);
      }
    }
    void WindowAction(string action) {
      if(action=="minimize") WindowState=FormWindowState.Minimized;
      if(action=="maximize") WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;
      if(action=="close") Close();
      if(action=="drag" && WindowState==FormWindowState.Normal) { ReleaseCapture();SendMessage(Handle,0xA1,2,0); }
    }
    void UpdateCorners() {
      if(WindowState==FormWindowState.Maximized) { Region=null;return; }
      if(Width<20||Height<20)return;
      using(var p=new GraphicsPath()) {
        int d=20;
        p.AddArc(0,0,d,d,180,90);p.AddArc(Width-d,0,d,d,270,90);
        p.AddArc(Width-d,Height-d,d,d,0,90);p.AddArc(0,Height-d,d,d,90,90);p.CloseFigure();
        var old=Region;Region=new Region(p);if(old!=null)old.Dispose();
      }
    }
    protected override CreateParams CreateParams {
      get { var p=base.CreateParams;p.ClassStyle|=0x00020000;return p; }
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,int w,int l);
    async void RunSelfTest() {
      try {
        Activate();
        await System.Threading.Tasks.Task.Delay(500);
        await web.ExecuteScriptAsync("document.querySelector('#newTitle').value='写完周报';document.querySelector('#addBtn').click();");
        for(int i=0;i<20 && tasks.Count==0;i++) await System.Threading.Tasks.Task.Delay(100);
        bool addWorked=tasks.Count==1 && tasks[0].title=="写完周报";
        tasks[0].due=DateTime.Now.ToString("yyyy-MM-dd")+"T10:00";
        tasks[0].category="工作";
        tasks[0].priority="重要";
        tasks.Add(new TaskData { id=Guid.NewGuid().ToString("N"),title="买一束花",category="生活",priority="普通",due=DateTime.Now.ToString("yyyy-MM-dd")+"T14:00",created=DateTime.Now.ToString("o") });
        tasks.Add(new TaskData { id=Guid.NewGuid().ToString("N"),title="晚上散步",category="健康",priority="普通",due=DateTime.Now.ToString("yyyy-MM-dd")+"T20:00",created=DateTime.Now.ToString("o") });
        tasks.Add(new TaskData { id=Guid.NewGuid().ToString("N"),title="看一本书",category="学习",priority="普通",due=DateTime.Now.ToString("yyyy-MM-dd")+"T22:00",created=DateTime.Now.ToString("o") });
        tasks.Add(new TaskData { id=Guid.NewGuid().ToString("N"),title="整理房间",category="生活",priority="普通",due="",created=DateTime.Now.ToString("o") });
        SendState();
        await System.Threading.Tasks.Task.Delay(900);
        string navResult=await web.ExecuteScriptAsync("document.querySelector('[data-view=planned]').click();document.body.dataset.view;");
        bool navWorked=navResult.Contains("planned");
        await web.ExecuteScriptAsync("document.querySelector('#newTitle').value='整理旅行计划';document.querySelector('#addBtn').click();");
        for(int i=0;i<20 && tasks.Count<6;i++) await System.Threading.Tasks.Task.Delay(100);
        string plannedResult=await web.ExecuteScriptAsync("document.querySelectorAll('#tasks .task').length===1&&document.querySelector('#tasks .task-title').textContent==='整理旅行计划'&&document.querySelector('#newList').value==='planned';");
        bool plannedSeparate=tasks.Count==6&&tasks.Any(t=>t.title=="整理旅行计划"&&t.list=="planned"&&string.IsNullOrEmpty(t.due))&&plannedResult=="true";
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-planned.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        string allImage=await web.ExecuteScriptAsync("document.querySelector('[data-view=all]').click();getComputedStyle(document.querySelector('.scene')).backgroundImage.includes('ui-all.png');");
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-all.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        string doneImage=await web.ExecuteScriptAsync("document.querySelector('[data-view=done]').click();getComputedStyle(document.querySelector('.scene')).backgroundImage.includes('ui-done.png');");
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-done.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        await web.ExecuteScriptAsync("document.querySelector('[data-view=planned]').click();");
        bool viewImagesWorked=allImage=="true"&&doneImage=="true";
        await web.ExecuteScriptAsync("document.querySelector('.task-title').click();document.querySelector('#editList').value='today';document.querySelector('#saveEdit').click();");
        await System.Threading.Tasks.Task.Delay(250);
        string movedToday=await web.ExecuteScriptAsync("document.querySelectorAll('#tasks .task').length===0;");
        await web.ExecuteScriptAsync("document.querySelector('[data-view=today]').click();Array.from(document.querySelectorAll('.task')).find(x=>x.textContent.includes('整理旅行计划')).querySelector('.task-title').click();document.querySelector('#editList').value='planned';document.querySelector('#saveEdit').click();");
        await System.Threading.Tasks.Task.Delay(250);
        string movedBack=await web.ExecuteScriptAsync("document.querySelectorAll('#tasks .task').length===5;");
        bool moveBetweenLists=movedToday=="true"&&movedBack=="true"&&tasks.Any(t=>t.title=="整理旅行计划"&&t.list=="planned");
        await web.ExecuteScriptAsync("document.querySelector('[data-view=today]').click();document.querySelector('#optionsToggle').click();");
        string todayResult=await web.ExecuteScriptAsync("document.querySelectorAll('#tasks .task').length===5&&!document.querySelector('#tasks').textContent.includes('整理旅行计划')&&document.querySelector('#newList').value==='today';");
        bool todaySeparate=todayResult=="true";
        string optionsResult=await web.ExecuteScriptAsync("document.querySelector('#options').classList.contains('hidden');");
        bool optionsWorked=optionsResult=="false";
        string newCategories=await web.ExecuteScriptAsync("document.querySelector('#newCategory').blur();document.querySelector('#newCategory').focus();document.querySelectorAll('#options .category-menu:not(.hidden) .category-choice').length;");
        bool newCategoryMenuWorked=newCategories=="18";
        await web.ExecuteScriptAsync("document.querySelector('#options .category-choice[data-value=购物]').click();");
        bool newCategorySelected=(await web.ExecuteScriptAsync("document.querySelector('#newCategory').value;"))=="\"购物\"";
        await web.ExecuteScriptAsync("document.querySelector('#optionsToggle').click();");
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-screenshot.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        string quickCategoryResult=await web.ExecuteScriptAsync("document.querySelector('.task .category-inline').click();!document.querySelector('#quickCategory').classList.contains('hidden')&&document.querySelectorAll('#quickCategoryChoices .category-choice').length===18;");
        bool quickCategoryOpened=quickCategoryResult=="true";
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-quick-category.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        await web.ExecuteScriptAsync("document.querySelector('#quickCategoryChoices [data-value=家庭]').click();");
        await System.Threading.Tasks.Task.Delay(250);
        bool quickCategorySaved=tasks.Any(t=>t.category=="家庭");
        string quickPriorityResult=await web.ExecuteScriptAsync("document.querySelector('.task .priority-inline').click();!document.querySelector('#quickPriority').classList.contains('hidden')&&document.querySelectorAll('#quickPriority [data-value]').length===3;");
        bool quickPriorityOpened=quickPriorityResult=="true";
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-quick-priority.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        await web.ExecuteScriptAsync("document.querySelector('#quickPriority [data-value=紧急]').click();");
        await System.Threading.Tasks.Task.Delay(250);
        bool quickPrioritySaved=tasks.Any(t=>t.priority=="紧急");
        string quickDueResult=await web.ExecuteScriptAsync("document.querySelector('.task .due').click();!document.querySelector('#quickDue').classList.contains('hidden');");
        bool quickDueOpened=quickDueResult=="true";
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-quick-due.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        string quickDueValue=DateTime.Now.AddDays(1).ToString("yyyy-MM-dd")+"T09:30";
        await web.ExecuteScriptAsync("document.querySelector('#quickDueInput').value='"+quickDueValue+"';document.querySelector('#quickDueSave').click();");
        await System.Threading.Tasks.Task.Delay(250);
        bool quickDueSaved=tasks.Any(t=>t.due==quickDueValue);
        string deleteResult=await web.ExecuteScriptAsync("document.querySelector('.more').click();document.querySelector('#menu [data-action=delete]').click();let b=document.querySelector('#deleteModal .delete-card').getBoundingClientRect(),s=document.querySelector('.scene').getBoundingClientRect();!document.querySelector('#deleteModal').classList.contains('hidden')&&Math.abs((b.left+b.width/2)-(s.left+s.width/2))<2&&Math.abs((b.top+b.height/2)-(s.top+s.height/2))<2;");
        bool deleteCentered=deleteResult=="true";
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-delete.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        await web.ExecuteScriptAsync("document.querySelector('#cancelDelete').click();");
        await System.Threading.Tasks.Task.Delay(200);
        bool deleteCancelWorked=tasks.Count==6;
        await web.ExecuteScriptAsync("document.querySelector('.task-title').click();document.querySelector('#editCategory').blur();document.querySelector('#editCategory').focus();");
        string editCategories=await web.ExecuteScriptAsync("document.querySelectorAll('#modal .category-menu:not(.hidden) .category-choice').length;");
        bool editCategoryMenuWorked=editCategories=="18";
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-category.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        await web.ExecuteScriptAsync("document.querySelector('#modal .category-choice[data-value=财务]').click();document.querySelector('#editTitle').value='编辑后的小事';document.querySelector('#saveEdit').click();");
        await System.Threading.Tasks.Task.Delay(300);
        bool editWorked=tasks.Any(t=>t.title=="编辑后的小事"&&t.category=="财务");
        await web.ExecuteScriptAsync("document.querySelector('.check').click();");
        await System.Threading.Tasks.Task.Delay(300);
        bool completeWorked=tasks.Any(t=>t.completed);
        string groupsResult=await web.ExecuteScriptAsync("document.querySelector('[data-view=planned]').click();document.querySelector('.check').click();document.querySelector('[data-view=done]').click();document.querySelectorAll('.completed-group').length===2&&document.querySelectorAll('.completed-group[data-list=today] .task').length===1&&document.querySelectorAll('.completed-group[data-list=planned] .task').length===1;");
        bool completedGroupsWorked=groupsResult=="true";
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-done-groups.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        await web.ExecuteScriptAsync("document.querySelector('#minimizeBtn').click();");
        await System.Threading.Tasks.Task.Delay(300);
        bool minimizeWorked=WindowState==FormWindowState.Minimized;
        WindowState=FormWindowState.Normal;
        await web.ExecuteScriptAsync("document.querySelector('#maximizeBtn').click();");
        await System.Threading.Tasks.Task.Delay(300);
        bool maximizeWorked=WindowState==FormWindowState.Maximized;
        using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-max.png"))) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        WindowState=FormWindowState.Normal;
        await web.ExecuteScriptAsync("document.querySelector('.more').click();document.querySelector('#menu [data-action=delete]').click();document.querySelector('#confirmDelete').click();");
        await System.Threading.Tasks.Task.Delay(300);
        bool deleteConfirmWorked=tasks.Count==5;
        await web.ExecuteScriptAsync("document.querySelector('#closeBtn').click();");
        await System.Threading.Tasks.Task.Delay(300);
        bool trayCloseWorked=!Visible;
        ShowMain();
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-result.txt"),"addWorked="+addWorked+";rows=5;navWorked="+navWorked+";plannedSeparate="+plannedSeparate+";todaySeparate="+todaySeparate+";moveBetweenLists="+moveBetweenLists+";viewImagesWorked="+viewImagesWorked+";optionsWorked="+optionsWorked+";newCategoryMenuWorked="+newCategoryMenuWorked+";newCategorySelected="+newCategorySelected+";editCategoryMenuWorked="+editCategoryMenuWorked+";completedGroupsWorked="+completedGroupsWorked+";quickCategoryOpened="+quickCategoryOpened+";quickCategorySaved="+quickCategorySaved+";quickPriorityOpened="+quickPriorityOpened+";quickPrioritySaved="+quickPrioritySaved+";quickDueOpened="+quickDueOpened+";quickDueSaved="+quickDueSaved+";deleteCentered="+deleteCentered+";deleteCancelWorked="+deleteCancelWorked+";deleteConfirmWorked="+deleteConfirmWorked+";editWorked="+editWorked+";completeWorked="+completeWorked+";minimizeWorked="+minimizeWorked+";maximizeWorked="+maximizeWorked+";trayCloseWorked="+trayCloseWorked);
      } catch(Exception ex) {
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-result.txt"),ex.ToString());
      }
      exiting=true;Close();
    }
    void SendState() {
      if(!ready||web.CoreWebView2==null)return;
      web.CoreWebView2.PostWebMessageAsJson(serializer.Serialize(new { type="state",tasks=tasks }));
    }
    void BuildTray() {
      tray=new NotifyIcon { Icon=Icon,Text="小贴事 · 任务备忘录",Visible=true };
      tray.DoubleClick += delegate { ShowMain(); };
      var menu=new ContextMenuStrip { Font=new Font("KaiTi",12f) };
      menu.Items.Add("打开小贴事",null,delegate { ShowMain(); });
      menu.Items.Add("今日速览",null,delegate { ShowQuickView(); });
      menu.Items.Add("退出",null,delegate { exiting=true;tray.Visible=false;Close(); });
      tray.ContextMenuStrip=menu;
    }
    void ShowMain() { Show();WindowState=FormWindowState.Normal;Activate(); }
    void ShowQuickView() {
      var due=tasks.Where(t=>!t.completed && (string.IsNullOrEmpty(t.due) || ParseDue(t.due).Date<=DateTime.Today)).OrderBy(t=>string.IsNullOrEmpty(t.due)?DateTime.MaxValue:ParseDue(t.due)).Take(5).ToList();
      using(var f=new Form { Text="今日速览",Icon=Icon,BackColor=Color.FromArgb(255,250,240),ForeColor=Color.FromArgb(60,49,43),Size=new Size(355,Math.Max(205,125+due.Count*46)),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,ShowInTaskbar=false,TopMost=true }) {
        var area=Screen.PrimaryScreen.WorkingArea; f.Location=new Point(area.Right-f.Width-15,area.Bottom-f.Height-15);
        f.Paint += delegate(object sender,PaintEventArgs e) { using(var p=new Pen(Color.FromArgb(225,195,177),1))e.Graphics.DrawRectangle(p,0,0,f.Width-1,f.Height-1); };
        var hand=new Font(handFamily,16f);
        f.Controls.Add(new Label { Text="今日的小事 ♡",Font=new Font(handFamily,21f),ForeColor=Color.FromArgb(72,52,45),AutoSize=true,Location=new Point(20,15) });
        var close=new Button { Text="×",Font=new Font(handFamily,19f),ForeColor=Color.FromArgb(139,110,98),BackColor=f.BackColor,FlatStyle=FlatStyle.Flat,Location=new Point(f.Width-43,8),Size=new Size(32,30) };
        close.FlatAppearance.BorderSize=0;close.Click += delegate { f.Close(); };f.Controls.Add(close);
        if(due.Count==0) f.Controls.Add(new Label { Text="今天没有待办，轻松一下吧。",Font=hand,AutoSize=true,Location=new Point(22,75) });
        for(int i=0;i<due.Count;i++) {
          var t=due[i];
          var cb=new CheckBox { Text=t.title,Font=hand,Location=new Point(20,61+i*46),Size=new Size(315,39),AutoEllipsis=true,BackColor=i%2==0?Color.FromArgb(255,239,229):Color.FromArgb(255,248,235) };
          cb.CheckedChanged += delegate { t.completed=true;SaveTasks();SendState();cb.Enabled=false; };
          f.Controls.Add(cb);
        }
        var open=new Button { Text="打开主窗口",Font=new Font(handFamily,15f),BackColor=Color.FromArgb(228,119,104),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Location=new Point(20,f.ClientSize.Height-48),Size=new Size(128,34),Anchor=AnchorStyles.Bottom|AnchorStyles.Left };
        open.FlatAppearance.BorderSize=0;open.Click += delegate { f.Close();ShowMain(); };f.Controls.Add(open);
        f.ShowDialog();
      }
    }
    DateTime ParseDue(string value) { DateTime result;return DateTime.TryParse(value,out result)?result:DateTime.MaxValue; }
    void CheckReminders() {
      bool changed=false;
      foreach(var t in tasks.Where(t=>!t.completed&&!t.reminded&&!string.IsNullOrEmpty(t.due)&&ParseDue(t.due)<=DateTime.Now).ToList()) {
        t.reminded=true;changed=true;
        tray.ShowBalloonTip(8000,"小贴事 · 该做任务了",t.title,ToolTipIcon.Info);
        if(ready) web.CoreWebView2.PostWebMessageAsJson(serializer.Serialize(new { type="reminded",id=t.id }));
      }
      if(changed) SaveTasks();
    }
    void LoadTasks() {
      try {
        Directory.CreateDirectory(dataDir);
        string json=Path.Combine(dataDir,"tasks.json");
        if(File.Exists(json)) { var saved=serializer.Deserialize<List<TaskData>>(File.ReadAllText(json));if(saved!=null)tasks.AddRange(saved);return; }
        string legacy=Path.Combine(dataDir,"tasks.xml");
        if(!File.Exists(legacy))return;
        var doc=XDocument.Load(legacy);
        foreach(var item in doc.Descendants("TaskItem")) {
          Func<string,string> val=n=>(string)item.Element(n)??"";
          bool hasDue=val("HasDue").Equals("true",StringComparison.OrdinalIgnoreCase);
          tasks.Add(new TaskData { id=val("Id"),title=val("Title"),category=val("Category"),priority=val("Priority"),due=hasDue?ParseDue(val("Due")).ToString("yyyy-MM-ddTHH:mm"):"",completed=val("Completed").Equals("true",StringComparison.OrdinalIgnoreCase),reminded=val("Reminded").Equals("true",StringComparison.OrdinalIgnoreCase),created=val("Created") });
        }
        SaveTasks();
      } catch(Exception ex) { MessageBox.Show("读取旧任务失败："+ex.Message,"小贴事",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }
    void SaveTasks() {
      try {
        Directory.CreateDirectory(dataDir);
        string path=Path.Combine(dataDir,"tasks.json"),temp=path+".tmp";
        File.WriteAllText(temp,serializer.Serialize(tasks),System.Text.Encoding.UTF8);
        if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
      } catch(Exception ex) { MessageBox.Show("保存任务失败："+ex.Message,"小贴事",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
    void OnClosing(object sender,FormClosingEventArgs e) {
      if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true;Hide();return; }
      timer.Stop();tray.Visible=false;tray.Dispose();
    }
  }
}
