using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace BlockMook {
internal sealed class NetworkView : Border {
    private readonly StackPanel stack=new StackPanel();
    internal NetworkView(){Padding=new Thickness(16,12,16,12);CornerRadius=new CornerRadius(12);BorderThickness=new Thickness(1);Child=stack;}
    internal void Paint(NetworkFrame frame,bool megabytes,IEnumerable<NetworkFrame> history){
        Background=new SolidColorBrush(Color.FromRgb(8,12,13));BorderBrush=new SolidColorBrush(Color.FromRgb(30,65,62));
        stack.Children.Clear();
        Add("ТЕКУЩИЙ ТРАФИК",11,"#A9B5B4","Segoe UI");
        Add("↓  "+NetworkMonitor.Speed(frame.Down,megabytes),20,"#00E8D2","Segoe UI");
        Add("↑  "+NetworkMonitor.Speed(frame.Up,megabytes),20,"#00E8D2","Segoe UI");
        Add("Пинг  "+(frame.Ping.HasValue?(frame.Ping.Value<1?"< 1":frame.Ping.Value.ToString("0"))+" мс":frame.Sent==0?"—":"Нет ответа"),20,"#00E8D2","Segoe UI");
        Add("Потери  "+Number(frame.Loss," %")+" · "+frame.Sent+" проб",20,"#00E8D2","Segoe UI");
        Add("Колебания  "+Number(frame.Jitter," мс"),20,"#00E8D2","Segoe UI");
        Add("ICMP → "+frame.Target+" · последние 60 с",10,"#A9B5B4","Segoe UI");
        Add(frame.Adapter,10,"#A9B5B4","Segoe UI");
        {
            var data=history.ToArray();var canvas=new Canvas{Width=260,Height=48,Margin=new Thickness(0,8,0,0)};
            Draw(canvas,data,x=>x.Down,"#00E8D2");Draw(canvas,data,x=>x.Up,"#F1F5F4");stack.Children.Add(canvas);Add("60 с · ↓ цвет текста / ↑ белый",10,"#A9B5B4","Segoe UI");
        }
    }
    private void Draw(Canvas canvas,NetworkFrame[] frames,Func<NetworkFrame,double?> value,string color){
        if(frames.Length<2)return;double max=Math.Max(1,frames.Max(x=>Math.Max(x.Down??0,x.Up??0)));Polyline line=null;
        DateTime end=frames[frames.Length-1].Time;
        foreach(var frame in frames){var n=value(frame);if(!n.HasValue){line=null;continue;}
            if(line==null){line=new Polyline{Stroke=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),StrokeThickness=1.4};canvas.Children.Add(line);}
            line.Points.Add(new Point(Math.Max(0,260-(end-frame.Time).TotalSeconds/60*260),46-n.Value/max*44));
        }
    }
    private static string Number(double? value,string suffix){return value.HasValue?value.Value.ToString("0.0",System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))+suffix:"—";}
    private void Add(string text,double size,string color,string font){stack.Children.Add(new TextBlock{Text=text,FontFamily=new FontFamily(font),FontSize=size,Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),Margin=new Thickness(0,2,0,2),TextWrapping=TextWrapping.NoWrap});}
}
}
