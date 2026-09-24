<#
    Converteste ID-ul brut citit de RFID reader (125 kHz) in formatul:
        4217212212  ->  093,36148

    Utilizare:
        .\convert_rfid.ps1 4217212212
        .\convert_rfid.ps1              # mod interactiv
        .\convert_rfid.ps1 -Serial COM3 # citire continua de pe serial
#>

param(
    [Parameter(Position = 0)][string]$Raw,
    [string]$Serial,
    [int]$Baud = 9600
)

function Convert-RfidId([long]$Value) {
    $facility = ($Value -shr 16) -band 0xFF
    $card     = $Value -band 0xFFFF
    return ("{0:D3},{1}" -f $facility, $card)
}

if ($Serial) {
    # citire continua de pe portul serial al reader-ului
    $port = New-Object System.IO.Ports.SerialPort $Serial, $Baud, None, 8, One
    $port.Open()
    Write-Host "Ascult pe $Serial @ $Baud baud... (Ctrl+C oprire)"
    try {
        while ($true) {
            $line = $port.ReadLine().Trim()
            $digits = ($line -replace '[^\d]', '')
            if ($digits) {
                Write-Host ("{0,14}  ->  {1}" -f $line, (Convert-RfidId ([long]$digits)))
            }
        }
    } finally {
        $port.Close()
    }
}
elseif ($Raw) {
    Write-Host (Convert-RfidId ([long]$Raw))
}
else {
    # mod interactiv - lipit de reader-ul care tasteaza automat (HID)
    while ($true) {
        $line = Read-Host "ID brut"
        if (-not $line) { break }
        $digits = ($line -replace '[^\d]', '')
        if ($digits) { Write-Host "Rezultat: $(Convert-RfidId ([long]$digits))" }
    }
}
