// File: src/InstanciumLens.Core/Services/DecisionEngine.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InstanciumLens.Core.Interfaces;
using InstanciumLens.Core.Models;

namespace InstanciumLens.Core.Services
{
    public sealed class DecisionEngine : IDecisionEngine
    {
        private readonly IGraphRepositoryFactory _repositoryFactory;
        private readonly IGraphValidationPipeline _validationPipeline;

        public DecisionEngine(
            IGraphRepositoryFactory repositoryFactory,
            IGraphValidationPipeline validationPipeline)
        {
            _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
            _validationPipeline = validationPipeline ?? throw new ArgumentNullException(nameof(validationPipeline));
        }

        public Task<DecisionBranch> CreateBranchAsync(
            string folderPath,
            GraphProfile profile,
            string branchName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentNullException(nameof(folderPath));
            }

            ArgumentNullException.ThrowIfNull(profile);

            if (string.IsNullOrWhiteSpace(branchName))
            {
                throw new ArgumentNullException(nameof(branchName));
            }

            cancellationToken.ThrowIfCancellationRequested();

            string branchId = BuildBranchId(branchName);

            // Materializes branch database file from master when master exists.
            _ = _repositoryFactory.CreateBranchRepository(folderPath, profile.Id, branchId);

            DecisionBranch branch = new()
            {
                BranchId = branchId,
                ProfileId = profile.Id,
                FolderPath = folderPath,
                DatabaseFilePath = _repositoryFactory.GetBranchDatabasePath(folderPath, profile.Id, branchId),
                CreatedAtUtc = DateTime.UtcNow
            };

            return Task.FromResult(branch);
        }

        public async Task<ValidationReport> ApplyChangeToBranchAsync(
            DecisionBranch branch,
            GraphChange change,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(branch);
            ArgumentNullException.ThrowIfNull(change);

            IGraphRepository repository = _repositoryFactory.CreateBranchRepository(
                branch.FolderPath,
                branch.ProfileId,
                branch.BranchId);

            await repository.SaveChangesAsync(change, cancellationToken);

            KnowledgeGraph graph = await repository.LoadFullGraphAsync(cancellationToken);

            return _validationPipeline.Validate(graph, change);
        }

        public Task DeleteBranchAsync(
            DecisionBranch branch,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(branch);

            cancellationToken.ThrowIfCancellationRequested();

            _repositoryFactory.DeleteBranch(
                branch.FolderPath,
                branch.ProfileId,
                branch.BranchId);

            return Task.CompletedTask;
        }

        private static string BuildBranchId(string branchName)
        {
            string safeName = SanitizeName(branchName);
            return $"{safeName}_{Guid.NewGuid():N}";
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "branch";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();

            string result = new string(value
                .Select(ch => invalidChars.Contains(ch) || char.IsWhiteSpace(ch)
                    ? '_'
                    : char.ToLowerInvariant(ch))
                .ToArray());

            return string.IsNullOrWhiteSpace(result) ? "branch" : result;
        }
    }
}