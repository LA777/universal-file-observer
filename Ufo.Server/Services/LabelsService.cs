using Ufo.Abstractions;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.Requests;
using Ufo.Abstractions.Responses;
using Ufo.Server.Extensions;

namespace Ufo.Server.Services;

public interface ILabelsService
{
    Task<IList<LabelResponse>> GetAllLabelsAsync(Ulid userId, CancellationToken cancellationToken);
    Task<IList<LabelResponse>> GetLabelsBySnapshotIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken);
    Task<IList<ServerResult>> AddLabelAsync(LabelRequest label, Ulid userId, CancellationToken cancellationToken);
    Task<ServerResult> UpdateLabelAsync(LabelRequest label, Ulid userId, CancellationToken cancellationToken);
    Task<ServerResult> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken);
    Task<ServerResult> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken);
    Task<ServerResult> DeleteLabelByIdAsync(Ulid labelId, Ulid userId, CancellationToken cancellationToken);
    Task<LabelResponse?> GetLabelByNameAsync(string labelName, Ulid userId, CancellationToken cancellationToken);
}

public class LabelsService : ILabelsService
{
    // TODO LA - Cover with Unit tests (Low Priority)
    private readonly ILabelsRepository _labelsRepository;
    private readonly ILogger<LabelsService> _logger;

    public LabelsService(ILabelsRepository labelsRepository, ILogger<LabelsService> logger)
    {
        _labelsRepository = labelsRepository ?? throw new ArgumentNullException(nameof(labelsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IList<LabelResponse>> GetAllLabelsAsync(Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetAllLabelsAsync - UserId: {UserId}", userId);
        var entities = await _labelsRepository.GetAllLabelsAsync(userId, cancellationToken);
        return entities.ToResponseList();
    }

    public async Task<IList<LabelResponse>> GetLabelsBySnapshotIdAsync(Ulid snapshotId, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetLabelsBySnapshotIdAsync - SnapshotId: {SnapshotId}, UserId: {UserId}", snapshotId, userId);
        var entities = await _labelsRepository.GetLabelsBySnapshotIdAsync(snapshotId, userId, cancellationToken);
        return entities.ToResponseList();
    }

    public async Task<IList<ServerResult>> AddLabelAsync(LabelRequest label, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AddLabelAsync - LabelId: {LabelId}, UserId: {UserId}", label.Id, userId);
        return await _labelsRepository.AddLabelAsync(label, userId, cancellationToken);
    }

    public async Task<ServerResult> UpdateLabelAsync(LabelRequest label, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("UpdateLabelAsync - LabelId: {LabelId}, UserId: {UserId}", label.Id, userId);
        return await _labelsRepository.UpdateLabelAsync(label, userId, cancellationToken);
    }

    public async Task<ServerResult> AddLabelToSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AddLabelToSnapshotAsync - LabelId: {LabelId}, SnapshotId: {SnapshotId}, UserId: {UserId}", labelId, snapshotId, userId);
        return await _labelsRepository.AddLabelToSnapshotAsync(labelId, snapshotId, userId, cancellationToken);
    }

    public async Task<ServerResult> RemoveLabelFromSnapshotAsync(Ulid labelId, Ulid snapshotId, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RemoveLabelFromSnapshotAsync - LabelId: {LabelId}, SnapshotId: {SnapshotId}, UserId: {UserId}", labelId, snapshotId, userId);
        return await _labelsRepository.RemoveLabelFromSnapshotAsync(labelId, snapshotId, userId, cancellationToken);
    }

    public async Task<ServerResult> DeleteLabelByIdAsync(Ulid labelId, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteLabelByIdAsync - LabelId: {LabelId}, UserId: {UserId}", labelId, userId);
        return await _labelsRepository.DeleteLabelByIdAsync(labelId, userId, cancellationToken);
    }

    public async Task<LabelResponse?> GetLabelByNameAsync(string labelName, Ulid userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetLabelByNameAsync - LabelName: {LabelName}, UserId: {UserId}", labelName, userId);
        var entity = await _labelsRepository.GetLabelByNameAsync(labelName, userId, cancellationToken);
        return entity?.ToResponse();
    }
}