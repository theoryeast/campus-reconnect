using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Microsoft.Win32;

public class Settings {
 public string Account="", Password="", Prefix="", PortalUrl="http://10.0.100.3/";
 public bool Daily=true, Periodic=true, Enabled=false;
 public string Times="07:00", Windows="07:00-23:59";
 public int Interval=5;
}
public static class Core {
 public static string Portal { get { return NormalizePortal(Load().PortalUrl); } }
 public static string NormalizePortal(string url){Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https")||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)||uri.AbsolutePath!="/")throw new Exception("认证网址应为 http(s)://服务器地址/，不能包含账号或查询参数。");return uri.AbsoluteUri;}
 public static string Home=AppDomain.CurrentDomain.BaseDirectory;
 public static string Config=Path.Combine(Home,"设置.xml");
 public static Settings Load() { if(!File.Exists(Config)) return new Settings(); using(var f=File.OpenRead(Config)) return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(f); }
 public static void Save(Settings s) { string tmp=Config+".tmp"; using(var f=File.Create(tmp)) new XmlSerializer(typeof(Settings)).Serialize(f,s); if(File.Exists(Config)) File.Replace(tmp,Config,null); else File.Move(tmp,Config); }
 public static int Minute(string s) { if(!System.Text.RegularExpressions.Regex.IsMatch(s,@"^\d{2}:\d{2}$")) throw new Exception("时间请使用 HH:mm，例如 07:00"); int h=int.Parse(s.Substring(0,2)),m=int.Parse(s.Substring(3,2)); if(h>23||m>59) throw new Exception("时间超出范围"); return h*60+m; }
 public static string[] Parts(string s) { return s.Replace('，',',').Replace('；',',').Replace(';',',').Split(new[]{',','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).ToArray(); }
 public static bool InWindow(int t,int a,int b) { return a==b || (a<b ? t>=a && t<=b : t>=a || t<=b); }
 public static bool InWindows(Settings s,DateTime now) { int t=now.Hour*60+now.Minute; return Parts(s.Windows).Any(x=>{var p=x.Split('-');return InWindow(t,Minute(p[0]),Minute(p[1]));}); }
 public static void Validate(Settings s) { if(string.IsNullOrWhiteSpace(s.Account)||string.IsNullOrEmpty(s.Password)) throw new Exception("请填写账号和密码"); if(s.Daily && Parts(s.Times).Length==0) throw new Exception("请填写至少一个定时时间"); foreach(var t in Parts(s.Times)) Minute(t); if(s.Periodic && Parts(s.Windows).Length==0) throw new Exception("请填写检测时间段"); foreach(var w in Parts(s.Windows)) {var p=w.Split('-');if(p.Length!=2)throw new Exception("时间段示例：07:00-23:59，多个用逗号分隔");Minute(p[0]);Minute(p[1]);} if(s.Interval<1||s.Interval>1440)throw new Exception("检测间隔应为 1–1440 分钟"); }
 public static string Get(string url,out int status) {
  var r=(HttpWebRequest)WebRequest.Create(url); r.Proxy=null;r.AllowAutoRedirect=false;r.Timeout=7000;r.ReadWriteTimeout=7000;r.UserAgent="CampusReconnect/1.0";r.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
  r.CachePolicy=new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);r.KeepAlive=false;
  using(var deadline=new System.Threading.Timer(o=>r.Abort(),null,8000,Timeout.Infinite))
  using(var response=(HttpWebResponse)r.GetResponse()) {status=(int)response.StatusCode; using(var reader=new StreamReader(response.GetResponseStream(),url.Contains("/eportal/")?Encoding.UTF8:Encoding.GetEncoding(936))) {var b=new char[4096];var text=new StringBuilder();int n;while((n=reader.Read(b,0,b.Length))>0){text.Append(b,0,n);if(text.Length>1024*1024)throw new IOException("响应过大");}return text.ToString();}}
 }
 public static Dictionary<string,object> Json(string text) { int a=text.IndexOf('{'),b=text.LastIndexOf('}'); if(a<0||b<a)throw new Exception("认证接口未返回有效数据");return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text.Substring(a,b-a+1)); }
 public static string Value(Dictionary<string,object> d,string k) {return d.ContainsKey(k)?Convert.ToString(d[k]):"";}
 public static bool Online() {int status;try { if(Get("http://www.msftconnecttest.com/connecttest.txt",out status).Trim()=="Microsoft Connect Test" && status==200)return true; }catch{} try {string body=Get("https://connectivitycheck.platform.hicloud.com/generate_204",out status);return status==204 && body.Length==0;}catch{return false;} }
 public static Dictionary<string,object> Status() {int status;return Json(Get(Portal+"drcom/chkstatus?callback=cb",out status));}
 public static Dictionary<string,object> Login(Settings s) {return PortalAuth.Login(s);}
 public static bool Accepted(Dictionary<string,object> r){return Value(r,"result")=="1"||Value(r,"result")=="ok";}
 public static string Check(Settings s, bool reconnect) {return CheckWith(s,reconnect,Online,Status,Login,()=>Thread.Sleep(2000),()=>PortalAuth.Logout(s),()=>RecoveryRuntime.ClaimRepair());}
 public static string CheckWith(Settings s,bool reconnect,Func<bool> online,Func<Dictionary<string,object>> status,Func<Settings,Dictionary<string,object>> login,Action delay,Func<Dictionary<string,object>> logout=null,Func<bool> claimRepair=null) {
  if(online()) return "外网正常，无需重连。";
  Dictionary<string,object> state;try {state=status();}catch{return "外网不可用，校园网网关不可达。请检查 Wi-Fi / 网线，等待下次检测。";}
  if(!reconnect)return "校园网未认证，外网不可用。";
  var reply=login(s);
  if(!Accepted(reply)&&Value(reply,"ret_code")!="2") return "Portal 认证未成功（result="+Value(reply,"result")+"，ret_code="+Value(reply,"ret_code")+"）；请检查账号或学校服务。";
  for(int i=0;i<3;i++){delay();if(online())return "重连成功，已通过 Portal 认证并验证外网可用。";}
  if(logout==null||claimRepair==null||!claimRepair())return "认证在线但外网尚未恢复；会话修复处于冷却期，稍后再检查。";
  var cleared=logout();if(!Accepted(cleared))return "外网不通，网页会话清理未成功；已停止本轮修复。";
  delay();reply=login(s);if(!Accepted(reply)&&Value(reply,"ret_code")!="2")return "已清理旧会话，但重新认证失败；下次检查继续登录。";
  for(int i=0;i<3;i++){delay();if(online())return "已按网页流程解绑旧会话并重新登录，外网验证通过。";}
  return "已清理会话并重新认证，但外网尚未恢复；本轮不再重复注销。";
 }
}
public class Scheduler {
 DateTime last=DateTime.MinValue; HashSet<string> done=new HashSet<string>(); string day="";
 public bool Due(Settings s,DateTime now) {
  if(!s.Enabled)return false; string date=now.ToString("yyyy-MM-dd");if(day!=date){day=date;done.Clear();}
  bool due=false;
  if(s.Daily)foreach(var t in Core.Parts(s.Times)) {int m=Core.Minute(t);var target=now.Date.AddMinutes(m); if(now>=target && now<target.AddMinutes(10) && !done.Contains(t)){done.Add(t);due=true;}}
  if(s.Periodic && Core.InWindows(s,now) && (now-last).TotalMinutes>=s.Interval)due=true;
  if(due)last=now; return due;
 }
}
public class MainForm:Form {
 Settings settings; Scheduler schedule=new Scheduler(); TextBox account,password,times,windows,prefix,log; NumericUpDown interval; CheckBox daily,periodic,startup; Label status; Button toggle; NotifyIcon tray; System.Windows.Forms.Timer timer; bool busy,exiting;
 EventWaitHandle heartbeat,guardStop;DateTime lastTick=DateTime.UtcNow,readyAfter=DateTime.UtcNow.AddSeconds(20);int resumeFlag,epoch;PowerModeChangedEventHandler powerHandler;
 const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
 public MainForm(bool hidden) {
  settings=Core.Load(); Text="校园网自动重连 1.3"; ClientSize=new Size(760,650); MinimumSize=new Size(776,689); Font=new Font("Microsoft YaHei UI",10); BackColor=Color.FromArgb(245,247,251); StartPosition=FormStartPosition.CenterScreen; AutoScaleMode=AutoScaleMode.Dpi;
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=6};root.RowStyles.Add(new RowStyle(SizeType.Absolute,70));root.RowStyles.Add(new RowStyle(SizeType.Absolute,135));root.RowStyles.Add(new RowStyle(SizeType.Absolute,155));root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));root.RowStyles.Add(new RowStyle(SizeType.Absolute,38));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(root);
  var title=new Label{Text="校园网自动重连\n定时检查 · 断网重登 · 托盘守护",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,15,FontStyle.Bold),ForeColor=Color.FromArgb(30,64,105)};root.Controls.Add(title);
  var credentials=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=3}; for(int i=0;i<4;i++)credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,i%2==0?15:35));root.Controls.Add(credentials);
  account=Input(settings.Account);password=Input(settings.Password);password.UseSystemPasswordChar=true;prefix=Input(settings.PortalUrl);
  AddPair(credentials,"账号",account,0,0);AddPair(credentials,"密码",password,2,0);AddPair(credentials,"认证网址",prefix,0,1);

  var show=new CheckBox{Text="显示密码",AutoSize=true};show.CheckedChanged+=(o,e)=>password.UseSystemPasswordChar=!show.Checked;credentials.Controls.Add(show,2,1);credentials.SetColumnSpan(show,2);
  var address=new Label{Text="设置保存在 EXE 同目录（密码明文） | 请只填写本校受信任的认证网址",AutoSize=true,ForeColor=Color.DimGray};credentials.Controls.Add(address,0,2);credentials.SetColumnSpan(address,4);
  var rules=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4};rules.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));rules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.Controls.Add(rules);
  daily=new CheckBox{Text="每日指定时间",Checked=settings.Daily,AutoSize=true};times=Input(settings.Times);rules.Controls.Add(daily,0,0);rules.Controls.Add(times,1,0);
  periodic=new CheckBox{Text="时间段内循环检测",Checked=settings.Periodic,AutoSize=true};windows=Input(settings.Windows);rules.Controls.Add(periodic,0,1);rules.Controls.Add(windows,1,1);
  rules.Controls.Add(new Label{Text="检测间隔（分钟）",AutoSize=true},0,2);interval=new NumericUpDown{Minimum=1,Maximum=1440,Value=Math.Max(1,Math.Min(1440,settings.Interval)),Width=90};rules.Controls.Add(interval,1,2);
  var hint=new Label{Text="多个用逗号分隔，如 07:00,12:30；时间段如 07:00-12:00,18:00-23:59\n支持跨午夜；起止相同代表全天。修改后请点击“保存设置”。",AutoSize=true,ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9)};rules.Controls.Add(hint,0,3);rules.SetColumnSpan(hint,2);
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};root.Controls.Add(buttons);Button save=Btn("保存设置",()=>SaveUI());toggle=Btn("",()=>{if(!settings.Enabled){if(!SaveUI())return;}settings.Enabled=!settings.Enabled;try{Core.Save(settings);}catch(Exception ex){MessageBox.Show(ex.Message);}UpdateStatus();});buttons.Controls.Add(save);buttons.Controls.Add(toggle);buttons.Controls.Add(Btn("立即检测 / 重连",()=>{if(SaveUI())RunCheck(true);}));buttons.Controls.Add(Btn("隐藏到托盘",()=>Hide()));
  var strip=new FlowLayoutPanel{Dock=DockStyle.Fill};root.Controls.Add(strip);startup=new CheckBox{Text="Windows 登录后自动启动",AutoSize=true,Checked=HasStartup()};strip.Controls.Add(startup);status=new Label{AutoSize=true,Margin=new Padding(15,4,0,0)};strip.Controls.Add(status);
  log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Color.White,Font=new Font("Microsoft YaHei UI",9)};root.Controls.Add(log);
  tray=new NotifyIcon{Icon=SystemIcons.Application,Text="校园网自动重连",Visible=true};var menu=new ContextMenuStrip();menu.Items.Add("打开设置",null,(o,e)=>Restore());menu.Items.Add("立即检测 / 重连",null,(o,e)=>RunCheck(true));menu.Items.Add("退出程序",null,(o,e)=>{exiting=true;Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=(o,e)=>Restore();
  FormClosing+=(o,e)=>{if(!exiting && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();tray.ShowBalloonTip(2000,"校园网自动重连","已转入托盘后台运行。右键托盘图标可退出。",ToolTipIcon.Info);}};
  powerHandler=(o,e)=>{if(e.Mode==PowerModes.Resume)Interlocked.Exchange(ref resumeFlag,1);};SystemEvents.PowerModeChanged+=powerHandler;
  FormClosed+=(o,e)=>{timer.Stop();if(guardStop!=null)guardStop.Set();RecoveryRuntime.Cancel();SystemEvents.PowerModeChanged-=powerHandler;tray.Dispose();};
  timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=(o,e)=>{try{if(heartbeat!=null)heartbeat.Set();var now=DateTime.UtcNow;if(Interlocked.Exchange(ref resumeFlag,0)==1||(now-lastTick).TotalSeconds>60){epoch++;RecoveryRuntime.Cancel();readyAfter=now.AddSeconds(20);schedule=new Scheduler();Write("检测到休眠恢复或长时间停顿，取消旧任务，等待网络稳定 20 秒。");}lastTick=now;if(now>=readyAfter&&!busy && schedule.Due(settings,DateTime.Now))RunCheck(true);}catch(Exception ex){Write("定时器异常："+ex.GetType().Name);}};timer.Start();UpdateStatus();Write("版本 1.3 已启动：网络任务独立运行；开机/恢复后等待网络稳定。 ");
  Shown+=(o,e)=>{if(hidden)Hide();};
 }
 static TextBox Input(string s){return new TextBox{Text=s,Dock=DockStyle.Top};}
 public void StartGuard(){string token=Guid.NewGuid().ToString();heartbeat=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CampusBeat"+token);guardStop=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\CampusStop"+token);RecoveryRuntime.Start("--guard "+System.Diagnostics.Process.GetCurrentProcess().Id+" "+token);heartbeat.Set();}
 static void AddPair(TableLayoutPanel p,string s,Control c,int x,int y){p.Controls.Add(new Label{Text=s,AutoSize=true},x,y);p.Controls.Add(c,x+1,y);}
 static Button Btn(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=34,MinimumSize=new Size(104,34)};b.Click+=(o,e)=>action();return b;}
 void Restore(){Show();WindowState=FormWindowState.Normal;Activate();}
 bool HasStartup(){using(var k=Registry.CurrentUser.OpenSubKey(RunKey))return k!=null && k.GetValue("CampusReconnect")!=null;}
 bool SaveUI(){try{var s=new Settings{Account=account.Text.Trim(),Password=password.Text,Prefix="",PortalUrl=Core.NormalizePortal(prefix.Text.Trim()),Daily=daily.Checked,Periodic=periodic.Checked,Times=times.Text,Windows=windows.Text,Interval=(int)interval.Value,Enabled=settings.Enabled};Core.Validate(s);Core.Save(s);settings=s;using(var k=Registry.CurrentUser.CreateSubKey(RunKey)){if(startup.Checked)k.SetValue("CampusReconnect","\""+Application.ExecutablePath+"\" --background");else k.DeleteValue("CampusReconnect",false);}Write("设置已保存。 ");return true;}catch(Exception ex){MessageBox.Show(ex.Message,"无法保存");return false;}}
 void UpdateStatus(){toggle.Text=settings.Enabled?"暂停监控":"启动监控";status.Text=settings.Enabled?"● 监控中（按已保存规则）":"● 已暂停";status.ForeColor=settings.Enabled?Color.SeaGreen:Color.DimGray;}
 void Write(string s){if(log.TextLength>40000)log.Clear();log.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"  "+s+Environment.NewLine);ThreadPool.QueueUserWorkItem(o=>RecoveryRuntime.Log(s));}
 async void RunCheck(bool reconnect){if(busy)return;try{Core.Validate(settings);}catch(Exception ex){Write(ex.Message);return;}busy=true;int generation=epoch;status.Text="● 正在检测…";Write("开始检测网络（独立任务，最长 180 秒）。");try{string result=await RecoveryRuntime.RunWorker();if(!IsDisposed&&generation==epoch)Write(result);}catch(Exception ex){if(!IsDisposed)Write("检测未完成："+ex.GetType().Name+"，等待下次检测。");}finally{busy=false;if(!IsDisposed)UpdateStatus();}}
}
public static class Program {
 [STAThread] public static void Main(string[] args){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
  if(args.Length==3&&args[0]=="--worker"){RecoveryRuntime.Worker(args[1],int.Parse(args[2]));return;}
  if(args.Length==3&&args[0]=="--guard"){RecoveryRuntime.Guard(int.Parse(args[1]),args[2]);return;}
  if(args.Contains("--hang-fixture")){Thread.Sleep(Timeout.Infinite);return;}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  if(args.Contains("--portal-info")){try{var c=PortalAuth.Read();File.WriteAllText(Path.Combine(Core.Home,"Portal诊断.txt"),"method="+c.Method+"\r\nip="+c.Ip+"\r\nvlan="+c.Vlan+"\r\naccount_prefix="+Core.Value(c.Config,"account_prefix"));}catch(Exception ex){File.WriteAllText(Path.Combine(Core.Home,"Portal诊断.txt"),ex.Message);}return;}
  if(args.Contains("--verify-reconnect")){LiveVerification.Run();return;}
  if(args.Contains("--self-test")){SelfTest();NetworkTests.Run();return;}
  if(args.Contains("--diagnose")){try{var s=Core.Status();File.WriteAllText(Path.Combine(Core.Home,"诊断.txt"),"时间："+DateTime.Now+"\r\n校园网已认证："+(Core.Value(s,"result")=="1")+"\r\n外网探测："+Core.Online(),Encoding.UTF8);}catch(Exception e){File.WriteAllText(Path.Combine(Core.Home,"诊断.txt"),e.GetType().Name);}return;}
  bool first;using(var mutex=new Mutex(true,"Local\\CampusReconnectDesktop",out first)){if(!first){if(!args.Contains("--background"))MessageBox.Show("程序已运行，请双击系统托盘中的图标打开。");return;}try{var form=new MainForm(args.Contains("--background"));form.StartGuard();Application.Run(form);}catch(Exception ex){RecoveryRuntime.Log("主界面异常："+ex.GetType().Name+" "+ex.StackTrace);MessageBox.Show("程序启动失败，请检查日志及设置.xml。");}}
 }
 static void Assert(bool b,string message){if(!b)throw new Exception(message);}
 static void SelfTest(){var results=new List<string>();try{Assert(Core.InWindow(60,1380,120),"cross midnight");Assert(!Core.InWindow(600,1380,120),"outside window");Assert(Core.InWindow(800,0,0),"full day");results.Add("PASS: midnight / daytime / full-day windows");bool invalid=false;try{Core.Minute("24:00");}catch{invalid=true;}Assert(invalid,"invalid time");results.Add("PASS: invalid time rejected");var s=new Settings{Account="demo-student",Password="test",Enabled=true,Periodic=false,Times="07:00,12:00"};var q=new Scheduler();var d=new DateTime(2026,9,9,7,0,0);Assert(q.Due(s,d),"first schedule");Assert(!q.Due(s,d.AddSeconds(1)),"duplicate");Assert(q.Due(s,d.Date.AddHours(12)),"second schedule");Assert(q.Due(s,d.AddDays(1)),"next day");results.Add("PASS: multiple daily schedules / deduplication / next day");q=new Scheduler();s.Daily=false;s.Periodic=true;s.Windows="07:00-09:00";Assert(q.Due(s,d),"period first");Assert(!q.Due(s,d.AddMinutes(4)),"interval early");Assert(q.Due(s,d.AddMinutes(5)),"interval due");Assert(!q.Due(s,d.AddHours(3)),"outside");s.Enabled=false;Assert(!q.Due(s,d.AddDays(1)),"paused");results.Add("PASS: periodic interval / outside window / pause");Assert(Core.Value(Core.Json("cb({\"result\":1});"),"result")=="1","jsonp");results.Add("PASS: portal JSONP parsing");var f=new MainForm(false);f.Show();Application.DoEvents();using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,f.ClientRectangle);bmp.Save(Path.Combine(Core.Home,"界面预览.png"));}f.Dispose();results.Add("PASS: GUI creation and rendering");}catch(Exception e){results.Add("FAIL: "+e);Environment.ExitCode=1;}File.WriteAllLines(Path.Combine(Core.Home,"测试结果.txt"),results,Encoding.UTF8);}
}
