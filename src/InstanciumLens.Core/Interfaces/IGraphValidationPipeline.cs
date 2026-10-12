// File: src/InstanciumLens.Core/Interfaces/IGraphValidationPipeline.cs
using InstanciumLens.Core.Models;

namespace InstanciumLens.Core.Interfaces
{
    public interface IGraphValidationPipeline
    {
        ValidationReport Validate(KnowledgeGraph graph, GraphChange change);
    }
}