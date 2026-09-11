using System;
using System.ComponentModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace BlockMook {
internal sealed class TrayColors : Forms.ProfessionalColorTable {
    private static readonly Drawing.Color Surface=Drawing.Color.FromArgb(12,14,15),Accent=Drawing.Color.FromArgb(32,54,53),Border=Drawing.Color.FromArgb(63,97,94);
    public TrayColors(){UseSystemColors=false;}
    public override Drawing.Color ToolStripDropDownBackground{get{return Surface;}}
    public override Drawing.Color MenuItemSelected{get{return Accent;}}
    public override Drawing.Color MenuItemBorder{get{return Border;}}
    public override Drawing.Color MenuBorder{get{return Border;}}
    public override Drawing.Color SeparatorDark{get{return Border;}}
    public override Drawing.Color SeparatorLight{get{return Surface;}}
}
internal static class InstanceSignal {
    internal static string Name {get{return "Local\\BlockMook-Show-"+WindowsIdentity.GetCurrent().User.Value;}}
    internal static bool ShowExisting(){try{using(var signal=EventWaitHandle.OpenExisting(Name)){signal.Set();return true;}}catch(WaitHandleCannotBeOpenedException){return false;}}
}
internal sealed partial class MainWindow {
    private Forms.NotifyIcon tray;
    private Forms.ContextMenuStrip trayMenu;
    private Forms.ToolStripMenuItem trayToggle,trayStatus,trayDiagnose;
    private Drawing.Icon trayImage;
    private EventWaitHandle showSignal;
    private RegisteredWaitHandle showWait;
    private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
    private bool exitRequested,userStopRequested,desktopDisposed;
    internal bool StartupLaunch;
    private void SetupDesktop(){
        foreach(string id in new[]{"CloseToTray","AutoRecover","ReconnectOnChange","Notifications","AutoConnect"}){
            var control=Find<CheckBox>(id);control.IsChecked=id=="CloseToTray"?preferences.CloseToTray:id=="AutoRecover"?preferences.AutoRecover:id=="ReconnectOnChange"?preferences.ReconnectOnChange:id=="Notifications"?preferences.Notifications:preferences.AutoConnect;
            control.Click+=(s,e)=>{
                preferences.CloseToTray=Find<CheckBox>("CloseToTray").IsChecked==true;
                preferences.AutoRecover=Find<CheckBox>("AutoRecover").IsChecked==true;
                preferences.ReconnectOnChange=Find<CheckBox>("ReconnectOnChange").IsChecked==true;
                preferences.Notifications=Find<CheckBox>("Notifications").IsChecked==true;
                preferences.AutoConnect=Find<CheckBox>("AutoConnect").IsChecked==true;
                if(!preferences.AutoRecover&&recovering){recovery.Stop();userStopRequested=true;if(connectionCancellation!=null)connectionCancellation.Cancel();}
                SavePreferences();PaintDesktop();
            };
        }
        try{Find<CheckBox>("LaunchAtLogin").IsChecked=!isSmoke&&DesktopIntegration.StartupEnabled();}
        catch(Exception ex){Find<CheckBox>("LaunchAtLogin").IsEnabled=false;Find<TextBlock>("DesktopStatus").Text="Не удалось прочитать автозапуск: "+ex.Message;}
        Find<CheckBox>("LaunchAtLogin").Click+=(s,e)=>{
            try{if(!isSmoke)DesktopIntegration.SetStartup(Find<CheckBox>("LaunchAtLogin").IsChecked==true);Find<TextBlock>("DesktopStatus").Text="Настройка автозапуска сохранена.";}
            catch(Exception ex){Find<CheckBox>("LaunchAtLogin").IsChecked=!(Find<CheckBox>("LaunchAtLogin").IsChecked==true);Find<TextBlock>("DesktopStatus").Text=ex.Message;}
        };
        Find<Button>("CreateShortcut").Click+=(s,e)=>{try{Find<TextBlock>("DesktopStatus").Text=isSmoke?"Тест: ярлык не создавался.":"Ярлык создан: "+DesktopIntegration.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));}catch(Exception ex){Find<TextBlock>("DesktopStatus").Text=ex.Message;}};
        Find<Button>("ExitApp").Click+=(s,e)=>RequestExit();
        Find<Button>("TrayNoticeHide").Click+=(s,e)=>{CancelCloseChoice();Window.Hide();};
        Find<Button>("TrayNoticeExit").Click+=(s,e)=>RequestExit();
        Find<Button>("TrayNoticeCancel").Click+=(s,e)=>CancelCloseChoice();
        Window.Closing+=OnWindowClosing;
        if(isSmoke)return;
        trayMenu=new Forms.ContextMenuStrip{BackColor=Drawing.Color.FromArgb(12,14,15),ForeColor=Drawing.Color.FromArgb(145,230,222),ShowImageMargin=false,Font=new Drawing.Font("Segoe UI",10),Renderer=new Forms.ToolStripProfessionalRenderer(new TrayColors())};
        trayStatus=new Forms.ToolStripMenuItem("BlockMook · отключён"){Enabled=false};trayMenu.Items.Add(trayStatus);
        trayMenu.Items.Add("Открыть BlockMook",null,(s,e)=>RestoreWindow());
        trayToggle=new Forms.ToolStripMenuItem("Подключить",null,async(s,e)=>await UserToggle());trayMenu.Items.Add(trayToggle);
        trayDiagnose=new Forms.ToolStripMenuItem("Диагностика",null,(s,e)=>{RestoreWindow();Navigate("Diagnostics");});trayMenu.Items.Add(trayDiagnose);
        trayMenu.Items.Add("Настройки",null,(s,e)=>{RestoreWindow();Navigate("Settings");});
        trayMenu.Items.Add(new Forms.ToolStripSeparator());trayMenu.Items.Add("Выйти",null,(s,e)=>RequestExit());
        using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("BlockMook.ico"))using(var icon=new Drawing.Icon(input))trayImage=(Drawing.Icon)icon.Clone();
        tray=new Forms.NotifyIcon{Icon=trayImage,Text="BlockMook · отключён",ContextMenuStrip=trayMenu,Visible=true};
        tray.DoubleClick+=(s,e)=>RestoreWindow();tray.BalloonTipClicked+=(s,e)=>{RestoreWindow();Navigate("Diagnostics");};
        var security=new EventWaitHandleSecurity();security.AddAccessRule(new EventWaitHandleAccessRule(WindowsIdentity.GetCurrent().User,EventWaitHandleRights.FullControl,AccessControlType.Allow));
        bool created;showSignal=new EventWaitHandle(false,EventResetMode.AutoReset,InstanceSignal.Name,out created,security);
        showWait=ThreadPool.RegisterWaitForSingleObject(showSignal,(s,t)=>Post(RestoreWindow),null,Timeout.Infinite,false);
        NetworkChange.NetworkAddressChanged+=NetworkChanged;
        SystemEvents.PowerModeChanged+=PowerChanged;
        Application.Current.SessionEnding+=SessionEnding;
    }
    private void Post(Action action){if(!closing&&!Window.Dispatcher.HasShutdownStarted)Window.Dispatcher.BeginInvoke(action);}
    private void NetworkChanged(object sender,EventArgs args){Post(()=>ConnectionEnvironmentChanged("Сеть изменилась. Проверим соединение."));}
    private void PowerChanged(object sender,PowerModeChangedEventArgs args){if(args.Mode==PowerModes.Resume)Post(()=>ConnectionEnvironmentChanged("Компьютер вышел из сна. Проверим соединение."));}
    private void SessionEnding(object sender,SessionEndingCancelEventArgs args){exitRequested=true;lifetime.Cancel();if(connectionCancellation!=null)connectionCancellation.Cancel();bridge.Dispose();DisposeDesktop();}
    private void RestoreWindow(){if(exitRequested||closing)return;Window.Show();if(Window.WindowState==WindowState.Minimized)Window.WindowState=WindowState.Normal;Window.Activate();}
    private void OnWindowClosing(object sender,CancelEventArgs args){
        if(closing||isSmoke){DisposeDesktop();return;}
        args.Cancel=true;
        if(!exitRequested){
            ShowCloseChoice();
            return;
        }
        RequestExit();
    }
    private void ShowCloseChoice(){
        Find<Grid>("AppContent").IsEnabled=false;Find<Grid>("TrayNotice").Visibility=Visibility.Visible;
        Find<Button>("TrayNoticeHide").IsEnabled=isSmoke||tray!=null;
        Find<Button>("TrayNoticeCancel").Focus();
    }
    private void CancelCloseChoice(){
        Find<Grid>("TrayNotice").Visibility=Visibility.Collapsed;
        Find<Grid>("AppContent").IsEnabled=Find<Grid>("Catalog").Visibility!=Visibility.Visible&&Find<Grid>("Welcome").Visibility!=Visibility.Visible;
    }
    private void RequestExit(){
        if(exitRequested)return;exitRequested=true;recovery.Stop();timer.Stop();lifetime.Cancel();
        if(connectionCancellation!=null)connectionCancellation.Cancel();if(diagnosticCancellation!=null)diagnosticCancellation.Cancel();if(updateCancellation!=null)updateCancellation.Cancel();
        bridge.Dispose();running=false;Find<TextBlock>("DesktopStatus").Text="Завершаем операции…";
        if(!isSmoke)Post(TryCompleteExit);
    }
    private void TryCompleteExit(){if(!exitRequested||busy||updateCancellation!=null)return;closing=true;DisposeDesktop();Window.Close();}
    private void DisposeDesktop(){
        if(desktopDisposed)return;desktopDisposed=true;DisposeNetwork();timer.Stop();lifetime.Cancel();bridge.Dispose();
        if(showWait!=null)showWait.Unregister(null);if(showSignal!=null)showSignal.Dispose();
        if(tray!=null){tray.Visible=false;tray.Dispose();}if(trayMenu!=null)trayMenu.Dispose();if(trayImage!=null)trayImage.Dispose();
        if(!isSmoke){NetworkChange.NetworkAddressChanged-=NetworkChanged;SystemEvents.PowerModeChanged-=PowerChanged;if(Application.Current!=null)Application.Current.SessionEnding-=SessionEnding;}
    }
    private void NotifyFailure(string message){if(tray==null||!preferences.Notifications||exitRequested)return;tray.ShowBalloonTip(7000,"BlockMook · требуется внимание",message,Forms.ToolTipIcon.Warning);}
    private void PaintDesktop(){
        Find<CheckBox>("ReconnectOnChange").IsEnabled=preferences.AutoRecover;
        Find<Button>("QuickDiagnostic").IsEnabled=!busy&&!exitRequested;
        if(tray==null)return;
        string status=recovering?"восстановление":recovery.Paused?"нужна проверка":running?"подключён":"отключён";
        tray.Text="BlockMook · "+status;trayStatus.Text=tray.Text;
        trayToggle.Text=recovery.Wanted||running||connectionCancellation!=null?"Отключить":"Подключить";
        trayToggle.Enabled=!exitRequested&&(!busy||healthChecking||connectionCancellation!=null);
    }
    private void TestDesktopUi(){
        Navigate("Home");recovery.Start(DateTime.UtcNow,3);running=true;
        preferences.TrayExplained=false;ShowCloseChoice();UiAssert(Find<Grid>("TrayNotice").Visibility==Visibility.Visible&&Window.IsVisible,"close offers explicit choices");CaptureUi("-TrayNotice");
        UiClick("TrayNoticeHide");UiAssert(!Window.IsVisible&&running&&recovery.Wanted,"hiding keeps connection intent");
        RestoreWindow();UiAssert(Window.IsVisible&&Find<Grid>("AppContent").IsEnabled,"restore hidden window");
        preferences.TrayExplained=true;preferences.CloseToTray=false;ShowCloseChoice();UiAssert(Window.IsVisible&&Find<Grid>("TrayNotice").Visibility==Visibility.Visible,"every close asks even with old preferences");
        UiClick("TrayNoticeCancel");UiAssert(Window.IsVisible&&Find<Grid>("TrayNotice").Visibility==Visibility.Collapsed&&running&&recovery.Wanted,"cancel leaves connection and window intact");
        ShowWelcome();ShowCloseChoice();UiClick("TrayNoticeCancel");UiAssert(Find<Grid>("Welcome").Visibility==Visibility.Visible&&!Find<Grid>("AppContent").IsEnabled,"cancel preserves underlying onboarding modal");DismissWelcome();
        ShowCloseChoice();UiClick("TrayNoticeHide");UiAssert(!Window.IsVisible,"explicit tray choice hides on repeated close");RestoreWindow();recovery.Stop();running=false;
        ShowCatalog();ShowCloseChoice();
        var escape=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(Window),Environment.TickCount,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};Window.RaiseEvent(escape);
        UiAssert(escape.Handled&&Find<Grid>("TrayNotice").Visibility==Visibility.Collapsed&&Find<Grid>("Catalog").Visibility==Visibility.Visible&&!Find<Grid>("AppContent").IsEnabled,"Escape cancels only top close dialog and preserves catalog");CloseCatalog();
        recovery.Start(DateTime.UtcNow,3);MonitorConnection().GetAwaiter().GetResult();UiAssert(recovery.Paused&&!bridge.Connected,"lost worker pauses without automatic elevation");recovery.Stop();
        using(var cancellation=new CancellationTokenSource()){
            busy=true;connectionCancellation=cancellation;recovery.Start(DateTime.UtcNow,3);UserToggle().GetAwaiter().GetResult();UiAssert(cancellation.IsCancellationRequested&&!recovery.Wanted&&userStopRequested,"manual disconnect cancels pending recovery");connectionCancellation=null;userStopRequested=false;busy=false;
        }
        Navigate("Settings");Find<ScrollViewer>("SettingsPage").ScrollToBottom();CaptureUi("-BackgroundSettings");
        var autoConnect=Find<CheckBox>("AutoConnect");autoConnect.IsChecked=true;autoConnect.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));UiAssert(preferences.AutoConnect,"auto-connect preference bound");autoConnect.IsChecked=false;autoConnect.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        UiClick("CreateShortcut");UiAssert(Find<TextBlock>("DesktopStatus").Text.StartsWith("Тест:"),"smoke never writes Desktop");
        Find<ScrollViewer>("SettingsPage").ScrollToTop();Navigate("Home");
    }
    private void TestExitUi(){
        recovery.Start(DateTime.UtcNow,3);
        using(var cancellation=new CancellationTokenSource()){
            connectionCancellation=cancellation;ShowCloseChoice();UiClick("TrayNoticeExit");UiAssert(exitRequested&&!recovery.Wanted&&lifetime.IsCancellationRequested&&cancellation.IsCancellationRequested,"full exit choice cancels recovery and connection");connectionCancellation=null;
        }
    }
}
}
