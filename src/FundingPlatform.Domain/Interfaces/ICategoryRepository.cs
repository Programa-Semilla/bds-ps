using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Domain.Interfaces;

public interface ICategoryRepository
{
    Task<List<Category>> GetAllActiveAsync();
    Task<Category?> GetByIdAsync(int id);

    // Spec 035 / US1 — admin category-field management (mirrors IImpactTemplateRepository).
    Task<IReadOnlyList<Category>> GetAllAsync();
    Task<Category?> GetByIdWithFieldsAsync(int id);
    Task AddAsync(Category category);
    Task UpdateAsync(Category category);
    Task SaveChangesAsync();
}
