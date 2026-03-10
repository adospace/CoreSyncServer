using CoreSync;

namespace CoreSyncServer.Data;

public class DataStoreConfiguration
{
    public int Id { get; set; }

    public int DataStoreId { get; set; }

    public int VersioId { get; set; }

    public DataStore? DataStore { get; set; }

    public IList<DataStoreTableConfiguration> TableConfigurations { get; set; } = [];

    public IList<Endpoint> Endpoints { get; set; } = [];
}

public class DataStoreTableConfiguration
{
    public int Id { get; set; }

    public int DataStoreConfigurationId { get; set; }

    public DataStoreConfiguration? DataStoreConfiguration { get; set; }

    public required string Name { get; set; }

    public string? Schema { get; set; }

    public SyncDirection SyncDirection { get; set; }
}

public class Endpoint
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public int DataStoreConfigurationId { get; set; }

    public DataStoreConfiguration? DataStoreConfiguration { get; set; }

    public string? Tags { get; set; }

    public int? AuthenticationId { get; set; }

    public EndPointAuthentication? Authentication { get; set; }
}

public enum EndPointAuthenticationType
{
    Basic,
    ApiKey,
    Jwt
}

public abstract class EndPointAuthentication
{
    public int Id { get; set; }

    public EndPointAuthenticationType Type { get; set; }
}

public class BasicAuthentication : EndPointAuthentication
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

public class ApiKeyAuthentication : EndPointAuthentication
{
    public required string ApiKey { get; set; }
}

public class JwtAuthentication : EndPointAuthentication
{
    public required string JWKSEndpoint { get; set; }

    public required string Issuer { get; set; }

    public string UserIdClaim { get; set; } = "sub";

    public string? UserNameClaim { get; set; }
}

