// File: src/InstanciumLens.Core/Interfaces/IDecisionEngine.cs
// [AI DIFF]: added for WBS stage 5 decision engine and what-if analysis
using InstanciumLens.Core.Models;

namespace InstanciumLens.Core.Interfaces
{
    public interface IDecisionEngine
    {
        Task<DecisionBranch> CreateBranchAsync(
            string folderPath,
            GraphProfile profile,
            string branchName,
            CancellationToken cancellationToken = default);

        Task<ValidationReport> ApplyChangeToBranchAsync(
            DecisionBranch branch,
            GraphChange change,
            CancellationToken cancellationToken = default);

        Task DeleteBranchAsync(
            DecisionBranch branch,
            CancellationToken cancellationToken = default);
    }
}