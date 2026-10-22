using System.Reflection;
using System.Text.Json;
using MaiAnaliz.Analiz;

namespace MaiAnaliz.Rapor;

/// <summary>Gömülü HTML şablonlarını yükler ve analiz verisiyle raporu üretir.</summary>
public static class RaporHtml
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Sablon(string ad)
    {
        var asm = Assembly.GetExecutingAssembly();
        string kaynak = asm.GetManifestResourceNames().Single(n => n.EndsWith("Sablonlar." + ad));
        using var s = asm.GetManifestResourceStream(kaynak)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static readonly Lazy<Dictionary<string, string>> TasResimleri = new(() =>
    {
        var sonuc = new Dictionary<string, string>();
        foreach (char c in "PNBRQKpnbrqk")
        {
            string dosya = (char.IsUpper(c) ? "w" : "b") + char.ToUpperInvariant(c) + ".svg";
            string svg = Sablon("taslar." + dosya);
            sonuc[c.ToString()] = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
        }
        return sonuc;
    });

    public static string Uret(List<OyunRaporu> oyunlar, string baslik, string? geri = null)
    {
        var veri = new
        {
            oyunlar,
            siniflar = Analizci.Siniflar.ToDictionary(s => s.Anahtar, s => new[] { s.Ad, s.Simge, s.Renk }),
            sira = Analizci.Siniflar.Select(s => s.Anahtar).ToArray(),
            taslar = TasResimleri.Value,
        };
        string geriHtml = geri != null ? $"<a class=\"geri\" href=\"{geri}\">← Yeni analiz</a>" : "";
        return Sablon("rapor.html")
            .Replace("__GERI__", geriHtml)
            .Replace("__BASLIK__", JsonSerializer.Serialize(baslik, Json))
            .Replace("__VERI__", JsonSerializer.Serialize(veri, Json));
    }

    public static string DosyaAdi(OyunRaporu o)
    {
        string ad = $"{o.Beyaz}-vs-{o.Siyah}-{DateTime.Now:yyyyMMdd-HHmmss}";
        ad = System.Text.RegularExpressions.Regex.Replace(ad, @"[^\w.-]+", "_");
        return (ad.Length > 120 ? ad[..120] : ad) + ".analiz.html";
    }
}
