using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using webserwer.Models;

namespace webserwer.Controllers;

public class NadzorController : Controller
{
    private readonly ApplicationDbContext _context;

    public NadzorController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> OstatnieAktywnosci(int strona = 1, string szukaj = "", string status = "", DateTime? dataOd = null, DateTime? dataDo = null)
    {
        int wielkoscStrony = 50;
        int pomin = (strona - 1) * wielkoscStrony;

        var query = _context.Zdarzenia.AsQueryable();

        // Filtrowanie po tekście
        if (!string.IsNullOrEmpty(szukaj))
        {
            query = query.Where(z => z.NazwaStudenta.Contains(szukaj) || z.NazwaPliku.Contains(szukaj));
        }

        // Filtrowanie po statusie
        if (status == "kopia") query = query.Where(z => z.CzyToKopia == true);
        else if (status == "ok") query = query.Where(z => z.CzyToKopia == false);

        // NOWE: Filtrowanie po dacie
        if (dataOd.HasValue)
        {
            query = query.Where(z => z.DataLogowania >= dataOd.Value);
        }
        if (dataDo.HasValue)
        {
            // Dodajemy 1 dzień lub ustawiamy czas na 23:59:59, żeby objąć cały wybrany dzień "do"
            var koniecDnia = dataDo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(z => z.DataLogowania <= koniecDnia);
        }

        int wszystkichZgodnych = await query.CountAsync();

        var lista = await query
            .OrderByDescending(z => z.DataLogowania)
            .Skip(pomin)
            .Take(wielkoscStrony)
            .ToListAsync();

        // Przekazujemy wszystko do widoku
        ViewBag.Strona = strona;
        ViewBag.MaNastepna = (pomin + wielkoscStrony) < wszystkichZgodnych;
        ViewBag.Szukaj = szukaj;
        ViewBag.Status = status;
        ViewBag.DataOd = dataOd?.ToString("yyyy-MM-dd"); // Format dla inputa typu date
        ViewBag.DataDo = dataDo?.ToString("yyyy-MM-dd");

        return View(lista);
    }

    public async Task<IActionResult> Index()
    {
        // Pobieramy historię logowań (to już masz)
        var historia = await _context.Zdarzenia
            .OrderByDescending(z => z.DataLogowania)
            .ToListAsync();

        // POBIERAMY LISTĘ ŚLEDZONYCH FOLDERÓW
        var foldery = await _context.FolderyMonitorowane.ToListAsync();

        // Przekazujemy foldery przez ViewBag
        ViewBag.Foldery = foldery;

        return View(historia);
    }

    [HttpPost]
    [Route("api/nadzor/loguj")]
    public async Task<IActionResult> Loguj([FromBody] ZdarzeniePliku nowe)
    {
        if (nowe == null) return BadRequest("Błędne dane.");

        // 1. Logika porównywania Hasha (Anty-plagiat)
        // Sprawdzamy, czy w tabeli Zdarzenia istnieje już taki sam Hash,
        // ale wysłany przez INNEGO studenta niż ten, który wysyła go teraz.
        bool czyIstniejeTakiSamHash = await _context.Zdarzenia
            .AnyAsync(z => z.Hash == nowe.Hash && z.NazwaStudenta != nowe.NazwaStudenta);

        // 2. Przypisujemy wynik do obiektu
        nowe.CzyToKopia = czyIstniejeTakiSamHash;
        nowe.DataLogowania = DateTime.Now;

        // 3. Opcjonalnie: Wypisz alert w konsoli serwera (ułatwia debugowanie)
        if (nowe.CzyToKopia)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"[ALERT] Wykryto kopię! Student {nowe.NazwaStudenta} wysłał plik identyczny z już istniejącym.");
            Console.ResetColor();
        }

        // 4. Zapisujemy do bazy
        _context.Zdarzenia.Add(nowe);
        await _context.SaveChangesAsync();

        // 5. Zwracamy odpowiedź do Agenta (studenta)
        // Agent dzięki temu wyświetli czerwony komunikat u siebie na ekranie
        return Ok(new { status = "Sukces", alert = nowe.CzyToKopia });
    }
    // Widok główny konfiguracji
    public IActionResult Konfiguracja(string sciezka)
    {
        // Domyślnie startujemy np. od C:\ (lub innej ścieżki, gdzie trzymasz projekty)
        string katalogStartowy = string.IsNullOrEmpty(sciezka) ? @"C:\" : sciezka;

        // Pobieramy listę podfolderów w wybranym miejscu
        var podfoldery = Directory.GetDirectories(katalogStartowy)
                                  .Select(f => Path.GetFullPath(f)).ToList();

        ViewBag.ObecnaSciezka = katalogStartowy;
        ViewBag.Rodzic = Directory.GetParent(katalogStartowy)?.FullName;

        return View(podfoldery);
    }

    [HttpPost]
    public async Task<IActionResult> ZatwierdzFolder(string sciezka)
    {
        if (string.IsNullOrEmpty(sciezka)) return RedirectToAction("Konfiguracja");

        // Wyciągamy ostatni człon ścieżki (np. z "C:\Users\Desktop" zrobi "Desktop")
        string nazwa = Path.GetFileName(sciezka);

        // Jeśli to sam dysk (np. "C:\"), to GetFileName zwróci pusty string, wtedy dajemy nazwę dysku
        if (string.IsNullOrEmpty(nazwa)) nazwa = sciezka;

        var nowyFolder = new FolderMonitorowany
        {
            Sciezka = sciezka,
            NazwaWyswietlana = nazwa // TO ROZWIĄŻE PROBLEM Z NULLem
        };

        _context.FolderyMonitorowane.Add(nowyFolder);
        await _context.SaveChangesAsync();

        return RedirectToAction("Index");
    }

    [HttpGet]
    [Route("api/nadzor/konfiguracja")]
    public async Task<IActionResult> GetKonfiguracja()
    {
        // Pobieramy ostatnio dodany folder przez admina
        var folder = await _context.FolderyMonitorowane
            .OrderByDescending(f => f.Id)
            .FirstOrDefaultAsync();

        if (folder == null) return NotFound("Brak skonfigurowanych folderów.");

        return Ok(new { sciezka = folder.Sciezka });
    }

    public async Task<IActionResult> Szczegoly(int id)
    {
        // 1. Pobieramy szczegóły klikniętego pliku
        var zdarzenie = await _context.Zdarzenia.FirstOrDefaultAsync(z => z.Id == id);

        if (zdarzenie == null) return NotFound();

        // 2. Jeśli to kopia, szukamy oryginału
        if (zdarzenie.CzyToKopia)
        {
            var oryginal = await _context.Zdarzenia
                .Where(z => z.Hash == zdarzenie.Hash && z.Id != zdarzenie.Id)
                .OrderBy(z => z.DataLogowania) // Najstarszy wpis to oryginał
                .FirstOrDefaultAsync();

            ViewBag.OryginalnyAutor = oryginal?.NazwaStudenta ?? "Nieznany (oryginał mógł zostać usunięty)";
            ViewBag.DataOryginallu = oryginal?.DataLogowania;
        }

        return View(zdarzenie);
    }

    public async Task<IActionResult> SzczegolyFolderu(string sciezka)
    {
        if (string.IsNullOrEmpty(sciezka) || !Directory.Exists(sciezka))
            return NotFound("Folder nie istnieje na serwerze.");

        var folder = await _context.FolderyMonitorowane
            .FirstOrDefaultAsync(f => f.Sciezka == sciezka);

        ViewBag.NazwaFolderu = folder?.NazwaWyswietlana ?? Path.GetFileName(sciezka);
        ViewBag.Sciezka = sciezka;

        // 1. Pobieramy FIZYCZNĄ listę plików z dysku
        var plikiNaDysku = Directory.GetFiles(sciezka)
            .Select(f => new {
                Nazwa = Path.GetFileName(f),
                DataModyfikacji = System.IO.File.GetLastWriteTime(f),
                Rozmiar = new FileInfo(f).Length / 1024 + " KB"
            }).ToList();

        ViewBag.PlikiNaDysku = plikiNaDysku;

        // 2. Pobieramy HISTORIĘ wysyłek z bazy (to co miałeś wcześniej)
        var historia = await _context.Zdarzenia
            .Where(z => z.NazwaPliku != null) // Tutaj możesz dodać filtrację jeśli logujesz ścieżkę
            .OrderByDescending(z => z.DataLogowania)
            .ToListAsync();

        return View(historia);
    }

}