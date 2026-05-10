// =============================================
// PLIK: Controllers/NadzorHub.cs  (ZASTĄP – finalna wersja)
// =============================================
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using webserwer.Models;

namespace SerwerWeb.Controllers
{
    public class NadzorHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public NadzorHub(ApplicationDbContext context)
        {
            _context = context;
        }

        // ── Admin dołącza do grupy "admin" żeby odbierać odpowiedzi od agentów ──
        public async Task DolaczDoGrupyAdmin()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
        }

        // ── Agent rejestruje się przy starcie ────────────────────────────────────
        public async Task Zarejestruj(string nazwaKomputera, string nazwaStudenta)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, nazwaKomputera);

            var komp = await _context.Komputery
                .FirstOrDefaultAsync(k => k.NazwaKomputera == nazwaKomputera);

            if (komp == null)
            {
                komp = new Komputer
                {
                    NazwaKomputera = nazwaKomputera,
                    NazwaStudenta = nazwaStudenta,
                    DataDodania = DateTime.Now,
                };
                _context.Komputery.Add(komp);
            }
            else
            {
                komp.NazwaStudenta = nazwaStudenta;
            }

            komp.Online = true;
            komp.OstatnioWidziany = DateTime.Now;
            komp.ConnectionId = Context.ConnectionId;

            await _context.SaveChangesAsync();

            // Poinformuj panel admina
            await Clients.Group("admin").SendAsync("AgentPolaczony", new
            {
                komp.Id,
                komp.NazwaKomputera,
                komp.NazwaStudenta,
                komp.OstatnioWidziany
            });

            // Wyślij agentowi aktualne foldery do śledzenia
            var sledzone = await _context.FolderyKomputerow
                .Where(f => f.KomputerId == komp.Id)
                .Select(f => f.Sciezka)
                .ToListAsync();

            await Clients.Caller.SendAsync("UstawSledzenie", sledzone);
        }

        // ── Agent odsyła zawartość folderu (odpowiedź na PobierzDrzewo) ─────────
        public async Task OdpowiedzDrzewo(string nazwaKomputera, string sciezka, List<string> podfoldery)
        {
            await Clients.Group("admin").SendAsync("OdebranoDrzewo", nazwaKomputera, sciezka, podfoldery);
        }

        // ── Rozłączenie agenta ───────────────────────────────────────────────────
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var komp = await _context.Komputery
                .FirstOrDefaultAsync(k => k.ConnectionId == Context.ConnectionId);

            if (komp != null)
            {
                komp.Online = false;
                komp.OstatnioWidziany = DateTime.Now;
                komp.ConnectionId = null;
                await _context.SaveChangesAsync();

                await Clients.Group("admin").SendAsync("AgentRozlaczony", komp.NazwaKomputera);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}