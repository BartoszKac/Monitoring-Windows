// =============================================
// PLIK: Controllers/NadzorController.cs  (ZASTĄP)
// =============================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SerwerWeb.Controllers;
using webserwer.Models;

namespace webserwer.Controllers;

public class NadzorController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<NadzorHub> _hubContext;

    public NadzorController(ApplicationDbContext context, IHubContext<NadzorHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    // ─── STRONA GŁÓWNA ────────────────────────────────────────────────────────

    public async Task<IActionResult> Index(string? komputer = null)
    {
        var komputery = await _context.Komputery
            .Include(k => k.Foldery)
            .OrderByDescending(k => k.Online)
            .ThenBy(k => k.NazwaKomputera)
            .ToListAsync();

        var query = _context.Zdarzenia.AsQueryable();
        if (!string.IsNullOrEmpty(komputer))
            query = query.Where(z => z.NazwaKomputera == komputer);

        var historia = await query
            .OrderByDescending(z => z.DataLogowania)
            .Take(200)
            .ToListAsync();

        ViewBag.Komputery = komputery;
        ViewBag.WybranyKomputer = komputer;
        ViewBag.FolderyGlobalne = await _context.FolderyMonitorowane.ToListAsync();

        return View(historia);
    }

    // ─── LOGI Z FILTRAMI ──────────────────────────────────────────────────────

    public async Task<IActionResult> OstatnieAktywnosci(
        int strona = 1,
        string szukaj = "",
        string status = "",
        string student = "",
        string komputer = "",
        DateTime? dataOd = null,
        DateTime? dataDo = null)
    {
        int wielkoscStrony = 50;
        int pomin = (strona - 1) * wielkoscStrony;

        var query = _context.Zdarzenia.AsQueryable();

        if (!string.IsNullOrEmpty(szukaj))
            query = query.Where(z => z.NazwaStudenta.Contains(szukaj) || z.NazwaPliku.Contains(szukaj));

        if (!string.IsNullOrEmpty(student))
            query = query.Where(z => z.NazwaStudenta == student);

        if (!string.IsNullOrEmpty(komputer))
            query = query.Where(z => z.NazwaKomputera == komputer);

        if (status == "kopia") query = query.Where(z => z.CzyToKopia == true);
        else if (status == "ok") query = query.Where(z => z.CzyToKopia == false);

        if (dataOd.HasValue)
            query = query.Where(z => z.DataLogowania >= dataOd.Value);
        if (dataDo.HasValue)
            query = query.Where(z => z.DataLogowania <= dataDo.Value.Date.AddDays(1).AddTicks(-1));

        int wszystkich = await query.CountAsync();

        var lista = await query
            .OrderByDescending(z => z.DataLogowania)
            .Skip(pomin)
            .Take(wielkoscStrony)
            .ToListAsync();

        ViewBag.WszyscyStudenci = await _context.Zdarzenia.Select(z => z.NazwaStudenta).Distinct().OrderBy(n => n).ToListAsync();
        ViewBag.WszystkieKomputery = await _context.Zdarzenia.Select(z => z.NazwaKomputera).Distinct().OrderBy(n => n).ToListAsync();
        ViewBag.Strona = strona;
        ViewBag.MaNastepna = (pomin + wielkoscStrony) < wszystkich;
        ViewBag.Szukaj = szukaj;
        ViewBag.Status = status;
        ViewBag.Student = student;
        ViewBag.Komputer = komputer;
        ViewBag.DataOd = dataOd?.ToString("yyyy-MM-dd");
        ViewBag.DataDo = dataDo?.ToString("yyyy-MM-dd");

        return View(lista);
    }

    // ─── API: LOGOWANIE ZDARZEŃ ───────────────────────────────────────────────

    [HttpPost]
    [Route("api/nadzor/loguj")]
    public async Task<IActionResult> Loguj([FromBody] ZdarzeniePliku nowe)
    {
        if (nowe == null) return BadRequest("Błędne dane.");

        bool zablokowany = await _context.CzarnaLista
            .AnyAsync(c => c.NazwaStudenta == nowe.NazwaStudenta);
        if (zablokowany)
            return StatusCode(403, new { status = "Zablokowany", alert = false });

        bool czyKopia = await _context.Zdarzenia
            .AnyAsync(z => z.Hash == nowe.Hash && z.NazwaStudenta != nowe.NazwaStudenta);

        nowe.CzyToKopia = czyKopia;
        nowe.DataLogowania = DateTime.Now;

        if (czyKopia)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"[ALERT] Kopia! {nowe.NazwaStudenta} @ {nowe.NazwaKomputera}");
            Console.ResetColor();
        }

        _context.Zdarzenia.Add(nowe);
        await _context.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("NoweZdarzenie", new
        {
            nowe.Id,
            nowe.NazwaStudenta,
            nowe.NazwaKomputera,
            nowe.NazwaPliku,
            nowe.DataLogowania,
            nowe.CzyToKopia
        });

        return Ok(new { status = "Sukces", alert = nowe.CzyToKopia });
    }

    // ─── API: KONFIGURACJA DLA AGENTA ────────────────────────────────────────

    [HttpGet]
    [Route("api/nadzor/konfiguracja")]
    public async Task<IActionResult> GetKonfiguracja([FromQuery] string? komputer = null)
    {
        List<string> sciezki;

        if (!string.IsNullOrEmpty(komputer))
        {
            var komp = await _context.Komputery
                .Include(k => k.Foldery)
                .FirstOrDefaultAsync(k => k.NazwaKomputera == komputer);

            sciezki = (komp != null && komp.Foldery.Any())
                ? komp.Foldery.Select(f => f.Sciezka).ToList()
                : await _context.FolderyMonitorowane.Select(f => f.Sciezka).ToListAsync();
        }
        else
        {
            sciezki = await _context.FolderyMonitorowane.Select(f => f.Sciezka).ToListAsync();
        }

        // Zwracamy pustą listę zamiast 404 – agent sam obsłuży brak folderów
        return Ok(sciezki);
    }

    // ─── API: ŻĄDANIE DRZEWA FOLDERÓW OD AGENTA ──────────────────────────────

    [HttpPost]
    [Route("api/nadzor/zadaj-drzewo")]
    public async Task<IActionResult> ZadajDrzewo([FromBody] ZadanieDrzewaDto dto)
    {
        // Szukamy komputera w bazie, żeby sprawdzić czy jest Online
        var komp = await _context.Komputery
            .FirstOrDefaultAsync(k => k.NazwaKomputera == dto.nazwaKomputera);

        if (komp == null || !komp.Online)
            return BadRequest("Komputer offline lub nie znaleziony.");

        // Wysyłamy do GRUPY o nazwie takiej jak nazwa komputera
        // Agent w NadzorHub.cs dodaje się do grupy o swojej nazwie przy rejestracji
        await _hubContext.Clients.Group(dto.nazwaKomputera)
            .SendAsync("PobierzDrzewo", dto.sciezka ?? "");

        return Ok();

    }
    // ─── ZARZĄDZANIE KOMPUTERAMI ──────────────────────────────────────────────

    public async Task<IActionResult> ZarzadzajKomputerami()
    {
        var komputery = await _context.Komputery
            .Include(k => k.Foldery)
            .OrderByDescending(k => k.Online)
            .ThenBy(k => k.NazwaKomputera)
            .ToListAsync();
        return View(komputery);
    }

    /// <summary>
    /// Strona do przeglądania folderów zdalnego komputera i zarządzania śledzeniem.
    /// </summary>
    public async Task<IActionResult> KomputerFoldery(int id)
    {
        var komp = await _context.Komputery
            .Include(k => k.Foldery)
            .FirstOrDefaultAsync(k => k.Id == id);

        if (komp == null) return NotFound();

        return View(komp);
    }

    [HttpPost]
    public async Task<IActionResult> UsunKomputer(int id)
    {
        var komp = await _context.Komputery
            .Include(k => k.Foldery)
            .FirstOrDefaultAsync(k => k.Id == id);

        if (komp != null)
        {
            _context.FolderyKomputerow.RemoveRange(komp.Foldery);
            _context.Komputery.Remove(komp);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction("ZarzadzajKomputerami");
    }

    [HttpPost]
    public async Task<IActionResult> DodajFolderKomputera(int komputerId, string sciezka)
    {
        if (string.IsNullOrWhiteSpace(sciezka))
            return RedirectToAction("ZarzadzajKomputerami");

        bool istnieje = await _context.FolderyKomputerow
            .AnyAsync(f => f.KomputerId == komputerId && f.Sciezka == sciezka);

        if (!istnieje)
        {
            _context.FolderyKomputerow.Add(new FolderKomputera
            {
                KomputerId = komputerId,
                Sciezka = sciezka.Trim(),
                NazwaWyswietlana = Path.GetFileName(sciezka.TrimEnd('\\', '/')) ?? sciezka,
                DataDodania = DateTime.Now
            });
            await _context.SaveChangesAsync();

            var komputer = await _context.Komputery.FindAsync(komputerId);
            if (komputer != null && !string.IsNullOrEmpty(komputer.ConnectionId))
                await _hubContext.Clients.Client(komputer.ConnectionId)
                    .SendAsync("NowyFolder", sciezka);
        }

        return RedirectToAction("KomputerFoldery", new { id = komputerId });
    }

    [HttpPost]
    public async Task<IActionResult> UsunFolderKomputera(int id)
    {
        var folder = await _context.FolderyKomputerow
            .Include(f => f.Komputer)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (folder != null)
        {
            int komputerId = folder.KomputerId;
            string sciezka = folder.Sciezka;
            string? connId = folder.Komputer?.ConnectionId;

            _context.FolderyKomputerow.Remove(folder);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(connId))
                await _hubContext.Clients.Client(connId).SendAsync("UsunFolder", sciezka);

            return RedirectToAction("KomputerFoldery", new { id = komputerId });
        }

        return RedirectToAction("ZarzadzajKomputerami");
    }

    // ─── GLOBALNE FOLDERY (fallback) ──────────────────────────────────────────

    public IActionResult Konfiguracja(string sciezka)
    {
        string katalog = string.IsNullOrEmpty(sciezka) ? @"C:\" : sciezka;
        var podfoldery = Directory.Exists(katalog)
            ? Directory.GetDirectories(katalog).Select(Path.GetFullPath).ToList()
            : new List<string>();

        ViewBag.ObecnaSciezka = katalog;
        ViewBag.Rodzic = Directory.GetParent(katalog)?.FullName;
        return View(podfoldery);
    }

    [HttpPost]
    public async Task<IActionResult> ZatwierdzFolder(string sciezka)
    {
        if (string.IsNullOrEmpty(sciezka)) return RedirectToAction("Konfiguracja");

        bool juzIstnieje = await _context.FolderyMonitorowane.AnyAsync(f => f.Sciezka == sciezka);
        if (!juzIstnieje)
        {
            _context.FolderyMonitorowane.Add(new FolderMonitorowany
            {
                Sciezka = sciezka,
                NazwaWyswietlana = Path.GetFileName(sciezka) ?? sciezka
            });
            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("NowyFolder", sciezka);
        }

        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> UsunFolderGlobalny(int id)
    {
        var folder = await _context.FolderyMonitorowane.FindAsync(id);
        if (folder != null)
        {
            _context.FolderyMonitorowane.Remove(folder);
            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("UsunFolder", folder.Sciezka);
        }
        return RedirectToAction("Index");
    }

    // ─── CZARNA LISTA ─────────────────────────────────────────────────────────

    public async Task<IActionResult> CzarnaListaView()
    {
        var lista = await _context.CzarnaLista
            .OrderByDescending(c => c.DataDodania).ToListAsync();

        var naListach = lista.Select(c => c.NazwaStudenta).ToHashSet();
        ViewBag.DostepniStudenci = await _context.Zdarzenia
            .Select(z => z.NazwaStudenta).Distinct()
            .Where(n => !naListach.Contains(n))
            .OrderBy(n => n).ToListAsync();

        return View(lista);
    }

    [HttpPost]
    public async Task<IActionResult> DodajDoCzarnejListy(string nazwaStudenta, string powod)
    {
        if (!string.IsNullOrEmpty(nazwaStudenta) &&
            !await _context.CzarnaLista.AnyAsync(c => c.NazwaStudenta == nazwaStudenta))
        {
            _context.CzarnaLista.Add(new CzarnaLista
            {
                NazwaStudenta = nazwaStudenta,
                Powod = powod ?? "",
                DataDodania = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }
        return RedirectToAction("CzarnaListaView");
    }

    [HttpPost]
    public async Task<IActionResult> UsunZCzarnejListy(int id)
    {
        var wpis = await _context.CzarnaLista.FindAsync(id);
        if (wpis != null) { _context.CzarnaLista.Remove(wpis); await _context.SaveChangesAsync(); }
        return RedirectToAction("CzarnaListaView");
    }

    // ─── SZCZEGÓŁY ────────────────────────────────────────────────────────────

    public async Task<IActionResult> Szczegoly(int id)
    {
        var z = await _context.Zdarzenia.FirstOrDefaultAsync(x => x.Id == id);
        if (z == null) return NotFound();

        if (z.CzyToKopia)
        {
            var org = await _context.Zdarzenia
                .Where(x => x.Hash == z.Hash && x.Id != z.Id)
                .OrderBy(x => x.DataLogowania).FirstOrDefaultAsync();
            ViewBag.OryginalnyAutor = org?.NazwaStudenta ?? "Nieznany";
            ViewBag.DataOryginallu = org?.DataLogowania;
        }
        return View(z);
    }

    public async Task<IActionResult> SzczegolyFolderu(string sciezka)
    {
        if (string.IsNullOrEmpty(sciezka) || !Directory.Exists(sciezka))
            return NotFound("Folder nie istnieje.");

        var folder = await _context.FolderyMonitorowane.FirstOrDefaultAsync(f => f.Sciezka == sciezka);
        ViewBag.NazwaFolderu = folder?.NazwaWyswietlana ?? Path.GetFileName(sciezka);
        ViewBag.Sciezka = sciezka;

        ViewBag.PlikiNaDysku = Directory.GetFiles(sciezka).Select(f => new
        {
            Nazwa = Path.GetFileName(f),
            DataModyfikacji = System.IO.File.GetLastWriteTime(f),
            Rozmiar = new FileInfo(f).Length / 1024 + " KB"
        }).ToList();

        var historia = await _context.Zdarzenia
            .Where(z => z.NazwaPliku != null)
            .OrderByDescending(z => z.DataLogowania)
            .ToListAsync();

        return View(historia);
    }
}

// ─── DTO ──────────────────────────────────────────────────────────────────────
public class ZadanieDrzewaDto
{
    public string nazwaKomputera { get; set; } = ""; // Zmienione na małą literę (zgodnie z JS)
    public string? sciezka { get; set; }           // Zmienione na małą literę
}