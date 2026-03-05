using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ufo.Abstractions;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;

namespace Ufo.Database.Repositories;

public class LabelsRepository : ILabelsRepository
{
    private readonly ILogger<LabelsRepository> _logger;
    private readonly string _connectionString;

    public LabelsRepository(IOptionsMonitor<DatabaseOptions> databaseOptionsMonitor, ILogger<LabelsRepository>? logger)
    {
        _connectionString = databaseOptionsMonitor.CurrentValue.ConnectionString ?? throw new ArgumentNullException(nameof(databaseOptionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IList<ServerResult>> AddLabelAsync(LabelEntity labelEntity, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AddLabelAsync - LabelId: {LabelId}, UserId: {UserId}", labelEntity.Id, userId);
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            var labelInDatabase = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByNameSql,
                new { labelEntity.Name, UserId = userId });
            if (labelInDatabase is not null)
            {
                return new List<ServerResult>
                {
                    new() {
                        ActionName = $"Adding Label '{labelEntity.Name}'.",
                        Result = Result.Error,
                        Priority = ActionPriority.Highest,
                        Message = $"Label with name '{labelEntity.Name} already exists."
                    }
                };
            }

            var rowsAffectedInLabels = await sqLiteConnection.ExecuteAsync(SqlScripts.InsertLabelSql,
                new { labelEntity.Id, labelEntity.Name, labelEntity.ColorHex, UserId = userId });
            _logger.LogInformation($"Added label with id: {labelEntity.Id}");

            var serverResults = new List<ServerResult>
            {
                new() {
                    ActionName = $"Adding Label '{labelEntity.Name}'.",
                    Result = rowsAffectedInLabels == 1 ? Result.Success : Result.Error,
                    Priority = ActionPriority.Highest
                }
            };

            // If Label has Snapshots - add associations in LabelsToSnapshots table
            if (labelEntity.Snapshots is { Count: > 0 })
            {
                foreach (var snapshot in labelEntity.Snapshots)
                {
                    // Check if Snapshot exists
                    var snapshotEntity = await sqLiteConnection.QueryFirstAsync<SnapshotEntity>(
                        SqlScripts.SelectSnapshotOnlyByIdSql,
                        new { SnapshotId = snapshot.Id, UserId = userId });
                    if (snapshotEntity is not null)
                    {
                        var rowsAffectedInLabelsToSnapshots = await sqLiteConnection.ExecuteAsync(
                        SqlScripts.InsertLabelsToSnapshotsSql,
                        new { LabelId = labelEntity.Id, SnapshotId = snapshot.Id });
                        _logger.LogInformation($"Assigned label with id: {labelEntity.Id} to snapshot: {snapshot.Id}");

                        serverResults.Add(new ServerResult
                        {
                            ActionName = $"Assigning Label '{labelEntity.Name}' to Snapshot with id: {snapshot.Id}.",
                            Result = rowsAffectedInLabelsToSnapshots == 1 ? Result.Success : Result.Error,
                            Priority = ActionPriority.Optional
                        });
                    }
                }
            }

            return serverResults;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - AddLabelAsync");
            throw;
        }
    }

    public async Task<ServerResult> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AddLabelToSnapshotAsync - LabelId: {LabelId}, SnapshotId: {SnapshotId}, UserId: {UserId}", labelId, snapshotId, userId);
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            // Check that such Label exists
            var labelEntity = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByIdSql,
                new { LabelId = labelId, UserId = userId });

            if (labelEntity == null)
            {
                return new ServerResult
                {
                    ActionName = $"Assigning Label with id: {labelId} to Snapshot with id: {snapshotId}.",
                    Result = Result.NotFound,
                    Priority = ActionPriority.Highest,
                    Message = $"Label with id: {labelId} does not exist."
                };
            }

            // Check that such Snapshot exists
            var snapshot = await sqLiteConnection.QueryFirstOrDefaultAsync<SnapshotEntity>(
                SqlScripts.SelectSnapshotOnlyByIdSql,
                new { SnapshotId = snapshotId, UserId = userId });

            if (snapshot == null)
            {
                return new ServerResult
                {
                    ActionName = $"Assigning Label with id: {labelId} to Snapshot with id: {snapshotId}.",
                    Result = Result.NotFound,
                    Priority = ActionPriority.Highest,
                    Message = $"Snapshot with id: {snapshotId} does not exist."
                };
            }

            var rowsAffected = await sqLiteConnection.ExecuteAsync(
                SqlScripts.InsertLabelsToSnapshotsSql,
                new { LabelId = labelId, SnapshotId = snapshotId });
            _logger.LogInformation($"Assigned label with id: {labelId} to snapshot: {snapshotId}");

            var serverResult = new ServerResult
            {
                ActionName = $"Assigning Label '{labelEntity.Name}' to Snapshot with id: {snapshotId}.",
                Result = rowsAffected == 1 ? Result.Success : Result.Error,
                Priority = ActionPriority.Highest
            };

            return serverResult;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - AddLabelToSnapshotAsync");
            throw;
        }
    }

    public async Task<ServerResult> DeleteLabelByIdAsync(Ulid labelId, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteLabelByIdAsync - LabelId: {LabelId}, UserId: {UserId}", labelId, userId);
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            await sqLiteConnection.OpenAsync(cancellationToken);
            using var transaction = await sqLiteConnection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Check whether such label exists
                var labelEntity = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                    SqlScripts.SelectLabelByIdSql,
                    new { LabelId = labelId, UserId = userId }, 
                    transaction);

                if (labelEntity == null)
                {
                    return new ServerResult
                    {
                        ActionName = $"Deleting Label with id: {labelId}.",
                        Result = Result.NotFound,
                        Priority = ActionPriority.Highest
                    };
                }

                // First, delete associations in LabelsToSnapshots table
                var rowsAffectedInLabelsToSnapshots = await sqLiteConnection.ExecuteAsync(
                    SqlScripts.DeleteLabelsToSnapshotsByLabelIdSql,
                    new { LabelId = labelId },
                    transaction);

                if (rowsAffectedInLabelsToSnapshots > 0)
                {
                    _logger.LogInformation($"Deleted associations for label with id: {labelId}");
                }
                else
                {
                    _logger.LogInformation($"No associations found for label with id: {labelId} in LabelsToSnapshots table");
                }

                // Then delete the label itself
                var rowsAffectedInLabels = await sqLiteConnection.ExecuteAsync(
                        SqlScripts.DeleteLabelByIdSql,
                        new { LabelId = labelId, UserId = userId },
                        transaction);

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation($"Deleted label with id: {labelId}");

                return new ServerResult
                {
                    ActionName = $"Deleting Label '{labelEntity.Name}'.",
                    Result = rowsAffectedInLabels == 1 ? Result.Success : Result.Error,
                    Priority = ActionPriority.Highest
                };
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

    public async Task<IList<LabelEntity>> GetAllLabelsAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetAllLabelsAsync - UserId: {UserId}", userId);
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            var labels = await sqLiteConnection.QueryAsync<LabelEntity>(
                SqlScripts.SelectAllLabelsSql,
                new { UserId = userId });

            _logger.LogInformation($"Retrieved {labels.Count()} labels");
            return labels.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetAllLabelsAsync");
            throw;
        }
    }

    public async Task<IList<LabelEntity>> GetLabelsBySnapshotIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetLabelsBySnapshotIdAsync - SnapshotId: {SnapshotId}, UserId: {UserId}", snapshotId, userId);
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            var labels = await sqLiteConnection.QueryAsync<LabelEntity>(
                SqlScripts.SelectLabelsBySnapshotIdSql,
                new { SnapshotId = snapshotId, UserId = userId });

            _logger.LogInformation($"Retrieved {labels.Count()} labels for snapshot: {snapshotId}");
            return labels.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetLabelsBySnapshotIdAsync");
            throw;
        }
    }

    public async Task<ServerResult> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RemoveLabelFromSnapshotAsync - LabelId: {LabelId}, SnapshotId: {SnapshotId}, UserId: {UserId}", labelId, snapshotId, userId);
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            var labelEntity = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByIdSql, new { LabelId = labelId, UserId = userId });

            if (labelEntity == null)
            {
                return new ServerResult
                {
                    ActionName = $"Label with id: {labelId} was not found.",
                    Result = Result.NotFound,
                    Priority = ActionPriority.Highest
                };
            }

            var snapshotEntity = await sqLiteConnection.QueryFirstOrDefaultAsync<SnapshotEntity>(
                SqlScripts.SelectSnapshotOnlyByIdSql, new { SnapshotId = snapshotId, UserId = userId });
            if (snapshotEntity == null)
            {
                return new ServerResult
                {
                    ActionName = $"Snapshot with id: {snapshotId} was not found.",
                    Result = Result.NotFound,
                    Priority = ActionPriority.Highest
                };
            }

            var rowsAffected = await sqLiteConnection.ExecuteAsync(
                SqlScripts.DeleteLabelFromSnapshotSql,
                new { LabelId = labelId, SnapshotId = snapshotId, UserId = userId });
            _logger.LogInformation($"Removed label with id: {labelId} from snapshot: {snapshotId}");

            if (rowsAffected == 0)
            {
                return new ServerResult
                {
                    ActionName = $"Snapshot with id: {snapshotId} has no associsation with Label with id: {labelId}.",
                    Result = Result.NotFound,
                    Priority = ActionPriority.Highest
                };
            }
            
            return new ServerResult
            {
                ActionName = $"Removing Label with id: {labelId} from Snapshot with id: {snapshotId}.",
                Result = Result.Success,
                Priority = ActionPriority.Highest
            };                     
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - RemoveLabelFromSnapshot");
            throw;
        }
    }

    public async Task<ServerResult> UpdateLabelAsync(LabelEntity labelEntity, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("UpdateLabelAsync - LabelId: {LabelId}, UserId: {UserId}", labelEntity.Id, userId);
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);

            var labelToUpdate = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByIdSql,
                new { LabelId = labelEntity.Id, UserId = userId });

            if (labelToUpdate == null)
            {
                return new ServerResult
                {
                    ActionName = $"Updating Label '{labelEntity.Name}'. Label with ID: {labelEntity.Id} was not found in Database.",
                    Result = Result.NotFound,
                    Priority = ActionPriority.Highest
                };
            }

            var labelWithSameName = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByNameSql,
                new { Name = labelEntity.Name, UserId = userId });

            if (labelWithSameName != null && labelWithSameName.Id != labelEntity.Id)
            {
                return new ServerResult
                {
                    ActionName = $"Updating Label '{labelEntity.Name}'.",
                    Result = Result.Error,
                    Priority = ActionPriority.Highest,
                    Message = $"The Label with name '{labelEntity.Name}' already exists."
                };
            }

            var rowsAffected = await sqLiteConnection.ExecuteAsync(
                SqlScripts.UpdateLabelSql,
                new { labelEntity.Id, labelEntity.Name, labelEntity.ColorHex, UserId = userId });
            _logger.LogInformation($"Updated label with id: {labelEntity.Id}");
               
            var serverResult = new ServerResult
            {
                ActionName = $"Updating Label '{labelEntity.Name}'.",
                Result = rowsAffected == 1 ? Result.Success : Result.Error,
                Priority = ActionPriority.Highest
            };

            return serverResult;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - UpdateLabel");
            throw;
        }
    }

    public async Task<LabelEntity?> GetLabelByNameAsync(string labelName, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetLabelByNameAsync - LabelName: {LabelName}, UserId: {UserId}", labelName, userId);
        try
        {
            await using var sqLiteConnection = new SqliteConnection(_connectionString);
            var label = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByNameSql,
                new { Name = labelName, UserId = userId });

            if (label == null)
            {
                _logger.LogInformation($"Label with name: '{labelName}' was not found.");
            } 
            else
            {
                _logger.LogInformation($"Label with name: '{labelName}' was found. ID {label.Id}");
            }

            return label;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ERROR - GetLabelByNameAsync");
            throw;
        }
    }
}
