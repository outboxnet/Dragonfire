using Microsoft.Data.SqlClient;

namespace OutboxNet.SqlServer;

/// <summary>
/// Provides access to the current SQL Server connection and transaction.
/// Implement this interface to allow the direct SQL outbox publisher to participate
/// in the caller's transaction, ensuring atomicity between domain writes and outbox inserts.
/// </summary>
public interface ISqlTransactionAccessor
{
    SqlConnection Connection { get; }
    SqlTransaction Transaction { get; }
}
