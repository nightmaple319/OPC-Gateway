using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace OPCGateway.Core.Tests;

/// <summary>整合測試用的最小 OPC UA 客戶端（無安全性、匿名）。</summary>
internal static class UaTestClient
{
    public const string ApplicationName = "OPC Gateway E2E Client";

    public static async Task<ISession> ConnectAsync(string endpointUrl)
    {
        var telemetry = DefaultTelemetry.Create(_ => { });
        var storeRoot = @"%CommonApplicationData%\OPC Foundation\CertificateStores";
        var config = new ApplicationConfiguration
        {
            ApplicationName = ApplicationName,
            ApplicationUri = "urn:localhost:OPCGatewayE2EClient",
            ProductUri = "urn:opcgateway:tests",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storeRoot + @"\MachineDefault",
                    SubjectName = "CN=" + ApplicationName,
                },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = storeRoot + @"\UA Certificate Authorities" },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = storeRoot + @"\UA Applications" },
                RejectedCertificateStore = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = storeRoot + @"\RejectedCertificates" },
                AutoAcceptUntrustedCertificates = true,
                MinimumCertificateKeySize = 2048,
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
        };
        await config.ValidateAsync(ApplicationType.Client, CancellationToken.None);
        config.CertificateValidator.CertificateValidation += (_, e) => e.Accept = true;

        var application = new ApplicationInstance(config, telemetry) { ApplicationType = ApplicationType.Client };
        await application.CheckApplicationInstanceCertificatesAsync(true, null, CancellationToken.None);

        var endpoint = await CoreClientUtils.SelectEndpointAsync(config, endpointUrl, false, telemetry, CancellationToken.None);
        var configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(config));

        var factory = new DefaultSessionFactory(telemetry);
        return await factory.CreateAsync(config, configured, false, ApplicationName, 60000, new UserIdentity(), null, CancellationToken.None);
    }

    public static async Task<DataValue[]> ReadAsync(ISession session, params NodeId[] nodeIds)
    {
        var nodesToRead = new ReadValueIdCollection(nodeIds.Select(id => new ReadValueId { NodeId = id, AttributeId = Attributes.Value }));
        var response = await session.ReadAsync(null, 0, TimestampsToReturn.Both, nodesToRead, CancellationToken.None);
        return response.Results.ToArray();
    }

    public static async Task<List<string>> BrowseNamesAsync(ISession session, NodeId nodeId)
    {
        var description = new BrowseDescription
        {
            NodeId = nodeId,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = 0,
            ResultMask = (uint)BrowseResultMask.All,
        };
        var response = await session.BrowseAsync(null, null, 0, new BrowseDescriptionCollection { description }, CancellationToken.None);
        return response.Results[0].References.Select(r => r.BrowseName.Name).ToList();
    }

    public static async Task CloseAsync(ISession session)
    {
        try { await session.CloseAsync(5000, true, CancellationToken.None); }
        catch { /* ignore */ }
        session.Dispose();
    }
}
