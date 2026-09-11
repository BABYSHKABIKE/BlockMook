using System;
using System.IO;
using System.Linq;

namespace BlockMook {
internal static partial class Tests {
    private static void TestDesktop(string folder){
        DateTime now=new DateTime(2026,9,11,0,0,0,DateTimeKind.Utc);
        var policy=new RecoveryPolicy();Check("Recovery inactive before successful manual connection",!policy.BeginAttempt(now));
        policy.Start(now,3);policy.Observe(now,false);policy.Observe(now.AddSeconds(30),false);
        Check("Two bad samples never restart a connection",!policy.BeginAttempt(now.AddSeconds(60)));
        policy.Observe(now.AddSeconds(60),true);Check("A healthy sample clears consecutive failures",policy.Failures==0);
        for(int i=0;i<3;i++)policy.Observe(now.AddSeconds(90+30*i),false);
        Check("Three failures allow recovery",policy.BeginAttempt(now.AddMinutes(3))&&policy.Attempts==1);
        policy.Finished(now.AddMinutes(3),false);
        Check("Backoff prevents immediate retries",!policy.BeginAttempt(now.AddMinutes(3).AddSeconds(14)));
        Check("Recovery can retry after cooldown",policy.BeginAttempt(now.AddMinutes(3).AddSeconds(15)));
        policy.Finished(now.AddMinutes(4),false);Check("Second cooldown is longer",!policy.BeginAttempt(now.AddMinutes(4).AddSeconds(29))&&policy.BeginAttempt(now.AddMinutes(4).AddSeconds(30)));
        policy.Finished(now.AddMinutes(5),false);Check("Three failed recoveries latch a pause",policy.Paused&&!policy.BeginAttempt(now.AddHours(1)));
        policy.Stop();policy.Changed(now);policy.Observe(now,false);Check("Manual disconnect cancels all future recovery",!policy.Wanted&&!policy.BeginAttempt(now.AddHours(2)));
        policy.Start(now,1);DateTime due=policy.NextCheck;policy.Changed(now.AddSeconds(1));Check("Network change schedules delayed check without restarting",policy.NextCheck<due&&policy.NextCheck==now.AddSeconds(6)&&policy.Failures==0);
        policy.Paused=true;due=policy.NextCheck;policy.Changed(now.AddHours(2));Check("Network events do not bypass paused retry budget",policy.NextCheck==due);
        policy.Start(now,3);
        for(int n=0;n<3;n++){for(int i=0;i<3;i++)policy.Observe(now.AddMinutes(n),false);Check("Flapping recovery allowed within budget "+n,policy.BeginAttempt(now.AddMinutes(n)));policy.Finished(now.AddMinutes(n),true);}
        for(int i=0;i<3;i++)policy.Observe(now.AddMinutes(4),false);Check("Brief successes do not reset retry budget",!policy.BeginAttempt(now.AddMinutes(4))&&policy.Paused);
        var defaults=Preferences.Parse("");Check("Startup opt-in with tray and recovery defaults",!defaults.AutoConnect&&defaults.CloseToTray&&defaults.AutoRecover&&defaults.ReconnectOnChange);
        var preferences=Preferences.Parse("closeToTray=False\nautoRecover=False\nreconnectOnChange=False\nnotifications=False\nautoConnect=True\ntrayExplained=True");
        string path=Path.Combine(folder,"desktop-prefs.txt");preferences.Save(path);var reloaded=Preferences.Load(path);
        Check("Desktop preferences survive save and reload",!reloaded.CloseToTray&&!reloaded.AutoRecover&&!reloaded.ReconnectOnChange&&!reloaded.Notifications&&reloaded.AutoConnect&&reloaded.TrayExplained);
        Check("Startup command quotes paths with spaces",DesktopIntegration.StartupCommand(@"C:\My App\BlockMook.exe")=="\"C:\\My App\\BlockMook.exe\" --startup");
        Check("Startup command rejects argument injection",Throws(()=>DesktopIntegration.StartupCommand("C:\\bad\".exe")));
        Check("Startup rejects relative and overlong paths",Throws(()=>DesktopIntegration.StartupCommand("app.exe"))&&Throws(()=>DesktopIntegration.StartupCommand("C:\\"+new string('a',260)+".exe")));
        var values=new[]{new ProbeResult{Index=0,Ok=true,Detail=@"C:\private\name 192.0.2.1",Checks=new[]{"secret=private"}},new ProbeResult{Index=2,Ok=true}};
        string summary=DiagnosticSummary.Build(1,values,true);
        Check("Export omits raw logs paths and addresses",!summary.Contains("private")&&!summary.Contains("192.0.2.1")&&!summary.Contains("secret"));
        Check("Diagnostic states VPN context and selected services only",summary.Contains("VPN")&&summary.Contains("YouTube")&&!summary.Contains("Discord"));
        Check("Missing control has actionable advice",DiagnosticSummary.Build(1,values.Where(v=>v.Index!=2).ToArray(),false).Contains("проверь другие сайты"));
        string shortcut=DesktopIntegration.CreateShortcut(Path.Combine(folder,"shortcut-"+Guid.NewGuid().ToString("N")));
        Check("Desktop shortcut created outside real Desktop in test",File.Exists(shortcut)&&new FileInfo(shortcut).Length>76);
        Check("Creating the same shortcut is repeatable",DesktopIntegration.CreateShortcut(Path.GetDirectoryName(shortcut))==shortcut);
    }
}
}
