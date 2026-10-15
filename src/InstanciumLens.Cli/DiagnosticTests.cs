// File: src/InstanciumLens.Cli/DiagnosticTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InstanciumLens.Core.Models;

namespace InstanciumLens.Cli
{
    public class DiagnosticTests
    {
        public static async Task TestEntityExtraction(string text, int pageNumber)
        {
            Console.WriteLine($"\n=== Testing AI Extraction for Page {pageNumber} ===");
            Console.WriteLine("Sample text (first 500 chars):");
            Console.WriteLine(text.Length > 500 ? text[..500] + "..." : text);

            // Test 1: Check if text contains formulas/equations
            var hasFormulas = text.Contains('=') && (text.Contains('²') || text.Contains('³') || text.Contains('√'));
            Console.WriteLine($"\nContains formulas: {hasFormulas}");

            // Test 2: Check for tables
            var lines = text.Split('\n');
            var hasTables = lines.Any(l => l.Contains('|') || l.Trim().All(c => c == '-' || c == '|' || char.IsWhiteSpace(c)));
            Console.WriteLine($"Contains tables: {hasTables}");

            // Test 3: Check for images/diagrams references
            var hasImages = text.Contains("Fig.") || text.Contains("Figure") || text.Contains("Diagram");
            Console.WriteLine($"References images: {hasImages}");

            // Test 4: Entity types we might extract
            Console.WriteLine("\nPotential entity types:");
            if (text.Contains("voltage") || text.Contains("current") || text.Contains("resistance"))
                Console.WriteLine("  - Electrical parameters");
            if (text.Contains("Ω") || text.Contains("V") || text.Contains("A"))
                Console.WriteLine("  - Electrical units");
            if (text.Contains("formula") || text.Contains("equation"))
                Console.WriteLine("  - Formulas/Equations");
        }

        public static void ShowJsonParsingIssues(string json)
        {
            Console.WriteLine("\n=== JSON Parsing Test ===");
            Console.WriteLine("Raw JSON:");
            Console.WriteLine(json);

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsed = JsonSerializer.Deserialize<KnowledgeGraph>(json, options);
                Console.WriteLine("\n✓ JSON parsed successfully");
                Console.WriteLine($"  Entities: {parsed?.Entities?.Count ?? 0}");
                Console.WriteLine($"  Relationships: {parsed?.Relationships?.Count ?? 0}");
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"\n✗ JSON parsing failed:");
                Console.WriteLine($"  Error: {ex.Message}");
                Console.WriteLine($"  StackTrace: {ex.StackTrace}");
            }
        }
    }
}