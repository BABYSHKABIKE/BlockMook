using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace BlockMook {
internal sealed class NetworkFrame {
    internal double? Down,Up,Ping,Loss,Jitter;
    internal string Adapter="Нет активного адаптера",Target="1.1.1.1",Status="Ожидание измерения";
    internal DateTime Time;
    internal int Replies,Sent;
}
internal sealed class RateCounter {
    private string id;
    private long received,sent;
    private double stamp;
    internal double[] Read(string next,long rx,long tx,double seconds){
        double elapsed=seconds-stamp;
        bool valid=id==next&&elapsed>0&&elapsed<=10&&rx>=received&&tx>=sent;
        double[] result=valid?new[]{(rx-received)/elapsed,(tx-sent)/elapsed}:null;
        id=next;received=rx;sent=tx;stamp=seconds;return result;
    }
}
internal sealed class PingWindow {
    private sealed class Sample {internal DateTime Time;internal long? Value;}
    private readonly Queue<Sample> samples=new Queue<Sample>();
    internal void Clear(){samples.Clear();}
    internal void Add(DateTime time,long? value){samples.Enqueue(new Sample{Time=time,Value=value});Trim(time);}
    private void Trim(DateTime now){while(samples.Count>0&&(samples.Peek().Time<=now.AddSeconds(-60)||samples.Count>60))samples.Dequeue();}
    internal void Fill(NetworkFrame frame,DateTime now){
        Trim(now);var all=samples.ToArray();frame.Sent=all.Length;frame.Replies=all.Count(x=>x.Value.HasValue);
        frame.Loss=all.Length==0?(double?)null:100.0*(all.Length-frame.Replies)/all.Length;
        var changes=new List<double>();for(int i=1;i<all.Length;i++)if(all[i].Value.HasValue&&all[i-1].Value.HasValue)changes.Add(Math.Abs(all[i].Value.Value-all[i-1].Value.Value));
        frame.Jitter=changes.Count==0?(double?)null:changes.Average();
    }
}
internal sealed class NetworkMonitor {
    private readonly RateCounter rates=new RateCounter();
    private readonly PingWindow latency=new PingWindow();
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private string previousTarget,previousAdapter;
    internal static NetworkInterface[] Adapters(){return NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.NetworkInterfaceType!=NetworkInterfaceType.Loopback).ToArray();}
    internal static bool ValidTarget(string target){IPAddress address;return IPAddress.TryParse(target,out address)&&!IPAddress.Any.Equals(address)&&!IPAddress.IPv6Any.Equals(address)&&!IPAddress.None.Equals(address);}
    internal static string Speed(double? bytes,bool megabytes){
        if(!bytes.HasValue||Double.IsNaN(bytes.Value)||Double.IsInfinity(bytes.Value)||bytes.Value<0)return "—";
        double value=bytes.Value*(megabytes?1:8);if(Double.IsInfinity(value))return "—";
        string[] units=megabytes?new[]{"Б/с","КБ/с","МБ/с","ГБ/с","ТБ/с"}:new[]{"бит/с","Кбит/с","Мбит/с","Гбит/с","Тбит/с"};
        int unit=0;while(value>=1000&&unit<units.Length-1){value/=1000;unit++;}
        if(value>0&&value<1)return "< 1 "+units[unit];
        double rounded=Math.Round(value,0,MidpointRounding.AwayFromZero);
        if(rounded>=1000&&unit<units.Length-1){rounded=1;unit++;}
        return rounded.ToString("0",CultureInfo.GetCultureInfo("ru-RU"))+" "+units[unit];
    }
    internal async Task<NetworkFrame> Read(string adapterId,string target){
        var frame=new NetworkFrame{Time=DateTime.UtcNow,Target=target};
        try{
            var all=Adapters();
            var adapter=String.IsNullOrEmpty(adapterId)?all.Where(x=>x.OperationalStatus==OperationalStatus.Up&&(x.NetworkInterfaceType==NetworkInterfaceType.Ethernet||x.NetworkInterfaceType==NetworkInterfaceType.Wireless80211))
                .OrderByDescending(x=>x.GetIPProperties().GatewayAddresses.Count>0).ThenBy(x=>x.Id,StringComparer.Ordinal).FirstOrDefault():all.FirstOrDefault(x=>x.Id==adapterId&&x.OperationalStatus==OperationalStatus.Up);
            if(adapter==null){latency.Clear();previousAdapter=null;frame.Status="Адаптер отключён или недоступен";return frame;}
            frame.Adapter=adapter.Name;
            if(previousAdapter!=adapter.Id||previousTarget!=target){latency.Clear();previousAdapter=adapter.Id;previousTarget=target;}
            var counters=adapter.GetIPv4Statistics();var speed=rates.Read(adapter.Id,counters.BytesReceived,counters.BytesSent,clock.Elapsed.TotalSeconds);
            if(speed!=null){frame.Down=speed[0];frame.Up=speed[1];}
            // Ping follows the Windows route; the selected adapter only scopes traffic counters.
            using(var ping=new Ping()){
                var reply=await ping.SendPingAsync(IPAddress.Parse(target),900);
                bool ok=reply.Status==IPStatus.Success;frame.Ping=ok?(double?)reply.RoundtripTime:null;
                latency.Add(DateTime.UtcNow,ok?(long?)reply.RoundtripTime:null);frame.Status=ok?"Ответ получен":("Нет ответа · "+reply.Status);
            }
        }catch(Exception ex){if(!(ex is NetworkInformationException)&&!(ex is PingException)&&!(ex is System.Net.Sockets.SocketException)&&!(ex is InvalidOperationException)&&!(ex is ArgumentException))throw;latency.Clear();frame.Status="Измерение недоступно";}
        latency.Fill(frame,DateTime.UtcNow);return frame;
    }
}
internal sealed class SessionHistory {
    private readonly Queue<string> entries=new Queue<string>();
    internal void Add(string message){entries.Enqueue(DateTime.Now.ToString("HH:mm:ss")+"  "+message);while(entries.Count>100)entries.Dequeue();}
    internal string Text {get{return String.Join("\n",entries.Reverse());}}
    internal void Clear(){entries.Clear();}
}
}
