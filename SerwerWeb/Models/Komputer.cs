// =============================================
// PLIK: Models/Komputer.cs  (ZASTĄP)
// =============================================
using System.ComponentModel.DataAnnotations;

namespace webserwer.Models;

public class Komputer
{
    [Key]
    public int Id { get; set; }

    public string NazwaKomputera { get; set; } = "";
    public string NazwaStudenta { get; set; } = "";  // kto aktualnie zalogowany
    public string Opis { get; set; } = "";

    public bool Online { get; set; } = false;
    public DateTime OstatnioWidziany { get; set; } = DateTime.Now;
    public DateTime DataDodania { get; set; } = DateTime.Now;

    /// <summary>SignalR ConnectionId – do wysyłania poleceń do konkretnego agenta</summary>
    public string? ConnectionId { get; set; }

    // Nawigacja
    public List<FolderKomputera> Foldery { get; set; } = new();
}
