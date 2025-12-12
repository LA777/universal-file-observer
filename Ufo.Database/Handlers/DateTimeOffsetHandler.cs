using Dapper;
using System.Data;

namespace Ufo.Database.Handlers
{
    public class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value)
        {
            return value switch
            {
                DateTimeOffset dto => dto,
                string s when DateTimeOffset.TryParse(s, out var result) => result,
                _ => throw new DataException($"Unable to parse '{value}' as DateTimeOffset")
            };
        }

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.Value = value.ToString("O");
        }
    }
}
