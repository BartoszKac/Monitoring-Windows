// =============================================
// PLIK: Models/ZdarzeniePliku.cs  (ZASTĄP)
// =============================================
using System.ComponentModel.DataAnnotations;

namespace webserwer.Models;

public class ZdarzeniePliku
{
    [Key]
    public int Id { get; set; }

    public string NazwaStudenta { get; set; } = "";
    public string NazwaKomputera { get; set; } = "";   // ← NOWE
    public string NazwaPliku { get; set; } = "";
    public string Tresc { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime DataLogowania { get; set; } = DateTime.Now;
    public bool CzyToKopia { get; set; }
}