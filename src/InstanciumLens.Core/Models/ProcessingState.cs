using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Core.Models
{
    public enum ProcessingPhase
    {
        Idle,
        Pass1_TextExtraction,   // Pass 1: Render PDF and extract text via OCR
        Pass2_EntityResolution, // Pass 2: LLM (Qwen 8B) finds facts and entities
        Pass3_GraphBuilding,    // Pass 3: GraphRAG relationship building
        Completed,
        Error
    }

    public class ProcessingState
    {
        public ProcessingPhase CurrentPhase { get; set; } = ProcessingPhase.Idle;
        public int ProgressPercentage { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public int TotalDocuments { get; set; }
        public int ProcessedDocuments { get; set; }
        public int TotalPages { get; set; }
        public int Pass1ProcessedPages { get; set; }
        public int Pass2ProcessedPages { get; set; }
        public int ExtractedEntities { get; set; }
        public int ExtractedRelationships { get; set; }
        public int ResumedPass1Pages { get; set; }
        public int ResumedPass2Pages { get; set; }
        public string? CurrentDocumentName { get; set; }
        public TimeSpan Elapsed { get; set; }
        public TimeSpan? EstimatedRemaining { get; set; }
        public bool IsRunning => CurrentPhase != ProcessingPhase.Idle &&
            CurrentPhase != ProcessingPhase.Completed &&
            CurrentPhase != ProcessingPhase.Error;
    }
}
