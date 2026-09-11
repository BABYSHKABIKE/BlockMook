using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BlockMook {
internal sealed partial class MainWindow {
    private int catalogMask;
    private CheckBox ServiceControl(ServiceDefinition service){return Find<CheckBox>(service.Index==5?"Meet":service.Name);}
    private void SetupCatalog(){
        foreach(var service in Services.Items){
            var control=ServiceControl(service);control.IsChecked=(preferences.Services&service.Bit)!=0;control.ToolTip=service.Description;
            if(service.Index>2)control.Click+=(s,e)=>ServicesChanged(control);
        }
        PaintServices();
        Find<Button>("AddService").Click+=(s,e)=>ShowCatalog();
        Find<Button>("CatalogClose").Click+=(s,e)=>CloseCatalog();
        Find<Button>("CatalogApply").Click+=(s,e)=>ApplyCatalog();
        Find<TextBox>("CatalogSearch").TextChanged+=(s,e)=>PaintCatalog();
        Window.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape&&Find<Grid>("Catalog").Visibility==Visibility.Visible){CloseCatalog();e.Handled=true;}};
    }
    private void PaintServices(){
        foreach(var service in Services.Items){ServiceControl(service).Visibility=(Mask&service.Bit)!=0?Visibility.Visible:Visibility.Collapsed;}
        foreach(int index in Enumerable.Range(0,results.Length))Find<FrameworkElement>("ProbeCard"+index).Visibility=Services.ProbeIndices(Mask).Contains(index)?Visibility.Visible:Visibility.Collapsed;
    }
    private void ResetChecks(){
        foreach(var result in results){result.Text="—";result.ToolTip=null;result.Foreground=Brushes.LightGray;}
        shareableReport=null;Find<Button>("ExportDiagnostic").IsEnabled=false;Find<TextBox>("DiagnosticPreview").Text="Выбор сервисов изменён. Выполни новую проверку.";
        probeTime.Text="Выбор изменён · нужна проверка";
        Find<TextBlock>("ProbeDetails").Text="Проверка соединений не заменяет проверку видео, сообщений и звонков в приложении.";
    }
    private void ShowCatalog(){
        if(busy||running||recovery.Wanted)return;
        catalogMask=Mask;Find<TextBox>("CatalogSearch").Text="";PaintCatalog();
        Find<Grid>("AppContent").IsEnabled=false;Find<Grid>("Catalog").Visibility=Visibility.Visible;Find<TextBox>("CatalogSearch").Focus();
    }
    private void CloseCatalog(){Find<Grid>("Catalog").Visibility=Visibility.Collapsed;Find<Grid>("AppContent").IsEnabled=true;Find<Button>("AddService").Focus();}
    private void CatalogCount(){
        int count=Services.Items.Count(s=>(catalogMask&s.Bit)!=0);
        Find<TextBlock>("CatalogCount").Text=count==0?"Выбери хотя бы один сервис":"Выбрано: "+count;
        Find<Button>("CatalogApply").IsEnabled=count>0&&!busy&&!running;
    }
    private void PaintCatalog(){
        var rows=Find<StackPanel>("CatalogRows");rows.Children.Clear();
        foreach(var service in Services.Search(Find<TextBox>("CatalogSearch").Text)){
            var definition=service;
            var text=new StackPanel();
            text.Children.Add(new TextBlock{Text=service.Name,FontSize=17,FontWeight=FontWeights.SemiBold});
            text.Children.Add(new TextBlock{Text=service.Support,FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(129,216,208)),Margin=new Thickness(0,4,0,8)});
            text.Children.Add(new TextBlock{Text=service.Description,FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(166,169,175)),TextWrapping=TextWrapping.Wrap});
            var toggle=new CheckBox{Content=text,IsChecked=(catalogMask&service.Bit)!=0,Margin=new Thickness(0,0,8,0)};
            AutomationProperties.SetName(toggle,service.Name);AutomationProperties.SetAutomationId(toggle,"CatalogService"+service.Bit);
            toggle.Click+=(s,e)=>{if(toggle.IsChecked==true)catalogMask|=definition.Bit;else catalogMask&=~definition.Bit;CatalogCount();};
            rows.Children.Add(new Border{Child=toggle,Padding=new Thickness(0,15,0,15),BorderThickness=new Thickness(0,0,0,1),BorderBrush=new SolidColorBrush(Color.FromRgb(48,49,53))});
        }
        if(rows.Children.Count==0)rows.Children.Add(new TextBlock{Text="Такого сервиса пока нет в каталоге.",Margin=new Thickness(0,22,0,22),Foreground=Brushes.LightGray});
        CatalogCount();
    }
    private void ApplyCatalog(){
        if(!Services.ValidMask(catalogMask)||busy||running||recovery.Wanted)return;
        foreach(var service in Services.Items)ServiceControl(service).IsChecked=(catalogMask&service.Bit)!=0;
        preferences.Services=Mask;if(!manualProfile){profile=Core.LoadProfile(Mask);PaintProfile();}
        SavePreferences();PaintServices();ResetChecks();CloseCatalog();
    }
    private void TestCatalogUi(){
        ShowCatalog();UiAssert(!Find<Grid>("AppContent").IsEnabled,"catalog modal");CaptureUi("-Catalog");
        Find<TextBox>("CatalogSearch").Text="meet";UiAssert(Find<StackPanel>("CatalogRows").Children.Count==1,"catalog search");
        var row=(Border)Find<StackPanel>("CatalogRows").Children[0];var toggle=(CheckBox)row.Child;
        toggle.IsChecked=true;toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Find<TextBox>("CatalogSearch").Text="absent-service";UiAssert(Find<StackPanel>("CatalogRows").Children[0] is TextBlock,"empty search");
        Find<TextBox>("CatalogSearch").Text="";UiAssert((catalogMask&16)!=0,"selection survives filtering");
        UiClick("CatalogClose");UiAssert((Mask&16)==0,"cancel does not save draft");
        ShowCatalog();catalogMask=0;CatalogCount();UiAssert(!Find<Button>("CatalogApply").IsEnabled,"empty selection blocked");ApplyCatalog();UiAssert(Mask!=0,"apply guard");
        catalogMask=31;ApplyCatalog();UiAssert(Mask==31&&preferences.Services==31&&Find<Grid>("AppContent").IsEnabled,"all services saved");
        UiAssert(Find<FrameworkElement>("ProbeCard5").Visibility==Visibility.Visible,"Meet result shown");CaptureUi("-AllServices");
        ShowCatalog();Find<TextBox>("CatalogSearch").Text="meet";CaptureUi("-Meet");catalogMask=3;ApplyCatalog();
    }
}
}
