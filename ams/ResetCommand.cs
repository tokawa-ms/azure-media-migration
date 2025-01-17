﻿using AMSMigrate.Contracts;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Media;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AMSMigrate.Ams
{
    internal class ResetCommand : BaseMigrator
    {
        private readonly ILogger _logger;
        private readonly ResetOptions _options;
        private readonly IMigrationTracker<BlobContainerClient, AssetMigrationResult> _tracker;
        internal const string AssetTypeKey = "AssetType";
        internal const string MigrateResultKey = "MigrateResult";
        internal const string ManifestNameKey = "ManifestName";
        internal const string OutputPathKey = "OutputPath";

        public ResetCommand(GlobalOptions globalOptions,
            ResetOptions resetOptions,
            IAnsiConsole console,
            TokenCredential credential,
            IMigrationTracker<BlobContainerClient, AssetMigrationResult> tracker,
            ILogger<ResetCommand> logger)
         : base(globalOptions, console, credential)
        {
            _options = resetOptions;
            _logger = logger;
            _tracker = tracker;
        }

        public override async Task MigrateAsync(CancellationToken cancellationToken)
        {
            var account = await GetMediaAccountAsync(_options.AccountName, cancellationToken);
            _logger.LogInformation("Begin reset assets on account: {name}", account.Data.Name);

            AsyncPageable<MediaAssetResource> assets = account.GetMediaAssets()
                .GetAllAsync(cancellationToken: cancellationToken);
            List<MediaAssetResource>? assetList = await assets.ToListAsync(cancellationToken);
            int resetAssetCount = 0;
            foreach (var asset in assetList)
            {
                var (storage, _) = await _resourceProvider.GetStorageAccount(asset.Data.StorageAccountName, cancellationToken);
                var container = storage.GetContainer(asset);
                if (!await container.ExistsAsync(cancellationToken))
                {
                    _logger.LogWarning("Container {name} missing for asset {asset}", container.Name, asset.Data.Name);
                    return;
                }

                if (_options.Category.Equals("all", StringComparison.OrdinalIgnoreCase) || (_tracker.GetMigrationStatusAsync(container, cancellationToken).Result.Status == MigrationStatus.Failed))
                {
                    try
                    {
                        BlobContainerProperties properties = await container.GetPropertiesAsync(cancellationToken: cancellationToken);

                        if (properties?.Metadata != null && properties.Metadata.Count == 0)
                        {
                            _logger.LogInformation("Container '{container}' does not have metadata.", container.Name);
                        }
                        else
                        {   // Clear container metadata
                            properties?.Metadata?.Remove(MigrateResultKey);
                            properties?.Metadata?.Remove(AssetTypeKey);
                            properties?.Metadata?.Remove(OutputPathKey);
                            properties?.Metadata?.Remove(ManifestNameKey);
                            var deleteOperation = await container.SetMetadataAsync(properties?.Metadata, cancellationToken: cancellationToken);
                            if (deleteOperation.GetRawResponse().Status == 200)
                            {
                                _logger.LogInformation("Metadata in Container '{container}' is deleted successfully.", container.Name);
                                resetAssetCount++;
                            }
                            else
                            {
                                _logger.LogInformation("Metadata in Container '{container}' does not exist or was not deleted.", container.Name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("An unexpected error occurred: {message}", ex.Message);
                    }
                }
            }
            _logger.LogDebug("{resetAssetCount} out of {totalAssetCount} assets has been reset.", resetAssetCount, assetList.Count);
        }
    }
}
