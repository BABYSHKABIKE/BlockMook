using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace BlockMook {
internal static partial class Tests {
    private static void TestNetwork(string folder){
        var rates=new RateCounter();Check("No rate from absolute byte counters",rates.Read("a",1000000,2000000,1)==null);
        var delta=rates.Read("a",3000000,2500000,3);Check("Rate uses actual elapsed seconds",delta[0]==1000000&&delta[1]==250000);
        Check("Reset counters do not generate negative traffic",rates.Read("a",1,1,4)==null);
        Check("Adapter change resets rate baseline",rates.Read("b",90000000,90000000,5)==null);
        Check("Long sampling gap does not pretend to be current traffic",rates.Read("b",90000100,90000100,25)==null);
        Check("Zero time interval is not divided",rates.Read("b",90000200,90000200,25)==null);
        Check("Megabits convert bytes by eight",NetworkMonitor.Speed(1000000,false)=="8 Мбит/с");
        Check("Megabytes retain byte scale",NetworkMonitor.Speed(1000000,true)=="1 МБ/с");
        Check("One megabit is shown as one megabit",NetworkMonitor.Speed(125000,false)=="1 Мбит/с");
        Check("Twenty megabits stay in megabits",NetworkMonitor.Speed(2500000,false)=="20 Мбит/с");
        Check("Thirty megabits stay in megabits",NetworkMonitor.Speed(3750000,false)=="30 Мбит/с");
        Check("One kilobit is not rounded to zero megabits",NetworkMonitor.Speed(125,false)=="1 Кбит/с");
        Check("Two and three kilobits use whole numbers",NetworkMonitor.Speed(250,false)=="2 Кбит/с"&&NetworkMonitor.Speed(375,false)=="3 Кбит/с");
        Check("Small traffic automatically uses kilobits",NetworkMonitor.Speed(3750,false)=="30 Кбит/с");
        Check("Sub-kilobit traffic stays visible",NetworkMonitor.Speed(7,false)=="56 бит/с");
        Check("No traffic is a real zero",NetworkMonitor.Speed(0,false)=="0 бит/с");
        Check("Fractional base units are not a false zero",NetworkMonitor.Speed(0.05,false)=="< 1 бит/с");
        Check("Integer rounding promotes the next unit",NetworkMonitor.Speed(999999.9/8,false)=="1 Мбит/с");
        Check("Gigabit traffic uses gigabits",NetworkMonitor.Speed(125000000,false)=="1 Гбит/с");
        Check("Byte mode also switches units",NetworkMonitor.Speed(1500,true)=="2 КБ/с"&&NetworkMonitor.Speed(5,true)=="5 Б/с");
        Check("Invalid measurements never look like traffic",NetworkMonitor.Speed(Double.NaN,false)=="—"&&NetworkMonitor.Speed(Double.PositiveInfinity,false)=="—"&&NetworkMonitor.Speed(-1,false)=="—");
        Check("Unknown rate is not zero",NetworkMonitor.Speed(null,false)=="—");
        DateTime now=DateTime.UtcNow;var window=new PingWindow();var frame=new NetworkFrame();window.Fill(frame,now);
        Check("No packet samples means unknown loss",frame.Loss==null&&frame.Jitter==null);
        window.Add(now,10);window.Add(now.AddSeconds(1),30);window.Add(now.AddSeconds(2),null);window.Fill(frame,now.AddSeconds(2));
        Check("Loss denominator includes timeouts",frame.Sent==3&&frame.Replies==2&&Math.Abs(frame.Loss.Value-100.0/3)<0.0001);
        Check("Jitter compares consecutive successful replies",frame.Jitter==20);
        window.Add(now.AddSeconds(3),90);window.Fill(frame,now.AddSeconds(3));Check("Jitter does not bridge timeouts",frame.Jitter==20);
        window.Fill(frame,now.AddSeconds(64));Check("Old packet samples expire",frame.Loss==null&&frame.Sent==0);
        for(int i=0;i<100;i++)window.Add(now.AddMilliseconds(i),0);window.Fill(frame,now.AddSeconds(1));Check("Packet history bounded at sixty",frame.Sent==60);
        window.Clear();window.Fill(frame,now);Check("Target reset clears statistics",frame.Sent==0);
        Check("Reject ping URLs",!NetworkMonitor.ValidTarget("https://example.com"));Check("Accept IPv6 target",NetworkMonitor.ValidTarget("2606:4700:4700::1111"));Check("Reject unspecified target",!NetworkMonitor.ValidTarget("0.0.0.0"));
        var p=NetworkOptions.Parse("enabled=True\nhotkey=F9\nsize=28\ntarget=https://invalid\nmegabytes=invalid");
        Check("Legacy overlay settings cannot activate a window",p.Target=="1.1.1.1"&&!p.Megabytes&&!p.Serialize().Contains("enabled=")&&!p.Serialize().Contains("hotkey="));
        var saved=NetworkOptions.Parse(new NetworkOptions{Megabytes=true,Target="8.8.8.8",Adapter="test-adapter"}.Serialize());
        Check("Measurement settings round trip",saved.Megabytes&&saved.Target=="8.8.8.8"&&saved.Adapter=="test-adapter");
        string envelope=SettingsStorage.Wrap("megabytes=True\n");Check("Settings checksum round trip",SettingsStorage.Decode(envelope)=="megabytes=True\n");
        Check("Truncated settings fail integrity check",SettingsStorage.Decode(envelope.Substring(0,envelope.Length-2))==null);
        Check("Modified settings fail integrity check",SettingsStorage.Decode(envelope.Replace("True","False"))==null);
        string path=Path.Combine(folder,"network-storage-test.txt");SettingsStorage.Write(path,"megabytes=False\n");SettingsStorage.Write(path,"megabytes=True\n");Check("Encrypted-compatible storage can replace saved settings",SettingsStorage.Read(path)=="megabytes=True\n");
        File.WriteAllText(path,"BLOCKMOOK-SETTINGS-SHA256 partial");Check("Interrupted main write recovers completed pending copy",SettingsStorage.Read(path)=="megabytes=True\n");
        SettingsStorage.Write(path,"megabytes=False\n");File.WriteAllText(path+".tmp","partial");Check("Interrupted pending write preserves previous settings",SettingsStorage.Read(path)=="megabytes=False\n");
        string legacy=Path.Combine(folder,"old-overlay.txt"),current=Path.Combine(folder,"new-network-"+Guid.NewGuid().ToString("N")+".txt");
        SettingsStorage.Write(legacy,"enabled=True\nhotkey=F9\nmegabytes=True\ntarget=8.8.8.8\nadapter=legacy-adapter\n");
        var imported=NetworkOptions.Load(current,legacy);Check("Import only old measurement preferences",imported.Megabytes&&imported.Target=="8.8.8.8"&&imported.Adapter=="legacy-adapter"&&!imported.Serialize().Contains("enabled"));
        SettingsStorage.Write(current,new NetworkOptions().Serialize());Check("New measurement settings take precedence",NetworkOptions.Load(current,legacy).Target=="1.1.1.1");
        var appAssembly=typeof(NetworkOptions).Assembly;
        Check("No floating window implementation in assembly",appAssembly.GetType("BlockMook.OverlayWindow")==null&&appAssembly.GetType("BlockMook.DesktopNative")==null);
        Check("No global hotkey imports in assembly",!appAssembly.GetTypes().SelectMany(x=>x.GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)).Any(x=>x.Name=="RegisterHotKey"||x.Name=="UnregisterHotKey"));
        Check("Public release and application version agree",Updates.DisplayVersion=="1.0"&&Updates.Installed==new Version(1,0,0)&&!Updates.DevelopmentBuild);
        var history=new SessionHistory();for(int i=0;i<110;i++)history.Add("event-"+i);Check("Event history bounded at 100",history.Text.Split('\n').Length==100&&history.Text.Contains("event-109")&&!history.Text.Contains("event-9\n"));history.Clear();Check("Clear session history",history.Text=="");
    }
}
internal sealed partial class MainWindow {
    private void TestNetworkUi(){
        Navigate("Network");UiAssert(!networkTimer.IsEnabled,"smoke never starts live probes");
        UiAssert(Window.FindName("OverlayEnabled")==null&&Application.Current.Windows.Count==1,"network page creates no floating window or toggle");
        Window.WindowState=WindowState.Minimized;UiAssert(!NetworkVisible&&!networkTimer.IsEnabled,"minimized window does not measure");Window.WindowState=WindowState.Normal;
        Window.Hide();UiAssert(!NetworkVisible&&!networkTimer.IsEnabled,"tray does not measure");Window.Show();
        Navigate("Home");UiAssert(!NetworkVisible&&!networkTimer.IsEnabled,"other pages do not measure");Navigate("Network");
        var frame=new NetworkFrame{Time=DateTime.UtcNow,Down=3100000,Up=400000,Ping=18,Loss=0,Jitter=2,Sent=30,Replies=30,Adapter="ТЕСТОВЫЙ ПРЕДПРОСМОТР",Target="192.0.2.1",Status="Тестовые данные"};
        networkFrame=frame;PaintNetwork();CaptureUi("-Network-TEST");ResetNetworkSamples();
        var networkBody=(StackPanel)Find<ScrollViewer>("NetworkPage").Content;foreach(var panel in networkBody.Children.OfType<Border>()){var expander=panel.Child as Expander;if(expander!=null)expander.IsExpanded=true;}
        Find<ScrollViewer>("NetworkPage").ScrollToVerticalOffset(460);CaptureUi("-Network-Settings");Find<ScrollViewer>("NetworkPage").ScrollToBottom();CaptureUi("-Network-Address");
        foreach(var panel in networkBody.Children.OfType<Border>()){var expander=panel.Child as Expander;if(expander!=null)expander.IsExpanded=false;}Find<ScrollViewer>("NetworkPage").ScrollToTop();Navigate("Home");
        UiAssert(System.Windows.Shell.WindowChrome.GetWindowChrome(Window).CaptionHeight==36,"custom black caption retains native drag zone");
    }
}
}
