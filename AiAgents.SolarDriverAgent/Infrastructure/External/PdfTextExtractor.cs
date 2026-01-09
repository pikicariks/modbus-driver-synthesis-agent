using System.Text;
using AiAgents.SolarDriverAgent.Application.Contracts;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AiAgents.SolarDriverAgent.Infrastructure.External;

/// <summary>
/// PDF text extractor koristi PdfPig za ekstrakciju teksta i tabela.
/// Posebna pažnja na Modbus register tabele.
/// </summary>
public class PdfTextExtractor : IPdfTextExtractor
{
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<string> ExtractTextAsync(byte[] pdfDocument, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ExtractText(pdfDocument), cancellationToken);
    }

    private string ExtractText(byte[] pdfDocument)
    {
        _logger.LogDebug("Extracting text from PDF ({Size} bytes)", pdfDocument.Length);

        if (pdfDocument.Length < 10)
        {
            throw new InvalidOperationException("PDF document is too small to be valid");
        }

        try
        {
            using var memoryStream = new MemoryStream(pdfDocument);
            using var document = PdfDocument.Open(memoryStream);

            var sb = new StringBuilder();
            var totalPages = document.NumberOfPages;

            _logger.LogDebug("PDF has {PageCount} pages", totalPages);

            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                var page = document.GetPage(pageNum);

                sb.AppendLine($"=== PAGE {pageNum} ===");
                sb.AppendLine();

                // Ekstraktuj tekst sa strukturom
                var pageText = ExtractPageWithStructure(page);
                sb.Append(pageText);

                // Pokušaj ekstraktovati tabele
                var tables = ExtractTables(page);
                if (!string.IsNullOrWhiteSpace(tables))
                {
                    sb.AppendLine();
                    sb.AppendLine("--- TABLES ---");
                    sb.Append(tables);
                }

                sb.AppendLine();
            }

            var result = sb.ToString();

            _logger.LogInformation(
                "Extracted {CharCount} characters from {PageCount} pages",
                result.Length, totalPages);

            if (string.IsNullOrWhiteSpace(result))
            {
                _logger.LogWarning("PDF extraction returned empty text - PDF might be scanned/image-based");
                return "[WARNING: No text extracted. PDF might be image-based or scanned. OCR required.]";
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from PDF");
            throw new InvalidOperationException($"Failed to extract PDF text: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Ekstraktuje tekst sa stranice sa očuvanjem strukture.
    /// </summary>
    private string ExtractPageWithStructure(Page page)
    {
        var sb = new StringBuilder();

        try
        {
            // Koristi NearestNeighbourWordExtractor za bolju ekstrakciju riječi
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance);

            if (!words.Any())
            {
                // Fallback na osnovni tekst
                sb.AppendLine(page.Text);
                return sb.ToString();
            }

            // Koristi DocstrumBoundingBoxes za segmentaciju
            var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);

            foreach (var block in blocks.OrderBy(b => -b.BoundingBox.Top).ThenBy(b => b.BoundingBox.Left))
            {
                var blockText = string.Join(" ", block.TextLines.Select(l => l.Text));
                sb.AppendLine(blockText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during structured extraction, falling back to simple text");
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Pokušava ekstraktovati tabele na osnovu pozicije teksta.
    /// Traži Modbus register pattern-e.
    /// </summary>
    private string ExtractTables(Page page)
    {
        var sb = new StringBuilder();

        try
        {
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();

            if (!words.Any())
                return string.Empty;

            // Grupiši riječi po Y poziciji (redovi)
            var rows = GroupWordsIntoRows(words);

            // Pronađi redove koji izgledaju kao tabela (više od 2 kolone sa konzistentnim razmacima)
            var tableRows = DetectTableRows(rows);

            if (tableRows.Any())
            {
                sb.AppendLine();
                foreach (var row in tableRows)
                {
                    sb.AppendLine(string.Join(" | ", row));
                }
            }

            // Traži specifične Modbus pattern-e
            var modbusInfo = ExtractModbusPatterns(page.Text);
            if (!string.IsNullOrWhiteSpace(modbusInfo))
            {
                sb.AppendLine();
                sb.AppendLine("--- MODBUS REGISTERS ---");
                sb.Append(modbusInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting tables");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Grupiše riječi u redove na osnovu Y koordinate.
    /// </summary>
    private List<List<Word>> GroupWordsIntoRows(List<Word> words)
    {
        var rows = new List<List<Word>>();
        var tolerance = 5.0; // Tolerancija za Y poziciju

        var sortedWords = words.OrderByDescending(w => w.BoundingBox.Bottom).ToList();

        List<Word>? currentRow = null;
        double currentY = double.MinValue;

        foreach (var word in sortedWords)
        {
            var wordY = word.BoundingBox.Bottom;

            if (currentRow == null || Math.Abs(wordY - currentY) > tolerance)
            {
                // Novi red
                currentRow = new List<Word>();
                rows.Add(currentRow);
                currentY = wordY;
            }

            currentRow.Add(word);
        }

        // Sortiraj riječi u svakom redu po X poziciji
        foreach (var row in rows)
        {
            row.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        }

        return rows;
    }

    /// <summary>
    /// Detektuje redove koji izgledaju kao dio tabele.
    /// </summary>
    private List<List<string>> DetectTableRows(List<List<Word>> rows)
    {
        var tableRows = new List<List<string>>();

        foreach (var row in rows)
        {
            if (row.Count < 2)
                continue;

            // Provjeri da li red ima konzistentne razmake (tabela)
            var gaps = new List<double>();
            for (int i = 1; i < row.Count; i++)
            {
                var gap = row[i].BoundingBox.Left - row[i - 1].BoundingBox.Right;
                gaps.Add(gap);
            }

            // Ako ima bar jedan značajan razmak (>20), tretiramo kao tabelu
            if (gaps.Any(g => g > 20))
            {
                // Grupiši riječi u ćelije na osnovu razmaka
                var cells = new List<string>();
                var currentCell = new StringBuilder();
                currentCell.Append(row[0].Text);

                for (int i = 1; i < row.Count; i++)
                {
                    var gap = row[i].BoundingBox.Left - row[i - 1].BoundingBox.Right;

                    if (gap > 20)
                    {
                        // Nova ćelija
                        cells.Add(currentCell.ToString().Trim());
                        currentCell.Clear();
                    }
                    else
                    {
                        currentCell.Append(' ');
                    }

                    currentCell.Append(row[i].Text);
                }

                cells.Add(currentCell.ToString().Trim());

                // Dodaj samo ako ima više od jedne ćelije
                if (cells.Count > 1)
                {
                    tableRows.Add(cells);
                }
            }
        }

        return tableRows;
    }

    /// <summary>
    /// Ekstraktuje Modbus-specifične informacije iz teksta.
    /// </summary>
    private string ExtractModbusPatterns(string text)
    {
        var sb = new StringBuilder();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Traži hex adrese (0x0000, 0x0010, itd.)
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmedLine, @"0x[0-9a-fA-F]{2,4}"))
            {
                sb.AppendLine(trimmedLine);
                continue;
            }

            // Traži decimalne adrese sa "register" ili "address" ključnim riječima
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmedLine, 
                @"(?:register|address|addr|reg)[:\s]*\d+", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                sb.AppendLine(trimmedLine);
                continue;
            }

            // Traži function code reference
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmedLine,
                @"(?:function\s*code|FC)[:\s]*(?:0?[1-6]|1[56])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                sb.AppendLine(trimmedLine);
                continue;
            }

            // Traži data type keywords u kontekstu registara
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmedLine,
                @"(?:uint16|int16|uint32|int32|float|word|dword|holding|input|coil)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                System.Text.RegularExpressions.Regex.IsMatch(trimmedLine, @"\d"))
            {
                sb.AppendLine(trimmedLine);
            }
        }

        return sb.ToString();
    }
}
