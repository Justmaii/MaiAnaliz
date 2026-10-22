using MaiAnaliz;
using MaiAnaliz.Analiz;
using MaiAnaliz.Rapor;
using MaiAnaliz.Satranc;
using MaiAnaliz.Site;

// MaiAnaliz — MaiEngine ile chess.com tarzı oyun incelemesi.
//   dotnet run -c Release                      site (http://localhost:8080)
//   dotnet run -c Release -- analiz oyun.pgn   komut satırı: oyun.analiz.html

string kok = ProjeKoku();
var secenek = Secenekler(args.Skip(args.Length > 0 && !args[0].StartsWith("--") ? 1 : 0).ToArray());
string komut = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "site";

string motor = secenek.GetValueOrDefault("motor") ?? MotorBul(kok);
if (komut is "site" or "analiz" && !File.Exists(motor))
{
    Console.Error.WriteLine($"Motor bulunamadı: {motor}");
    Console.Error.WriteLine("MaiEngineV3'ü bu klasörün yanına indirip 'make' ile derle, ya da --motor / MAIENGINE ile yolunu ver.");
    return 1;
}
string? kitap = secenek.ContainsKey("kitapsiz") ? null : secenek.GetValueOrDefault("kitap") ?? KitapBul(kok);
var ayarlar = new Ayarlar(
    motor,
    SureMs: (int)(double.Parse(secenek.GetValueOrDefault("sure") ?? "0.5", System.Globalization.CultureInfo.InvariantCulture) * 1000),
    Derinlik: secenek.TryGetValue("derinlik", out var d) ? int.Parse(d!) : null,
    Threads: int.Parse(secenek.GetValueOrDefault("threads") ?? Math.Max(1, Environment.ProcessorCount / 2).ToString()),
    HashMb: int.Parse(secenek.GetValueOrDefault("hash") ?? "256"),
    Hizli: secenek.ContainsKey("hizli"));

switch (komut)
{
    case "site":
        await AnalizSitesi.Calistir(ayarlar, kitap, Path.Combine(kok, "analizler"),
            int.Parse(secenek.GetValueOrDefault("port") ?? "8080"), tarayiciAc: !secenek.ContainsKey("acma"));
        return 0;

    case "analiz":
        return KomutSatiri(args.Length > 1 ? args[1] : null, ayarlar, kitap, secenek);

    case "perft":   // geliştirici testi: hamle üretimi ve SAN doğrulaması
        return Testler.Perft(args[1]);

    default:
        Console.WriteLine("Kullanım: MaiAnaliz [site] [--port 8080] | analiz oyun.pgn [-o rapor.html] [--sure 0.5]");
        Console.WriteLine("          [--derinlik N] [--threads N] [--hash MB] [--oyunlar 1,3] [--hizli] [--kitapsiz] [--motor yol]");
        return komut is "-h" or "--help" or "yardim" ? 0 : 1;
}

static int KomutSatiri(string? pgnYolu, Ayarlar ayarlar, string? kitapYolu, Dictionary<string, string?> secenek)
{
    if (pgnYolu == null || !File.Exists(pgnYolu)) { Console.Error.WriteLine("PGN dosyası bulunamadı."); return 1; }
    var secili = secenek.GetValueOrDefault("oyunlar")?.Split(',').Select(int.Parse).ToHashSet();
    var oyunlar = Pgn.Oku(File.ReadAllText(pgnYolu));

    using var kitap = Kitap.Ac(kitapYolu);
    using var analizci = new Analizci(ayarlar, kitap);
    var sonuclar = new List<OyunRaporu>();
    for (int no = 1; no <= oyunlar.Count; no++)
    {
        if (secili != null && !secili.Contains(no)) continue;
        var basla = DateTime.Now;
        int n = no;
        var o = analizci.OyunuAnalizEt(oyunlar[no - 1], (k, t) => Console.Write($"\r  Oyun {n}: adım {k + 1}/{t}"));
        o.No = no;
        Console.Write($"  ({(DateTime.Now - basla).TotalSeconds:0} sn)");
        TerminalOzeti(o);
        sonuclar.Add(o);
    }
    if (sonuclar.Count == 0) { Console.Error.WriteLine("PGN'de analiz edilecek oyun yok."); return 1; }

    string cikti = secenek.GetValueOrDefault("o") ?? Path.ChangeExtension(pgnYolu, null) + ".analiz.html";
    File.WriteAllText(cikti, RaporHtml.Uret(sonuclar, Path.GetFileName(pgnYolu)));
    Console.WriteLine($"\nRapor: {cikti}");
    return 0;
}

static void TerminalOzeti(OyunRaporu o)
{
    Console.WriteLine($"\n  {o.Beyaz} vs {o.Siyah}  {o.Sonuc}");
    Console.WriteLine($"  Doğruluk: Beyaz {o.Dogruluk["w"]:0.0}  Siyah {o.Dogruluk["b"]:0.0}");
    foreach (var s in Analizci.Siniflar)
    {
        int b = o.Sayac["w"][s.Anahtar], w = o.Sayac["b"][s.Anahtar];
        if (b > 0 || w > 0) Console.WriteLine($"    {s.Ad,-11} {b,3}  {w,3}");
    }
    foreach (var p in o.Plylar.Where(p => p.Sinif is "parlak" or "harika" or "kacirma" or "hata" or "buyukhata"))
    {
        string nokta = p.Renk == "w" ? "." : "...";
        string ad = Analizci.Siniflar.First(s => s.Anahtar == p.Sinif).Ad;
        string ek = p.Sinif is "parlak" or "harika" ? "" : $"  (en iyisi {p.EniyiSan})";
        Console.WriteLine($"    {p.No}{nokta}{p.San,-8} {ad}{ek}");
    }
}

static Dictionary<string, string?> Secenekler(string[] a)
{
    var s = new Dictionary<string, string?>();
    for (int i = 0; i < a.Length; i++)
    {
        if (!a[i].StartsWith('-')) continue;
        string ad = a[i].TrimStart('-');
        bool deger = i + 1 < a.Length && !a[i + 1].StartsWith("--") && ad is not ("hizli" or "kitapsiz" or "acma");
        s[ad] = deger ? a[++i] : null;
    }
    return s;
}

// Proje kökü: MaiAnaliz.csproj'un olduğu klasör (dotnet run bin/ altından çalışır)
static string ProjeKoku()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "MaiAnaliz.csproj"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}

// MaiEngine: MAIENGINE ortam değişkeni, proje kökü, ya da yan klasördeki MaiEngineV3
static string MotorBul(string kok)
{
    string ad = OperatingSystem.IsWindows() ? "maiengine.exe" : "maiengine";
    string?[] adaylar =
    {
        Environment.GetEnvironmentVariable("MAIENGINE"),
        Path.Combine(kok, ad),
        Path.Combine(Path.GetDirectoryName(kok)!, "MaiEngineV3", ad),
    };
    return adaylar.FirstOrDefault(a => !string.IsNullOrEmpty(a) && File.Exists(a)) ?? adaylar[2]!;
}

static string? KitapBul(string kok) => new[]
{
    Path.Combine(kok, "book.bin"),
    Path.Combine(Path.GetDirectoryName(kok)!, "MaiEngineV3", "book.bin"),
}.FirstOrDefault(File.Exists);
