namespace BookingPlatform.IntegrationTests.Infrastructure;

/// <summary>Per-test migrated database (cloned from the template) and a real Api host over it.</summary>
[Collection(InfrastructureCollection.Name)]
[Trait("Category", "Docker")]
public abstract class ApiTestBase(InfrastructureFixture fixture) : IAsyncLifetime
{
    protected InfrastructureFixture Fixture { get; } = fixture;

    protected ApiFactory Factory { get; private set; } = null!;

    protected TestData Data { get; private set; } = null!;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected virtual IReadOnlyDictionary<string, string?>? Settings => null;

    public virtual async ValueTask InitializeAsync()
    {
        var connectionString = await Fixture.CreateMigratedDatabaseAsync();
        Data = new TestData(connectionString);
        Factory = new ApiFactory(connectionString, Fixture.RedisConnectionString, Settings);
    }

    public virtual async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected ApiSession NewSession() => new(Factory.CreateApiClient());

    protected async Task<ApiSession> SignInAsync(TestUser user) => await NewSession().LoginOkAsync(user.Email, user.Password);
}
