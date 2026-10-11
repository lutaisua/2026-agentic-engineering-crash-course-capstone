using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
services.AddSingleton<IFileCommitter, FileCommitter>();
services.AddSingleton<IAtomicFileWriter, AtomicFileWriter>();
services.AddSingleton<IPublisherStoreFactory, PublisherStoreFactory>();
services.AddSingleton<IPrivateStorageGuard, WindowsPrivateStorageGuard>();
services.AddSingleton<ICredentialVault, WindowsGenericCredentialVault>();
services.AddSingleton<IApplicationCredentialResolver>(serviceProvider => new ApplicationCredentialResolver(
    ApplicationCredentialResolver.DefaultMappingPath, serviceProvider.GetRequiredService<IPrivateStorageGuard>(),
    serviceProvider.GetRequiredService<ICredentialVault>()));
services.AddSingleton(serviceProvider => new ConfigurationMigrator(serviceProvider.GetRequiredService<IPublisherStoreFactory>(),
    serviceProvider.GetRequiredService<IPrivateStorageGuard>(), new AtomicFileWriter(new NewFileCommitter())));
services.AddTransient<PublisherCommands>();
services.AddSingleton(TimeProvider.System);
services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) });
services.AddSingleton<BoundedJsonHttp>();
services.AddSingleton<IInventoryTokens, ClientCredentialTokens>();
services.AddSingleton<AuthorizedInventoryHttp>();
services.AddSingleton<StoreInventoryReader>();
services.AddSingleton<IInventoryAccountProof>(provider => new InventoryAccountProof(InventoryAccountProof.DefaultDirectory,
    provider.GetRequiredService<IPrivateStorageGuard>(), provider.GetRequiredService<TimeProvider>()));
services.AddSingleton<IProductInventoryProvider, StoreInventoryProvider>();
services.AddTransient<InventoryCommands>();
services.AddSingleton<ICredentialReferenceInspector, OfflineCredentialReferenceInspector>();
services.AddTransient<ConfigurationCommands>();
services.AddTransient(provider => new CliApplication(System.Console.Out, System.Console.Error,
    provider.GetRequiredService<PublisherCommands>(), provider.GetRequiredService<ConfigurationCommands>(),
    provider.GetRequiredService<InventoryCommands>()));
await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
using CancellationTokenSource cancellation = new();
System.Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
return await provider.GetRequiredService<CliApplication>().RunAsync(args, cancellation.Token);
