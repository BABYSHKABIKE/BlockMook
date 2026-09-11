using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlockMook {
internal sealed class Engine : IDisposable {
    private IntPtr job;
    private Process process;
    private string hosts;
    private string error = "";
    private readonly object gate = new object();
    internal bool Alive { get { return process != null && !process.HasExited; } }
    internal int OwnedPid {get{return Alive?process.Id:0;}}
    internal async Task Start(int profile, int mask) {
        Dispose();
        var conflict = Core.Conflict();
        if (conflict != null) throw new InvalidOperationException(conflict);
        Core.VerifyEngine();
        // Elevated worker uses its own temporary host list, never accepts a path or command from IPC.
        hosts = Path.Combine(Path.GetTempPath(), "blockmook-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(hosts, Core.Domains(mask), new UTF8Encoding(false));
        job = Native.CreateKillJob();
        try {
            process = new Process { StartInfo = new ProcessStartInfo {
                FileName=Path.Combine(Core.EngineRoot,"winws.exe"), Arguments=Core.Arguments(profile,mask,hosts), WorkingDirectory=Core.EngineRoot,
                UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true
            }};
            error = "";
            process.OutputDataReceived += Capture;
            process.ErrorDataReceived += Capture;
            process.Start();
            if (!Native.AssignProcessToJobObject(job, process.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            await Task.Delay(1400);
            if (!Alive) { string detail; lock(gate) detail=error; throw new IOException("Движок завершился (" + process.ExitCode + "). " + detail); }
        } catch { Dispose(); throw; }
    }
    private void Capture(object sender, DataReceivedEventArgs e) { if (!String.IsNullOrWhiteSpace(e.Data)) lock(gate) { error = (error + " " + e.Data); if (error.Length > 1800) error=error.Substring(error.Length-1800); } }
    public void Dispose() {
        try {
            if (process != null) { try { if (!process.HasExited) { process.Kill(); process.WaitForExit(4000); } } catch (InvalidOperationException) {} finally { process.Dispose(); process=null; } }
        } finally {
            if (job != IntPtr.Zero) { Native.CloseHandle(job); job=IntPtr.Zero; }
            if (hosts != null) { try { File.Delete(hosts); } catch (IOException) {} catch (UnauthorizedAccessException) {} hosts=null; }
        }
    }
}
internal static class Native {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, IntPtr value, uint length);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint process);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Basic { public long PerProcess,PerJob; public uint Flags; public UIntPtr Min,Max; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct Io { public ulong A,B,C,D,E,F; }
    [StructLayout(LayoutKind.Sequential)] private struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob; }
    internal static IntPtr CreateKillJob() {
        IntPtr job=CreateJobObject(IntPtr.Zero,null);
        if (job==IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        var limits=new Extended(); limits.Basic.Flags=0x2000;
        int size=Marshal.SizeOf(limits); IntPtr memory=Marshal.AllocHGlobal(size);
        try { Marshal.StructureToPtr(limits,memory,false); if (!SetInformationJobObject(job,9,memory,(uint)size)) { CloseHandle(job); throw new System.ComponentModel.Win32Exception(); } } finally { Marshal.FreeHGlobal(memory); }
        return job;
    }
}
internal static class Worker {
    internal static async Task Run(string pipeName, int parentId, long parentTicks) {
        Guid token;
        if (!Guid.TryParseExact(pipeName,"N",out token)) return;
        using (var parent=Process.GetProcessById(parentId)) {
            if (parent.StartTime.ToUniversalTime().Ticks != parentTicks) return;
            if (!String.Equals(parent.MainModule.FileName, System.Reflection.Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase)) return;
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null), PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            using (var pipe=new NamedPipeServerStream("BlockMook-"+pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security))
            using (var engine=new Engine()) {
                var wait=Task.Factory.FromAsync(pipe.BeginWaitForConnection,pipe.EndWaitForConnection,null);
                if (await Task.WhenAny(wait,Task.Delay(30000)) != wait) return;
                await wait;
                uint client;
                if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out client) || client != parentId) return;
                using (var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true))
                using (var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true) { AutoFlush=true }) {
                    await writer.WriteLineAsync("READY");
                    while (!parent.HasExited) {
                        // Bounded protocol. EOF means the UI closed: stop our engine immediately.
                        string command=await ReadCommand(reader);
                        if (command==null || command=="QUIT") break;
                        string commandError=null;
                        try {
                            if (command=="STOP") { engine.Dispose(); await writer.WriteLineAsync("STOPPED"); }
                            else if (command=="STATUS") await writer.WriteLineAsync(engine.Alive ? "RUNNING" : "STOPPED");
                            else if (command=="CONFLICT") await writer.WriteLineAsync(Core.Conflict(engine.OwnedPid)??"CLEAR");
                            else {
                                var parts=command.Split('|'); int profile,mask;
                                if (parts.Length!=3 || parts[0]!="START" || !Int32.TryParse(parts[1],out profile) || !Int32.TryParse(parts[2],out mask) || profile<0 || profile>2 || !Services.ValidMask(mask)) throw new ArgumentException("Недопустимая команда");
                                await engine.Start(profile,mask); await writer.WriteLineAsync("RUNNING");
                            }
                        } catch (Exception ex) { commandError="ERROR|"+ex.Message.Replace('\r',' ').Replace('\n',' '); }
                        if(commandError!=null) await writer.WriteLineAsync(commandError);
                    }
                }
            }
        }
    }
    private static async Task<string> ReadCommand(StreamReader reader) {
        var result=new StringBuilder(); var buffer=new char[1];
        while (result.Length<=80) { if (await reader.ReadAsync(buffer,0,1)==0) return null; if(buffer[0]=='\n') return result.ToString().TrimEnd('\r'); result.Append(buffer[0]); }
        throw new IOException("Команда превышает лимит");
    }
}
internal sealed class Bridge : IDisposable {
    private NamedPipeClientStream pipe; private StreamReader reader; private StreamWriter writer;
    internal bool Connected { get { return pipe!=null && pipe.IsConnected; } }
    internal async Task Connect() {
        if (Connected) return;
        Dispose();
        string token=Guid.NewGuid().ToString("N");
        using (var current=Process.GetCurrentProcess()) {
            var info=new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--worker "+token+" "+current.Id+" "+current.StartTime.ToUniversalTime().Ticks) { UseShellExecute=true, Verb="runas", WindowStyle=ProcessWindowStyle.Hidden };
            using (var launched=Process.Start(info)) {}
        }
        pipe=new NamedPipeClientStream(".","BlockMook-"+token,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
        try {
            await Task.Run(()=>pipe.Connect(30000));
            reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
            writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true) {AutoFlush=true};
            if(await Receive()!= "READY") throw new IOException("Сетевой компонент не подтвердил запуск");
        } catch { Dispose(); throw; }
    }
    private async Task<string> Receive() {
        var read=reader.ReadLineAsync();
        if(await Task.WhenAny(read,Task.Delay(15000))!=read) { Dispose(); throw new IOException("Сетевой компонент не отвечает; соединение закрыто"); }
        string response=await read;
        if(response==null) { Dispose(); throw new IOException("Сетевой компонент завершился"); }
        return response;
    }
    internal async Task<string> Send(string command) {
        if(!Connected) throw new IOException("Сетевой компонент не подключён");
        await writer.WriteLineAsync(command);
        string response=await Receive();
        if(response.StartsWith("ERROR|")) throw new IOException(response.Substring(6));
        return response;
    }
    public void Dispose() {
        if(pipe!=null) { pipe.Dispose(); pipe=null; }
        if(reader!=null) { reader.Dispose(); reader=null; }
        if(writer!=null) { try { writer.Dispose(); } catch(IOException) {} catch(ObjectDisposedException) {} writer=null; }
    }
}
}
