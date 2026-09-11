using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace BlockMook {
internal sealed partial class MainWindow {
    private NetworkOptions networkOptions;
    private NetworkMonitor networkMonitor=new NetworkMonitor();
    private int networkGeneration;
    private readonly Queue<NetworkFrame> networkFrames=new Queue<NetworkFrame>();
    private readonly SessionHistory sessionHistory=new SessionHistory();
    private readonly DispatcherTimer networkTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
    private NetworkFrame networkFrame=new NetworkFrame();
    private NetworkView networkPreview;
    private TextBlock networkStatus,settingsStatus,historyText;
    private bool networkBusy,networkPageVisible,networkDisposed,historyRunning;
    private void SetupNetwork(){
        networkOptions=isSmoke?new NetworkOptions():NetworkOptions.Load();
        var page=Find<ScrollViewer>("NetworkPage");var body=new StackPanel{Margin=new Thickness(0,0,8,0)};page.Content=body;
        body.Children.Add(Label("Сеть",30));body.Children.Add(Label("Скорость передачи данных и задержка соединения",13));
        var display=new StackPanel();networkPreview=new NetworkView();display.Children.Add(new Viewbox{Child=networkPreview,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left});networkStatus=Label("Ожидание измерения",12);display.Children.Add(networkStatus);
        body.Children.Add(Panel(display));
        settingsStatus=Label("Настройки измерений применяются сразу.",12);body.Children.Add(settingsStatus);
        var units=Choice("Скорость · единицы выбираются автоматически",new[]{"Биты · Кбит/с, Мбит/с, Гбит/с","Байты · КБ/с, МБ/с, ГБ/с"},networkOptions.Megabytes?"Байты · КБ/с, МБ/с, ГБ/с":"Биты · Кбит/с, Мбит/с, Гбит/с",v=>{networkOptions.Megabytes=v.StartsWith("Байты",StringComparison.Ordinal);SaveNetwork();PaintNetwork();});body.Children.Add(units);
        var measure=new StackPanel();measure.Children.Add(Label("Параметры измерений",18));
        measure.Children.Add(Label("Скорость — текущий трафик одного адаптера, а не предел тарифа. Автовыбор предпочитает активный Ethernet или Wi-Fi с шлюзом. При нескольких подключениях выберите нужное вручную.",12));
        var adapterCombo=new ComboBox{Margin=new Thickness(0,8,0,8),MinHeight=32};var adapters=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>("Автоматически · Ethernet / Wi-Fi","")};
        if(!isSmoke){try{adapters.AddRange(NetworkMonitor.Adapters().Select(x=>new KeyValuePair<string,string>(x.Name,x.Id)));}catch(System.Net.NetworkInformation.NetworkInformationException){networkStatus.Text="Не удалось прочитать адаптеры.";}}
        if(networkOptions.Adapter!=""&&!adapters.Any(x=>x.Value==networkOptions.Adapter))adapters.Add(new KeyValuePair<string,string>("Сохранённый адаптер · недоступен",networkOptions.Adapter));
        adapterCombo.ItemsSource=adapters.Select(x=>x.Key).ToArray();adapterCombo.SelectedIndex=adapters.FindIndex(x=>x.Value==networkOptions.Adapter);adapterCombo.SelectionChanged+=(s,e)=>{networkOptions.Adapter=adapterCombo.SelectedIndex>=0?adapters[adapterCombo.SelectedIndex].Value:"";ResetNetworkSamples();SaveNetwork();};measure.Children.Add(adapterCombo);
        measure.Children.Add(Label("Адрес пинга (IPv4 / IPv6)",14));var target=Input("PingTarget",networkOptions.Target);measure.Children.Add(target);
        measure.Children.Add(ActionButton("Применить адрес",()=>{if(!NetworkMonitor.ValidTarget(target.Text.Trim())){settingsStatus.Text="Введите IP-адрес, например 1.1.1.1. URL и имена сайтов не подходят.";return;}networkOptions.Target=target.Text.Trim();ResetNetworkSamples();SaveNetwork();PaintNetwork();}));
        measure.Children.Add(Label("Одна ICMP-проба в секунду к указанному адресу; по умолчанию 1.1.1.1 (Cloudflare). Пинг следует маршруту Windows, включая активный VPN. Это не обязательно задержка игрового сервера. Тайм-аут — 900 мс. Потери ICMP не доказывают потери игрового трафика. Колебания — средняя разница соседних успешных ответов.",12));
        measure.Children.Add(Label("Измерения идут только пока этот раздел открыт в видимом окне. При сворачивании или уходе в другой раздел измерения останавливаются.",12));body.Children.Add(Panel(new Expander{Header="Адрес пинга и сетевой адаптер",Content=measure}));
        var history=new StackPanel();history.Children.Add(Label("Последние 100 событий текущего сеанса. В памяти, без файлов и фоновой загрузки.",12));historyText=Label("Событий пока нет.",13);history.Children.Add(historyText);history.Children.Add(ActionButton("Очистить историю",()=>{sessionHistory.Clear();historyText.Text="Событий пока нет.";}));body.Children.Add(Panel(new Expander{Header="События подключения",Content=history}));
        networkTimer.Tick+=async(s,e)=>{if(networkBusy||networkDisposed||!NetworkVisible)return;networkBusy=true;int generation=networkGeneration;string adapter=networkOptions.Adapter,targetAddress=networkOptions.Target;try{var frame=await networkMonitor.Read(adapter,targetAddress);if(networkDisposed||!NetworkVisible||generation!=networkGeneration||adapter!=networkOptions.Adapter||targetAddress!=networkOptions.Target)return;networkFrame=frame;networkFrames.Enqueue(frame);while(networkFrames.Count>60||networkFrames.Count>0&&networkFrames.Peek().Time<frame.Time.AddSeconds(-60))networkFrames.Dequeue();PaintNetwork();}catch(Exception ex){networkStatus.Text="Не удалось измерить сеть: "+ex.Message;}finally{networkBusy=false;}};
        Window.IsVisibleChanged+=(s,e)=>UpdateNetworkTimer();Window.StateChanged+=(s,e)=>UpdateNetworkTimer();
        PaintNetwork();
    }
    private void ResetNetworkSamples(){networkGeneration++;networkMonitor=new NetworkMonitor();networkFrames.Clear();networkFrame=new NetworkFrame{Target=networkOptions.Target};PaintNetwork();}
    private TextBlock Label(string text,double size){return new TextBlock{Text=text,FontSize=size,Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(size>=18?"#F2F5F4":"#A9B5B4")),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,8)};}
    private Border Panel(UIElement content){return new Border{Style=(Style)Window.FindResource("Panel"),Child=content,Margin=new Thickness(0,14,0,0)};}
    private Button ActionButton(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(0,6,8,6),HorizontalAlignment=HorizontalAlignment.Left};b.Click+=(s,e)=>action();return b;}
    private TextBox Input(string id,string value){var t=new TextBox{Name=id,Text=value,MaxLength=64,Background=new SolidColorBrush(Color.FromRgb(12,18,19)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(40,75,72)),Padding=new Thickness(10),Margin=new Thickness(0,6,0,6)};Window.RegisterName(id,t);return t;}
    private FrameworkElement Choice(string label,string[] values,string current,Action<string> changed){var stack=new StackPanel();stack.Children.Add(Label(label,13));var c=new ComboBox{ItemsSource=values,SelectedItem=current,MinHeight=32,Margin=new Thickness(0,0,0,8)};c.SelectionChanged+=(s,e)=>{if(c.SelectedItem!=null)changed((string)c.SelectedItem);};stack.Children.Add(c);return stack;}
    private void PaintNetwork(){if(networkPreview==null)return;networkPreview.Paint(networkFrame,networkOptions.Megabytes,networkFrames);networkStatus.Text=networkFrame.Status+" · "+networkFrame.Replies+" ответов / "+networkFrame.Sent+" проб";}
    private void SaveNetwork(){if(isSmoke)return;try{networkOptions.Save();}catch(Exception ex){settingsStatus.Text="Настройки не сохранились: "+ex.Message;}}
    private bool NetworkVisible {get{return networkPageVisible&&Window.IsVisible&&Window.WindowState!=WindowState.Minimized;}}
    private void UpdateNetworkTimer(){if(networkDisposed||networkOptions==null)return;if(NetworkVisible&&!isSmoke)networkTimer.Start();else{networkTimer.Stop();ResetNetworkSamples();}}
    private void RecordNetworkEvent(string message){sessionHistory.Add(message);if(historyText!=null)historyText.Text=sessionHistory.Text;}
    private void ObserveConnectionState(){if(historyRunning==running)return;historyRunning=running;RecordNetworkEvent(running?"Движок подключения запущен":recovery.Wanted?"Соединение прервано или перезапускается":"Подключение остановлено");}
    private void DisposeNetwork(){if(networkDisposed)return;networkDisposed=true;networkGeneration++;networkTimer.Stop();}
}
}
