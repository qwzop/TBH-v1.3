using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trainer.Core;

namespace Trainer.Desktop;

static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new Studio()); }
}

sealed class Studio : Form
{
    readonly TextBox log = new() { Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Dock=DockStyle.Fill };
    readonly object sync = new();
    readonly List<string> events = [];
    string state="Ready", profile="None", moduleStatus="Not verified";
    HttpListener? listener;
    string? dashboard;
    readonly CancellationTokenSource stopped = new();
    public Studio()
    {
        Text="Trainer Studio — 0.1.0"; Width=940; Height=610; MinimumSize=new(780,480);
        var controls=new FlowLayoutPanel { Dock=DockStyle.Top, Height=100, Padding=new(8), AutoSize=false };
        void Button(string label,Action action) { var button=new Button { Text=label, AutoSize=true, Height=34 }; button.Click+=(_,_)=>{try{action();}catch(Exception e){Add("Error: "+e.Message);MessageBox.Show(this,e.Message,"Trainer Studio");}};controls.Controls.Add(button); }
        Button("Open Dashboard",OpenDashboard);Button("Verify Game Profile",VerifyProfile);Button("Run Demo",Demo);Button("Export Diagnostics",Export);Button("Install Update",InstallUpdate);
        controls.Controls.Add(new Label { Text="Prototype: local dashboard, profile verification, simulated trainer demo. Live game attachment is not implemented.", AutoSize=true, Padding=new(0,8,0,0) });
        Controls.Add(log);Controls.Add(controls);
        Shown+=(_,_)=>{try{StartDashboard();}catch(Exception e){Add("Dashboard unavailable: "+e.Message);}Add("Ready. No game is attached.");};
        FormClosed+=(_,_)=>{stopped.Cancel();listener?.Close();stopped.Dispose();};
    }
    void Add(string message)
    {
        var line=$"{DateTimeOffset.Now:HH:mm:ss} {message}";
        lock(sync){events.Add(line);if(events.Count>200)events.RemoveAt(0);}
        log.AppendText(line+Environment.NewLine);
    }
    string Snapshot()
    {
        lock(sync)return JsonSerializer.Serialize(new { app="Trainer Studio", version="0.1.0", state, profile, moduleStatus, gameAttached=false, mode="simulation / read-only verification", utc=DateTimeOffset.UtcNow, events=events.ToArray() });
    }
    void StartDashboard()
    {
        // Loopback only; an unguessable path scopes this local viewing session.
        for(int attempt=0;attempt<8;attempt++)
        {
            var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();int port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
            var candidate=new HttpListener();candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
            try {candidate.Start();listener=candidate;dashboard=$"http://127.0.0.1:{port}/{Guid.NewGuid():N}/";break;}
            catch(HttpListenerException){candidate.Close();}
        }
        if(listener is null)throw new IOException("Could not start loopback dashboard.");
        string path=new Uri(dashboard!).AbsolutePath;
        var server=listener;
        _=Task.Run(async()=>{
            while(!stopped.IsCancellationRequested)
            {
                HttpListenerContext context;
                try{context=await server.GetContextAsync();}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}
                try
                {
                    var response=context.Response;
                    response.Headers["Cache-Control"]="no-store";response.Headers["X-Content-Type-Options"]="nosniff";
                    response.Headers["Content-Security-Policy"]="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'";
                    string resource=context.Request.Url?.AbsolutePath??"";
                    if(context.Request.HttpMethod!="GET" || (resource!=path && resource!=path+"status")) {response.StatusCode=404;response.Close();continue;}
                    bool json=resource.EndsWith("/status",StringComparison.Ordinal);
                    var body=Encoding.UTF8.GetBytes(json?Snapshot():DashboardHtml);
                    response.ContentType=json?"application/json; charset=utf-8":"text/html; charset=utf-8";
                    response.ContentLength64=body.Length;await response.OutputStream.WriteAsync(body);response.Close();
                }
                catch(Exception e) when(e is IOException or HttpListenerException or ObjectDisposedException){try{context.Response.Abort();}catch{}}
            }
        });
    }
    void OpenDashboard()
    {
        if(dashboard is null)throw new InvalidOperationException("Dashboard could not start; diagnostics are still available in the app.");
        Process.Start(new ProcessStartInfo(dashboard){UseShellExecute=true});
    }
    void VerifyProfile()
    {
        using var choice=new OpenFileDialog { Filter="Game profiles (*.json)|*.json",Title="Select a game profile" };
        if(choice.ShowDialog(this)!=DialogResult.OK)return;
        var selected=GameProfile.Load(choice.FileName);
        using var module=new OpenFileDialog { Filter="All files (*.*)|*.*", Title="Select the module named by the profile" };
        if(module.ShowDialog(this)!=DialogResult.OK)return;
        bool matched=selected.MatchesFile(module.FileName);
        lock(sync){profile=selected.Game+" / "+selected.Build;moduleStatus=matched?"Exact module hash matched":"Unknown build — refused";}
        Add(moduleStatus+". No game file was modified.");
    }
    void Demo()
    {
        byte[] bytes=[10,0,0,0];string hash=Convert.ToHexString(SHA256.HashData(bytes));
        var p=new GameProfile(1,"demo","Simulated game","1","buffer",hash,[new("health","Demo health",0,"0A000000","64000000")]);
        var memory=new BufferTarget(bytes);var session=new PatchSession(p,hash,memory);
        lock(sync)state="Running demo";
        try{session.Enable("health");Add($"Simulated health: 10 → {BitConverter.ToInt32(memory.Read(0,4))}");}
        finally{session.RestoreAll();lock(sync)state="Ready";}
        Add($"Restored simulated health: {BitConverter.ToInt32(memory.Read(0,4))}. No game attached.");
    }
    void Export()
    {
        using var save=new SaveFileDialog { Filter="Diagnostics (*.json)|*.json", FileName=$"trainer-diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.json" };
        if(save.ShowDialog(this)!=DialogResult.OK)return;
        File.WriteAllText(save.FileName,Snapshot());Add("Diagnostics exported to your selected file.");
    }
    void InstallUpdate()
    {
        using var zip=new OpenFileDialog { Filter="Signed updates (*.zip)|*.zip" };
        if(zip.ShowDialog(this)!=DialogResult.OK)return;
        using var folder=new FolderBrowserDialog { Description="Choose where to install the new version", UseDescriptionForTitle=true };
        if(folder.ShowDialog(this)!=DialogResult.OK)return;
        using var keyStream=typeof(Studio).Assembly.GetManifestResourceStream("Trainer.Desktop.release-public.pem")??throw new IOException("Update verification key missing.");
        using var reader=new StreamReader(keyStream);
        var installed=UpdatePackage.Install(zip.FileName,folder.SelectedPath,reader.ReadToEnd(),new Version(0,1,0));
        Add("Verified update installed: "+installed);
        MessageBox.Show(this,"Update installed alongside this version. Open its folder and launch Trainer Studio.exe. Your previous version was preserved.","Update ready");
        Process.Start(new ProcessStartInfo(folder.SelectedPath){UseShellExecute=true});
    }
    const string DashboardHtml="""
<!doctype html><html><head><meta charset="utf-8"><title>Trainer Studio</title><style>
body{background:#121820;color:#e7edf6;font:16px system-ui;margin:3rem auto;max-width:980px;padding:0 24px}h1{color:#7fe3c3}#cards{display:flex;gap:16px;flex-wrap:wrap}.card,pre{background:#202b39;border-radius:12px;padding:20px}.card{flex:1}small{color:#aebdd0}pre{white-space:pre-wrap;max-height:420px;overflow:auto}#connection{color:#7fe3c3}
</style></head><body><h1>Trainer Studio</h1><p id="connection">Connecting…</p><div id="cards"><div class="card">App state<h2 id="state">—</h2></div><div class="card">Profile<h2 id="profile">—</h2><small id="module"></small></div><div class="card">Game connection<h2>Not attached</h2><small>Simulation and read-only verification</small></div></div><h2>Recent diagnostics</h2><pre id="log"></pre><small>This dashboard is local to your computer. No remote control or public sharing is enabled.</small><script>
async function refresh(){try{const r=await fetch('status',{cache:'no-store'});if(!r.ok)throw Error();const s=await r.json();document.getElementById('connection').textContent='Live · v'+s.version+' · '+new Date(s.utc).toLocaleTimeString();document.getElementById('state').textContent=s.state;document.getElementById('profile').textContent=s.profile;document.getElementById('module').textContent=s.moduleStatus;document.getElementById('log').textContent=s.events.join('\n');}catch{document.getElementById('connection').textContent='Disconnected — open Trainer Studio to reconnect.'}}refresh();setInterval(refresh,1000);
</script></body></html>
""";
}
