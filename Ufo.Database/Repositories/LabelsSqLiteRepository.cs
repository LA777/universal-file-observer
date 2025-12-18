using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Options;

namespace Ufo.Database.Repositories
{
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
                
                // Insert the label
                var result = await sqLiteConnection.ExecuteAsync(SqlScripts.InsertLabelSql,
                    new { labelEntity.Id, labelEntity.Name, labelEntity.ColorHex });

                _logger.LogInformation($"Added label with id: {labelEntity.Id}");
                return result;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ERROR - AddLabelAsync");
                throw;
            }
        }

        public async Task<int> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, CancellationToken cancellationToken = default)
        { // TODO LA - Add IntegrationTests
            try
            {
                await using var sqLiteConnection = new SqliteConnection(_connectionString);

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
            // TODO LA - Add IntegrationTests
            try
            {
                await using var sqLiteConnection = new SqliteConnection(_connectionString);
                var result = await sqLiteConnection.ExecuteAsync(
                    SqlScripts.DeleteLabelByIdSql,
                    new { LabelId = labelId });
                // TODO LA - Remove associations in LabelsToSnapshots table

                _logger.LogInformation($"Deleted label with id: {labelId}");
                return result;

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
        { // TODO LA - Add IntegrationTests
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
}
