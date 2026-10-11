using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed record DiagnosticMetadata(string State, bool? IdentityMatches = null, bool? ParentMatches = null,
    DiagnosticField? WireProductType = null, bool? ParserCompatible = null, string? Publication = null,
    string? ObservedStoreId = null, string? ObservedParentId = null, int? TotalCount = null);
internal sealed record DiagnosticProbe(string Operation, string Query, string Outcome, int? Status, DiagnosticMetadata Metadata);
internal sealed record StoreDiagnosticPacket(int SchemaVersion, string RunId, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    string SourceSha256, string BindingFingerprint, StoreDiagnosticInput Input, IReadOnlyList<DiagnosticAttempt> Attempts,
    IReadOnlyList<DiagnosticProbe> Probes, IReadOnlyList<string> Interpretations, int ExitCode)
{
    public override string ToString() => "[protected Store diagnostic packet]";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal object PublicProjection() => new
    {
        diagnosticCompletion = ExitCode == 0 ? "recorded" : ExitCode == 130 ? "cancelled" : "partial",
        exitCode = ExitCode,
        observedProbes = Probes.Count(probe => probe.Status is not null),
        plannedProbes = 6,
        observations = Attempts.Select(attempt => attempt.PublicProjection()),
        interpretations = Interpretations,
        inventoryAcceptance = "unverified"
    };
}

internal sealed class StoreDiagnosticPacketStore(string root, IPrivateStorageGuard privacy, IAtomicFileWriter writer)
{
    internal async Task<string> SaveAsync(StoreDiagnosticPacket packet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (packet.SchemaVersion != 1 || !Guid.TryParseExact(packet.RunId, "D", out _)) { throw new InputException("Invalid diagnostic evidence version/run."); }
        if (packet.ExitCode == 0 && (packet.Probes.Count != 6 || packet.Probes.Any(probe => probe.Status is null)))
        { throw new InputException("A completed diagnostic requires all six HTTP observations."); }
        string privateRoot = privacy.PrepareDirectory(root);
        string runDirectory = Path.Combine(privateRoot, packet.RunId);
        if (Directory.Exists(runDirectory) || File.Exists(runDirectory)) { throw new InputException("Diagnostic run destination already exists."); }
        privacy.PrepareDirectory(runDirectory);
        byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(packet, StoreDiagnosticPacket.JsonOptions);
        byte[] attachment = Encoding.UTF8.GetBytes(StoreDiagnosticSupport.PrivateView(packet));
        string evidencePath = Path.Combine(runDirectory, "evidence.json"), attachmentPath = Path.Combine(runDirectory, "support-attachment.md");
        await writer.WriteAsync(evidencePath, evidence, cancellationToken).ConfigureAwait(false);
        privacy.VerifyFile(evidencePath);
        await writer.WriteAsync(attachmentPath, attachment, cancellationToken).ConfigureAwait(false);
        privacy.VerifyFile(attachmentPath);
        if (Hash(await File.ReadAllBytesAsync(evidencePath, cancellationToken).ConfigureAwait(false)) != Hash(evidence) ||
            Hash(await File.ReadAllBytesAsync(attachmentPath, cancellationToken).ConfigureAwait(false)) != Hash(attachment))
        { throw new InputException("Diagnostic evidence verification failed."); }
        // The receipt is last: a directory or one completed file alone is not a packet.
        byte[] receipt = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            packet.RunId,
            completion = packet.ExitCode == 0 ? "recorded" : "partial",
            packet.ExitCode,
            evidenceSha256 = Hash(evidence),
            attachmentSha256 = Hash(attachment)
        }, StoreDiagnosticPacket.JsonOptions);
        string receiptPath = Path.Combine(runDirectory, "receipt.json");
        await writer.WriteAsync(receiptPath, receipt, cancellationToken).ConfigureAwait(false);
        privacy.VerifyFile(receiptPath);
        if (Hash(await File.ReadAllBytesAsync(receiptPath, cancellationToken).ConfigureAwait(false)) != Hash(receipt))
        { throw new InputException("Diagnostic receipt verification failed."); }
        return runDirectory;
    }
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

internal static class StoreDiagnosticSupport
{
    internal const string Precedent = "https://learn.microsoft.com/en-us/answers/questions/806501/not-supported-managedconsumable-product-type-when";
    internal static string SharedView(StoreDiagnosticPacket packet)
    {
        StringBuilder text = new();
        text.AppendLine("# Store Subscription diagnostic comparison");
        text.AppendLine("Observed UTC: " + packet.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        text.AppendLine("Diagnostic exit: " + packet.ExitCode.ToString(CultureInfo.InvariantCulture) + "; inventory acceptance remains unverified.");
        foreach (DiagnosticProbe probe in packet.Probes)
        {
            text.AppendLine($"- {probe.Operation} ({probe.Query}): HTTP {probe.Status?.ToString(CultureInfo.InvariantCulture) ?? "not observed"}; {probe.Outcome}; metadata {probe.Metadata.State}.");
            if (probe.Operation == "addon-detail")
            {
                text.AppendLine($"  Operator declaration: Subscription; observed wire type: {probe.Metadata.WireProductType?.Value ?? probe.Metadata.WireProductType?.State ?? "not read"}; " +
                    $"identity match: {probe.Metadata.IdentityMatches?.ToString() ?? "unknown"}; parent match: {probe.Metadata.ParentMatches?.ToString() ?? "unknown"}; " +
                    $"production parser compatible: {probe.Metadata.ParserCompatible?.ToString() ?? "unknown"}; publication: {probe.Metadata.Publication ?? "unknown"}.");
            }
        }
        foreach (DiagnosticAttempt attempt in packet.Attempts.Where(attempt => attempt.Operation != "token"))
        {
            foreach (DiagnosticErrorRecord error in attempt.Error?.Records ?? [])
            {
                text.AppendLine($"  Error at {error.Path}: code {error.Code.Value ?? error.Code.State}; target {error.Target.Value ?? error.Target.State}; type {error.ProductType.Value ?? error.ProductType.State}; source {error.TypeSource ?? "absent"}.");
            }
            text.AppendLine("  Evidence limitations: " + string.Join(", ", attempt.Error?.Limitations ?? []));
            text.AppendLine($"  Correlations: {attempt.Correlations.Count(field => field.State == "present")} accepted; " +
                string.Join(", ", attempt.Correlations.Where(field => field.State != "present").Select(field => field.Field + "=" + field.State)));
        }
        text.AppendLine("Sample-limited interpretations: " + string.Join(", ", packet.Interpretations));
        text.AppendLine("Account/product identifiers and returned correlation values are retained only in the protected attachment.");
        text.AppendLine("April 2022 precedent: " + Precedent);
        text.AppendLine("That report concerns ManagedConsumable, does not state HTTP status, and reports recovery after removal without an authoritative service explanation. It is not a confirmed Subscription fix.");
        text.AppendLine("Please confirm the supported complete publisher Subscription inventory route and investigate the observed collection/detail difference or rejection.");
        return text.ToString();
    }
    internal static string PrivateView(StoreDiagnosticPacket packet)
    {
        StringBuilder text = new(SharedView(packet));
        text.AppendLine("\nPrivate operator attachment; review before sending to Microsoft.");
        text.AppendLine("Publisher alias: " + packet.Input.PublisherId);
        text.AppendLine("Parent Store ID: " + packet.Input.ParentApplicationStoreId);
        text.AppendLine("Subscription Store ID: " + packet.Input.SubscriptionStoreId);
        text.AppendLine("Binding fingerprint: " + packet.BindingFingerprint);
        text.AppendLine("Local run ID (not a server correlation): " + packet.RunId);
        text.AppendLine("Test assembly SHA-256: " + packet.SourceSha256);
        foreach (DiagnosticAttempt attempt in packet.Attempts)
        {
            foreach (DiagnosticCorrelation field in attempt.Correlations)
            {
                text.AppendLine($"{attempt.Operation}/{attempt.Query} attempt {attempt.Attempt} at {attempt.StartedAt:O}: {field.Field} = {field.Value ?? field.State}");
            }
        }
        return text.ToString();
    }
}
