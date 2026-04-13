using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

// --- KONFIGURACJA ---
string serwerUrl = "https://localhost:7014/api/nadzor/loguj";
string konfigUrl = "https://localhost:7014/api/nadzor/konfiguracja";
string nazwaStudenta = "Adam Kowalski";

var handler = new HttpClientHandler();
handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
using var client = new HttpClient(handler);

Console.WriteLine("Pobieranie konfiguracji z serwera...");
string sciezkaDoObserwowania = "";

try
{
    var response = await client.GetAsync(konfigUrl);
    if (response.IsSuccessStatusCode)
    {
        var jsonString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonString);
        sciezkaDoObserwowania = doc.RootElement.GetProperty("sciezka").GetString();
    }
    else
    {
        throw new Exception($"Serwer zwrócił kod: {response.StatusCode}");
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"BŁĄD KRYTYCZNY: Nie można pobrać konfiguracji.");
    Console.WriteLine($"Szczegóły: {ex.Message}");
    Console.ResetColor();
    Console.ReadLine();
    return;
}

if (string.IsNullOrEmpty(sciezkaDoObserwowania) || !Directory.Exists(sciezkaDoObserwowania))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"BŁĄD: Folder [{sciezkaDoObserwowania}] nie istnieje!");
    Console.ResetColor();
    Console.ReadLine();
    return;
}

Console.WriteLine($"--- MONITORING URUCHOMIONY: {sciezkaDoObserwowania} ---");

using var watcher = new FileSystemWatcher(sciezkaDoObserwowania);
watcher.Filter = "*.*";
watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

// Zdarzenia
watcher.Changed += async (s, e) => await WyslijPlik(e.FullPath);
watcher.Created += async (s, e) => await WyslijPlik(e.FullPath);
watcher.EnableRaisingEvents = true;

Console.WriteLine("Działam... Naciśnij [Enter], aby wyłączyć.");
Console.ReadLine();

async Task WyslijPlik(string sciezka)
{
    try
    {
        // Krótkie czekanie, aż edytor (np. VS Code) puści plik po zapisie
        await Task.Delay(600);

        if (!File.Exists(sciezka)) return;

        // Bezpieczne czytanie pliku (nawet jeśli jest otwarty)
        string tresc;
        using (var stream = new FileStream(sciezka, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            tresc = await reader.ReadToEndAsync();
        }

        string nazwaPliku = Path.GetFileName(sciezka);
        string hash = ObliczHash(tresc);

        var dane = new
        {
            NazwaStudenta = nazwaStudenta,
            NazwaPliku = nazwaPliku,
            Tresc = tresc,
            Hash = hash,
            DataLogowania = DateTime.Now
        };

        var response = await client.PostAsJsonAsync(serwerUrl, dane);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Przesłano: {nazwaPliku}");

            // Sprawdzenie czy serwer wykrył duplikat (ALERT)
            if (result.TryGetProperty("alert", out var alertProp) && alertProp.GetBoolean())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("!!! UWAGA: System wykrył identyczny plik u innego studenta !!!");
                Console.ResetColor();
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Błąd wysyłki: {ex.Message}");
    }
}

string ObliczHash(string input)
{
    using SHA256 sha256 = SHA256.Create();
    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
    return Convert.ToHexString(bytes);
}