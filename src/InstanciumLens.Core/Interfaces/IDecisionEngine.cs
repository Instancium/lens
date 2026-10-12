// File: src/InstanciumLens.Core/Interfaces/IDecisionEngine.cs
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