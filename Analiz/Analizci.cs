using MaiAnaliz.Satranc;

namespace MaiAnaliz.Analiz;

/// <summary>Analiz ayarları: pozisyon başına süre (ya da sabit derinlik), motor kaynakları.</summary>
public sealed record Ayarlar(string MotorYolu, int SureMs = 500, int? Derinlik = null,
                             int Threads = 4, int HashMb = 256, bool Hizli = false);

// Rapor verisi — alan adları HTML şablonundaki JavaScript'in beklediği snake_case adlara çevrilir.
public sealed class PlyRaporu
{
    public string San { get; set; } = "";
    public string Uci { get; set; } = "";
    public string Renk { get; set; } = "w";
    public int No { get; set; }
    public string Sinif { get; set; } = "";
    public string? EniyiSan { get; set; }
    public string? EniyiUci { get; set; }
    public string? EniyiFen { get; set; }
    public string EniyiSkor { get; set; } = "";
    public List<string> Pv { get; set; } = new();
    public string? Ikinci { get; set; }
    public string Skor { get; set; } = "";
    public double KazanmaBeyaz { get; set; }
    public double Kayip { get; set; }
    public double Dogruluk { get; set; }
    public string Fen { get; set; } = "";
}

public sealed class OyunRaporu
{
    public int No { get; set; }
    public string Beyaz { get; set; } = "Beyaz";
    public string Siyah { get; set; } = "Siyah";
    public string Sonuc { get; set; } = "*";
    public string Etkinlik { get; set; } = "";
    public string Tarih { get; set; } = "";
    public string BaslangicFen { get; set; } = "";
    public double BaslangicKazanma { get; set; }
    public string BaslangicSkor { get; set; } = "";
    public List<PlyRaporu> Plylar { get; set; } = new();
    public Dictionary<string, double?> Dogruluk { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> Sayac { get; set; } = new();
}

/// <summary>
/// chess.com tarzı hamle sınıflandırması. Lichess'in "kazanma olasılığı" modeli:
/// hamleden önceki en iyi devamın kazanma şansı ile oynanan hamleninki arasındaki fark.
/// Elo'ya göre yumuşatma yok — motor gözü.
/// </summary>
public sealed class Analizci : IDisposable
{
    // Sınıf anahtarı -> (görünen ad, simge, renk); sıra rapordaki tablo sırasıdır.
    public static readonly (string Anahtar, string Ad, string Simge, string Renk)[] Siniflar =
    {
        ("parlak",    "Parlak",     "!!", "#1baca6"),
        ("harika",    "Harika",     "!",  "#5c8bb0"),
        ("eniyi",     "En İyi",     "★",  "#81b64c"),
        ("mukemmel",  "Mükemmel",   "👍", "#96bc4b"),
        ("iyi",       "İyi",        "✓",  "#95af8a"),
        ("kitap",     "Kitap",      "📖", "#a88865"),
        ("zorunlu",   "Zorunlu",    "□",  "#97a5a8"),
        ("yanlislik", "Yanlışlık",  "?!", "#f7c631"),
        ("hata",      "Hata",       "?",  "#ffa459"),
        ("kacirma",   "Kaçırma",    "✕",  "#ff7769"),
        ("buyukhata", "Büyük Hata", "??", "#fa412d"),
    };

    private readonly Motor _motor;
    private readonly Kitap? _kitap;
    public Ayarlar Ayarlar { get; set; }

    public Analizci(Ayarlar ayarlar, Kitap? kitap)
    {
        Ayarlar = ayarlar;
        _kitap = kitap;
        _motor = new Motor(ayarlar.MotorYolu, ayarlar.Threads, ayarlar.HashMb);
    }

    public void Dispose() => _motor.Dispose();

    // ---------------------------------------------------------------- formüller

    /// <summary>Santipiyondan kazanma olasılığına (0..1), Lichess formülü.</summary>
    public static double Kazanma(int cp)
    {
        cp = Math.Clamp(cp, -Skor.MatCp, Skor.MatCp);
        return 1 / (1 + Math.Exp(-0.00368208 * cp));
    }

    /// <summary>Kazanma olasılığı düşüşünden hamle doğruluğu (0..100), Lichess formülü.</summary>
    public static double HamleDogrulugu(double once, double sonra)
    {
        double fark = Math.Max(0, once - sonra) * 100;
        return Math.Clamp(103.1668 * Math.Exp(-0.04354 * fark) - 3.1669, 0, 100);
    }

    public static double? OyunDogrulugu(List<double> liste)
    {
        if (liste.Count == 0) return null;
        double aritmetik = liste.Average();
        double harmonik = liste.Count / liste.Sum(a => 1 / Math.Max(a, 1.0));
        return (aritmetik + harmonik) / 2;
    }

    // ---------------------------------------------------------------- taş kurbanı

    /// <summary>Sıra rakipteyken, rakibin bizim piyon dışı taşlarımızdan birini alarak
    /// kazanabileceği en büyük net malzeme (kaba SEE).</summary>
    private static int AsiliKazanc(Tahta tahta, int ben)
    {
        int enBuyuk = 0;
        var yasal = tahta.YasalHamleler();
        foreach (var (kare, tas) in tahta.Taslar())
        {
            if (Tas.Renk(tas) != ben || Tas.Tur(tas) is Tas.Piyon or Tas.Sah) continue;
            var alicilar = yasal.Where(m => m.Kime == kare).ToList();
            if (alicilar.Count == 0) continue;
            int enUcuz = alicilar.Min(m => Tas.Deger(Tas.Tur(tahta[m.Kimden])));
            bool korunuyor = tahta.SaldiriAltinda(kare, ben);
            int kazanc = korunuyor ? Tas.Deger(Tas.Tur(tas)) - enUcuz : Tas.Deger(Tas.Tur(tas));
            enBuyuk = Math.Max(enBuyuk, kazanc);
        }
        return enBuyuk;
    }

    /// <summary>Hamle bir taş kurbanı mı? Hamleden sonra rakip, hamlede aldığımızdan en az
    /// 2 puan fazla malzeme kazanabiliyorsa ve bu durum hamleyle yeni oluştuysa.</summary>
    private static bool KurbanMi(Tahta once, Hamle hamle)
    {
        int ben = once.Sira;
        int alinan = once.GecerkenMi(hamle) ? 1 : Tas.Deger(Tas.Tur(once[hamle.Kime]));
        if (once.RokMu(hamle)) alinan = 0;
        if (hamle.Terfi != 0) alinan += Tas.Deger(hamle.Terfi) - 1;

        var sonra = once.Kopya();
        sonra.Uygula(hamle);
        if (sonra.MatMi()) return false;
        int kazancSonra = AsiliKazanc(sonra, ben);
        int kazancOnce = once.SahCekilmis() ? 0 : AsiliKazanc(once.BosHamle(), ben);
        return kazancSonra - alinan >= 2 && kazancSonra > kazancOnce;
    }

    /// <summary>Korumasız ya da alan taştan değerli bir taşı almak — bulması kolay bir hamle.</summary>
    private static bool BedavaAlis(Tahta tahta, Hamle hamle)
    {
        if (!tahta.AlisMi(hamle) || tahta.GecerkenMi(hamle)) return false;
        int alinan = Tas.Tur(tahta[hamle.Kime]), alan = Tas.Tur(tahta[hamle.Kimden]);
        return !tahta.SaldiriAltinda(hamle.Kime, -tahta.Sira) || Tas.Deger(alinan) > Tas.Deger(alan);
    }

    /// <summary>Hamle kitapta mı? Ya doğrudan kitap hamlesi ya da kitaptaki bir pozisyona geçiş.</summary>
    private bool KitaptaMi(Tahta tahta, Hamle hamle)
    {
        if (_kitap == null) return false;
        if (_kitap.Hamleler(tahta).Contains(hamle)) return true;
        var sonra = tahta.Kopya();
        sonra.Uygula(hamle);
        return _kitap.PozisyonVar(sonra);
    }

    // ---------------------------------------------------------------- analiz

    private sealed record Poz(Skor Skor, Hamle Eniyi, List<Hamle> Pv);

    private Poz PozisyonAnalizi(Tahta tahta, IReadOnlyList<Hamle>? kokHamleler = null)
    {
        if (tahta.MatMi()) return new Poz(Skor.Matta, Hamle.Yok, new());
        if (tahta.PatMi() || tahta.YetersizMalzeme()) return new Poz(new Skor(0, null), Hamle.Yok, new());
        var (skor, pv) = _motor.Analiz(tahta, Ayarlar.SureMs, Ayarlar.Derinlik, kokHamleler);
        return new Poz(skor, pv.Count > 0 ? pv[0] : Hamle.Yok, pv);
    }

    private static List<string> SanDizisi(Tahta tahta, List<Hamle> hamleler, int n = 8)
    {
        var t = tahta.Kopya();
        var sonuc = new List<string>();
        foreach (var h in hamleler.Take(n))
        {
            sonuc.Add(t.San(h));
            t.Uygula(h);
        }
        return sonuc;
    }

    /// <param name="ilerleme">(adım, toplam adım)</param>
    public OyunRaporu OyunuAnalizEt(Oyun oyun, Action<int, int> ilerleme)
    {
        _motor.YeniOyun();
        var tahtalar = new List<Tahta> { new(oyun.BaslangicFen) };
        foreach (var h in oyun.Hamleler)
        {
            var t = tahtalar[^1].Kopya();
            t.Uygula(h);
            tahtalar.Add(t);
        }
        int toplam = 2 * tahtalar.Count - 1;

        // Her pozisyonu bir kez analiz et: hamle i'nin "sonrası", hamle i+1'in "öncesi".
        var pozlar = new List<Poz>();
        for (int i = 0; i < tahtalar.Count; i++)
        {
            ilerleme(i, toplam);
            pozlar.Add(PozisyonAnalizi(tahtalar[i]));
        }

        var plylar = new List<PlyRaporu>();
        string? oncekiSinif = null;
        var gorulen = new HashSet<string>();
        bool kitapta = _kitap != null;

        for (int i = 0; i < oyun.Hamleler.Count; i++)
        {
            ilerleme(tahtalar.Count + i, toplam);
            var once = tahtalar[i];
            var m = oyun.Hamleler[i];
            var pOnce = pozlar[i];
            var pSonra = pozlar[i + 1];
            var eniyi = pOnce.Eniyi;
            int enIyiCp = pOnce.Skor.Sayi;
            int yasal = once.YasalHamleler().Count;

            int oynananCp = m == eniyi ? enIyiCp : -pSonra.Skor.Sayi;
            double kOnce = Kazanma(enIyiCp), kOynanan = Kazanma(oynananCp);
            double kayip = Math.Max(0, kOnce - kOynanan);

            string sinif;
            // Kitap hamlesi motor gözünde kötüyse (tuzak hatları da kitapta) yine hata sayılır.
            kitapta = kitapta && kayip <= 0.05 && KitaptaMi(once, m);
            if (kitapta) sinif = "kitap";
            else if (yasal == 1) sinif = "zorunlu";
            else if (m == eniyi || kayip < 0.005) sinif = "eniyi";
            else if (kayip <= 0.02) sinif = "mukemmel";
            else if (kayip <= 0.05) sinif = "iyi";
            else if (kayip <= 0.10) sinif = "yanlislik";
            else if (kayip <= 0.20) sinif = "hata";
            else sinif = "buyukhata";

            // Kaçırma: rakip hata yaptı, fırsat vardı, sen kaçırdın ama oyun çökmedi.
            if (sinif is "hata" or "buyukhata" && oncekiSinif is "hata" or "buyukhata" or "kacirma"
                && kOnce >= 0.70 && kOynanan >= 0.35)
                sinif = "kacirma";

            // Parlak: en iyi (veya neredeyse) hamle + gerçek bir taş kurbanı,
            // sonrasında kötü durumda değilsin ve zaten tamamen kazanmış değildin.
            if (sinif is "eniyi" or "mukemmel" && kayip <= 0.02 && kOynanan >= 0.45
                && kOnce < 0.90 && KurbanMi(once, m))
                sinif = "parlak";

            // Harika: tek iyi hamle — ikinci en iyi hamle belirgin şekilde daha kötü.
            // Basit geri almalar, şahtan kaçışlar, bedava almalar ve tekrar eden pozisyonlar sayılmaz.
            string? ikinciMetin = null;
            if (sinif == "eniyi" && m == eniyi && !Ayarlar.Hizli && yasal > 1)
            {
                bool geriAlma = i > 0 && once.AlisMi(m) && m.Kime == oyun.Hamleler[i - 1].Kime;
                bool tekrar = gorulen.Contains(once.KonumAnahtari());
                if (!(geriAlma || tekrar || once.SahCekilmis() || BedavaAlis(once, m)))
                {
                    var digerleri = once.YasalHamleler().Where(x => x != m).ToList();
                    var p2 = PozisyonAnalizi(once, digerleri);
                    if (!p2.Eniyi.BosMu)
                        ikinciMetin = once.San(p2.Eniyi) + " " + p2.Skor.Metin(once.BeyazMi);
                    if (kOnce - Kazanma(p2.Skor.Sayi) >= 0.20 && kOnce >= 0.30)
                        sinif = "harika";
                }
            }

            string? eniyiFen = null;
            if (!eniyi.BosMu)
            {
                var t = once.Kopya();
                t.Uygula(eniyi);
                eniyiFen = t.Fen();
            }
            var sonraTahta = tahtalar[i + 1];
            int beyazSonraCp = sonraTahta.BeyazMi ? pSonra.Skor.Sayi : pSonra.Skor.Ters.Sayi;

            plylar.Add(new PlyRaporu
            {
                San = once.San(m),
                Uci = m.Uci,
                Renk = once.BeyazMi ? "w" : "b",
                No = once.HamleNo,
                Sinif = sinif,
                EniyiSan = eniyi.BosMu ? null : once.San(eniyi),
                EniyiUci = eniyi.BosMu ? null : eniyi.Uci,
                EniyiFen = eniyiFen,
                EniyiSkor = pOnce.Skor.Metin(once.BeyazMi),
                Pv = SanDizisi(once, pOnce.Pv),
                Ikinci = ikinciMetin,
                Skor = pSonra.Skor.Metin(sonraTahta.BeyazMi),
                KazanmaBeyaz = Kazanma(beyazSonraCp),
                Kayip = Math.Round(kayip * 100, 1),
                Dogruluk = Math.Round(HamleDogrulugu(kOnce, kOynanan), 1),
                Fen = sonraTahta.Fen(),
            });
            oncekiSinif = sinif;
            gorulen.Add(once.KonumAnahtari());
        }

        var rapor = new OyunRaporu
        {
            Beyaz = oyun.Baslik("White", "Beyaz"),
            Siyah = oyun.Baslik("Black", "Siyah"),
            Sonuc = oyun.Baslik("Result", "*"),
            Etkinlik = oyun.Baslik("Event", ""),
            Tarih = oyun.Baslik("Date", ""),
            BaslangicFen = tahtalar[0].Fen(),
            BaslangicSkor = pozlar[0].Skor.Metin(tahtalar[0].BeyazMi),
            BaslangicKazanma = Kazanma(tahtalar[0].BeyazMi ? pozlar[0].Skor.Sayi : pozlar[0].Skor.Ters.Sayi),
            Plylar = plylar,
        };
        foreach (var renk in new[] { "w", "b" })
        {
            var liste = plylar.Where(p => p.Renk == renk && p.Sinif is not ("zorunlu" or "kitap"))
                              .Select(p => p.Dogruluk).ToList();
            var d = OyunDogrulugu(liste);
            rapor.Dogruluk[renk] = d is double x ? Math.Round(x, 1) : null;
            rapor.Sayac[renk] = Siniflar.ToDictionary(s => s.Anahtar, s => plylar.Count(p => p.Renk == renk && p.Sinif == s.Anahtar));
        }
        return rapor;
    }
}
