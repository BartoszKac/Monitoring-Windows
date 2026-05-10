// =============================================
// PLIK: Models/ApplicationDbContext.cs  (ZASTĄP)
// =============================================
using Microsoft.EntityFrameworkCore;

namespace webserwer.Models;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<ZdarzeniePliku> Zdarzenia { get; set; }
    public DbSet<FolderMonitorowany> FolderyMonitorowane { get; set; }  // globalne (fallback)
    public DbSet<CzarnaLista> CzarnaLista { get; set; }
    public DbSet<Komputer> Komputery { get; set; }
    public DbSet<FolderKomputera> FolderyKomputerow { get; set; }
}