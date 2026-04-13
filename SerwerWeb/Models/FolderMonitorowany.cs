using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace webserwer.Models;

public class FolderMonitorowany
{
    public int Id { get; set; }
    public string Sciezka { get; set; }
    public string NazwaWyswietlana { get; set; } // np. "Zadanie 1 - Algorytmy"
}
