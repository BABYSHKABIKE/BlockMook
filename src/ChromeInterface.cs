using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
namespace BlockMook {
internal sealed partial class MainWindow {
    private void SetupChrome(){
        Window.WindowStyle=WindowStyle.None;
        WindowChrome.SetWindowChrome(Window,new WindowChrome{CaptionHeight=36,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0),UseAeroCaptionButtons=false});
        Find<Button>("MinimizeWindow").Click+=(s,e)=>SystemCommands.MinimizeWindow(Window);
        Find<Button>("MaximizeWindow").Click+=(s,e)=>{if(Window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(Window);else SystemCommands.MaximizeWindow(Window);};
        Find<Button>("CloseWindow").Click+=(s,e)=>Window.Close();
        foreach(string id in new[]{"MinimizeWindow","MaximizeWindow","CloseWindow"})WindowChrome.SetIsHitTestVisibleInChrome(Find<Button>(id),true);
        Window.StateChanged+=(s,e)=>{Find<FrameworkElement>("WindowRoot").Margin=Window.WindowState==WindowState.Maximized?new Thickness(8):new Thickness(0);};
    }
}
}
