using System;
using System.IO;
using System.Collections.Generic;
public static class NetworkTests {
 static void Assert(bool value,string name){if(!value)throw new Exception(name);}
 public static void Run(){var lines=new List<string>();try{
  var defaults=new Settings();Assert(defaults.Account==""&&defaults.Password==""&&!defaults.Enabled,"public defaults must have no credentials and monitoring must be off");
  bool badPortal=false;try{Core.NormalizePortal("http://user:password@example.invalid/");}catch{badPortal=true;}Assert(badPortal,"reject credentials in gateway URL");lines.Add("PASS: credential-free defaults / monitoring off / gateway URL validation");
  var s=new Settings{Account="demo-student",Password="test"};int calls=0,probes=0;
  var context=new PortalContext{Ip="10.2.3.4",Mac="112233445566",Config=Core.Json("{\"login_method\":\"1\",\"account_prefix\":\"1\"}")};
  var data=PortalAuth.LoginData(s,context);Assert(data["user_account"]==",0,"+s.Account && data["wlan_user_ip"]==context.Ip && data["login_method"]=="1" && !data.ContainsKey("DDDDD"),"Portal login must use current method, prefix and client IP");
  var special=new Settings{Account="test@school",Password="a&b+c= %"};var encoded=System.Web.HttpUtility.ParseQueryString(PortalAuth.Encode(PortalAuth.LoginData(special,context)));Assert(encoded["user_password"]==special.Password,"credential encoding roundtrip");context.Config["account_prefix"]="0";Assert(PortalAuth.LoginData(s,context)["user_account"]==s.Account,"server disables prefix");
  Assert(PortalAuth.Variable("ss5='10.2.3.4';","ss5")=="10.2.3.4","page IP parsing");lines.Add("PASS: Portal endpoint fields / live client IP / server-selected prefix / credential encoding");
  Func<Settings,Dictionary<string,object>> login=x=>{calls++;return Core.Json("{\"result\":1}");};
  Func<Dictionary<string,object>> offline=()=>Core.Json("{\"result\":0}");
  Func<Dictionary<string,object>> active=()=>Core.Json("{\"result\":1}");
  string r=Core.CheckWith(s,true,()=>true,offline,login,()=>{});Assert(calls==0&&r.Contains("正常"),"online skips login");lines.Add("PASS: healthy network does not log in");
  r=Core.CheckWith(s,true,()=>false,active,login,()=>{});Assert(calls==1&&r.Contains("尚未恢复"),"stale local authentication must not block Portal login");lines.Add("PASS: stale local online status does not prevent real Portal authentication");calls=0;
  r=Core.CheckWith(s,true,()=>false,()=>{throw new Exception();},login,()=>{});Assert(calls==0&&r.Contains("网关不可达"),"gateway unreachable");lines.Add("PASS: unreachable gateway never receives credentials");
  r=Core.CheckWith(s,true,()=>++probes>1,offline,login,()=>{});Assert(calls==1&&r.Contains("重连成功"),"offline recovery");lines.Add("PASS: offline -> login -> internet verified");
  r=Core.CheckWith(s,true,()=>false,offline,login,()=>{});Assert(r.Contains("尚未恢复"),"unverified login");lines.Add("PASS: login accepted without internet is not reported as recovered");
  r=Core.CheckWith(s,true,()=>false,offline,x=>Core.Json("{\"result\":0,\"msga\":\"password error\"}"),()=>{});Assert(r.Contains("认证未成功"),"bad credentials");lines.Add("PASS: authentication failure reported without retry storm");
  int before=calls;r=Core.CheckWith(s,false,()=>false,offline,login,()=>{});Assert(calls==before,"read only");lines.Add("PASS: diagnostic-only mode never logs in");
  context.Ip="192.0.2.10";context.Mac="abcdef123456";var unbind=PortalAuth.UnbindData(context,s.Account);Assert(unbind["wlan_user_ip"]=="3221225994"&&unbind["wlan_user_mac"]=="ABCDEF123456"&&unbind["user_account"]==s.Account,"web unbind protocol");lines.Add("PASS: MAC unbind uses unsigned integer IP and unprefixed account");
  int cleanups=0,attempts=0;bool restored=false;
  r=Core.CheckWith(s,true,()=>restored,active,x=>{attempts++;if(attempts==2)restored=true;return Core.Json("{\"result\":0,\"ret_code\":2}");},()=>{},()=>{cleanups++;return Core.Json("{\"result\":1}");},()=>true);Assert(cleanups==1&&attempts==2&&r.Contains("验证通过"),"stale session recovered");lines.Add("PASS: already-online without internet -> unbind -> login -> verified recovery");
  cleanups=0;attempts=0;r=Core.CheckWith(s,true,()=>false,active,x=>{attempts++;return Core.Json("{\"result\":1}");},()=>{},()=>{cleanups++;return Core.Json("{\"result\":1}");},()=>true);Assert(cleanups==1&&attempts==2,"bound cleanup loop");
  cleanups=0;r=Core.CheckWith(s,true,()=>false,active,login,()=>{},()=>{cleanups++;return Core.Json("{\"result\":1}");},()=>false);Assert(cleanups==0,"cooldown");lines.Add("PASS: one cleanup per round and cooldown prevents logout storms");
  string timeout=RecoveryRuntime.RunWorker(2,true).GetAwaiter().GetResult();Assert(timeout.Contains("已终止"),"hung worker termination");lines.Add("PASS: real stalled child process killed at deadline");
  using(var fixture=RecoveryRuntime.Start("--hang-fixture"))using(var beat=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset))using(var stop=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset)){Assert(RecoveryRuntime.Watch(fixture,beat,stop,2)&&fixture.HasExited,"guardian terminates stalled process");}lines.Add("PASS: guardian detects missing UI heartbeat and terminates stalled fixture");
 }catch(Exception e){lines.Add("FAIL: "+e);Environment.ExitCode=1;}File.AppendAllLines(Path.Combine(Core.Home,"测试结果.txt"),lines);}
}
