using FundingPlatform.Application.Notifications;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace FundingPlatform.Infrastructure.Notifications.Persistence;

/// <summary>
/// Spec 021 / FR-001 — EF Core implementation of <see cref="IWorkflowTransactionScope"/>.
/// Opens an explicit <see cref="IDbContextTransaction"/> on the scoped <c>AppDbContext</c>;
/// reuses the connection so both SaveChanges calls land inside the same transaction.
/// </summary>
public sealed class EfWorkflowTransactionScope : IWorkflowTransactionScope
{
    private readonly AppDbContext _context;

    public EfWorkflowTransactionScope(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IWorkflowTransaction> BeginAsync(CancellationToken ct)
    {
        // If a transaction is already open on this DbContext (e.g., nested call
        // path), return a no-op handle so the outer scope keeps ownership.
        if (_context.Database.CurrentTransaction is not null)
        {
            return new NoOpTransaction();
        }
        var tx = await _context.Database.BeginTransactionAsync(ct);
        return new EfWorkflowTransaction(tx);
    }

    private sealed class EfWorkflowTransaction : IWorkflowTransaction
    {
        private readonly IDbContextTransaction _tx;
        private bool _disposed;

        public EfWorkflowTransaction(IDbContextTransaction tx) => _tx = tx;

        public Task CommitAsync(CancellationToken ct) => _tx.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _tx.DisposeAsync();
        }
    }

    private sealed class NoOpTransaction : IWorkflowTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
