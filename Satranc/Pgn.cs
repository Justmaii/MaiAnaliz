using System.Text;

namespace MaiAnaliz.Satranc;

public sealed class Oyun
{
    public Dictionary<string, string> Basliklar { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string BaslangicFen { get; set; } = Tahta.BaslangicFen;
    public List<Hamle> Hamleler { get; } = new();

    public string Baslik(string ad, string varsayilan) =>
        Basliklar.TryGetValue(ad, out var d) && d.Length > 0 && d != "?" ? d : varsayilan;
}

/// <summary>PGN okuyucu: başlıklar, ana hat hamleleri; yorumlar, varyantlar, NAG'ler atlanır.</summary>
public static class Pgn
{
    public static List<Oyun> Oku(string metin, int enFazla = int.MaxValue)
    {
        var oyunlar = new List<Oyun>();
        Oyun? oyun = null;
        Tahta? tahta = null;
        bool hamleBasladi = false;
        int i = 0;

        void Bitir()
        {
            if (oyun != null && oyun.Hamleler.Count > 0) oyunlar.Add(oyun);
            oyun = null; tahta = null; hamleBasladi = false;
        }

        while (i < metin.Length && oyunlar.Count < enFazla)
        {
            char c = metin[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '[')
            {
                if (hamleBasladi) Bitir();
                int son = metin.IndexOf(']', i);
                if (son < 0) break;
                var satir = metin[(i + 1)..son];
                i = son + 1;
                int bosluk = satir.IndexOf(' ');
                if (bosluk < 0) continue;
                oyun ??= new Oyun();
                string ad = satir[..bosluk].Trim();
                string deger = satir[(bosluk + 1)..].Trim().Trim('"').Replace("\\\"", "\"");
                oyun.Basliklar[ad] = deger;
                if (ad.Equals("FEN", StringComparison.OrdinalIgnoreCase)) oyun.BaslangicFen = deger;
                continue;
            }
            if (c == '{') { int son = metin.IndexOf('}', i); i = son < 0 ? metin.Length : son + 1; continue; }
            if (c == ';') { int son = metin.IndexOf('\n', i); i = son < 0 ? metin.Length : son + 1; continue; }
            if (c == '(')
            {
                int derinlik = 0;
                for (; i < metin.Length; i++)
                {
                    if (metin[i] == '{') { int son = metin.IndexOf('}', i); i = son < 0 ? metin.Length - 1 : son; continue; }
                    if (metin[i] == '(') derinlik++;
                    else if (metin[i] == ')' && --derinlik == 0) { i++; break; }
                }
                continue;
            }

            // Bir sözcük oku
            int bas = i;
            while (i < metin.Length && !char.IsWhiteSpace(metin[i]) && "{}();[".IndexOf(metin[i]) < 0) i++;
            string sozcuk = metin[bas..i];
            if (sozcuk.Length == 0) { i++; continue; }

            if (sozcuk is "1-0" or "0-1" or "1/2-1/2" or "*") { if (oyun != null) Bitir(); continue; }
            if (sozcuk.StartsWith('$')) continue;

            // Hamle numarası: "12." "12..." ya da "12.e4" gibi bitişik yazım
            var temiz = sozcuk;
            int nokta = temiz.LastIndexOf('.');
            if (nokta >= 0 && temiz[..nokta].TrimEnd('.').All(char.IsDigit)) temiz = temiz[(nokta + 1)..];
            if (temiz.Length == 0 || temiz.All(ch => ch == '.' || char.IsDigit(ch))) continue;
            if (temiz.All(ch => ch is '!' or '?')) continue;

            oyun ??= new Oyun();
            tahta ??= new Tahta(oyun.BaslangicFen);
            hamleBasladi = true;
            var hamle = tahta.SanOku(temiz);
            oyun.Hamleler.Add(hamle);
            tahta.Uygula(hamle);
        }
        if (oyunlar.Count < enFazla) Bitir();
        return oyunlar;
    }
}
