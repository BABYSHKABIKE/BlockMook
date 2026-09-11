using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BlockMook {
internal sealed class ServiceCheck {internal string Name,Url,Detail;internal bool Ok;}
internal static class DiscordChecks {
    internal const string UpdateUrl="https://updates.discord.com/distributions/app/manifests/latest?channel=stable&platform=win&arch=x64";
    internal const string GatewayUrl="wss://gateway.discord.gg/?v=10&encoding=json";
    private static Dictionary<string,object> Json(string body){try{return new JavaScriptSerializer{MaxJsonLength=1048576}.DeserializeObject(body) as Dictionary<string,object>;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}}
    internal static bool ValidApi(string body){var json=Json(body);object url;return json!=null&&json.TryGetValue("url",out url)&&url is string&&(string)url=="wss://gateway.discord.gg";}
    internal static bool ValidManifest(string body){var json=Json(body);return json!=null&&json.ContainsKey("full")&&json["full"] is Dictionary<string,object>&&json.ContainsKey("modules")&&json.ContainsKey("metadata_version");}
    internal static bool ValidHello(string body){var json=Json(body);object op,data;if(json==null||!json.TryGetValue("op",out op)||!(op is int)||(int)op!=10||!json.TryGetValue("d",out data))return false;var values=data as Dictionary<string,object>;object interval;return values!=null&&values.TryGetValue("heartbeat_interval",out interval)&&(interval is int||interval is decimal)&&Convert.ToDouble(interval)>0;}
    internal static ProbeResult Combine(ServiceCheck[] checks){return new ProbeResult{Index=1,Ok=checks.Length==4&&checks.All(c=>c.Ok),Detail=checks.All(c=>c.Ok)&&checks.Length==4?"Проверки запуска: 4/4":"Проверки запуска: "+checks.Count(c=>c.Ok)+"/4 · "+String.Join(", ",checks.Where(c=>!c.Ok).Select(c=>c.Name)),Checks=checks.Select(c=>c.Name+" | "+c.Url+" | "+(c.Ok?"PASS":"FAIL")+" | "+c.Detail).ToArray()};}
    internal static async Task<ProbeResult> Run(CancellationToken token){
        var checks=await Task.WhenAll(Api(token),Http("Интерфейс","https://discord.com/app",false,token),Http("Обновления",UpdateUrl,true,token),Gateway(token));
        return Combine(checks);
    }
    private static async Task<ServiceCheck> Api(CancellationToken token){var result=await Probes.Basic(1,token);return new ServiceCheck{Name="API",Url=Probes.Urls[1],Ok=result.Ok,Detail=result.Detail};}
    private static async Task<ServiceCheck> Http(string name,string url,bool json,CancellationToken token){
        var result=new ServiceCheck{Name=name,Url=url};var watch=Stopwatch.StartNew();
        using(var limit=CancellationTokenSource.CreateLinkedTokenSource(token)) {
            limit.CancelAfter(10000);
            try{
                using(var handler=new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,UseCookies=false})
                using(var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10),MaxResponseContentBufferSize=1048576}) {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BlockMook/"+Updates.CurrentVersion);
                    using(var response=await client.GetAsync(url,limit.Token)){
                        string body=await response.Content.ReadAsStringAsync();
                        string type=response.Content.Headers.ContentType==null?"":response.Content.Headers.ContentType.MediaType;
                        result.Ok=(int)response.StatusCode==200&&(json?type=="application/json"&&ValidManifest(body):type=="text/html"&&body.IndexOf("<html",StringComparison.OrdinalIgnoreCase)>=0&&body.IndexOf("discord",StringComparison.OrdinalIgnoreCase)>=0&&body.Contains("/assets/"));
                        result.Detail=result.Ok?"Ответ проверен · "+watch.ElapsedMilliseconds+" мс":"Не подтверждено · HTTP "+(int)response.StatusCode;
                    }
                }
            }catch(OperationCanceledException){token.ThrowIfCancellationRequested();result.Detail="Таймаут · 10 с";}
             catch(Exception ex){result.Detail=ex.GetBaseException().Message;}
        }
        return result;
    }
    private static async Task<ServiceCheck> Gateway(CancellationToken token){
        var result=new ServiceCheck{Name="WebSocket",Url=GatewayUrl};var watch=Stopwatch.StartNew();
        using(var limit=CancellationTokenSource.CreateLinkedTokenSource(token))
        using(var socket=new ClientWebSocket()){
            limit.CancelAfter(10000);socket.Options.Proxy=null;
            try{
                await socket.ConnectAsync(new Uri(GatewayUrl),limit.Token);
                using(var body=new MemoryStream()){
                    var bytes=new byte[4096];WebSocketReceiveResult part;
                    do {part=await socket.ReceiveAsync(new ArraySegment<byte>(bytes),limit.Token);if(part.MessageType!=WebSocketMessageType.Text)throw new IOException("Gateway не прислал текстовый Hello");body.Write(bytes,0,part.Count);if(body.Length>16384)throw new IOException("Превышен размер Hello");}while(!part.EndOfMessage);
                    result.Ok=ValidHello(Encoding.UTF8.GetString(body.ToArray()));
                    result.Detail=result.Ok?"Получен Hello · "+watch.ElapsedMilliseconds+" мс":"Неожиданный ответ Gateway";
                }
            }catch(OperationCanceledException){token.ThrowIfCancellationRequested();result.Detail="Таймаут · 10 с";}
             catch(Exception ex){result.Detail=ex.GetBaseException().Message;}
            finally{socket.Abort();}
        }
        return result;
    }
}
}
