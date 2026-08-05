using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using ProcurementRadar.Persistence;

namespace ProcurementRadar.UnitTests.Persistence;

/// <summary>
/// A connection that executes nothing and remembers what it was asked to send.
/// </summary>
/// <remarks>
/// Not a database, and not an integration test: this exercises the real Dapper
/// parameter-building code and reports what would have gone over the wire.
/// Static checks on the SQL text cannot see that, and it is where a value can
/// quietly change type on the way out.
/// </remarks>
internal sealed class RecordingDbConnection : DbConnection
{
    private readonly Func<string, DataTable>? _resultFor;

    public RecordingDbConnection(Func<string, DataTable>? resultFor = null)
    {
        _resultFor = resultFor;
    }

    public List<(string Sql, IReadOnlyList<(string Name, object? Value, DbType Type)> Parameters)> Executed { get; } = [];

    public IReadOnlyList<(string Name, object? Value, DbType Type)> LastParameters => Executed[^1].Parameters;

    public object? Find(string parameterName)
        => LastParameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase)).Value;

    internal DataTable ResultFor(string sql) => _resultFor?.Invoke(sql) ?? new DataTable();

    [AllowNull]
    public override string ConnectionString { get; set; } = "recording";

    public override string Database => "recording";

    public override string DataSource => "recording";

    public override string ServerVersion => "0";

    public override ConnectionState State => ConnectionState.Open;

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Close()
    {
    }

    public override void Open()
    {
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();

    protected override DbCommand CreateDbCommand() => new RecordingCommand(this);

    private sealed class RecordingCommand : DbCommand
    {
        private readonly RecordingDbConnection _owner;

        public RecordingCommand(RecordingDbConnection owner)
        {
            _owner = owner;
            DbConnection = owner;
        }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            Record();
            return 1;
        }

        public override object? ExecuteScalar()
        {
            Record();
            return true;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new RecordingParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Record();
            return _owner.ResultFor(CommandText).CreateDataReader();
        }

        private void Record()
            => _owner.Executed.Add((
                CommandText,
                DbParameterCollection
                    .Cast<DbParameter>()
                    .Select(parameter => (parameter.ParameterName, parameter.Value, parameter.DbType))
                    .ToList()));
    }

    private sealed class RecordingParameter : DbParameter
    {
        public override DbType DbType { get; set; } = DbType.String;

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType() => DbType = DbType.String;
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;

        public override object SyncRoot { get; } = new();

        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }

        public override void Clear() => _parameters.Clear();

        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName)
            => _parameters.FindIndex(parameter =>
                string.Equals(parameter.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));

        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);

        public override void Remove(object value) => _parameters.Remove((DbParameter)value);

        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => _parameters[index];

        protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
            => _parameters[IndexOf(parameterName)] = value;
    }
}

internal sealed class StubConnectionFactory : IDbConnectionFactory
{
    private readonly DbConnection _connection;

    public StubConnectionFactory(DbConnection connection)
    {
        _connection = connection;
    }

    public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        => Task.FromResult(_connection);
}
