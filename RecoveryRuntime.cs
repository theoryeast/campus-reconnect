using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
public static class RecoveryRuntime {
 static readonly object gate=new object(),logGate=new object();static Process worker;
 public static string Exe {get{return System.Reflection.Assembly.GetExecutingAssembly().Location;}}
 public static string ResultPath(string id){Guid.Parse(id);return Path.Combine(Core.Home,"job-"+id+".txt");}
 public static void Log(string text){try{lock(logGate){string p=Path.Combine(Core.Home,"运行日志.txt");if(File.Exists(p)&&new FileInfo(p).Length>1048576)File.Move(p,p+"."+DateTime.Now.Ticks);File.AppendAllText(p,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"  "+text+Environment.NewLine,Encoding.UTF8);}}catch{}}
 public static bool ClaimRepair(){string p=Path.Combine(Core.Home,"会话修复时间.txt");DateTime last;if(File.Exists(p)&&DateTime.TryParse(File.ReadAllText(p),out last)&&(DateTime.UtcNow-last).TotalMinutes<15)return false;File.WriteAllText(p,DateTime.UtcNow.ToString("o"));Log("外网持续失败，执行一次网页解绑并重登（15 分钟冷却）。");return true;}
 public static Process Start(string args){return Process.Start(new ProcessStartInfo(Exe,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Core.Home});}
 public static void Cancel(){lock(gate){try{if(worker!=null&&!worker.HasExited)worker.Kill();}catch{}}}
 public static async Task<string> RunWorker(int timeout=180,bool fixture=false){string id=Guid.NewGuid().ToString();string path=ResultPath(id);Process p=Start(fixture?"--hang-fixture":"--worker "+id+" "+Process.GetCurrentProcess().Id);lock(gate){worker=p;}var watch=Stopwatch.StartNew();try{
  while(!p.HasExited){if(watch.Elapsed.TotalSeconds>timeout){p.Kill();return "网络任务超过 "+timeout+" 秒，已终止；界面仍可使用，下次检查重试。";}await Task.Delay(250).ConfigureAwait(false);}
  return File.Exists(path)?File.ReadAllText(path):"网络任务已取消或异常退出，下次检测会重新创建任务。";
 }finally{lock(gate){if(worker==p)worker=null;}p.Dispose();try{File.Delete(path);}catch{}}}
 public static void Worker(string id,int parent){string path=ResultPath(id);using(var life=new System.Threading.Timer(o=>{try{if(Process.GetProcessById(parent).HasExited)Environment.Exit(0);}catch{Environment.Exit(0);}},null,1000,1000)){
  bool got;using(var mutex=new Mutex(true,"Local\\CampusReconnectNetwork",out got)){if(!got){File.WriteAllText(path,"上一网络任务尚未结束，本轮跳过。");return;}try{var s=Core.Load();Core.Validate(s);File.WriteAllText(path,Core.Check(s,true),Encoding.UTF8);}catch(Exception ex){Log("网络任务异常："+ex.GetType().Name+"；"+ex.StackTrace);File.WriteAllText(path,"网络任务异常："+ex.GetType().Name+"，已退出任务并释放资源。");}}}}
 public static void Guard(int parent,string token){Guid.Parse(token);try{using(var beat=EventWaitHandle.OpenExisting("Local\\CampusBeat"+token))using(var stop=EventWaitHandle.OpenExisting("Local\\CampusStop"+token))using(var p=Process.GetProcessById(parent)){
  if(!string.Equals(p.MainModule.FileName,Exe,StringComparison.OrdinalIgnoreCase))return;
  if(!Watch(p,beat,stop,75))return;
  Log("界面心跳停止或异常退出，守护进程准备恢复。");string path=Path.Combine(Core.Home,"守护恢复记录.txt");var now=DateTime.UtcNow;var times=new System.Collections.Generic.List<DateTime>();if(File.Exists(path))foreach(var line in File.ReadAllLines(path)){DateTime d;if(DateTime.TryParse(line,out d)&&(now-d).TotalMinutes<10)times.Add(d);}if(times.Count>=3){Log("10 分钟内已恢复 3 次，停止自动重启，请检查日志。");return;}times.Add(now);File.WriteAllLines(path,times.ConvertAll(d=>d.ToString("o")));Start("--background --recovered");
 }}catch(Exception ex){Log("守护异常："+ex.GetType().Name);}}
 public static bool Watch(Process p,EventWaitHandle beat,EventWaitHandle stop,int seconds){DateTime last=DateTime.UtcNow,previous=last;while(true){if(stop.WaitOne(0))return false;if(p.HasExited)return true;if(beat.WaitOne(500))last=DateTime.UtcNow;var now=DateTime.UtcNow;if((now-previous).TotalSeconds>20)last=now;previous=now;if((now-last).TotalSeconds>seconds){p.Kill();p.WaitForExit(5000);return true;}}}
}
