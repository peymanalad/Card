using System.Data.Common;
using Dario.Core.Abstraction.Card.Database;

namespace Dario.Core.Application.Card.Db;

public interface IDbConnectionFactory
{
    DatabaseProvider Provider { get; }
    DbConnection Create();
}