﻿using Microsoft.Extensions.Logging;
using Quartz;
using Regard.Backend.Common.Providers;
using Regard.Backend.Common.Utils;
using Regard.Backend.Configuration;
using Regard.Backend.DB;
using Regard.Backend.Downloader;
using Regard.Backend.Model;
using Regard.Backend.Services;
using Regard.Common.Utils;
using Regard.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Regard.Backend.Jobs
{
    [DisallowConcurrentExecution]
    public class SynchronizeJob : JobBase
    {
        private readonly IOptionManager optionManager;
        private readonly IProviderManager providerManager;
        private readonly IVideoStorageService videoStorageService;
        private readonly IVideoDownloaderService videoDownloader;
        private IJobExecutionContext context;
        private RegardScheduler scheduler;

        /// <summary>
        /// If set, synchronization will only be done for the subscriptions in the given folder (and all its subfolders).
        /// </summary>
        public int? FolderId { get; set; }

        /// <summary>
        /// If set, synchronization will only be done for the given subscription ID
        /// </summary>
        public int? SubscriptionId { get; set; }

        // Don't retry, since synchronization job runs pretty frequently anyway
        protected override int RetryCount => 0;

        protected override TimeSpan RetryInterval => TimeSpan.Zero;

        public SynchronizeJob(ILogger<SynchronizeJob> log,
                              DataContext dataContext,
                              IOptionManager optionManager,
                              IProviderManager providerManager,
                              IVideoStorageService videoStorageService,
                              IVideoDownloaderService videoDownloader) : base(log, dataContext)
        {
            this.optionManager = optionManager;
            this.providerManager = providerManager;
            this.videoStorageService = videoStorageService;
            this.videoDownloader = videoDownloader;
        }

        protected override async Task ExecuteJob(IJobExecutionContext context)
        {
            this.context = context;
            this.scheduler = new RegardScheduler(log, context.Scheduler);
            
            if (context.MergedJobDataMap.ContainsKey("SubscriptionId"))
                this.SubscriptionId =  context.MergedJobDataMap.GetInt("SubscriptionId");
            
            if (context.MergedJobDataMap.ContainsKey("FolderId"))
                this.FolderId = context.MergedJobDataMap.GetInt("FolderId");

            if (SubscriptionId.HasValue)
            {
                var sub = dataContext.Subscriptions.Find(SubscriptionId.Value);
                if (sub != null)
                {
                    log.LogInformation($"Synchronization started for subscription {sub}.");
                    await Synchronize(sub);
                }
            }
            else if (FolderId.HasValue)
            {
                var folder = dataContext.SubscriptionFolders.Find(FolderId.Value);
                if (folder != null)
                {
                    log.LogInformation($"Synchronization started for folder {folder}.");
                    await dataContext.GetSubscriptionsRecursive(folder)
                          .ToList()
                          .ToAsyncEnumerable()
                          .ForEachAwaitAsync(Synchronize);
                }
            }
            else
            {
                log.LogInformation($"Synchronization started.");
                await dataContext.Subscriptions
                    .ToList()
                    .ToAsyncEnumerable()
                    .ForEachAwaitAsync(Synchronize);
            }

            log.LogInformation("Synchronization finished.");
        }

        private async Task Synchronize(Subscription sub)
        {
            try
            {
                if (sub.SubscriptionProviderId != null)
                {
                    await CheckForNewVideos(sub);
                }
                await CheckFiles(sub);
                await videoDownloader.ProcessDownloadRules(sub);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Synchronization failed for subscription {sub}");
            }
        }

        private async Task CheckForNewVideos(Subscription sub)
        {
            var subProvider = providerManager.Get<ISubscriptionProvider>(sub.SubscriptionProviderId);
            subProvider.VerifyNotNull($"Could not find subscription provider {sub.SubscriptionProviderId}");

            var videos = subProvider
                .FetchVideos(sub)
                .OrderBy(video => video.Published);

            await foreach (var video in videos)
            {
                Video existingVideo = FindMatchingVideo(sub, video);

                if (existingVideo != null)
                {
                    MergeVideoInfo(existingVideo, video);
                    continue;
                }

                FillVideoDetails(sub, video);

                // Find a video provider to give us more details
                if (video.VideoProviderId == null)
                {
                    var videoProvider = await providerManager.FindForVideo(video).FirstOrDefaultAsync();

                    try
                    {
                        videoProvider.VerifyNotNull($"Could not find a video provider for video {video}");
                        await videoProvider.UpdateMetadata(new[] { video }, true, true);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Could not retrieve any information about video {0}", video);
                        continue;
                    }
                }

                // Store video
                dataContext.Videos.Add(video);
                log.LogInformation("New video {0}", video);
                await dataContext.SaveChangesAsync();
            }
        }

        private void FillVideoDetails(Subscription sub, Video video)
        {
            // TODO: allow providers to set playlist indices

            var nextIndex = dataContext.Videos.AsQueryable()
                                .Select(x => (int?)x.PlaylistIndex)
                                .Max();

            video.Subscription = sub;
            video.PlaylistIndex = (nextIndex ?? -1) + 1;
            video.IsWatched = false;
            video.Discovered = DateTimeOffset.UtcNow;

            if (video.Name != null)
            {
                video.Name = video.Name.Trim();
                int? maxLen = video.GetPropertyMaxLength("Name");
                if (maxLen.HasValue)
                    video.Name = video.Name.Truncate(maxLen.Value);
            }
        }

        private void MergeVideoInfo(Video existingVideo, Video fetchedVideo)
        {
            // TODO: merge data, if any extra details
        }

        private Video FindMatchingVideo(Subscription sub, Video video)
        {
            Video existingVideo = null;

            // Find matching video
            if (video.SubscriptionProviderId != null)
            {
                existingVideo = dataContext.Videos.AsQueryable()
                    .Where(x => x.SubscriptionId == sub.Id)
                    .Where(x => x.SubscriptionProviderId == video.SubscriptionProviderId)
                    .FirstOrDefault();
            }
            else if (video.VideoId != null)
            {
                existingVideo = dataContext.Videos.AsQueryable()
                    .Where(x => x.SubscriptionId == sub.Id)
                    .Where(x => x.VideoId == video.VideoId)
                    .FirstOrDefault();
            }
            if (existingVideo == null)
            {
                // The URL should always be provided, but it may not be 100% accurate
                existingVideo = dataContext.Videos.AsQueryable()
                    .Where(x => x.SubscriptionId == sub.Id)
                    .Where(x => x.OriginalUrl.ToLower() == video.OriginalUrl.ToLower())
                    .FirstOrDefault();
            }

            return existingVideo;
        }

        private async Task CheckFiles(Subscription sub)
        {
            var downloadedVideos = dataContext.Videos.AsQueryable()
                .Where(x => x.SubscriptionId == sub.Id)
                .Where(x => x.DownloadedPath != null)
                .ToList();
                
            foreach (var video in downloadedVideos)
            {
                if (!await videoStorageService.VerifyIsDownloaded(video))
                    await OnVideoDeleted(sub, video);

                if (!video.DownloadedSize.HasValue)
                    await OnMissingSize(video);
            }

            // TODO: error handling, show user the errors
        }

        private async Task OnVideoDeleted(Subscription sub, Video video)
        {
            log.LogInformation("Video file for {0} was deleted. Will clean up.", video);
            await videoStorageService.Delete(video);
            video.DownloadedPath = null;
            video.DownloadedSize = null;

            if (optionManager.GetForSubscription(Options.Subscriptions_AutoDeleteWatched, sub.Id))
            {
                video.IsWatched = true;
                log.LogInformation("Deleted video {0} marked as watched.", video);
            }

            await dataContext.SaveChangesAsync();
        }

        private async Task OnMissingSize(Video video)
        {
            video.DownloadedSize = await videoStorageService.CalculateSize(video);
            await dataContext.SaveChangesAsync();
        }
    }
}
