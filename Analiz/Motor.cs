using System.Diagnostics;
using MaiAnaliz.Satranc;

namespace MaiAnaliz.Analiz;

/// <summary>
/// Sıra gelen taraf açısından motor skoru: santipiyon ya da mat (Mat = n hamlede).
/// Mat 0 = oyun matla bitti; kimin lehine olduğu Cp'nin işaretinde (-1: sıra gelen mat oldu).
/// </summary>
public readonly record struct Skor(int Cp, int? Mat)
{
    public const int MatCp = 100000;

    /// <summary>Mat skorunu sayıya çevirir (n hamlede mat = 100000 - n).</summary>
    public int Sayi => Mat switch
    {
        0 => Cp >= 0 ? MatCp : -MatCp,
        int m => m > 0 ? MatCp - m : -MatCp - m,
        _ => Cp
    };

    public Skor Ters => new(-Cp, Mat is int m ? -m : null);

    /// <summary>Sıra gelen taraf mat olmuş.</summary>
    public static Skor Matta => new(-1, 0);

    /// <summary>Beyaz açısından metin: +1.25, -0.40, M3, -M2, #.</summary>
    public string Metin(bool beyazSirada)
    {
        var b = beyazSirada ? this : Ters;
        return b.Mat switch
        {
            0 => "#",
            int m => (m > 0 ? "M" : "-M") + Math.Abs(m),
            _ => (b.Cp / 100.0).ToString("+0.00;-0.00;+0.00", System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}

/// <summary>Bir UCI motorunu (MaiEngine) ayrı süreçte çalıştırır.</summary>
public sealed class Motor : IDisposable
{
    private readonly Process _surec;
    private readonly StreamWriter _giris;
    private readonly StreamReader _cikis;

    public Motor(string yol, int threads, int hashMb)
    {
        var bilgi = new ProcessStartInfo(yol)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(yol))!,
        };
        _surec = Process.Start(bilgi) ?? throw new InvalidOperationException($"Motor başlatılamadı: {yol}");
        _surec.ErrorDataReceived += (_, _) => { };
        _surec.BeginErrorReadLine();
        _giris = _surec.StandardInput;
        _giris.AutoFlush = true;
        _cikis = _surec.StandardOutput;

        Gonder("uci");
        Bekle("uciok");
        Gonder($"setoption name Threads value {threads}");
        Gonder($"setoption name Hash value {hashMb}");
        Hazir();
    }

    private void Gonder(string komut) => _giris.WriteLine(komut);

    private string Bekle(string baslangic)
    {
        while (true)
        {
            string? satir = _cikis.ReadLine();
            if (satir == null) throw new InvalidOperationException("Motor beklenmedik şekilde kapandı.");
            if (satir.Contains("HATA")) throw new InvalidOperationException("Motor: " + satir);
            if (satir.StartsWith(baslangic)) return satir;
        }
    }

    private void Hazir() { Gonder("isready"); Bekle("readyok"); }

    public void YeniOyun() { Gonder("ucinewgame"); Hazir(); }

    /// <summary>Pozisyonu analiz eder; sıra gelen taraf açısından skor ve ana devam.</summary>
    public (Skor skor, List<Hamle> pv) Analiz(Tahta tahta, int sureMs, int? derinlik, IReadOnlyList<Hamle>? kokHamleler = null)
    {
        Gonder("position fen " + tahta.Fen());
        string go = derinlik is int d ? $"go depth {d}" : $"go movetime {sureMs}";
        if (kokHamleler is { Count: > 0 }) go += " searchmoves " + string.Join(' ', kokHamleler.Select(h => h.Uci));
        Gonder(go);

        Skor skor = default;
        var pv = new List<string>();
        while (true)
        {
            string? satir = _cikis.ReadLine() ?? throw new InvalidOperationException("Motor beklenmedik şekilde kapandı.");
            if (satir.StartsWith("bestmove"))
            {
                if (pv.Count == 0)
                {
                    var p = satir.Split(' ');
                    if (p.Length > 1 && p[1] != "0000") pv.Add(p[1]);
                }
                break;
            }
            if (!satir.StartsWith("info ") || !satir.Contains(" score ")) continue;
            var t = satir.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int i = Array.IndexOf(t, "score");
            if (i < 0 || i + 2 >= t.Length) continue;
            skor = t[i + 1] == "mate" ? new Skor(0, int.Parse(t[i + 2])) : new Skor(int.Parse(t[i + 2]), null);
            int pvi = Array.IndexOf(t, "pv");
            if (pvi >= 0) pv = t.Skip(pvi + 1).ToList();
        }

        // PV'yi yasal hamlelere çevir (yasal olmayan yerde kes)
        var hamleler = new List<Hamle>();
        var k = tahta.Kopya();
        foreach (var uci in pv)
        {
            var h = k.UciOku(uci);
            if (h.BosMu || !k.YasalMi(h)) break;
            hamleler.Add(h);
            k.Uygula(h);
        }
        return (skor, hamleler);
    }

    public void Dispose()
    {
        try
        {
            Gonder("quit");
            if (!_surec.WaitForExit(2000)) _surec.Kill();
        }
        catch { /* zaten kapanmış */ }
        _surec.Dispose();
    }
}
