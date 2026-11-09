using System;
using System.IO;
using System.Text;
using System.Threading;
public static class LiveVerification {
 static void Log(string s){File.AppendAllText(Path.Combine(Core.Home,"真实重连测试.txt"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+s+Environment.NewLine,Encoding.UTF8);}
 public static void Run(){var s=Core.Load();bool loggedOut=false;try{
  Core.Validate(s);PortalAuth.Read();Log("Configuration loaded; starting Portal logout/login test.");
  var r=PortalAuth.Logout();Log("Logout result="+Core.Value(r,"result")+" ret_code="+Core.Value(r,"ret_code"));
  if(Core.Value(r,"result")!="1"&&Core.Value(r,"result")!="ok"){Log("Logout not confirmed; test stopped without changing network adapters.");return;}
  loggedOut=true;Thread.Sleep(1500);Log("Internet after logout="+Core.Online());
  string result=Core.Check(s,true);Log(result);bool online=Core.Online();Log("Internet after automatic login="+online);
  if(!online)return;
  r=PortalAuth.Logout();Log("Logout after automatic login result="+Core.Value(r,"result")+" ret_code="+Core.Value(r,"ret_code"));
 }catch(Exception e){Log("Failure: "+e.GetType().Name);}
 finally{if(loggedOut){try{Log("Restoring connection: "+Core.Check(s,true));if(!Core.Online()){Thread.Sleep(3000);Log("One recovery retry: "+Core.Check(s,true));}Log("Final internet="+Core.Online());}catch(Exception e){Log("Restore failure: "+e.GetType().Name);}}}
 }
}
