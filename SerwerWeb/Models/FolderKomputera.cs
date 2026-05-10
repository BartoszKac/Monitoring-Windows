// =============================================
// PLIK: Models/FolderKomputera.cs  (bez zmian)
// =============================================
namespace webserwer.Models;

public class FolderKomputera
{
    public int Id { get; set; }

    public int KomputerId { get; set; }
    public Komputer? Komputer { get; set; }

    public string Sciezka { get; set; } = "";
    public string NazwaWyswietlana { get; set; } = "";
    public DateTime DataDodania { get; set; } = DateTime.Now;
}
