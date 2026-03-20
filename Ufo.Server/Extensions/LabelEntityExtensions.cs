using Ufo.Abstractions.Database.Entities;
using Ufo.Abstractions.Responses;

namespace Ufo.Server.Extensions
{
    public static class LabelEntityExtensions
    {
        public static LabelResponse ToResponse(this LabelEntity entity) =>
            new()
            {
                Id = entity.Id,
                Name = entity.Name,
                ColorHex = entity.ColorHex,
                UserId = entity.UserId.ToString()
            };

        public static IList<LabelResponse> ToResponseList(this IList<LabelEntity> entities) =>
            entities.Select(e => e.ToResponse()).ToList();
    }
}
