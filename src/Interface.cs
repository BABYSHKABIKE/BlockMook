using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BlockMook {
internal sealed partial class MainWindow {
    private Preferences preferences;
    private bool isSmoke;
    private int welcomeStep;
    private readonly string[] pageNames={"Home","Updates","Settings","Help"};
    private void SetupUi(bool smoke) {
        isSmoke=smoke;preferences=smoke?new Preferences():Preferences.LoadUser();
        SetupUpdates();
        youtube.IsChecked=(preferences.Services&1)!=0;discord.IsChecked=(preferences.Services&2)!=0;
        SetupCatalog();
        Find<CheckBox>("ReduceMotion").IsChecked=preferences.ReduceMotion;
        foreach(string name in pageNames){string selected=name;Find<Button>("Nav"+name).Click+=(s,e)=>Navigate(selected);}
        Find<CheckBox>("ReduceMotion").Click+=(s,e)=>{preferences.ReduceMotion=Find<CheckBox>("ReduceMotion").IsChecked==true;SavePreferences();};
        Find<Button>("ShowWelcome").Click+=(s,e)=>ShowWelcome();
        Find<Button>("WelcomeSkip").Click+=(s,e)=>DismissWelcome();
        Find<Button>("WelcomeNext").Click+=(s,e)=>{if(welcomeStep==2)DismissWelcome();else{welcomeStep++;PaintWelcome();}};
        Find<Button>("WelcomeBack").Click+=(s,e)=>{if(welcomeStep>0)welcomeStep--;PaintWelcome();};
        Find<CheckBox>("WelcomeYouTube").Click+=(s,e)=>ServicesChanged(youtube);
        Find<CheckBox>("WelcomeDiscord").Click+=(s,e)=>ServicesChanged(discord);
        Window.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape&&Find<FrameworkElement>("Welcome").Visibility==Visibility.Visible){DismissWelcome();e.Handled=true;}};
        Navigate("Home");
    }
    private void SavePreferences() {
        if(isSmoke)return;
        try {preferences.Save(Preferences.DefaultPath);}catch(Exception ex){Write("Не удалось сохранить настройки: "+ex.Message);detail.Text="Настройки не сохранились. Подробности — в журнале раздела «Помощь».";}
    }
    private void ServicesChanged(CheckBox changed) {
        if(Mask==0){changed.IsChecked=true;detail.Text="Оставь выбранным хотя бы один сервис.";}
        preferences.Services=Mask;PaintServices();ResetChecks();
        if(!manualProfile){profile=Core.LoadProfile(Mask);PaintProfile();}
        SavePreferences();
    }
    private void Navigate(string name) {
        foreach(string page in pageNames) {
            var view=Find<FrameworkElement>(page+"Page");view.BeginAnimation(UIElement.OpacityProperty,null);
            view.Visibility=page==name?Visibility.Visible:Visibility.Collapsed;
            Find<Button>("Nav"+page).Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(page==name?"#292F21":"#141517"));
            Find<Button>("Nav"+page).Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(page==name?"#D6F578":"#95989E"));
            if(page==name&&!preferences.ReduceMotion&&!isSmoke)view.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)));
        }
    }
    private void ShowWelcome() {
        welcomeStep=0;Find<FrameworkElement>("AppContent").IsEnabled=false;Find<FrameworkElement>("Welcome").Visibility=Visibility.Visible;PaintWelcome();Find<Button>("WelcomeNext").Focus();
    }
    private void PaintWelcome() {
        Find<TextBlock>("WelcomeProgress").Text="0"+(welcomeStep+1)+" / 03";
        Find<TextBlock>("WelcomeTitle").Text=new[]{"Привет, это BlockMook.","Твои сервисы. Твой выбор.","Всё готово к старту."}[welcomeStep];
        Find<TextBlock>("WelcomeText").Text=new[]{"YouTube, Discord и дополнительные сервисы из каталога. BlockMook подбирает способ подключения и проверяет соединения.","Выбери нужные сервисы. Другие сервисы, включая Google Meet, добавляются через «Добавить сервис» на главном экране.","Отключи другой VPN или zapret, затем нажми «Подключить». Windows запросит права администратора. Закрытие окна отключит BlockMook."}[welcomeStep];
        Find<FrameworkElement>("WelcomeServices").Visibility=welcomeStep==1?Visibility.Visible:Visibility.Collapsed;
        Find<Button>("WelcomeBack").Visibility=welcomeStep==0?Visibility.Hidden:Visibility.Visible;
        Find<Button>("WelcomeNext").Content=welcomeStep==2?"Начать":"Далее";
    }
    private void DismissWelcome() {
        Find<FrameworkElement>("Welcome").Visibility=Visibility.Collapsed;Find<FrameworkElement>("AppContent").IsEnabled=true;
        preferences.WelcomeDone=true;preferences.Services=Mask;SavePreferences();Navigate("Home");connect.Focus();
    }
    private void CaptureUi(string suffix) {
        Window.UpdateLayout();var surface=(FrameworkElement)Window.Content;
        var bitmap=new RenderTargetBitmap((int)surface.ActualWidth,(int)surface.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var output=File.Create(SmokePath+suffix+".png"))encoder.Save(output);
    }
    private void UiAssert(bool value,string name) {if(!value)throw new Exception("UI regression: "+name);}
    private void UiClick(string name) {Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private void RunUiSmoke() {
        foreach(string name in pageNames){UiClick("Nav"+name);UiAssert(Find<FrameworkElement>(name+"Page").Visibility==Visibility.Visible,"navigation "+name);CaptureUi("-"+name);}
        Navigate("Settings");Find<Expander>("ManualExpander").IsExpanded=true;CaptureUi("-Manual");UiClick("Profile2");UiAssert(manualProfile&&profile==2&&preferences.ManualProfile==2,"manual profile selection");UiClick("Auto");UiAssert(!manualProfile&&preferences.ManualProfile==-1,"automatic profile selection");Find<Expander>("ManualExpander").IsExpanded=false;
        UiClick("ShowWelcome");UiAssert(!Find<FrameworkElement>("AppContent").IsEnabled,"modal disables background");CaptureUi("-Welcome");
        UiClick("WelcomeNext");UiAssert(Find<FrameworkElement>("WelcomeServices").Visibility==Visibility.Visible,"service step");
        Find<CheckBox>("WelcomeDiscord").IsChecked=false;Find<CheckBox>("WelcomeDiscord").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));UiAssert(Mask==1,"onboarding service binding");
        Find<CheckBox>("WelcomeYouTube").IsChecked=false;Find<CheckBox>("WelcomeYouTube").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));UiAssert(Mask==1&&Find<CheckBox>("WelcomeYouTube").IsChecked==true,"last service retained");
        UiClick("WelcomeBack");UiAssert(welcomeStep==0,"back");UiClick("WelcomeSkip");UiAssert(Find<FrameworkElement>("Welcome").Visibility==Visibility.Collapsed&&Find<FrameworkElement>("AppContent").IsEnabled&&preferences.WelcomeDone,"skip closes and completes");
        UiClick("ShowWelcome");UiClick("WelcomeNext");UiClick("WelcomeNext");UiClick("WelcomeNext");UiAssert(Find<FrameworkElement>("Welcome").Visibility==Visibility.Collapsed,"finish closes");
        youtube.IsChecked=discord.IsChecked=true;preferences.Services=3;detail.Text="Выбери сервисы и нажми «Подключить».";
        PaintServices();TestCatalogUi();
        Window.Width=Window.MinWidth;Window.Height=Window.MinHeight;
        foreach(string name in pageNames){Navigate(name);CaptureUi("-Small-"+name);}
        ShowCatalog();CaptureUi("-Small-Catalog");CloseCatalog();
        ShowWelcome();CaptureUi("-Small-Welcome");DismissWelcome();
        File.WriteAllText(SmokePath,"PASS: catalog search, selection, cancel, empty guard and apply; four navigation pages; manual/automatic selection; onboarding next/back/skip/finish; modal background disabled; last service retained. Five views rendered at normal and minimum sizes. No engine started; no personal preferences written.");
    }
}
}
