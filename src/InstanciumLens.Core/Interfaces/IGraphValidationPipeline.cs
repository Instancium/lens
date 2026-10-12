// File: src/InstanciumLens.Core/Interfaces/IGraphValidationPipeline.cs
// [AI DIFF]: added for WBS stage 5 validation pipeline
using InstanciumLens.Core.Models;

namespace InstanciumLens.Core.Interfaces
{
    public interface IGraphValidationPipeline
    {
        ValidationReport Validate(KnowledgeGraph graph, GraphChange change);
    }
}