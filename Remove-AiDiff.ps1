Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    $file = $_.FullName
    
    # Read using the correct encoding (UTF-8 with or without BOM)
    $text = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

    if ($text -notmatch '\[AI DIFF\]:')
    {
        return
    }

    $originalText = $text

    # Case 1: The line consists ONLY of a // [AI DIFF]: ... comment
    # Remove the entire line, including the newline character (\r\n or \n)
    $text = $text -replace '(?m)^\s*//\s*\[AI DIFF\]:[^\n]*\r?\n?', ''

    # Case 2: Inline comment at the end of a line of code
    # Remove whitespace before // and the comment itself, without touching the line break
    $text = $text -replace '(?m)\s*//\s*\[AI DIFF\]:[^\n]*$', ''

    if ($text -ne $originalText)
    {
        # Determine whether the original had a BOM
        $encoding = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($file, $text, $encoding)
        Write-Host "Fixed: $file" -ForegroundColor Green
    }
}