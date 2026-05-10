using System.ComponentModel.DataAnnotations;

namespace webserwer.Models;

public class CzarnaLista
{
    [Key]
    public int Id { get; set; }
    public string NazwaStudenta { get; set; } = "";
    public string Powod { get; set; } = "";
    public DateTime DataDodania { get; set; } = DateTime.Now;
}
