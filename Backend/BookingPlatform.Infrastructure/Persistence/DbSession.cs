using System.Data;
using BookingPlatform.Application.Common.Persistence;
using Dapper;
using Npgsql;

namespace BookingPlatform.Infrastructure.Persistence;

/// <summary>
/// Scoped connection + optional transaction. Repositories run every command through <see cref="Connection"/> and
/// <see cref="Transaction"/>, so they enlist automatically in the unit of work when one is open.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource) : IUnitOfWork, IAsyncDisposable, IDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    public bool HasActiveTransaction => _transaction is not null;

    public NpgsqlTransaction? Transaction => _transaction;

    public async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            _connection = await dataSource.OpenConnectionAsync(cancellationToken);
        }
        else if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        return _connection;
    }

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active in this scope.");
        }

        var connection = await GetConnectionAsync(cancellationToken);
        _transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("No active transaction.");
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction;
        if (transaction is null)
        {
            return;
        }

        try
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }
        finally
        {
            await transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public Task SavepointAsync(string name, CancellationToken cancellationToken) =>
        (_transaction ?? throw new InvalidOperationException("No active transaction.")).SaveAsync(name, cancellationToken);

    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken) =>
        (_transaction ?? throw new InvalidOperationException("No active transaction.")).RollbackAsync(name, cancellationToken);

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken cancellationToken) =>
        Run(async () => await (await GetConnectionAsync(cancellationToken)).QuerySingleOrDefaultAsync<T>(Command(sql, parameters, cancellationToken)));

    public Task<T> QuerySingleAsync<T>(string sql, object? parameters, CancellationToken cancellationToken) =>
        Run(async () => await (await GetConnectionAsync(cancellationToken)).QuerySingleAsync<T>(Command(sql, parameters, cancellationToken)));

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken cancellationToken) =>
        Run<IReadOnlyList<T>>(async () => (await (await GetConnectionAsync(cancellationToken)).QueryAsync<T>(Command(sql, parameters, cancellationToken))).AsList());

    public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken) =>
        Run(async () => await (await GetConnectionAsync(cancellationToken)).ExecuteAsync(Command(sql, parameters, cancellationToken)));

    public Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters, CancellationToken cancellationToken) =>
        Run(async () => await (await GetConnectionAsync(cancellationToken)).ExecuteScalarAsync<T>(Command(sql, parameters, cancellationToken)));

    /// <summary>
    /// Runs a statement inside a savepoint. Returns false (rolled back to the savepoint, transaction still usable) when
    /// <paramref name="isExpected"/> matches the failure, e.g. an exclusion-constraint violation.
    /// </summary>
    /// <remarks>
    /// Two transactions inserting conflicting rows under an exclusion constraint can each wait for the other's
    /// uncommitted row; PostgreSQL then aborts one with a deadlock. The statement is retried from the savepoint, where
    /// it now waits for the surviving transaction and either succeeds or fails with the expected conflict.
    /// </remarks>
    public async Task<bool> TryExecuteAsync(string sql, object? parameters, Func<PostgresException, bool> isExpected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isExpected);
        var transaction = _transaction ?? throw new InvalidOperationException("TryExecuteAsync requires an active transaction.");
        const string savepoint = "try_execute";
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            await transaction.SaveAsync(savepoint, cancellationToken);
            try
            {
                await ExecuteAsync(sql, parameters, cancellationToken);
                await transaction.ReleaseAsync(savepoint, cancellationToken);
                return true;
            }
            catch (PostgresException ex) when (isExpected(ex))
            {
                await transaction.RollbackAsync(savepoint, cancellationToken);
                return false;
            }
            catch (TransientDatabaseException ex) when (attempt < maxAttempts
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected })
            {
                await transaction.RollbackAsync(savepoint, cancellationToken);
            }
        }
    }

    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        {
            throw new TransientDatabaseException("The database reported a transient concurrency failure.", ex);
        }
    }

    public async Task<SqlMapper.GridReader> QueryMultipleAsync(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.QueryMultipleAsync(Command(sql, parameters, cancellationToken));
    }

    /// <summary>
    /// Runs a DELETE inside a savepoint. Returns false (and rolls back to the savepoint, keeping the transaction usable)
    /// when a foreign key still references the row, so callers can deactivate instead.
    /// </summary>
    public async Task<bool> TryDeleteAsync(string sql, object? parameters, CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("TryDeleteAsync requires an active transaction.");
        const string savepoint = "try_delete";
        await transaction.SaveAsync(savepoint, cancellationToken);
        try
        {
            await ExecuteAsync(sql, parameters, cancellationToken);
            await transaction.ReleaseAsync(savepoint, cancellationToken);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.RestrictViolation)
        {
            await transaction.RollbackAsync(savepoint, cancellationToken);
            return false;
        }
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _transaction, commandTimeout: 30, cancellationToken: cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
        _connection?.Dispose();
        _connection = null;
    }
}
