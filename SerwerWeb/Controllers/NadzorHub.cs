using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using webserwer.Models;

namespace SerwerWeb.Controllers
{
    public class NadzorHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<NadzorHub> _logger;

        // Klucz: "nazwaKomputera|sciezka" → connectionId przeglądarki czekającej na odpowiedź
        // Dzięki temu wiele równoległych żądań (np. rozwijanie wielu podfolderów naraz) działa poprawnie
        private static readonly ConcurrentDictionary<string, string> _oczekujacy = new();

        public NadzorHub(ApplicationDbContext context, ILogger<NadzorHub> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ─── REJESTRACJA AGENTA ───────────────────────────────────────────────────
        public async Task Zarejestruj(string nazwaKomputera, string nazwaStudenta)
        {
            var komp = await _context.Komputery
                .Include(k => k.Foldery)
                .FirstOrDefaultAsync(k => k.NazwaKomputera == nazwaKomputera);

            if (komp == null)
            {
                komp = new Komputer
                {
                    NazwaKomputera = nazwaKomputera,
                    NazwaStudenta = nazwaStudenta,
                    Online = true,
                    ConnectionId = Context.ConnectionId,
                    OstatnioWidziany = DateTime.Now,
                    DataDodania = DateTime.Now
                };
                _context.Komputery.Add(komp);
            }
            else
            {
                komp.Online = true;
                komp.ConnectionId = Context.ConnectionId;
                komp.NazwaStudenta = nazwaStudenta;
                komp.OstatnioWidziany = DateTime.Now;
            }

            await _context.SaveChangesAsync();

            await Groups.AddToGroupAsync(Context.ConnectionId, nazwaKomputera);

            var sciezki = komp.Foldery.Select(f => f.Sciezka).ToList();
            await Clients.Caller.SendAsync("UstawSledzenie", sciezki);

            await Clients.Group("Admin").SendAsync("AgentPolaczony", new
            {
                nazwaKomputera,
                nazwaStudenta,
                connectionId = Context.ConnectionId
            });

            _logger.LogInformation("Agent zarejestrowany: {PC} dla {Student}", nazwaKomputera, nazwaStudenta);
        }

        // ─── ADMIN DOŁĄCZA DO GRUPY POWIADOMIEŃ ──────────────────────────────────
        public async Task DolaczDoGrupyAdmin()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admin");
            _logger.LogInformation("Admin dołączył: {Id}", Context.ConnectionId);
        }

        // ─── PRZEGLĄDARKA ŻĄDA DRZEWA FOLDERÓW ───────────────────────────────────
        public async Task ZadajDrzewo(string nazwaKomputera, string sciezka)
        {
            // Klucz złożony: komputer + ścieżka — dzięki temu równoległe żądania
            // do różnych podfolderów nie nadpisują sobie connectionId
            var klucz = $"{nazwaKomputera}|{sciezka ?? ""}";
            _oczekujacy[klucz] = Context.ConnectionId;

            await Clients.Group(nazwaKomputera).SendAsync("PobierzDrzewo", sciezka ?? "");

            _logger.LogInformation("ZadajDrzewo → {PC} ścieżka='{Path}'", nazwaKomputera, sciezka);
        }

        // ─── AGENT ODPOWIADA DRZEWEM FOLDERÓW ────────────────────────────────────
        public async Task OdpowiedzDrzewo(string nazwaKomputera, string sciezka, List<string> podfoldery)
        {
            var klucz = $"{nazwaKomputera}|{sciezka ?? ""}";
            var ile = podfoldery?.Count ?? 0;

            if (_oczekujacy.TryRemove(klucz, out var connId))
            {
                _logger.LogInformation("✅ OdpowiedzDrzewo → przeglądarka {PC} ścieżka='{Path}' ({Count} folderów)",
                    nazwaKomputera, sciezka, ile);
                await Clients.Client(connId).SendAsync("OdpowiedzDrzewaFolderow", nazwaKomputera, sciezka, podfoldery);
            }
            else
            {
                _logger.LogWarning("⚠️ OdpowiedzDrzewo — brak oczekującej przeglądarki dla klucza '{Key}', wysyłam do grupy Admin ({Count} folderów)",
                    klucz, ile);
                await Clients.Group("Admin").SendAsync("OdpowiedzDrzewaFolderow", nazwaKomputera, sciezka, podfoldery);
            }
        }

        // ─── ROZŁĄCZENIE ──────────────────────────────────────────────────────────
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var komp = await _context.Komputery
                .FirstOrDefaultAsync(k => k.ConnectionId == Context.ConnectionId);

            if (komp != null)
            {
                komp.Online = false;
                komp.OstatnioWidziany = DateTime.Now;
                await _context.SaveChangesAsync();

                await Clients.Group("Admin").SendAsync("AgentRozlaczony", new
                {
                    nazwaKomputera = komp.NazwaKomputera,
                    nazwaStudenta = komp.NazwaStudenta
                });

                _logger.LogInformation("Agent rozłączony: {PC}", komp.NazwaKomputera);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}