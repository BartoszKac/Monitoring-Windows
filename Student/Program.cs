using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

// 1. Inicjalizacja Hostera Usługi
var builder = Host.CreateApplicationBuilder(args);

// Rejestracja jako usługa Windows
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AgentMonitorujacy";
});

// Rejestracja klasy roboczej
builder.Services.AddHostedService<MonitoringWorker>();

var host = builder.Build();
await host.RunAsync();

// 2. Definicja klasy MonitoringWorker
public class MonitoringWorker : BackgroundService
{
    private readonly string _nazwaKomputera;
    private readonly string _nazwaStudenta;
    private readonly string _serwerUrl;
    private readonly string _hubUrl;
    private readonly string _buforPath;
    private readonly HttpClient _client;
    private readonly HubConnection _connection;
    private readonly List<FileSystemWatcher> _watchers = new();
    private List<object> _buforOffline = new();

    public MonitoringWorker()
    {
        _nazwaKomputera = Environment.GetEnvironmentVariable("AGENT_PC_NAME") ?? Environment.MachineName;
        _nazwaStudenta = Environment.GetEnvironmentVariable("AGENT_STUDENT_NAME") ?? "Użytkownik Lokalny";
        string serwerUrlBase = (Environment.GetEnvironmentVariable("SERVER_URL") ?? "http://localhost:5271").TrimEnd('/');

        _serwerUrl = $"{serwerUrlBase}/api/nadzor/loguj";
        _hubUrl = $"{serwerUrlBase}/nadzorHub";
        _buforPath = Path.Combine(AppContext.BaseDirectory, $"bufor_{_nazwaKomputera}.json");

        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _client = new HttpClient(handler);

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        WczytajBufor();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connection.On<List<string>>("UstawSledzenie", (sciezki) => SynchronizujWatchery(sciezki));
        _connection.On<string>("NowyFolder", (sciezka) => UruchomMonitorowanieDlaFolderu(sciezka));
        _connection.On<string>("UsunFolder", (sciezka) => ZatrzymajMonitorowanie(sciezka));
        _connection.On<string>("PobierzDrzewo", async (sciezka) => await OdpowiedzDrzewo(sciezka));

        try
        {
            await _connection.StartAsync(stoppingToken);
            await _connection.InvokeAsync("Zarejestruj", _nazwaKomputera, _nazwaStudenta, cancellationToken: stoppingToken);
        }
        catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SprobujWyslacBufor();
            await Task.Delay(15000, stoppingToken);
        }
    }

    private void SynchronizujWatchery(List<string> sciezki)
    {
        var doUsuniecia = _watchers.Where(w => !sciezki.Contains(w.Path)).ToList();
        foreach (var w in doUsuniecia) ZatrzymajMonitorowanie(w.Path);
        foreach (var s in sciezki) UruchomMonitorowanieDlaFolderu(s);
    }

    private void UruchomMonitorowanieDlaFolderu(string sciezka)
    {
        try
        {
            if (!Directory.Exists(sciezka) || _watchers.Any(w => w.Path == sciezka)) return;
            var watcher = new FileSystemWatcher(sciezka)
            {
                Filter = "*.*",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += async (_, e) => await WyslijPlik(e.FullPath);
            watcher.Changed += async (_, e) => await WyslijPlik(e.FullPath);
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch { }
    }

    private void ZatrzymajMonitorowanie(string sciezka)
    {
        var w = _watchers.FirstOrDefault(x => x.Path == sciezka);
        if (w != null) { w.EnableRaisingEvents = false; w.Dispose(); _watchers.Remove(w); }
    }

    private async Task OdpowiedzDrzewo(string sciezka)
    {
        List<string> podfoldery = new();
        try
        {
            if (string.IsNullOrEmpty(sciezka))
                podfoldery = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToList();
            else if (Directory.Exists(sciezka))
                podfoldery = Directory.GetDirectories(sciezka).ToList();
        }
        catch { }
        await _connection.InvokeAsync("OdpowiedzDrzewo", _nazwaKomputera, sciezka, podfoldery);
    }

    private async Task WyslijPlik(string sciezka)
    {
        try
        {
            await Task.Delay(500);
            if (!File.Exists(sciezka)) return;
            string tresc = await File.ReadAllTextAsync(sciezka);
            var dane = new { NazwaStudenta = _nazwaStudenta, NazwaKomputera = _nazwaKomputera, NazwaPliku = Path.GetFileName(sciezka), Tresc = tresc, Hash = ObliczHash(tresc), DataLogowania = DateTime.Now };
            if (!await ProbujWyslac(dane)) { _buforOffline.Add(dane); ZapiszBufor(); }
        }
        catch { }
    }

    private async Task<bool> ProbujWyslac(object dane)
    {
        try { var resp = await _client.PostAsJsonAsync(_serwerUrl, dane); return resp.IsSuccessStatusCode; }
        catch { return false; }
    }

    private async Task SprobujWyslacBufor()
    {
        if (!_buforOffline.Any()) return;
        var kopia = _buforOffline.ToList();
        _buforOffline.Clear();
        foreach (var d in kopia) if (!await ProbujWyslac(d)) _buforOffline.Add(d);
        ZapiszBufor();
    }

    private void WczytajBufor() { try { if (File.Exists(_buforPath)) _buforOffline = JsonSerializer.Deserialize<List<object>>(File.ReadAllText(_buforPath)) ?? new(); } catch { } }
    private void ZapiszBufor() { try { File.WriteAllText(_buforPath, JsonSerializer.Serialize(_buforOffline)); } catch { } }
    private string ObliczHash(string input) { using SHA256 sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))); }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var w in _watchers) w.Dispose();
        await _connection.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}