using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Publishers;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class ConfigurationTests
{
    [TestMethod]
    public void RoundTripPreservesProgramsScopesAndCredentialReferences()
    {
        PublisherConfiguration source = new() { Publishers = [ProfileFixtures.Create()] };

        PublisherConfiguration result = ConfigurationJson.Deserialize<PublisherConfiguration>(
            JsonSerializer.Serialize(source, ConfigurationJson.Options));
        ConfigurationValidator.Validate(result);

        result.Should().BeEquivalentTo(source);
        result.Publishers[0].Authorization.Keys.Should().HaveCount(3);
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("CON")]
    [DataRow("con")]
    [DataRow("lpt1")]
    [DataRow("Upper")]
    [DataRow("bad.id")]
    [DataRow("")]
    [DataRow("ends-")]
    [DataRow("valid\n")]
    public void UnsafeIdsAreRejected(string id)
    {
        Action validate = () => ConfigurationValidator.Validate(ProfileFixtures.Create(id));

        validate.Should().Throw<InputException>();
    }

    [TestMethod]
    public void DuplicateProfileIdsAreRejected()
    {
        PublisherConfiguration configuration = new() { Publishers = [ProfileFixtures.Create(), ProfileFixtures.Create()] };

        Action validate = () => ConfigurationValidator.Validate(configuration);

        validate.Should().Throw<InputException>().WithMessage("*unique*");
    }

    [TestMethod]
    public void MissingAndUnknownFieldsAreRejected()
    {
        PublisherProfile valid = ProfileFixtures.Create();
        PublisherProfile[] invalid =
        [
            valid with { DisplayName = "" }, valid with { TenantId = "" }, valid with { Programs = ["unknown"] },
            valid with { Accounts = [] }, valid with { Authorization = [] },
            valid with { Authorization = new() { ["unknown"] = new() } }
        ];

        foreach (PublisherProfile profile in invalid)
        {
            Action validate = () => ConfigurationValidator.Validate(profile);
            validate.Should().Throw<InputException>();
        }
    }

    [TestMethod]
    [DataRow("{\"publishers\":[]}")]
    [DataRow("{\"schemaVersion\":1}")]
    [DataRow("[]")]
    [DataRow("{\"schemaVersion\":1,\"schemaVersion\":1,\"publishers\":[]}")]
    [DataRow("{\"schemaVersion\":1,\"settings\":{\"client_secret\":\"DO-NOT-ECHO\"}}")]
    [DataRow("{\"schemaVersion\":1,\"publishers\":[],\"token\":\"DO-NOT-ECHO\"}")]
    public void UnsafeJsonFailsWithoutEchoingValues(string json)
    {
        Action read = () => ConfigurationJson.Deserialize<PublisherConfiguration>(json);

        read.Should().Throw<InputException>().Which.Message.Should().NotContain("DO-NOT-ECHO");
    }

    [TestMethod]
    public void UnsupportedSchemaIsRejected()
    {
        Action validate = () => ConfigurationValidator.Validate(new PublisherConfiguration { SchemaVersion = 3 });

        validate.Should().Throw<InputException>().WithMessage("*schemaVersion 1*");
    }
}
