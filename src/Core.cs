using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace BlockMook {
internal static class Core {
    internal static string EngineRoot {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine");}}
    internal static readonly string[] Names = { "Основной", "Альтернативный", "Разделение TLS" };
    internal static string DataRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockMook"); } }
    internal static string LegacyDataRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prosvet");}}
    internal static string Q(string value) {
        if (value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0) throw new ArgumentException("Недопустимый путь");
        return "\"" + value + "\"";
    }
    internal static string[] Domains(int mask) {
        return Services.Selected(mask).SelectMany(s=>s.Domains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static string Arguments(int profile, int mask, string hosts) {
        if (profile < 0 || profile >= Names.Length) throw new ArgumentException("Неизвестная стратегия");
        Domains(mask);
        bool discord = (mask & 2) != 0;
        bool meet=(mask&16)!=0;
        string args = "--wf-tcp=80,443" + (discord ? ",2053,2083,2087,2096,8443" : "") + " --wf-udp=443" + (discord ? ",19294-19344,50000-50100" : "") + (meet?",3478,19302-19309":"");
        string h = " --hostlist=" + Q(hosts);
        // STUN has no TLS hostname. Restrict this strategy to Google's documented media networks.
        if(meet)args+=" --filter-udp=443,3478,19302-19309 --ipset-ip="+Services.MeetMedia+" --filter-l7=stun --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-stun="+Q(Path.Combine(EngineRoot,"ACTIVE_DISCORD_UDP.bin"))+" --new";
        args += " --filter-udp=443" + h + " --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=" + Q(Path.Combine(EngineRoot,"quic_initial_www_google_com.bin"));
        if (discord) args += " --new --filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-discord=" + Q(Path.Combine(EngineRoot,"ACTIVE_DISCORD_UDP.bin")) + " --dpi-desync-fake-stun=" + Q(Path.Combine(EngineRoot,"ACTIVE_DISCORD_UDP.bin"));
        args += " --new --filter-tcp=443" + (discord ? ",2053,2083,2087,2096,8443" : "") + h;
        if (profile == 0) args += " --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=" + Q(Path.Combine(EngineRoot,"tls_clienthello_www_google_com.bin"));
        else if (profile == 1) args += " --dpi-desync=fake,fakedsplit --dpi-desync-repeats=6 --dpi-desync-fooling=ts --dpi-desync-fakedsplit-pattern=0x00 --dpi-desync-fake-tls=" + Q(Path.Combine(EngineRoot,"tls_clienthello_www_google_com.bin"));
        else args += " --dpi-desync=multisplit --dpi-desync-split-pos=1,midsld";
        return args + " --new --filter-tcp=80" + h + " --dpi-desync=multisplit --dpi-desync-split-pos=2";
    }
    internal static void VerifyEngine() {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("engine.sha256"))
        using (var reader = new StreamReader(stream)) {
            string line;
            while ((line = reader.ReadLine()) != null) {
                var parts = line.Split('|');
                string path = Path.Combine(EngineRoot, parts[0]);
                using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) {
                    string hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                    if (!String.Equals(hash, parts[1], StringComparison.OrdinalIgnoreCase)) throw new IOException("Изменился компонент zapret: " + parts[0] + ". Нужна повторная проверка сборки.");
                }
            }
        }
    }
    internal static string Conflict() {
        // Never terminate a process owned by another application.
        foreach (string name in new[] { "winws", "winws2", "nfqws", "goodbyedpi", "sing-box", "xray", "v2ray" }) {
            var processes = Process.GetProcessesByName(name);
            bool found = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            if (found) return "Уже работает " + name + ". Отключи его в своей программе, затем повтори запуск.";
        }
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
            string text = (nic.Name + " " + nic.Description).ToLowerInvariant();
            if (nic.OperationalStatus == OperationalStatus.Up && (text.Contains("happ-tun") || text.Contains("wintun") || text.Contains("wireguard")))
                return "Активен VPN-интерфейс " + nic.Name + ". Отключи VPN для отдельной проверки BlockMook.";
        }
        return null;
    }
    internal const int DefaultProfile=1; // Try the alternative first; verify access before remembering a profile.
    internal static int[] ProfileOrder(int preferred) {if(preferred<0||preferred>=Names.Length)preferred=DefaultProfile;return new[]{preferred,1,0,2}.Distinct().ToArray();}
    internal static void SaveProfile(int profile) {SaveProfile(profile,3);}
    internal static void SaveProfile(int profile,int mask) {
        Domains(mask);if(profile<0||profile>=Names.Length)throw new ArgumentException("Неизвестный профиль");
        Directory.CreateDirectory(DataRoot);
        File.WriteAllText(Path.Combine(DataRoot,"verified-v3-"+mask+".txt"), profile.ToString());
    }
    internal static int LoadProfile() {return LoadProfile(3);}
    internal static int LoadProfile(int mask) {
        int value;
        string path=Path.Combine(DataRoot,"verified-v3-"+mask+".txt");if(!File.Exists(path))path=Path.Combine(LegacyDataRoot,"verified-v3-"+mask+".txt");
        try { if (Int32.TryParse(File.ReadAllText(path), out value) && value >= 0 && value < Names.Length) return value; } catch (IOException) {} catch (UnauthorizedAccessException) {}
        return DefaultProfile;
    }
}
internal sealed class ProbeResult {
    internal int Index; internal bool Ok; internal string Detail;
    internal string[] Checks=new string[0];
}
internal static class Probes {
    internal static readonly string[] Urls = { "https://www.youtube.com/generate_204", "https://discord.com/api/v10/gateway", "https://www.microsoft.com/favicon.ico", "https://chat.signal.org", "https://www.viber.com", "https://meet.google.com" };
    internal static bool Valid(int index, int status, string body) {
        if (index == 0) return status == 204;
        if (index == 1) return status == 200 && DiscordChecks.ValidApi(body);
        return index == 2 && status == 200;
    }
    internal static Task<ProbeResult> One(int index) {return One(index,CancellationToken.None);}
    internal static async Task<ProbeResult> One(int index,CancellationToken token) {
        if(index==1)return await DiscordChecks.Run(token);
        if(index>=3)return await ServiceChecks.Run(index,token);
        return await Basic(index,token);
    }
    internal static async Task<ProbeResult> Basic(int index,CancellationToken token) {
        var sw = Stopwatch.StartNew();
        try {
            using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 65536 }) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BlockMook/"+Updates.CurrentVersion);
                using (var response = await client.GetAsync(Urls[index],token)) {
                    string body = index == 1 ? await response.Content.ReadAsStringAsync() : "";
                    int status = (int)response.StatusCode;
                    bool ok = Valid(index, status, body);
                    return new ProbeResult { Index=index, Ok=ok, Detail=ok ? "Доступен · " + sw.ElapsedMilliseconds + " мс" : "Не подтверждено · HTTP " + status };
                }
            }
        } catch (TaskCanceledException) { token.ThrowIfCancellationRequested();return new ProbeResult { Index=index, Detail="Таймаут · 10 с" }; }
          catch (Exception ex) { return new ProbeResult { Index=index, Detail="Ошибка соединения: " + ex.GetBaseException().Message }; }
    }
    internal static Task<ProbeResult[]> All() { return All(3,CancellationToken.None); }
    internal static Task<ProbeResult[]> All(CancellationToken token) { return All(3,token); }
    internal static Task<ProbeResult[]> All(int mask,CancellationToken token) { return Task.WhenAll(Services.ProbeIndices(mask).Select(i=>One(i,token))); }
    internal static bool ControlOk(ProbeResult[] values){return values.Any(r=>r.Index==2&&r.Ok);}
    internal static int Score(ProbeResult[] results, int mask) { return Services.Selected(mask).Count(s=>results.Any(r=>r.Index==s.Index&&r.Ok)); }
    internal static bool AllSelected(ProbeResult[] values,int mask) {return ControlOk(values)&&Score(values,mask)==Services.Selected(mask).Length;}
}
}
