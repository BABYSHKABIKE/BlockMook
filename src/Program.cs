using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace BlockMook {
internal static class Program {
    [STAThread] public static int Main(string[] args) {
        try {
            if(args.Length==1 && args[0]=="--test-child") {Thread.Sleep(30000);return 0;}
            if(args.Length==2 && args[0]=="--engine-test") {Tests.EngineParameters(args[1]);return 0;}
            if(args.Length==4 && args[0]=="--worker") { Worker.Run(args[1],Int32.Parse(args[2]),Int64.Parse(args[3])).GetAwaiter().GetResult(); return 0; }
            if(args.Length==2 && args[0]=="--test") { Tests.Run(args[1]); return 0; }
            if((args.Length==2||args.Length==3) && args[0]=="--probe") {
                int mask=args.Length==3?Int32.Parse(args[2]):3;
                var results=Probes.All(mask,CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllLines(args[1],new[]{"Environment: "+(Core.Conflict()??"No known conflict")}.Concat(results.SelectMany(r=>new[]{Probes.Urls[r.Index]+" | "+r.Ok+" | "+r.Detail}.Concat(r.Checks))));
                return 0;
            }
            bool smoke=args.Length==2&&args[0]=="--ui-smoke",created;
            using(var mutex=new Mutex(true,InstanceMutexName(smoke),out created)) {
                if(!created) { if(!args.Contains("--startup")&&!InstanceSignal.ShowExisting())MessageBox.Show("BlockMook уже запускается. Повтори открытие через несколько секунд.","BlockMook");return 0; }
                var app=new Application();
                var controller=new MainWindow(smoke);
                if(smoke) controller.SmokePath=args[1];
                controller.StartupLaunch=args.Contains("--startup");
                app.Run(controller.Window);
            }
            return 0;
        } catch(Exception ex) {
            if(args.Length>0 && args[0]=="--worker") return 1;
            if(args.Length==2 && (args[0]=="--test" || args[0]=="--probe" || args[0]=="--engine-test" || args[0]=="--ui-smoke")) { File.WriteAllText(args[1],"FAIL: "+ex); return 1; }
            MessageBox.Show(ex.Message,"BlockMook · ошибка",MessageBoxButton.OK,MessageBoxImage.Error); return 1;
        }
    }
    internal static string InstanceMutexName(bool smoke){return "Local\\BlockMook-Desktop-"+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value+(smoke?"-Smoke-"+Guid.NewGuid().ToString("N"):"");}
}
internal sealed partial class MainWindow {
    internal Window Window;
    internal string SmokePath;
    private readonly Bridge bridge=new Bridge();
    private readonly DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(3) };
    private bool busy,running,closing;
    private bool manualProfile;
    private CancellationTokenSource connectionCancellation;
    private int profile;
    private readonly Button connect,auto,probe,diagnostic,cancelDiagnostic,openReports;
    private readonly TextBlock diagnosticStatus;
    private CancellationTokenSource diagnosticCancellation;
    private readonly CheckBox youtube,discord;
    private readonly TextBlock state,detail,environment,profileName,probeTime;
    private readonly TextBlock[] results;
    private readonly TextBox log;
    private readonly Button[] profiles;
    private T Find<T>(string name) where T:class { return Window.FindName(name) as T; }
    internal MainWindow(bool smoke=false) {
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")) Window=(Window)XamlReader.Load(stream);
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BlockMook.ico")) Window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.None,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        connect=Find<Button>("Connect"); auto=Find<Button>("Auto"); probe=Find<Button>("Probe");
        diagnostic=Find<Button>("Diagnostic");cancelDiagnostic=Find<Button>("CancelDiagnostic");openReports=Find<Button>("OpenReports");diagnosticStatus=Find<TextBlock>("DiagnosticStatus");
        youtube=Find<CheckBox>("YouTube"); discord=Find<CheckBox>("Discord");
        state=Find<TextBlock>("State"); detail=Find<TextBlock>("StateDetail"); environment=Find<TextBlock>("Environment"); profileName=Find<TextBlock>("ProfileName"); probeTime=Find<TextBlock>("ProbeTime"); log=Find<TextBox>("Log");
        results=Enumerable.Range(0,6).Select(i=>Find<TextBlock>("Result"+i)).ToArray();
        profiles=Enumerable.Range(0,3).Select(i=>Find<Button>("Profile"+i)).ToArray();
        SetupUi(smoke);
        SetupDesktop();
        SetupChrome();
        SetupNetwork();
        manualProfile=preferences.ManualProfile>=0;profile=manualProfile?preferences.ManualProfile:Core.LoadProfile(Mask); PaintProfile();
        connect.Click+=async(s,e)=>await UserToggle();
        auto.Click+=(s,e)=>{manualProfile=false;preferences.ManualProfile=-1;profile=Core.LoadProfile(Mask);PaintProfile();SavePreferences();};
        probe.Click+=async(s,e)=>await Guard(QuickDiagnostic);
        diagnostic.Click+=async(s,e)=>await Guard(RunDiagnostic);
        cancelDiagnostic.Click+=(s,e)=>{if(diagnosticCancellation!=null){diagnosticCancellation.Cancel();cancelDiagnostic.IsEnabled=false;diagnosticStatus.Text="Останавливаем проверку…";}};
        openReports.Click+=(s,e)=>{try{Directory.CreateDirectory(DiagnosticReport.Folder);Process.Start(new ProcessStartInfo(DiagnosticReport.Folder){UseShellExecute=true});}catch(Exception ex){diagnosticStatus.Text=ex.Message;}};
        for(int i=0;i<3;i++) { int value=i; profiles[i].Click+=(s,e)=>{manualProfile=true;profile=value;preferences.ManualProfile=value;PaintProfile();SavePreferences();}; }
        youtube.Click+=(s,e)=>ServicesChanged(youtube);
        discord.Click+=(s,e)=>ServicesChanged(discord);
        Window.SourceInitialized+=(s,e)=>{int dark=1; Native.DwmSetWindowAttribute(new WindowInteropHelper(Window).Handle,20,ref dark,4);};
        Window.Loaded+=(s,e)=> {
            try { Core.VerifyEngine(); Write("Сетевые компоненты найдены и совпадают с хешами сборки."); } catch(Exception ex) { Write(ex.Message); detail.Text=ex.Message; }
            UpdateEnvironment(); Controls();if(!isSmoke)timer.Start();
            if(!isSmoke&&StartupLaunch&&preferences.WelcomeDone)Post(async()=>{if(preferences.CloseToTray)Window.Hide();if(preferences.AutoConnect){RestoreWindow();await UserToggle();}});
            if(!preferences.WelcomeDone && SmokePath==null)ShowWelcome();
            if(SmokePath!=null) {
                try {
                    if(results.Any(r=>r==null) || connect==null || diagnostic==null || cancelDiagnostic==null || openReports==null || diagnosticStatus==null || profiles.Any(p=>p==null)) throw new Exception("Missing UI control");
                    RunUiSmoke();
                } catch(Exception ex) {File.WriteAllText(SmokePath,"FAIL: "+ex);}
                Window.Close();
            }
        };
        timer.Tick+=async(s,e)=>{if(!busy&&!closing&&!exitRequested)await Guard(MonitorConnection);};
    }
    private int Mask {get{return Services.Items.Where(s=>ServiceControl(s).IsChecked==true).Sum(s=>s.Bit);}}
    private void Write(string message) { if(closing)return; log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine); if(log.Text.Length>14000)log.Text=log.Text.Substring(log.Text.Length-10000);log.ScrollToEnd(); }
    private void UpdateEnvironment() { string conflict=Core.Conflict();environment.Text=conflict==null?"":"Обнаружен другой VPN или другой сетевой инструмент. Отключи его перед подключением.";environment.ToolTip=conflict;Find<FrameworkElement>("EnvironmentBanner").Visibility=conflict==null?Visibility.Collapsed:Visibility.Visible; }
    private void PaintProfile() { profileName.Text=(manualProfile?"Вручную · ":"Автоматически · ")+Core.Names[profile];for(int i=0;i<3;i++)profiles[i].Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(manualProfile&&i==profile?"#3800E8D2":"#2200E8D2"));auto.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(manualProfile?"#2200E8D2":"#3800E8D2")); }
    private void Controls() { ObserveConnectionState();PaintDesktop(); Find<TextBlock>("ConnectionBadge").Text=connectionCancellation!=null?"ПОДКЛЮЧАЕМ":diagnosticCancellation!=null?"ДИАГНОСТИКА":running?"ДОСТУП ВКЛЮЧЁН":"НЕ ПОДКЛЮЧЕНО"; connect.IsEnabled=!exitRequested&&(!busy||connectionCancellation!=null||healthChecking);auto.IsEnabled=!busy&&!running&&!recovery.Wanted;probe.IsEnabled=!busy;diagnostic.IsEnabled=!busy;foreach(var service in Services.Items)ServiceControl(service).IsEnabled=!busy&&!running&&!recovery.Wanted;Find<Button>("AddService").IsEnabled=!busy&&!running&&!recovery.Wanted;Find<Button>("ShowWelcome").IsEnabled=!busy&&!running&&!recovery.Wanted;foreach(var p in profiles)p.IsEnabled=!busy&&!running&&!recovery.Wanted;connect.Content=connectionCancellation!=null?"Отменить":running||recovery.Wanted?"Отключить":"Подключить";Find<System.Windows.Shapes.Ellipse>("StateDot").Fill=new SolidColorBrush((Color)ColorConverter.ConvertFromString(running?"#00E8D2":busy?"#C6C9CC":"#80838A")); }
    private async Task RunDiagnostic() {
        recovery.Stop();
        if(running)await Stop();
        using(var report=new DiagnosticReport(DiagnosticReport.Folder))
        using(var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) {
            cancellation.CancelAfter(TimeSpan.FromMinutes(3));diagnosticCancellation=cancellation;cancelDiagnostic.Visibility=Visibility.Visible;cancelDiagnostic.IsEnabled=true;
            diagnosticStatus.Text="Отчёт сохраняется по мере проверки: "+report.PathName;
            bool startPending=false;
            try {
                var outcome=await Diagnostics.Run(report,Mask,Core.Conflict,
                    async()=>{Core.VerifyEngine();await bridge.Connect();},()=>Probes.All(Mask,cancellation.Token),
                    async selected=>{startPending=true;if(await bridge.Send("START|"+selected+"|"+Mask)!="RUNNING")throw new IOException("Движок не подтвердил запуск");},
                    async()=>{if(!bridge.Connected){if(startPending)throw new IOException("Канал потерян после команды запуска");return;}if(await bridge.Send("STOP")!="STOPPED")throw new IOException("Движок не подтвердил остановку");startPending=false;},
                    bridge.Dispose,cancellation.Token,message=>{state.Text=message;Write(message);});
                state.Text=outcome.Status=="ЗАВЕРШЕНО"?"Диагностика завершена":outcome.Status=="ПРЕРВАНО"?"Диагностика прервана":"Диагностика не завершена";
                detail.Text=outcome.StopConfirmed?"Подключение выключено. Результат — в разделе «Диагностика».":"Остановка не подтверждена. Подробности — в отчёте.";
                diagnosticStatus.Text=(outcome.Error==null?"":outcome.Error+"\n")+"Отчёт: "+report.PathName;
            } finally {bridge.Dispose();running=false;diagnosticCancellation=null;cancelDiagnostic.Visibility=Visibility.Collapsed;UpdateEnvironment();}
        }
    }
    private async Task Guard(Func<Task> action) {
        if(busy||closing||exitRequested)return;busy=true;Controls();
        try {await action();}
        catch(Exception ex) {
            if(closing||exitRequested)return;
            // A failed command must not leave an untracked engine running.
            bridge.Dispose();running=false;state.Text="Не удалось выполнить";
            if(ex is OperationCanceledException)state.Text="Подключение отменено";
            detail.Text=ex is OperationCanceledException?"Сетевой компонент остановлен. Можно повторить подключение.":ex is System.ComponentModel.Win32Exception && ((System.ComponentModel.Win32Exception)ex).NativeErrorCode==1223?"Запрос прав администратора отменён. Ничего не включено.":ex.Message;
            Write(detail.Text);
        } finally {busy=false;if(!closing)Controls();Post(TryCompleteExit);}
        if(userStopRequested&&!exitRequested){userStopRequested=false;await Guard(Stop);}
    }
    private void EnsureClear() { string conflict=Core.Conflict();if(conflict!=null)throw new InvalidOperationException(conflict);Core.Domains(Mask);Core.VerifyEngine(); }
    private async Task Start() {
        if(recovering&&!bridge.Connected)throw new IOException("Нужно повторное подтверждение прав. Подключись вручную.");
        EnsureClear();state.Text="Подключаем…";detail.Text="Подтверди запрос прав в окне Windows.";
        await bridge.Connect();lifetime.Token.ThrowIfCancellationRequested();if(connectionCancellation!=null)connectionCancellation.Token.ThrowIfCancellationRequested();
        if(await bridge.Send("START|"+profile+"|"+Mask)!="RUNNING")throw new IOException("Движок не подтвердил запуск");
        running=true;state.Text="Проверяем доступ…";detail.Text="Подбираем рабочий способ подключения.";
        Find<FrameworkElement>("EnvironmentBanner").Visibility=Visibility.Collapsed;Write("Запущен профиль «"+Core.Names[profile]+"».");
    }
    private async Task Stop() {
        if(bridge.Connected)await bridge.Send("STOP");running=false;state.Text="Отключено";detail.Text="Чтобы снова открыть доступ, нажми «Подключить».";Write("Наш движок остановлен.");UpdateEnvironment();
    }
    private async Task Toggle() {if(running)await Stop();else await ConnectAutomatically();}
    private async Task<ProbeResult[]> Check() {
        foreach(var block in results){block.Text="Проверяем…";block.Foreground=Brushes.LightGray;}
        string conflict=running?null:Core.Conflict();
        probeTime.Text="Проверяем · до 10 секунд";
        var values=await Probes.All(Mask,connectionCancellation!=null?connectionCancellation.Token:quickCancellation!=null?quickCancellation.Token:lifetime.Token);
        if(closing)return values;
        foreach(var value in values){results[value.Index].Text=value.Ok?(value.Index>=3?"TLS: доступен":"Доступен"):"Не подтверждён";results[value.Index].Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value.Ok?"#00E8D2":"#C6C9CC"));results[value.Index].ToolTip=value.Detail+"\n"+String.Join("\n",value.Checks);Write(Services.ProbeName(value.Index)+": "+value.Detail);foreach(string check in value.Checks)Write(check);}
        Find<TextBlock>("ProbeDetails").Text=String.Join("\n\n",values.Select(v=>Services.ProbeName(v.Index)+" · "+v.Detail+"\n"+String.Join("\n",v.Checks)));
        PaintDiagnostic(values,conflict!=null);
        probeTime.Text="Проверено в "+DateTime.Now.ToString("HH:mm")+(conflict!=null?" · другой VPN активен":"");
        if(running){detail.Text=Probes.AllSelected(values,Mask)?"Проверки выбранных сервисов прошли. Теперь можно открыть приложения; видео и голос требуют отдельной проверки.":"Часть проверок не прошла. Причина и результаты компонентов доступны в журнале.";}
        return values;
    }
    private async Task ConnectAutomatically() {
        EnsureClear();
        using(var report=new DiagnosticReport(DiagnosticReport.Folder,true))
        using(var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) {
            cancellation.CancelAfter(TimeSpan.FromMinutes(3));connectionCancellation=cancellation;Controls();diagnosticStatus.Text="Отчёт подключения: "+report.PathName;
            try {
                var outcome=await ConnectionFlow.Run(profile,Mask,manualProfile,
                    async chosen=>{profile=chosen;PaintProfile();await Start();},Stop,bridge.Dispose,
                    async()=>{var values=await Check();if(!bridge.Connected||await bridge.Send("STATUS")!="RUNNING")throw new IOException("Движок завершился во время проверки");return values;},
                    message=>{report.Add(message);Write(message);},(chosen,values)=>report.Sample(Core.Names[chosen],values,Mask),cancellation.Token);
                running=outcome.Running;
                lastProvenMask=outcome.Results==null?0:Services.Selected(Mask).Where(service=>outcome.Results.Any(r=>r.Index==service.Index&&r.Ok)).Sum(service=>service.Bit);
                lastConnectionHealthy=outcome.Results!=null&&(recovering?Probes.AllSelected(outcome.Results,recovery.ProvenMask):outcome.Verified);
                if(!running){state.Text="Доступ не подтверждён";detail.Text="Проверенные стратегии не дали подтверждённого доступа. Обход выключен; отчёт сохранён.";}
                else{
                    state.Text=outcome.Verified?"Подключено":"Доступ частичный";
                    detail.Text=outcome.Verified?"Соединения проверены. Видео, сообщения и звонки проверь в выбранных приложениях.":"Часть проверок не прошла. Подключение включено; подробности — в «Помощи».";
                    if(outcome.Verified)try{Core.SaveProfile(outcome.Profile,Mask);}catch(Exception ex){Write("Не удалось сохранить профиль: "+ex.Message);}
                }
                report.Add("ИТОГ: "+(outcome.Verified?"ПРОВЕРЕНО":running?"ЧАСТИЧНО":"НЕ ПОДТВЕРЖДЕНО"));
            }catch(Exception ex){bridge.Dispose();running=false;report.Add("ИТОГ: "+(ex is OperationCanceledException?"ОТМЕНЕНО":"ОШИБКА")+" | "+ex.Message);throw;}
            finally{connectionCancellation=null;}
        }
    }
}
}
