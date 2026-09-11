using System;
using System.Linq;

namespace BlockMook {
internal sealed class ServiceDefinition {
    internal int Bit,Index;
    internal string Name,Category,Support,Description;
    internal string[] Domains,TlsHosts;
}
internal static class Services {
    internal const int AllMask=31;
    internal const string MeetMedia="74.125.250.0/24,74.125.247.128/32,142.250.82.0/24,2001:4860:4864:5::/64,2001:4860:4864:4:8000::/128,2001:4860:4864:6::/64";
    internal static readonly ServiceDefinition[] Items={
        new ServiceDefinition {Bit=1,Index=0,Name="YouTube",Category="Видео",Support="Базовый профиль",Description="Сайт и видео. Проверка подтверждает ответ сервиса; воспроизведение проверь в браузере.",Domains=new[]{"youtube.com","youtu.be","googlevideo.com","ytimg.com","youtubei.googleapis.com","youtube.googleapis.com","youtube-nocookie.com","yt3.ggpht.com","yt4.ggpht.com","yt3.googleusercontent.com","youtubeembeddedplayer.googleapis.com"}},
        new ServiceDefinition {Bit=2,Index=1,Name="Discord",Category="Общение",Support="Базовый профиль",Description="Приложение и голос. Проверяем API, запуск клиента и WebSocket. Голосовой звонок — вручную.",Domains=new[]{"discord.com","discord.gg","discordapp.com","discordapp.net","discord.media","discordstatus.com"}},
        new ServiceDefinition {Bit=16,Index=5,Name="Google Meet",Category="Видеовстречи · мит",Support="Экспериментально",Description="Сайт, вход и медиасерверы Google. Проверяем TLS и отдельно UDP/STUN. Видео, звук и вход в аккаунт проверь во встрече.",Domains=new[]{"meet.google.com","accounts.google.com","apis.google.com","meetings.googleapis.com","hangouts.googleapis.com","apps.google.com","docs.google.com","clients2.google.com","clients4.google.com","clients6.google.com","www.gstatic.com","fonts.gstatic.com","lh3.googleusercontent.com","workspace.turns.goog","meet.turns.goog"},TlsHosts=new[]{"meet.google.com","accounts.google.com","meet.turns.goog"}},
        new ServiceDefinition {Bit=4,Index=3,Name="Signal",Category="Мессенджеры",Support="Экспериментально",Description="Домены Signal и ссылки. Проверяем TLS сервера сообщений и хранилища. Доставку сообщений и звонки — в приложении; отдельной UDP-стратегии нет.",Domains=new[]{"signal.org","signal.art","signal.group","signal.link","signal.me","signal.tube","signalcaptchas.org"},TlsHosts=new[]{"chat.signal.org","storage.signal.org"}},
        new ServiceDefinition {Bit=8,Index=4,Name="Viber",Category="Мессенджеры",Support="Частично · сайт",Description="Профиль доменов viber.com. Автопроверка подтверждает только TLS сайта. Работа клиента и звонков не подтверждена; отдельные порты клиента пока не обрабатываются.",Domains=new[]{"viber.com"},TlsHosts=new[]{"www.viber.com"}}
    };
    internal static bool ValidMask(int mask){return mask>0&&(mask&~AllMask)==0;}
    internal static ServiceDefinition[] Selected(int mask){if(!ValidMask(mask))throw new ArgumentException("Выбери хотя бы один сервис");return Items.Where(s=>(s.Bit&mask)!=0).ToArray();}
    internal static int[] ProbeIndices(int mask){return Selected(mask).Select(s=>s.Index).Concat(new[]{2}).OrderBy(i=>i).ToArray();}
    internal static string ProbeName(int index){return index==2?"Интернет":Items.Single(s=>s.Index==index).Name;}
    internal static ServiceDefinition[] Search(string query){string term=(query??"").Trim();return Items.Where(s=>(s.Name+" "+s.Category).IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0).ToArray();}
}
}
