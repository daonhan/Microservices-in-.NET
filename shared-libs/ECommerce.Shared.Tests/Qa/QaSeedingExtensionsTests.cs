using ECommerce.Shared.Qa;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ECommerce.Shared.Tests.Qa;

public class QaSeedingExtensionsTests
{
    [Fact]
    public void GivenDevelopmentEnvironment_WhenQaSeedUnset_ThenSeedingIsEnabled()
    {
        var configuration = BuildConfiguration();
        var environment = new TestHostEnvironment("Development");

        var enabled = QaSeedingExtensions.IsQaSeedingEnabled(environment, configuration);

        Assert.True(enabled);
    }

    [Fact]
    public void GivenProductionEnvironment_WhenQaSeedUnset_ThenSeedingIsDisabled()
    {
        var configuration = BuildConfiguration();
        var environment = new TestHostEnvironment("Production");

        var enabled = QaSeedingExtensions.IsQaSeedingEnabled(environment, configuration);

        Assert.False(enabled);
    }

    [Fact]
    public void GivenProductionEnvironment_WhenQaSeedTrue_ThenSeedingIsEnabled()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Qa:Seed"] = "true"
        });
        var environment = new TestHostEnvironment("Production");

        var enabled = QaSeedingExtensions.IsQaSeedingEnabled(environment, configuration);

        Assert.True(enabled);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? values = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? [])
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "ECommerce.Shared.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
