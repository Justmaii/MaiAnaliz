namespace MaiAnaliz.Satranc;

/// <summary>
/// Polyglot (.bin) açılış kitabı. Dosya anahtara göre sıralı 16 baytlık kayıtlardan
/// oluşur (8 bayt anahtar, 2 bayt hamle, 2 bayt ağırlık, 4 bayt öğrenme; big-endian),
/// o yüzden belleğe almadan ikili aramayla okunur.
/// </summary>
public sealed class Kitap : IDisposable
{
    private const int KayitBoyu = 16;
    private readonly FileStream _dosya;
    private readonly long _kayitSayisi;
    private readonly object _kilit = new();

    public Kitap(string yol)
    {
        _dosya = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.Read);
        _kayitSayisi = _dosya.Length / KayitBoyu;
    }

    public static Kitap? Ac(string? yol) => yol != null && File.Exists(yol) ? new Kitap(yol) : null;

    private (ulong anahtar, ushort hamle) Oku(long i)
    {
        Span<byte> b = stackalloc byte[KayitBoyu];
        _dosya.Seek(i * KayitBoyu, SeekOrigin.Begin);
        _dosya.ReadExactly(b);
        ulong anahtar = 0;
        for (int k = 0; k < 8; k++) anahtar = (anahtar << 8) | b[k];
        return (anahtar, (ushort)((b[8] << 8) | b[9]));
    }

    /// <summary>Pozisyon için kitaptaki hamleler (UCI biçiminde, rok e1g1 gibi).</summary>
    public List<Hamle> Hamleler(Tahta tahta)
    {
        ulong anahtar = Anahtar(tahta);
        var sonuc = new List<Hamle>();
        lock (_kilit)
        {
            long dusuk = 0, yuksek = _kayitSayisi - 1, ilk = -1;
            while (dusuk <= yuksek)
            {
                long orta = (dusuk + yuksek) / 2;
                var a = Oku(orta).anahtar;
                if (a < anahtar) dusuk = orta + 1;
                else { if (a == anahtar) ilk = orta; yuksek = orta - 1; }
            }
            if (ilk < 0) return sonuc;
            for (long i = ilk; i < _kayitSayisi; i++)
            {
                var (a, kod) = Oku(i);
                if (a != anahtar) break;
                sonuc.Add(Coz(tahta, kod));
            }
        }
        return sonuc;
    }

    public bool PozisyonVar(Tahta tahta) => Hamleler(tahta).Count > 0;

    /// <summary>Polyglot hamle kodu: 0-2 varış sütunu, 3-5 varış sırası, 6-8 kalkış sütunu,
    /// 9-11 kalkış sırası, 12-14 terfi. Rok "şah kendi kalesini alır" diye kodlanır.</summary>
    private static Hamle Coz(Tahta tahta, ushort kod)
    {
        int kime = Kare.Yap(kod & 7, (kod >> 3) & 7);
        int kimden = Kare.Yap((kod >> 6) & 7, (kod >> 9) & 7);
        int terfi = (kod >> 12) & 7;
        if (Tas.Tur(tahta[kimden]) == Tas.Sah)
        {
            if (kimden == 4 && kime == 7) kime = 6;
            else if (kimden == 4 && kime == 0) kime = 2;
            else if (kimden == 60 && kime == 63) kime = 62;
            else if (kimden == 60 && kime == 56) kime = 58;
        }
        return new Hamle(kimden, kime, terfi == 0 ? 0 : terfi + 1);
    }

    public static ulong Anahtar(Tahta tahta)
    {
        var r = PolyglotRastgele.Values;
        ulong anahtar = 0;
        foreach (var (kare, tas) in tahta.Taslar())
        {
            // Polyglot sırası: siyah piyon 0, beyaz piyon 1, siyah at 2, ...
            int tur = 2 * (Tas.Tur(tas) - 1) + (tas > 0 ? 1 : 0);
            anahtar ^= r[PolyglotRastgele.PieceOffset + 64 * tur + kare];
        }
        int rok = tahta.Rok;
        if ((rok & Tahta.BeyazKisa) != 0) anahtar ^= r[PolyglotRastgele.CastleOffset + 0];
        if ((rok & Tahta.BeyazUzun) != 0) anahtar ^= r[PolyglotRastgele.CastleOffset + 1];
        if ((rok & Tahta.SiyahKisa) != 0) anahtar ^= r[PolyglotRastgele.CastleOffset + 2];
        if ((rok & Tahta.SiyahUzun) != 0) anahtar ^= r[PolyglotRastgele.CastleOffset + 3];

        // Geçerken kare yalnızca yanında alabilecek bir piyon varsa sayılır
        int ep = tahta.GecerkenKare;
        if (ep >= 0)
        {
            int sutun = Kare.Sutun(ep), sira = tahta.BeyazMi ? 4 : 3;
            foreach (int ds in new[] { -1, 1 })
            {
                int s = sutun + ds;
                if (s is >= 0 and < 8 && tahta[Kare.Yap(s, sira)] == Tas.Piyon * tahta.Sira)
                {
                    anahtar ^= r[PolyglotRastgele.EnPassantOffset + sutun];
                    break;
                }
            }
        }
        if (tahta.BeyazMi) anahtar ^= r[PolyglotRastgele.TurnOffset];
        return anahtar;
    }

    public void Dispose() => _dosya.Dispose();
}
