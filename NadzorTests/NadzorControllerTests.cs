using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using SerwerWeb.Controllers;
using webserwer.Controllers;
using webserwer.Models;
using Xunit;

namespace NadzorTests;

// ─── HELPER: Tworzy świeżą bazę InMemory dla każdego testu ───────────────────
public static class DbHelper
{
    public static ApplicationDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(opts);
    }
}

// ─── HELPER: Mock IHubContext ─────────────────────────────────────────────────
public static class HubMockHelper
{
    public static (Mock<IHubContext<NadzorHub>> hubCtx, Mock<IClientProxy> allClients) Create()
    {
        var hubCtx = new Mock<IHubContext<NadzorHub>>();
        var allClients = new Mock<IClientProxy>();
        var hubClients = new Mock<IHubClients>();

        hubClients.Setup(c => c.All).Returns(allClients.Object);
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(allClients.Object);
        hubCtx.Setup(h => h.Clients).Returns(hubClients.Object);

        return (hubCtx, allClients);
    }
}

// =============================================================================
// TESTY: Loguj (POST api/nadzor/loguj)
// =============================================================================
public class LogujTests
{
    [Fact]
    public async Task Loguj_PoprawneZdarzenie_ZwracaOkISukces()
    {
        using var db = DbHelper.CreateDb(nameof(Loguj_PoprawneZdarzenie_ZwracaOkISukces));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var zdarzenie = new ZdarzeniePliku
        {
            NazwaStudenta = "jan.kowalski",
            NazwaKomputera = "PC-01",
            NazwaPliku = "zadanie.cs",
            Hash = "abc123",
            Tresc = "kod"
        };

        var result = await ctrl.Loguj(zdarzenie) as OkObjectResult;

        Assert.NotNull(result);
        dynamic val = result!.Value!;
        Assert.Equal("Sukces", (string)val.GetType().GetProperty("status").GetValue(val));
        Assert.Equal(1, await db.Zdarzenia.CountAsync());
    }

    [Fact]
    public async Task Loguj_NullBody_ZwracaBadRequest()
    {
        using var db = DbHelper.CreateDb(nameof(Loguj_NullBody_ZwracaBadRequest));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.Loguj(null!);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Loguj_ZablokowanyStudent_Zwraca403()
    {
        using var db = DbHelper.CreateDb(nameof(Loguj_ZablokowanyStudent_Zwraca403));
        db.CzarnaLista.Add(new CzarnaLista { NazwaStudenta = "zly.student", Powod = "oszustwo" });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var zdarzenie = new ZdarzeniePliku { NazwaStudenta = "zly.student", Hash = "x" };
        var result = await ctrl.Loguj(zdarzenie) as ObjectResult;

        Assert.Equal(403, result!.StatusCode);
    }

    [Fact]
    public async Task Loguj_TenSamHash_InnyStudent_UstawiaCzyToKopiaNaTrue()
    {
        using var db = DbHelper.CreateDb(nameof(Loguj_TenSamHash_InnyStudent_UstawiaCzyToKopiaNaTrue));

        // Oryginalne zdarzenie innego studenta z tym samym hashem
        db.Zdarzenia.Add(new ZdarzeniePliku
        {
            NazwaStudenta = "oryginalny.student",
            Hash = "hash_duplikat",
            NazwaPliku = "plik.cs"
        });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var zdarzenie = new ZdarzeniePliku
        {
            NazwaStudenta = "kopiujacy.student",
            Hash = "hash_duplikat",
            NazwaPliku = "plik_kopia.cs"
        };

        var result = await ctrl.Loguj(zdarzenie) as OkObjectResult;
        Assert.NotNull(result);

        var zapisane = await db.Zdarzenia.FirstAsync(z => z.NazwaStudenta == "kopiujacy.student");
        Assert.True(zapisane.CzyToKopia);
    }

    [Fact]
    public async Task Loguj_TenSamHash_TenSamStudent_NieUstawiaCzyToKopii()
    {
        using var db = DbHelper.CreateDb(nameof(Loguj_TenSamHash_TenSamStudent_NieUstawiaCzyToKopii));

        db.Zdarzenia.Add(new ZdarzeniePliku
        {
            NazwaStudenta = "jan.kowalski",
            Hash = "moj_hash",
            NazwaPliku = "plik.cs"
        });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var zdarzenie = new ZdarzeniePliku
        {
            NazwaStudenta = "jan.kowalski",
            Hash = "moj_hash",
            NazwaPliku = "plik_v2.cs"
        };

        await ctrl.Loguj(zdarzenie);

        var zapisane = await db.Zdarzenia
            .Where(z => z.NazwaStudenta == "jan.kowalski")
            .OrderByDescending(z => z.Id)
            .FirstAsync();
        Assert.False(zapisane.CzyToKopia);
    }

    [Fact]
    public async Task Loguj_WysylaNoweZdarzenieDoSignalR()
    {
        using var db = DbHelper.CreateDb(nameof(Loguj_WysylaNoweZdarzenieDoSignalR));
        var (hub, allClients) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var zdarzenie = new ZdarzeniePliku
        {
            NazwaStudenta = "test",
            Hash = "h1",
            NazwaPliku = "f.cs"
        };

        await ctrl.Loguj(zdarzenie);

        allClients.Verify(
            c => c.SendCoreAsync("NoweZdarzenie", It.IsAny<object[]>(), default),
            Times.Once);
    }
}

// =============================================================================
// TESTY: GetKonfiguracja (GET api/nadzor/konfiguracja)
// =============================================================================
public class GetKonfiguracjaTests
{
    [Fact]
    public async Task GetKonfiguracja_BezParametru_ZwracaFolderyGlobalne()
    {
        using var db = DbHelper.CreateDb(nameof(GetKonfiguracja_BezParametru_ZwracaFolderyGlobalne));
        db.FolderyMonitorowane.AddRange(
            new FolderMonitorowany { Sciezka = @"C:\a", NazwaWyswietlana = "a" },
            new FolderMonitorowany { Sciezka = @"C:\b", NazwaWyswietlana = "b" }
        );
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.GetKonfiguracja(null) as OkObjectResult;
        var lista = result!.Value as List<string>;

        Assert.Equal(2, lista!.Count);
        Assert.Contains(@"C:\a", lista);
    }

    [Fact]
    public async Task GetKonfiguracja_KomputerMaFoldery_ZwracaFolderyKomputera()
    {
        using var db = DbHelper.CreateDb(nameof(GetKonfiguracja_KomputerMaFoldery_ZwracaFolderyKomputera));

        var komp = new Komputer { NazwaKomputera = "PC-LAB1", NazwaStudenta = "s1" };
        db.Komputery.Add(komp);
        await db.SaveChangesAsync();

        db.FolderyKomputerow.Add(new FolderKomputera
        {
            KomputerId = komp.Id,
            Sciezka = @"C:\LAB",
            NazwaWyswietlana = "LAB"
        });
        // fallback globalny — nie powinien być zwrócony
        db.FolderyMonitorowane.Add(new FolderMonitorowany { Sciezka = @"C:\global", NazwaWyswietlana = "g" });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.GetKonfiguracja("PC-LAB1") as OkObjectResult;
        var lista = result!.Value as List<string>;

        Assert.Single(lista!);
        Assert.Equal(@"C:\LAB", lista[0]);
    }

    [Fact]
    public async Task GetKonfiguracja_KomputerBezFolderow_FallbackNaGlobalne()
    {
        using var db = DbHelper.CreateDb(nameof(GetKonfiguracja_KomputerBezFolderow_FallbackNaGlobalne));

        db.Komputery.Add(new Komputer { NazwaKomputera = "PC-X", NazwaStudenta = "s" });
        db.FolderyMonitorowane.Add(new FolderMonitorowany { Sciezka = @"C:\global", NazwaWyswietlana = "g" });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.GetKonfiguracja("PC-X") as OkObjectResult;
        var lista = result!.Value as List<string>;

        Assert.Single(lista!);
        Assert.Equal(@"C:\global", lista[0]);
    }
}

// =============================================================================
// TESTY: DodajFolderKomputeraAjax (POST)
// =============================================================================
public class DodajFolderKomputeraAjaxTests
{
    [Fact]
    public async Task DodajFolder_PoprawneDto_DodajeFolderIWysylaSignalR()
    {
        using var db = DbHelper.CreateDb(nameof(DodajFolder_PoprawneDto_DodajeFolderIWysylaSignalR));
        var komp = new Komputer { NazwaKomputera = "PC-02", NazwaStudenta = "s" };
        db.Komputery.Add(komp);
        await db.SaveChangesAsync();

        var (hub, allClients) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var dto = new DodajFolderDto { KomputerId = komp.Id, Sciezka = @"C:\Dokumenty" };
        var result = await ctrl.DodajFolderKomputeraAjax(dto) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(1, await db.FolderyKomputerow.CountAsync());

        allClients.Verify(
            c => c.SendCoreAsync("UstawSledzenie", It.IsAny<object[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task DodajFolder_PustaSciazka_ZwracaBadRequest()
    {
        using var db = DbHelper.CreateDb(nameof(DodajFolder_PustaSciazka_ZwracaBadRequest));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.DodajFolderKomputeraAjax(new DodajFolderDto { Sciezka = "" });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DodajFolder_KomputerNieIstnieje_ZwracaNotFound()
    {
        using var db = DbHelper.CreateDb(nameof(DodajFolder_KomputerNieIstnieje_ZwracaNotFound));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.DodajFolderKomputeraAjax(new DodajFolderDto { KomputerId = 999, Sciezka = @"C:\x" });
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DodajFolder_DuplikatSciezki_NieDodajePonownie()
    {
        using var db = DbHelper.CreateDb(nameof(DodajFolder_DuplikatSciezki_NieDodajePonownie));
        var komp = new Komputer { NazwaKomputera = "PC-03", NazwaStudenta = "s" };
        db.Komputery.Add(komp);
        await db.SaveChangesAsync();
        db.FolderyKomputerow.Add(new FolderKomputera { KomputerId = komp.Id, Sciezka = @"C:\istniejacy" });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var dto = new DodajFolderDto { KomputerId = komp.Id, Sciezka = @"C:\istniejacy" };
        var result = await ctrl.DodajFolderKomputeraAjax(dto) as OkObjectResult;

        // Powinien zwrócić sukces=false, istnial=true
        dynamic val = result!.Value!;
        Assert.False((bool)val.GetType().GetProperty("sukces").GetValue(val));
        Assert.Equal(1, await db.FolderyKomputerow.CountAsync()); // brak duplikatu
    }
}

// =============================================================================
// TESTY: UsunFolderKomputeraAjax (POST)
// =============================================================================
public class UsunFolderKomputeraAjaxTests
{
    [Fact]
    public async Task UsunFolder_IstniejacyFolder_UsuwaIWysylaSignalR()
    {
        using var db = DbHelper.CreateDb(nameof(UsunFolder_IstniejacyFolder_UsuwaIWysylaSignalR));
        var komp = new Komputer { NazwaKomputera = "PC-04", NazwaStudenta = "s" };
        db.Komputery.Add(komp);
        await db.SaveChangesAsync();

        var folder = new FolderKomputera { KomputerId = komp.Id, Sciezka = @"C:\do\usuniecia" };
        db.FolderyKomputerow.Add(folder);
        await db.SaveChangesAsync();

        var (hub, allClients) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.UsunFolderKomputeraAjax(new UsunFolderDto { Id = folder.Id }) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(0, await db.FolderyKomputerow.CountAsync());
        allClients.Verify(
            c => c.SendCoreAsync("UstawSledzenie", It.IsAny<object[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task UsunFolder_NieIstniejacyId_ZwracaNotFound()
    {
        using var db = DbHelper.CreateDb(nameof(UsunFolder_NieIstniejacyId_ZwracaNotFound));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.UsunFolderKomputeraAjax(new UsunFolderDto { Id = 9999 });
        Assert.IsType<NotFoundObjectResult>(result);
    }
}

// =============================================================================
// TESTY: ZadajDrzewo (POST api/nadzor/zadaj-drzewo)
// =============================================================================
public class ZadajDrzewoTests
{
    [Fact]
    public async Task ZadajDrzewo_KomputerOnline_WysylaSignalRIOkReturns()
    {
        using var db = DbHelper.CreateDb(nameof(ZadajDrzewo_KomputerOnline_WysylaSignalRIOkReturns));
        db.Komputery.Add(new Komputer { NazwaKomputera = "PC-05", Online = true, NazwaStudenta = "s" });
        await db.SaveChangesAsync();

        var (hub, allClients) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.ZadajDrzewo(new ZadanieDrzewaDto { nazwaKomputera = "PC-05", sciezka = @"C:\" });

        Assert.IsType<OkResult>(result);
        allClients.Verify(
            c => c.SendCoreAsync("PobierzDrzewo", It.IsAny<object[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task ZadajDrzewo_KomputerOffline_ZwracaBadRequest()
    {
        using var db = DbHelper.CreateDb(nameof(ZadajDrzewo_KomputerOffline_ZwracaBadRequest));
        db.Komputery.Add(new Komputer { NazwaKomputera = "PC-OFF", Online = false, NazwaStudenta = "s" });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.ZadajDrzewo(new ZadanieDrzewaDto { nazwaKomputera = "PC-OFF" });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ZadajDrzewo_NieznanaNazwa_ZwracaBadRequest()
    {
        using var db = DbHelper.CreateDb(nameof(ZadajDrzewo_NieznanaNazwa_ZwracaBadRequest));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.ZadajDrzewo(new ZadanieDrzewaDto { nazwaKomputera = "GHOST" });
        Assert.IsType<BadRequestObjectResult>(result);
    }
}

// =============================================================================
// TESTY: CzarnaLista
// =============================================================================
public class CzarnaListaTests
{
    [Fact]
    public async Task DodajDoCzarnejListy_NowyStudent_DodajeDoBazy()
    {
        using var db = DbHelper.CreateDb(nameof(DodajDoCzarnejListy_NowyStudent_DodajeDoBazy));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.DodajDoCzarnejListy("nowy.student", "Powod testu");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(1, await db.CzarnaLista.CountAsync());
    }

    [Fact]
    public async Task DodajDoCzarnejListy_JuzIstnieje_NieDodajeDuplikatu()
    {
        using var db = DbHelper.CreateDb(nameof(DodajDoCzarnejListy_JuzIstnieje_NieDodajeDuplikatu));
        db.CzarnaLista.Add(new CzarnaLista { NazwaStudenta = "stary.student" });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        await ctrl.DodajDoCzarnejListy("stary.student", "drugi raz");
        Assert.Equal(1, await db.CzarnaLista.CountAsync());
    }

    [Fact]
    public async Task UsunZCzarnejListy_IstniejacyWpis_Usuwa()
    {
        using var db = DbHelper.CreateDb(nameof(UsunZCzarnejListy_IstniejacyWpis_Usuwa));
        var wpis = new CzarnaLista { NazwaStudenta = "do.usuniecia" };
        db.CzarnaLista.Add(wpis);
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        await ctrl.UsunZCzarnejListy(wpis.Id);
        Assert.Equal(0, await db.CzarnaLista.CountAsync());
    }

    [Fact]
    public async Task UsunZCzarnejListy_NieistniejacyId_NieRzucaWyjatku()
    {
        using var db = DbHelper.CreateDb(nameof(UsunZCzarnejListy_NieistniejacyId_NieRzucaWyjatku));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        // Nie powinno rzucić wyjątku
        var result = await ctrl.UsunZCzarnejListy(9999);
        Assert.IsType<RedirectToActionResult>(result);
    }
}

// =============================================================================
// TESTY: ZatwierdzFolder / UsunFolderGlobalny
// =============================================================================
public class FolderyGlobalneTests
{
    [Fact]
    public async Task ZatwierdzFolder_NowaSciezka_DodajeFolderGlobalny()
    {
        using var db = DbHelper.CreateDb(nameof(ZatwierdzFolder_NowaSciezka_DodajeFolderGlobalny));
        var (hub, allClients) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        await ctrl.ZatwierdzFolder(@"C:\nowy\folder");

        Assert.Equal(1, await db.FolderyMonitorowane.CountAsync());
        allClients.Verify(
            c => c.SendCoreAsync("NowyFolder", It.IsAny<object[]>(), default),
            Times.Once);
    }

    [Fact]
    public async Task ZatwierdzFolder_Duplikat_NieDodajePonownie()
    {
        using var db = DbHelper.CreateDb(nameof(ZatwierdzFolder_Duplikat_NieDodajePonownie));
        db.FolderyMonitorowane.Add(new FolderMonitorowany { Sciezka = @"C:\istniejacy", NazwaWyswietlana = "x" });
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        await ctrl.ZatwierdzFolder(@"C:\istniejacy");
        Assert.Equal(1, await db.FolderyMonitorowane.CountAsync());
    }

    [Fact]
    public async Task ZatwierdzFolder_PustaSciazka_Redirect()
    {
        using var db = DbHelper.CreateDb(nameof(ZatwierdzFolder_PustaSciazka_Redirect));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.ZatwierdzFolder("") as RedirectToActionResult;
        Assert.Equal("Konfiguracja", result!.ActionName);
    }

    [Fact]
    public async Task UsunFolderGlobalny_IstniejacyFolder_UsuwaIWysylaSignalR()
    {
        using var db = DbHelper.CreateDb(nameof(UsunFolderGlobalny_IstniejacyFolder_UsuwaIWysylaSignalR));
        var folder = new FolderMonitorowany { Sciezka = @"C:\stary", NazwaWyswietlana = "stary" };
        db.FolderyMonitorowane.Add(folder);
        await db.SaveChangesAsync();

        var (hub, allClients) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        await ctrl.UsunFolderGlobalny(folder.Id);

        Assert.Equal(0, await db.FolderyMonitorowane.CountAsync());
        allClients.Verify(
            c => c.SendCoreAsync("UsunFolder", It.IsAny<object[]>(), default),
            Times.Once);
    }
}

// =============================================================================
// TESTY: Szczegoly
// =============================================================================
public class SzczegolyTests
{
    [Fact]
    public async Task Szczegoly_NieistniejaceId_ZwracaNotFound()
    {
        using var db = DbHelper.CreateDb(nameof(Szczegoly_NieistniejaceId_ZwracaNotFound));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.Szczegoly(9999);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Szczegoly_IstniejaceId_ZwracaView()
    {
        using var db = DbHelper.CreateDb(nameof(Szczegoly_IstniejaceId_ZwracaView));
        var z = new ZdarzeniePliku { NazwaStudenta = "s", Hash = "h", NazwaPliku = "f.cs" };
        db.Zdarzenia.Add(z);
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.Szczegoly(z.Id) as ViewResult;
        Assert.NotNull(result);
        Assert.IsType<ZdarzeniePliku>(result!.Model);
    }

    [Fact]
    public async Task Szczegoly_KopiaZdarzenia_UstawiaOryginalnegoAutora()
    {
        using var db = DbHelper.CreateDb(nameof(Szczegoly_KopiaZdarzenia_UstawiaOryginalnegoAutora));

        var oryginal = new ZdarzeniePliku
        {
            NazwaStudenta = "autor.oryginalny",
            Hash = "wspolny_hash",
            NazwaPliku = "original.cs",
            DataLogowania = DateTime.Now.AddHours(-2)
        };
        db.Zdarzenia.Add(oryginal);
        await db.SaveChangesAsync();

        var kopia = new ZdarzeniePliku
        {
            NazwaStudenta = "kopiujacy",
            Hash = "wspolny_hash",
            NazwaPliku = "kopia.cs",
            CzyToKopia = true,
            DataLogowania = DateTime.Now
        };
        db.Zdarzenia.Add(kopia);
        await db.SaveChangesAsync();

        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.Szczegoly(kopia.Id) as ViewResult;
        Assert.Equal("autor.oryginalny", result!.ViewData["OryginalnyAutor"]);
    }
}

// =============================================================================
// TESTY: OstatnieAktywnosci (filtrowanie i paginacja)
// =============================================================================
public class OstatnieAktywnosciTests
{
    private async Task<ApplicationDbContext> SeedDbAsync(string name)
    {
        var db = DbHelper.CreateDb(name);
        db.Zdarzenia.AddRange(
            new ZdarzeniePliku { NazwaStudenta = "adam", NazwaKomputera = "PC-A", NazwaPliku = "a.cs", CzyToKopia = false, DataLogowania = DateTime.Now.AddDays(-1) },
            new ZdarzeniePliku { NazwaStudenta = "basia", NazwaKomputera = "PC-B", NazwaPliku = "b.cs", CzyToKopia = true, DataLogowania = DateTime.Now.AddDays(-2) },
            new ZdarzeniePliku { NazwaStudenta = "adam", NazwaKomputera = "PC-A", NazwaPliku = "c.cs", CzyToKopia = false, DataLogowania = DateTime.Now.AddDays(-3) }
        );
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task OstatnieAktywnosci_BezFiltra_ZwracaWszystkie()
    {
        using var db = await SeedDbAsync(nameof(OstatnieAktywnosci_BezFiltra_ZwracaWszystkie));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.OstatnieAktywnosci() as ViewResult;
        var model = result!.Model as List<ZdarzeniePliku>;

        Assert.Equal(3, model!.Count);
    }

    [Fact]
    public async Task OstatnieAktywnosci_FiltrujPoStudencie_ZwracaTylkoJego()
    {
        using var db = await SeedDbAsync(nameof(OstatnieAktywnosci_FiltrujPoStudencie_ZwracaTylkoJego));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.OstatnieAktywnosci(student: "basia") as ViewResult;
        var model = result!.Model as List<ZdarzeniePliku>;

        Assert.Single(model!);
        Assert.Equal("basia", model[0].NazwaStudenta);
    }

    [Fact]
    public async Task OstatnieAktywnosci_FiltrujPoStatusieKopia_ZwracaTylkoKopie()
    {
        using var db = await SeedDbAsync(nameof(OstatnieAktywnosci_FiltrujPoStatusieKopia_ZwracaTylkoKopie));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.OstatnieAktywnosci(status: "kopia") as ViewResult;
        var model = result!.Model as List<ZdarzeniePliku>;

        Assert.All(model!, z => Assert.True(z.CzyToKopia));
    }

    [Fact]
    public async Task OstatnieAktywnosci_FiltrujPoKomputerze_ZwracaTylkoTego()
    {
        using var db = await SeedDbAsync(nameof(OstatnieAktywnosci_FiltrujPoKomputerze_ZwracaTylkoTego));
        var (hub, _) = HubMockHelper.Create();
        var ctrl = new NadzorController(db, hub.Object);

        var result = await ctrl.OstatnieAktywnosci(komputer: "PC-B") as ViewResult;
        var model = result!.Model as List<ZdarzeniePliku>;

        Assert.Single(model!);
        Assert.Equal("PC-B", model[0].NazwaKomputera);
    }
}