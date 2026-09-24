# RFID Converter — 125 kHz → format `FFF,CCCCC`

Converteste ID-ul brut citit de un RFID reader de **125 kHz** in formatul
cu facility code + numar card:

```
4217212212  →  093,36148
```

## Structura proiectului

| Fisier             | Descriere                                                        |
|--------------------|------------------------------------------------------------------|
| `RfidConverter/`   | Codul sursa C# al aplicatiei (WinForms, .NET 8)                  |
| `icon.ico`         | Iconita aplicatiei (incorporata in `.exe` la compilare)          |
| `.github/workflows/build.yml` | Build automat `.exe` via GitHub Actions               |
| `convert_rfid.ps1` | Varianta linie de comanda (PowerShell)                           |
| `convert_rfid.py`  | Varianta linie de comanda (Python, necesita interpretor instalat)|

## Pornire rapida (Windows)

1. Descarca **`RfidConverter.exe`** din pagina **Releases** a repo-ului —
   un singur fisier (~154 MB), aplicatia standalone (include runtime-ul
   .NET, merge pe orice Windows 10/11 64-bit fara nimic instalat; la prima
   pornire dureaza cateva secunde — extrage bibliotecile native).
2. Dublu-click pe `.exe` si scaneaza cardul cu reader-ul:
   - **Reader HID** (tasteaza automat): pune cursorul in campul *ID brut*,
     scaneaza — conversia se face singura la Enter.
   - **Reader serial** (port COM): alege portul + baud rate (uzual 9600),
     apasa *Conectare* — fiecare card scanat apare automat in istoric.
3. Butonul **Copiaza** pune rezultatul in clipboard, **Export CSV** salveaza
   istoricul cu coloanele `data,numar_125,numar_13_5` (ex: `2026-09-24 10:15:00,4217212212,093,36148`).

## Build automat (GitHub Actions)

`RfidConverter.exe` (~154 MB) **nu este comis in repo** — GitHub refuza
fisierele peste 100 MB. In schimb, workflow-ul `.github/workflows/build.yml`
compileaza automat `.exe`-ul:

- la fiecare `git push` pe `main`/`master` → build + verificare
- la tag `v*` (ex: `git tag v1.0.1; git push origin v1.0.1`) → `.exe`-ul se
  ataseaza automat la GitHub Release
- manual: tab-ul **Actions** → *Build EXE* → *Run workflow*
- descarcare `.exe`: pagina **Releases** a repo-ului (cate un Release
  la fiecare tag `v*`)

Compilare locala (necesita .NET 8 SDK: `winget install Microsoft.DotNet.SDK.8`):

```powershell
dotnet publish RfidConverter -c Release -o dist
```

## Variante linie de comanda

### PowerShell

```powershell
.\convert_rfid.ps1 4217212212        # o valoare  ->  093,36148
.\convert_rfid.ps1                    # mod interactiv
.\convert_rfid.ps1 -Serial COM3       # citire continua de pe port serial
.\convert_rfid.ps1 -Serial COM3 -Baud 9600
```

Pentru a afla portul reader-ului:

```powershell
[System.IO.Ports.SerialPort]::GetPortNames()
# sau: Get-PnpDevice -Class Ports -PresentOnly
```

### Python

```powershell
python convert_rfid.py 4217212212
python convert_rfid.py                # mod interactiv
python convert_rfid.py --watch COM3   # necesita: pip install pyserial
```

## Cum functioneaza conversia

Valoarea bruta este un numar pe 32 de biti:

```
4217212212 = 0xFB 5D 8D34
                  ──  ──────
                0x5D   0x8D34
                 = 93   = 36148
```

- **facility** = `(valoare >> 16) AND 0xFF` → `93` → afisat `093`
- **card** = `valoare AND 0xFFFF` → `36148`

Rezultat: `093,36148`

## Nota importanta: 125 kHz vs 13.56 MHz

Conversia frecventei **nu se poate face prin software**. Frecventa este o
proprietate fizica a reader-ului si a cardului:

- **125 kHz** (LF) — carduri EM4100, HID Prox, T5577
- **13.56 MHz** (HF) — carduri MIFARE, NTAG, iCLASS

Un reader de 125 kHz nu poate citi carduri de 13.56 MHz si invers. Acest
proiect converteste **formatul datelor** (ceea ce citeste reader-ul actual).
Pentru carduri de 13.56 MHz este necesar un reader compatibil (separat sau
dual-band 125 kHz + 13.56 MHz) — datele citite se pot converti apoi cu
aceleasi unelte, daca folosesc acelasi format pe 32 de biti.
