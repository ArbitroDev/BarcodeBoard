# Convertit LICENSE.md en RTF pour la page de licence de l'installeur.
# La fenêtre d'Inno Setup affiche le texte brut : la syntaxe Markdown (#, **) y reste visible et les lignes
# coupées à ~95 caractères se recoupent dans sa zone étroite. Ici les paragraphes sont rejoints (le texte suit
# la largeur de la fenêtre), les titres et le gras sont mis en forme.
# Appelé par BarcodeBoard.iss à la compilation : license-rtf.ps1 <LICENSE.md> <sortie.rtf>
param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Output)

$ErrorActionPreference = 'Stop'

# Texte → RTF : caractères spéciaux échappés, tout caractère non ASCII en \uN? (lisible quelle que soit la page de code).
function ConvertTo-RtfText([string]$text) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $text.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '\' -or $ch -eq '{' -or $ch -eq '}') { [void]$sb.Append('\').Append($ch) }
        elseif ($code -gt 127) { [void]$sb.Append('\u').Append($(if ($code -gt 32767) { $code - 65536 } else { $code })).Append('?') }
        else { [void]$sb.Append($ch) }
    }
    $sb.ToString()
}

# Mise en forme dans une ligne : **gras**, et [texte](lien) réduit au texte (suivi du lien s'il diffère).
function ConvertTo-RtfInline([string]$text) {
    $text = [regex]::Replace($text, '\[([^\]]+)\]\(([^)]+)\)', { param($m) if ($m.Groups[1].Value -eq $m.Groups[2].Value) { $m.Groups[1].Value } else { "$($m.Groups[1].Value) ($($m.Groups[2].Value))" } })
    $parts = $text -split '\*\*'
    $out = ''
    for ($i = 0; $i -lt $parts.Count; $i++) {
        $piece = ConvertTo-RtfText $parts[$i]
        $out += if ($i % 2 -eq 1) { "{\b $piece}" } else { $piece }
    }
    $out
}

$lines = [System.IO.File]::ReadAllLines($Source, [System.Text.Encoding]::UTF8)

# Blocs séparés par des lignes vides ; les lignes d'un même bloc forment un seul paragraphe.
$blocks = New-Object System.Collections.Generic.List[string[]]
$current = New-Object System.Collections.Generic.List[string]
foreach ($line in $lines + '') {
    if ($line.Trim() -eq '') {
        if ($current.Count) { $blocks.Add($current.ToArray()); $current.Clear() }
    } else { $current.Add($line.Trim()) }
}

$body = New-Object System.Text.StringBuilder
foreach ($block in $blocks) {
    $text = $block -join ' '
    if ($text -match '^-{3,}$') { continue }
    # Syntaxe non convertie (titre de niveau 3, liste à puces ou numérotée, citation, code, italique) : elle
    # resterait brute dans l'installeur. On s'arrête plutôt, et la compilation de l'installeur le signale.
    foreach ($line in $block) {
        if ($line -match '^(#{3,} |[-*+] |\d+\. |>|```)' -or $line.Contains('`') -or ($line -replace '\*\*', '').Contains('*')) {
            [Console]::Error.WriteLine("license-rtf.ps1: Markdown not supported, add it to the conversion: $line")
            exit 1
        }
    }
    if ($text -match '^# (.+)$') { [void]$body.Append("\pard\sa160\b\fs28 $(ConvertTo-RtfInline $Matches[1])\b0\fs18\par`r`n") }
    elseif ($text -match '^## (.+)$') { [void]$body.Append("\pard\sb120\sa80\b\fs22 $(ConvertTo-RtfInline $Matches[1])\b0\fs18\par`r`n") }
    elseif ($text -match '^[a-z]\) ') { [void]$body.Append("\pard\li300\fi-300\sa100 $(ConvertTo-RtfInline $text)\par`r`n") }
    else { [void]$body.Append("\pard\sa120 $(ConvertTo-RtfInline $text)\par`r`n") }
}

$rtf = "{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Segoe UI;}}\f0\fs18`r`n" + $body.ToString() + "}`r`n"
New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
[System.IO.File]::WriteAllText($Output, $rtf, [System.Text.Encoding]::ASCII)
