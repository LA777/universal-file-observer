using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;

namespace Ufo.Database.Repositories;

public class LabelsSqLiteRepository : ILabelsSqLiteRepository
{
    private readonly ILogger<LabelsSqLiteRepository> _logger;
    private readonly string _connectionString;

    public LabelsSqLiteRepository(IOptionsMonitor<DatabaseOptions> databaseOptionsMonitor, ILogger<LabelsSqLiteRepository>? logger)
    {
        _connectionString = databaseOptionsMonitor.CurrentValue.ConnectionString ?? throw new ArgumentNullException(nameof(databaseOptionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> AddLabelAsync(LabelEntity labelEntity, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            var result = await sqLiteConnection.ExecuteAsync(SqlScripts.InsertLabelSql,
                new { labelEntity.Id, labelEntity.Name, labelEntity.ColorHex });
            _logger.LogInformation($"Added label with id: {labelEntity.Id}");

            // TODO LA - Update Integration tests to cover these checks
            // If Label has Snapshots - add associations in LabelsToSnapshots table
            if (labelEntity.Snapshots is { Count: > 0 })
            {                    
                foreach (var snapshot in labelEntity.Snapshots)
                {
                    // Check is Snapshot exists
                    var snapshotEntity = await sqLiteConnection.QueryFirstAsync<SnapshotEntity>(
                        SqlScripts.SelectSnapshotOnlyByIdSql,
                        new { SnapshotId = snapshot.Id });
                    if (snapshotEntity is not null)
                    {
                        await sqLiteConnection.ExecuteAsync(
                        SqlScripts.InsertLabelsToSnapshotsSql,
                        new { LabelId = labelEntity.Id, SnapshotId = snapshot.Id });
                        _logger.LogInformation($"Assigned label with id: {labelEntity.Id} to snapshot: {snapshot.Id}");
                    }                        
                }
            }

            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - AddLabelAsync");
            throw;
        }
    }

    public async Task<int> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            // TODO LA - Update Integration tests to cover these checks
            // Check that such Label exists
            var labelEntity = await sqLiteConnection.QueryFirstAsync<LabelEntity>(
                SqlScripts.SelectLabelByIdSql,
                new { LabelId = labelId }) 
                ?? throw new InvalidOperationException($"Label with id: {labelId} does not exist.");

            // Check that such Snapshot exists
            var snapshot = await sqLiteConnection.QueryFirstAsync<SnapshotEntity>(
                SqlScripts.SelectSnapshotOnlyByIdSql,
                new { SnapshotId = snapshotId }) 
                ?? throw new InvalidOperationException($"Snapshot with id: {snapshotId} does not exist.");

            var result = await sqLiteConnection.ExecuteAsync(
                SqlScripts.InsertLabelsToSnapshotsSql,
                new { LabelId = labelId, SnapshotId = snapshotId });
            _logger.LogInformation($"Assigned label with id: {labelId} to snapshot: {snapshotId}");

            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - AddLabelToSnapshotAsync");
            throw;
        }
    }

    public async Task<int> DeleteLabelByIdAsync(Ulid labelId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            await sqLiteConnection.OpenAsync(cancellationToken);
            using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

            try
            {
                // First, delete associations in LabelsToSnapshots table
                await sqLiteConnection.ExecuteAsync(
                    SqlScripts.DeleteLabelsToSnapshotsByLabelIdSql,
                    new { LabelId = labelId },
                    transaction);
                _logger.LogInformation($"Deleted associations for label with id: {labelId}");

                // Then delete the label itself
                var result = await sqLiteConnection.ExecuteAsync(
                    SqlScripts.DeleteLabelByIdSql,
                    new { LabelId = labelId },
                    transaction);

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation($"Deleted label with id: {labelId}");
                return result;
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(exception, "ERROR - DeleteLabelByIdAsync - Transaction Rollback");
                throw;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - DeleteLabelByIdAsync");
            throw;
        }
    }

    public async Task<IList<LabelEntity>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            var labels = await sqLiteConnection.QueryAsync<LabelEntity>(
                SqlScripts.SelectAllLabelsSql);

            _logger.LogInformation($"Retrieved {labels.Count()} labels");
            return labels.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetAllLabelsAsync");
            throw;
        }
    }

    public async Task<IList<LabelEntity>> GetLabelsBySnapshotIdAsync(Ulid snapshotId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            var labels = await sqLiteConnection.QueryAsync<LabelEntity>(
                SqlScripts.SelectLabelsBySnapshotIdSql,
                new { SnapshotId = snapshotId });

            _logger.LogInformation($"Retrieved {labels.Count()} labels for snapshot: {snapshotId}");
            return labels.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetLabelsBySnapshotIdAsync");
            throw;
        }
    }

    public async Task<int> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            var result = await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteLabelFromSnapshotSql,
                new { LabelId = labelId, SnapshotId = snapshotId });
            _logger.LogInformation($"Removed label with id: {labelId} from snapshot: {snapshotId}");

            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - RemoveLabelFromSnapshot");
            throw;
        }
    }

    public async Task<int> UpdateLabelAsync(LabelEntity labelEntity, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            var result = await sqLiteConnection.ExecuteAsync(
                SqlScripts.UpdateLabelSql,
                new { labelEntity.Id, labelEntity.Name, labelEntity.ColorHex });
            _logger.LogInformation($"Updated label with id: {labelEntity.Id}");

            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - UpdateLabel");
            throw;
        }
    }
}
