using InstanciumLens.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstanciumLens.Infrastructure.Data
{
    public class SqLiteGraphRepositoryFactory : IGraphRepositoryFactory
    {
        public IGraphRepository CreateRepository(string targetFolderPath, string profileId)
        {
            // Instantiates the repository which dynamically creates knowledge_graph_{profileId}.db
            return new SqLiteGraphRepository(targetFolderPath, profileId);
        }
    }
}
