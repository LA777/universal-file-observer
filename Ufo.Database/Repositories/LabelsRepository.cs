using Dapper;
using Microsoft.Extensions.Logging;
using Ufo.Abstractions;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;

namespace Ufo.Database.Repositories;

public class LabelsRepository : ILabelsRepository
{
    private readonly ILogger<LabelsRepository> _logger;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public LabelsRepository(IDbConnectionFactory dbConnectionFactory, ILogger<LabelsRepository>? logger)
    {
        _dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<ServerResult>> AddLabelAsync(LabelRequest label, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AddLabelAsync - LabelId: {LabelId}, UserId: {UserId}", label.Id, userId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            var labelInDatabase = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByNameSql,
                new { label.Name, UserId = userId });
            if (labelInDatabase is not null)
            {
                return new List<ServerResult>
                {
                    new() {
                        ActionName = $"Adding Label '{label.Name}'.",
                        Result = Result.Error,
                        Priority = ActionPriority.Highest,
                        Message = $"Label with name '{label.Name} already exists."
                    }
                };
            }

            var rowsAffectedInLabels = await sqLiteConnection.ExecuteAsync(SqlScripts.InsertLabelSql,
                new { label.Id, label.Name, label.ColorHex, UserId = userId });
            _logger.LogInformation($"Added label with id: {label.Id}");

            var serverResults = new List<ServerResult>
            {
                new() {
                    ActionName = $"Adding Label '{label.Name}'.",
                    Result = rowsAffectedInLabels == 1 ? Result.Success : Result.Error,
                    Priority = ActionPriority.Highest
                }
            };

            // If Label has Snapshots - add associations in LabelsToSnapshots table
            if (label.SnapshotIds is { Count: > 0 })
            {
                foreach (var snapshotId in label.SnapshotIds)
                {
                    // Check if Snapshot exists
                    var snapshotEntity = await sqLiteConnection.QueryFirstAsync<SnapshotEntity>(
                        SqlScripts.SelectSnapshotOnlyByIdSql,
                        new { SnapshotId = snapshotId, UserId = userId });
                    if (snapshotEntity is not null)
                    {
                        var rowsAffectedInLabelsToSnapshots = await sqLiteConnection.ExecuteAsync(
                        SqlScripts.InsertLabelsToSnapshotsSql,
                        new { LabelId = label.Id, SnapshotId = snapshotId });
                        _logger.LogInformation($"Assigned label with id: {label.Id} to snapshot: {snapshotId}");

                        serverResults.Add(new ServerResult
                        {
                            ActionName = $"Assigning Label '{label.Name}' to Snapshot with id: {snapshotId}.",
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
            // Check that such Label exists
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
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
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
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

    public async Task<List<LabelEntity>> GetAllLabelsAsync(Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetAllLabelsAsync - UserId: {UserId}", userId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
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

    public async Task<List<LabelEntity>> GetLabelsBySnapshotIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetLabelsBySnapshotIdAsync - SnapshotId: {SnapshotId}, UserId: {UserId}", snapshotId, userId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
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
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
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

    public async Task<ServerResult> UpdateLabelAsync(LabelRequest label, Ulid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("UpdateLabelAsync - LabelId: {LabelId}, UserId: {UserId}", label.Id, userId);
        try
        {
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
            var labelToUpdate = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByIdSql,
                new { LabelId = label.Id, UserId = userId });

            if (labelToUpdate == null)
            {
                return new ServerResult
                {
                    ActionName = $"Updating Label '{label.Name}'. Label with ID: {label.Id} was not found in Database.",
                    Result = Result.NotFound,
                    Priority = ActionPriority.Highest
                };
            }

            var labelWithSameName = await sqLiteConnection.QueryFirstOrDefaultAsync<LabelEntity>(
                SqlScripts.SelectLabelByNameSql,
                new { Name = label.Name, UserId = userId });

            if (labelWithSameName != null && labelWithSameName.Id != label.Id)
            {
                return new ServerResult
                {
                    ActionName = $"Updating Label '{label.Name}'.",
                    Result = Result.Error,
                    Priority = ActionPriority.Highest,
                    Message = $"The Label with name '{label.Name}' already exists."
                };
            }

            var rowsAffected = await sqLiteConnection.ExecuteAsync(
                SqlScripts.UpdateLabelSql,
                new { label.Id, label.Name, label.ColorHex, UserId = userId });
            _logger.LogInformation($"Updated label with id: {label.Id}");
               
            var serverResult = new ServerResult
            {
                ActionName = $"Updating Label '{label.Name}'.",
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
            var sqLiteConnection = await _dbConnectionFactory.GetSqliteConnectionAsync(cancellationToken);
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
