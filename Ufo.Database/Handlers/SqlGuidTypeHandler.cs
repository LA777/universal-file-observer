using Dapper;
using System.Data;

namespace Ufo.Database.Handlers
{
    public class SqlGuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid guid)
        {
            parameter.Value = guid.ToString();
        }

        public override Guid Parse(object value)
        {
            if (value == null)
            {

            }

            if (value is Guid guid)
            {
                return guid;
            }

            return new Guid((string)value);
        }
    }

    public class SqlNullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
    {
        public override void SetValue(IDbDataParameter parameter, Guid? guid)
        {
            parameter.Value = guid.ToString();
        }

        public override Guid? Parse(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is Guid guid)
            {
                return guid;
            }

            return new Guid((string)value);
        }
    }
}
