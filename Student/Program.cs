using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR.Client;

// ─── 1. KONFIGURACJA HOSTA ───────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AgentMonitorujacy";
});

builder.Services.AddHostedService<MonitoringWorker>();

var host = builder.Build();
await host.RunAsync();

// ─── 2. GŁÓWNA LOGIKA AGENTA ──────────────────────────────────────────────────

public class MonitoringWorker : BackgroundService
{
    private readonly string _nazwaKomputera;
    private readonly string _nazwaStudenta;
    private readonly string _logujUrl;
    private readonly string _konfiguracjaUrl;
    private readonly string _heartbeatUrl;
    private readonly string _buforPath;
    private readonly HttpClient _client;

    private HubConnection? _hubConnection;
    private readonly string _serwerBase;

    // Blokada duplikatów zdarzeń (debouncing)
    private string _ostatniPlik = "";
    private DateTime _ostatniCzas = DateTime.MinValue;

    private readonly Dictionary<string, FileSystemWatcher> _watchers
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ZdarzenieDane> _buforOffline = new();
    private readonly SemaphoreSlim _buforLock = new(1, 1);

    // Śledź aktualnie monitorowane ścieżki
    private List<string> _aktualneSciezki = new();

    public MonitoringWorker()
    {
        _nazwaKomputera = Environment.GetEnvironmentVariable("AGENT_PC_NAME")
                          ?? Environment.MachineName;
        _nazwaStudenta = Environment.GetEnvironmentVariable("AGENT_STUDENT_NAME")
                          ?? "Użytkownik Lokalny";

        _serwerBase = (Environment.GetEnvironmentVariable("SERVER_URL")
                          ?? "http://localhost:5271").TrimEnd('/');

        _logujUrl = $"{_serwerBase}/api/nadzor/loguj";
        _konfiguracjaUrl = $"{_serwerBase}/api/nadzor/konfiguracja?komputer={_nazwaKomputera}";
        _heartbeatUrl = $"{_serwerBase}/api/nadzor/heartbeat";
        _buforPath = Path.Combine(AppContext.BaseDirectory, $"bufor_{_nazwaKomputera}.json");

        _client = new HttpClient(
            new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            })
        { Timeout = TimeSpan.FromSeconds(10) };

        WczytajBufor();
    }

    // ─── Budowanie połączenia SignalR (wywoływane przy każdym reconnect) ───────

    private HubConnection BudujPolaczenie()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl($"{_serwerBase}/nadzorHub")
            .WithAutomaticReconnect(new[] { 2, 5, 10, 30 }.Select(s => TimeSpan.FromSeconds(s)).ToArray())
            .Build();

        // Po automatycznym reconnect — ponownie zarejestruj agenta na Hubie
        hub.Reconnected += async (connectionId) =>
        {
            Console.WriteLine($"[Agent] Reconnected (connId={connectionId}). Ponowna rejestracja...");
            await ZarejestrujNaHubie(hub);
        };

        hub.Closed += async (ex) =>
        {
            Console.WriteLine($"[Agent] Połączenie zamknięte: {ex?.Message}. Ponawiam za 10s...");
            await Task.Delay(10_000);
            await PolaczIZarejestruj(hub);
        };

        // Serwer wysyła nową listę folderów — natychmiast aktualizujemy watchery
        // Serwer wysyła nową listę folderów — natychmiast aktualizujemy watchery
        hub.On<List<string>>("UstawSledzenie", (sciezki) =>
        {
            Console.WriteLine($"[Agent] UstawSledzenie: {string.Join(", ", sciezki)}"); // Tu już masz logowanie!
            _aktualneSciezki = sciezki;
            SynchronizujWatchery(sciezki);
        });

        // Serwer pyta o drzewo podfolderów (dla UI admina)
        // Serwer prosi o listę folderów, żeby wyświetlić je w panelu (to naprawi pustą listę)
        hub.On<string>("PobierzDrzewo", async (sciezka) =>
        {
            Console.WriteLine($"[Agent] <<< PobierzDrzewo odebrano, sciezka='{sciezka}'");
            try
            {
                var folderRoot = string.IsNullOrEmpty(sciezka) ? "/" : sciezka;

                if (Directory.Exists(folderRoot))
                {
                    var podfoldery = Directory.GetDirectories(folderRoot)
                                              .Select(Path.GetFullPath)
                                              .ToList();

                    Console.WriteLine($"[Agent] Odpowiadam drzewem dla: {folderRoot} ({podfoldery.Count} folderów)");

                    await hub.InvokeAsync("OdpowiedzDrzewo", _nazwaKomputera, sciezka, podfoldery);
                }
                else
                {
                    Console.WriteLine($"[Agent] PobierzDrzewo: Folder {folderRoot} nie istnieje.");
                    await hub.InvokeAsync("OdpowiedzDrzewo", _nazwaKomputera, sciezka, new List<string>());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agent] Błąd skanowania drzewa: {ex.Message}");
            }
        });

        return hub;
    }

    private async Task PolaczIZarejestruj(HubConnection hub)
    {
        while (true)
        {
            try
            {
                if (hub.State == HubConnectionState.Disconnected)
                    await hub.StartAsync();

                await ZarejestrujNaHubie(hub);
                Console.WriteLine("[Agent] Połączono z SignalR.");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agent] SignalR niedostępny: {ex.Message}. Retry za 15s...");
                await Task.Delay(15_000);
            }
        }
    }

    private async Task ZarejestrujNaHubie(HubConnection hub)
    {
        await hub.InvokeAsync("Zarejestruj", _nazwaKomputera, _nazwaStudenta);
    }

    // ─── GŁÓWNA PĘTLA ─────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"[Agent] Start: {_nazwaKomputera} jako {_nazwaStudenta}");

        // Buduj i połącz SignalR w tle (nie blokuj startu)
        _hubConnection = BudujPolaczenie();
        _ = Task.Run(() => PolaczIZarejestruj(_hubConnection), stoppingToken);

        // Pierwsza konfiguracja przez HTTP (zanim SignalR się połączy)
        await PobierzKonfiguracje();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(15_000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Co 15 sekund: wyślij bufor offline + heartbeat + (opcjonalnie) sprawdź konfigurację
            await SprobujWyslacBufor();
            await WyslijHeartbeat();

            // Pobierz konfigurację przez HTTP jako backup (na wypadek że SignalR nie działa)
            // Synchronizujemy tylko jeśli lista się zmieniła
            await PobierzKonfiguracje();
        }
    }

    // ─── KONFIGURACJA PRZEZ HTTP (backup dla SignalR) ─────────────────────────

    private async Task PobierzKonfiguracje()
    {
        try
        {
            var sciezki = await _client.GetFromJsonAsync<List<string>>(_konfiguracjaUrl);
            if (sciezki == null) return;

            // Aktualizuj tylko jeśli lista się zmieniła
            bool zmiana = sciezki.Count != _aktualneSciezki.Count
                || sciezki.Except(_aktualneSciezki, StringComparer.OrdinalIgnoreCase).Any();

            if (zmiana)
            {
                Console.WriteLine($"[Agent] HTTP konfiguracja zaktualizowana: {string.Join(", ", sciezki)}");
                _aktualneSciezki = sciezki;
                SynchronizujWatchery(sciezki);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent] HTTP konfiguracja niedostępna: {ex.Message}");
        }
    }

    // ─── HEARTBEAT — serwer wie że agent żyje ─────────────────────────────────

    private async Task WyslijHeartbeat()
    {
        try
        {
            await _client.PostAsJsonAsync(_heartbeatUrl, new
            {
                nazwaKomputera = _nazwaKomputera,
                nazwaStudenta = _nazwaStudenta,
                czas = DateTime.Now
            });
        }
        catch
        {
            // Serwer offline — ignorujemy, bufor się zajmie resztą
        }
    }

    // ─── WATCHERY ─────────────────────────────────────────────────────────────

    // ─── ZARZĄDZANIE ŚLEDZENIEM (LOGIKA ZMIAN) ─────────────────────────────────

    private void SynchronizujWatchery(List<string> sciezki)
    {
        var aktualneKlucze = _watchers.Keys.ToList();

        // LOG: Informacja o rozpoczęciu synchronizacji
        Console.WriteLine($"\n[Agent] === SYNCHRONIZACJA FOLDERÓW ({DateTime.Now:HH:mm:ss}) ===");
        Console.WriteLine($"[Agent] Serwer przysłał ścieżki: {(sciezki.Any() ? string.Join(", ", sciezki) : "BRAK")}");

        // 1. Usuń watchery, których już nie ma w nowej konfiguracji
        foreach (var k in aktualneKlucze.Where(k => !sciezki.Contains(k, StringComparer.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"[Agent] Usuwanie śledzenia (ścieżka wycofana): {k}");
            ZatrzymajWatcher(k);
        }

        // 2. Dodaj nowe watchery
        foreach (var s in sciezki)
        {
            if (!_watchers.ContainsKey(s))
            {
                Console.WriteLine($"[Agent] Wykryto nową ścieżkę do śledzenia: {s}");
                UruchomWatcher(s);
            }
        }

        Console.WriteLine($"[Agent] Aktualnie śledzonych folderów: {_watchers.Count}");
        Console.WriteLine("[Agent] =================================================\n");
    }

    private void UruchomWatcher(string sciezka)
    {
        // Jeśli już śledzimy, pomiń
        if (_watchers.ContainsKey(sciezka)) return;

        // Sprawdzenie fizycznego istnienia
        if (!Directory.Exists(sciezka))
        {
            Console.WriteLine($"[Agent] !!! BŁĄD !!! Folder nie istnieje na dysku tego komputera: {sciezka}");
            return;
        }

        try
        {
            var w = new FileSystemWatcher(sciezka)
            {
                Filter = "*.*",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            // Podpięcie zdarzeń
            w.Created += async (_, e) =>
            {
                Console.WriteLine($"[Agent] [NOWY PLIK] Wykryto: {e.Name}");
                await WyslijPlik(e.FullPath);
            };

            w.Changed += async (_, e) =>
            {
                Console.WriteLine($"[Agent] [ZMIANA] W pliku: {e.Name}");
                await WyslijPlik(e.FullPath);
            };

            w.Error += (s, e) =>
            {
                Console.WriteLine($"[Agent] [BŁĄD WATCHERA] {sciezka}: {e.GetException().Message}");
            };

            w.EnableRaisingEvents = true;
            _watchers[sciezka] = w;

            Console.WriteLine($"[Agent] >>> SUKCES: Rozpoczęto monitorowanie: {sciezka}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent] !!! KRYTYCZNY BŁĄD !!! Nie udało się odpalić watchera dla {sciezka}: {ex.Message}");
        }
    }

    private void ZatrzymajWatcher(string sciezka)
    {
        if (_watchers.TryGetValue(sciezka, out var w))
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
            _watchers.Remove(sciezka);
            Console.WriteLine($"[Agent] Przestałem śledzić: {sciezka}");
        }
    }

    // ─── WYSYŁANIE PLIKU ──────────────────────────────────────────────────────

    private async Task WyslijPlik(string sciezka)
    {
        try
        {
            // Debouncing
            if (_ostatniPlik == sciezka && (DateTime.Now - _ostatniCzas).TotalMilliseconds < 800)
                return;
            _ostatniPlik = sciezka;
            _ostatniCzas = DateTime.Now;

            await Task.Delay(600); // Czekaj na zwolnienie pliku przez system
            if (!File.Exists(sciezka)) return;

            string tresc = await File.ReadAllTextAsync(sciezka);
            var dane = new ZdarzenieDane
            {
                NazwaStudenta = _nazwaStudenta,
                NazwaKomputera = _nazwaKomputera,
                NazwaPliku = Path.GetFileName(sciezka),
                Tresc = tresc,
                Hash = ObliczHash(tresc),
                DataLogowania = DateTime.Now
            };

            if (!await ProbujWyslac(dane))
            {
                Console.WriteLine($"[Agent] Buforuję offline: {dane.NazwaPliku}");
                await _buforLock.WaitAsync();
                try { _buforOffline.Add(dane); ZapiszBufor(); }
                finally { _buforLock.Release(); }
            }
            else
            {
                Console.WriteLine($"[Agent] Wysłano: {dane.NazwaPliku}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent] Błąd WyslijPlik: {ex.Message}");
        }
    }

    private async Task<bool> ProbujWyslac(ZdarzenieDane dane)
    {
        try
        {
            var resp = await _client.PostAsJsonAsync(_logujUrl, dane);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ─── BUFOR OFFLINE ────────────────────────────────────────────────────────

    private async Task SprobujWyslacBufor()
    {
        if (!_buforOffline.Any()) return;

        await _buforLock.WaitAsync();
        List<ZdarzenieDane> doWyslania;
        try
        {
            doWyslania = _buforOffline.ToList();
            _buforOffline.Clear();
        }
        finally { _buforLock.Release(); }

        Console.WriteLine($"[Agent] Wysyłam bufor offline: {doWyslania.Count} zdarzeń...");
        var nieudane = new List<ZdarzenieDane>();

        foreach (var d in doWyslania)
        {
            if (!await ProbujWyslac(d)) nieudane.Add(d);
        }

        if (nieudane.Any())
        {
            Console.WriteLine($"[Agent] {nieudane.Count} zdarzeń zostało w buforze (serwer nadal offline).");
            await _buforLock.WaitAsync();
            try { _buforOffline.AddRange(nieudane); ZapiszBufor(); }
            finally { _buforLock.Release(); }
        }
        else
        {
            Console.WriteLine("[Agent] Bufor offline opróżniony.");
        }
    }

    private void WczytajBufor()
    {
        try
        {
            if (File.Exists(_buforPath))
            {
                var dane = JsonSerializer.Deserialize<List<ZdarzenieDane>>(File.ReadAllText(_buforPath));
                if (dane != null) { _buforOffline.AddRange(dane); }
                Console.WriteLine($"[Agent] Wczytano bufor: {_buforOffline.Count} zdarzeń.");
            }
        }
        catch { }
    }

    private void ZapiszBufor()
    {
        try { File.WriteAllText(_buforPath, JsonSerializer.Serialize(_buforOffline)); }
        catch { }
    }

    private static string ObliczHash(string input)
    {
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }

    // ─── ZATRZYMANIE ──────────────────────────────────────────────────────────

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[Agent] Zatrzymywanie...");
        if (_hubConnection != null)
            await _hubConnection.DisposeAsync();
        foreach (var w in _watchers.Values)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        ZapiszBufor();
        await base.StopAsync(cancellationToken);
    }
}

// ─── MODELE DANYCH ────────────────────────────────────────────────────────────

public class ZdarzenieDane
{
    public string NazwaStudenta { get; set; } = "";
    public string NazwaKomputera { get; set; } = "";
    public string NazwaPliku { get; set; } = "";
    public string Tresc { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime DataLogowania { get; set; } = DateTime.Now;
}