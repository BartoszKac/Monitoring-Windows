using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace webserwer.Models;

public class ZdarzeniePliku
{
    [Key]
    public int Id { get; set; }
    public string NazwaStudenta { get; set; } = "";
    public string NazwaPliku { get; set; } = "";
    public string Tresc { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime DataLogowania { get; set; } = DateTime.Now;
    public bool CzyToKopia { get; set; }
}



public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Tabela do logowania plików od studentów
    public DbSet<ZdarzeniePliku> Zdarzenia { get; set; }

    // TWOJA NOWA TABELA DO WYBIERANIA FOLDERÓW
    public DbSet<FolderMonitorowany> FolderyMonitorowane { get; set; }
}