using System.Collections.Concurrent;
using System.Threading.Channels;
using MaiAnaliz.Analiz;
using MaiAnaliz.Rapor;
using MaiAnaliz.Satranc;

namespace MaiAnaliz.Site;

/// <summary>
/// Yerel analiz sitesi: PGN'i bırak, ilerlemeyi izle, raporu aç.
/// Analizler tek bir motor sürecinde sırayla yapılır.
/// </summary>
public static class AnalizSitesi
{
    private const int MaksOyun = 20;
    private static readonly Dictionary<string, int> Modlar = new() { ["hizli"] = 200, ["normal"] = 500, ["derin"] = 1500 };

    private sealed class Is
    {
        public required string Mod { get; init; }
        public List<Oyun>? Oyunlar { get; set; }
        public int OyunSayisi { get; init; }
        public volatile string Durum = "sirada";
        public string? Hata;
        public int Konum, Toplam, OyunNo;
        public string? Html;
        public Dictionary<string, object?>? Ozet;
        public DateTime Zaman;
    }

    private static readonly ConcurrentDictionary<string, Is> Isler = new();
    private static readonly Channel<string> Sira = Channel.CreateUnbounded<string>();
    private static readonly ConcurrentQueue<string> Bekleyenler = new();

    public static async Task Calistir(Ayarlar ayarlar, string? kitapYolu, string kayitDizini, int port, bool tarayiciAc)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower);
        var app = builder.Build();

        string anaSayfa = RaporHtml.Sablon("ana.html");
        app.MapGet("/", () => Results.Content(anaSayfa, "text/html; charset=utf-8"));

        app.MapGet("/gecmis", () => Isler
            .Where(x => x.Value.Durum == "bitti")
            .OrderByDescending(x => x.Value.Zaman)
            .Take(12)
            .Select(x => new Dictionary<string, object?>(x.Value.Ozet!) { ["id"] = x.Key })
            .ToList());

        app.MapGet("/durum/{id}", (string id) =>
        {
            if (!Isler.TryGetValue(id, out var i)) return Results.NotFound(new { hata = "iş yok" });
            var bekleyen = Bekleyenler.ToList();
            return Results.Json(new
            {
                durum = i.Durum, hata = i.Hata, konum = i.Konum, toplam = i.Toplam,
                oyun_no = i.OyunNo, oyun_sayisi = i.OyunSayisi,
                sirada = bekleyen.IndexOf(id) + 1,
            });
        });

        app.MapGet("/rapor/{id}", (string id) =>
            Isler.TryGetValue(id, out var i) && i.Durum == "bitti"
                ? Results.Content(i.Html!, "text/html; charset=utf-8")
                : Results.NotFound("Rapor bulunamadı"));

        app.MapPost("/analiz", async (HttpRequest istek) =>
        {
            if (istek.ContentLength > 5_000_000) return Results.Json(new { hata = "Dosya çok büyük" }, statusCode: 413);
            List<Oyun> oyunlar;
            string mod;
            try
            {
                var govde = await istek.ReadFromJsonAsync<Dictionary<string, string>>() ?? new();
                mod = govde.TryGetValue("mod", out var m) && Modlar.ContainsKey(m) ? m : "normal";
                oyunlar = Pgn.Oku(govde.GetValueOrDefault("pgn", ""), MaksOyun);
            }
            catch (Exception e)
            {
                return Results.BadRequest(new { hata = "PGN okunamadı: " + e.Message });
            }
            if (oyunlar.Count == 0) return Results.BadRequest(new { hata = "PGN'de hamlesi olan bir oyun bulunamadı." });

            string id = Guid.NewGuid().ToString("N")[..10];
            Isler[id] = new Is { Mod = mod, Oyunlar = oyunlar, OyunSayisi = oyunlar.Count };
            Bekleyenler.Enqueue(id);
            await Sira.Writer.WriteAsync(id);
            return Results.Json(new { id });
        });

        _ = Task.Run(() => Isci(ayarlar, kitapYolu, kayitDizini));

        string adres = $"http://localhost:{port}";
        Console.WriteLine($"MaiAnaliz: {adres}  (kapatmak için Ctrl+C)");
        if (tarayiciAc) app.Lifetime.ApplicationStarted.Register(() => TarayicidaAc(adres));
        await app.RunAsync();
    }

    private static async Task Isci(Ayarlar ayarlar, string? kitapYolu, string kayitDizini)
    {
        using var kitap = Kitap.Ac(kitapYolu);
        Analizci? analizci = null;
        await foreach (var id in Sira.Reader.ReadAllAsync())
        {
            Bekleyenler.TryDequeue(out _);
            var i = Isler[id];
            try
            {
                analizci ??= new Analizci(ayarlar, kitap);
                analizci.Ayarlar = ayarlar with { SureMs = Modlar[i.Mod], Derinlik = null };
                i.Durum = "calisiyor";
                var sonuclar = new List<OyunRaporu>();
                int no = 0;
                foreach (var oyun in i.Oyunlar!)
                {
                    i.OyunNo = ++no;
                    var o = analizci.OyunuAnalizEt(oyun, (k, n) => { i.Konum = k + 1; i.Toplam = n; });
                    o.No = no;
                    sonuclar.Add(o);
                }
                var ilk = sonuclar[0];
                i.Html = RaporHtml.Uret(sonuclar, $"{ilk.Beyaz} – {ilk.Siyah}", geri: "/");
                i.Ozet = new Dictionary<string, object?>
                {
                    ["beyaz"] = ilk.Beyaz, ["siyah"] = ilk.Siyah, ["sonuc"] = ilk.Sonuc,
                    ["dogruluk"] = ilk.Dogruluk, ["oyun_sayisi"] = sonuclar.Count,
                };
                Directory.CreateDirectory(kayitDizini);
                await File.WriteAllTextAsync(Path.Combine(kayitDizini, RaporHtml.DosyaAdi(ilk)), i.Html);
                i.Zaman = DateTime.UtcNow;
                i.Durum = "bitti";
            }
            catch (Exception e)
            {
                i.Hata = e.Message;
                i.Durum = "hata";
                analizci?.Dispose();   // motor çöktüyse bir sonraki işte yeniden başlat
                analizci = null;
            }
            finally
            {
                i.Oyunlar = null;
            }
        }
    }

    private static void TarayicidaAc(string adres)
    {
        try
        {
            string komut = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsWindows() ? "explorer" : "xdg-open";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(komut, adres) { UseShellExecute = false });
        }
        catch { /* tarayıcı açılamazsa adres zaten yazıldı */ }
    }
}
