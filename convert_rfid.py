"""
Converteste ID-ul brut citit de RFID reader (125 kHz) in formatul:
    4217212212  ->  093,36148

Structura valorii pe 32 biti:
    bits 16..23 : facility code
    bits  0..15 : card number

Utilizare:
    python convert_rfid.py 4217212212
    python convert_rfid.py            # citeste de la tastatura
    python convert_rfid.py --watch COM3   # citeste serial continuu
"""

import sys


def convert(raw: int) -> str:
    facility = (raw >> 16) & 0xFF
    card = raw & 0xFFFF
    return f"{facility:03d},{card}"


def watch(port: str, baud: int = 9600) -> None:
    """Citeste continuu de pe portul serial al reader-ului."""
    try:
        import serial  # pip install pyserial
    except ImportError:
        sys.exit("Lipsa pyserial: pip install pyserial")

    ser = serial.Serial(port, baud, timeout=1)
    print(f"Ascult pe {port} @ {baud} baud... (Ctrl+C oprire)")
    while True:
        line = ser.readline().decode(errors="ignore").strip()
        if not line:
            continue
        # reader-ele pot trimite prefixe/sufixi, pastram doar cifrele
        digits = "".join(c for c in line if c.isdigit())
        if not digits:
            continue
        print(f"{line:>14}  ->  {convert(int(digits))}")


if __name__ == "__main__":
    args = [a for a in sys.argv[1:]]

    if args and args[0] == "--watch":
        watch(args[1] if len(args) > 1 else "COM3")
    elif args:
        print(convert(int(args[0])))
    else:
        while True:
            try:
                raw = input("ID brut: ").strip()
            except (EOFError, KeyboardInterrupt):
                break
            if raw:
                print("Rezultat:", convert(int(raw)))
