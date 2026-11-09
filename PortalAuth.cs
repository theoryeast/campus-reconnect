using System;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
public class PortalContext {
 public string Ip,Mac,Account="",Ipv6="",Vlan="1",AcIp="",AcName="";
 public Dictionary<string,object> Config;
 public string Method {get{return Core.Value(Config,"login_method");}}
}
public static class PortalAuth {
 public static string Endpoint {get {var u=new UriBuilder(Core.Portal);u.Port=u.Scheme=="https"?802:801;u.Path="/eportal/portal/";return u.Uri.AbsoluteUri;}}
 public static string Variable(string html,string key,string fallback="") {var m=Regex.Match(html,@"\b"+Regex.Escape(key)+@"\s*=\s*['"" ]([^'"";\r\n]*)['""]");return m.Success?m.Groups[1].Value.Trim():fallback;}
 public static string Encode(Dictionary<string,string> values){return string.Join("&",values.Select(k=>Uri.EscapeDataString(k.Key)+"="+Uri.EscapeDataString(k.Value??"")));}
 static string B64(string s){return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));}
 public static PortalContext Read() {
  int code;string html=Core.Get(Core.Portal,out code);var c=new PortalContext();
  c.Ip=Variable(html,"v46ip",Variable(html,"ss5",Variable(html,"v4ip")));c.Account=Variable(html,"uid");
  c.Mac=Variable(html,"ss4",Variable(html,"olmac","000000000000")).Replace(":","").Replace("-","");
  var vlan=Regex.Match(html,@"\bvlanid\s*=\s*['"" ]?(\d+)");c.Vlan=vlan.Success?vlan.Groups[1].Value:"1";
  IPAddress ip;if(!IPAddress.TryParse(c.Ip,out ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||c.Ip=="0.0.0.0")throw new Exception("未能从校园网页面获取有效本机 IP，未提交登录。");
  var query=new Dictionary<string,string>{{"callback","cb"},{"program_index",""},{"wlan_vlan_id",c.Vlan},{"wlan_user_ip",B64(c.Ip)},{"wlan_user_ipv6",B64(c.Ipv6)},{"wlan_user_ssid",""},{"wlan_user_areaid",""},{"wlan_ac_ip",B64(c.AcIp)},{"wlan_ap_mac","000000000000"},{"gw_id","000000000000"},{"jsVersion","4.X"}};
  var r=Core.Json(Core.Get(Endpoint+"page/loadConfig?"+Encode(query),out code));
  if(Core.Value(r,"code")!="1" || !r.ContainsKey("data"))throw new Exception("学校认证配置读取失败，未提交登录。");
  c.Config=(Dictionary<string,object>)r["data"];
  if(c.Method!="1"&&c.Method!="9")throw new Exception("学校认证方式已变化（"+c.Method+"），停止登录以避免使用错误接口。");
  if(c.Mac=="000000000000") {var state=Core.Status();var mac=Core.Value(state,"ss4");if(!string.IsNullOrEmpty(mac)&&mac!="000000000000")c.Mac=mac;}
  return c;
 }
 public static Dictionary<string,string> LoginData(Settings s,PortalContext c) {
  string prefix=Core.Value(c.Config,"account_prefix")=="1" ? (Core.Value(c.Config,"custom_perceive")=="1" ? ",b," : ",0,") : "";
  return new Dictionary<string,string>{{"callback","cb"},{"login_method",c.Method},{"user_account",prefix+s.Account},{"user_password",s.Password},{"wlan_user_ip",c.Ip},{"wlan_user_ipv6",c.Ipv6},{"wlan_user_mac",c.Mac},{"wlan_ac_ip",c.AcIp},{"wlan_ac_name",c.AcName},{"jsVersion","4.2.1"},{"terminal_type","1"},{"lang","zh-cn"}};
 }
 public static Dictionary<string,object> Login(Settings s){var c=Read();int status;return Core.Json(Core.Get(Endpoint+"login?"+Encode(LoginData(s,c)),out status));}
 public static Dictionary<string,string> UnbindData(PortalContext c,string account){var b=IPAddress.Parse(c.Ip).GetAddressBytes();uint ip=((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3];return new Dictionary<string,string>{{"callback","cb"},{"user_account",account.Trim()},{"wlan_user_mac",c.Mac.ToUpperInvariant()},{"wlan_user_ip",ip.ToString(System.Globalization.CultureInfo.InvariantCulture)},{"jsVersion","4.2.1"}};}
 public static Dictionary<string,object> Logout(){return Logout(Core.Load());}
 public static Dictionary<string,object> Logout(Settings s){var c=Read();int status;
  // wc() first calls mac/unbind for this school's register_mode; only then falls back to logout.init().
  if(Core.Value(c.Config,"un_bind_mac")=="1" && new[]{"1","3","4"}.Contains(Core.Value(c.Config,"register_mode"))){
   string account=string.IsNullOrEmpty(c.Account)?s.Account:c.Account;
   if(account!=s.Account)throw new Exception("当前会话账号不同，未执行解绑。");
   var unbound=Core.Json(Core.Get(Endpoint+"mac/unbind?"+Encode(UnbindData(c,account)),out status));
   if(Core.Accepted(unbound)){System.Threading.Thread.Sleep(5000);return unbound;}
  }
  var data=new Dictionary<string,string>{{"callback","cb"},{"login_method",c.Method},{"user_account","drcom"},{"user_password","123"},{"ac_logout",Core.Value(c.Config,"ac_logout")},{"register_mode",Core.Value(c.Config,"register_mode")},{"wlan_user_ip",c.Ip},{"wlan_user_ipv6",c.Ipv6},{"wlan_vlan_id",c.Vlan},{"wlan_user_mac",c.Mac},{"wlan_ac_ip",c.AcIp},{"wlan_ac_name",c.AcName},{"jsVersion","4.2.1"}};return Core.Json(Core.Get(Endpoint+"logout?"+Encode(data),out status));}
}
