using MaiAnaliz.Satranc;

namespace MaiAnaliz;

/// <summary>Geliştirici testi: perft EPD dosyasıyla hamle üretimi + SAN gidiş-dönüş doğrulaması.</summary>
public static class Testler
{
    public static int Perft(string epd, int enFazlaDerinlik = 3)
    {
        int hata = 0, toplam = 0;
        foreach (var satir in File.ReadLines(epd))
        {
            var p = satir.Split(';');
            var tahta = new Tahta(p[0]);
            foreach (var d in p.Skip(1))
            {
                var x = d.Trim().Split(' ');
                int derinlik = int.Parse(x[0][1..]);
                if (derinlik > enFazlaDerinlik) continue;
                long beklenen = long.Parse(x[1]), bulunan = Say(tahta, derinlik);
                toplam++;
                if (beklenen != bulunan) { hata++; Console.WriteLine($"HATA {p[0]} d{derinlik}: {bulunan} != {beklenen}"); }
            }
        }
        Console.WriteLine($"{toplam - hata}/{toplam} doğru");
        return hata == 0 ? 0 : 1;
    }

    private static long Say(Tahta t, int d)
    {
        if (d == 0) return 1;
        long n = 0;
        foreach (var h in t.YasalHamleler())
        {
            if (t.SanOku(t.San(h)) != h) throw new Exception($"SAN hatası: {t.Fen()} {h}");
            var k = t.Kopya();
            k.Uygula(h);
            n += Say(k, d - 1);
        }
        return n;
    }
}
