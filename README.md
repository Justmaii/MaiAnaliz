# MaiAnaliz

Satranç oyunlarını **MaiEngine** ile analiz eden, chess.com'daki "Oyun İncelemesi" tarzında
bir araç ve yerel site. PGN'i bırakırsın, her hamle sınıflandırılır:

**Parlak · Harika · En İyi · Mükemmel · İyi · Kitap · Yanlışlık · Hata · Kaçırma · Büyük Hata**

Rapor; animasyonlu tahta, tahta sesleri, değerlendirme çubuğu ve grafiği, iki taraf için
doğruluk yüzdesi ve kötü hamlelerde motorun en iyi hamlesini gösterme içerir.
Elo'ya göre yumuşatma yok: hamleler motor gözüyle değerlendirilir.

## Kurulum (bir kez, macOS / Linux)

Analizi [MaiEngineV3](https://github.com/Justmaii/MaiEngineV3) yapıyor; onu bu deponun
**yanındaki klasöre** indirip derlemek gerekiyor. Gerekenler: `git`, C++20 derleyici
(macOS'ta `xcode-select --install`, Linux'ta `g++` / `clang` ve `make`) ve Python 3.

```sh
# 1. MaiEngine'i indir, derle ve ağ dosyasını koy (98 MB, Stockfish 19 NNUE ağı)
git clone https://github.com/Justmaii/MaiEngineV3.git
cd MaiEngineV3
make
curl -LO https://tests.stockfishchess.org/api/nn/nn-1a298aa575a0.nnue
./maiengine bench        # hatasız düğüm sayısı yazmalı
cd ..

# 2. MaiAnaliz'i indir ve Python ortamını kur
git clone https://github.com/Justmaii/MaiAnaliz.git
cd MaiAnaliz
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
```

Klasör düzeni şöyle olmalı:

```
MaiEngineV3/   (maiengine + nn-1a298aa575a0.nnue)
MaiAnaliz/     (bu depo)
```

Motor başka bir yerdeyse `MAIENGINE=/yol/maiengine` ortam değişkeni ya da `--motor` ile verilebilir.

**Açılış kitabı (isteğe bağlı):** herhangi bir Polyglot kitabını `book.bin` adıyla bu klasöre
(ya da `MaiEngineV3/` içine) koy. Yoksa analiz yine çalışır, sadece "Kitap" sınıfı çıkmaz.

## Kullanım

```sh
.venv/bin/python site.py              # site: http://localhost:8080 (tarayıcı kendiliğinden açılır)
.venv/bin/python analiz.py oyun.pgn   # komut satırı: oyun.analiz.html
```

Sitede PGN'i sürükleyip bırak ya da yapıştır (chess.com'da oyun → Paylaş → PGN).
Kullanıcı adını yazarsan siyahla oynadığın oyunlarda tahta senin tarafından açılır.
Raporlar `analizler/` klasörüne de kaydedilir.

**Kısayollar:** ←/→ hamleler · **B** en iyi hamleyi göster · **F** tahtayı çevir · **M** ses

Analiz derinliği: Hızlı (~0,4 sn/hamle), Normal (~1 sn/hamle), Derin (~3 sn/hamle).
Komut satırında `--sure`, `--derinlik`, `--threads`, `--hash`, `--kitapsiz` seçenekleri var
(`.venv/bin/python analiz.py -h`).

## Sınıflar nasıl belirleniyor

Her pozisyon motorla analiz edilir; hamleden önceki en iyi devamın kazanma olasılığı ile
oynanan hamlenin kazanma olasılığı karşılaştırılır (Lichess'in kazanma olasılığı eğrisi).

| Sınıf | Kural |
|---|---|
| En İyi | motorun en iyi hamlesi (ya da eşdeğeri) |
| Mükemmel / İyi | kazanma şansı kaybı ≤ %2 / ≤ %5 |
| Yanlışlık / Hata / Büyük Hata | kayıp ≤ %10 / ≤ %20 / > %20 |
| Harika | tek iyi hamle: ikinci en iyi hamle en az %20 daha kötü (basit geri almalar, bedava taş almalar, şahtan kaçışlar sayılmaz) |
| Parlak | en iyi hamleyle gelen gerçek taş kurbanı; sonrasında kötü durumda değilsin ve zaten tamamen kazanmış değildin |
| Kaçırma | rakip hata yaptı, kazanma şansın vardı, kaçırdın |
| Kitap | açılış kitabındaki hamle (motor gözünde kötüyse — tuzak hatları — yine hata sayılır) |
| Zorunlu | tek yasal hamle |

Doğruluk, Lichess'in hamle doğruluğu formülüyle hesaplanır (Kitap ve Zorunlu hamleler hariç).
chess.com'un CAPS sistemi Elo'ya göre yumuşattığı için yüzdeler ve hata sayıları farklı çıkabilir.

## Lisans

GPLv3 (bkz. `LICENSE`). Taş görselleri [python-chess](https://github.com/niklasf/python-chess) üzerinden.
