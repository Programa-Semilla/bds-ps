You are Claude Code acting as a senior cloud architect and .NET/Azure engineer.

I want to brainstorm a new technical requirement for an existing platform.

Context:
- The platform currently uses local storage for all file-related operations, including receiving, storing, and downloading files.
- We need to deploy the platform to Azure.
- The platform uses .NET Aspire for orchestration.
- In production/cloud environments, the platform should use Azure cloud storage.
- For local development, we need to decide the best approach:
  1. Continue using local filesystem storage
  2. Use Azurite as an Azure Storage emulator
  3. Use another recommended local development strategy

Goal:
Help me evaluate the best architecture and development approach for introducing Azure cloud storage while keeping local development simple and productive.

Please brainstorm the requirement and cover:

1. Recommended Azure service
   - Which Azure storage product should be used for file upload/download scenarios?
   - Why is it the right fit?
   - Any relevant alternatives and why they may or may not apply.

2. Environment-based architecture
   - How the app should behave in production vs local development.
   - How configuration should be handled through Aspire.
   - How to avoid hardcoding storage implementations.

3. Local development options
   Compare:
   - Local filesystem storage
   - Azurite
   - Direct connection to an Azure development storage account
   - Any hybrid approach

4. Pros and cons
   For each local development option, evaluate:
   - Developer experience
   - Setup complexity
   - Similarity to production
   - Testing reliability
   - Cost
   - CI/CD compatibility
   - Security considerations

5. Recommended approach
   Give a clear recommendation for:
   - Production
   - Local development
   - Automated tests
   - CI/CD pipelines

6. Implementation strategy
   Suggest a clean technical design, including:
   - Storage abstraction/interface
   - Azure Blob Storage implementation
   - Local/Azurite implementation if appropriate
   - Configuration strategy
   - Aspire integration ideas
   - Migration steps from the current local storage implementation

7. Risks and trade-offs
   Identify possible risks, edge cases, and operational concerns, such as:
   - File naming
   - Containers/buckets
   - Permissions
   - SAS URLs vs backend streaming
   - Large files
   - Cleanup/lifecycle policies
   - Observability/logging
   - Local/prod behavior differences

8. Questions to clarify
   At the end, list the key technical and product questions we should answer before implementation.

Output format:
- Be practical and implementation-oriented.
- this is for a real .NET Aspire application being prepared for Azure production deployment.
