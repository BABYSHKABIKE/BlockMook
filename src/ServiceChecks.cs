using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BlockMook {
internal static class ServiceChecks {
    private sealed class CheckResult {internal bool Ok;internal string Detail;}
    // Bound DNS, connect, TLS and receive as one operation. Observe late faults after a timeout.
    private static async Task Bounded(Task task,CancellationToken token){
        using(var gate=new CancellationTokenSource())
        using(var linked=CancellationTokenSource.CreateLinkedTokenSource(token,gate.Token)){
            var delay=Task.Delay(Timeout.Infinite,linked.Token);
            if(await Task.WhenAny(task,delay)!=task){
                ObserveFault(task);
                token.ThrowIfCancellationRequested();
            }
            gate.Cancel();await task;
        }
    }
    private static void ObserveFault(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
    private static async Task<CheckResult> Tls(string host,CancellationToken token){
        var watch=Stopwatch.StartNew();
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
        using(var socket=new TcpClient()){
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using(deadline.Token.Register(()=>socket.Close())){
                try{
                    await Bounded(socket.ConnectAsync(host,443),deadline.Token);
                    // Signal's public API uses its own CA; all other hosts use Windows trust.
                    bool signal=host=="chat.signal.org"||host=="storage.signal.org";
                    using(var tls=signal?new SslStream(socket.GetStream(),false,SignalTrust.Validate):new SslStream(socket.GetStream(),false))await Bounded(tls.AuthenticateAsClientAsync(host),deadline.Token);
                    return new CheckResult{Ok=true,Detail=host+" · TLS подтверждён · "+watch.ElapsedMilliseconds+" мс"};
                }catch(Exception ex){token.ThrowIfCancellationRequested();return new CheckResult{Detail=host+" · "+(deadline.IsCancellationRequested?"таймаут 10 с":ex.GetBaseException().Message)};}
            }
        }
    }
    internal static bool ValidStun(byte[] request,byte[] response){
        if(request==null||request.Length!=20||response==null||response.Length<20||response[0]!=1||response[1]!=1)return false;
        int length=(response[2]<<8)|response[3];
        if(length%4!=0||length+20!=response.Length||!request.Skip(4).SequenceEqual(response.Skip(4).Take(16)))return false;
        bool mapped=false;
        for(int offset=20;offset<response.Length;){
            if(offset+4>response.Length)return false;
            int type=(response[offset]<<8)|response[offset+1],size=(response[offset+2]<<8)|response[offset+3];
            int end=offset+4+size;if(end>response.Length)return false;
            if(type==0x20||type==1){
                if(size<4)return false;
                byte family=response[offset+5];
                if((family==1&&size==8)||(family==2&&size==20))mapped=true;else return false;
            }
            offset=end+(4-size%4)%4;if(offset>response.Length)return false;
        }
        return mapped;
    }
    private static async Task<CheckResult> Stun(CancellationToken token){
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            try{
                var lookup=Dns.GetHostAddressesAsync("meet.turns.goog");await Bounded(lookup,deadline.Token);
                var address=lookup.Result.FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork)??lookup.Result.First();
                using(var client=new UdpClient(address.AddressFamily))
                using(deadline.Token.Register(()=>client.Close())){
                    client.Connect(address,3478);
                    byte[] request=new byte[20];request[1]=1;request[4]=0x21;request[5]=0x12;request[6]=0xA4;request[7]=0x42;
                    byte[] transaction=new byte[12];using(var random=RandomNumberGenerator.Create())random.GetBytes(transaction);Array.Copy(transaction,0,request,8,12);
                    await Bounded(client.SendAsync(request,request.Length),deadline.Token);
                    var receive=client.ReceiveAsync();await Bounded(receive,deadline.Token);
                    bool ok=ValidStun(request,receive.Result.Buffer);
                    return new CheckResult{Ok=ok,Detail=ok?"UDP/STUN · сервер Meet ответил; качество звонка не проверено":"UDP/STUN · ответ не прошёл проверку"};
                }
            }catch(Exception ex){token.ThrowIfCancellationRequested();return new CheckResult{Detail="UDP/STUN · "+(deadline.IsCancellationRequested?"нет ответа за 8 с":ex.GetBaseException().Message)+". Meet может использовать TLS; проверьте звонок."};}
        }
    }
    internal static async Task<ProbeResult> Run(int index,CancellationToken token){
        token.ThrowIfCancellationRequested();
        var service=Services.Items.Single(s=>s.Index==index);
        var checks=service.TlsHosts.Select(host=>Tls(host,token)).ToArray();
        var media=index==5?Stun(token):null;
        await Task.WhenAll(media==null?checks:checks.Concat(new[]{media}).ToArray());
        var values=checks.Select(t=>t.Result).ToArray();
        var details=values.Select(v=>v.Detail).ToList();
        if(media!=null)details.Add((await media).Detail);
        details.Add(service.Description);
        bool ok=values.All(v=>v.Ok);
        return new ProbeResult{Index=index,Ok=ok,Detail=ok?(index==4?"TLS сайта подтверждён; клиент не проверен":"TLS подтверждён; работа приложения требует проверки"):"Не все TLS-соединения подтверждены",Checks=details.ToArray()};
    }
}
}
