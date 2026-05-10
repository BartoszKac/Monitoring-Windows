namespace webserwer.Models;

public class FolderUzytkownika
{
    public int Id { get; set; }
    public string NazwaStudenta { get; set; } = "";   // do kogo należy
    public string Sciezka { get; set; } = "";
    public string NazwaWyswietlana { get; set; } = "";
    public DateTime DataDodania { get; set; } = DateTime.Now;
}
