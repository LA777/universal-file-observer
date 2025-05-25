using Dapper;
using System.Data;

namespace Ufo.Database.Handlers;

public class SqlUlidTypeHandler : SqlMapper.TypeHandler<Ulid>
{
    public override void SetValue(IDbDataParameter parameter, Ulid value)
    {
        parameter.DbType = DbType.StringFixedLength;
        parameter.Size = 26;
        parameter.Value = value.ToString();
    }

    public override Ulid Parse(object value)
    {
        return Ulid.Parse((string)value);
    }
}

public class SqlNullableUlidTypeHandler : SqlMapper.TypeHandler<Ulid?>
{
    public override void SetValue(IDbDataParameter parameter, Ulid? value)
    {
        parameter.DbType = DbType.StringFixedLength;
        parameter.Size = 26;
        parameter.Value = value.ToString();
    }

    public override Ulid? Parse(object value)
    {
        if (value == null)
        {
            return Ulid.Empty;
        }

        return Ulid.Parse((string)value);
    }
}
